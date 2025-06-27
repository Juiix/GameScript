using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class OperatorNode(
		string @operator,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public string Operator { get; } = @operator;

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
