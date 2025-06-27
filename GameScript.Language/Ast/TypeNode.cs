using System;
using GameScript.Language.File;
using GameScript.Language.Visitors;

namespace GameScript.Language.Ast
{
	public sealed class TypeNode(
		string name,
		string filePath,
		in FileRange fileRange) : AstNode(filePath, in fileRange), IEquatable<TypeNode>
	{
		public string Name { get; } = name;

		public override void Accept(IAstVisitor visitor)
		{
			visitor.Visit(this);
		}

		public bool Equals(TypeNode? other)
		{
			// Check for null and return false if other is null
			if (other is null)
				return false;

			// If they're the same reference, they are equal.
			if (ReferenceEquals(this, other))
				return true;

			// Equality can be defined solely by the type name,
			// or you can also factor in other properties (like FileRange) if needed.
			return string.Equals(Name, other.Name, StringComparison.Ordinal);
		}

		public override bool Equals(object? obj)
		{
			return Equals(obj as TypeNode);
		}

		public override int GetHashCode()
		{
			// Use the type name's hash code; adjust if you include additional fields.
			return Name != null ? Name.GetHashCode() : 0;
		}

		public static bool operator ==(TypeNode left, TypeNode right)
		{
			if (ReferenceEquals(left, right))
				return true;
			if (left is null || right is null)
				return false;
			return left.Equals(right);
		}

		public static bool operator !=(TypeNode left, TypeNode right)
		{
			return !(left == right);
		}

		public override string ToString()
		{
			return Name;
		}
	}
}
