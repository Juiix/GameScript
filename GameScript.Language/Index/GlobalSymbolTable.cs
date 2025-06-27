using GameScript.Language.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Language.Index
{
	public sealed class GlobalSymbolTable : ConcurrentFileSymbolTable<SymbolInfo>, ISymbolIndex
	{
		public IEnumerable<SymbolInfo> Symbols => Values;
		public SymbolInfo? GetSymbol(string symbol) => GetValues(symbol).FirstOrDefault();
		public IEnumerable<SymbolInfo> GetSymbols(string symbol) => GetValues(symbol);
		public IEnumerable<SymbolInfo> GetSymbolsForFile(string filePath) => GetValuesForFile(filePath);
	}
}
