using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class CommentNode(
		string comment,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public string Comment { get; } = comment;

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
