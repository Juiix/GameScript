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
		private readonly Dictionary<string, IReadOnlyList<FileError>> _files = [];
		private readonly Dictionary<string, DocumentUri> _openUris = [];
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
		/// Records the URI the client opened a document with. While it is open, the
		/// file's diagnostics are published under that URI: a client matches diagnostics
		/// to its editors by exact URI, and on a case-insensitive file system that URI
		/// can be spelled differently from the path the file is keyed by.
		/// </summary>
		public void DocumentOpened(string filePath, DocumentUri uri)
		{
			DocumentUri previous;
			IReadOnlyList<FileError>? errors;
			lock (_lock)
			{
				previous = TargetUri(filePath);
				_openUris[filePath] = uri;
				_files.TryGetValue(filePath, out errors);
			}

			Move(previous, uri, errors);
		}

		/// <summary>
		/// Forgets the client's URI for a closed document; its diagnostics go back to
		/// the URI of the path the file is keyed by.
		/// </summary>
		public void DocumentClosed(string filePath)
		{
			DocumentUri previous, next;
			IReadOnlyList<FileError>? errors;
			lock (_lock)
			{
				if (!_openUris.Remove(filePath, out previous!))
					return;

				next = TargetUri(filePath);
				_files.TryGetValue(filePath, out errors);
			}

			Move(previous, next, errors);
		}

		/// <summary>
		/// Removes all diagnostics for a file and notifies the client.
		/// </summary>
		/// <param name="filePath">Absolute path of the file that was fixed or closed.</param>
		public void Clear(string filePath)
		{
			DocumentUri uri;
			lock (_lock)
			{
				// If we weren't tracking diagnostics for this file, nothing to do.
				if (!_files.Remove(filePath))
					return;

				uri = TargetUri(filePath);
			}

			Send(uri, []);
		}

		/// <summary>
		/// Publishes the given set of diagnostics for a file.
		/// If the list is empty, any existing diagnostics are cleared.
		/// </summary>
		/// <param name="filePath">Absolute path of the file being analyzed.</param>
		/// <param name="fileErrors">Errors produced by the analyzer.</param>
		public void Publish(string filePath, IReadOnlyList<FileError> fileErrors)
		{
			DocumentUri uri;
			lock (_lock)
			{
				// Track the file only if it has diagnostics.
				if (fileErrors.Count == 0 && !_files.Remove(filePath))
					return;

				if (fileErrors.Count != 0)
				{
					_files[filePath] = fileErrors;
				}

				uri = TargetUri(filePath);
			}

			Send(uri, fileErrors);
		}

		/// <summary>The URI a file's diagnostics go to. Call under <see cref="_lock"/>.</summary>
		private DocumentUri TargetUri(string filePath) =>
			_openUris.TryGetValue(filePath, out var uri) ? uri : DocumentUri.FromFileSystemPath(filePath);

		/// <summary>Re-homes a file's shown diagnostics when its target URI changes.</summary>
		private void Move(DocumentUri previous, DocumentUri next, IReadOnlyList<FileError>? errors)
		{
			if (errors is null ||
				string.Equals(previous.ToString(), next.ToString(), StringComparison.Ordinal))
				return;

			Send(previous, []);
			Send(next, errors);
		}

		private void Send(DocumentUri uri, IReadOnlyList<FileError> fileErrors)
		{
			var diagnostics = fileErrors.Select(error => new Diagnostic
			{
				Range = error.FileRange.ConvertRange(),
				Message = error.Message,
				Severity = ConvertSeverity(error.Severity),
				Tags = ConvertTag(error.Tag),
				Source = "GameScript"
			});

			_server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
			{
				Uri = uri,
				Diagnostics = new Container<Diagnostic>(diagnostics)
			});
		}

		private static DiagnosticSeverity ConvertSeverity(FileErrorSeverity severity)
		{
			return severity switch
			{
				FileErrorSeverity.Warning => DiagnosticSeverity.Warning,
				FileErrorSeverity.Information => DiagnosticSeverity.Information,
				FileErrorSeverity.Hint => DiagnosticSeverity.Hint,
				_ => DiagnosticSeverity.Error,
			};
		}

		private static Container<DiagnosticTag>? ConvertTag(FileErrorTag tag)
		{
			return tag switch
			{
				FileErrorTag.Unnecessary => new Container<DiagnosticTag>(DiagnosticTag.Unnecessary),
				FileErrorTag.Deprecated => new Container<DiagnosticTag>(DiagnosticTag.Deprecated),
				_ => null,
			};
		}
	}
}
