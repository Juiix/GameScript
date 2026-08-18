using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DefinitionHandler(
	OpenDocumentCache openDocumentCache,
	AstCache astCache,
	Services.ProjectRegistry projects) : IDefinitionHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly AstCache _astCache = astCache;
	private readonly Services.ProjectRegistry _projects = projects;

	public async Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
	{
		// 1. Load parsed ast
		var filePath = request.TextDocument.Uri.GetNormalizedFilePath();
		if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) ||
			!_astCache.TryGetRoot(filePath, out var rootData) ||
			rootData.Parse.FileVersion != fileVersion)
		{
			ExceptionHelper.ThrowFileVersionNotFound();
			return null;
		}

		// 2. Get identifier under cursor
		var (node, parent) = rootData.Root.FindNodeAndParentAtPosition(request.Position.Line, request.Position.Character);
		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);

		// table columns jump to the column's declaration in the table header
		if (node != null && node.IsColumnIdentifier())
		{
			var (_, column) = rootData.ResolveColumn(node, parent, localIndex, _projects.GetProject(filePath).Symbols);
			if (column == null)
				return null;
			return new LocationOrLocationLinks(new Location
			{
				Uri = DocumentUri.FromFileSystemPath(column.FilePath),
				Range = column.Range.ConvertRange()
			});
		}

		if (node is not IdentifierNode identifierNode)
		{
			return null;
		}

		// 3. Lookup local scope/symbol
		SymbolInfo? symbol = null;
		if (localIndex != null)
		{
			symbol = localIndex.GetSymbol(identifierNode.Name);
		}

		// 4. Lookup the file's project symbols
		symbol ??= _projects.GetProject(filePath).Symbols.GetSymbol(identifierNode.Name);
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