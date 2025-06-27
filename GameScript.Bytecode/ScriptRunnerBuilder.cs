using System;
using System.Collections.Generic;

namespace GameScript.Bytecode
{
	public sealed class ScriptRunnerBuilder
	{
		private readonly Dictionary<ushort, Action<ScriptState>> _handlers = new(CoreOps.Handlers);

		public void Register(ushort opCode, Action<ScriptState> handler)
		{
			if (opCode < 100)
				throw new InvalidOperationException("OpCodes < 100 are reserved for CoreOps");
			_handlers[opCode] = handler;
		}
	}
}
