using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// One '[key] TYPE name' column of a table header. 'key' is contextual (not
	// a reserved word): it only means "lookup-able column" inside a table header.
	public sealed class TableColumnNode(
		KeywordNode? keyKeyword,
		TypeNode type,
		IdentifierDeclarationNode name,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode? KeyKeyword { get; } = keyKeyword;
		public TypeNode Type { get; } = type;
		public IdentifierDeclarationNode Name { get; } = name;

		public bool IsKey => KeyKeyword != null;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				if (KeyKeyword != null)
				{
					yield return KeyKeyword;
				}
				yield return Type;
				yield return Name;
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
