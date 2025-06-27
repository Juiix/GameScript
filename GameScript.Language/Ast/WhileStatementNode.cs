using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class WhileStatementNode(
		KeywordNode whileKeyword,
		ExpressionNode condition,
		BlockNode? body,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode WhileKeyword { get; } = whileKeyword;
		public ExpressionNode Condition { get; } = condition;
		public BlockNode? Body { get; } = body;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return WhileKeyword;
				yield return Condition;
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
