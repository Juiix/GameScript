using System;

namespace GameScript.Bytecode;

public sealed class ScriptRunner<TContext>(IScriptHandler<TContext>[] handlers) : IScriptRunner<TContext> where TContext : IScriptContext
{
    private readonly IScriptHandler<TContext>[] _handlers = handlers;

    public IScriptHandler<TContext>? GetHandler(ushort opCode) =>
        (uint)opCode < (uint)_handlers.Length ? _handlers[opCode] : null;

    public ScriptExecution Run(ScriptState<TContext> state)
    {
        if (state.Program is null)
            throw new InvalidOperationException("ScriptState has no program assigned. Call Start() before running.");

        state.Execution = ScriptExecution.Running;
        try
        {
            while (state.Execution == ScriptExecution.Running)
            {
                state.Next();
                var handler = _handlers[state.OpCode] ??
                    throw new NotImplementedException($"Operation not implemented for OpCode: {state.OpCode}");
                handler.Handle(state);
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
