using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers
{
	internal sealed class PrepareRenameHandler(
		AstCache astCache,
		ISymbolIndex symbols) : IPrepareRenameHandler
	{
		private readonly AstCache _astCache = astCache;
		private readonly ISymbolIndex _symbols = symbols;

		public async Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
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

			var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
			var symbol = localIndex?.GetSymbol(symbolName) ?? _symbols.GetSymbol(symbolName);
			if (symbol == null)
			{
				return null;
			}

			var placeholder = symbol.Name;
			return new RangeOrPlaceholderRange(
				new PlaceholderRange
				{
					Placeholder = placeholder,
					Range = astNode.FileRange.ConvertRange()
				}
			);
		}

		public RenameRegistrationOptions GetRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
		{
			return new()
			{
				DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
				PrepareProvider = true
			};
		}
	}
}
