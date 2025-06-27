using System.Collections.Generic;

namespace GameScript.Bytecode
{
	public sealed class BytecodeProgram(BytecodeMethod[] methods, IReadOnlyDictionary<string, int> methodIndex, Value[] constants)
	{
		public BytecodeMethod[] Methods { get; } = methods;
		public IReadOnlyDictionary<string, int> MethodIndex { get; } = methodIndex;
		public Value[] Constants { get; } = constants;
	}
}
