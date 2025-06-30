namespace GameScript.Bytecode
{
	public sealed class BytecodeMethodMetadata(string name, int[] lineNumbers, string filePath)
	{
		public readonly string Name = name;
		public readonly int[] LineNumbers = lineNumbers;
		public readonly string FilePath = filePath;
	}
}
