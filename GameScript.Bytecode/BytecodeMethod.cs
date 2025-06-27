namespace GameScript.Bytecode
{
	public sealed class BytecodeMethod(string name, ushort[] ops, int[] operands, int paramCount, int localsCount, int returnCount, string filePath)
	{
		public readonly string Name = name;
		public readonly ushort[] Ops = ops;
		public readonly int[] Operands = operands;
		public readonly int ParamCount = paramCount;
		public readonly int LocalsCount = localsCount;
		public readonly int ReturnCount = returnCount;
		public readonly string FilePath = filePath;
	}
}
