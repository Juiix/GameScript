using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class PostfixExpressionNode(
		ExpressionNode operand,
		UnaryOperator op,
		OperatorNode operatorNode,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public ExpressionNode Operand { get; } = operand;
		public UnaryOperator Operator { get; } = op;
		public OperatorNode OperatorNode { get; } = operatorNode;
		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Operand;
				yield return OperatorNode;
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
