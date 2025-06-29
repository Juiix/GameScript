using GameScript.Language.Index;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DocumentSymbolHandler(
	OpenDocumentCache openDocumentCache,
	AstCache astCache,
	GlobalSymbolTable globalSymbolTable) : IDocumentSymbolHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly AstCache _astCache = astCache;
	private readonly GlobalSymbolTable _globalSymbolTable = globalSymbolTable;

	public Task<SymbolInformationOrDocumentSymbolContainer?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        var filePath = request.TextDocument.Uri.Path.NormalizePath();
		if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) ||
			!_astCache.TryGetRoot(filePath, out var rootData) ||
			rootData.Parse.FileVersion != fileVersion)
		{
			ExceptionHelper.ThrowFileVersionNotFound();
			return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
		}


		var fileSymbols = _globalSymbolTable.GetSymbolsForFile(filePath);

        var flat = fileSymbols.Select(x => new SymbolInformationOrDocumentSymbol(new SymbolInformation
        {
            Name = x.Name,
            Kind = x.IdentifierType.GetSymbolKind(),
            Location = x.GetLocation()
		}));

        var symbols = new SymbolInformationOrDocumentSymbolContainer(flat);
		return Task.FromResult(symbols)!;
	}

    public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new ()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
        };
    }
}