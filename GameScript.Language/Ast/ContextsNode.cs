using GameScript.Language.File;
using GameScript.Language.Visitors;
using System.Collections.Generic;

namespace GameScript.Language.Ast
{
	public sealed class ContextsNode(
		List<ContextDefinitionNode>? definitions,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public List<ContextDefinitionNode>? Definitions { get; } = definitions;
		public override IEnumerable<AstNode> Children => Definitions ?? [];

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
