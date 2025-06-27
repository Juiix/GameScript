using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class ConstantsNode(
		List<ConstantDefinitionNode>? definitions,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public List<ConstantDefinitionNode>? Definitions { get; } = definitions;
		public override IEnumerable<AstNode> Children => Definitions ?? [];

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
