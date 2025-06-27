using GameScript.Language.Symbols;
using System.Collections.Generic;

namespace GameScript.Language.Index
{
	public interface ISymbolIndex
	{
		IEnumerable<SymbolInfo> Symbols { get; }

		SymbolInfo? GetSymbol(string name);
		IEnumerable<SymbolInfo> GetSymbols(string name);
	}
}
