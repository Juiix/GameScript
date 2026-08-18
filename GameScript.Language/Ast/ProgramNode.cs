using System.Collections.Generic;
using System.Linq;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class ProgramNode : AstNode
	{
		public ProgramNode(
			List<AstNode>? declarations,
			string filePath,
			in FileRange fileRange) : base(filePath, in fileRange)
		{
			Declarations = declarations;
			Methods = declarations?.OfType<MethodDefinitionNode>().ToList();
			Tables = declarations?.OfType<TableDefinitionNode>().ToList();
		}

		/// <summary>Every top-level declaration (methods and tables) in source order.</summary>
		public List<AstNode>? Declarations { get; }
		/// <summary>The func/command/trigger/handler declarations, in source order.</summary>
		public List<MethodDefinitionNode>? Methods { get; }
		/// <summary>The constant table declarations, in source order.</summary>
		public List<TableDefinitionNode>? Tables { get; }

		public override IEnumerable<AstNode> Children => Declarations ?? [];

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
