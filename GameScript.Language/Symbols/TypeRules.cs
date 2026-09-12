using GameScript.Language.Ast;

namespace GameScript.Language.Symbols
{
	/// <summary>
	/// The assignability rules of the type system, in one place. Named types are
	/// aliases over an int/string root: a named type widens to its root implicitly;
	/// the root (or another named type) must be cast to it with 'NAME(expr)'; the
	/// root's zero literal ('0' / '""') is assignable to every named type. Tuples
	/// follow the rules element-wise. Types passed here must already be resolved
	/// (see IndexExtensions.Resolve) — an unresolved placeholder only ever equals
	/// itself by name.
	/// </summary>
	public static class TypeRules
	{
		/// <summary>Whether a value of type <paramref name="from"/> may be stored where <paramref name="to"/> is expected.</summary>
		public static bool IsAssignable(TypeInfo from, TypeInfo to)
		{
			if (from.Equals(to))
				return true;

			if (from.Kind == TypeKind.Tuple || to.Kind == TypeKind.Tuple)
			{
				if (from.Kind != to.Kind || from.TypeParameters == null || to.TypeParameters == null ||
					from.TypeParameters.Count != to.TypeParameters.Count)
					return false;
				for (int i = 0; i < from.TypeParameters.Count; i++)
				{
					if (!IsAssignable(from.TypeParameters[i], to.TypeParameters[i]))
						return false;
				}
				return true;
			}

			// widening: a named type is still its root
			return from.IsNamed && !to.IsNamed && from.Underlying != null && from.Underlying.Equals(to);
		}

		/// <summary>
		/// <see cref="IsAssignable(TypeInfo, TypeInfo)"/> plus the zero-literal rule:
		/// the expression '0' (or '""') is assignable to every named type over that root.
		/// Tuple expressions apply the rule per element.
		/// </summary>
		public static bool IsAssignable(ExpressionNode? expression, TypeInfo from, TypeInfo to)
		{
			if (IsAssignable(from, to))
				return true;

			expression = Unwrap(expression);

			if (to.IsNamed)
				return to.Underlying != null && from.Equals(to.Underlying) && IsZeroLiteral(expression);

			if (to.Kind == TypeKind.Tuple && from.Kind == TypeKind.Tuple &&
				expression is TupleExpressionNode tuple &&
				from.TypeParameters != null && to.TypeParameters != null &&
				tuple.Elements.Count == from.TypeParameters.Count &&
				tuple.Elements.Count == to.TypeParameters.Count)
			{
				for (int i = 0; i < tuple.Elements.Count; i++)
				{
					if (!IsAssignable(tuple.Elements[i], from.TypeParameters[i], to.TypeParameters[i]))
						return false;
				}
				return true;
			}

			return false;
		}

		/// <summary>The literal '0' (any spelling, negated or not) or the literal '""' — the "none" value of every id kind.</summary>
		public static bool IsZeroLiteral(ExpressionNode? expression)
		{
			return Unwrap(expression) switch
			{
				LiteralNode { Type: LiteralType.Number } number =>
					LiteralNode.TryParseNumber(number.Value, out var value) && value == 0,
				LiteralNode { Type: LiteralType.String } text => text.Value == "\"\"",
				UnaryExpressionNode { Operator: UnaryOperator.Negate, Operand: LiteralNode { Type: LiteralType.Number } negated } =>
					LiteralNode.TryParseNumber(negated.Value, out var value) && value == 0,
				_ => false,
			};
		}

		/// <summary>'==' / '!=': the same type, or a named type against its root (either side).</summary>
		public static bool AreComparable(TypeInfo left, TypeInfo right) =>
			IsAssignable(left, right) || IsAssignable(right, left);

		/// <summary>'NAME(expr)': legal when the argument's root is the named type's root.</summary>
		public static bool CanCast(TypeInfo from, TypeInfo to) =>
			to.IsNamed && to.Underlying != null && from.Root.Equals(to.Underlying);

		private static ExpressionNode? Unwrap(ExpressionNode? expression)
		{
			while (expression is ParenthesizedExpressionNode parenthesized)
				expression = parenthesized.Inner;
			return expression;
		}
	}
}
