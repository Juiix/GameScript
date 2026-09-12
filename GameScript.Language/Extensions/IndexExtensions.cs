using GameScript.Language.Symbols;
using System.Collections.Generic;

namespace GameScript.Language.Index
{
	public static class IndexExtensions
	{
		/// <summary>
		/// A tuple type over the named element types, a single type for one name, or
		/// null for no names. An unknown element name yields null — unless
		/// <paramref name="placeholders"/> is set, in which case it becomes an
		/// unresolved named type (the index pass records named types by name only and
		/// analysis resolves them; see <see cref="Resolve"/>).
		/// </summary>
		public static TypeInfo? GetTuple(this ITypeIndex typeIndex, IEnumerable<string>? typeNames, bool placeholders = false)
		{
			if (typeNames == null)
			{
				return null;
			}

			TypeInfo? first = null;
			List<TypeInfo>? tuple = null;
			List<string>? collectedNames = null;
			foreach (var typeName in typeNames)
			{
				var type = placeholders ? typeIndex.GetTypeOrPlaceholder(typeName) : typeIndex.GetType(typeName);
				if (type is null)
				{
					return null;
				}

				if (first is null)
				{
					first = type;
				}
				else
				{
					collectedNames ??= [first.Name];
					collectedNames.Add(typeName);
					tuple ??= [first];
					tuple.Add(type);
				}
			}

			if (tuple == null)
			{
				return first;
			}

			var tupleTypeName = $"({string.Join(",", collectedNames!)})";
			var tupleType = new TypeInfo(tupleTypeName, TypeKind.Tuple, tuple);
			return tupleType;
		}

		/// <summary>The built-in type of that name (int, string, bool, func), or null.</summary>
		public static TypeInfo? GetPrimitive(this ITypeIndex typeIndex, string name) =>
			typeIndex is ProjectTypeIndex project ? project.Primitives.GetType(name) : typeIndex.GetType(name);

		/// <summary>
		/// Index-time type lookup: a primitive by name, otherwise an unresolved named
		/// type. Never bakes a resolved named type into a symbol, so symbols stay
		/// correct however files are ordered and when a 'type' line changes its root.
		/// </summary>
		public static TypeInfo GetTypeOrPlaceholder(this ITypeIndex typeIndex, string name) =>
			typeIndex.GetPrimitive(name) ?? TypeInfo.Unresolved(name);

		/// <summary>
		/// Analysis-time view of a possibly unresolved type: a named-type placeholder
		/// becomes the declared type (root attached), tuples resolve element-wise, and
		/// everything else passes through. Null when the named type is not declared (or
		/// declared with an invalid root) so callers suppress cascades exactly as they
		/// do for an undefined type.
		/// </summary>
		public static TypeInfo? Resolve(this ITypeIndex typeIndex, TypeInfo? type)
		{
			if (type == null)
				return null;

			if (type.Kind == TypeKind.Named)
			{
				if (type.Underlying != null)
					return type;
				var declared = typeIndex.GetType(type.Name);
				return declared is { Kind: TypeKind.Named, Underlying: not null } ? declared : null;
			}

			if (type.Kind == TypeKind.Tuple && type.TypeParameters != null)
			{
				List<TypeInfo>? resolved = null;
				for (int i = 0; i < type.TypeParameters.Count; i++)
				{
					var element = typeIndex.Resolve(type.TypeParameters[i]);
					if (element == null)
						return null;
					if (resolved == null && !ReferenceEquals(element, type.TypeParameters[i]))
					{
						resolved = [];
						for (int j = 0; j < i; j++)
							resolved.Add(type.TypeParameters[j]);
					}
					resolved?.Add(element);
				}
				return resolved == null ? type : new TypeInfo(type.Name, TypeKind.Tuple, resolved);
			}

			return type;
		}
	}
}
