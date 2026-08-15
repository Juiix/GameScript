using GameScript.Language.Index;
using GameScript.LanguageServer.Extensions;
using Microsoft.Extensions.Logging;

namespace GameScript.LanguageServer.Services;

/// <summary>
/// Maps workspace files to sub-projects. A project root is any folder containing
/// a <c>gamescript.json</c> marker file; files resolve to the nearest ancestor
/// project root, and files outside every marker fall into the default project
/// (the workspace root). Each project owns its own symbol/reference tables, so
/// independent script projects (e.g. content/server and content/client, each
/// with its own core.gs) don't collide in one namespace.
/// </summary>
internal sealed class Project
{
	public required string RootPath { get; init; }    // canonical, trailing separator ("" = default project)
	public GlobalSymbolTable Symbols { get; } = new();
	public GlobalReferenceTable References { get; } = new();
}

internal sealed class ProjectRegistry(ILogger<ProjectRegistry> logger)
{
	public const string MarkerFileName = "gamescript.json";

	private readonly ILogger<ProjectRegistry> _logger = logger;
	private readonly object _lock = new();
	private string? _rootPath;

	// Sorted longest-root-first so the nearest ancestor wins; the default
	// project (workspace root) is always last. Replaced wholesale on rebuild —
	// readers take a stable snapshot.
	private volatile IReadOnlyList<Project> _projects = [new Project { RootPath = string.Empty }];

	/// <summary>All projects, default project last.</summary>
	public IReadOnlyList<Project> Projects => _projects;

	/// <summary>
	/// Resets the registry to the given workspace root (canonicalised with a
	/// trailing separator) and discovers its project markers.
	/// </summary>
	public void SetRoot(string canonicalRootPath)
	{
		lock (_lock)
		{
			_rootPath = canonicalRootPath;
			_projects = [new Project { RootPath = canonicalRootPath }];
		}
		Rebuild();
	}

	/// <summary>
	/// Rescans the workspace for <c>gamescript.json</c> markers. Returns true when
	/// the set of project roots changed (all tables are then fresh and every file
	/// must be re-indexed by the caller); false when nothing changed (no-op).
	/// </summary>
	public bool Rebuild()
	{
		lock (_lock)
		{
			if (_rootPath is null)
				return false;

			List<string> roots = [];
			try
			{
				foreach (var marker in Directory.EnumerateFiles(_rootPath, MarkerFileName, SearchOption.AllDirectories))
				{
					roots.Add(CanonicalDir(Path.GetDirectoryName(marker)!));
				}
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Failed to scan for {marker} project markers", MarkerFileName);
				return false;
			}

			roots.Sort(PathComparer);
			var current = _projects;
			if (current.Count == roots.Count + 1 &&
				roots.Select((r, i) => string.Equals(r, current[i].RootPath, PathComparison)).All(x => x))
			{
				return false;    // same roots — keep the existing tables
			}

			List<Project> projects = [];
			foreach (var root in roots)
			{
				projects.Add(new Project { RootPath = root });
			}
			projects.Add(new Project { RootPath = _rootPath });    // default project last

			_projects = projects;
			_logger.LogInformation("Workspace projects: {roots}",
				string.Join(", ", projects.Select(p => p.RootPath)));
			return true;
		}
	}

	/// <summary>Resolves the project owning <paramref name="filePath"/> — the
	/// nearest ancestor project root, or the default project.</summary>
	public Project GetProject(string filePath)
	{
		var projects = _projects;
		for (int i = 0; i < projects.Count - 1; i++)
		{
			if (filePath.StartsWith(projects[i].RootPath, PathComparison))
				return projects[i];
		}
		return projects[^1];    // default project
	}

	public static bool IsMarkerFile(string path) =>
		string.Equals(Path.GetFileName(path), MarkerFileName, PathComparison);

	private static string CanonicalDir(string path)
	{
		string full = path.NormalizePath()
						  .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return full + Path.DirectorySeparatorChar;
	}

	private static StringComparison PathComparison =>
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

	// longest root first so nested projects shadow their parents
	private static readonly Comparison<string> PathComparer = (a, b) =>
		a.Length != b.Length ? b.Length.CompareTo(a.Length)
							 : string.Compare(a, b, PathComparison);
}
