using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class AssignmentExpressionNode(
		ExpressionNode left,
		AssignmentOperator op,
		OperatorNode operatorNode,
		ExpressionNode right,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public ExpressionNode Left { get; } = left;
		public AssignmentOperator Operator { get; } = op;
		public OperatorNode OperatorNode { get; } = operatorNode;
		public ExpressionNode Right { get; } = right;
		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Left;
				yield return OperatorNode;
				yield return Right;
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
