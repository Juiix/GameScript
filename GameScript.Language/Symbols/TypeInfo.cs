using GameScript.Language.Index;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Language.Symbols
{
	/// <summary>
	/// Constructs a new instance of TypeInfo.
	/// </summary>
	/// <param name="name">The type name (e.g., "int", "string").</param>
	/// <param name="kind">The kind of type.</param>
	/// <param name="typeParameters">Optional type parameters for composite types.</param>
	public sealed class TypeInfo(
		string name,
		TypeKind kind,
		IEnumerable<TypeInfo>? typeParameters = null) : IEquatable<TypeInfo>
	{
		/// <summary>
		/// The name of the type (e.g., "int", "string", or a custom type name).
		/// </summary>
		public string Name { get; } = name;

		/// <summary>
		/// The kind of the type, e.g., Int, String, Tuple.
		/// </summary>
		public TypeKind Kind { get; } = kind;

		/// <summary>
		/// For composite types (such as generic types, function types, or tuple types), 
		/// this list holds the type parameters.
		/// </summary>
		public IReadOnlyList<TypeInfo>? TypeParameters { get; } = typeParameters?.ToList();

		public IEnumerable<TypeInfo> AllTypes
		{
			get
			{
				if (TypeParameters == null || TypeParameters.Count == 0)
				{
					yield return this;
				}
				else
				{
					foreach (var type in TypeParameters)
					{
						yield return type;
					}
				}
			}
		}

		public override bool Equals(object? obj)
		{
			return Equals(obj as TypeInfo);
		}

		public bool Equals(TypeInfo? other)
		{
			if (other is null) return false;
			return string.Equals(Name, other.Name, StringComparison.Ordinal);
		}

		public override int GetHashCode()
		{
			return Name.GetHashCode();
		}

		public static bool operator ==(TypeInfo? left, TypeInfo? right)
		{
			if (ReferenceEquals(left, right))
				return true;
			return left?.Equals(right) ?? false;
		}

		public static bool operator !=(TypeInfo? left, TypeInfo? right)
		{
			return !(left == right);
		}

		public override string ToString() => Name;
	}
}
