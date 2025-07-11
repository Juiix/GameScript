using System;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Bytecode;

public sealed class ScriptRunnerBuilder<TContext>() where TContext : IScriptContext
{
	private readonly Dictionary<ushort, Action<ScriptState<TContext>>> _handlers = new(CoreOps<TContext>.Handlers);

	public ScriptRunner<TContext> Build()
	{
		var max = _handlers.DefaultIfEmpty().Max(x => x.Key + 1);
		var array = new Action<ScriptState<TContext>>[max];
		foreach (var pair in _handlers)
			array[pair.Key] = pair.Value;
		return new ScriptRunner<TContext>(array);
	}

	public void Register(ushort opCode, Action<ScriptState<TContext>> handler)
	{
		if (opCode < 100)
			throw new InvalidOperationException("OpCodes < 100 are reserved for CoreOps");
		_handlers[opCode] = handler;
	}
}
