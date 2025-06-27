using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class ReturnStatementNode(
		KeywordNode returnKeyword,
		ExpressionNode? expression,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode ReturnKeyword { get; } = returnKeyword;
		public ExpressionNode? Expression { get; } = expression;
		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return ReturnKeyword;
				if (Expression != null)
				{
					yield return Expression;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
