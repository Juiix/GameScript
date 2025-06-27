using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class ProgramNode(
		List<MethodDefinitionNode>? methods,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public List<MethodDefinitionNode>? Methods { get; } = methods;
		public override IEnumerable<AstNode> Children => Methods ?? [];

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
