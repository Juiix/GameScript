using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Parsing;
using GameScript.LanguageServer.Tools;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace GameScript.LanguageServer.Services;

/// <summary>
/// Maintains the state of the user’s workspace:  
/// • Performs an initial crawl of all GameScript files under the workspace root  
/// • Caches their parse / index / analysis results  
/// • Publishes initial diagnostics  
/// • Watches the file-system for changes and schedules re-processing jobs
/// </summary>
internal sealed class WorkspaceService : IAsyncDisposable
{
	private readonly AstCache _astCache;
	private readonly TextCache _textCache;
	private readonly FileProcessingService _fileProcessingService;
	private readonly ParsingService _parsingService;
	private readonly IndexingService _indexingService;
	private readonly AnalysisService _analysisService;
	private readonly DiagnosticsService _diagnosticsService;
	private readonly ILogger<WorkspaceService> _logger;

	private CancellationTokenSource? _watchCts;
	private FileSystemWatcher? _watcher;
	private string? _rootPath = null;

	public WorkspaceService(
		AstCache astCache,
		TextCache textCache,
		FileProcessingService fileProcessingService,
		ParsingService parsingService,
		IndexingService indexingService,
		AnalysisService analysisService,
		DiagnosticsService diagnosticsService,
		ILogger<WorkspaceService> logger)
	{
		_astCache = astCache;
		_textCache = textCache;
		_fileProcessingService = fileProcessingService;
		_parsingService = parsingService;
		_indexingService = indexingService;
		_analysisService = analysisService;
		_diagnosticsService = diagnosticsService;
		_logger = logger;
	}

	/// <summary>
	/// Sets (or updates) the workspace root and restarts the file-system watcher.
	/// </summary>
	/// <param name="rootUri">The root folder URI provided by the client.</param>
	public void SetRoot(string rootUri)
	{
		_rootPath = CanonicaliseRoot(new Uri(rootUri).LocalPath);
		RestartWatcher();
		_logger.LogInformation("Starting with workspace: {path}", _rootPath);
	}

	/// <summary>
	/// Scans every GameScript file in the workspace, building the caches in parallel.
	/// Call once during server initialization.
	/// </summary>
	public async Task InitialScanAsync(CancellationToken ct = default)
	{
		if (_rootPath is null) return;
		await ScanFolderAsync(_rootPath, false, ct);
	}

	/// <summary>
	/// Publishes diagnostics for all cached files after the initial scan completes.
	/// </summary>
	public async Task SendInitialDiagnosticsAsync(CancellationToken ct = default)
	{
		await Parallel.ForEachAsync(_astCache.Datas, ct, (rootData, token) =>
		{
			_diagnosticsService.Publish(rootData.Root.FilePath, rootData.Errors);
			return ValueTask.CompletedTask;
		});
	}

	/* ---------- File-system watcher ---------- */

	private void RestartWatcher()
	{
		_watchCts?.Cancel();
		_watcher?.Dispose();

		if (_rootPath is null) return;

		_watchCts = new CancellationTokenSource();
		_watcher = new FileSystemWatcher(_rootPath, "*.*")
		{
			IncludeSubdirectories = true,
			EnableRaisingEvents = true,
			NotifyFilter = NotifyFilters.FileName |
				NotifyFilters.DirectoryName |
				NotifyFilters.LastWrite
		};

		_watcher.Created += OnCreatedOrChanged;   // file  or  folder
		_watcher.Changed += OnCreatedOrChanged;   // file change
		_watcher.Deleted += OnDeleted;            // file  or  folder
		_watcher.Renamed += OnRenamed;            // file  or  folder
	}

	/* ───── event handlers ──────────────────────────────────────────*/

	private void OnCreatedOrChanged(object? s, FileSystemEventArgs e)
	{
		if (IsDirectory(e.FullPath))
		{
			// new folder or a write-time change to folder attributes
			_ = ScanFolderAsync(e.FullPath, true, default);
		}
		else
		{
			OnDiskChanged(e.FullPath);
		}
	}

	private void OnDeleted(object? s, FileSystemEventArgs e)
	{
		IEnumerable<string>? toReanalyze;
		if (IsDirectory(e.FullPath.NormalizePath()))
		{
			// whole folder removed → evict everything under it
			var path = CanonicaliseRoot(e.FullPath);
			var files = _astCache.FilePaths.Where(p => IsContainedInDirectory(p, path));
			toReanalyze = OnRemovedFolder(files.ToArray());
		}
		else
		{
			toReanalyze = OnRemovedFile(e.FullPath);
		}

		// reanalyze dependent files
		foreach (var filePath in toReanalyze)
		{
			_fileProcessingService.QueueAnalysis(filePath);
		}
	}

	private void OnRenamed(object? s, RenamedEventArgs e)
	{
		FileSystemEventArgs args;

		// check if file is still in root
		if (_rootPath == null ||
			!IsContainedInDirectory(e.FullPath.NormalizePath(), _rootPath))
		{
			// then treat the new path like a delete
			args = new FileSystemEventArgs(
				WatcherChangeTypes.Deleted,
				Path.GetDirectoryName(e.OldFullPath)!,
				Path.GetFileName(e.OldFullPath)
			);
			OnDeleted(s, args);
			return;
		}

		// handle old path first
		// we can ignore reanalyze lists, create will generate its own
		if (IsDirectory(e.OldFullPath.NormalizePath()))
		{
			var oldPath = CanonicaliseRoot(e.OldFullPath);
			var files = _astCache.FilePaths.Where(p => IsContainedInDirectory(p, oldPath));
			OnRemovedFolder(files.ToArray());
		}
		else
		{
			OnRemovedFile(e.OldFullPath);
		}

		// then treat the new path like a create
		args = new FileSystemEventArgs(
			WatcherChangeTypes.Created,
			Path.GetDirectoryName(e.FullPath)!,
			Path.GetFileName(e.FullPath)
		);
		OnCreatedOrChanged(s, args);
	}

