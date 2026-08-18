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
		TableRow = 101
	}
}
