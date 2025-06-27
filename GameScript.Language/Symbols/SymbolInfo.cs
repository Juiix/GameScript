using System;
using System.Collections.Generic;
using GameScript.Language.Ast;
using GameScript.Language.File;

namespace GameScript.Language.Symbols
{
	public sealed class SymbolInfo(
		IdentifierType identifierType,
		string name,
		TypeInfo? type,
		List<string>? typeNames,
		TypeInfo? paramTypes,
		List<string>? paramNames,
		string? summary,
		string filePath,
		FileRange fileRange)
	{
		public IdentifierType IdentifierType { get; } = identifierType;
		public string Name { get; } = name;
		public TypeInfo? Type { get; } = type;
		public List<string>? TypeNames { get; } = typeNames;
		public TypeInfo? ParamTypes { get; } = paramTypes;
		public List<string>? ParamNames { get; } = paramNames;
		public string? Summary { get; } = summary;
		public string FilePath { get; } = filePath;
		public FileRange FileRange { get; } = fileRange;
		public string Signature { get; } = CreateSignature(identifierType, name, type, typeNames, paramTypes, paramNames);

		public bool IsGlobalSymbol => IdentifierType == IdentifierType.Func ||
			IdentifierType == IdentifierType.Label ||
			IdentifierType == IdentifierType.Command ||
			IdentifierType == IdentifierType.Constant ||
			IdentifierType == IdentifierType.Context;
		public string PrefixedName => $"{GetPrefix(IdentifierType)}{Name}";

		private static string CreateSignature(
			IdentifierType identifierType,
			string name,
			TypeInfo? type,
			List<string>? typeNames,
			TypeInfo? paramTypes,
			List<string>? paramNames)
		{
			Span<char> buffer = stackalloc char[64];
			var vsb = new ValueStringBuilder(buffer);
			if ((identifierType & IdentifierType.Method) != IdentifierType.Unknown)
			{
				if (identifierType != IdentifierType.Trigger)
				{
					vsb.Append(GetIdentifierKeyword(identifierType));
					vsb.Append(' ');
				}
				vsb.Append(name);

				vsb.Append('(');
				if (paramTypes != null)
				{
					AppendTypes(ref vsb, paramTypes, paramNames);
				}
				vsb.Append(')');

				if (type != null)
				{
					vsb.Append(" returns ");
					if (type.TypeParameters?.Count > 0)
					{
						vsb.Append('(');
					}
					AppendTypes(ref vsb, type, typeNames);
					if (type.TypeParameters?.Count > 0)
					{
						vsb.Append(')');
					}
				}
			}
			else
			{
				vsb.Append(type?.Name ?? "?");
				vsb.Append(' ');
				vsb.Append(GetPrefix(identifierType));
				vsb.Append(name);
			}

			return vsb.ToString();
		}

		private static string GetPrefix(IdentifierType identifierType)
		{
			return identifierType switch
			{
				IdentifierType.Func => "~",
				IdentifierType.Label => "@",
				IdentifierType.Local => "$",
				IdentifierType.Context => "%",
				IdentifierType.Constant => "^",
				_ => string.Empty,
			};
		}

		private static string GetIdentifierKeyword(IdentifierType identifierType)
		{
			return identifierType switch
			{
				IdentifierType.Func => "func",
				IdentifierType.Command => "command",
				IdentifierType.Label => "label",
				IdentifierType.Local => "local",
				IdentifierType.Context => "context",
				IdentifierType.Constant => "constant",
				_ => "unknown"
			};
		}

		private static void AppendTypes(ref ValueStringBuilder vsb, TypeInfo types, List<string>? names)
		{
			int paramCount = 0;
			foreach (var type in types.AllTypes)
			{
				if (paramCount++ > 0)
				{
					vsb.Append(", ");
				}
				vsb.Append(type.Name);
				if (names != null &&
					!string.IsNullOrWhiteSpace(names[paramCount - 1]))
				{
					vsb.Append(' ');
					vsb.Append('$');
					vsb.Append(names[paramCount - 1]);
				}
			}
		}
	}
}
