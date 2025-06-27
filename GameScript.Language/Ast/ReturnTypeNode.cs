using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class ReturnTypeNode(
		TypeNode type,
		IdentifierDeclarationNode? name,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public TypeNode Type { get; } = type;
		public IdentifierDeclarationNode? Name { get; } = name;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Type;
				if (Name != null)
				{
					yield return Name;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
