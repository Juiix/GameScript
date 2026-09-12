using GameScript.Language.Ast;
using GameScript.Language.Symbols;

namespace GameScript.Language.Index
{
	/// <summary>
	/// The type index of one compilation / project: the built-in primitives plus every
	/// named type ('type item : int') declared in the project's symbol table. Named
	/// types are ordinary global symbols (<see cref="IdentifierType.Type"/>), so they
	/// are per project, added and removed with their file, and visible regardless of
	/// the order files are indexed in. <see cref="Visitors.VisitorContext"/> wraps any
	/// plain type index in one of these automatically.
	/// </summary>
	public sealed class ProjectTypeIndex(ITypeIndex primitives, ISymbolIndex symbols) : ITypeIndex
	{
		/// <summary>The built-in types only (int, string, bool, func).</summary>
		public ITypeIndex Primitives { get; } = primitives;

		/// <summary>The project's symbol table, where named types are declared.</summary>
		public ISymbolIndex Symbols { get; } = symbols;

		/// <summary>
		/// A primitive by name, else the declared named type by name (its TypeInfo carries
		/// the root in <see cref="TypeInfo.Underlying"/>), else null.
		/// </summary>
		public TypeInfo? GetType(string name)
		{
			var primitive = Primitives.GetType(name);
			if (primitive != null)
				return primitive;

			foreach (var symbol in Symbols.GetSymbols(name))
			{
				if (symbol.IdentifierType == IdentifierType.Type)
					return symbol.Type;
			}
			return null;
		}

		public TypeInfo? GetType(TypeKind typeKind) => Primitives.GetType(typeKind);
	}
}
