using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DefinitionHandler(
	AstCache astCache,
	ISymbolIndex symbols) : IDefinitionHandler
{
	private readonly AstCache _astCache = astCache;
	private readonly ISymbolIndex _symbols = symbols;

	public async Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
	{
		// 1. Load parsed ast
		var filePath = request.TextDocument.Uri.Path.NormalizePath();
		if (!_astCache.TryGetRoot(filePath, out var rootData))
		{
			return null;
		}

		// 2. Get identifier under cursor
		var identifierNode = rootData.Root.FindNodeAtPosition<IdentifierNode>(request.Position.Line, request.Position.Character);
		if (identifierNode == null)
		{
			return null;
		}

		// 3. Lookup local scope/symbol
		SymbolInfo? symbol = null;
		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		if (localIndex != null)
		{
			symbol = localIndex.GetSymbol(identifierNode.Name);
		}

		// 4. Lookup global symbol
		symbol ??= _symbols.GetSymbol(identifierNode.Name);
		if (symbol == null)
		{
			return null;
		}

		// 5. Return location
		var definitionLocation = symbol.GetLocation();
		return new LocationOrLocationLinks(definitionLocation);
	}

	public DefinitionRegistrationOptions GetRegistrationOptions(DefinitionCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
		};
	}
}