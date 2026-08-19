namespace GameScript.Bytecode
{
	public sealed class BytecodeMethod(string name, ushort[] ops, int[] operands, int paramCount, int localsCount, int returnCount, ValueType[]? paramTypes = null)
	{
		public readonly string Name = name;
		public readonly ushort[] Ops = ops;
		public readonly int[] Operands = operands;
		public readonly int ParamCount = paramCount;
		public readonly int LocalsCount = localsCount;
		public readonly int ReturnCount = returnCount;

		/// <summary>
		/// Declared type of each parameter, positionally aligned with locals 0..ParamCount-1
		/// (label parameters are reported as <see cref="ValueType.Int"/>). Null when the
		/// program predates parameter typing (legacy bytecode); hosts that bind arguments
		/// by position should treat null as "unknown".
		/// </summary>
		public readonly ValueType[]? ParamTypes = paramTypes;
	}
}
