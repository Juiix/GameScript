using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Language.Visitors
{
	public sealed class SemanticAnalysisVisitor(
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes,
		VisitorContext context) : AnalysisVisitorBase(localIndexes)
	{
		private readonly VisitorContext _context = context;
		private int _loopDepth = 0;

		// Visitors have no parent pointers, so the nodes that legitimately appear
		// as a table / row target ('t' in 't[k]', 'r' in 'r.col', 't[k]' in
		// 't[k].col', 't' in 'for r in t') are recorded by their parent before the
		// children are visited; any table / cursor / row-valued node seen outside
		// this set is being misused as a value.
		private readonly HashSet<AstNode> _tableTargets = [];

		/// <summary>Row-count threshold above which a table warns (lookups are compare chains).</summary>
		public const int LargeTableRowCount = 64;

		public override void Visit(MethodDefinitionNode node)
		{
			ReturnFlowCheck(node);

			// check if can have return types
			if (node.ReturnTypes != null &&
				node.Name.Type == IdentifierType.Trigger)
			{
				Error($"{node.Name.Type} cannot have return values", node.ReturnTypes);
			}

			if (node.ReturnTypes != null &&
				node.Name.Type == IdentifierType.TriggerDeclaration)
			{
				Error("Trigger declarations cannot declare return values.", node.ReturnTypes);
			}

			if (node.Name.Type == IdentifierType.Command &&
				node.Body != null)
			{
				Error($"Commands cannot define a method body.", node.Body);
			}

			if (node.Name.Type == IdentifierType.TriggerDeclaration &&
				node.Body != null)
			{
				Error("Trigger declarations cannot define a method body; handlers provide the body.", node.Body);
			}

			ParameterDefaultsCheck(node);

			base.Visit(node);
		}

		// Defaults are allowed only on a contiguous trailing group of func/command
		// parameters, and must be compile-time constants (baked at the call site).
		private void ParameterDefaultsCheck(MethodDefinitionNode node)
		{
			if (node.Parameters == null)
				return;

			var isTrigger = node.Name.Type is IdentifierType.Trigger or IdentifierType.TriggerDeclaration;
			var seenDefault = false;
			foreach (var param in node.Parameters)
			{
				if (param.Default == null)
				{
					if (seenDefault && !isTrigger)
					{
						Error($"Parameter '{param.Name.Name}' must also declare a default value; parameters without defaults cannot follow defaulted parameters.", param);
					}
					continue;
				}

				seenDefault = true;

				if (isTrigger)
				{
					Error("Trigger parameters cannot declare default values.", param.Default);
					continue;
				}

				if (!IsConstantExpression(param.Default))
				{
					Error("Parameter defaults must be a literal or a '^' constant.", param.Default);
				}
			}
		}

		// The shape whitelist shared by constant initializers, parameter defaults,
		// case values and table cells: a literal, a negated number literal, or a '^' constant.
		private static bool IsConstantExpression(ExpressionNode node) => ConstantExpressions.IsConstant(node);

		// ---------------------------------------------------------------
		// tables
		// ---------------------------------------------------------------

		public override void Visit(TableDefinitionNode node)
		{
			var name = node.Name.Name;
			var columns = node.Columns ?? [];

			if (columns.Count == 0)
				Error($"Table '{name}' must declare at least one column.", node.Name);

			var seenColumns = new HashSet<string>();
			foreach (var column in columns)
			{
				if (column.Type.Name is not ("int" or "string" or "bool"))
					Error($"Table columns must be 'int', 'string' or 'bool'; column '{column.Name.Name}' is '{column.Type.Name}'.", column.Type);
				if (MemberExpressionNode.IsReservedMemberName(column.Name.Name))
					Error($"'{column.Name.Name}' is a reserved table member ('count', 'has', 'at'); pick another column name.", column.Name);
				else if (!seenColumns.Add(column.Name.Name))
					Error($"Duplicate column '{column.Name.Name}' in table '{name}'.", column.Name);
			}

			var rows = node.Rows ?? [];
			if (rows.Count == 0)
				Error($"Table '{name}' needs at least one row.", node.Name);
			else if (rows.Count > LargeTableRowCount)
				Warning($"Table '{name}' has {rows.Count} rows; lookups compile to compare chains, so consider a data asset for tables this large.", node.Name.FileRange);

			for (int r = 0; r < rows.Count; r++)
				RowShapeCheck(node, rows[r], r);
			if (node.DefaultRow != null)
				RowShapeCheck(node, node.DefaultRow, -1);

			base.Visit(node);
		}

		private void RowShapeCheck(TableDefinitionNode table, TableRowNode row, int rowIndex)
		{
			var columnCount = table.Columns?.Count ?? 0;
			var label = rowIndex < 0 ? "The default row" : $"Row {rowIndex + 1}";
			if (row.Cells.Count != columnCount)
				Error($"{label} of table '{table.Name.Name}' has {row.Cells.Count} cell(s) but the table has {columnCount} column(s).", row);

			foreach (var cell in row.Cells)
			{
				if (!IsConstantExpression(cell))
					Error("Table cells must be constants ('^name' or a literal).", cell);
			}
		}

		public override void Visit(ForTableStatementNode node)
		{
			_tableTargets.Add(node.Table);
			_loopDepth++;
			base.Visit(node);
			_loopDepth--;
		}

		public override void Visit(IndexExpressionNode node)
		{
			_tableTargets.Add(node.Target);
			if (!_tableTargets.Contains(node))
				Error("A table lookup yields a row; select a column: t[key].column", node);
			base.Visit(node);
		}

		public override void Visit(MemberExpressionNode node)
		{
			_tableTargets.Add(node.Target);
			if (node.IsAt && !_tableTargets.Contains(node))
				Error("'at' yields a row; select a column: t.at(i).column", node);
			base.Visit(node);
		}

		public override void Visit(ConstantDefinitionNode node)
		{
			base.Visit(node);

			var isLiteral = node.Initializer is LiteralNode;
			var isNegatedLiteral = node.Initializer is UnaryExpressionNode { Operator: UnaryOperator.Negate, Operand: LiteralNode };
			if (!isLiteral && !isNegatedLiteral)
			{
				Error("Only literal assignments are allowed in constant declaration.", node.Initializer);
			}
		}

		public override void Visit(ContextDefinitionNode node)
		{
			base.Visit(node);

			if (node.Initializer is not LiteralNode literalNode)
			{
				Error("Only literal numbers assignments are allowed in context declaration.", node.Initializer);
			}
			else if (literalNode.Type != LiteralType.Number)
			{
				Error("Context variable declaration expects an ID number assignment.", node.Initializer);
			}
		}

		public override void Visit(WhileStatementNode node)
		{
			_loopDepth++;
			base.Visit(node);
			_loopDepth--;
		}

		public override void Visit(ForStatementNode node)
		{
			_loopDepth++;
			base.Visit(node);
			_loopDepth--;
		}

		public override void Visit(SwitchCaseNode node)
		{
			if (node.Values != null)
			{
				foreach (var value in node.Values)
				{
					if (!IsConstantExpression(value))
					{
						Error("Case values must be constants ('^name' or a literal).", value);
					}
				}
			}
			base.Visit(node);
		}

		public override void Visit(BlockNode node)
		{
			UnreachableCodeCheck(node);
			base.Visit(node);
		}

		private void UnreachableCodeCheck(BlockNode node)
		{
			if (node.Statements == null) return;

			for (int i = 0; i < node.Statements.Count - 1; i++)
			{
				if (!IsTerminator(node.Statements[i])) continue;

				var deadRange = FileRange.Combine(node.Statements
					.Skip(i + 1)
					.Select(x => x.FileRange));
				Warning("Unreachable code detected.", deadRange, FileErrorTag.Unnecessary);
				return;
			}
		}

		// A statement that never falls through to the next statement in its block:
		// return, break, continue, or a label call (one-way jump that never returns).
		private static bool IsTerminator(AstNode statement)
		{
			return statement switch
			{
				ReturnStatementNode => true,
				BreakStatementNode => true,
				ContinueStatementNode => true,
				CallExpressionNode call => call.FunctionName.Type == IdentifierType.Label,
				_ => false,
			};
		}

		public override void Visit(BreakStatementNode node)
		{
			if (_loopDepth <= 0)
			{
				Error("Cannot use 'break' outside of a loop.", node);
			}
		}

		public override void Visit(ContinueStatementNode node)
		{
			if (_loopDepth <= 0)
			{
				Error("Cannot use 'continue' outside of a loop.", node);
			}
		}

		public override void Visit(AssignmentExpressionNode node)
		{
			// Check that the left-hand side is a valid lvalue (we assume it should be an IdentifierNode).
			if (node.Left is TupleExpressionNode leftTuple)
			{
				foreach (var child in leftTuple.Children)
				{
					// inline declarations ('bool $ok') declare-and-receive in place
					if (child is not DeclarationExpressionNode &&
						!IsAssignableIdentifier(child))
					{
						Error("Tuple element must be an assignable variable.", child.FileRange);
					}
				}
			}
			else if (!IsAssignableIdentifier(node.Left))
			{
				Error("Left-hand side of assignment must be an assignable variable.", node.FileRange);
			}

			base.Visit(node);
		}

		public override void Visit(IdentifierNode node)
		{
			// column names resolve against their table (type analysis), not the symbol tables
			if (node.Type == IdentifierType.Column)
				return;

			// check that the written mark ('^'/'@'/bare) agrees with the symbol's kind
			var symbol = LocalIndex?.GetSymbol(node.Name) ??
				_context.Symbols.GetSymbol(node.Name);

			if (symbol == null)
			{
				Error($"'{node.Name}' is not declared.", node);
			}
			else if (node.Type == IdentifierType.Table)
			{
				if (!_tableTargets.Contains(node))
					Error($"Table '{node.Name}' cannot be used as a value; look up a column with {node.Name}[key].column", node);
				return;
			}
			else if (symbol.IdentifierType == IdentifierType.Local && TableRowType.IsRow(symbol.Type))
			{
				// falls through to the declared-before-use check below
				if (!_tableTargets.Contains(node))
					Error($"'{node.Name}' is a table row cursor; read a column with {node.Name}.column", node);
			}
			else if (symbol.IdentifierType != node.Type)
			{
				switch (symbol.IdentifierType)
				{
					case IdentifierType.Trigger:
						Error($"{symbol.IdentifierType} '{symbol.Name}' cannot be referenced.", node);
						break;
					case IdentifierType.TriggerDeclaration:
						Error($"Trigger '{symbol.Name}' cannot be called or referenced.", node);
						break;
					case IdentifierType.Context:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be referenced with an '@' mark", node);
						break;
					case IdentifierType.Constant:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be referenced with a '^' mark", node);
						break;
					default:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be referenced without a mark", node);
						break;
				}
			}

			if (node.DotPrefix > 0 && node.Type != IdentifierType.Context)
			{
				Error($"'.' prefix is not supported on {node.Type} variables.", node);
			}
			else if (node.DotPrefix > 1)
			{
				Error($"Context variables only support a single '.' prefix.", node);
			}

			if (symbol?.IdentifierType == IdentifierType.Local &&
				node.FileRange.Start.Position < symbol.FileRange.Start.Position)
			{
				Error($"{symbol.PrefixedName} cannot be referenced before it's declared", node);
			}
		}

		private void ReturnFlowCheck(MethodDefinitionNode node)
		{
			if (node.Name.Type is IdentifierType.Command or IdentifierType.TriggerDeclaration ||
				node.ReturnTypes == null ||
				node.ReturnTypes.Count <= 0)
			{
				return;
			}

			// Evaluate the body to see if it *must* return on all paths
			var mustReturn = MustReturn(node.Body);
			if (!mustReturn)
			{
				Error($"{node.Name.Type} '{node.Name.Name}' declares a return type but not all paths return.", node.Name);
			}
		}

		// Evaluate a block to see if it must return.
		private bool MustReturn(BlockNode? block)
		{
			bool guaranteed = false;
			if (block?.Statements != null)
			{
				foreach (var statement in block.Statements)
				{
					// Evaluate each statement
					if (MustReturn(statement))
					{
						guaranteed = true;
						// once we find a guaranteed return, subsequent statements are unreachable
						// so we can break if we want to optimize
						break;
					}
				}
			}
			return guaranteed;
		}

		// Evaluate a single statement to see if it must return
		private bool MustReturn(AstNode statement)
		{
			switch (statement)
			{
				case ReturnStatementNode returnNode:
					return true;  // A direct return means we definitely returned

				case IfStatementNode ifNode:
					// MustReturn only when every branch returns AND a final else exists —
					// without an else, the fall-through path skips the chain entirely.
					if (ifNode.ElseBlock == null)
						return false;

					bool ifPath = MustReturn(ifNode.IfBlock);
					if (ifNode.ElseIfNodes != null)
					{
						foreach (var elseIf in ifNode.ElseIfNodes)
						{
							ifPath &= MustReturn(elseIf.Block);
						}
					}
					ifPath &= MustReturn(ifNode.ElseBlock);
					return ifPath;

				case WhileStatementNode whileNode:
					// Usually "while" is not guaranteed to return unless the DSL ensures infinite loop or break
					// For simplicity, we'll say it might not return => false
					return false;

				case ForStatementNode:
				case ForTableStatementNode:
					// the range / table may be empty, so the body may never run
					return false;

				case SwitchStatementNode switchNode:
					// guaranteed only when a default exists and every arm returns —
					// without a default, a no-match skips the switch entirely
					if (switchNode.Cases == null || switchNode.DefaultCase == null)
						return false;
					foreach (var caseNode in switchNode.Cases)
					{
						if (!MustReturn(caseNode.Body))
							return false;
					}
					return true;

				default:
					// By default, if the statement is a block or something else, just keep checking children
					bool subPath = false;
					foreach (var child in statement.Children)
					{
						if (MustReturn(child))
						{
							subPath = true;
							break;
						}
					}
					return subPath;
			}
		}

		private static bool IsAssignableIdentifier(AstNode node)
		{
			return node is IdentifierNode identifierNode &&
				(identifierNode.Type & IdentifierType.Assignable) != IdentifierType.Unknown;
		}
	}
}
