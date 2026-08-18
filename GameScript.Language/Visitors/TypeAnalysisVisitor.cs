using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Language.Visitors
{
	public sealed class TypeAnalysisVisitor(
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes,
		VisitorContext context) : AnalysisVisitorBase(localIndexes)
	{
		private readonly VisitorContext _context = context;
		private readonly InferredTypeVisitor _inferredTypeVisitor = new(context);

		/// <summary>
		/// The overload chosen for each call site. Populated during analysis and
		/// consumed by the bytecode compiler so codegen never re-derives argument
		/// types (see BytecodeCompiler's resolution input).
		/// </summary>
		public Dictionary<CallExpressionNode, SymbolInfo> ResolvedCalls { get; } = [];

		public override void Visit(VariableDefinitionNode node)
		{
			base.Visit(node);

			var varType = _context.Types.GetType(node.VarType.Name);
			foreach (var (varName, initializer) in node.Vars)
			{
				if (initializer != null)
				{
					var initializerType = GetInferredType(initializer);
					if (initializerType != null &&
						varType != null &&
						!initializerType.Equals(varType))
					{
						Error($"Type mismatch, cannot assign '{initializerType}' to '{varType}'", FileRange.Combine(varName.FileRange, initializer.FileRange));
					}
				}
			}
		}

		public override void Visit(ParameterNode node)
		{
			base.Visit(node);

			if (node.Default == null)
			{
				return;
			}

			var paramType = _context.Types.GetType(node.Type.Name);
			var defaultType = GetInferredType(node.Default);
			if (paramType != null &&
				defaultType != null &&
				!defaultType.Equals(paramType))
			{
				Error($"Type mismatch, cannot assign '{defaultType}' default to '{paramType}' parameter '{node.Name.Name}'", node.Default);
			}
		}

		public override void Visit(ConstantDefinitionNode node)
		{
			base.Visit(node);

			var constantType = _context.Types.GetType(node.Type.Name);
			var initializerType = GetInferredType(node.Initializer);
			if (initializerType != null &&
				constantType != null &&
				!initializerType.Equals(constantType))
			{
				Error($"Type mismatch, cannot assign '{initializerType}' to '{constantType}'", node.Initializer);
			}
		}

		public override void Visit(ReturnStatementNode node)
		{
			base.Visit(node);

			if (Method == null)
			{
				return;
			}

			var returnType = _context.Types.GetTuple(Method.ReturnTypes?.Select(x => x.Type.Name));
			if (Method.ReturnTypes == null)
			{
				if (node.Expression != null)
				{
					Error($"{Method.Name.Type} has no return type declared.", node);
				}
			}
			else if (returnType != null)
			{
				if (node.Expression == null)
				{
					Error($"{Method.Name.Type} must return '{returnType}'", node);
				}
				else
				{
					var expressionType = GetInferredType(node.Expression);
					if (expressionType != null &&
						!expressionType.Equals(returnType))
					{
						Error($"Cannot return '{expressionType}', expected '{returnType}'", node);
					}
				}
			}
		}

		public override void Visit(IfStatementNode node)
		{
			base.Visit(node);

			ConditionExpressionCheck(node.Condition);

			if (node.ElseIfNodes != null)
			{
				foreach (var elseIf in node.ElseIfNodes)
				{
					ConditionExpressionCheck(elseIf.Condition);
				}
			}
		}

		public override void Visit(WhileStatementNode node)
		{
			base.Visit(node);

			ConditionExpressionCheck(node.Condition);
		}

		public override void Visit(ForStatementNode node)
		{
			base.Visit(node);

			var startType = GetInferredType(node.Start);
			if (startType != null && startType.Kind != TypeKind.Int)
			{
				Error("For-loop range bounds must be 'int'", node.Start);
			}

			var endType = GetInferredType(node.End);
			if (endType != null && endType.Kind != TypeKind.Int)
			{
				Error("For-loop range bounds must be 'int'", node.End);
			}
		}

		public override void Visit(SwitchStatementNode node)
		{
			base.Visit(node);

			var subjectType = GetInferredType(node.Subject);
			if (subjectType != null &&
				subjectType.Kind is not (TypeKind.Int or TypeKind.String or TypeKind.Bool))
			{
				Error("Switch subject must be an 'int', 'string' or 'bool' expression", node.Subject);
				subjectType = null;    // suppress per-case mismatch noise
			}

			if (node.Cases == null)
				return;

			HashSet<object>? seenValues = null;
			foreach (var caseNode in node.Cases)
			{
				if (caseNode.Values == null)
					continue;

				foreach (var value in caseNode.Values)
				{
					var valueType = GetInferredType(value);
					if (subjectType != null &&
						valueType != null &&
						!valueType.Equals(subjectType))
					{
						Error($"Case value type '{valueType}' does not match the switch subject type '{subjectType}'", value);
						continue;
					}

					// duplicate detection over compile-time values; values that
					// failed the constness check simply don't extract
					var constant = ExtractConstantValue(value);
					if (constant == null)
						continue;
					if (!(seenValues ??= []).Add(constant))
					{
						Error($"Duplicate case value '{DisplayConstant(constant)}'", value);
					}
				}
			}
		}

		// Compile-time value of a case expression: literal, negated number literal,
		// or a '^' constant's indexed literal value. Null when not extractable.
		private object? ExtractConstantValue(ExpressionNode node) => ConstantExpressions.Evaluate(node, _context.Symbols);

		private static string DisplayConstant(object value) =>
			value is bool b ? (b ? "true" : "false") : value.ToString() ?? "?";

		// ---------------------------------------------------------------
		// tables
		// ---------------------------------------------------------------

		private readonly Dictionary<SymbolInfo, TableShape> _shapes = [];

		private TableShape GetShape(SymbolInfo table)
		{
			if (!_shapes.TryGetValue(table, out var shape))
			{
				shape = TableShape.Resolve(table, _context.Symbols);
				_shapes[table] = shape;
			}
			return shape;
		}

		public override void Visit(TableDefinitionNode node)
		{
			base.Visit(node);    // column TypeNodes ('Undefined type'), cell identifiers

			// find this declaration's own symbol (a same-named table elsewhere is a
			// separate diagnostic)
			SymbolInfo? self = null;
			foreach (var symbol in _context.Symbols.GetSymbols(node.Name.Name))
			{
				if (symbol.IsTable &&
					symbol.FilePath.Equals(node.Name.FilePath) &&
					symbol.FileRange == node.Name.FileRange)
				{
					self = symbol;
					break;
				}
			}
			if (self == null)
				return;

			var shape = GetShape(self);
			var columns = node.Columns ?? [];
			var rows = node.Rows ?? [];

			// cell types
			for (int r = 0; r < rows.Count; r++)
				RowTypeCheck(node, rows[r], r, columns);
			if (node.DefaultRow != null)
				RowTypeCheck(node, node.DefaultRow, -1, columns);

			// key uniqueness
			if (shape.DuplicateRowIndex >= 0 && shape.DuplicateRowIndex < rows.Count)
			{
				Error($"Duplicate row: row {shape.DuplicateRowIndex + 1} of table '{node.Name.Name}' repeats an earlier row.", rows[shape.DuplicateRowIndex]);
			}
			foreach (var (column, row) in shape.DuplicateKeyRows)
			{
				if (row < rows.Count && column < rows[row].Cells.Count)
				{
					var value = ConstantExpressions.Display(shape.Rows[row][column]);
					Error($"Duplicate key {value} in 'key' column '{columns[column].Name.Name}' of table '{node.Name.Name}'.", rows[row].Cells[column]);
				}
			}
		}

		private void RowTypeCheck(TableDefinitionNode table, TableRowNode row, int rowIndex, List<TableColumnNode> columns)
		{
			var label = rowIndex < 0 ? "the default row" : $"row {rowIndex + 1}";
			for (int c = 0; c < row.Cells.Count && c < columns.Count; c++)
			{
				var columnType = _context.Types.GetType(columns[c].Type.Name);
				var cellType = GetInferredType(row.Cells[c]);
				if (columnType != null && cellType != null && !cellType.Equals(columnType))
				{
					Error($"Cell {c + 1} of {label} in table '{table.Name.Name}' is '{cellType}' but column '{columns[c].Name.Name}' is '{columnType}'.", row.Cells[c]);
				}
			}
		}

		public override void Visit(ForTableStatementNode node)
		{
			base.Visit(node);

			if (node.Table.Type != IdentifierType.Table)
			{
				// unknown names are reported by the semantic pass
				if (node.Table.Type != IdentifierType.Unknown)
					Error($"'{node.Table.Name}' is not a table; 'for {node.Cursor.Name} in ...' iterates a table.", node.Table);
			}
		}

		public override void Visit(IndexExpressionNode node)
		{
			base.Visit(node);

			if (!TableAccess.IsTableTarget(node.Target))
			{
				if (node.Target is not IdentifierNode { Type: IdentifierType.Unknown })
					Error(node.Target is IdentifierNode target
						? $"'{target.Name}' is not a table and cannot be indexed."
						: "Only a table can be indexed with '[ ]'.", node.Target);
				return;
			}

			var table = TableAccess.ResolveTable(node.Target, LocalIndex, _context.Symbols);
			if (table == null)
				return;

			CheckKeyArguments(GetShape(table), node.KeyColumn, node.Arguments, node);
		}

		public override void Visit(MemberExpressionNode node)
		{
			base.Visit(node);

			var member = node.Member.Name;

			if (node.IsBuiltin)
			{
				if (!TableAccess.IsTableTarget(node.Target))
				{
					if (node.Target is not IdentifierNode { Type: IdentifierType.Unknown })
						Error($"'.{member}' is only valid on a table (t.{member}{(node.IsCount ? "" : "(...)")}).", node.Target);
					return;
				}

				var table = TableAccess.ResolveTable(node.Target, LocalIndex, _context.Symbols);
				if (table == null)
					return;
				var shape = GetShape(table);

				if (node.IsCount)
				{
					if (node.Arguments != null)
						Error($"'count' is a property; write {table.Name}.count without parentheses.", node);
					if (node.KeyColumn != null)
						Error("'count' takes no key.", node.KeyColumn);
					return;
				}

				if (node.IsHas)
				{
					if (node.Arguments == null)
						Error($"'has' takes the key(s) to test: {table.Name}.has(key)", node);
					else
						CheckKeyArguments(shape, node.KeyColumn, node.Arguments, node);
					return;
				}

				// at
				if (node.KeyColumn != null)
					Error("'at' takes a row index, not a key column.", node.KeyColumn);
				if (node.Arguments == null || node.Arguments.Count != 1)
				{
					Error($"'at' takes one row index: {table.Name}.at(i)", node);
				}
				else
				{
					var indexType = GetInferredType(node.Arguments[0]);
					if (indexType != null && indexType.Kind != TypeKind.Int)
						Error($"'at' takes an 'int' row index, not '{indexType}'.", node.Arguments[0]);
				}
				return;
			}

			// a column read: the target must be a row (lookup, at, or cursor)
			if (!TableAccess.IsRowTarget(node.Target, LocalIndex))
			{
				if (TableAccess.IsTableTarget(node.Target))
				{
					var t = TableAccess.ResolveTable(node.Target, LocalIndex, _context.Symbols);
					Error($"Select a row before a column: {t?.Name ?? "t"}[key].{member} or {t?.Name ?? "t"}.at(i).{member}", node.Member);
				}
				else if (node.Target is not IdentifierNode { Type: IdentifierType.Unknown })
				{
					Error("Member access ('.name') is only valid on a table or a table row.", node.Member);
				}
				return;
			}

			var rowTable = TableAccess.ResolveTable(node.Target, LocalIndex, _context.Symbols);
			if (rowTable == null)
				return;

			if (GetShape(rowTable).IndexOfColumn(member) < 0)
			{
				Error($"Table '{rowTable.Name}' has no column '{member}'.", node.Member);
				return;
			}
			if (node.Arguments != null)
				Error($"Column '{member}' is a value, not a call; write .{member} without parentheses.", node);
			if (node.KeyColumn != null)
				Error("A column read takes no key.", node.KeyColumn);
		}

		// Validates the key(s) of 't[...]' / 't.has(...)': named or positional,
		// arity against the key width, per-key type, and (for all-constant keys)
		// warns when no row can match.
		private void CheckKeyArguments(TableShape shape, IdentifierNode? keyColumn, List<ExpressionNode> args, AstNode site)
		{
			var tableName = shape.Table.Name;
			var keyColumns = new List<int>();

			if (keyColumn != null)
			{
				var column = shape.IndexOfColumn(keyColumn.Name);
				if (column < 0)
				{
					Error($"Table '{tableName}' has no column '{keyColumn.Name}'.", keyColumn);
					return;
				}
				if (!shape.IsLookupColumn(column))
				{
					Error($"'{keyColumn.Name}' is not a key column of table '{tableName}'; mark it 'key' in the table header to look it up.", keyColumn);
					return;
				}
				if (args.Count != 1)
				{
					Error($"A named lookup takes exactly one key: {tableName}[{keyColumn.Name}: value]", site);
					return;
				}
				keyColumns.Add(column);
			}
			else
			{
				if (args.Count == 0)
					return;    // the parser already reported the empty '[ ]'
				if (args.Count < shape.MinKeyArgs)
				{
					Error($"Lookup on '{tableName}' needs at least {shape.MinKeyArgs} key(s): its first {shape.MinKeyArgs} columns form the key.", site);
					return;
				}
				if (args.Count > shape.Columns.Count)
				{
					Error($"Lookup on '{tableName}' passes {args.Count} key(s) but the table has only {shape.Columns.Count} column(s).", site);
					return;
				}
				for (int i = 0; i < args.Count; i++)
					keyColumns.Add(i);
			}

			var allConstant = true;
			var values = new List<object?>();
			for (int i = 0; i < keyColumns.Count; i++)
			{
				var column = shape.Columns[keyColumns[i]];
				var argType = GetInferredType(args[i]);
				if (argType != null && !argType.Equals(column.Type))
					Error($"Key {i + 1} of '{tableName}' must be '{column.Type}' (column '{column.Name}'), not '{argType}'.", args[i]);

				var value = ConstantExpressions.IsConstant(args[i]) ? ExtractConstantValue(args[i]) : null;
				allConstant &= value != null;
				values.Add(value);
			}

			if (allConstant && shape.IsFullyResolved && shape.DefaultRow == null &&
				shape.FindRow(keyColumns, values) < 0)
			{
				var shown = string.Join(", ", values.Select(ConstantExpressions.Display));
				Warning($"No row of '{tableName}' has key ({shown}); this lookup always yields the zero value.", site.FileRange);
			}
		}

		public override void Visit(BinaryExpressionNode node)
		{
			base.Visit(node);

			var leftType = GetInferredType(node.Left);
			var rightType = GetInferredType(node.Right);
			if (leftType == null || rightType == null)
			{
				return; // type check error
			}

			if ((node.Operator & BinaryOperator.Logical) != BinaryOperator.Unknown)
			{
				if (leftType.Kind != TypeKind.Bool)
					Error($"Left operand of '{node.OperatorNode.Operator}' must be 'bool' type.", node.Left);
				if (rightType.Kind != TypeKind.Bool)
					Error($"Right operand of '{node.OperatorNode.Operator}' must be 'bool' type.", node.Right);
				return;
			}

			var isStringAdd = node.Operator == BinaryOperator.Add &&
				(leftType.Kind == TypeKind.String || rightType.Kind == TypeKind.String);

			if (!isStringAdd && leftType != rightType)
			{
				Error($"Type mismatch, cannot operate '{leftType}' and '{rightType}'", node);
			}

			if (!isStringAdd &&
				(node.Operator & BinaryOperator.Relational) == BinaryOperator.Unknown &&
				leftType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}

		// For unary expressions, the inferred type is usually that of the operand.
		public override void Visit(UnaryExpressionNode node)
		{
			base.Visit(node);

			var operandType = GetInferredType(node.Operand);
			if (node.Operator == UnaryOperator.Not &&
				operandType?.Kind != TypeKind.Bool)
			{
				Error("'!' operator can only be used on 'bool' types.", node);
			}

			if ((node.Operator & UnaryOperator.Numeric) != UnaryOperator.Unknown &&
				operandType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}

		// For assignment expressions, we set the InferredType to that of the left-hand side.
		public override void Visit(AssignmentExpressionNode node)
		{
			base.Visit(node);

			var leftType = GetInferredType(node.Left);
			var rightType = GetInferredType(node.Right);
			if (leftType == null || rightType == null)
			{
				return; // type check error
			}

			var isStringAdd = node.Operator == AssignmentOperator.Add &&
				leftType?.Kind == TypeKind.String;

			if (!isStringAdd && leftType != rightType)
			{
				Error($"Type mismatch, cannot operate '{leftType}' and '{rightType}'", node);
			}

			if (!isStringAdd &&
				node.Operator != AssignmentOperator.Assign &&
				leftType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}
		// For call expressions, look up the function symbol for the function name,
		// then assign the return type from the function signature.
		public override void Visit(CallExpressionNode node)
		{
			base.Visit(node);

			var name = node.FunctionName.Name;
			var argTypes = node.Arguments?.Select(GetInferredType).ToList() ?? [];
			var resolved = _context.Symbols.ResolveCallable(name, argTypes, out var status);
			switch (status)
			{
				case CallableResolutionStatus.NotFound:
					// unknown symbols are reported by the semantic pass
					break;

				case CallableResolutionStatus.Match:
					ResolvedCalls[node] = resolved!;
					break;

				case CallableResolutionStatus.Ambiguous:
					var viaDefaults = _context.Symbols.GetSymbols(name).Any(x =>
						x.IsCallable() && x.DefaultCount > 0 &&
						argTypes.Count >= x.RequiredArity && argTypes.Count < x.Arity);
					Error(viaDefaults
						? $"Ambiguous call to '{name}': omitted defaulted parameters make more than one overload applicable. Pass the arguments explicitly."
						: $"Ambiguous call to '{name}': multiple overloads match.", node);
					break;

				case CallableResolutionStatus.NoOverloadMatches:
					var candidates = _context.Symbols.GetSymbols(name).Where(x => x.IsCallable()).ToList();
					if (candidates.Count == 1)
					{
						// legacy single-symbol diagnostics
						var symbol = candidates[0];
						if (symbol.ParamTypes != null)
						{
							if (node.Arguments != null)
							{
								var argumentType = GetInferredType(node.Arguments);
								Error($"Type mismatch, cannot call '{symbol.Name}{symbol.ParamSignature}' with '{argumentType}'", node.Arguments);
							}
							else
							{
								Error($"Missing arguments {symbol.ParamSignature}.", node);
							}
						}
						else if (node.Arguments != null)
						{
							Error($"{symbol.IdentifierType} {symbol.Name} does not require arguments.", node.Arguments);
						}
					}
					else
					{
						var argSignature = $"({string.Join(",", argTypes.Select(x => x?.Name ?? "?"))})";
						Error($"No overload of '{name}' matches {argSignature}.", node);
					}
					break;
			}
		}

		// For postfix and other expressions, if no specific handling is required,
		// the default visiting of children will suffice.
		public override void Visit(PostfixExpressionNode node)
		{
			base.Visit(node);

			var operandType = GetInferredType(node.Operand);
			if ((node.Operator & UnaryOperator.Numeric) != UnaryOperator.Unknown &&
				operandType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}

		public override void Visit(TypeNode node)
		{
			var type = _context.Types.GetType(node.Name);
			if (type == null &&
				!node.Name.StartsWith('?'))
			{
				Error($"Undefined type '{node}'", node);
			}
		}

		private void ConditionExpressionCheck(ExpressionNode expression)
		{
			var conditionType = GetInferredType(expression);
			if (conditionType?.Kind != TypeKind.Bool)
			{
				Error("Condition expression must resolve to a bool", expression);
			}
		}

		private TypeInfo? GetInferredType(ExpressionNode expression)
		{
			_inferredTypeVisitor.LocalIndex = LocalIndex;
			expression.Accept(_inferredTypeVisitor);
			return _inferredTypeVisitor.InferredType;
		}

		private TypeInfo? GetInferredType(List<ExpressionNode>? expressions)
		{
			if (expressions == null)
			{
				return null;
			}

			var elementTypes = expressions.Select(x =>
			{
				return GetInferredType(x)?.Name;
			}).ToList();

			if (!elementTypes.Any(x => x is null))
			{
				return _context.Types.GetTuple(elementTypes!);
			}
			else
			{
				return null;
			}
		}
	}
}
