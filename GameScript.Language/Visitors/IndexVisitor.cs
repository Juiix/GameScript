using System.Collections.Generic;
using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;

namespace GameScript.Language.Visitors
{
	public sealed class IndexVisitor(
		FileIndex fileIndex,
		VisitorContext context) : AstVisitorBase
	{
		private readonly FileIndex _fileIndex = fileIndex;
		private readonly VisitorContext _context = context;
		private readonly Dictionary<MethodDefinitionNode, LocalIndex> _localIndexes = [];
		private LocalIndex? _localIndex;

		public Dictionary<MethodDefinitionNode, LocalIndex> LocalIndexes => _localIndexes;

		public override void Visit(ConstantDefinitionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var symbol = new SymbolInfo(
				IdentifierType.Constant,
				node.Name.Name,
				_context.Types.GetType(node.Type.Name),
				null,
				null,
				null,
				node.Name.Summary,
				ParseLiteral(node.Initializer),
				_context.FilePath,
				node.Name.FileRange
			);

			_fileIndex.AddSymbol(symbol);
		}

		public override void Visit(ContextDefinitionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var symbol = new SymbolInfo(
				IdentifierType.Context,
				node.Name.Name,
				_context.Types.GetType(node.Type.Name),
				null,
				null,
				null,
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange
			);

			_fileIndex.AddSymbol(symbol);
		}

		public override void Visit(MethodDefinitionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			// For triggers, we compose the symbol name to include trigger type for uniqueness.
			var symbol = new SymbolInfo(
				node.Name.Type,
				node.SymbolName,
				_context.Types.GetTuple(node.ReturnTypes?.Select(x => x.Type.Name)),
				node.ReturnTypes?.Select(x => x.Name?.Name ?? string.Empty).ToList(),
				_context.Types.GetTuple(node.Parameters?.Select(x => x.Type.Name)),
				node.Parameters?.Select(x => x.Name.Name).ToList(),
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange,
				node.InternalName,
				node.Parameters?.Count(x => x.Default != null) ?? 0,
				node.Parameters?.Any(x => x.Default != null) == true
					? node.Parameters.Select(x => DefaultLabel(x.Default)).ToList()
					: null,
				isVariadic: node.IsVariadic
			);

			_fileIndex.AddSymbol(symbol);

			_localIndex = new LocalIndex(node.FilePath, node.FileRange);

			base.Visit(node);

			_localIndexes.Add(node, _localIndex);
			_localIndex = null;
		}

		public override void Visit(VariableDefinitionNode node)
		{
			// Build symbols for the declared variables.
			var varType = _context.Types.GetType(node.VarType.Name);
			foreach (var (varName, initializer) in node.Vars)
			{
				if (InvalidSymbolName(varName.Name))
					return;

				var varSymbol = new SymbolInfo(
					IdentifierType.Local,
					varName.Name,
					varType,
					null,
					null,
					null,
					varName.Summary,
					null,
					_context.FilePath,
					varName.FileRange
				);

				_localIndex?.AddSymbol(varSymbol);
				initializer?.Accept(this);
			}
		}

		public override void Visit(DeclarationExpressionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var varSymbol = new SymbolInfo(
				IdentifierType.Local,
				node.Name.Name,
				_context.Types.GetType(node.Type.Name),
				null,
				null,
				null,
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange
			);

			_localIndex?.AddSymbol(varSymbol);
		}

		public override void Visit(ParameterNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var paramSymbol = new SymbolInfo(
				node.Name.Type,
				node.Name.Name,
				_context.Types.GetType(node.Type.Name),
				null,
				null,
				null,
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange
			);

			_localIndex?.AddSymbol(paramSymbol);

			// index references inside the default value ('^const' identifiers)
			node.Default?.Accept(this);
		}

		public override void Visit(ForStatementNode node)
		{
			if (!InvalidSymbolName(node.Variable.Name))
			{
				// The first 'for' declares the symbol (and marks it as a loop
				// variable); later 'for' loops in the same method reuse it. A name
				// declared by anything else stays unmarked, so reuse of it errors
				// in symbol analysis.
				if (_localIndex?.GetSymbol(node.Variable.Name) == null)
				{
					_localIndex?.AddSymbol(new SymbolInfo(
						IdentifierType.Local,
						node.Variable.Name,
						_context.Types.GetType("int"),
						null,
						null,
						null,
						node.Variable.Summary,
						null,
						_context.FilePath,
						node.Variable.FileRange
					));
					_localIndex?.MarkLoopVariable(node.Variable.Name);
				}
			}

			node.Start.Accept(this);
			node.End.Accept(this);
			node.Body?.Accept(this);
		}

		public override void Visit(ForTableStatementNode node)
		{
			if (!InvalidSymbolName(node.Cursor.Name))
			{
				// Same first-declaration rule as 'for VAR in a..b'; the cursor's type
				// is the row type of the iterated table so 'r.col' can find it.
				if (_localIndex?.GetSymbol(node.Cursor.Name) == null)
				{
					_localIndex?.AddSymbol(new SymbolInfo(
						IdentifierType.Local,
						node.Cursor.Name,
						TableRowType.Create(node.Table.Name),
						null,
						null,
						null,
						node.Cursor.Summary,
						null,
						_context.FilePath,
						node.Cursor.FileRange
					));
					_localIndex?.MarkLoopVariable(node.Cursor.Name);
				}
			}

			// the table name is a reference (dependents re-analyze when the table changes)
			node.Table.Accept(this);
			node.Body?.Accept(this);
		}

		public override void Visit(TableDefinitionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var columns = new List<TableColumnInfo>();
			foreach (var column in node.Columns ?? [])
			{
				columns.Add(new TableColumnInfo(
					column.Name.Name,
					_context.Types.GetType(column.Type.Name) ?? new TypeInfo(column.Type.Name, TypeKind.Int),
					column.IsKey,
					_context.FilePath,
					column.Name.FileRange));
			}

			var rows = new List<IReadOnlyList<TableCell>>();
			foreach (var row in node.Rows ?? [])
				rows.Add(ToCells(row));

			var symbol = new SymbolInfo(
				IdentifierType.Table,
				node.Name.Name,
				null,
				null,
				null,
				null,
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange,
				columns: columns,
				rows: rows,
				defaultRow: node.DefaultRow != null ? ToCells(node.DefaultRow) : null
			);

			_fileIndex.AddSymbol(symbol);

			// index the '^' constant references inside the cells
			base.Visit(node);
		}

		private static List<TableCell> ToCells(TableRowNode row)
		{
			var cells = new List<TableCell>(row.Cells.Count);
			foreach (var cell in row.Cells)
			{
				// non-constant cells are reported by semantic analysis; keep the arity
				cells.Add(ConstantExpressions.ToTableCell(cell) ?? new TableCell(null, null));
			}
			return cells;
		}

		public override void Visit(IdentifierNode node)
		{
			// column names are resolved against their table, never against the symbol tables
			if (node.Type == IdentifierType.Column)
				return;

			// Check if the identifier has been declared in the current scope chain.
			var reference = new ReferenceInfo(
				node.Name,
				node.FilePath,
				node.FileRange
			);

			var symbol = _localIndex?.GetSymbol(node.Name);
			if (symbol != null)
			{
				_localIndex?.AddReference(reference);
			}
			else
			{
				_fileIndex.AddReference(reference);
			}
		}


		// A constant's compile-time value: literal or negated number literal; null
		// otherwise (semantic analysis reports the shape). Must never be a boxed
		// Value.Null — duplicate-key / duplicate-case detection compares these.
		private static object? ParseLiteral(ExpressionNode node) => ConstantExpressions.ParseInlineValue(node);
		private static bool InvalidSymbolName(string name) => name.StartsWith('?');

		// Display text for a parameter default ('""', '-1', '^anim_still'); null when absent.
		private static string? DefaultLabel(ExpressionNode? node)
		{
			return node switch
			{
				null => null,
				LiteralNode literal => literal.Value,
				UnaryExpressionNode { Operator: UnaryOperator.Negate, Operand: LiteralNode operand } => $"-{operand.Value}",
				IdentifierNode { Type: IdentifierType.Constant } identifier => $"^{identifier.Name}",
				_ => "?"
			};
		}
	}
}
