using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// A single 'case v1, v2:' or 'default:' arm of a switch statement.
	// Values is null for the default arm. Inline bodies ('case x: stmt') are
	// normalized into a synthetic single-statement BlockNode (IsInline == true).
	public sealed class SwitchCaseNode(
		KeywordNode keyword,
		List<ExpressionNode>? values,
		BlockNode? body,
		bool isInline,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode Keyword { get; } = keyword;
		public List<ExpressionNode>? Values { get; } = values;
		public BlockNode? Body { get; } = body;
		public bool IsInline { get; } = isInline;

		public bool IsDefault => Values == null;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Keyword;
				if (Values != null)
				{
					foreach (ExpressionNode value in Values)
					{
						yield return value;
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
