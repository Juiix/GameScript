using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Services;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal class DidOpenTextDocumentHandler(
	OpenDocumentCache openDocumentCache,
	FileProcessingService fileProcessingService) : IDidOpenTextDocumentHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly FileProcessingService _fileProcessingService = fileProcessingService;

	public Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
	{
		var filePath = request.TextDocument.Uri.Path.NormalizePath();
		var text = request.TextDocument.Text;

		_openDocumentCache.Update(filePath, text, request.TextDocument.Version ?? 0);
		_fileProcessingService.Queue(filePath);

		return Unit.Task;
	}

	public TextDocumentOpenRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
		};
	}
}
