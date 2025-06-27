using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal class DidOpenTextDocumentHandler(
	TextCache textCache) : IDidOpenTextDocumentHandler
{
	private readonly TextCache _textCache = textCache;

	public Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
	{
		var filePath = request.TextDocument.Uri.Path.NormalizePath();
		var text = request.TextDocument.Text;

		_textCache.Update(filePath, text);

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
