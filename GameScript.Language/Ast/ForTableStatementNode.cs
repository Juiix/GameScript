using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// 'for CURSOR in TABLE' — positional iteration over a constant table. The
	// cursor is a row handle: 'cursor.col' is its only valid use. Lowered to a
	// counted loop over a hidden index; each 'cursor.col' read is a positional
	// lookup on that index.
	public sealed class ForTableStatementNode(
		KeywordNode forKeyword,
		IdentifierDeclarationNode cursor,
		KeywordNode inKeyword,
		IdentifierNode table,
		BlockNode? body,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode ForKeyword { get; } = forKeyword;
		public IdentifierDeclarationNode Cursor { get; } = cursor;
		public KeywordNode InKeyword { get; } = inKeyword;
		public IdentifierNode Table { get; } = table;
		public BlockNode? Body { get; } = body;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return ForKeyword;
				yield return Cursor;
				yield return InKeyword;
				yield return Table;
				if (Body != null)
				{
					yield return Body;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
