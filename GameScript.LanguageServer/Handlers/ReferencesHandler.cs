using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class ReferencesHandler(
	AstCache astCache,
	IReferenceIndex references) : IReferencesHandler
{
	private readonly AstCache _astCache = astCache;
	private readonly IReferenceIndex _references = references;

	public async Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
	{
		var filePath = request.TextDocument.Uri.Path.NormalizePath();
		if (!_astCache.TryGetRoot(filePath, out var rootData))
		{
			return null;
		}

		var astNode = rootData.Root.FindNodeAtPosition(request.Position.Line, request.Position.Character);
		if (astNode == null)
		{
			return null;
		}

		var symbolName = astNode.GetSymbolName();
		if (symbolName == null)
		{
			return null;
		}

		// if local symbol is found, return local references
		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		var localSymbol = localIndex?.GetSymbol(symbolName);
		var references = localSymbol != null ?
			localIndex?.GetReferences(symbolName) ?? [] :
			_references.GetReferences(symbolName);

		return new LocationContainer(
			references.Select(x => new Location
			{
				Uri = DocumentUri.FromFileSystemPath(x.FilePath),
				Range = x.FileRange.ConvertRange()
			})
		);
	}

	public ReferenceRegistrationOptions GetRegistrationOptions(ReferenceCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
		};
	}
}