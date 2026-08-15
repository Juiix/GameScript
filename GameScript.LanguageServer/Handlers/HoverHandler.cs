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
		return GetHover(astNode, parent, localIndex, symbols);
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
			IdentifierNode identifierNode => GetHover(identifierNode.Type, identifierNode.Name, localIndex, symbols),
			IdentifierDeclarationNode identifierDeclarationNode => parent is MethodDefinitionNode parentMethod
						? CreateMethodHover(parentMethod.SymbolName, symbols)
						: GetHover(identifierDeclarationNode.Type, identifierDeclarationNode.Name, localIndex, symbols),
			_ => null
		};
	}

	private static Hover? GetHover(IdentifierType identifierType, string name, LocalIndex? localIndex, ISymbolIndex symbols)
	{
		if ((identifierType & IdentifierType.Method) != IdentifierType.Unknown)
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

		var md = CreateFromSymbol(symbol);
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

		var md = CreateFromSymbol(symbol);
		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(md),
			Range = symbol.FileRange.ConvertRange()
		};
	}

	private static MarkupContent CreateFromSymbol(SymbolInfo symbol)
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