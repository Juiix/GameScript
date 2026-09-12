namespace GameScript.Language.Symbols
{
	public enum TypeKind
	{
		Int,
		String,
		Bool,
		Label,
		Tuple = 100,
		// the row cursor of a 'for r in table' loop — never a value; only 'r.col' is valid
		TableRow = 101,
		// a named type ('type item : int'): an alias over an int/string root. Erases to the
		// root at codegen. TypeInfo.Underlying is null while the alias is unresolved (index
		// time) and the root TypeInfo once resolved against the declaring symbol.
		Named = 102
	}
}
