namespace GameScript.Bytecode;

public sealed class ScriptGlobals(BytecodeMethod[] methods, Value[] constants)
{
	public BytecodeMethod[] Methods { get; } = methods;
	public Value[] Constants { get; } = constants;
}
