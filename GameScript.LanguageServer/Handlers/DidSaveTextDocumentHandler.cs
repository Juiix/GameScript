using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Services;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DidSaveTextDocumentHandler(
	TextCache textCache,
	FileProcessingService fileProcessingService) : IDidSaveTextDocumentHandler
{
	private readonly TextCache _textCache = textCache;
	private readonly FileProcessingService _fileProcessingService = fileProcessingService;

	public async Task<Unit> Handle(
		DidSaveTextDocumentParams request,
		CancellationToken cancellationToken)
	{
		var filePath = request.TextDocument.Uri.Path.NormalizePath();
		string text;

		if (request.Text is { Length: > 0 })
		{
			// Client provided the full text in the notification
			text = request.Text;
		}
		else
		{
			// Fall back to loading the file from disk
			text = await File.ReadAllTextAsync(filePath, cancellationToken)
				.ConfigureAwait(false);
		}

		_textCache.Update(filePath, text);
		_fileProcessingService.Queue(filePath);

		return Unit.Value;
	}

	// Tell VS Code we accept saves for our language and that we prefer the full
	// text (`includeText: true`).  If the client refuses, we fall back to disk I/O.
	public TextDocumentSaveRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
	{
		DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
		IncludeText = true
	};
}