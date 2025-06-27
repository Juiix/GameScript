using System;
using System.Collections.Generic;

namespace GameScript.Bytecode;

internal static class CoreOps
{
    public static readonly Dictionary<ushort, Action<ScriptState>> Handlers = new()
	{
		// -----------------------
		// ----- stack/frame -----
		// -----------------------
		[(ushort)CoreOpCode.LoadConst] = static state =>
		{
			ref var c = ref state.Context.Constants[state.Operand];
			state.Push(in c);
		},
		[(ushort)CoreOpCode.LoadConstInt] = static state =>
		{
			state.Push(Value.FromInt(state.Operand));
		},
		[(ushort)CoreOpCode.LoadConstBool] = static state =>
		{
			state.Push(Value.FromBool(state.Operand > 0));
		},
		[(ushort)CoreOpCode.LoadLocal] = static state =>
		{
			ref var local = ref state.GetLocal(state.Operand);
			state.Push(in local);
		},
		[(ushort)CoreOpCode.StoreLocal] = static state =>
		{
			var value = state.Pop();
			state.SetLocal(state.Operand, in value);
		},
		[(ushort)CoreOpCode.Pop] = static state =>
		{
			state.PopDiscard();
		},

		// ----------------------
		// ----- arithmetic -----
		// ----------------------
		[(ushort)CoreOpCode.Add] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromInt(a.Int + b.Int));
		},
		[(ushort)CoreOpCode.Subtract] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromInt(a.Int - b.Int));
		},
		[(ushort)CoreOpCode.Multiply] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromInt(a.Int * b.Int));
		},
		[(ushort)CoreOpCode.Divide] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromInt(a.Int / b.Int));
		},

		// -----------------
		// ----- unary -----
		// -----------------
		[(ushort)CoreOpCode.Negate] = static state =>
		{
			var a = state.Pop();
			state.Push(Value.FromInt(-a.Int));
		},
		[(ushort)CoreOpCode.Not] = static state =>
		{
			var a = state.Pop();
			state.Push(Value.FromBool(!a.Bool));
		},
		[(ushort)CoreOpCode.IncrementStoreOrigin] = static state =>
		{
			var a = state.Pop();
			state.SetLocal(state.Operand, Value.FromInt(a.Int + 1));
			state.Push(in a);
		},
		[(ushort)CoreOpCode.IncrementStoreResult] = static state =>
		{
			var a = state.Pop();
			state.SetLocal(state.Operand, Value.FromInt(a.Int + 1));
			state.Push(Value.FromInt(a.Int + 1));
		},
		[(ushort)CoreOpCode.DecrementStoreOrigin] = static state =>
		{
			var a = state.Pop();
			state.SetLocal(state.Operand, Value.FromInt(a.Int - 1));
			state.Push(in a);
		},
		[(ushort)CoreOpCode.DecrementStoreResult] = static state =>
		{
			var a = state.Pop();
			state.SetLocal(state.Operand, Value.FromInt(a.Int - 1));
			state.Push(Value.FromInt(a.Int - 1));
		},

		// -----------------------
		// ----- comparisons -----
		// -----------------------
		[(ushort)CoreOpCode.Equal] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromBool(a == b));
		},
		[(ushort)CoreOpCode.NotEqual] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromBool(a != b));
		},
		[(ushort)CoreOpCode.LessThan] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromBool(a.Int < b.Int));
		},
		[(ushort)CoreOpCode.GreaterThan] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromBool(a.Int > b.Int));
		},
		[(ushort)CoreOpCode.LessOrEqual] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromBool(a.Int <= b.Int));
		},
		[(ushort)CoreOpCode.GreaterOrEqual] = static state =>
		{
			var b = state.Pop();
			var a = state.Pop();
			state.Push(Value.FromBool(a.Int >= b.Int));
		},

		[(ushort)CoreOpCode.Jump] = state =>
		{
			// -1 to ignore the current extra next call
			state.Jump(state.Operand - 1);
		},
		[(ushort)CoreOpCode.JumpIfFalse] = static state =>
		{
			var cond = state.Pop();
			if (!cond.Bool)
			{
				// -1 to ignore the current extra next call
				state.Jump(state.Operand - 1);
			}
		},

		// -----------------
		// ----- calls -----
		// -----------------
		[(ushort)CoreOpCode.Call] = static state =>
		{
			var method = state.Context.Methods[state.Operand];
			state.Call(method);
		},
		[(ushort)CoreOpCode.Goto] = static state =>
		{
			var method = state.Context.Methods[state.Operand];
			state.Goto(method);
		},
		[(ushort)CoreOpCode.Return] = static state =>
		{
			state.Return();
		},
	};
}