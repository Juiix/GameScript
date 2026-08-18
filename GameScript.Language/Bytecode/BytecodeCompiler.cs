using System;
using System.Collections.Generic;
using System.Linq;
using GameScript.Bytecode;
using GameScript.Language.Ast;
using ValueType = GameScript.Bytecode.ValueType;

namespace GameScript.Language.Bytecode;

public sealed class BytecodeCompiler<TCommandOp> where TCommandOp : struct, Enum
{
	private readonly List<BytecodeMethod> _methods = [];
	private readonly List<BytecodeMethodMetadata> _methodMetadata = [];
	private readonly Dictionary<string, int> _methodIndex = [];
	private readonly Dictionary<string, MethodDefinitionNode> _methodNodes = [];
	private readonly Dictionary<string, int> _returnCounts = [];
	private readonly Dictionary<string, string?> _nameToKey = [];
	private readonly IReadOnlyDictionary<CallExpressionNode, Symbols.SymbolInfo>? _resolvedCalls;

	public BytecodeCompiler() : this(null) { }

	/// <summary>
	/// <paramref name="resolvedCalls"/> maps call sites to the overload chosen during
	/// type analysis (TypeAnalysisVisitor.ResolvedCalls, merged across files). Without
	/// it, calls to overloaded names cannot be compiled and '=' op bindings on
	/// overloads are unreachable — only pass null for legacy single-overload content.
	/// </summary>
	public BytecodeCompiler(IReadOnlyDictionary<CallExpressionNode, Symbols.SymbolInfo>? resolvedCalls)
	{
		_resolvedCalls = resolvedCalls;
	}
	private readonly Dictionary<string, Value> _globals = [];
	private readonly Dictionary<Value, int> _constMap = [];
	private readonly List<Value> _constPool = [];

	// constant tables by name (compiled in step 2b), and the table each active
	// 'for r in t' row cursor iterates (per method)
	private readonly Dictionary<string, TableData> _tables = [];
	private readonly Dictionary<string, TableData> _cursorTables = [];

	private readonly List<int> _lineNumbers = [];
	private readonly List<ushort> _ops = [];
	private readonly List<int> _operands = [];
	private readonly Dictionary<string, int> _localSlots = [];
	private readonly Dictionary<string, int> _ctxSlots = [];
	private readonly Stack<LoopContext> _loopStack = [];
	private int _nextSlot;
	private int _currentReturnCount;

	/// <summary>Compiles content without constant tables (kept for existing hosts).</summary>
	public BytecodeCompilerResult Compile(
		IEnumerable<ConstantDefinitionNode> constants,
		IEnumerable<ContextDefinitionNode> contexts,
		IEnumerable<MethodDefinitionNode> methods)
	{
		return Compile(constants, contexts, methods, []);
	}

