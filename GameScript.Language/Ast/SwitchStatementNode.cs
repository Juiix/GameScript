using System.Collections.Generic;
using System.Linq;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class SwitchStatementNode(
		KeywordNode switchKeyword,
		ExpressionNode subject,
		List<SwitchCaseNode>? cases,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode SwitchKeyword { get; } = switchKeyword;
		public ExpressionNode Subject { get; } = subject;
		public List<SwitchCaseNode>? Cases { get; } = cases;

		public SwitchCaseNode? DefaultCase => Cases?.FirstOrDefault(x => x.IsDefault);

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return SwitchKeyword;
				yield return Subject;
				if (Cases != null)
				{
					foreach (SwitchCaseNode caseNode in Cases)
					{
						yield return caseNode;
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
