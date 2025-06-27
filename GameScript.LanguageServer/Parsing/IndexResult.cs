using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;

namespace GameScript.LanguageServer.Parsing
{
	internal sealed class IndexResult(
		FileIndex fileIndex,
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes,
		List<FileError> errors)
	{
		public FileIndex FileIndex { get; } = fileIndex;
		public IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> LocalIndexes { get; } = localIndexes;
		public List<FileError> Errors { get; } = errors;
	}
}
