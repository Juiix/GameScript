using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DidCloseTextDocumentHandler(
	TextCache textCache) : IDidCloseTextDocumentHandler
{
	private readonly TextCache _textCache = textCache;

	public Task<Unit> Handle(DidCloseTextDocumentParams req, CancellationToken ct)
	{
		var filePath = req.TextDocument.Uri.Path.NormalizePath();
		_textCache.Clear(filePath);
		return Unit.Task;
	}

	public TextDocumentCloseRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
		};
	}
}