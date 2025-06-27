using GameScript.Language.Symbols;
using System;
using System.Collections.Generic;

namespace GameScript.Language.Index
{
	public sealed class GlobalTypeIndex : ITypeIndex
	{
		// The dictionary is keyed by the type name.
		private readonly Dictionary<string, TypeInfo> _typeCache = new(StringComparer.Ordinal);
		private readonly TypeInfo[] _primitiveTypes = new TypeInfo[3];

		public GlobalTypeIndex()
		{
			RegisterType("int", TypeKind.Int);
			RegisterType("string", TypeKind.String);
			RegisterType("bool", TypeKind.Bool);
		}

		public TypeInfo? GetType(string name)
		{
			return _typeCache.TryGetValue(name, out var cachedType) ? cachedType : null;
		}

		public TypeInfo? GetType(TypeKind typeKind)
		{
			var index = (int)typeKind;
			if (index < 0 || index >= _primitiveTypes.Length)
			{
				return null;
			}

			return _primitiveTypes[index];
		}

		private void RegisterType(string name, TypeKind kind)
		{
			var type = new TypeInfo(name, kind);
			_typeCache[name] = type;
			if ((int)kind < _primitiveTypes.Length)
			{
				_primitiveTypes[(int)kind] = type;
			}
		}
	}
}
