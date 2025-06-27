using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class UnparsableExpressionNode(
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
