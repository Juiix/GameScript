using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Language.Visitors
{
	/// <summary>
	/// Infers the static type of an expression. Every symbol type is read through
	/// <see cref="IndexExtensions.Resolve"/> so named-type placeholders recorded at
	/// index time come back with their root attached; arithmetic yields the root
	/// (a named type is only preserved by identity: reads, casts, resolved calls).
	/// </summary>
	internal sealed class InferredTypeVisitor(
		VisitorContext context,
		IReadOnlyDictionary<CallExpressionNode, SymbolInfo> resolvedCalls) : AstVisitorBase
	{
		private readonly VisitorContext _context = context;
		private readonly IReadOnlyDictionary<CallExpressionNode, SymbolInfo> _resolvedCalls = resolvedCalls;

		public LocalIndex? LocalIndex { get; set; }
		public TypeInfo? InferredType { get; private set; }

		// For Literal nodes, assign a primitive type based on the literal kind.
		public override void Visit(LiteralNode node)
		{
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

			// A type name is not a value (semantic analysis reports the misuse).
			if (node.Type == IdentifierType.Type)
			{
				InferredType = null;
				return;
			}

			var symbol = LocalIndex?.GetSymbol(node.Name) ??
				_context.Symbols.GetSymbol(node.Name);
			InferredType = _context.Types.Resolve(symbol?.Type);
		}

		public override void Visit(ReturnStatementNode node)
		{
			InferredType = null;
			node.Expression?.Accept(this);
		}

		// Relational/logical operators yield bool; '+' with a string operand yields
		// string; every other arithmetic result is the root type of the left operand.
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
				if (leftType?.RootKind == TypeKind.String || rightType?.RootKind == TypeKind.String)
					InferredType = _context.Types.GetType(TypeKind.String);
				else
					InferredType = leftType?.Root;
				return;
			}

			node.Left.Accept(this);
			InferredType = InferredType?.Root;
		}

		// Unary '-', '++', '--' yield the operand's root; 'not' keeps bool.
		public override void Visit(UnaryExpressionNode node)
		{
			node.Operand.Accept(this);
			if (node.Operator != UnaryOperator.Not)
				InferredType = InferredType?.Root;
		}

		// For assignment expressions, we set the InferredType to that of the left-hand side.
		public override void Visit(AssignmentExpressionNode node)
		{
			node.Left.Accept(this);
		}

		// A cast yields its target type. A call yields the return type of the overload
		// type analysis resolved for it (calls are visited before their parents ask);
		// an unresolved call falls back to the only callable of that name, if there is
		// exactly one. The name is NOT visited as an identifier — that would infer 'func'.
		public override void Visit(CallExpressionNode node)
		{
			if (node.FunctionName.Type == IdentifierType.Type)
			{
				InferredType = _context.Types.GetType(node.FunctionName.Name);
				return;
			}

			if (_resolvedCalls.TryGetValue(node, out var resolved))
			{
				InferredType = _context.Types.Resolve(resolved.Type);
				return;
			}

			SymbolInfo? only = null;
			var count = 0;
			foreach (var symbol in _context.Symbols.GetSymbols(node.FunctionName.Name))
			{
				if (symbol.IsCallable())
				{
					only = symbol;
					count++;
				}
			}
			InferredType = count == 1 ? _context.Types.Resolve(only!.Type) : null;
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

		// Postfix '++' / '--' yield the operand's root.
		public override void Visit(PostfixExpressionNode node)
		{
			node.Operand.Accept(this);
			InferredType = InferredType?.Root;
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
					InferredType = _context.Types.Resolve(column.Type);
					return;
				}
			}
		}
	}
}
