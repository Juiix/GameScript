using System.Collections.Generic;
using System.Linq;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class KeywordNode(
		string keyword,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public string Keyword { get; } = keyword;

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
