using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class ForStatementNode(
		KeywordNode forKeyword,
		IdentifierDeclarationNode variable,
		KeywordNode inKeyword,
		ExpressionNode start,
		OperatorNode rangeOperator,
		ExpressionNode end,
		BlockNode? body,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode ForKeyword { get; } = forKeyword;
		public IdentifierDeclarationNode Variable { get; } = variable;
		public KeywordNode InKeyword { get; } = inKeyword;
		public ExpressionNode Start { get; } = start;
		public OperatorNode RangeOperator { get; } = rangeOperator;
		public ExpressionNode End { get; } = end;
		public BlockNode? Body { get; } = body;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return ForKeyword;
				yield return Variable;
				yield return InKeyword;
				yield return Start;
				yield return RangeOperator;
				yield return End;
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
