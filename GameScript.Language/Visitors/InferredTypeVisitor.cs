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
			if ((node.Operator & BinaryOperator.Relational) != BinaryOperator.Unknown)
			{
				InferredType = _context.Types.GetType(TypeKind.Bool);
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
		// then assign the return type from the function signature.
		public override void Visit(CallExpressionNode node)
		{
			// First, visit the function name and all arguments.
			node.FunctionName.Accept(this);
		}

		// For tuple expressions, we need to generate a tuple type composed of the types of its elements.
		public override void Visit(TupleExpressionNode node)
		{
			// Visit each element in the tuple to set their inferred types.
			foreach (var element in node.Elements)
			{
				element.Accept(this);
			}

			// Generate a canonical tuple type from the inferred types of the elements.
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
	}
}
