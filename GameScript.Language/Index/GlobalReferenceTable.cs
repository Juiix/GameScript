using GameScript.Language.Symbols;
using System.Collections.Generic;

namespace GameScript.Language.Index
{
	public sealed class GlobalReferenceTable : ConcurrentFileSymbolTable<ReferenceInfo>, IReferenceIndex
	{
		public IEnumerable<ReferenceInfo> References => Values;
		public IEnumerable<ReferenceInfo> GetReferences(string symbol) => GetValues(symbol);
	}
}