	/// <summary>
	/// Compiles a whole content root. <paramref name="tables"/> are the 'table'
	/// declarations of every program file (ProgramNode.Tables) — every table access
	/// in a method body lowers to a compare chain over the table's rows, so a table
	/// that is used but not passed here is a compile error.
	/// </summary>
	public BytecodeCompilerResult Compile(
		IEnumerable<ConstantDefinitionNode> constants,
		IEnumerable<ContextDefinitionNode> contexts,
		IEnumerable<MethodDefinitionNode> methods,
		IEnumerable<TableDefinitionNode> tables)
	{
		// Initialize data
		_methods.Clear();
		_methodMetadata.Clear();
		_methodIndex.Clear();
		_methodNodes.Clear();
		_returnCounts.Clear();
		_globals.Clear();
		_constMap.Clear();
		_constPool.Clear();
		_loopStack.Clear();
		_lineNumbers.Clear();
		_tables.Clear();
		_cursorTables.Clear();

		// 1) Index all methods (keyed by name + parameter signature so overloads coexist)
		int index = 0;
		_nameToKey.Clear();
		foreach (var method in methods)
		{
			var key = MangledKey(method);
			var compiled = IsCompilable(method);
			_methodIndex[key] = compiled ? index++ : 0;
			_methodNodes[key] = method;      // kept for call-site default-argument baking
			_returnCounts[key] = method.ReturnTypes?.Count ?? 0;
			// plain-name shortcut for non-overloaded methods; null marks an overloaded name
			_nameToKey[method.SymbolName] = _nameToKey.ContainsKey(method.SymbolName) ? null : key;
		}

		// 2) Compile constant init method
		CompileConstants(constants);

		// 2b) Resolve constant tables (cells may reference the constants above)
		CompileTables(tables);

		// 3) Compile context definitions
		CompileContexts(contexts);

		// 4) Compile each method
		foreach (var method in methods.Where(IsCompilable))
		{
			var methodResult = CompileMethod(method);
			_methods.Add(methodResult.Method);
			_methodMetadata.Add(methodResult.MethodMetadata);
		}

		var program = new BytecodeProgram(
			[.. _methods],
			[.. _constPool]
		);
		var contextNames = _ctxSlots.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Value).ToArray();
		var metadata = new BytecodeProgramMetadata([.. _methodMetadata], contextNames);
		return new BytecodeCompilerResult(program, metadata);
	}

	/// <summary>Name + parameter-type signature, matching SymbolInfo.MangledName.</summary>
	private static string MangledKey(MethodDefinitionNode method) =>
		$"{method.SymbolName}({string.Join(",", method.Parameters?.Select(p => p.Type.Name) ?? [])})";

	private static bool IsCompilable(MethodDefinitionNode method)
	{
		return method.Name.Type switch
		{
			IdentifierType.Func or IdentifierType.Label or IdentifierType.Trigger => true,
			_ => false,
		};
	}

	private void CompileConstants(IEnumerable<ConstantDefinitionNode> constants)
	{
		// for each top‐level constant
		foreach (var c in constants)
		{
			Value v;
			if (c.Initializer is LiteralNode literal)
			{
				v = ParseLiteral(literal);
			}
			else if (c.Initializer is UnaryExpressionNode { Operator: UnaryOperator.Negate, Operand: LiteralNode negLiteral })
			{
				v = Value.FromInt(-ParseLiteral(negLiteral).Int);
			}
			else
			{
				throw new InvalidOperationException("Constant initializer must be a literal expression");
			}

			_globals[c.Name.Name] = v;
		}
	}

	// A resolved constant table: column names/types, data rows and the optional
	// default row as Values. There is no runtime representation — accesses lower
	// to compare chains over these cells at each site.
	private sealed class TableData(string name, string[] columnNames, ValueType[] columnTypes, List<Value[]> rows, Value[]? defaultRow)
	{
		public string Name { get; } = name;
		public string[] ColumnNames { get; } = columnNames;
		public ValueType[] ColumnTypes { get; } = columnTypes;
		public List<Value[]> Rows { get; } = rows;
		public Value[]? DefaultRow { get; } = defaultRow;

		public int IndexOfColumn(string column) => Array.IndexOf(ColumnNames, column);

		/// <summary>The value a lookup yields when no row matches: the default row's cell, else the column type's zero.</summary>
		public Value FallbackValue(int column) => DefaultRow != null
			? DefaultRow[column]
			: ColumnTypes[column] switch
			{
				ValueType.String => Value.FromString(string.Empty),
				ValueType.Bool => Value.FromBool(false),
				_ => Value.FromInt(0),
			};
	}

	private void CompileTables(IEnumerable<TableDefinitionNode> tables)
	{
		foreach (var table in tables)
		{
			var columns = table.Columns ?? [];
			var columnNames = new string[columns.Count];
			var columnTypes = new ValueType[columns.Count];
			for (int c = 0; c < columns.Count; c++)
			{
				columnNames[c] = columns[c].Name.Name;
				columnTypes[c] = columns[c].Type.Name switch
				{
					"string" => ValueType.String,
					"bool" => ValueType.Bool,
					_ => ValueType.Int,
				};
			}

			var rows = new List<Value[]>();
			foreach (var row in table.Rows ?? [])
				rows.Add(CompileRow(table, row, columns.Count));

			var defaultRow = table.DefaultRow != null ? CompileRow(table, table.DefaultRow, columns.Count) : null;

			_tables[table.Name.Name] = new TableData(table.Name.Name, columnNames, columnTypes, rows, defaultRow);
		}
	}

	private Value[] CompileRow(TableDefinitionNode table, TableRowNode row, int columnCount)
	{
		if (row.Cells.Count != columnCount)
			throw new InvalidOperationException($"Row of table '{table.Name.Name}' has {row.Cells.Count} cells; expected {columnCount}");
		var values = new Value[columnCount];
		for (int c = 0; c < columnCount; c++)
			values[c] = EvaluateConstantExpression(row.Cells[c], $"Cell of table '{table.Name.Name}'");
		return values;
	}

	private void CompileContexts(IEnumerable<ContextDefinitionNode> contexts)
	{
		// each context variable sets its own slot from the initializer
		foreach (var c in contexts)
		{
			if (c.Initializer is not LiteralNode literal ||
				literal.Type is not LiteralType.Number)
			{
				throw new InvalidOperationException("Context initializer must be a literal number expression");
			}

			Value v = ParseLiteral(literal);
			_ctxSlots[c.Name.Name] = v.Int;
		}
	}

	private BytecodeMethodResult CompileMethod(MethodDefinitionNode methodNode)
	{
		// Initialize buffers
		_ops.Clear();
		_operands.Clear();
		_lineNumbers.Clear();
		_localSlots.Clear();
		_cursorTables.Clear();

		// 1) Parameter slots
		var paramCount = methodNode.Parameters?.Count ?? 0;
		if (methodNode.Parameters != null)
		{
			for (int i = 0; i < paramCount; i++)
			{
				_localSlots[methodNode.Parameters[i].Name.Name] = i;
			}
		}
		_nextSlot = paramCount;
		_currentReturnCount = methodNode.ReturnTypes?.Count ?? 0;

		// 2) Emit body statements. The final statement of a void method that is a
		//    plain func call compiles as a tail transfer (frame replacement).
		var statements = methodNode.Body?.Statements;
		if (statements != null)
		{
			for (int i = 0; i < statements.Count; i++)
			{
				if (i == statements.Count - 1 &&
					_currentReturnCount == 0 &&
					statements[i] is CallExpressionNode lastCall &&
					TryEmitTailCall(lastCall))
				{
					continue;
				}
				EmitStatement(statements[i]);
			}
		}

		// 3) Ensure there's a Return at the end (a TailCall never falls through)
		var lastOp = _ops.Count > 0 ? (CoreOpCode)_ops[_ops.Count - 1] : default;
		var lastOpIsReturn = _ops.Count > 0 &&
			(lastOp == CoreOpCode.Return || lastOp == CoreOpCode.TailCall);
		if (!lastOpIsReturn)
		{
			for (int i = 0; i < (methodNode.ReturnTypes?.Count ?? 0); i++)
			{
				Emit(CoreOpCode.LoadConst, AddConstant(Value.Null), methodNode.FileRange.End.Line);
			}
			Emit(CoreOpCode.Return, 0, methodNode.FileRange.End.Line);
		}

		// 4) Bake into method
		var name = methodNode.SymbolName;
		var method = new BytecodeMethod(
			name,
			[.. _ops],
			[.. _operands],
			paramCount,
			_nextSlot - paramCount,
			methodNode.ReturnTypes?.Count ?? 0);

		var localNames = new string[_nextSlot];
		foreach (var (localName, slot) in _localSlots)
			localNames[slot] = localName;

		var metadata = new BytecodeMethodMetadata(
			name,
			[.. _lineNumbers],
			methodNode.FilePath,
			localNames);

		return new BytecodeMethodResult(method, metadata);
	}

	private void EmitStatement(AstNode statement)
	{
		switch (statement)
		{
			// ----------------------------------------
			// local variable declaration:  int $x = expr;
			// ----------------------------------------
			case VariableDefinitionNode varDef:
				foreach (var (name, initializer) in varDef.Vars)
				{
					// initializer first
					if (initializer != null)
					{
						EmitExpression(initializer);
					}

					// allocate a slot
					int slot = _nextSlot++;
					_localSlots[name.Name] = slot;
					if (initializer != null)
					{
						Emit(CoreOpCode.StoreLocal, slot, statement.FileRange.Start.Line);
					}
				}
				break;

			// ----------------------------------------
			// expression statements:  foo();  or  $x = 5;  or  tuple‑assign
			// ----------------------------------------
			case ExpressionNode expression:
				// detect tuple assignment syntax:  (a,b) = (c,d)
				if (expression is AssignmentExpressionNode assign
					&& assign.Left is TupleExpressionNode leftTuple
					&& assign.Right is TupleExpressionNode rightTuple)
				{
					EmitTupleAssignment(leftTuple, rightTuple);
				}
				else
				{
					// regular expr: push result then pop it off
					var popCount = EmitExpression(expression);
					for (int i = 0; i < popCount; i++)
					{
						// a table access ends in a jump-patched compare chain whose
						// last op is only one of several arms' pushes — it must be
						// popped for real, never elided
						if (expression is IndexExpressionNode or MemberExpressionNode)
							Emit(CoreOpCode.Pop, 0, statement.FileRange.End.Line);
						else
							EmitPopLast(statement.FileRange.End.Line);
					}
				}
				break;

			// ----------------------------------------
			// return statement
			// ----------------------------------------
			case ReturnStatementNode ret:
				// 'return f(...)' where arities match compiles as a tail transfer
				if (ret.Expression is CallExpressionNode retCall &&
					TryEmitTailCall(retCall))
				{
					break;
				}
				if (ret.Expression != null)
				{
					EmitExpression(ret.Expression);
				}
				Emit(CoreOpCode.Return, 0, statement.FileRange.Start.Line);
				break;

			// ----------------------------------------
			// if (cond) { ... } [ else { ... } ]
			// ----------------------------------------
			case IfStatementNode ifStatement:
				// 1) Compile the 'if' condition
				EmitExpression(ifStatement.Condition);

				// 2) Jump over the 'if' block if false
				int jumpToNext = EmitPlaceholder(CoreOpCode.JumpIfFalse, statement.FileRange.Start.Line);

				// 3) Emit the 'if' block
				if (ifStatement.IfBlock?.Statements != null)
				{
					foreach (var s in ifStatement.IfBlock.Statements)
					{
						EmitStatement(s);
					}
				}

				// 4) After 'if' block, jump to end of the whole if/elseif/else chain
				int jumpPastAll = EmitPlaceholder(CoreOpCode.Jump, ifStatement.FileRange.End.Line);

				// 5) Patch jumpToNext to the start of the first else-if (or else/end)
				Patch(jumpToNext, _ops.Count - jumpToNext);

				// 6) Emit each 'else if' clause in turn
				if (ifStatement.ElseIfNodes != null)
				{
					foreach (var elseIf in ifStatement.ElseIfNodes)
					{
						// 6a) compile the else-if condition
						EmitExpression(elseIf.Condition);

						// 6b) jump over this else-if block if false
						int jumpOverElseIf = EmitPlaceholder(CoreOpCode.JumpIfFalse, elseIf.FileRange.Start.Line);

						// 6c) emit the else-if block
						if (elseIf.Block?.Statements != null)
						{
							foreach (var s in elseIf.Block.Statements)
							{
								EmitStatement(s);
							}
						}

						// 6d) after this else-if block, jump past all remaining clauses
						int jumpPastThis = EmitPlaceholder(CoreOpCode.Jump, elseIf.FileRange.End.Line);

						// 6e) patch the jumpOverElseIf to here (start of next clause)
						Patch(jumpOverElseIf, _ops.Count - jumpOverElseIf);

						// 6f) record this jumpPastThis so we can patch it to the final end
						//     (we'll patch it immediately below once we know 'end' ip)
						// For simplicity we patch now to jumpPastAll—this works because
						// jumpPastAll is already placed and we back‐patch it before finishing.
						Patch(jumpPastThis, jumpPastAll - jumpPastThis);
					}
				}

				// 7) Emit the optional 'else' block
				if (ifStatement.ElseBlock?.Statements != null)
				{
					foreach (var s in ifStatement.ElseBlock.Statements)
					{
						EmitStatement(s);
					}
				}

				// 8) Finally, patch the jumpPastAll to here (the end of the entire chain)
				Patch(jumpPastAll, _ops.Count - jumpPastAll);
				break;

			// ----------------------------------------
			// break
			// ----------------------------------------
			case BreakStatementNode _:
				{
					if (_loopStack.Count == 0)
					{
						throw new Exception("`break` used outside of a loop");
					}

					// emit an unconditional jump placeholder
					int brPos = EmitPlaceholder(CoreOpCode.Jump, statement.FileRange.Start.Line);
					_loopStack.Peek().BreakPlaceholders.Add(brPos);
				}
				break;

			// ----------------------------------------
			// continue
			// ----------------------------------------
			case ContinueStatementNode _:
				{
					if (_loopStack.Count == 0)
					{
						throw new Exception("`continue` used outside of a loop");
					}

					int contPos = EmitPlaceholder(CoreOpCode.Jump, statement.FileRange.Start.Line);
					_loopStack.Peek().ContinuePlaceholders.Add(contPos);
				}
				break;

			// ----------------------------------------
			// while (cond) { ... }
			// ----------------------------------------
			case WhileStatementNode whileStmt:
				{
					// 1) Mark the start of the condition
					int conditionIp = _ops.Count;

					// 2) Compile the loop condition
					EmitExpression(whileStmt.Condition);

					// 3) Jump out if false (placeholder)
					int exitPlaceholder = EmitPlaceholder(CoreOpCode.JumpIfFalse, statement.FileRange.Start.Line);

					// 4) Push a new loop context
					var ctx = new LoopContext
					{
						ConditionIp = conditionIp,
						ExitPlaceholder = exitPlaceholder,
						ContinueTargetIp = conditionIp
					};
					_loopStack.Push(ctx);

					// 5) Compile the loop body
					if (whileStmt.Body?.Statements != null)
					{
						foreach (var s in whileStmt.Body.Statements)
						{
							EmitStatement(s);
						}
					}

					// 6) At end of body, jump back to condition
					Emit(CoreOpCode.Jump, conditionIp - _ops.Count, statement.FileRange.Start.Line);

					// 7) Pop the loop context so no deeper breaks/continues get mixed up
					_loopStack.Pop();

					// 8) Patch the exit‐jump to here (after loop)
					int loopEndIp = _ops.Count;
					Patch(exitPlaceholder, loopEndIp - exitPlaceholder);

					// 9) Patch all `break` placeholders to exit the loop
					foreach (var brPos in ctx.BreakPlaceholders)
					{
						Patch(brPos, loopEndIp - brPos);
					}

					// 10) Patch all `continue` placeholders to re‐evaluate condition
					foreach (var contPos in ctx.ContinuePlaceholders)
					{
						Patch(contPos, ctx.ContinueTargetIp - contPos);
					}
				}
				break;

			// ----------------------------------------
			// for VAR in START..END { ... }  — half-open [START, END)
			// ----------------------------------------
			case ForStatementNode forStmt:
				{
					int line = statement.FileRange.Start.Line;

					// loop variable slot; a later same-name 'for' reuses it
					if (!_localSlots.TryGetValue(forStmt.Variable.Name, out int varSlot))
					{
						varSlot = _nextSlot++;
						_localSlots[forStmt.Variable.Name] = varSlot;
					}

					// 1) i = START; END hoisted to a hidden temp — both evaluated once
					EmitExpression(forStmt.Start);
					Emit(CoreOpCode.StoreLocal, varSlot, line);
					int endSlot = _nextSlot++;      // hidden, unnamed slot
					EmitExpression(forStmt.End);
					Emit(CoreOpCode.StoreLocal, endSlot, line);

					// 2..5) condition i < END, body, increment, patch exits
					EmitCountedLoopTail(varSlot, () => Emit(CoreOpCode.LoadLocal, endSlot, line), forStmt.Body, line);
				}
				break;

			// ----------------------------------------
			// for CURSOR in TABLE { ... }  — positional iteration over a constant
			// table: a counted loop over a hidden row index; each 'CURSOR.col' read
			// inside is a positional lookup on that index (see EmitTableLookup).
			// ----------------------------------------
			case ForTableStatementNode forTable:
				{
					int line = statement.FileRange.Start.Line;
					var table = ResolveTable(forTable.Table.Name);

					// the cursor's slot IS the hidden row index; a later same-name
					// 'for' over the same table reuses it
					if (!_localSlots.TryGetValue(forTable.Cursor.Name, out int indexSlot))
					{
						indexSlot = _nextSlot++;
						_localSlots[forTable.Cursor.Name] = indexSlot;
					}
					_cursorTables[forTable.Cursor.Name] = table;

					// 1) index = 0
					Emit(CoreOpCode.LoadConstInt, 0, line);
					Emit(CoreOpCode.StoreLocal, indexSlot, line);

					// 2..5) condition index < count, body, increment, patch exits
					EmitCountedLoopTail(indexSlot, () => Emit(CoreOpCode.LoadConstInt, table.Rows.Count, line), forTable.Body, line);
				}
				break;

			// ----------------------------------------
			// switch subject / case v1, v2: ... / default: ...
			// Compiles as the equivalent if/else-if chain over a hidden temp
			// holding the subject (evaluated once). No fallthrough.
			// ----------------------------------------
			case SwitchStatementNode switchStmt:
				{
					int line = statement.FileRange.Start.Line;

					// subject evaluated once into a hidden temp local
					EmitExpression(switchStmt.Subject);
					int subjectSlot = _nextSlot++;   // hidden, unnamed slot
					Emit(CoreOpCode.StoreLocal, subjectSlot, line);

					if (switchStmt.Cases == null)
					{
						break;
					}

					var endJumps = new List<int>();
					foreach (var caseNode in switchStmt.Cases)
					{
						if (caseNode.IsDefault)
						{
							continue;    // emitted after all value cases
						}

						int caseLine = caseNode.FileRange.Start.Line;
						var values = caseNode.Values!;

						// condition: (subj == v1) or (subj == v2) or … (short-circuit)
						List<int>? orJumps = null;
						for (int k = 0; k < values.Count; k++)
						{
							Emit(CoreOpCode.LoadLocal, subjectSlot, caseLine);
							EmitExpression(values[k]);
							Emit(CoreOpCode.Equal, 0, caseLine);
							if (k < values.Count - 1)
							{
								(orJumps ??= []).Add(EmitPlaceholder(CoreOpCode.JumpIfTrueKeep, caseLine));
								Emit(CoreOpCode.Pop, 0, caseLine);
							}
						}
						if (orJumps != null)
						{
							foreach (var orJump in orJumps)
							{
								Patch(orJump, _ops.Count - orJump);
							}
						}

						int jumpNext = EmitPlaceholder(CoreOpCode.JumpIfFalse, caseLine);
						EmitBlock(caseNode.Body);
						endJumps.Add(EmitPlaceholder(CoreOpCode.Jump, caseNode.FileRange.End.Line));
						Patch(jumpNext, _ops.Count - jumpNext);
					}

					EmitBlock(switchStmt.DefaultCase?.Body);

					foreach (var endJump in endJumps)
					{
						Patch(endJump, _ops.Count - endJump);
					}
				}
				break;

			default:
				throw new NotSupportedException($"Statement not handled: {statement.GetType().Name}");
		}
	}

	private int EmitExpression(ExpressionNode expression)
	{
		int slot;
		switch (expression)
		{
			// ----------------------------------------
			// Literal values: push a constant Value
			// ----------------------------------------
			case LiteralNode lit:
				Value v = ParseLiteral(lit);
				EmitLoadConstant(v, expression.FileRange.Start.Line);
				return 1;

			// ----------------------------------------
			// Grouping: semantically transparent
			// ----------------------------------------
			case ParenthesizedExpressionNode paren:
				return EmitExpression(paren.Inner);

			// ----------------------------------------
			// Variable or constant: load from a local slot
			// ----------------------------------------
			case IdentifierNode id:
				if (TryGetVarSlot(id.Type, id.Name, out slot))
				{
					if (id.DotPrefix > 0)
						slot = (id.DotPrefix << 16) | (slot & 0xFFFF);
					EmitLoadVar(id.Type, slot, expression.FileRange.Start.Line);
				}
				else if (id.Type == IdentifierType.Constant && _globals.TryGetValue(id.Name, out var constValue))
				{
					EmitLoadConstant(constValue, expression.FileRange.Start.Line);
				}
				else if (id.Type is IdentifierType.Func or IdentifierType.Label &&
					_nameToKey.TryGetValue(id.Name, out var refKey))
				{
					if (refKey == null)
						throw new Exception($"Reference to '{id.Name}' is ambiguous: the name is overloaded.");
					Emit(CoreOpCode.LoadMethodRef, _methodIndex[refKey], expression.FileRange.Start.Line);
				}
				else
				{
					throw new Exception($"Unknown variable {id.Type}: '{id.Name}'");
				}
				return 1;

			case BinaryExpressionNode bin:
				if (bin.Operator == BinaryOperator.And)
				{
					EmitExpression(bin.Left);
					int jumpEnd = EmitPlaceholder(CoreOpCode.JumpIfFalseKeep, expression.FileRange.Start.Line);
					Emit(CoreOpCode.Pop, 0, expression.FileRange.Start.Line);
					EmitExpression(bin.Right);
					Patch(jumpEnd, _ops.Count - jumpEnd);
					return 1;
				}
				if (bin.Operator == BinaryOperator.Or)
				{
					EmitExpression(bin.Left);
					int jumpEnd = EmitPlaceholder(CoreOpCode.JumpIfTrueKeep, expression.FileRange.Start.Line);
					Emit(CoreOpCode.Pop, 0, expression.FileRange.Start.Line);
					EmitExpression(bin.Right);
					Patch(jumpEnd, _ops.Count - jumpEnd);
					return 1;
				}

				EmitExpression(bin.Left);
				EmitExpression(bin.Right);

				var opCode = GetOpCode(bin.Operator);
				Emit(opCode, 0, expression.FileRange.Start.Line);
				return 1;

			// ----------------------------------------
			// Simple assignment as expression: ($x = expr) pushes the new value
			// ----------------------------------------
			case AssignmentExpressionNode assign:
				if (assign.Left is TupleExpressionNode leftTuple)
				{
					EmitTupleAssignment(leftTuple, assign.Right);
					return leftTuple.Elements.Count;
				}

				if (assign.Left is not IdentifierNode vid ||
					!TryGetVarSlot(vid.Type, vid.Name, out slot))
				{
					throw new Exception("Invalid left‑hand side in assignment");
				}

				if (vid.DotPrefix > 0)
					slot = (vid.DotPrefix << 16) | (slot & 0xFFFF);

				// Handle simple '='
				if (assign.Operator == AssignmentOperator.Assign)
				{
					// just evaluate RHS
					EmitExpression(assign.Right);
				}
				else
				{
					// operator-assignment x op= y => x = x op y
					// 1) load old x
					EmitLoadVar(vid.Type, slot, expression.FileRange.Start.Line);
					// 2) evaluate y
					EmitExpression(assign.Right);
					// 3) apply the binary op
					var binOp = GetOpCode(assign.Operator);
					Emit(binOp, 0, expression.FileRange.Start.Line);
				}

				// store the result back into x
				EmitStoreVar(vid.Type, slot, expression.FileRange.Start.Line);
				// leave the assigned value on the stack as the expression result
				EmitLoadVar(vid.Type, slot, expression.FileRange.Start.Line);
				return 1;

			// ----------------------------------------
			// Function call: push args then Call
			// ----------------------------------------
			case CallExpressionNode call:
				// 1) resolve the target overload
				var callName = call.FunctionName.Name;
				if (!TryResolveCall(call, out var methodKey, out var resolvedSymbol))
				{
					throw new Exception($"Unknown method '{callName}'");
				}

				if (!_methodIndex.TryGetValue(methodKey, out var fid))
				{
					throw new Exception($"Unknown method '{methodKey}'");
				}
				var returnCount = _returnCounts[methodKey];

				// 2) arguments (omitted trailing defaults baked as constants)
				EmitCallArguments(call, methodKey, expression.FileRange.Start.Line);

				switch (resolvedSymbol?.IdentifierType ?? call.FunctionName.Type)
				{
					case IdentifierType.Func:
						Emit(CoreOpCode.Call, fid, expression.FileRange.Start.Line);
						break;
					case IdentifierType.Label:
						Emit(CoreOpCode.Goto, fid, expression.FileRange.Start.Line);
						break;
					case IdentifierType.Command:
						// a '= name' binding routes this overload to a specific engine op
						var opName = resolvedSymbol?.InternalName ?? callName;
						if (CommandHandler<TCommandOp>.TryGetOp(opName, out var commandOp))
						{
							Emit(commandOp, call.DotPrefix, expression.FileRange.Start.Line);
							break;
						}
						else
						{
							throw new NotImplementedException($"Command '{opName}' is not a supported operation.");
						}
					default:
						throw new Exception($"Cannot call method of type '{call.FunctionName.Type}' ('{call.FunctionName.Name}')");
				}
				return returnCount;

			// ----------------------------------------
			// Unary expressions: prefix -, !
			// ----------------------------------------
			case UnaryExpressionNode unary:
				// Only identifiers can be incremented/decremented
				if ((unary.Operator == UnaryOperator.Increment || unary.Operator == UnaryOperator.Decrement) &&
					unary.Operand is IdentifierNode incrTarget &&
					TryGetVarSlot(incrTarget.Type, incrTarget.Name, out slot))
				{
					if (incrTarget.DotPrefix > 0)
						slot = (incrTarget.DotPrefix << 16) | (slot & 0xFFFF);
					// prefix: evaluate to (x = x ± 1), leave new value on stack
					EmitLoadVar(incrTarget.Type, slot, expression.FileRange.Start.Line);

					var toAdd = unary.Operator is UnaryOperator.Increment ? 1 : -1;
					Emit(CoreOpCode.LoadConstInt, toAdd, expression.FileRange.Start.Line);
					Emit(CoreOpCode.Add, 0, expression.FileRange.Start.Line);

					EmitStoreVar(incrTarget.Type, slot, expression.FileRange.Start.Line);
					EmitLoadVar(incrTarget.Type, slot, expression.FileRange.Start.Line);
				}
				else
				{
					// handle -, ! as before
					switch (unary.Operator)
					{
						case UnaryOperator.Negate:
							EmitExpression(unary.Operand);
							Emit(CoreOpCode.Negate, 0, expression.FileRange.Start.Line);
							break;

						case UnaryOperator.Not:
							EmitExpression(unary.Operand);
							Emit(CoreOpCode.Not, 0, expression.FileRange.Start.Line);
							break;

						default:
							throw new NotSupportedException($"Unsupported unary operator {unary.Operator}");
					}
				}
				return 1;

			// ----------------------------------------
			// Postfix: x++, x--
			// ----------------------------------------
			case PostfixExpressionNode postfix:
				if ((postfix.Operator == UnaryOperator.Increment || postfix.Operator == UnaryOperator.Decrement) &&
					postfix.Operand is IdentifierNode target &&
					TryGetVarSlot(target.Type, target.Name, out slot))
				{
					if (target.DotPrefix > 0)
						slot = (target.DotPrefix << 16) | (slot & 0xFFFF);
					// postfix: evaluate to original x, but side-effect x = x ± 1
					// push original x first (this is the expression result)
					EmitLoadVar(target.Type, slot, expression.FileRange.Start.Line);

					// compute x ± 1 and store back
					EmitLoadVar(target.Type, slot, expression.FileRange.Start.Line);
					var toAdd = postfix.Operator is UnaryOperator.Increment ? 1 : -1;
					Emit(CoreOpCode.LoadConstInt, toAdd, expression.FileRange.Start.Line);
					Emit(CoreOpCode.Add, 0, expression.FileRange.Start.Line);
					EmitStoreVar(target.Type, slot, expression.FileRange.Start.Line);
				}
				else
				{
					throw new NotSupportedException($"Unsupported postfix operator {postfix.Operator}");
				}
				return 1;

			// ----------------------------------------
			// Tuple literal: push each element in order
			// (useful only in an assignment context)
			// ----------------------------------------
			case TupleExpressionNode tuple:
				foreach (var element in tuple.Elements)
				{
					EmitExpression(element);
				}
				return tuple.Elements.Count;

			// ----------------------------------------
			// Table accesses: t.count / t.has(...) / t[k].col / t.at(i).col / r.col
			// (a bare 't[k]' or 't.at(i)' is a row, never a value — analysis rejects it)
			// ----------------------------------------
			case MemberExpressionNode member:
				EmitMemberExpression(member);
				return 1;

			case IndexExpressionNode index:
				throw new InvalidOperationException($"Table lookup on '{DescribeTarget(index.Target)}' yields a row; select a column");

			default:
				throw new NotSupportedException($"Expression not handled: {expression.GetType().Name}");
		}
	}

	// ----------------------------------------------------------------------
	// Constant tables
	// ----------------------------------------------------------------------

	private TableData ResolveTable(string name)
	{
		if (!_tables.TryGetValue(name, out var table))
			throw new InvalidOperationException($"Unknown table '{name}' (was it passed to Compile?)");
		return table;
	}

	private static string DescribeTarget(ExpressionNode target) =>
		target is IdentifierNode id ? id.Name : target.GetType().Name;

	private void EmitMemberExpression(MemberExpressionNode member)
	{
		int line = member.FileRange.Start.Line;

		// t.count / t.has(...) — the target is the table itself
		if (member.IsCount || member.IsHas)
		{
			if (member.Target is not IdentifierNode { Type: IdentifierType.Table } tableId)
				throw new InvalidOperationException($"'.{member.Member.Name}' requires a table target");
			var table = ResolveTable(tableId.Name);

			if (member.IsCount)
			{
				EmitLoadConstant(Value.FromInt(table.Rows.Count), line);
				return;
			}

			var (keyColumns, keyExprs) = KeyColumnsOf(table, member.KeyColumn, member.Arguments ?? []);
			EmitTableLookup(table, keyColumns, keyExprs, null, valueColumn: -1, has: true, line);
			return;
		}

		if (member.IsAt)
			throw new InvalidOperationException("'at' yields a row; select a column");

		// row.col — the target is a keyed lookup, a positional 'at', or a row cursor
		switch (member.Target)
		{
			case IndexExpressionNode index when index.Target is IdentifierNode { Type: IdentifierType.Table } tableId:
				{
					var table = ResolveTable(tableId.Name);
					var (keyColumns, keyExprs) = KeyColumnsOf(table, index.KeyColumn, index.Arguments);
					EmitTableLookup(table, keyColumns, keyExprs, null, ColumnOf(table, member), has: false, line);
					return;
				}

			case MemberExpressionNode { IsAt: true } at when at.Target is IdentifierNode { Type: IdentifierType.Table } tableId:
				{
					var table = ResolveTable(tableId.Name);
					// key column -1 = the row index
					EmitTableLookup(table, [-1], [at.Arguments![0]], null, ColumnOf(table, member), has: false, line);
					return;
				}

			case IdentifierNode { Type: IdentifierType.Local } cursor when _cursorTables.TryGetValue(cursor.Name, out var cursorTable):
				{
					// the cursor's own slot holds the row index — no evaluation needed
					var indexSlot = _localSlots[cursor.Name];
					EmitTableLookup(cursorTable, [-1], null, [indexSlot], ColumnOf(cursorTable, member), has: false, line);
					return;
				}

			default:
				throw new InvalidOperationException($"Member '.{member.Member.Name}' on '{DescribeTarget(member.Target)}' is not a table row access");
		}
	}

	private static int ColumnOf(TableData table, MemberExpressionNode member)
	{
		var column = table.IndexOfColumn(member.Member.Name);
		if (column < 0)
			throw new InvalidOperationException($"Table '{table.Name}' has no column '{member.Member.Name}'");
		return column;
	}

	// The key columns of a lookup: 'name' for '[name: k]', else the leading N columns.
	private static (int[] Columns, ExpressionNode[] Exprs) KeyColumnsOf(TableData table, IdentifierNode? keyColumn, List<ExpressionNode> args)
	{
		if (keyColumn != null)
		{
			var column = table.IndexOfColumn(keyColumn.Name);
			if (column < 0)
				throw new InvalidOperationException($"Table '{table.Name}' has no column '{keyColumn.Name}'");
			return ([column], [.. args]);
		}
		var columns = new int[args.Count];
		for (int i = 0; i < columns.Length; i++)
			columns[i] = i;
		return (columns, [.. args]);
	}

	// Lowers one table access to a value on the stack.
	//   keyColumns[i] : the column compared for key i, or -1 for "the row index"
	//   keyExprs      : the key expressions to evaluate (null when keySlots is given)
	//   keySlots      : local slots already holding the keys (row cursors)
	//   valueColumn   : the column pushed on a match (ignored when has)
	//   has           : push true/false instead of a cell
	// All-constant keys fold to a single constant push. Otherwise each key is
	// evaluated once into a hidden temp and the rows are scanned as a compare
	// chain — the same shape the 'switch' emitter produces — with the default
	// row's cell (or the column's zero value / false) as the fallthrough.
	private void EmitTableLookup(TableData table, int[] keyColumns, ExpressionNode[]? keyExprs, int[]? keySlots, int valueColumn, bool has, int line)
	{
		// 1) constant folding
		if (keyExprs != null && keyExprs.All(IsConstantExpression))
		{
			var keys = keyExprs.Select(e => EvaluateConstantExpression(e, "Table key")).ToArray();
			int found = -1;
			for (int r = 0; r < table.Rows.Count && found < 0; r++)
			{
				if (RowMatches(table, r, keyColumns, keys))
					found = r;
			}
			if (has)
				EmitLoadConstant(Value.FromBool(found >= 0), line);
			else
				EmitLoadConstant(found >= 0 ? table.Rows[found][valueColumn] : table.FallbackValue(valueColumn), line);
			return;
		}

		// 2) keys into hidden temps (each evaluated exactly once)
		if (keySlots == null)
		{
			keySlots = new int[keyExprs!.Length];
			for (int i = 0; i < keyExprs.Length; i++)
			{
				EmitExpression(keyExprs[i]);
				keySlots[i] = _nextSlot++;      // hidden, unnamed slot
				Emit(CoreOpCode.StoreLocal, keySlots[i], line);
			}
		}

		// 3) compare chain over the rows
		var endJumps = new List<int>();
		for (int r = 0; r < table.Rows.Count; r++)
		{
			var nextJumps = new List<int>();
			for (int i = 0; i < keyColumns.Length; i++)
			{
				Emit(CoreOpCode.LoadLocal, keySlots[i], line);
				if (keyColumns[i] < 0)
					Emit(CoreOpCode.LoadConstInt, r, line);
				else
					EmitLoadConstant(table.Rows[r][keyColumns[i]], line);
				Emit(CoreOpCode.Equal, 0, line);
				nextJumps.Add(EmitPlaceholder(CoreOpCode.JumpIfFalse, line));
			}

			// matched: push the arm's value and leave the chain
			EmitLoadConstant(has ? Value.FromBool(true) : table.Rows[r][valueColumn], line);
			endJumps.Add(EmitPlaceholder(CoreOpCode.Jump, line));

			foreach (var jump in nextJumps)
				Patch(jump, _ops.Count - jump);
		}

		// 4) no row matched
		EmitLoadConstant(has ? Value.FromBool(false) : table.FallbackValue(valueColumn), line);

		foreach (var jump in endJumps)
			Patch(jump, _ops.Count - jump);
	}

	private static bool RowMatches(TableData table, int row, int[] keyColumns, Value[] keys)
	{
		for (int i = 0; i < keyColumns.Length; i++)
		{
			var cell = keyColumns[i] < 0 ? Value.FromInt(row) : table.Rows[row][keyColumns[i]];
			if (!cell.Equals(keys[i]))
				return false;
		}
		return true;
	}

	// Shared tail of a counted loop over local 'varSlot': condition
	// 'var < LIMIT' (LIMIT pushed by emitLimit), body, increment (the 'continue'
	// target), back-jump, and break/continue/exit patching.
	private void EmitCountedLoopTail(int varSlot, Action emitLimit, BlockNode? body, int line)
	{
		// 2) condition: i < LIMIT
		int conditionIp = _ops.Count;
		Emit(CoreOpCode.LoadLocal, varSlot, line);
		emitLimit();
		Emit(CoreOpCode.LessThan, 0, line);
		int exitPlaceholder = EmitPlaceholder(CoreOpCode.JumpIfFalse, line);

		var ctx = new LoopContext
		{
			ConditionIp = conditionIp,
			ExitPlaceholder = exitPlaceholder
		};
		_loopStack.Push(ctx);

		// 3) body
		EmitBlock(body);

		// 4) increment ('continue' target): i = i + 1
		ctx.ContinueTargetIp = _ops.Count;
		Emit(CoreOpCode.LoadLocal, varSlot, line);
		Emit(CoreOpCode.LoadConstInt, 1, line);
		Emit(CoreOpCode.Add, 0, line);
		Emit(CoreOpCode.StoreLocal, varSlot, line);
		Emit(CoreOpCode.Jump, conditionIp - _ops.Count, line);

		// 5) patch exits
		_loopStack.Pop();
		int loopEndIp = _ops.Count;
		Patch(exitPlaceholder, loopEndIp - exitPlaceholder);
		foreach (var brPos in ctx.BreakPlaceholders)
		{
			Patch(brPos, loopEndIp - brPos);
		}
		foreach (var contPos in ctx.ContinuePlaceholders)
		{
			Patch(contPos, ctx.ContinueTargetIp - contPos);
		}
	}

	private void EmitLoadConstant(Value value, int lineNumber)
	{
		switch (value.Type)
		{
			case ValueType.Int:
				Emit(CoreOpCode.LoadConstInt, value.Int, lineNumber);
				break;
			case ValueType.Bool:
				Emit(CoreOpCode.LoadConstBool, value.Bool ? 1 : 0, lineNumber);
				break;
			default:
				Emit(CoreOpCode.LoadConst, AddConstant(value), lineNumber);
				break;
		}
	}

	private void EmitLoadVar(IdentifierType type, int slot, int lineNumber)
	{
		switch (type)
		{
			case IdentifierType.Context:
				Emit(CoreOpCode.LoadCtx, slot, lineNumber);
				break;
			default:
				Emit(CoreOpCode.LoadLocal, slot, lineNumber);
				break;
		}
	}

	private void EmitStoreVar(IdentifierType type, int slot, int lineNumber)
	{
		switch (type)
		{
			case IdentifierType.Context:
				Emit(CoreOpCode.StoreCtx, slot, lineNumber);
				break;
			default:
				Emit(CoreOpCode.StoreLocal, slot, lineNumber);
				break;
		}
	}

	/// <summary>
	/// Helper for tuple‑to‑tuple assignment: (a, b) = (X, Y)
	/// </summary>
	private void EmitTupleAssignment(TupleExpressionNode left, ExpressionNode right)
	{
		// 1) Evaluate all RHS expressions, pushing each result in order
		if (right is TupleExpressionNode rightTuple)
		{
			foreach (var rhs in rightTuple.Elements)
			{
				EmitExpression(rhs);
			}
		}
		else if (right is CallExpressionNode rightCall)
		{
			EmitExpression(rightCall);
		}

		// 1b) Allocate slots for any inline declarations (left to right, before storing)
		foreach (var element in left.Elements)
		{
			if (element is DeclarationExpressionNode decl)
			{
				_localSlots[decl.Name.Name] = _nextSlot++;
			}
		}

		// 2) Pop them into the LHS identifiers *in reverse order*
		for (int i = left.Elements.Count - 1; i >= 0; i--)
		{
			var (type, slot) = ResolveTupleTarget(left.Elements[i]);

			// store the top of the stack into that slot
			EmitStoreVar(type, slot, left.FileRange.Start.Line);
		}

		// 3) Load values back onto the stack for chain tuple assignment (EmitPopLast will clean up trailing loads)
		for (int i = 0; i < left.Elements.Count; i++)
		{
			var (type, slot) = ResolveTupleTarget(left.Elements[i]);

			// load them back on the stack
			EmitLoadVar(type, slot, left.FileRange.Start.Line);
		}
	}

	/// <summary>
	/// Resolves a call site to its method-index key and (when overload data is
	/// available) the chosen overload's symbol. Throws only when the name exists
	/// but is overloaded and no resolution data was provided.
	/// </summary>
	private bool TryResolveCall(CallExpressionNode call, out string key, out Symbols.SymbolInfo? resolved)
	{
		resolved = null;
		_resolvedCalls?.TryGetValue(call, out resolved);
		if (resolved != null)
		{
			key = resolved.MangledName;
			return true;
		}
		if (_nameToKey.TryGetValue(call.FunctionName.Name, out var uniqueKey))
		{
			key = uniqueKey ??
				throw new Exception($"Call to overloaded method '{call.FunctionName.Name}' requires overload resolution data (pass TypeAnalysisVisitor.ResolvedCalls to the compiler).");
			return true;
		}
		key = string.Empty;
		return false;
	}

	/// <summary>
	/// Emits 'args… + TailCall' for a call in tail position when the callee is a
	/// script func whose return arity matches the current method's. Returns false
	/// (emitting nothing) when the pattern doesn't qualify — commands, arity
	/// mismatches, and unknown targets compile as ordinary calls.
	/// </summary>
	private bool TryEmitTailCall(CallExpressionNode call)
	{
		if (!TryResolveCall(call, out var key, out var resolved))
			return false;

		var kind = resolved?.IdentifierType ?? call.FunctionName.Type;
		if (kind is not (IdentifierType.Func or IdentifierType.Label))
			return false;

		if (!_methodIndex.TryGetValue(key, out var fid))
			return false;

		if (_returnCounts[key] != _currentReturnCount)
			return false;

		EmitCallArguments(call, key, call.FileRange.Start.Line);
		Emit(CoreOpCode.TailCall, fid, call.FileRange.Start.Line);
		return true;
	}

	/// <summary>
	/// Emits a call site's arguments: the provided expressions, then a constant
	/// for each omitted trailing parameter that declares a default value.
	/// </summary>
	private void EmitCallArguments(CallExpressionNode call, string methodKey, int lineNumber)
	{
		int provided = call.Arguments?.Count ?? 0;
		if (call.Arguments != null)
		{
			foreach (var arg in call.Arguments)
			{
				EmitExpression(arg);
			}
		}

		if (!_methodNodes.TryGetValue(methodKey, out var target) ||
			target.Parameters == null ||
			target.Parameters.Count <= provided)
		{
			return;
		}

		for (int i = provided; i < target.Parameters.Count; i++)
		{
			var parameter = target.Parameters[i];
			var defaultValue = parameter.Default
				?? throw new InvalidOperationException(
					$"Missing argument for parameter '{parameter.Name.Name}' in call to '{call.FunctionName.Name}'");
			EmitLoadConstant(EvaluateConstantExpression(defaultValue), lineNumber);
		}
	}

	// Compile-time evaluation of a constant expression (parameter defaults, table
	// cells, folded table keys): literal, negated number literal, or a '^' constant
	// (resolved via the globals compiled in step 2).
	private Value EvaluateConstantExpression(ExpressionNode node, string what = "Parameter default")
	{
		return node switch
		{
			LiteralNode literal => ParseLiteral(literal),
			UnaryExpressionNode { Operator: UnaryOperator.Negate, Operand: LiteralNode negLiteral } =>
				Value.FromInt(-ParseLiteral(negLiteral).Int),
			IdentifierNode { Type: IdentifierType.Constant } id when _globals.TryGetValue(id.Name, out var constValue) =>
				constValue,
			_ => throw new InvalidOperationException($"{what} must be a literal or a '^' constant"),
		};
	}

	private static bool IsConstantExpression(ExpressionNode node) =>
		Symbols.ConstantExpressions.IsConstant(node);

	private void EmitBlock(BlockNode? block)
	{
		if (block?.Statements == null)
		{
			return;
		}
		foreach (var s in block.Statements)
		{
			EmitStatement(s);
		}
	}

	private (IdentifierType Type, int Slot) ResolveTupleTarget(ExpressionNode element)
	{
		if (element is DeclarationExpressionNode decl)
		{
			return (IdentifierType.Local, _localSlots[decl.Name.Name]);
		}

		if (element is not IdentifierNode ident)
		{
			throw new InvalidOperationException("LHS of tuple must be identifiers or inline declarations");
		}

		if (!TryGetVarSlot(ident.Type, ident.Name, out int slot))
		{
			throw new KeyNotFoundException($"Unknown local variable '{ident.Name}'");
		}

		if (ident.DotPrefix > 0)
			slot = (ident.DotPrefix << 16) | (slot & 0xFFFF);

		return (ident.Type, slot);
	}

	private static CoreOpCode GetOpCode(BinaryOperator binOp)
	{
		return binOp switch
		{
			BinaryOperator.Add => CoreOpCode.Add,
			BinaryOperator.Subtract => CoreOpCode.Subtract,
			BinaryOperator.Multiply => CoreOpCode.Multiply,
			BinaryOperator.Divide => CoreOpCode.Divide,
			BinaryOperator.Modulo => CoreOpCode.Modulo,
			BinaryOperator.EqualTo => CoreOpCode.Equal,
			BinaryOperator.NotEqualTo => CoreOpCode.NotEqual,
			BinaryOperator.LessThan => CoreOpCode.LessThan,
			BinaryOperator.GreaterThan => CoreOpCode.GreaterThan,
			BinaryOperator.LessThanOrEqual => CoreOpCode.LessOrEqual,
			BinaryOperator.GreaterThanOrEqual => CoreOpCode.GreaterOrEqual,
			_ => throw new NotSupportedException($"Unsupported binary operator {binOp}"),
		};
	}

	private static CoreOpCode GetOpCode(AssignmentOperator assOp)
	{
		return assOp switch
		{
			AssignmentOperator.Add => CoreOpCode.Add,
			AssignmentOperator.Subtract => CoreOpCode.Subtract,
			AssignmentOperator.Multiply => CoreOpCode.Multiply,
			AssignmentOperator.Divide => CoreOpCode.Divide,
			AssignmentOperator.Modulo => CoreOpCode.Modulo,
			_ => throw new NotSupportedException($"Unsupported assignment operator {assOp}"),
		};
	}

	private static Value ParseLiteral(LiteralNode node)
	{
		return node.Type switch
		{
			LiteralType.Number => Value.FromInt(LiteralNode.ParseNumber(node.Value)),
			LiteralType.Boolean => Value.FromBool(bool.Parse(node.Value)),
			LiteralType.String => Value.FromString(node.Value.Substring(1, node.Value.Length - 2)),
			_ => throw new InvalidOperationException($"Cannot parse LiteralType.{node.Type}"),
		};
	}

	private bool TryGetVarSlot(IdentifierType type, string name, out int slot)
	{
		return type switch
		{
			IdentifierType.Context => _ctxSlots.TryGetValue(name, out slot),
			_ => _localSlots.TryGetValue(name, out slot),
		};
	}

	private int AddConstant(Value value)
	{
		// Try dictionary lookup first
		if (_constMap.TryGetValue(value, out int idx))
		{
			return idx;
		}
		// Not found: append to the list and record in the map
		idx = _constPool.Count;
		_constPool.Add(value);
		_constMap[value] = idx;
		return idx;
	}

	private void Emit(CoreOpCode op, int operand, int lineNumber)
	{
		_ops.Add((ushort)op);
		_operands.Add(operand);
		_lineNumbers.Add(lineNumber);
	}

	private void Emit(ushort op, int operand, int lineNumber)
	{
		_ops.Add(op);
		_operands.Add(operand);
		_lineNumbers.Add(lineNumber);
	}

	private int EmitPlaceholder(CoreOpCode op, int lineNumber)
	{
		_ops.Add((ushort)op);
		_operands.Add(0);
		_lineNumbers.Add(lineNumber);
		return _operands.Count - 1;
	}

	private void EmitPopLast(int lineNumber)
	{
		var lastOp = (CoreOpCode)_ops[^1];
		if (lastOp == CoreOpCode.LoadConst ||
			lastOp == CoreOpCode.LoadConstInt ||
			lastOp == CoreOpCode.LoadConstBool ||
			lastOp == CoreOpCode.LoadLocal ||
			lastOp == CoreOpCode.LoadCtx ||
			lastOp == CoreOpCode.LoadMethodRef)
		{
			_ops.RemoveAt(_ops.Count - 1);
			_operands.RemoveAt(_operands.Count - 1);
			_lineNumbers.RemoveAt(_lineNumbers.Count - 1);
		}
		else
		{
			Emit(CoreOpCode.Pop, 0, lineNumber);
		}
	}

	private void Patch(int position, int value)
		=> _operands[position] = value;

	private readonly record struct BytecodeMethodResult(
		BytecodeMethod Method,
		BytecodeMethodMetadata MethodMetadata);
}
