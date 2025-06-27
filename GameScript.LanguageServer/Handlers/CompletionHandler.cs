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
	TextCache textCache,
	AstCache astCache,
	ISymbolIndex symbols) : ICompletionHandler
{
	private readonly TextCache _textCache = textCache;
	private readonly AstCache _astCache = astCache;
	private readonly ISymbolIndex _symbols = symbols;

	public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
			ResolveProvider = false,
			TriggerCharacters = new Container<string>("$", "^", "%", "~", "@")
		};
	}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
	public async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
	{
		var filePath = request.TextDocument.Uri.Path.NormalizePath();
		if (!_textCache.TryGetText(filePath, out var text) ||
			!_astCache.TryGetRoot(filePath, out var rootData))
		{
			return new CompletionList();
		}

		var prefix = rootData.GetPrefix(text, request.Position, out var targetType);
		var returnSymbols = new List<SymbolInfo>();

		// get local scope
		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		if (localIndex != null)
		{
			returnSymbols.AddRange(localIndex.Symbols.Where(x => Matches(x.Name, x.IdentifierType, prefix, targetType)));
		}

		// add global symbols
		returnSymbols.AddRange(_symbols.Symbols.Where(x => x.IdentifierType != IdentifierType.Trigger && Matches(x.Name, x.IdentifierType, prefix, targetType)));

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
			var keywordItems = Constants.AllKeywords
				.Where(x => x.StartsWith(prefix))
				.Select(x => new CompletionItem
				{
					Label = x,
					Kind = CompletionItemKind.Keyword
				});
			items = keywordItems.Concat(symbolItems);
		}
		return new CompletionList(items, false);
	}

	private static bool Matches(string name, IdentifierType idType, string prefix, IdentifierType targetType)
	{
		return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
			(targetType == IdentifierType.Unknown || idType == targetType);
	}
}