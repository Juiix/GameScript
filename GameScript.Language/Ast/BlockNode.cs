using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class BlockNode(
		List<AstNode>? statements,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public List<AstNode>? Statements { get; } = statements;
		public override IEnumerable<AstNode> Children => Statements ?? [];

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
