using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class CallExpressionNode(
		IdentifierNode functionName,
		List<ExpressionNode>? arguments,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public IdentifierNode FunctionName { get; } = functionName;
		public List<ExpressionNode>? Arguments { get; } = arguments;
		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return FunctionName;
				if (Arguments != null)
				{
					foreach (var arg in Arguments)
					{
						yield return arg;
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
