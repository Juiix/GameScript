using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Services;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DidCloseTextDocumentHandler(
	OpenDocumentCache openDocumentCache,
	DiagnosticsService diagnosticsService) : IDidCloseTextDocumentHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly DiagnosticsService _diagnosticsService = diagnosticsService;

	public Task<Unit> Handle(DidCloseTextDocumentParams req, CancellationToken ct)
	{
		var filePath = req.TextDocument.Uri.GetNormalizedFilePath();
		_openDocumentCache.Remove(filePath);
		_diagnosticsService.DocumentClosed(filePath);
		return Unit.Task;
	}

	public TextDocumentCloseRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = new TextDocumentSelector(
				TextDocumentFilter.ForLanguage("gamescript"),
				TextDocumentFilter.ForLanguage("objectdef"))
		};
	}
}