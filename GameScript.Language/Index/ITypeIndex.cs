using GameScript.Language.Symbols;

namespace GameScript.Language.Index
{
	public interface ITypeIndex
	{
		TypeInfo? GetType(string name);
		TypeInfo? GetType(TypeKind typeKind);
	}
}
