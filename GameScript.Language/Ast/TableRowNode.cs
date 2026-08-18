using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// One row of a table body: comma-separated constant cells on a single line.
	// The 'default:' row (DefaultKeyword != null) supplies per-column fallback
	// values for missing keys / out-of-range positions; it is not a data row.
	public sealed class TableRowNode(
		KeywordNode? defaultKeyword,
		List<ExpressionNode> cells,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode? DefaultKeyword { get; } = defaultKeyword;
		public List<ExpressionNode> Cells { get; } = cells;

		public bool IsDefault => DefaultKeyword != null;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				if (DefaultKeyword != null)
				{
					yield return DefaultKeyword;
				}
				foreach (var cell in Cells)
				{
					yield return cell;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
