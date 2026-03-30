using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class IdentifierNode(
		string name,
		IdentifierType type,
		int dotPrefix,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public string Name { get; } = name;
		public IdentifierType Type { get; } = type;
		public int DotPrefix { get; } = dotPrefix;

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
