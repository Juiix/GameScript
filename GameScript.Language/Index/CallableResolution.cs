using System.Collections.Generic;
using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.Symbols;

namespace GameScript.Language.Index
{
	public enum CallableResolutionStatus
	{
		/// <summary>Exactly one overload matched.</summary>
		Match,
		/// <summary>No callable symbol with that name exists.</summary>
		NotFound,
		/// <summary>Callables exist, but none match the argument count/types.</summary>
		NoOverloadMatches,
		/// <summary>More than one overload matched (only possible with unknown argument types).</summary>
		Ambiguous,
	}

	public static class CallableResolutionExtensions
	{
		public static bool IsCallable(this SymbolInfo symbol) =>
			(symbol.IdentifierType & IdentifierType.Method) != IdentifierType.Unknown &&
			symbol.IdentifierType != IdentifierType.Trigger &&
			symbol.IdentifierType != IdentifierType.TriggerDeclaration;

		/// <summary>
		/// Resolves a call to <paramref name="name"/> against all callable overloads.
		/// A null entry in <paramref name="argTypes"/> is a wildcard (unknown at the
		/// call site — e.g. mid-keystroke in the LSP) and matches any parameter type.
		/// A null <paramref name="argTypes"/> list skips signature filtering entirely
		/// and returns the first callable.
		/// </summary>
		public static SymbolInfo? ResolveCallable(
			this ISymbolIndex index,
			string name,
			IReadOnlyList<TypeInfo?>? argTypes,
			out CallableResolutionStatus status)
		{
			SymbolInfo? first = null;
			SymbolInfo? resolved = null;
			int matchCount = 0;

			foreach (var symbol in index.GetSymbols(name))
			{
				if (!symbol.IsCallable())
					continue;

				first ??= symbol;
				if (argTypes == null)
					break;

				if (Matches(symbol, argTypes) && matchCount++ == 0)
					resolved = symbol;
			}

			if (first == null)
			{
				status = CallableResolutionStatus.NotFound;
				return null;
			}

			if (argTypes == null)
			{
				status = CallableResolutionStatus.Match;
				return first;
			}

			status = matchCount switch
			{
				0 => CallableResolutionStatus.NoOverloadMatches,
				1 => CallableResolutionStatus.Match,
				_ => CallableResolutionStatus.Ambiguous,
			};
			return resolved;
		}

		private static bool Matches(SymbolInfo symbol, IReadOnlyList<TypeInfo?> argTypes)
		{
			// trailing parameters with default values may be omitted at the call site
			if (argTypes.Count < symbol.RequiredArity || argTypes.Count > symbol.Arity)
				return false;

			int i = 0;
			if (symbol.ParamTypes != null)
			{
				foreach (var paramType in symbol.ParamTypes.AllTypes)
				{
					if (i >= argTypes.Count)
						break;                      // omitted defaulted parameters
					var argType = argTypes[i++];
					if (argType != null && !argType.Equals(paramType))
						return false;
				}
			}
			return true;
		}
	}
}
