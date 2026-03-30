using GameScript.Language;
using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class CompletionHandler(
	OpenDocumentCache openDocumentCache,
	AstCache astCache,
	ISymbolIndex symbols) : ICompletionHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly AstCache _astCache = astCache;
	private readonly ISymbolIndex _symbols = symbols;

	public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
			ResolveProvider = false,
			TriggerCharacters = new Container<string>("$", "^", "%", "~", "@", ".")
		};
	}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
	public async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
	{
		var filePath = request.TextDocument.Uri.GetNormalizedFilePath();
		if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) ||
			!_astCache.TryGetRoot(filePath, out var rootData))
		{
			ExceptionHelper.ThrowFileVersionNotFound();
			return new CompletionList();
		}

		var prefix = rootData.GetPrefix(text, request.Position, out var targetType);
		// strip leading dots for matching — symbol names don't include the dot prefix
		prefix = prefix.TrimStart('.');
		var startsWithSymbols = new List<SymbolInfo>();
		var containsSymbols = new List<SymbolInfo>();

		// get local scope
		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		if (localIndex != null)
		{
			foreach (var x in localIndex.Symbols)
				AddIfMatch(x, prefix, targetType, startsWithSymbols, containsSymbols);
		}

		// add global symbols
		foreach (var x in _symbols.Symbols)
		{
			if (x.IdentifierType != IdentifierType.Trigger)
				AddIfMatch(x, prefix, targetType, startsWithSymbols, containsSymbols);
		}

		var returnSymbols = startsWithSymbols.Concat(containsSymbols);

		// symbol items
		var symbolItems = returnSymbols.Select(x => new CompletionItem
		{
			Label = x.Name,
			Kind = x.IdentifierType switch
			{
				IdentifierType.Local or
				IdentifierType.Context or
				IdentifierType.Constant => CompletionItemKind.Variable,

				IdentifierType.Func or
				IdentifierType.Label or
				IdentifierType.Command => CompletionItemKind.Function,

				_ => default
			},
			Detail = x.Signature
		});

		var items = symbolItems;

		// keyword items
		if (targetType == IdentifierType.Unknown)
		{
			var startsWithKeywords = Constants.AllKeywords.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			var containsKeywords = Constants.AllKeywords.Where(x => !x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && x.Contains(prefix, StringComparison.OrdinalIgnoreCase));
			var keywordItems = startsWithKeywords.Concat(containsKeywords)
				.Select(x => new CompletionItem
				{
					Label = x,
					Kind = CompletionItemKind.Keyword
				});
			items = keywordItems.Concat(symbolItems);
		}
		return new CompletionList(items, false);
	}

	private static void AddIfMatch(SymbolInfo symbol, string prefix, IdentifierType targetType,
		List<SymbolInfo> startsWithList, List<SymbolInfo> containsList)
	{
		if (targetType != IdentifierType.Unknown && symbol.IdentifierType != targetType)
			return;

		if (symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			startsWithList.Add(symbol);
		else if (symbol.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
			containsList.Add(symbol);
	}
}