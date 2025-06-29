using GameScript.Language.File;
using GameScript.Language.Visitors;
using System.Collections.Generic;

namespace GameScript.Language.Ast
{
	public sealed class ContextDefinitionNode(
		TypeNode type,
		IdentifierDeclarationNode name,
		OperatorNode operatorNode,
		ExpressionNode initializer,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public TypeNode Type { get; } = type;
		public IdentifierDeclarationNode Name { get; } = name;
		public OperatorNode OperatorNode { get; } = operatorNode;
		public ExpressionNode Initializer { get; } = initializer;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Type;
				yield return Name;
				yield return OperatorNode;
				yield return Initializer;
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