	/// <summary>
	/// Handles file deletions/renames by evicting caches and clearing diagnostics.
	/// </summary>
	private HashSet<string> OnRemovedFolder(IEnumerable<string> files)
	{
		// clear
		foreach (var filePath in files)
		{
			_textCache.Remove(filePath);
			_diagnosticsService.Clear(filePath);
		}

		// reanalze dependents
		List<RootFileData> roots = [];
		foreach (var filePath in files)
		{
			if (_astCache.TryRemoveFile(filePath, out var root))
			{
				roots.Add(root);
			}
		}

		HashSet<string> visited = [..files];
		HashSet<string> toReanalze = [];
		foreach (var root in roots)
		{
			foreach (var dependent in _indexingService.GetDependencies(root, visited))
			{
				toReanalze.Add(dependent);
			}
		}

		// remove indexes
		foreach (var filePath in files)
		{
			_indexingService.RemoveFile(filePath);
		}

		return toReanalze;
	}

	/// <summary>
	/// Handles file deletions/renames by evicting caches and clearing diagnostics.
	/// </summary>
	private IEnumerable<string> OnRemovedFile(string filePath)
	{
		if (!ExtensionFilter.IsGameScript(filePath)) return [];

		// clear
		_textCache.Remove(filePath);
		_diagnosticsService.Clear(filePath);

		// reanalze dependents
		List<string> toReanalze = [];
		if (_astCache.TryRemoveFile(filePath, out var rootData))
		{
			foreach (var dependent in _indexingService.GetDependencies(rootData, [filePath]))
			{
				(toReanalze ??= []).Add(dependent);
			}
		}

		// remove indexes
		_indexingService.RemoveFile(filePath);

		return toReanalze;
	}

	/// <summary>
	/// Handles file edits/creations by flushing the text cache and forwarding the
	/// path to the <see cref="FileProcessingService"/> pipeline.
	/// </summary>
	private void OnDiskChanged(string filePath)
	{
		if (!ExtensionFilter.IsGameScript(filePath)) return;

		filePath = filePath.NormalizePath();
		_textCache.Clear(filePath);
		_fileProcessingService.Queue(filePath);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		if (_watchCts != null)
			await _watchCts.CancelAsync();

		_watcher?.Dispose();
	}
	
	/* ───── helpers ────────────────────────────────────────────────*/

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsDirectory(string path)
	{
		if (string.IsNullOrEmpty(path))
			return false;

		return !Path.HasExtension(path);
	}

	/// <summary>
	/// Determines whether <paramref name="itemPath"/> is located inside
	/// <paramref name="directoryPath"/> (or is the directory itself).
	/// </summary>
	/// <remarks>
	/// Both paths are resolved with <see cref="Path.GetFullPath"/> to remove
	/// relative segments and normalize separators before comparison.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsContainedInDirectory(string itemPath, string directoryPath)
	{
		if (string.IsNullOrEmpty(itemPath) || string.IsNullOrEmpty(directoryPath))
			return false;

		// Choose correct case-sensitivity for this OS
		var cmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
				  ? StringComparison.OrdinalIgnoreCase
				  : StringComparison.Ordinal;

		return itemPath.StartsWith(directoryPath, cmp);
	}

	/// <summary>
	/// Normalises <paramref name="path"/> once and ensures it ends with the
	/// platform directory separator, ready to be passed into <see cref="IsContainedIn"/>.
	/// Call this once when you set / refresh <c>_rootPath</c>.
	/// </summary>
	public static string CanonicaliseRoot(string path)
	{
		string full = path.NormalizePath()
						  .TrimEnd(Path.DirectorySeparatorChar,
								   Path.AltDirectorySeparatorChar);

		return full + Path.DirectorySeparatorChar;
	}

	private async Task ScanFolderAsync(string folderPath, bool reanalyzeDependents, CancellationToken ct)
	{
		var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
					.Where(ExtensionFilter.IsGameScript)
					.Select(x => x.NormalizePath());

		var results = new ConcurrentBag<(ParseResult Parse, IndexResult Index)>();
		await Parallel.ForEachAsync(files, ct, (filePath, token) =>
		{
			var parse = _parsingService.Parse(filePath, []);
			if (parse is null) return ValueTask.CompletedTask;

			var index = _indexingService.Index(parse.Root);
			if (index is null) return ValueTask.CompletedTask;

			results.Add((parse, index));
			return ValueTask.CompletedTask;
		});

		var roots = new ConcurrentBag<RootFileData>();
		await Parallel.ForEachAsync(results, ct, (pair, token) =>
		{
			var (parse, index) = pair;
			var analysis = _analysisService.Analyze(parse.Root, index.LocalIndexes);
			if (analysis is null) return ValueTask.CompletedTask;

			var filePath = parse.Root.FilePath;
			var rootData = new RootFileData(parse, index, analysis);
			roots.Add(rootData);
			_astCache.Update(filePath, rootData);
			return ValueTask.CompletedTask;
		});

		if (!reanalyzeDependents)
		{
			return;
		}

		HashSet<string> visited = [.. roots.Select(x => x.Root.FilePath)];
		HashSet<string> toReanalyze = [];
		foreach (var root in roots)
		{
			foreach (var file in _indexingService.GetDependencies(root, visited))
			{
				toReanalyze.Add(file);
			}
		}

		foreach (var file in toReanalyze)
		{
			_fileProcessingService.QueueAnalysis(file);
		}
	}
}
