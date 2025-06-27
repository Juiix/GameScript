using GameScript.Language.Symbols;
using System.Collections.Generic;

namespace GameScript.Language.Index
{
	public interface IReferenceIndex
	{
		IEnumerable<ReferenceInfo> References { get; }

		IEnumerable<ReferenceInfo> GetReferences(string symbol);
	}
}
