using GameScript.Language.Ast;
using GameScript.Language.Index;

namespace GameScript.Language.Symbols
{
	/// <summary>
	/// The one shape whitelist for compile-time constants — constant initializers,
	/// parameter defaults, case values and table cells: a literal, a negated
	/// number literal, or a '^' constant.
	/// </summary>
	public static class ConstantExpressions
	{
		public static bool IsConstant(ExpressionNode node)
		{
			return node switch
			{
				LiteralNode => true,
				UnaryExpressionNode { Operator: UnaryOperator.Negate, Operand: LiteralNode } => true,
				IdentifierNode { Type: IdentifierType.Constant } => true,
				_ => false,
			};
		}

		/// <summary>The boxed int / bool / string value of a literal, or null when malformed.</summary>
		public static object? ParseLiteral(LiteralNode literal)
		{
			return literal.Type switch
			{
				LiteralType.Number => LiteralNode.TryParseNumber(literal.Value, out var i) ? i : null,
				LiteralType.Boolean => bool.TryParse(literal.Value, out var b) ? b : null,
				LiteralType.String => literal.Value.Length >= 2 ? literal.Value.Substring(1, literal.Value.Length - 2) : null,
				_ => null,
			};
		}

		/// <summary>
		/// The value of a literal or negated number literal, or null (including for
		/// '^' constants — use <see cref="Evaluate"/> when a symbol index is at hand).
		/// </summary>
		public static object? ParseInlineValue(ExpressionNode node)
		{
			return node switch
			{
				LiteralNode literal => ParseLiteral(literal),
				UnaryExpressionNode { Operator: UnaryOperator.Negate, Operand: LiteralNode operand } =>
					ParseLiteral(operand) is int number ? -number : null,
				_ => null,
			};
		}

		/// <summary>
		/// The compile-time value of a constant expression: literal, negated number
		/// literal, or a '^' constant's indexed literal value. Null when not extractable.
		/// </summary>
		public static object? Evaluate(ExpressionNode node, ISymbolIndex symbols)
		{
			if (node is IdentifierNode { Type: IdentifierType.Constant } identifier)
			{
				foreach (var symbol in symbols.GetSymbols(identifier.Name))
				{
					if (symbol.IdentifierType == IdentifierType.Constant)
						return symbol.LiteralValue;
				}
				return null;
			}
			return ParseInlineValue(node);
		}

		/// <summary>A table cell descriptor for a constant expression, or null when the shape isn't constant.</summary>
		public static TableCell? ToTableCell(ExpressionNode node)
		{
			if (node is IdentifierNode { Type: IdentifierType.Constant } identifier)
				return new TableCell(null, identifier.Name);
			var value = ParseInlineValue(node);
			return value == null ? null : new TableCell(value, null);
		}

		public static string Display(object? value) => value switch
		{
			null => "?",
			bool b => b ? "true" : "false",
			string s => $"\"{s}\"",
			_ => value.ToString() ?? "?",
		};
	}
}
