using GameScript.Language.Ast;
using GameScript.Language.File;

namespace GameScript.LanguageServer.Parsing
{
	internal sealed class ParseResult(
		AstNode root,
		List<FileError> errors,
		IReadOnlyList<CommentNode> comments,
		IReadOnlyList<int> lineOffsets)
	{
		public AstNode Root { get; } = root;
		public List<FileError> Errors { get; } = errors;
		public IReadOnlyList<CommentNode> Comments { get; } = comments;
		public IReadOnlyList<int> LineOffsets { get; } = lineOffsets;
	}
}
