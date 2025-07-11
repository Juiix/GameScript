using System;

namespace GameScript.Bytecode;

public sealed class ScriptRunner<TContext>(Action<ScriptState<TContext>>[] handlers) where TContext : IScriptContext
{
	private readonly Action<ScriptState<TContext>>[] _handlers = handlers;

	public ScriptExecution Run(ScriptState<TContext> state)
	{
		state.Execution = ScriptExecution.Running;
		try
		{
			while (state.Execution == ScriptExecution.Running)
			{
				state.Next();
				var handler = _handlers[state.OpCode] ??
					throw new NotImplementedException($"Operation not implemented for OpCode: {state.OpCode}");
				handler(state);
			}
		}
		catch (Exception)
		{
			state.Execution = ScriptExecution.Aborted;
			throw;
		}
		return state.Execution;
	}
}
