using GameScript.Language.Symbols;
using System.Collections.Generic;

namespace GameScript.Language.Index
{
	public static class IndexExtensions
	{
		public static TypeInfo? GetTuple(this ITypeIndex typeIndex, IEnumerable<string>? typeNames)
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
				var type = typeIndex.GetType(typeName);
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
	}
}
