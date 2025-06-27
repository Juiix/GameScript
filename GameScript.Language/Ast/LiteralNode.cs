using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class LiteralNode(
		LiteralType type,
		string value,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public LiteralType Type { get; } = type;
		public string Value { get; } = value;

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
