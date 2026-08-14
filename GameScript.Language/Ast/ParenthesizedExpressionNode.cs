using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	/// <summary>
	/// A grouping expression '(expr)'. Semantically transparent — it exists so the
	/// grammar can reject full-wrap parentheses around if/while conditions while
	/// still allowing grouping inside larger expressions.
	/// </summary>
	public sealed class ParenthesizedExpressionNode(
		ExpressionNode inner,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public ExpressionNode Inner { get; } = inner;

		public override IEnumerable<AstNode> Children
		{
			get { yield return Inner; }
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
