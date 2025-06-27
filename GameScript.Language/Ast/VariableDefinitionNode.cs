using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class VariableDefinitionNode(
		TypeNode varType,
		List<(IdentifierDeclarationNode Name, ExpressionNode? Initializer)> vars,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public TypeNode VarType { get; } = varType;
		public List<(IdentifierDeclarationNode Name, ExpressionNode? Initializer)> Vars { get; } = vars;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return VarType;
				foreach (var (name, initializer) in Vars)
				{
					yield return name;
					if (initializer != null)
					{
						yield return initializer;
					}
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
