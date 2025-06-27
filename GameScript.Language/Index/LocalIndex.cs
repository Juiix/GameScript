using GameScript.Language.File;
using GameScript.Language.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Language.Index
{
	public sealed class LocalIndex(string filePath, in FileRange fileRange) : IReferenceTable, ISymbolTable
	{
		private readonly Dictionary<string, List<SymbolInfo>> _symbols = [];
		private readonly Dictionary<string, List<ReferenceInfo>> _references = [];

		public string FilePath { get; } = filePath;
		public FileRange FileRange { get; } = fileRange;
		public IEnumerable<ReferenceInfo> References => _references.Values.SelectMany(x => x);
		public IEnumerable<SymbolInfo> Symbols => _symbols.Values.SelectMany(x => x);

		public void AddReference(ReferenceInfo reference)
		{
			if (!_references.TryGetValue(reference.Name, out var referenceList))
			{
				referenceList = [];
				_references.Add(reference.Name, referenceList);
			}

			referenceList.Add(reference);
		}

		public void AddSymbol(SymbolInfo symbol)
		{
			if (!_symbols.TryGetValue(symbol.Name, out var symbolList))
			{
				symbolList = [];
				_symbols.Add(symbol.Name, symbolList);
			}

			symbolList.Add(symbol);
		}

		public IEnumerable<ReferenceInfo> GetReferences(string symbol)
		{
			return _references.TryGetValue(symbol, out var references) ? references : [];
		}

		public SymbolInfo? GetSymbol(string name)
		{
			return _symbols.TryGetValue(name, out var symbol) ? symbol[0] : null;
		}

		public IEnumerable<SymbolInfo> GetSymbols(string name)
		{
			return _symbols.TryGetValue(name, out var symbols) ? symbols : [];
		}
	}
}
