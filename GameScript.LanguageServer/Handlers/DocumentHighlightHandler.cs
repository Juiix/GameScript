using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers
{
	internal sealed class DocumentHighlightHandler(
		AstCache astCache,
		ISymbolIndex symbols) : IDocumentHighlightHandler
	{
		private readonly AstCache _astCache = astCache;
		private readonly ISymbolIndex _symbols = symbols;

		public DocumentHighlightRegistrationOptions GetRegistrationOptions(DocumentHighlightCapability capability, ClientCapabilities clientCapabilities)
		{
			return new()
			{
				DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
			};
		}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		public async Task<DocumentHighlightContainer?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
		{
			var filePath = request.TextDocument.Uri.Path.NormalizePath();
			if (!_astCache.TryGetRoot(filePath, out var rootData))
			{
				return null;
			}

			var symbolName = rootData.Root.FindNodeAtPosition(request.Position.Line, request.Position.Character)?.GetSymbolName();
			if (symbolName == null)
			{
				return null;
			}

			// load symbol & references (check local first)
			IEnumerable<ReferenceInfo>? references = null;
			var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
			var symbol = localIndex?.GetSymbol(symbolName);
			if (symbol != null) // is local
			{
				references = localIndex?.GetReferences(symbolName);
			}
			else // is global symbol
			{
				symbol = _symbols.GetSymbol(symbolName);
				references = rootData.Index.FileIndex.GetReferences(symbolName);
			}


			// build results
			var results = new List<DocumentHighlight>();
			if (references != null)
			{
				results.AddRange(references.Select(x => new DocumentHighlight
				{
					Kind = DocumentHighlightKind.Read,
					Range = x.FileRange.ConvertRange()
				}));
			}

			if (symbol != null &&
				symbol.FilePath.Equals(filePath))
			{
				results.Add(new DocumentHighlight
				{
					Kind = DocumentHighlightKind.Write,
					Range = symbol.FileRange.ConvertRange()
				});
			}

			return new DocumentHighlightContainer(results);
		}
	}
}
