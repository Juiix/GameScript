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
		LoadCtx,
		StoreLocal,
		StoreCtx,
		Pop,

		// Arithmetic
		Add,
		Subtract,
		Multiply,
		Divide,

		// Unary
		Negate,    // arithmetic negation: -x
		Not,       // logical not: !x

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
		JumpIfFalseKeep,  // like JumpIfFalse but doesn't pop the value
		JumpIfTrueKeep,   // like JumpIfTrue but doesn't pop the value

		// Calls
		Call,
		Goto,
		Return,

		// Method references
		LoadMethodRef   // push method index (operand) as int onto the stack
	}
}
