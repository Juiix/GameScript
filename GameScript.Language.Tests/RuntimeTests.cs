using GameScript.Bytecode;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Direct VM tests for the 2.0 runtime additions (Modulo, TailCall) using
/// hand-assembled bytecode.
/// </summary>
public class RuntimeTests
{
	private const ushort PauseOp = 1000;

	private sealed class NullContext : IScriptContext
	{
		public Value GetValue(int id) => default;
		public void SetValue(int id, in Value value) { }
	}

	private static ScriptRunner<NullContext> BuildRunner()
	{
		var builder = new ScriptRunnerBuilder<NullContext>();
		builder.Register(PauseOp, static state => state.Execution = ScriptExecution.Paused);
		return builder.Build();
	}

	private static BytecodeMethod Method(string name, ushort[] ops, int[] operands, int paramCount = 0, int localsCount = 0, int returnCount = 0) =>
		new(name, ops, operands, paramCount, localsCount, returnCount);

	[Theory]
	[InlineData(7, 3, 1)]
	[InlineData(9, 3, 0)]
	[InlineData(-7, 3, -1)]
	[InlineData(7, -3, 1)]
	public void Modulo_Matches_CSharp_Semantics(int a, int b, int expected)
	{
		var main = Method("main",
			[(ushort)CoreOpCode.LoadConstInt, (ushort)CoreOpCode.LoadConstInt, (ushort)CoreOpCode.Modulo, (ushort)CoreOpCode.Return],
			[a, b, 0, 0],
			returnCount: 1);
		var program = new BytecodeProgram([main], []);

		var state = new ScriptState<NullContext>();
		state.Start(program, new NullContext(), main);
		var execution = BuildRunner().Run(state);

		Assert.Equal(ScriptExecution.Finished, execution);
		Assert.Equal(expected, state.Peek().Int);
	}

	[Fact]
	public void TailCall_Replaces_Frame_And_Preserves_Caller()
	{
		// main() returns int:            Call a; Return          -> returns a()'s value
		// a() returns int:               LoadConstInt 9; TailCall b
		// b(int x) returns int:          Pause; LoadLocal 0; LoadConstInt 1; Add; Return
		var main = Method("main",
			[(ushort)CoreOpCode.Call, (ushort)CoreOpCode.Return],
			[1, 0],
			returnCount: 1);
		var a = Method("a",
			[(ushort)CoreOpCode.LoadConstInt, (ushort)CoreOpCode.TailCall],
			[9, 2],
			returnCount: 1);
		var b = Method("b",
			[PauseOp, (ushort)CoreOpCode.LoadLocal, (ushort)CoreOpCode.LoadConstInt, (ushort)CoreOpCode.Add, (ushort)CoreOpCode.Return],
			[0, 0, 1, 0, 0],
			paramCount: 1,
			returnCount: 1);
		var program = new BytecodeProgram([main, a, b], []);

		var state = new ScriptState<NullContext>();
		var runner = BuildRunner();
		state.Start(program, new NullContext(), main);
		var execution = runner.Run(state);

		// paused inside b: a's frame was REPLACED, so depth is main(0) -> b(1)
		Assert.Equal(ScriptExecution.Paused, execution);
		Assert.Equal(1, state.FrameDepth);
		Assert.Equal("b", state.CurrentFrameView.Method.Name);

		// b returns 10 to main, which returns it as the script result
		execution = runner.Run(state);
		Assert.Equal(ScriptExecution.Finished, execution);
		Assert.Equal(10, state.Peek().Int);
	}

	[Fact]
	public void TailCall_Loop_Does_Not_Grow_Frames()
	{
		// spin(int n): if n == 0 return; TailCall spin(n - 1)
		// ops: LoadLocal 0; JumpIfFalse +? ... simpler: LoadLocal 0; LoadConstInt 0; Equal; JumpIfFalse 3; Return; (fall) LoadLocal 0; LoadConstInt 1; Subtract; TailCall spin
		var spin = Method("spin",
			[
				(ushort)CoreOpCode.LoadLocal, (ushort)CoreOpCode.LoadConstInt, (ushort)CoreOpCode.Equal,
				(ushort)CoreOpCode.JumpIfFalse, (ushort)CoreOpCode.Return,
				(ushort)CoreOpCode.LoadLocal, (ushort)CoreOpCode.LoadConstInt, (ushort)CoreOpCode.Subtract,
				(ushort)CoreOpCode.TailCall,
			],
			[0, 0, 0, 2, 0, 0, 1, 0, 0],
			paramCount: 1);
		var program = new BytecodeProgram([spin], []);

		var state = new ScriptState<NullContext>();
		state.Start(program, new NullContext(), spin, Value.FromInt(500));
		var execution = BuildRunner().Run(state);

		// 500 self tail-calls with a 64-frame budget: only possible with frame replacement
		Assert.Equal(ScriptExecution.Finished, execution);
	}
}
