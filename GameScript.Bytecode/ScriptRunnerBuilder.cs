using System;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Bytecode;

public sealed class ScriptRunnerBuilder<TContext>() where TContext : IScriptContext
{
    private readonly Dictionary<ushort, IScriptHandler<TContext>> _handlers =
        CoreOps<TContext>.Handlers.ToDictionary(x => x.Key, x => (IScriptHandler<TContext>)new ActionScriptHandler(x.Value));

    public ScriptRunner<TContext> Build()
    {
        var max = _handlers.DefaultIfEmpty().Max(x => x.Key + 1);
        var array = new IScriptHandler<TContext>[max];
        foreach (var pair in _handlers)
            array[pair.Key] = pair.Value;
        return new ScriptRunner<TContext>(array);
    }

    public void Register(ushort opCode, IScriptHandler<TContext> handler)
    {
        if (opCode < 100)
            throw new InvalidOperationException("OpCodes < 100 are reserved for CoreOps");
        _handlers[opCode] = handler;
    }

    public void Register(ushort opCode, Action<ScriptState<TContext>> handler) =>
        Register(opCode, new ActionScriptHandler(handler));

    private sealed class ActionScriptHandler(Action<ScriptState<TContext>> action) : IScriptHandler<TContext>
    {
        public void Handle(ScriptState<TContext> state) => action(state);
    }
}
