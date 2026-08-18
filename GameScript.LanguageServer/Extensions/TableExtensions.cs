using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Parsing;

namespace GameScript.LanguageServer.Extensions
{
	/// <summary>
	/// Column-aware lookups for table syntax: given a Column-typed identifier
	/// under the cursor (a '.col' member, a '[col: k]' key selector, or the
	/// column's own declaration in a table header), find the owning table and
	/// the column's metadata.
	/// </summary>
	internal static class TableExtensions
	{
		/// <summary>
		/// The table symbol and column info a Column identifier refers to, or
		/// (null, null) when it cannot be resolved (unknown table / column).
		/// </summary>
		public static (SymbolInfo? Table, TableColumnInfo? Column) ResolveColumn(
			this RootFileData rootData,
			AstNode columnNode,
			AstNode? parent,
			LocalIndex? localIndex,
			ISymbolIndex symbols)
		{
			SymbolInfo? table = null;
			switch (parent)
			{
				case MemberExpressionNode member:
					table = TableAccess.ResolveTable(member.Target, localIndex, symbols);
					break;

				case IndexExpressionNode index:
					table = TableAccess.ResolveTable(index.Target, localIndex, symbols);
					break;

				case TableColumnNode:
					// the column's own declaration — find the enclosing table header
					if (rootData.Root is ProgramNode program && program.Tables != null)
					{
						foreach (var declaration in program.Tables)
						{
							if (declaration.FileRange.Contains(columnNode.FileRange.Start.Position))
							{
								table = TableAccess.GetTable(symbols, declaration.Name.Name);
								break;
							}
						}
					}
					break;
			}

			if (table?.Columns == null)
				return (table, null);

			var name = columnNode.GetSymbolNameIgnoringKind();
			foreach (var column in table.Columns)
			{
				if (column.Name == name)
					return (table, column);
			}
			return (table, null);
		}

		private static string? GetSymbolNameIgnoringKind(this AstNode node) => node switch
		{
			IdentifierNode i => i.Name,
			IdentifierDeclarationNode d => d.Name,
			_ => null,
		};

		public static bool IsColumnIdentifier(this AstNode node) => node switch
		{
			IdentifierNode { Type: IdentifierType.Column } => true,
			IdentifierDeclarationNode { Type: IdentifierType.Column } => true,
			_ => false,
		};

		/// <summary>Hover / detail text for a column: '[key] type name' plus its table.</summary>
		public static string ColumnSignature(this TableColumnInfo column) =>
			$"{(column.IsKey ? "key " : "")}{column.Type.Name} {column.Name}";
	}
}
