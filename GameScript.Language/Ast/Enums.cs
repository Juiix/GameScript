using System;

namespace GameScript.Language.Ast
{
	[Flags]
	public enum BinaryOperator
	{
		Unknown = 0,
		Add = 1 << 0,
		Subtract = 1 << 1,
		Multiply = 1 << 2,
		Divide = 1 << 3,
		EqualTo = 1 << 4,
		NotEqualTo = 1 << 5,
		GreaterThan = 1 << 6,
		LessThan = 1 << 7,
		GreaterThanOrEqual = 1 << 8,
		LessThanOrEqual = 1 << 9,

		Operational = Add | Subtract | Multiply | Divide,
		Relational = EqualTo | NotEqualTo | GreaterThan | LessThan | GreaterThanOrEqual | LessThanOrEqual
	}

	public enum UnaryOperator
	{
		Unknown = 0,
		Not = 1 << 0,
		Negate = 1 << 1,
		Increment = 1 << 2,
		Decrement = 1 << 3,

		Numeric = Increment | Decrement,
	}

	public enum AssignmentOperator
	{
		Unknown,
		Assign,
		Add,        // +=
		Subtract,   // -=
		Multiply,   // *=
		Divide      // /=
	}

	public enum LiteralType
	{
		Unknown,
		String,
		Number,
		Boolean
	}

	[Flags]
	public enum IdentifierType
	{
		Unknown,
		Local = 1 << 0,
		Constant = 1 << 1,
		Context = 1 << 2,

		Func = 1 << 5,
		Label = 1 << 6,
		Command = 1 << 7,
		Trigger = 1 << 8,

		Variable = Local | Context | Constant,
		Method = Func | Label | Command | Trigger,

		Assignable = Local | Context
	}
}
