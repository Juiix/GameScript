using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class ElseIfStatementNode(
		KeywordNode elseKeyword,
		KeywordNode ifKeyword,
		ExpressionNode condition,
		BlockNode? block,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode ElseKeyword { get; } = elseKeyword;
		public KeywordNode IfKeyword { get; } = ifKeyword;
		public ExpressionNode Condition { get; } = condition;
		public BlockNode? Block { get; } = block;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return ElseKeyword;
				yield return IfKeyword;
				yield return Condition;
				if (Block != null)
				{
					yield return Block;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
