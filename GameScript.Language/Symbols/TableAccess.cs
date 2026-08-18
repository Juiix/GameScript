using GameScript.Language.Ast;
using GameScript.Language.Index;

namespace GameScript.Language.Symbols
{
	/// <summary>
	/// Resolves the table behind a lookup / member-access target. Shared by type
	/// analysis and the language server (hover, definition, completion) so every
	/// consumer agrees on what 't[k]', 't.at(i)' and a row cursor refer to.
	/// </summary>
	public static class TableAccess
	{
		/// <summary>The table symbol named <paramref name="name"/>, or null.</summary>
		public static SymbolInfo? GetTable(ISymbolIndex symbols, string name)
		{
			foreach (var symbol in symbols.GetSymbols(name))
			{
				if (symbol.IsTable)
					return symbol;
			}
			return null;
		}

		/// <summary>
		/// The table a target expression refers to: a bare table name, a keyed
		/// lookup, a positional '.at(i)', or a 'for r in t' row cursor local.
		/// Null when the target is none of those.
		/// </summary>
		public static SymbolInfo? ResolveTable(ExpressionNode target, ISymbolIndex? locals, ISymbolIndex symbols)
		{
			switch (target)
			{
				case IdentifierNode { Type: IdentifierType.Table } table:
					return GetTable(symbols, table.Name);

				case IdentifierNode { Type: IdentifierType.Local } cursor:
					var tableName = TableRowType.TryGetTableName(locals?.GetSymbol(cursor.Name)?.Type);
					return tableName != null ? GetTable(symbols, tableName) : null;

				case IndexExpressionNode index:
					return ResolveTable(index.Target, locals, symbols);

				case MemberExpressionNode { IsAt: true } at:
					return ResolveTable(at.Target, locals, symbols);

				case ParenthesizedExpressionNode paren:
					return ResolveTable(paren.Inner, locals, symbols);

				default:
					return null;
			}
		}

		/// <summary>
		/// True when the target denotes a table row (so a '.column' read is what
		/// must follow): a keyed lookup, '.at(i)', or a row cursor.
		/// </summary>
		public static bool IsRowTarget(ExpressionNode target, ISymbolIndex? locals)
		{
			return target switch
			{
				IndexExpressionNode => true,
				MemberExpressionNode { IsAt: true } => true,
				IdentifierNode { Type: IdentifierType.Local } cursor => TableRowType.IsRow(locals?.GetSymbol(cursor.Name)?.Type),
				ParenthesizedExpressionNode paren => IsRowTarget(paren.Inner, locals),
				_ => false,
			};
		}

		/// <summary>True when the target is the table itself (a bare table name).</summary>
		public static bool IsTableTarget(ExpressionNode target)
		{
			return target switch
			{
				IdentifierNode { Type: IdentifierType.Table } => true,
				ParenthesizedExpressionNode paren => IsTableTarget(paren.Inner),
				_ => false,
			};
		}
	}
}
