using System;
using System.Collections.Generic;
using System.Linq;
using GameScript.Bytecode;

namespace GameScript.Language.Tests.Harness;

/// <summary>
/// Fake host command set. Script names are derived from the enum case names by
/// CommandHandler (e.g. QueueStrongInt -> queue_strong_int), matching how real
/// hosts bind commands.
/// </summary>
public enum TestOp : ushort
{
	Print = 1000,
	Wait,
	IntToStr,
	QueueStrong,
	QueueStrongInt,
}

public sealed class TestScriptContext : IScriptContext
{
	private readonly Dictionary<int, Value> _values = [];

	public List<string> Printed { get; } = [];
	public List<(int MethodIndex, int Delay, Value[] Args)> Queued { get; } = [];

	public Value GetValue(int id) => _values.TryGetValue(id, out var v) ? v : default;
	public void SetValue(int id, in Value value) => _values[id] = value;
}

/// <summary>
/// Executes compiled programs against the TestOp command set and records
/// observable effects (prints, queues, suspensions).
/// </summary>
public sealed class TestHost
{
	private readonly ScriptRunner<TestScriptContext> _runner;

	public TestHost()
	{
		var builder = new ScriptRunnerBuilder<TestScriptContext>();
		builder.Register((ushort)TestOp.Print, static state =>
		{
			var value = state.Pop();
			state.Context!.Printed.Add(ValueToString(value));
		});
		builder.Register((ushort)TestOp.Wait, static state =>
		{
			state.Execution = ScriptExecution.Paused;
		});
		builder.Register((ushort)TestOp.IntToStr, static state =>
		{
			var value = state.Pop();
			state.Push(Value.FromString(value.Int.ToString()));
		});
		builder.Register((ushort)TestOp.QueueStrong, static state =>
		{
			var delay = state.Pop();
			var method = state.Pop();
			state.Context!.Queued.Add((method.Int, delay.Int, []));
		});
		builder.Register((ushort)TestOp.QueueStrongInt, static state =>
		{
			var arg0 = state.Pop();
			var delay = state.Pop();
			var method = state.Pop();
			state.Context!.Queued.Add((method.Int, delay.Int, [arg0]));
		});
		_runner = builder.Build();
	}

	public TestScriptContext Context { get; private set; } = new();
	public ScriptState<TestScriptContext> State { get; } = new();

	/// <summary>Starts the named method and runs until finished or paused.</summary>
	public ScriptExecution Start(BytecodeProgram program, string methodName, params Value[] args)
	{
		Context = new TestScriptContext();
		var method = program.Methods.FirstOrDefault(m => m.Name == methodName)
			?? throw new InvalidOperationException($"No method named '{methodName}' in program");
		State.Start(program, Context, method, args);
		return _runner.Run(State);
	}

	/// <summary>Resumes a paused script until finished or paused again.</summary>
	public ScriptExecution Resume() => _runner.Run(State);

	private static string ValueToString(Value v) => v.Type switch
	{
		GameScript.Bytecode.ValueType.String => v.String ?? string.Empty,
		GameScript.Bytecode.ValueType.Int => v.Int.ToString(),
		GameScript.Bytecode.ValueType.Bool => v.Bool.ToString(),
		_ => "null",
	};
}
