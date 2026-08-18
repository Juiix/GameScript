using System.Collections.Generic;
using GameScript.Language.Ast;
using GameScript.Language.Index;

namespace GameScript.Language.Visitors
{
	/// <summary>
	/// Classifies bare identifiers (parsed as IdentifierType.Unknown) against the
	/// local and global symbol tables: locals/params first, then funcs/commands.
	/// Runs immediately after indexing and before every other analysis visitor —
	/// they (and the compiler) rely on IdentifierNode.Type being resolved.
	/// Unresolvable names stay Unknown and are reported by SemanticAnalysisVisitor.
	/// </summary>
	public sealed class NameResolutionVisitor(
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes,
		VisitorContext context) : AnalysisVisitorBase(localIndexes)
	{
		private readonly VisitorContext _context = context;

		public override void Visit(IdentifierNode node)
		{
			if (node.Type != IdentifierType.Unknown)
				return;

			if (LocalIndex?.GetSymbol(node.Name) != null)
			{
				node.ResolveType(IdentifierType.Local);
				return;
			}

			foreach (var symbol in _context.Symbols.GetSymbols(node.Name))
			{
				if (symbol.IsCallable() || symbol.IsTable)
				{
					node.ResolveType(symbol.IdentifierType);
					return;
				}
			}
			// stays Unknown → SemanticAnalysisVisitor reports it against the symbol table
		}
	}
}
