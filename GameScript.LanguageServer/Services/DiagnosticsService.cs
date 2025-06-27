using GameScript.Language.File;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace GameScript.LanguageServer.Services
{
	/// <summary>
	/// Central helper for sending <see cref="Diagnostic"/> notifications
	/// to the client and tracking which files currently have diagnostics.
	/// </summary>
	internal sealed class DiagnosticsService
	{
		private readonly ILanguageServerFacade _server;
		private readonly HashSet<string> _files = [];
		private readonly object _lock = new();

		/// <summary>
		/// Creates a new <see cref="DiagnosticsService"/>.
		/// </summary>
		/// <param name="server">
		/// The language-server facade used to publish <c>textDocument/publishDiagnostics</c> notifications.
		/// </param>
		public DiagnosticsService(ILanguageServerFacade server)
		{
			_server = server;
		}

		/// <summary>
		/// Removes all diagnostics for a file and notifies the client.
		/// </summary>
		/// <param name="filePath">Absolute path of the file that was fixed or closed.</param>
		public void Clear(string filePath)
		{
			lock (_lock)
			{
				// If we weren't tracking diagnostics for this file, nothing to do.
				if (!_files.Remove(filePath))
					return;
			}

			_server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
			{
				Uri = DocumentUri.FromFileSystemPath(filePath),
				Diagnostics = new Container<Diagnostic>() // empty = clear
			});
		}

		/// <summary>
		/// Publishes the given set of diagnostics for a file.
		/// If the list is empty, any existing diagnostics are cleared.
		/// </summary>
		/// <param name="filePath">Absolute path of the file being analyzed.</param>
		/// <param name="fileErrors">Errors produced by the analyzer.</param>
		public void Publish(string filePath, IReadOnlyList<FileError> fileErrors)
		{
			lock (_lock)
			{
				// Track the file only if it has diagnostics.
				if (fileErrors.Count == 0 && !_files.Remove(filePath))
					return;

				if (fileErrors.Count != 0)
				{
					_files.Add(filePath);
				}
			}

			var diagnostics = fileErrors.Select(error => new Diagnostic
			{
				Range = error.FileRange.ConvertRange(),
				Message = error.Message,
				Severity = DiagnosticSeverity.Error,
				Source = "GameScript"
			});

			_server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
			{
				Uri = DocumentUri.FromFileSystemPath(filePath),
				Diagnostics = new Container<Diagnostic>(diagnostics)
			});
		}
	}
}
