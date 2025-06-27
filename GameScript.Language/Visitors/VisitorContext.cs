using GameScript.Language.Index;

namespace GameScript.Language.Visitors
{
	public class VisitorContext(ITypeIndex types, ISymbolIndex symbols, string filePath)
	{
		public ITypeIndex Types { get; } = types;
		public ISymbolIndex Symbols { get; } = symbols;
		public string FilePath { get; } = filePath;
	}
}
