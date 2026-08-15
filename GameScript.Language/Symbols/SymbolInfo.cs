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
		object? literalValue,
		string filePath,
		FileRange fileRange,
		string? internalName = null,
		int defaultCount = 0,
		List<string?>? paramDefaultLabels = null)
	{
		public IdentifierType IdentifierType { get; } = identifierType;
		public string Name { get; } = name;
		public TypeInfo? Type { get; } = type;
		public List<string>? TypeNames { get; } = typeNames;
		public TypeInfo? ParamTypes { get; } = paramTypes;
		public List<string>? ParamNames { get; } = paramNames;
		public string? Summary { get; } = summary;
		public object? LiteralValue { get; } = literalValue;
		public string FilePath { get; } = filePath;
		public FileRange FileRange { get; } = fileRange;

		/// <summary>
		/// Engine-op name a command declaration binds to via '= name'
		/// (e.g. 'command queue(func m, int d, int a0) = queue_int'). Null when
		/// the declaration binds by its own name (the default).
		/// </summary>
		public string? InternalName { get; } = internalName;

		/// <summary>Number of trailing parameters that declare a default value.</summary>
		public int DefaultCount { get; } = defaultCount;

		/// <summary>
		/// Display text of each parameter's default value ('""', '-1', '^anim_still'),
		/// positionally aligned with ParamNames; null entries have no default.
		/// </summary>
		public List<string?>? ParamDefaultLabels { get; } = paramDefaultLabels;

		public string Signature { get; } = CreateSignature(identifierType, name, type, typeNames, paramTypes, paramNames, paramDefaultLabels);

		/// <summary>Number of declared parameters (methods only; 0 otherwise).</summary>
		public int Arity => ParamTypes == null ? 0 :
			ParamTypes.Kind == TypeKind.Tuple ? ParamTypes.TypeParameters!.Count : 1;

		/// <summary>Minimum number of call-site arguments (Arity minus defaulted parameters).</summary>
		public int RequiredArity => Arity - DefaultCount;

		/// <summary>
		/// Canonical parameter-type signature, e.g. "()", "(int)", "(int,string)".
		/// Return types deliberately excluded — they don't participate in overload identity.
		/// </summary>
		public string ParamSignature => ParamTypes == null ? "()" :
			ParamTypes.Kind == TypeKind.Tuple ? ParamTypes.Name : $"({ParamTypes.Name})";

		/// <summary>Name + ParamSignature; unique per overload.</summary>
		public string MangledName => $"{Name}{ParamSignature}";

		public bool IsGlobalSymbol => IdentifierType == IdentifierType.Func ||
			IdentifierType == IdentifierType.Label ||
			IdentifierType == IdentifierType.Command ||
			IdentifierType == IdentifierType.TriggerDeclaration ||
			IdentifierType == IdentifierType.Constant ||
			IdentifierType == IdentifierType.Context;
		public string PrefixedName => $"{GetPrefix(IdentifierType)}{Name}";

		private static string CreateSignature(
			IdentifierType identifierType,
			string name,
			TypeInfo? type,
			List<string>? typeNames,
			TypeInfo? paramTypes,
			List<string>? paramNames,
			List<string?>? paramDefaultLabels)
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
					AppendTypes(ref vsb, paramTypes, paramNames, paramDefaultLabels);
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
				IdentifierType.Context => "@",
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
				IdentifierType.TriggerDeclaration => "trigger",
				IdentifierType.Local => "local",
				IdentifierType.Context => "context",
				IdentifierType.Constant => "constant",
				_ => "unknown"
			};
		}

		private static void AppendTypes(ref ValueStringBuilder vsb, TypeInfo types, List<string>? names, List<string?>? defaultLabels = null)
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
					vsb.Append(names[paramCount - 1]);
				}
				if (defaultLabels != null &&
					paramCount - 1 < defaultLabels.Count &&
					defaultLabels[paramCount - 1] != null)
				{
					vsb.Append(" = ");
					vsb.Append(defaultLabels[paramCount - 1]!);
				}
			}
		}
	}
}
