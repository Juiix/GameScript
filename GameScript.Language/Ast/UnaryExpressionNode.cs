using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class UnaryExpressionNode(
		UnaryOperator op,
		OperatorNode operatorNode,
		ExpressionNode operand,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public UnaryOperator Operator { get; } = op;
		public OperatorNode OperatorNode { get; } = operatorNode;
		public ExpressionNode Operand { get; } = operand;
		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return OperatorNode;
				yield return Operand;
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
