using System;
using System.Collections.Generic;
using System.Linq;
using GameScript.Bytecode;
using GameScript.Language.Ast;
using ValueType = GameScript.Bytecode.ValueType;

namespace GameScript.Language.Bytecode
{
	public sealed class BytecodeCompiler<TCommandOp> where TCommandOp : struct, Enum
	{
		private readonly List<BytecodeMethod> _methods = [];
		private readonly Dictionary<string, int> _methodIndex = [];
		private readonly Dictionary<string, int> _returnCounts = [];
		private readonly Dictionary<string, Value> _globals = [];
		private readonly Dictionary<Value, int> _constMap = [];
		private readonly List<Value> _constPool = [];

		private readonly List<ushort> _ops = [];
		private readonly List<int> _operands = [];
		private readonly Dictionary<string, int> _localSlots = [];
		private readonly Dictionary<string, int> _ctxSlots = [];
		private readonly Stack<LoopContext> _loopStack = [];

		public BytecodeProgram Compile(
			IEnumerable<ConstantDefinitionNode> constants,
			IEnumerable<ContextDefinitionNode> contexts,
			IEnumerable<MethodDefinitionNode> methods)
		{
			// Initialize data
			_methods.Clear();
			_methodIndex.Clear();
			_returnCounts.Clear();
			_globals.Clear();
			_constMap.Clear();
			_constPool.Clear();
			_loopStack.Clear();

			// 1) Index all methods
			int index = 0;
			foreach (var method in methods)
			{
				var compiled = IsCompilable(method);
				_methodIndex[method.SymbolName] = compiled ? index++ : 0;
				_returnCounts[method.SymbolName] = method.ReturnTypes?.Count ?? 0;
			}

			// 2) Compile constant init method
			CompileConstants(constants);

			// 3) Compile constant init method
			CompileContexts(contexts);

			// 4) Compile each method
			index = 0;
			foreach (var method in methods.Where(IsCompilable))
			{
				_methods.Add(CompileMethod(method));
			}

			return new BytecodeProgram(
				[.._methods],
				_methodIndex,
				[.._constPool]
			);
		}

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
				if (c.Initializer is not LiteralNode literal)
				{
					throw new InvalidOperationException("Constant initializer must be a literal expression");
				}

				Value v = ParseLiteral(literal);
				_globals[c.Name.Name] = v;
			}
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

		private BytecodeMethod CompileMethod(MethodDefinitionNode method)
		{
			// Initialize buffers
			_ops.Clear();
			_operands.Clear();
			_localSlots.Clear();

			// 1) Parameter slots
			if (method.Parameters != null)
			{
				for (int i = 0; i < method.Parameters.Count; i++)
				{
					_localSlots[method.Parameters[i].Name.Name] = i;
				}
			}
			int nextSlot = method.Parameters?.Count ?? 0;

			// 2) Emit body statements
			if (method.Body?.Statements != null)
			{
				foreach (var statement in method.Body.Statements)
				{
					EmitStatement(statement, ref nextSlot);
				}
			}

			// 3) Ensure there's a Return at the end
			var lastOpIsReturn = _ops.Count > 0 && ((CoreOpCode)_ops[_ops.Count - 1] == CoreOpCode.Return);
			if (!lastOpIsReturn)
			{
				for (int i = 0; i < (method.ReturnTypes?.Count ?? 0); i++)
				{
					Emit(CoreOpCode.LoadConst, AddConstant(Value.Null));
				}
				Emit(CoreOpCode.Return, 0);
			}

			// 4) Bake into method
			return new BytecodeMethod(
				method.Name.Name,
				[.. _ops],
				[.. _operands],
				method.Parameters?.Count ?? 0,
				nextSlot,
				method.ReturnTypes?.Count ?? 0,
				method.FilePath
			);
		}

		private void EmitStatement(AstNode statement, ref int nextSlot)
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
						int slot = nextSlot++;
						_localSlots[name.Name] = slot;
						if (initializer != null)
						{
							Emit(CoreOpCode.StoreLocal, slot);
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
							EmitPopLast();
					}
					break;

				// ----------------------------------------
				// return statement
				// ----------------------------------------
				case ReturnStatementNode ret:
					if (ret.Expression != null)
					{
						EmitExpression(ret.Expression);
					}
					Emit(CoreOpCode.Return, 0);
					break;

				// ----------------------------------------
				// if (cond) { ... } [ else { ... } ]
				// ----------------------------------------
				case IfStatementNode ifStatement:
					// 1) Compile the 'if' condition
					EmitExpression(ifStatement.Condition);

					// 2) Jump over the 'if' block if false
					int jumpToNext = EmitPlaceholder(CoreOpCode.JumpIfFalse);

