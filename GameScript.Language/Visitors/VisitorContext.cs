using GameScript.Language.Index;

namespace GameScript.Language.Visitors
{
	public class VisitorContext(ITypeIndex types, ISymbolIndex symbols, string filePath)
	{
		/// <summary>
		/// The compilation's type index: the given primitives plus the named types
		/// declared in <see cref="Symbols"/> (a plain index is wrapped in a
		/// <see cref="ProjectTypeIndex"/>, so hosts keep passing their primitive index).
		/// </summary>
		public ITypeIndex Types { get; } = types as ProjectTypeIndex ?? new ProjectTypeIndex(types, symbols);
		public ISymbolIndex Symbols { get; } = symbols;
		public string FilePath { get; } = filePath;
	}
}
