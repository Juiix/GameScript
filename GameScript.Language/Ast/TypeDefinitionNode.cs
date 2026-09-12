using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// A top-level named-type declaration:
	//
	//   type NAME : int|string
	//
	// A named type is an alias over its root type: it widens to the root implicitly,
	// while the root (or another named type) must be cast to it with 'NAME(expr)'.
	// Named types erase at codegen. 'type' is contextual — only a top-level line of
	// this exact shape declares one; elsewhere 'type' is an ordinary identifier.
	public sealed class TypeDefinitionNode(
		KeywordNode keyword,
		IdentifierDeclarationNode name,
		OperatorNode colon,
		TypeNode underlying,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode Keyword { get; } = keyword;
		/// <summary>The declared type name (<see cref="IdentifierType.Type"/>); carries the doc summary.</summary>
		public IdentifierDeclarationNode Name { get; } = name;
		public OperatorNode Colon { get; } = colon;
		/// <summary>The root type the alias erases to ('int' or 'string').</summary>
		public TypeNode Underlying { get; } = underlying;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Keyword;
				yield return Name;
				yield return Colon;
				yield return Underlying;
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
