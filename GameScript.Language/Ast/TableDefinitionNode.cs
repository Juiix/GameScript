using System.Collections.Generic;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	// A top-level compile-time constant table:
	//
	//   table NAME([key] TYPE col, ...)
	//       cell, cell, ...          // one data row per line
	//       default: cell, cell, ... // optional, must be last
	//
	// Rows are constants only; every access lowers to a compare chain at compile
	// time (no runtime table exists).
	public sealed class TableDefinitionNode(
		KeywordNode keyword,
		IdentifierDeclarationNode name,
		List<TableColumnNode>? columns,
		List<TableRowNode>? rows,
		TableRowNode? defaultRow,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange)
	{
		public KeywordNode Keyword { get; } = keyword;
		public IdentifierDeclarationNode Name { get; } = name;
		public List<TableColumnNode>? Columns { get; } = columns;
		/// <summary>Data rows only — the default row is not included.</summary>
		public List<TableRowNode>? Rows { get; } = rows;
		public TableRowNode? DefaultRow { get; } = defaultRow;

		public override IEnumerable<AstNode> Children
		{
			get
			{
				yield return Keyword;
				yield return Name;
				if (Columns != null)
				{
					foreach (var column in Columns)
					{
						yield return column;
					}
				}
				if (Rows != null)
				{
					foreach (var row in Rows)
					{
						yield return row;
					}
				}
				if (DefaultRow != null)
				{
					yield return DefaultRow;
				}
			}
		}

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}
	}
}
