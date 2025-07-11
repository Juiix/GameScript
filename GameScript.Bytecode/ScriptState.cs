using System;
using System.Buffers;

namespace GameScript.Bytecode;

public sealed class ScriptState<TContext> : IDisposable where TContext : IScriptContext
{
	// debug fields
	private BytecodeMethod _entryMethod;
    private int _opCount = 0;

	private int _sp = 0;
	private int _fp = 0;

    private readonly Value[] _stack = ArrayPool<Value>.Shared.Rent(1024);
    private readonly CallFrame[] _frames = ArrayPool<CallFrame>.Shared.Rent(64);

    public ScriptState(BytecodeProgram program, TContext context, BytecodeMethod method, params Value[] args)
    {
		Program = program;
		Context = context;
        _entryMethod = method;
		foreach (ref var arg in args.AsSpan())
		{
			Push(in arg);
		}

        _frames[0] = new CallFrame(method, 0);
		OpCode = method.Ops[0];
		Operand = method.Operands[0];
		_sp = method.ParamCount + method.LocalsCount;
	}

	public BytecodeProgram Program { get; }
	public TContext Context { get; }
	public ScriptExecution Execution { get; set; } = ScriptExecution.Running;
	public ushort OpCode { get; private set; }
	public int Operand { get; private set; }

	private ref CallFrame CurrentFrame => ref _frames[_fp];

	public void Dispose()
	{
		ArrayPool<Value>.Shared.Return(_stack);
		ArrayPool<CallFrame>.Shared.Return(_frames);
	}

	public void Next()
	{
		ref var frame = ref CurrentFrame;
		frame.Ip++;
		_opCount++;
		OpCode = frame.CurrentOpCode;
		Operand = frame.CurrentOperand;
	}

	public ref Value GetLocal(int localIndex)
	{
		return ref _stack[CurrentFrame.StackStart + localIndex];
	}

	public void SetLocal(int localIndex, in Value value)
	{
		_stack[CurrentFrame.StackStart + localIndex] = value;
	}

	public void Push(in Value value)
	{
		try
		{
			_stack[_sp++] = value;
		}
		catch (IndexOutOfRangeException)
		{
			throw new StackOverflowException();
		}
	}

	public Value Pop()
	{
		var value = _stack[--_sp];
		_stack[_sp] = default;
		return value;
	}

	public void PopDiscard()
	{
		_stack[--_sp] = default;
	}

	public void Jump(int count)
	{
		CurrentFrame.Ip += count;
	}

	public void Call(BytecodeMethod method)
	{
		try
		{
			var frame = new CallFrame(method, _sp - method.ParamCount);
			_frames[++_fp] = frame;
			_sp += method.LocalsCount;
		}
		catch (IndexOutOfRangeException)
		{
			throw new StackOverflowException($"Exceeded {_frames.Length} call frames");
		}
	}

	public void Goto(BytecodeMethod method)
	{
		// clear frames (mainly to dereference the BytecodeMethods for GC)
		if (_fp > 0)
		{
			_frames.AsSpan(1, _fp).Clear();
		}

		var frame = new CallFrame(method, 0);
		_frames[0] = frame;
		_fp = 0;

		var span = _stack.AsSpan();

		// copy params to start of stack
		var paramCount = method.ParamCount;
		var paramValues = span.Slice(_sp - paramCount, paramCount);
		var paramValuesTarget = span.Slice(0, paramCount);
		paramValues.CopyTo(paramValuesTarget);

		// clear remaining stack
		span.Slice(paramCount, _sp - paramCount).Clear();

		// set stack pointer
		_sp = method.ParamCount + method.LocalsCount;
	}

	public void Return()
	{
		ref var frame = ref _frames[_fp--];
		var span = _stack.AsSpan();

		// copy return values to frame start
		var returnCount = frame.Method.ReturnCount;
		var returnValues = span.Slice(_sp - returnCount, returnCount);
		var returnValuesTarget = span.Slice(frame.StackStart, returnCount);
		returnValues.CopyTo(returnValuesTarget);

		// clear frame locals
		var clearStart = frame.StackStart + returnCount;
		var toClear = span.Slice(clearStart, _sp - clearStart);
		toClear.Clear();

		// set stack pointer
		_sp = frame.StackStart + returnCount;

		Execution = _fp < 0 ? ScriptExecution.Finished : Execution;

		frame = default; // clear value
	}
}