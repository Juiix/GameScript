using System.Collections.Generic;
using System.Linq;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class TupleExpressionNode(
		List<ExpressionNode> elements,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public List<ExpressionNode> Elements { get; } = elements;
		public override IEnumerable<AstNode> Children => Elements;

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
