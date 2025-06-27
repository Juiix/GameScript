namespace GameScript.Bytecode
{
	public enum CoreOpCode : ushort
	{
		/* ========================== */
		/*       Core Ops (0-99)      */
		/* ========================== */

		// Stack/frame
		LoadConst = 0,
		LoadConstInt,
		LoadConstBool,
		LoadLocal,
		StoreLocal,
		Pop,

		// Arithmetic
		Add,
		Subtract,
		Multiply,
		Divide,

		// Unary
		Negate,    // arithmetic negation: -x
		Not,       // logical not: !x
		IncrementStoreOrigin,
		IncrementStoreResult,
		DecrementStoreOrigin,
		DecrementStoreResult,

		// Comparisons
		Equal,
		NotEqual,
		LessThan,
		GreaterThan,
		LessOrEqual,
		GreaterOrEqual,

		// Control flow
		Jump,
		JumpIfFalse,

		// Calls
		Call,
		Goto,
		Return
	}
}
