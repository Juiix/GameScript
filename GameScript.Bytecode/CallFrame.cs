namespace GameScript.Bytecode;

public struct CallFrame(BytecodeMethod method, int stackStart)
{
	public readonly BytecodeMethod Method = method;
	public readonly int StackStart = stackStart;
	public int Ip = -1;

	public readonly ushort CurrentOpCode => Method.Ops[Ip];
	public readonly int CurrentOperand => Method.Operands[Ip];

	public readonly bool HasValidOp => Ip < Method.Ops.Length;
}
