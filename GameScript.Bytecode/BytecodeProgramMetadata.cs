namespace GameScript.Bytecode
{
	public sealed class BytecodeProgramMetadata(BytecodeMethodMetadata[] methodMetadata, (string Name, int Slot)[] contextNames)
	{
		public readonly BytecodeMethodMetadata[] MethodMetadata = methodMetadata;
		/// <summary>
		/// Names and slot IDs of all context variables, in slot order.
		/// </summary>
		public readonly (string Name, int Slot)[] ContextNames = contextNames;
	}
}
