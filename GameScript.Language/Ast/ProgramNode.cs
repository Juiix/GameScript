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
			Constants = declarations?.OfType<ConstantDefinitionNode>().ToList();
			Contexts = declarations?.OfType<ContextDefinitionNode>().ToList();
			Types = declarations?.OfType<TypeDefinitionNode>().ToList();
		}

		/// <summary>Every top-level declaration (methods, tables, constants, contexts, types) in source order.</summary>
		public List<AstNode>? Declarations { get; }
		/// <summary>The func/command/trigger/handler declarations, in source order.</summary>
		public List<MethodDefinitionNode>? Methods { get; }
		/// <summary>The constant table declarations, in source order.</summary>
		public List<TableDefinitionNode>? Tables { get; }
		/// <summary>The '^constant' declarations, in source order.</summary>
		public List<ConstantDefinitionNode>? Constants { get; }
		/// <summary>The '@context' variable declarations, in source order.</summary>
		public List<ContextDefinitionNode>? Contexts { get; }
		/// <summary>The named-type ('type NAME : root') declarations, in source order.</summary>
		public List<TypeDefinitionNode>? Types { get; }

		public override IEnumerable<AstNode> Children => Declarations ?? [];

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