					// 3) Emit the 'if' block
					if (ifStatement.IfBlock?.Statements != null)
					{
						foreach (var s in ifStatement.IfBlock.Statements)
						{
							EmitStatement(s, ref nextSlot);
						}
					}

					// 4) After 'if' block, jump to end of the whole if/elseif/else chain
					int jumpPastAll = EmitPlaceholder(CoreOpCode.Jump);

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
							int jumpOverElseIf = EmitPlaceholder(CoreOpCode.JumpIfFalse);

							// 6c) emit the else-if block
							if (elseIf.Block?.Statements != null)
							{
								foreach (var s in elseIf.Block.Statements)
								{
									EmitStatement(s, ref nextSlot);
								}
							}

							// 6d) after this else-if block, jump past all remaining clauses
							int jumpPastThis = EmitPlaceholder(CoreOpCode.Jump);

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
							EmitStatement(s, ref nextSlot);
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
						int brPos = EmitPlaceholder(CoreOpCode.Jump);
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

						int contPos = EmitPlaceholder(CoreOpCode.Jump);
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
						int exitPlaceholder = EmitPlaceholder(CoreOpCode.JumpIfFalse);

						// 4) Push a new loop context
						var ctx = new LoopContext
						{
							ConditionIp = conditionIp,
							ExitPlaceholder = exitPlaceholder
						};
						_loopStack.Push(ctx);

						// 5) Compile the loop body
						if (whileStmt.Body?.Statements != null)
						{
							foreach (var s in whileStmt.Body.Statements)
							{
								EmitStatement(s, ref nextSlot);
							}
						}

						// 6) At end of body, jump back to condition
						Emit(CoreOpCode.Jump, conditionIp - _ops.Count);

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
							Patch(contPos, ctx.ConditionIp - contPos);
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
					EmitLoadConstant(v);
					return 1;

				// ----------------------------------------
				// Variable or constant: load from a local slot
				// ----------------------------------------
				case IdentifierNode id:
					if (TryGetVarSlot(id.Type, id.Name, out slot))
					{
						EmitLoadVar(id.Type, slot);
					}
					else if (id.Type == IdentifierType.Constant && _globals.TryGetValue(id.Name, out var constValue))
					{
						EmitLoadConstant(constValue);
					}
					else
					{
						throw new Exception($"Unknown variable {id.Type}: '{id.Name}'");
					}
					return 1;

				case BinaryExpressionNode bin:
					EmitExpression(bin.Left);
					EmitExpression(bin.Right);

