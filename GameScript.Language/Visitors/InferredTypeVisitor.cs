using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using System;
using System.Linq;

namespace GameScript.Language.Visitors
{
	internal sealed class InferredTypeVisitor(VisitorContext context) : AstVisitorBase
	{
		private readonly VisitorContext _context = context;

		public LocalIndex? LocalIndex { get; set; }
		public TypeInfo? InferredType { get; private set; }

		// For Literal nodes, assign a primitive type based on the literal kind.
		public override void Visit(LiteralNode node)
		{
			// Example: if the literal type is Number, set its type to "int".
			// Adjust depending on your DSL; here we check an assumed LiteralType property.
			InferredType = node.Type switch
			{
				LiteralType.Number => _context.Types.GetType(TypeKind.Int),
				LiteralType.String => _context.Types.GetType(TypeKind.String),
				LiteralType.Boolean => _context.Types.GetType(TypeKind.Bool),
				_ => throw new NotSupportedException($"Cannot infer type of unsupported literal type: {node.Type}"),
			};
		}

		// For Identifier nodes, look up the symbol for the identifier.
		public override void Visit(IdentifierNode node)
		{
			// A bare func name in expression position is a method reference, not a call.
			if (node.Type is IdentifierType.Func or IdentifierType.Label)
			{
				InferredType = _context.Types.GetType("func");
				return;
			}

			var symbol = LocalIndex?.GetSymbol(node.Name) ??
				_context.Symbols.GetSymbol(node.Name);
			InferredType = symbol?.Type;
		}

		public override void Visit(ReturnStatementNode node)
		{
			InferredType = null;
			node.Expression?.Accept(this);
		}

		// For binary expressions, assume that if both sides have the same inferred type, that is the type of the binary expression.
		public override void Visit(BinaryExpressionNode node)
		{
			if ((node.Operator & (BinaryOperator.Relational | BinaryOperator.Logical)) != BinaryOperator.Unknown)
			{
				InferredType = _context.Types.GetType(TypeKind.Bool);
				return;
			}

			if (node.Operator == BinaryOperator.Add)
			{
				node.Left.Accept(this);
				var leftType = InferredType;
				node.Right.Accept(this);
				var rightType = InferredType;
				if (leftType?.Kind == TypeKind.String || rightType?.Kind == TypeKind.String)
					InferredType = _context.Types.GetType(TypeKind.String);
				else
					InferredType = leftType;
				return;
			}

			node.Left.Accept(this);
		}

		// For unary expressions, the inferred type is usually that of the operand.
		public override void Visit(UnaryExpressionNode node)
		{
			node.Operand.Accept(this);
		}

		// For assignment expressions, we set the InferredType to that of the left-hand side.
		public override void Visit(AssignmentExpressionNode node)
		{
			node.Left.Accept(this);
		}

		// For call expressions, look up the function symbol for the function name,
		// then assign the return type from the function signature. The name is NOT
		// visited as an identifier — that would infer 'func' (a method reference).
		public override void Visit(CallExpressionNode node)
		{
			foreach (var symbol in _context.Symbols.GetSymbols(node.FunctionName.Name))
			{
				if (symbol.IsCallable())
				{
					InferredType = symbol.Type;
					return;
				}
			}
			InferredType = null;
		}

		// Grouping is transparent.
		public override void Visit(ParenthesizedExpressionNode node)
		{
			node.Inner.Accept(this);
		}

		// An inline declaration's type is its declared type.
		public override void Visit(DeclarationExpressionNode node)
		{
			InferredType = _context.Types.GetType(node.Type.Name);
		}

		// For tuple expressions, we need to generate a tuple type composed of the types of its elements.
		public override void Visit(TupleExpressionNode node)
		{
			var elementTypes = node.Elements.Select(e =>
			{
				e.Accept(this);
				return InferredType?.Name;
			}).ToList();

			if (!elementTypes.Any(x => x is null))
			{
				InferredType = _context.Types.GetTuple(elementTypes!);
			}
			else
			{
				InferredType = null;
			}
		}

		// For postfix and other expressions, if no specific handling is required,
		// the default visiting of children will suffice.
		public override void Visit(PostfixExpressionNode node)
		{
			node.Operand.Accept(this);
		}

		// A keyed lookup yields a row, never a value.
		public override void Visit(IndexExpressionNode node)
		{
			InferredType = null;
		}

		// t.count → int; t.has(...) → bool; t.at(i) → row (null); row.col → the column's type.
		public override void Visit(MemberExpressionNode node)
		{
			if (node.IsCount)
			{
				InferredType = _context.Types.GetType(TypeKind.Int);
				return;
			}
			if (node.IsHas)
			{
				InferredType = _context.Types.GetType(TypeKind.Bool);
				return;
			}
			if (node.IsAt)
			{
				InferredType = null;
				return;
			}

			InferredType = null;
			if (!TableAccess.IsRowTarget(node.Target, LocalIndex))
				return;
			var table = TableAccess.ResolveTable(node.Target, LocalIndex, _context.Symbols);
			if (table?.Columns == null)
				return;
			foreach (var column in table.Columns)
			{
				if (column.Name == node.Member.Name)
				{
					InferredType = column.Type;
					return;
				}
			}
		}
	}
}
