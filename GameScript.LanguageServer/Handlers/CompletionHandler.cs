using GameScript.Language;
using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Tools;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class CompletionHandler(
	OpenDocumentCache openDocumentCache,
	AstCache astCache,
	Services.ProjectRegistry projects) : ICompletionHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly AstCache _astCache = astCache;
	private readonly Services.ProjectRegistry _projects = projects;

	public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = new TextDocumentSelector(
				TextDocumentFilter.ForLanguage("gamescript"),
				TextDocumentFilter.ForLanguage("objectdef")),
			ResolveProvider = false,
			TriggerCharacters = new Container<string>("^", "@", ".")
		};
	}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
	public async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
	{
		var filePath = request.TextDocument.Uri.GetNormalizedFilePath();

		// Object definition files only support constant completion
		if (ExtensionFilter.IsObjectDef(filePath))
			return HandleObjectDefCompletion(filePath, request.Position);

		if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) ||
			!_astCache.TryGetRoot(filePath, out var rootData))
		{
			ExceptionHelper.ThrowFileVersionNotFound();
			return new CompletionList();
		}

		var prefix = rootData.GetPrefix(text, request.Position, out var targetType);

		// get local scope
		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);

		// 't.', 't[k].', 't.at(i).', 'r.' — offer the table's members
		if (targetType == IdentifierType.Column)
			return HandleMemberCompletion(rootData, filePath, request.Position, prefix, localIndex);

		// strip leading dots for matching — symbol names don't include the dot prefix
		prefix = prefix.TrimStart('.');
		var startsWithSymbols = new List<SymbolInfo>();
		var containsSymbols = new List<SymbolInfo>();
		if (localIndex != null)
		{
			foreach (var x in localIndex.Symbols)
				AddIfMatch(x, prefix, targetType, startsWithSymbols, containsSymbols);
		}

		// add the file's project symbols
		foreach (var x in _projects.GetProject(filePath).Symbols.Symbols)
		{
			if (x.IdentifierType is not (IdentifierType.Trigger or IdentifierType.TriggerDeclaration))
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

				IdentifierType.Table => CompletionItemKind.Struct,

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

	// Completion after a member-access dot: the columns of the table behind the
	// target (a keyed lookup, '.at(i)', or a row cursor), or the built-ins
	// ('count', 'has', 'at') when the target is the table itself.
	private CompletionList HandleMemberCompletion(Parsing.RootFileData rootData, string filePath, Position position, string prefix, LocalIndex? localIndex)
	{
		// prefix is '.name' — the target ends just before the dot
		var dotOffset = rootData.GetOffset(position.Line, position.Character) - prefix.Length;
		var targetNode = dotOffset > 0 ? rootData.Root.FindNodeAtPosition(dotOffset - 1) : null;
		if (targetNode is not ExpressionNode target)
			return new CompletionList();

		var symbols = _projects.GetProject(filePath).Symbols;
		var table = TableAccess.ResolveTable(target, localIndex, symbols);
		if (table == null)
			return new CompletionList();

		var typed = prefix.TrimStart('.');
		var items = new List<CompletionItem>();

		if (TableAccess.IsTableTarget(target))
		{
			AddMember(MemberExpressionNode.CountMember, $"int {table.Name}.count", CompletionItemKind.Property);
			AddMember(MemberExpressionNode.HasMember, $"bool {table.Name}.has(key)", CompletionItemKind.Method);
			AddMember(MemberExpressionNode.AtMember, $"{table.Name}.at(i)", CompletionItemKind.Method);
		}
		else if (TableAccess.IsRowTarget(target, localIndex) && table.Columns != null)
		{
			foreach (var column in table.Columns)
				AddMember(column.Name, column.ColumnSignature(), CompletionItemKind.Field);
		}

		return new CompletionList(items, false);

		void AddMember(string name, string detail, CompletionItemKind kind)
		{
			if (typed.Length == 0 || name.Contains(typed, StringComparison.OrdinalIgnoreCase))
				items.Add(new CompletionItem { Label = name, Kind = kind, Detail = detail });
		}
	}

	private CompletionList HandleObjectDefCompletion(string filePath, Position position)
	{
		if (!_openDocumentCache.TryGet(filePath, out var text, out _))
			return new CompletionList();

		var offset = GetOffsetFromPosition(text, position.Line, position.Character);
		if (offset < 0) return new CompletionList();

		// walk backwards to collect identifier characters
		var i = offset - 1;
		while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
			i--;

		// only offer completions after ^
		if (i < 0 || text[i] != '^')
			return new CompletionList();

		var prefix = text[(i + 1)..offset];

		var startsWithSymbols = new List<SymbolInfo>();
		var containsSymbols = new List<SymbolInfo>();
		foreach (var symbol in _projects.GetProject(filePath).Symbols.Symbols)
		{
			if (symbol.IdentifierType != IdentifierType.Constant)
				continue;
			if (symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				startsWithSymbols.Add(symbol);
			else if (symbol.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
				containsSymbols.Add(symbol);
		}

		var items = startsWithSymbols.Concat(containsSymbols).Select(x => new CompletionItem
		{
			Label = x.Name,
			Kind = CompletionItemKind.Variable,
			Detail = x.Signature
		});
		return new CompletionList(items, false);
	}

	private static int GetOffsetFromPosition(string text, int line, int character)
	{
		var offset = 0;
		for (int l = 0; l < line; l++)
		{
			offset = text.IndexOf('\n', offset) + 1;
			if (offset == 0) return -1;
		}
		return Math.Min(offset + character, text.Length);
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