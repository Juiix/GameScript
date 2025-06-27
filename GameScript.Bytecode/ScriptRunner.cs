using System;

namespace GameScript.Bytecode
{
	public sealed class ScriptRunner(Action<ScriptState>[] handlers)
	{
		private readonly Action<ScriptState>[] _handlers = handlers;

		public ScriptExecution Run(ScriptState state)
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
}
