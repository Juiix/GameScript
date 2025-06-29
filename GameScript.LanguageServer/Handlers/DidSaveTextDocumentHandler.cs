using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Services;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DidSaveTextDocumentHandler(
	FileProcessingService fileProcessingService) : IDidSaveTextDocumentHandler
{
	private readonly FileProcessingService _fileProcessingService = fileProcessingService;

	public async Task<Unit> Handle(
		DidSaveTextDocumentParams request,
		CancellationToken cancellationToken)
	{
		var filePath = request.TextDocument.Uri.GetFileSystemPath().NormalizePath();
		_fileProcessingService.Queue(filePath);

		return Unit.Value;
	}

	// Tell VS Code we accept saves for our language and that we prefer the full
	// text (`includeText: true`).  If the client refuses, we fall back to disk I/O.
	public TextDocumentSaveRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
	{
		DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
		IncludeText = false
	};
}