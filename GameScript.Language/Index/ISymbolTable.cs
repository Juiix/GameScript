using GameScript.Language.Symbols;

namespace GameScript.Language.Index
{
	public interface ISymbolTable : ISymbolIndex
	{
		void AddSymbol(SymbolInfo symbol);
	}
}
