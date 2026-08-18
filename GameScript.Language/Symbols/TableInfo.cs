using System.Collections.Generic;
using System.Linq;
using GameScript.Language.File;
using GameScript.Language.Index;

namespace GameScript.Language.Symbols
{
	/// <summary>One column of a table symbol: its name, type, key flag and header range.</summary>
	public sealed record TableColumnInfo(string Name, TypeInfo Type, bool IsKey, string FilePath, FileRange Range);

	/// <summary>
	/// One cell of a table symbol as written: either an inline literal value
	/// (int / string / bool) or the name of a '^' constant. Cells are kept
	/// unresolved on the symbol because indexing runs per file (in parallel in
	/// the LSP) and the constant may live in a file that isn't indexed yet;
	/// <see cref="TableShape"/> resolves them at analysis time.
	/// </summary>
	public sealed record TableCell(object? Literal, string? ConstantName)
	{
		public bool IsConstant => ConstantName != null;

		/// <summary>The cell's compile-time value, or null when it cannot be resolved (unknown constant).</summary>
		public object? Resolve(ISymbolIndex symbols)
		{
			if (ConstantName == null)
				return Literal;
			foreach (var symbol in symbols.GetSymbols(ConstantName))
			{
				if (symbol.IdentifierType == Ast.IdentifierType.Constant)
					return symbol.LiteralValue;
			}
			return null;
		}
	}

	/// <summary>The type of a 'for r in table' row cursor.</summary>
	public static class TableRowType
	{
		private const string Prefix = "row of ";

		public static TypeInfo Create(string tableName) => new(Prefix + tableName, TypeKind.TableRow);

		public static bool IsRow(TypeInfo? type) => type?.Kind == TypeKind.TableRow;

		public static string? TryGetTableName(TypeInfo? type) =>
			IsRow(type) && type!.Name.StartsWith(Prefix) ? type.Name[Prefix.Length..] : null;
	}

	/// <summary>
	/// The resolved, analysis-time view of a table symbol: cell values, the key
	/// width, and the uniqueness facts the compiler and diagnostics need.
	/// </summary>
	public sealed class TableShape
	{
		public SymbolInfo Table { get; }
		public IReadOnlyList<TableColumnInfo> Columns { get; }
		/// <summary>Resolved data-row cells (null cells are unresolvable constants).</summary>
		public IReadOnlyList<object?[]> Rows { get; }
		public object?[]? DefaultRow { get; }
		/// <summary>
		/// Smallest N such that the leading-N tuples are unique across data rows.
		/// 0 when there is only one row (or none); -1 when two rows are identical
		/// (see <see cref="DuplicateRowIndex"/>).
		/// </summary>
		public int KeyWidth { get; }
		/// <summary>Index of the first data row that duplicates an earlier row in full, or -1.</summary>
		public int DuplicateRowIndex { get; } = -1;
		/// <summary>Per 'key' column: index of the first row whose value repeats an earlier row's, or -1.</summary>
		public IReadOnlyDictionary<int, int> DuplicateKeyRows { get; }
		/// <summary>True when every cell resolved (no unknown constants).</summary>
		public bool IsFullyResolved { get; }

		public int RowCount => Rows.Count;

		/// <summary>Fewest positional key arguments a lookup must pass (never less than one).</summary>
		public int MinKeyArgs => KeyWidth < 1 ? 1 : KeyWidth;

		private TableShape(SymbolInfo table, ISymbolIndex symbols)
		{
			Table = table;
			Columns = table.Columns ?? [];

			var rows = new List<object?[]>();
			var fullyResolved = true;
			foreach (var row in table.Rows ?? [])
			{
				var cells = new object?[Columns.Count];
				for (int c = 0; c < Columns.Count && c < row.Count; c++)
				{
					cells[c] = row[c].Resolve(symbols);
					fullyResolved &= cells[c] != null;
				}
				rows.Add(cells);
			}
			Rows = rows;
			IsFullyResolved = fullyResolved;

			if (table.DefaultRow != null)
			{
				DefaultRow = new object?[Columns.Count];
				for (int c = 0; c < Columns.Count && c < table.DefaultRow.Count; c++)
					DefaultRow[c] = table.DefaultRow[c].Resolve(symbols);
			}

			// key width: smallest prefix that is unique across all rows
			KeyWidth = 0;
			if (rows.Count > 1)
			{
				KeyWidth = -1;
				for (int n = 1; n <= Columns.Count; n++)
				{
					if (PrefixIsUnique(rows, n, out _))
					{
						KeyWidth = n;
						break;
					}
				}
				if (KeyWidth < 0)
				{
					PrefixIsUnique(rows, Columns.Count, out var dup);
					DuplicateRowIndex = dup;
				}
			}

			// each explicit key column must be unique on its own
			var duplicateKeys = new Dictionary<int, int>();
			for (int c = 0; c < Columns.Count; c++)
			{
				if (!Columns[c].IsKey)
					continue;
				var seen = new HashSet<object>();
				for (int r = 0; r < rows.Count; r++)
				{
					var v = rows[r][c];
					if (v != null && !seen.Add(v))
					{
						duplicateKeys[c] = r;
						break;
					}
				}
			}
			DuplicateKeyRows = duplicateKeys;
		}

		public static TableShape Resolve(SymbolInfo table, ISymbolIndex symbols) => new(table, symbols);

		public int IndexOfColumn(string name)
		{
			for (int i = 0; i < Columns.Count; i++)
			{
				if (Columns[i].Name == name)
					return i;
			}
			return -1;
		}

		/// <summary>
		/// True when the column may be used as a '[name: k]' lookup key: it is
		/// marked 'key', or it is column 0 and the key width is 1.
		/// </summary>
		public bool IsLookupColumn(int column) =>
			column >= 0 && column < Columns.Count &&
			(Columns[column].IsKey || (column == 0 && KeyWidth <= 1));

		/// <summary>The data row whose given columns equal the given values, or -1.</summary>
		public int FindRow(IReadOnlyList<int> keyColumns, IReadOnlyList<object?> keyValues)
		{
			for (int r = 0; r < Rows.Count; r++)
			{
				var match = true;
				for (int i = 0; i < keyColumns.Count && match; i++)
				{
					var cell = Rows[r][keyColumns[i]];
					match = cell != null && cell.Equals(keyValues[i]);
				}
				if (match)
					return r;
			}
			return -1;
		}

		private static bool PrefixIsUnique(List<object?[]> rows, int width, out int duplicateRow)
		{
			duplicateRow = -1;
			var seen = new HashSet<string>();
			for (int r = 0; r < rows.Count; r++)
			{
				var key = EncodeTuple(rows[r], width);
				if (!seen.Add(key))
				{
					duplicateRow = r;
					return false;
				}
			}
			return true;
		}

		// length-prefixed so that no two distinct tuples share an encoding
		private static string EncodeTuple(object?[] row, int width)
		{
			var sb = new System.Text.StringBuilder();
			for (int i = 0; i < width && i < row.Length; i++)
			{
				var text = row[i] switch
				{
					null => "?",
					string s => "s" + s,
					bool b => b ? "b1" : "b0",
					var v => "i" + v,
				};
				sb.Append(text.Length).Append(':').Append(text);
			}
			return sb.ToString();
		}
	}
}
