using GameScript.Language.Ast;
using GameScript.Language.File;

namespace GameScript.Language.Symbols
{
	public sealed class ReferenceInfo(
		string name,
		string filePath,
		FileRange fileRange)
	{
		public string Name { get; } = name;
		public string FilePath { get; } = filePath;
		public FileRange FileRange { get; } = fileRange;
	}
}
