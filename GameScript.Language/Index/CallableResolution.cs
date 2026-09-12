using System.Collections.Generic;
using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.Symbols;

namespace GameScript.Language.Index
{
	public enum CallableResolutionStatus
	{
		/// <summary>Exactly one overload matched (or one matched strictly better than the rest).</summary>
		Match,
		/// <summary>No callable symbol with that name exists.</summary>
		NotFound,
		/// <summary>Callables exist, but none match the argument count/types.</summary>
		NoOverloadMatches,
		/// <summary>
		/// More than one overload matched equally well: unknown argument types, or a
		/// conversion (a named type widening, a zero literal) that applies to several
		/// overloads with no exact match to prefer.
		/// </summary>
		Ambiguous,
	}

	/// <summary>
	/// One call-site argument: its inferred type (null = unknown, matches anything) and
	/// whether it is the literal '0' / '""', which converts to every named type over
	/// that root.
	/// </summary>
	public readonly record struct CallArgument(TypeInfo? Type, bool IsZeroLiteral);

	public static class CallableResolutionExtensions
	{
		public static bool IsCallable(this SymbolInfo symbol) =>
			(symbol.IdentifierType & IdentifierType.Method) != IdentifierType.Unknown &&
			symbol.IdentifierType != IdentifierType.Trigger &&
			symbol.IdentifierType != IdentifierType.TriggerDeclaration;

		/// <summary>
		/// Resolves a call to <paramref name="name"/> against all callable overloads by
		/// exact type match only (no conversions). A null entry in
		/// <paramref name="argTypes"/> is a wildcard (unknown at the call site — e.g.
		/// mid-keystroke in the LSP) and matches any parameter type. A null
		/// <paramref name="argTypes"/> list skips signature filtering entirely and
		/// returns the first callable.
		/// </summary>
		public static SymbolInfo? ResolveCallable(
			this ISymbolIndex index,
			string name,
			IReadOnlyList<TypeInfo?>? argTypes,
			out CallableResolutionStatus status)
		{
			return index.ResolveCallable(
				name,
				argTypes?.Select(x => new CallArgument(x, false)).ToList(),
				null,
				out status);
		}

		/// <summary>
		/// Resolves a call with the full assignability rules: an exact match on every
		/// argument outranks a match that needs a conversion (a named type widening to
		/// its root, or a zero literal converting to a named type). Among the best-ranked
		/// candidates, exactly one is a <see cref="CallableResolutionStatus.Match"/>;
		/// several are <see cref="CallableResolutionStatus.Ambiguous"/>.
		/// <paramref name="types"/> resolves named parameter types; null disables
		/// conversions (exact matches only).
		/// </summary>
		public static SymbolInfo? ResolveCallable(
			this ISymbolIndex index,
			string name,
			IReadOnlyList<CallArgument>? args,
			ITypeIndex? types,
			out CallableResolutionStatus status)
		{
			SymbolInfo? first = null;
			SymbolInfo? resolved = null;
			int bestRank = int.MaxValue;
			int matchCount = 0;

			// materialize first: matching resolves named parameter types through the
			// same symbol table, whose enumerator holds a non-recursive read lock
			var candidates = index.GetSymbols(name).ToList();
			foreach (var symbol in candidates)
			{
				if (!symbol.IsCallable())
					continue;

				first ??= symbol;
				if (args == null)
					break;

				if (!Matches(symbol, args, types, out var rank))
					continue;

				if (rank < bestRank)
				{
					bestRank = rank;
					resolved = symbol;
					matchCount = 1;
				}
				else if (rank == bestRank)
				{
					matchCount++;
				}
			}

			if (first == null)
			{
				status = CallableResolutionStatus.NotFound;
				return null;
			}

			if (args == null)
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

		// rank 0: every argument is an exact type match; rank 1: at least one argument
		// needs a conversion (widening or zero literal). Returns false on any mismatch.
		private static bool Matches(SymbolInfo symbol, IReadOnlyList<CallArgument> args, ITypeIndex? types, out int rank)
		{
			rank = 0;

			// trailing parameters with default values may be omitted at the call site
			if (args.Count < symbol.RequiredArity || args.Count > symbol.Arity)
				return false;

			int i = 0;
			if (symbol.ParamTypes != null)
			{
				foreach (var declaredType in symbol.ParamTypes.AllTypes)
				{
					if (i >= args.Count)
						break;                      // omitted defaulted parameters
					var arg = args[i++];
					if (arg.Type == null)
						continue;                   // wildcard

					var paramType = types?.Resolve(declaredType) ?? declaredType;
					if (arg.Type.Equals(paramType))
						continue;                   // exact

					if (types == null)
						return false;               // exact matches only

					var converts = TypeRules.IsAssignable(arg.Type, paramType) ||
						(paramType.IsNamed && arg.IsZeroLiteral &&
						 paramType.Underlying != null && arg.Type.Equals(paramType.Underlying));
					if (!converts)
						return false;
					rank = 1;
				}
			}
			return true;
		}
	}
}
