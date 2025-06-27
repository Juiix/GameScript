using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class IfStatementNode(
		KeywordNode ifKeyword,
		ExpressionNode condition,
		BlockNode? ifBlock,
		List<ElseIfStatementNode>? elseIfNodes,
		KeywordNode? elseKeyword,
		BlockNode? elseBlock,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode IfKeyword { get; } = ifKeyword;
		public ExpressionNode Condition { get; } = condition;
		public BlockNode? IfBlock { get; } = ifBlock;
		public List<ElseIfStatementNode>? ElseIfNodes { get; } = elseIfNodes;
		public KeywordNode? ElseKeyword { get; } = elseKeyword;
		public BlockNode? ElseBlock { get; } = elseBlock;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return IfKeyword;
				yield return Condition;
				if (IfBlock != null)
				{
					yield return IfBlock;
				}
				if (ElseIfNodes != null)
				{
					foreach (var elseIfNode in ElseIfNodes)
					{
						yield return elseIfNode;
					}
				}
				if (ElseKeyword != null)
				{
					yield return ElseKeyword;
				}
				if (ElseBlock != null)
				{
					yield return ElseBlock;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
