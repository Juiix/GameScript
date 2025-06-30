namespace GameScript.Bytecode
{
	public sealed class BytecodeProgramMetadata(BytecodeMethodMetadata[] methodMetadata)
	{
		public readonly BytecodeMethodMetadata[] MethodMetadata = methodMetadata;
	}
}
