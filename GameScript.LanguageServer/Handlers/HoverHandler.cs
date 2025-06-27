using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class HoverHandler(
	AstCache astCache,
	ISymbolIndex symbols) : IHoverHandler
{
	private readonly AstCache _astCache = astCache;
	private readonly ISymbolIndex _symbols = symbols;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
	public async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
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

		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		return GetHover(astNode, localIndex);
	}

	public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
		};
	}

	private Hover? GetHover(AstNode astNode, LocalIndex? localIndex)
	{
		return astNode switch
		{
			MethodDefinitionNode methodDefinitionNode => CreateMethodHover(methodDefinitionNode.Name.Name),
			IdentifierNode identifierNode => GetHover(identifierNode.Type, identifierNode.Name, localIndex),
			IdentifierDeclarationNode identifierDeclarationNode => GetHover(identifierDeclarationNode.Type, identifierDeclarationNode.Name, localIndex),
			_ => null
		};
	}

	private Hover? GetHover(IdentifierType identifierType, string name, LocalIndex? localIndex)
	{
		if ((identifierType & IdentifierType.Method) != IdentifierType.Unknown)
		{
			return CreateMethodHover(name);
		}
		else if ((identifierType & IdentifierType.Variable) != IdentifierType.Unknown)
		{
			return CreateVariableHover(name, localIndex);
		}

		return null;
	}

	private Hover? CreateMethodHover(string symbolName)
	{
		var symbol = _symbols.GetSymbol(symbolName);
		if (symbol == null)
		{
			return null;
		}

		var md = new MarkupContent
		{
			Kind = MarkupKind.PlainText,
			Value = symbol.Signature
		};

		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(md),
			Range = symbol.FileRange.ConvertRange()
		};
	}

	private Hover? CreateVariableHover(string symbolName, LocalIndex? localIndex)
	{
		var symbol = localIndex?.GetSymbol(symbolName) ?? _symbols.GetSymbol(symbolName);
		if (symbol == null)
		{
			return null;
		}
		
		var md = new MarkupContent
		{
			Kind = MarkupKind.PlainText,
			Value = symbol.Signature
		};

		return new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(md),
			Range = symbol.FileRange.ConvertRange()
		};
	}
}