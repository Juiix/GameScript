using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// A keyed table lookup: 't[k]', 't[a, b]' (leading columns positionally) or
	// 't[name: k]' (a named key column). Yields a row, which must be consumed by a
	// '.column' member access — it is never a value by itself.
	public sealed class IndexExpressionNode(
		ExpressionNode target,
		IdentifierNode? keyColumn,
		List<ExpressionNode> arguments,
		string filePath,
		in FileRange fileRange) : ExpressionNode(filePath, in fileRange)
	{
		public ExpressionNode Target { get; } = target;
		/// <summary>The 'name' of a 't[name: k]' lookup; null for positional lookups.</summary>
		public IdentifierNode? KeyColumn { get; } = keyColumn;
		public List<ExpressionNode> Arguments { get; } = arguments;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Target;
				if (KeyColumn != null)
				{
					yield return KeyColumn;
				}
				foreach (var arg in Arguments)
				{
					yield return arg;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
