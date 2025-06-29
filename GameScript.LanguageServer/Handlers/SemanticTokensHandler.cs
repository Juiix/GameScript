using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Collections.Concurrent;

namespace GameScript.LanguageServer.Handlers
{
	internal sealed class SemanticTokensHandler(
		OpenDocumentCache openDocumentCache,
		AstCache astCache) : SemanticTokensHandlerBase
	{
		private static readonly SemanticTokenType[] _types = [
			SemanticTokenType.Keyword,      // 0
			SemanticTokenType.Function,     // 1
			SemanticTokenType.Variable,     // 2
			SemanticTokenType.Number,       // 3
			SemanticTokenType.String,       // 4
			SemanticTokenType.Comment,      // 5
			SemanticTokenType.Operator,     // 6
			SemanticTokenType.Type			// 7
		];
		private static readonly SemanticTokenModifier[] _modifiers = [

		];
		public static readonly SemanticTokensLegend Legend = new()
		{
			TokenTypes = new Container<SemanticTokenType>(_types),
			TokenModifiers = new Container<SemanticTokenModifier>(_modifiers)
		};

		private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
		private readonly AstCache _astCache = astCache;
		private readonly ConcurrentDictionary<Uri, SemanticTokensDocument> _cache = [];

		protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
		{
			return new()
			{
				DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
				Legend = Legend,
				Full = true,
				Range = true
			};
		}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		protected override async Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
		{
			var uri = @params.TextDocument.Uri.ToUri();
			SemanticTokensDocument? document;
			do
			{
				if (_cache.TryGetValue(uri, out document))
				{
					return document;
				}
				document = new SemanticTokensDocument(Legend);
			} while (!_cache.TryAdd(uri, document));
			return document;
		}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		protected override async Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
		{
			var filePath = identifier.TextDocument.Uri.Path.NormalizePath();
			if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) ||
				!_astCache.TryGetRoot(filePath, out var rootData) ||
				rootData.Parse.FileVersion != fileVersion)
			{
				ExceptionHelper.ThrowFileVersionNotFound();
				return;
			}


			int commentIndex = 0;
			foreach (var node in rootData.Root.Traverse())
			{
				if (cancellationToken.IsCancellationRequested) break;

				foreach (var passedComment in GetPassedComments(rootData.Parse.Comments, node.FileRange, commentIndex))
				{
					PushNode(builder, passedComment);
					commentIndex++;
				}

				PushNode(builder, node);
			}

			for (int i = commentIndex; i < rootData.Parse.Comments.Count; i++)
			{
				PushNode(builder, rootData.Parse.Comments[i]);
			}
		}

		private IEnumerable<CommentNode> GetPassedComments(IReadOnlyList<CommentNode> comments, FileRange fileRange, int commentIndex)
		{
			while (commentIndex < comments.Count)
			{
				var nextComment = comments[commentIndex];
				if (fileRange.Start.Line > nextComment.FileRange.Start.Line ||
					fileRange.Start.Line == nextComment.FileRange.Start.Line && fileRange.Start.Column > nextComment.FileRange.Start.Column)
				{
					yield return nextComment;
					commentIndex++;
				}
				else
				{
					yield break;
				}
			}
		}

		private void PushNode(SemanticTokensBuilder builder, AstNode node)
		{
			var line = node.FileRange.Start.Line;
			var column = node.FileRange.Start.Column;
			var length = node.FileRange.End.Position - node.FileRange.Start.Position;

			switch (node)
			{
				case LiteralNode literalNode:
					builder.Push(line, column, length, GetLiteralType(literalNode.Type), 0);
					break;
				case TypeNode typeNode:
					builder.Push(line, column, length, 7, 0);
					break;
				case IdentifierDeclarationNode identifierDeclarationNode:
					builder.Push(line, column, length, GetIdentifierType(identifierDeclarationNode.Type), 0);
					break;
				case IdentifierNode identifierNode:
					builder.Push(line, column, length, GetIdentifierType(identifierNode.Type), 0);
					break;
				case KeywordNode keywordNode:
					builder.Push(line, column, length, 0, 0);
					break;
				case OperatorNode operatorNode:
					builder.Push(line, column, length, 6, 0);
					break;
				case CommentNode commentNode:
					builder.Push(line, column, length, 7, 0);
					break;
			}
		}

		private static int GetLiteralType(LiteralType type)
		{
			return type switch
			{
				LiteralType.Number => 3,
				LiteralType.String => 4,
				LiteralType.Boolean => 0,
				_ => 0
			};
		}

		private static int GetIdentifierType(IdentifierType type)
		{
			return type switch
			{
				IdentifierType.Func or IdentifierType.Label or IdentifierType.Command or IdentifierType.Trigger => 1,
				IdentifierType.Local or IdentifierType.Constant or IdentifierType.Context => 2,
				_ => 0,
			};
		}
	}
}
