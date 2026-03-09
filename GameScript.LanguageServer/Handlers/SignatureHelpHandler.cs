using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class SignatureHelpHandler(
	OpenDocumentCache openDocumentCache,
	AstCache astCache,
	ISymbolIndex symbols) : ISignatureHelpHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly AstCache _astCache = astCache;
	private readonly ISymbolIndex _symbols = symbols;

	public SignatureHelpRegistrationOptions GetRegistrationOptions(SignatureHelpCapability capability, ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
			TriggerCharacters = new Container<string>("(", ",")
		};
	}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
	public async Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
#pragma warning restore CS1998
	{
		var filePath = request.TextDocument.Uri.GetNormalizedFilePath();
		if (!_openDocumentCache.TryGet(filePath, out _, out var fileVersion) ||
			!_astCache.TryGetRoot(filePath, out var rootData) ||
			rootData.Parse.FileVersion != fileVersion)
		{
			ExceptionHelper.ThrowFileVersionNotFound();
			return null;
		}

		var line = request.Position.Line;
		var character = request.Position.Character;

		var callExpr = rootData.Root.FindNodeAtPosition<CallExpressionNode>(line, character);
		if (callExpr == null)
			return null;

		var symbol = _symbols.GetSymbol(callExpr.FunctionName.Name);
		if (symbol == null)
			return null;

		var cursorOffset = rootData.GetOffset(line, character);
		var activeParam = GetActiveParameterIndex(callExpr, cursorOffset);

		return BuildSignatureHelp(symbol, activeParam);
	}

	private static int GetActiveParameterIndex(CallExpressionNode callExpr, int cursorOffset)
	{
		if (callExpr.Arguments == null)
			return 0;

		int count = 0;
		foreach (var arg in callExpr.Arguments)
		{
			if (arg.FileRange.End.Position < cursorOffset)
				count++;
			else
				break;
		}
		return count;
	}

	private static SignatureHelp? BuildSignatureHelp(SymbolInfo symbol, int activeParam)
	{
		var paramInfos = new List<ParameterInformation>();
		if (symbol.ParamTypes != null)
		{
			int i = 0;
			foreach (var type in symbol.ParamTypes.AllTypes)
			{
				var label = type.Name;
				if (symbol.ParamNames != null && i < symbol.ParamNames.Count &&
					!string.IsNullOrWhiteSpace(symbol.ParamNames[i]))
				{
					label += " $" + symbol.ParamNames[i];
				}
				paramInfos.Add(new ParameterInformation { Label = new ParameterInformationLabel(label) });
				i++;
			}
		}

		int clampedParam = paramInfos.Count > 0 ? Math.Min(activeParam, paramInfos.Count - 1) : 0;

		var sigInfo = new SignatureInformation
		{
			Label = symbol.Signature,
			Documentation = string.IsNullOrEmpty(symbol.Summary)
				? null
				: new StringOrMarkupContent(symbol.Summary),
			Parameters = new Container<ParameterInformation>(paramInfos),
			ActiveParameter = clampedParam
		};

		return new SignatureHelp
		{
			Signatures = new Container<SignatureInformation>(sigInfo),
			ActiveSignature = 0,
			ActiveParameter = clampedParam
		};
	}
}
