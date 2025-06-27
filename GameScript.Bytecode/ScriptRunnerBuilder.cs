using System;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Bytecode
{
	public sealed class ScriptRunnerBuilder
	{
		private readonly Dictionary<ushort, Action<ScriptState>> _handlers = new(CoreOps.Handlers);

		public ScriptRunner Build()
		{
			var max = _handlers.DefaultIfEmpty().Max(x => x.Key);
			var array = new Action<ScriptState>[max];
			foreach (var pair in _handlers)
				array[pair.Key] = pair.Value;
			return new ScriptRunner(array);
		}

		public void Register(ushort opCode, Action<ScriptState> handler)
		{
			if (opCode < 100)
				throw new InvalidOperationException("OpCodes < 100 are reserved for CoreOps");
			_handlers[opCode] = handler;
		}
	}
}
