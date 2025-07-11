using System.Collections.Generic;

namespace GameScript.Bytecode;

public sealed class BytecodeProgram(BytecodeMethod[] methods, Value[] constants)
{
	public BytecodeMethod[] Methods { get; } = methods;
	public Value[] Constants { get; } = constants;
}
