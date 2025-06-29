using FuzzySharp;
using GameScript.Bytecode;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace GameScript.LanguageServer.Handlers;

internal sealed class WorkspaceSymbolsHandler(
	ISymbolIndex globalSymbolTable) : IWorkspaceSymbolsHandler
{
	private readonly ISymbolIndex _globalSymbolTable = globalSymbolTable;

	public async Task<Container<WorkspaceSymbol>?> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken)
	{
		var query = request.Query ?? string.Empty;
		if (string.IsNullOrWhiteSpace(query))
		{
			return null;
		}

		var querySymbol = new SymbolInfo(default, query, null, null, null, null, null, null, string.Empty, default);
		var results = Process.ExtractSorted(querySymbol, _globalSymbolTable.Symbols, x => x.Name);

		var flat = results.Take(100).Select(x => new WorkspaceSymbol
		{
			Name = x.Value.Name,
			Kind = x.Value.IdentifierType.GetSymbolKind(),
			Location = x.Value.GetLocation()
		});

		var symbols = new Container<WorkspaceSymbol>(flat);
		return symbols;
	}

    public WorkspaceSymbolRegistrationOptions GetRegistrationOptions(WorkspaceSymbolCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new()
		{
			ResolveProvider = true
		};
    }
}