using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class MethodDefinitionNode(
		KeywordNode keyword,
		KeywordNode? returnsKeyword,
		List<ReturnTypeNode>? returnTypes,
		IdentifierDeclarationNode name,
		List<ParameterNode>? parameters,
		BlockNode? body,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode Keyword { get; } = keyword;
		public KeywordNode? ReturnsKeyword { get; } = returnsKeyword;
		public List<ReturnTypeNode>? ReturnTypes { get; } = returnTypes;
		public IdentifierDeclarationNode Name { get; } = name;
		public List<ParameterNode>? Parameters { get; } = parameters;
		public BlockNode? Body { get; } = body;
		public string SymbolName { get; } = name.Type == IdentifierType.Trigger ?
					$"{keyword.Keyword} {name.Name}" : name.Name;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Keyword;
				yield return Name;
				if (Parameters != null)
				{
					foreach (var parameter in Parameters)
					{
						yield return parameter;
					}
				}
				if (ReturnsKeyword != null)
				{
					yield return ReturnsKeyword;
				}
				if (ReturnTypes != null)
				{
					foreach (var returnType in ReturnTypes)
					{
						yield return returnType;
					}
				}
				if (Body != null)
				{
					yield return Body;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
