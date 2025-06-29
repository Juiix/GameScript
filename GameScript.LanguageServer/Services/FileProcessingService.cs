using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Parsing;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace GameScript.LanguageServer.Services
{
	/// <summary>
	/// Orchestrates the end-to-end pipeline for a single source file:
	/// parse → index → analyze → publish diagnostics → cache results.
	/// <para>
	/// Requests are debounced (150 ms) and processed on a limited pool of
	/// worker tasks.
	/// </para>
	/// </summary>
	internal sealed class FileProcessingService
	{
		/// <summary>
		/// Indicates how much work needs to be done for a queued file.
		/// </summary>
		private enum ProcessType
		{
			/// <summary>Run only analysis passes.</summary>
			Analysis,

			/// <summary>Run parse, index, and analysis passes.</summary>
			Full
		}

		private readonly AstCache _astCache;
		private readonly OpenDocumentCache _openDocumentCache;
		private readonly TextCache _textCache;
		private readonly ParsingService _parsingService;
		private readonly IndexingService _indexingService;
		private readonly AnalysisService _analysisService;
		private readonly DiagnosticsService _diagnosticsService;

		private readonly ConcurrentDictionary<string, ProcessType> _jobInfo = [];
		private readonly ConcurrentDictionary<string, object?> _delays = [];

		private readonly Channel<string> _processChannel = Channel.CreateUnbounded<string>();
		private readonly SemaphoreSlim _processingSemaphore = new(8); // max concurrent file jobs

		/// <summary>
		/// Creates a new <see cref="FileProcessingService"/>.
		/// </summary>
		public FileProcessingService(
			AstCache astCache,
			OpenDocumentCache openDocumentCache,
			TextCache textCache,
			ParsingService parsingService,
			IndexingService indexingService,
			AnalysisService analysisService,
			DiagnosticsService diagnosticsService)
		{
			_astCache = astCache;
			_openDocumentCache = openDocumentCache;
			_textCache = textCache;
			_parsingService = parsingService;
			_indexingService = indexingService;
			_analysisService = analysisService;
			_diagnosticsService = diagnosticsService;
		}

		/// <summary>
		/// Starts the background loop that drains the processing channel.
		/// Call this once during server initialization.
		/// </summary>
		public void Start() => _ = ProcessRequestsAsync();

		/// <summary>
		/// Queues a file for a full re-process (parse + index + analysis).
		/// </summary>
		public void Queue(string filePath) => QueueInner(filePath, ProcessType.Full);

		/// <summary>
		/// Queues reanalysis for a file
		/// </summary>
		public void QueueAnalysis(string filePath) => QueueInner(filePath, ProcessType.Analysis);

		/* ---------- Pipeline helpers ---------- */

		private void QueueInner(string filePath, ProcessType processType)
		{
			// Record (or escalate) the work we need to do for this file.
			_jobInfo.AddOrUpdate(filePath, processType, (path, current) =>
				current > processType ? current : processType);
			_processChannel.Writer.TryWrite(filePath);
			/*
			// Debounce: if a delay is already scheduled, we’re done.
			if (!_delays.TryAdd(filePath, null))
				return;

			_ = DelayAsync(filePath);
			*/
		}

		private async Task DelayAsync(string filePath)
		{
			//await Task.Delay(250).ConfigureAwait(false);
			_delays.TryRemove(filePath, out _);
			await _processChannel.Writer.WriteAsync(filePath).ConfigureAwait(false);
		}

		private async Task ProcessRequestsAsync()
		{
			await foreach (var filePath in _processChannel.Reader.ReadAllAsync())
			{
				await _processingSemaphore.WaitAsync();
				_ = Task.Run(() => ProcessRequest(filePath));
			}
		}

		private void ProcessRequest(string filePath)
		{
			try
			{
				if (!_jobInfo.TryRemove(filePath, out var processType))
					return; // nothing to do (race condition)

				_astCache.TryGetRoot(filePath, out var previousRootData);

				// Run the required pipeline stages.
				var rootData = processType switch
				{
					ProcessType.Analysis => Analyze(previousRootData),
					ProcessType.Full => Full(filePath),
					_ => null
				};
				if (rootData == null) return;

				_diagnosticsService.Publish(filePath, rootData.Errors);
				_astCache.Update(filePath, rootData);

				// Re-analyze dependents if the structure changed.
				if (processType == ProcessType.Full && previousRootData != null)
				{
					foreach (var dep in _indexingService.GetDependencies(previousRootData, [filePath]))
						QueueInner(dep, ProcessType.Analysis);
				}
			}
			finally
			{
				_processingSemaphore.Release();
			}
		}

		private RootFileData? Analyze(RootFileData? previousRootData)
		{
			if (previousRootData == null) return null;

			var result = _analysisService.Analyze(
				previousRootData.Parse.Root,
				previousRootData.Index.LocalIndexes);

			return result == null ? null : previousRootData with { Analysis = result };
		}

		private RootFileData? Full(string filePath)
		{
			if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) &&
				!_textCache.TryGetText(filePath, out text))
				return null;

			var parse = _parsingService.Parse(filePath, text, fileVersion);
			if (parse == null) return null;

			var index = _indexingService.Index(parse.Root);
			if (index == null) return null;

			var analysis = _analysisService.Analyze(parse.Root, index.LocalIndexes);
			if (analysis == null) return null;

			return new RootFileData(parse, index, analysis);
		}
	}
}
