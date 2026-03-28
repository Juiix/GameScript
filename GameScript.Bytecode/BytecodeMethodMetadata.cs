namespace GameScript.Bytecode
{
	public sealed class BytecodeMethodMetadata(string name, int[] lineNumbers, string filePath, string[] localNames)
	{
		public readonly string Name = name;
		public readonly int[] LineNumbers = lineNumbers;
		public readonly string FilePath = filePath;
		/// <summary>
		/// Names of all locals indexed by slot: [param_0, ..., param_N, local_0, ..., local_M].
		/// Length equals ParamCount + LocalsCount of the corresponding <see cref="BytecodeMethod"/>.
		/// </summary>
		public readonly string[] LocalNames = localNames;
	}
}
