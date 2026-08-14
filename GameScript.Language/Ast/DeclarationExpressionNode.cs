using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	/// <summary>
	/// An inline local declaration inside a destructuring tuple, e.g. the elements of
	/// <c>(bool $ok, string $err) = ~send_login(...)</c>. Declares a new local and
	/// receives the corresponding tuple value.
	/// </summary>
	public sealed class DeclarationExpressionNode(
		TypeNode type,
		IdentifierDeclarationNode name,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public TypeNode Type { get; } = type;
		public IdentifierDeclarationNode Name { get; } = name;

		public override IEnumerable<AstNode> Children
		{
			get
			{
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
