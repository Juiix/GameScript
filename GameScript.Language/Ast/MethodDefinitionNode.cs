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
		in FileRange fileRange,
		OperatorNode? bindingOperator = null,
		IdentifierDeclarationNode? bindingName = null,
		bool isVariadic = false) : AstNode(filePath, in fileRange)
	{
		public KeywordNode Keyword { get; } = keyword;

		/// <summary>
		/// Trigger declarations only: declared as 'trigger NAME(...)'. Handlers of a variadic
		/// trigger declare their own int/string/bool parameters, which the host binds by position.
		/// </summary>
		public bool IsVariadic { get; } = isVariadic;
		public KeywordNode? ReturnsKeyword { get; } = returnsKeyword;
		public List<ReturnTypeNode>? ReturnTypes { get; } = returnTypes;
		public IdentifierDeclarationNode Name { get; } = name;
		public List<ParameterNode>? Parameters { get; } = parameters;
		public BlockNode? Body { get; } = body;
		public string SymbolName { get; } = name.Type == IdentifierType.Trigger ?
					$"{keyword.Keyword} {name.Name}" : name.Name;

		/// <summary>The '=' of a command's '= engine_op' binding, if present.</summary>
		public OperatorNode? BindingOperator { get; } = bindingOperator;

		/// <summary>The engine-op name node of a command's '= engine_op' binding, if present.</summary>
		public IdentifierDeclarationNode? BindingName { get; } = bindingName;

		/// <summary>Engine-op name from a command's '= name' binding, if present.</summary>
		public string? InternalName { get; } = bindingName?.Name;

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
				if (BindingOperator != null)
				{
					yield return BindingOperator;
				}
				if (BindingName != null)
				{
					yield return BindingName;
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
