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
	Services.ProjectRegistry projects) : IWorkspaceSymbolsHandler
{
	private readonly Services.ProjectRegistry _projects = projects;

	public async Task<Container<WorkspaceSymbol>?> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken)
	{
		var query = request.Query ?? string.Empty;
		if (string.IsNullOrWhiteSpace(query))
		{
			return null;
		}

		// workspace-wide query: union across every project
		var allSymbols = _projects.Projects.SelectMany(p => p.Symbols.Symbols);
		var querySymbol = new SymbolInfo(default, query, null, null, null, null, null, null, string.Empty, default);
		var results = Process.ExtractSorted(querySymbol, allSymbols, x => x.Name);

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