					var opCode = GetOpCode(bin.Operator);
					Emit(opCode, 0);
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
						EmitLoadVar(vid.Type, slot);
						// 2) evaluate y
						EmitExpression(assign.Right);
						// 3) apply the binary op
						var binOp = GetOpCode(assign.Operator);
						Emit(binOp, 0);
					}

					// store the result back into x
					EmitStoreVar(vid.Type, slot);
					// leave the assigned value on the stack as the expression result
					EmitLoadVar(vid.Type, slot);
					return 1;

				// ----------------------------------------
				// Function call: push args then Call
				// ----------------------------------------
				case CallExpressionNode call:
					// 1) arguments
					if (call.Arguments != null)
					{
						foreach (var arg in call.Arguments)
						{
							EmitExpression(arg);
						}
					}

					// 2) call
					if (!_methodIndex.TryGetValue(call.FunctionName.Name, out var fid))
					{
						throw new Exception($"Unknown method '{call.FunctionName.Name}'");
					}
					var returnCount = _returnCounts[call.FunctionName.Name];

					switch (call.FunctionName.Type)
					{
						case IdentifierType.Func:
							Emit(CoreOpCode.Call, fid);
							break;
						case IdentifierType.Label:
							Emit(CoreOpCode.Goto, fid);
							break;
						case IdentifierType.Command:
							if (CommandHandler<TCommandOp>.TryGetOp(call.FunctionName.Name, out var commandOp))
							{
								Emit(commandOp, 0);
								break;
							}
							else
							{
								throw new NotImplementedException($"Command '{call.FunctionName.Name}' is not a supported operation.");
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
						// prefix: evaluate to (x = x ± 1), leave new value on stack
						EmitLoadVar(incrTarget.Type, slot);

						var toAdd = unary.Operator is UnaryOperator.Increment ? 1 : -1;
						Emit(CoreOpCode.Add, toAdd);

						EmitStoreVar(incrTarget.Type, slot);
						EmitLoadVar(incrTarget.Type, slot);
					}
					else
					{
						// handle -, ! as before
						switch (unary.Operator)
						{
							case UnaryOperator.Negate:
								EmitExpression(unary.Operand);
								Emit(CoreOpCode.Negate, 0);
								break;

							case UnaryOperator.Not:
								EmitExpression(unary.Operand);
								Emit(CoreOpCode.Not, 0);
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
						// postfix: evaluate to original x, but side‐effect x = x ± 1
						// 1) push original x
						EmitLoadVar(target.Type, slot);

						var toAdd = postfix.Operator is UnaryOperator.Increment ? 1 : -1;
						Emit(CoreOpCode.Add, toAdd);

						EmitLoadVar(target.Type, slot);
						EmitStoreVar(target.Type, slot);
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

				default:
					throw new NotSupportedException($"Expression not handled: {expression.GetType().Name}");
			}
		}

		private void EmitLoadConstant(Value value)
		{
			switch (value.Type)
			{
				case ValueType.Int:
					Emit(CoreOpCode.LoadConstInt, value.Int);
					break;
				case ValueType.Bool:
					Emit(CoreOpCode.LoadConstBool, value.Bool ? 1 : 0);
					break;
				default:
					Emit(CoreOpCode.LoadConst, AddConstant(value));
					break;
			}
		}

		private void EmitLoadVar(IdentifierType type, int slot)
		{
			switch (type)
			{
				case IdentifierType.Context:
					Emit(CoreOpCode.LoadCtx, slot);
					break;
				default:
					Emit(CoreOpCode.LoadLocal, slot);
					break;
			}
		}

		private void EmitStoreVar(IdentifierType type, int slot)
		{
			switch (type)
			{
				case IdentifierType.Context:
					Emit(CoreOpCode.StoreCtx, slot);
					break;
				default:
					Emit(CoreOpCode.StoreLocal, slot);
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

			// 2) Pop them into the LHS identifiers *in reverse order*
			for (int i = left.Elements.Count - 1; i >= 0; i--)
			{
				if (left.Elements[i] is not IdentifierNode ident)
				{
					throw new InvalidOperationException("LHS of tuple must be identifiers");
				}

				if (!TryGetVarSlot(ident.Type, ident.Name, out int slot))
				{
					throw new KeyNotFoundException($"Unknown local variable '{ident.Name}'");
				}

				// store the top of the stack into that slot
				EmitStoreVar(ident.Type, slot);
			}

			// 3) Load values back onto the stack for chain tuple assignment (EmitPopLast will clean up trailing loads)
			for (int i = 0; i < left.Elements.Count; i++)
			{
				if (left.Elements[i] is not IdentifierNode ident)
				{
					throw new InvalidOperationException("LHS of tuple must be identifiers");
				}

				if (!TryGetVarSlot(ident.Type, ident.Name, out int slot))
				{
					throw new KeyNotFoundException($"Unknown local variable '{ident.Name}'");
				}

				// load them back on the stack
				EmitLoadVar(ident.Type, slot);
			}
		}

		private static CoreOpCode GetOpCode(BinaryOperator binOp)
		{
			return binOp switch
			{
				BinaryOperator.Add => CoreOpCode.Add,
				BinaryOperator.Subtract => CoreOpCode.Subtract,
				BinaryOperator.Multiply => CoreOpCode.Multiply,
				BinaryOperator.Divide => CoreOpCode.Divide,
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
				_ => throw new NotSupportedException($"Unsupported assignment operator {assOp}"),
			};
		}

		private static Value ParseLiteral(LiteralNode node)
		{
			return node.Type switch
			{
				LiteralType.Number => Value.FromInt(int.Parse(node.Value)),
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

		private void Emit(CoreOpCode op, int operand)
		{
			_ops.Add((ushort)op);
			_operands.Add(operand);
		}

		private void Emit(ushort op, int operand)
		{
			_ops.Add(op);
			_operands.Add(operand);
		}

		private int EmitPlaceholder(CoreOpCode op)
		{
			_ops.Add((ushort)op);
			_operands.Add(0);
			return _operands.Count - 1;
		}

		private void EmitPopLast()
		{
			var lastOp = (CoreOpCode)_ops[^1];
			if (lastOp == CoreOpCode.LoadConst ||
				lastOp == CoreOpCode.LoadConstInt ||
				lastOp == CoreOpCode.LoadConstBool ||
				lastOp == CoreOpCode.LoadLocal ||
				lastOp == CoreOpCode.LoadCtx)
			{
				_ops.RemoveAt(_ops.Count - 1);
				_operands.RemoveAt(_operands.Count - 1);
			}
			else
			{
				Emit(CoreOpCode.Pop, 0);
			}
		}

		private void Patch(int position, int value)
			=> _operands[position] = value;
	}
}
