using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Text;

namespace GameScript.LanguageServer.Handlers;

internal sealed class HoverHandler(
	OpenDocumentCache openDocumentCache,
	AstCache astCache,
	Services.ProjectRegistry projects) : IHoverHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly AstCache _astCache = astCache;
	private readonly Services.ProjectRegistry _projects = projects;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
	public async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
	{
		var filePath = request.TextDocument.Uri.GetNormalizedFilePath();
		if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) ||
			!_astCache.TryGetRoot(filePath, out var rootData) ||
			rootData.Parse.FileVersion != fileVersion)
		{
			ExceptionHelper.ThrowFileVersionNotFound();
			return null;
		}


		var (astNode, parent) = rootData.Root.FindNodeAndParentAtPosition(request.Position.Line, request.Position.Character);
		if (astNode == null)
		{
			return null;
		}

		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		var symbols = _projects.GetProject(filePath).Symbols;

		// table columns resolve against their table, not the symbol tables
		if (astNode.IsColumnIdentifier())
		{
			var (table, column) = rootData.ResolveColumn(astNode, parent, localIndex, symbols);
			return column != null && table != null ? CreateColumnHover(astNode, table, column) : null;
		}

		return GetHover(astNode, parent, localIndex, symbols);
	}

	private static Hover CreateColumnHover(AstNode node, SymbolInfo table, TableColumnInfo column)
	{
		var builder = new StringBuilder();
		builder.AppendLine("```gamescript");
		builder.AppendLine(column.ColumnSignature());
		builder.AppendLine("```");
		builder.AppendLine();
		builder.AppendLine($"Column of `table {table.Name}`");
		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(new MarkupContent
			{
				Kind = MarkupKind.Markdown,
				Value = builder.ToString()
			}),
			Range = node.FileRange.ConvertRange()
		};
	}

	public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
		};
	}

	private static Hover? GetHover(AstNode astNode, AstNode? parent, LocalIndex? localIndex, ISymbolIndex symbols)
	{
		return astNode switch
		{
			MethodDefinitionNode methodDefinitionNode => CreateMethodHover(methodDefinitionNode.SymbolName, symbols),
			TableDefinitionNode tableDefinitionNode => CreateTableHover(tableDefinitionNode.Name.Name, symbols),
			TypeDefinitionNode typeDefinitionNode => CreateTypeHover(typeDefinitionNode.Name.Name, astNode, symbols, cast: false),
			// a named type in type position ('item x', 'returns item', a column type)
			TypeNode typeNode => CreateTypeHover(typeNode.Name, astNode, symbols, cast: false),
			// a cast callee ('item(x)')
			IdentifierNode { Type: IdentifierType.Type } castCallee => CreateTypeHover(castCallee.Name, astNode, symbols, cast: parent is CallExpressionNode),
			IdentifierNode identifierNode => GetHover(identifierNode.Type, identifierNode.Name, localIndex, symbols),
			IdentifierDeclarationNode { Type: IdentifierType.EngineOp } engineOp
				when parent is MethodDefinitionNode boundMethod => CreateEngineOpHover(engineOp, boundMethod),
			IdentifierDeclarationNode { Type: IdentifierType.Type } typeName => CreateTypeHover(typeName.Name, astNode, symbols, cast: false),
			IdentifierDeclarationNode identifierDeclarationNode => parent switch
			{
				MethodDefinitionNode parentMethod => CreateMethodHover(parentMethod.SymbolName, symbols),
				TableDefinitionNode parentTable => CreateTableHover(parentTable.Name.Name, symbols),
				_ => GetHover(identifierDeclarationNode.Type, identifierDeclarationNode.Name, localIndex, symbols),
			},
			_ => null
		};
	}

	// hovering a named type: its declaration + doc, and what the name does at this site
	private static Hover? CreateTypeHover(string typeName, AstNode site, ISymbolIndex symbols, bool cast)
	{
		var symbol = symbols.FindTypeSymbol(typeName);
		if (symbol == null)
			return null;    // a built-in type, or undeclared

		var root = symbol.Type?.Underlying?.Name ?? "?";
		var builder = new StringBuilder();
		if (!string.IsNullOrEmpty(symbol.Summary))
			builder.AppendLine(symbol.Summary);
		builder.AppendLine();
		builder.AppendLine("```gamescript");
		builder.AppendLine(symbol.Signature);
		builder.AppendLine("```");
		builder.AppendLine();
		builder.AppendLine(cast
			? $"Cast: converts an `{root}` value (or another `{root}`-rooted named type) to `{typeName}`. Compiles to nothing."
			: $"Named type over `{root}`: widens to `{root}` implicitly; `{typeName}(expr)` converts to it.");

		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(new MarkupContent
			{
				Kind = MarkupKind.Markdown,
				Value = builder.ToString()
			}),
			Range = site.FileRange.ConvertRange()
		};
	}

	private static Hover? CreateTableHover(string tableName, ISymbolIndex symbols)
	{
		var symbol = TableAccess.GetTable(symbols, tableName);
		if (symbol == null)
			return null;

		var md = CreateFromSymbol(symbol, symbols);
		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(md),
			Range = symbol.FileRange.ConvertRange()
		};
	}

	// hovering the engine-op name of a command '= name' binding
	private static Hover CreateEngineOpHover(IdentifierDeclarationNode engineOp, MethodDefinitionNode command)
	{
		var md = new MarkupContent
		{
			Kind = MarkupKind.Markdown,
			Value = $"Engine op binding: `{command.SymbolName}` compiles to the host operation `{engineOp.Name}`."
		};
		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(md),
			Range = engineOp.FileRange.ConvertRange()
		};
	}

	private static Hover? GetHover(IdentifierType identifierType, string name, LocalIndex? localIndex, ISymbolIndex symbols)
	{
		if (identifierType == IdentifierType.Table)
		{
			return CreateTableHover(name, symbols);
		}
		else if (identifierType == IdentifierType.Type)
		{
			var typeSymbol = symbols.FindTypeSymbol(name);
			return typeSymbol == null ? null : CreateTypeHover(name, new TypeNode(name, typeSymbol.FilePath, typeSymbol.FileRange), symbols, cast: false);
		}
		else if ((identifierType & IdentifierType.Method) != IdentifierType.Unknown)
		{
			return CreateMethodHover(name, symbols);
		}
		else if ((identifierType & IdentifierType.Variable) != IdentifierType.Unknown)
		{
			return CreateVariableHover(name, localIndex, symbols);
		}

		return null;
	}

	private static Hover? CreateMethodHover(string symbolName, ISymbolIndex symbols)
	{
		var symbol = symbols.GetSymbol(symbolName);
		if (symbol == null)
			return null;

		var md = CreateFromSymbol(symbol, symbols);
		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(md),
			Range = symbol.FileRange.ConvertRange()
		};
	}

	private static Hover? CreateVariableHover(string symbolName, LocalIndex? localIndex, ISymbolIndex symbols)
	{
		var symbol = localIndex?.GetSymbol(symbolName) ?? symbols.GetSymbol(symbolName);
		if (symbol == null)
			return null;

		var md = CreateFromSymbol(symbol, symbols);
		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(md),
			Range = symbol.FileRange.ConvertRange()
		};
	}

	private static MarkupContent CreateFromSymbol(SymbolInfo symbol, ISymbolIndex symbols)
	{
		var builder = new StringBuilder();
		if (!string.IsNullOrEmpty(symbol.Summary))
			builder.AppendLine(symbol.Summary);
		builder.AppendLine();
		builder.AppendLine("```gamescript");
		if (symbol.LiteralValue != null)
		{
			builder.Append(symbol.Signature);
			builder.Append(" = ");
			builder.AppendLine(GetLiteralString(symbol.LiteralValue));
		}
		else
		{
			builder.AppendLine(symbol.Signature);
		}
		builder.AppendLine("```");

		// a typed constant / context / local / parameter: show the named type and its root
		if (symbol.Type is { IsNamed: true } named)
		{
			// the symbol's type may be an index-time placeholder; the root lives on the declaration
			var root = named.Underlying?.Name ?? symbols.FindTypeSymbol(named.Name)?.Type?.Underlying?.Name;
			builder.AppendLine();
			builder.AppendLine(root != null ? $"Type: `{named.Name}` (`{root}`)" : $"Type: `{named.Name}`");
		}

		var md = new MarkupContent
		{
			Kind = MarkupKind.Markdown,
			Value = builder.ToString()
		};
		return md;
	}

	private static string GetLiteralString(object literalValue)
	{
		return literalValue switch
		{
			string str => $"\"{str}\"",
			_ => literalValue.ToString() ?? ""
		};
	}
}