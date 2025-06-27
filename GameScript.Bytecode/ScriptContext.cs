namespace GameScript.Bytecode;

public sealed class ScriptContext(BytecodeMethod[] methods, Value[] constants)
{
	public BytecodeMethod[] Methods { get; } = methods;
	public Value[] Constants { get; } = constants;
}
