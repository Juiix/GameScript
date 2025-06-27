using System;

namespace GameScript.Bytecode
{
	public struct Value : IEquatable<Value>
	{
		public ValueType Type;

		// We store all small (primitive) data in this long.
		//   - ints go here
		//   - bools as 0/1
		private long _number;

		// We store object‐references (strings, tuples, user‐objects) here.
		// This is a single reference, so only strings or boxed custom
		//   types pay the boxing cost.
		private object? _obj;

		// factory methods:
		public static Value FromInt(int i) => new() { Type = ValueType.Int, _number = i };
		public static Value FromBool(bool b) => new() { Type = ValueType.Bool, _number = b ? 1 : 0 };
		public static Value FromString(string s) => new() { Type = ValueType.String, _obj = s };

		public static Value Null => new() { Type = ValueType.Null };

		// accessors:
		public int Int
		{
			get
			{
				if (Type == ValueType.Null) return 0;
				if (Type != ValueType.Int) throw new InvalidCastException();
				return (int)_number;
			}
		}

		public bool Bool
		{
			get
			{
				if (Type == ValueType.Null) return false;
				if (Type != ValueType.Bool) throw new InvalidCastException();
				return _number != 0;
			}
		}

		public string? String
		{
			get
			{
				if (Type == ValueType.Null) return string.Empty;
				if (Type != ValueType.String) throw new InvalidCastException();
				return (string)_obj!;
			}
		}

		public override string ToString()
		{
			return Type switch
			{
				ValueType.Int => Int.ToString(),
				ValueType.Bool => Bool.ToString(),
				ValueType.String => $"\"{String}\"",
				_ => "null",
			};
		}

		// IEquatable<Value>
		public bool Equals(Value other)
		{
			if (Type != other.Type)
				return false;

			return Type switch
			{
				ValueType.Int => _number == other._number,
				ValueType.Bool => _number == other._number,
				ValueType.String => string.Equals((string)_obj!, (string)other._obj!),
				ValueType.Null => true,
				_ => false,
			};
		}

		public override bool Equals(object? obj) =>
			obj is Value v && Equals(v);

		public override int GetHashCode()
		{
			// combine hash of Type and payload
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + Type.GetHashCode();
				hash = hash * 31 + GetValueHashCode();
				return hash;
			}
		}

		public static bool operator ==(Value a, Value b) => a.Equals(b);
		public static bool operator !=(Value a, Value b) => !a.Equals(b);

		private int GetValueHashCode()
		{
			return Type switch
			{
				ValueType.Int => _number.GetHashCode(),
				ValueType.Bool => _number.GetHashCode(),
				ValueType.String => (_obj?.GetHashCode() ?? 0),
				_ => 0,
			};
		}
	}
}
