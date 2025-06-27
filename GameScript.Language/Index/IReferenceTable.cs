using GameScript.Language.Symbols;

namespace GameScript.Language.Index
{
	public interface IReferenceTable : IReferenceIndex
	{
		void AddReference(ReferenceInfo reference);
	}
}
