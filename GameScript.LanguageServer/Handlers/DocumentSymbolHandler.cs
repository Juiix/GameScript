using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DocumentSymbolHandler(GlobalSymbolTable globalSymbolTable) : IDocumentSymbolHandler
{
    private readonly GlobalSymbolTable _globalSymbolTable = globalSymbolTable;

	public Task<SymbolInformationOrDocumentSymbolContainer?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        var filePath = request.TextDocument.Uri.Path.NormalizePath();
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