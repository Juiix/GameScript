using GameScript.Bytecode;

namespace GameScript.Language.Bytecode;

public readonly record struct BytecodeCompilerResult(
	BytecodeProgram Program,
	BytecodeProgramMetadata Metadata);
