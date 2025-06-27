using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace GameScript.Language.Visitors
{
	public sealed class TypeAnalysisVisitor(
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes,
		VisitorContext context) : AnalysisVisitorBase(localIndexes)
	{
		private readonly VisitorContext _context = context;
		private readonly InferredTypeVisitor _inferredTypeVisitor = new(context);

		public override void Visit(VariableDefinitionNode node)
		{
			base.Visit(node);

			var varType = _context.Types.GetType(node.VarType.Name);
			foreach (var (varName, initializer) in node.Vars)
			{
				varName.Accept(this);
				if (initializer != null)
				{
					initializer.Accept(this);

					var initializerType = GetInferredType(initializer);
					if (initializerType != null &&
						varType != null &&
						!initializerType.Equals(varType))
					{
						Error($"Type mismatch, cannot assign '{initializerType}' to '{varType}'", FileRange.Combine(varName.FileRange, initializer.FileRange));
					}
				}
			}
		}

		public override void Visit(ReturnStatementNode node)
		{
			base.Visit(node);

			if (Method == null)
			{
				return;
			}

			var returnType = _context.Types.GetTuple(Method.ReturnTypes?.Select(x => x.Type.Name));
			if (Method.ReturnTypes == null)
			{
				if (node.Expression != null)
				{
					Error($"{Method.Name.Type} has no return type declared.", node);
				}
			}
			else if (returnType != null)
			{
				if (node.Expression == null)
				{
					Error($"{Method.Name.Type} must return '{returnType}'", node);
				}
				else
				{
					var expressionType = GetInferredType(node.Expression);
					if (expressionType != null &&
						!expressionType.Equals(returnType))
					{
						Error($"Cannot return '{expressionType}', expected '{returnType}'", node);
					}
				}
			}
		}

		public override void Visit(IfStatementNode node)
		{
			base.Visit(node);

			ConditionExpressionCheck(node.Condition);

			if (node.ElseIfNodes != null)
			{
				foreach (var elseIf in node.ElseIfNodes)
				{
					ConditionExpressionCheck(elseIf.Condition);
				}
			}
		}

		public override void Visit(WhileStatementNode node)
		{
			base.Visit(node);

			ConditionExpressionCheck(node.Condition);
		}

		public override void Visit(BinaryExpressionNode node)
		{
			base.Visit(node);

			var leftType = GetInferredType(node.Left);
			var rightType = GetInferredType(node.Right);
			if (leftType == null || rightType == null)
			{
				return; // type check error
			}

			if (leftType != rightType)
			{
				Error($"Type mismatch, cannot operate '{leftType}' and '{rightType}'", node);
			}

			if ((node.Operator & BinaryOperator.Relational) == BinaryOperator.Unknown &&
				leftType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}

		// For unary expressions, the inferred type is usually that of the operand.
		public override void Visit(UnaryExpressionNode node)
		{
			base.Visit(node);

			var operandType = GetInferredType(node.Operand);
			if (node.Operator == UnaryOperator.Not &&
				operandType?.Kind != TypeKind.Bool)
			{
				Error("'!' operator can only be used on 'bool' types.", node);
			}

			if ((node.Operator & UnaryOperator.Numeric) != UnaryOperator.Unknown &&
				operandType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}

		// For assignment expressions, we set the InferredType to that of the left-hand side.
		public override void Visit(AssignmentExpressionNode node)
		{
			base.Visit(node);

			var leftType = GetInferredType(node.Left);
			var rightType = GetInferredType(node.Right);
			if (leftType == null || rightType == null)
			{
				return; // type check error
			}

			if (leftType != rightType)
			{
				Error($"Type mismatch, cannot operate '{leftType}' and '{rightType}'", node);
			}

			if (node.Operator != AssignmentOperator.Assign &&
				leftType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}
		// For call expressions, look up the function symbol for the function name,
		// then assign the return type from the function signature.
		public override void Visit(CallExpressionNode node)
		{
			base.Visit(node);

			var symbol = _context.Symbols.GetSymbol(node.FunctionName.Name);
			if (symbol != null)
			{
				// check params
				if (symbol.ParamTypes != null)
				{
					if (node.Arguments != null)
					{
						var argumentType = GetInferredType(node.Arguments);
						if (argumentType != symbol.ParamTypes)
						{
							Error($"Type mismatch, cannot call '{symbol.Name}{symbol.ParamTypes}' with '{argumentType}'", node.Arguments);
						}
					}
					else
					{
						Error($"Missing arguments {symbol.ParamTypes}.", node);
					}
				}
				else if (node.Arguments != null)
				{
					Error($"{symbol.IdentifierType} {symbol.Name} does not require arguments.", node.Arguments);
				}
			}
		}

		// For postfix and other expressions, if no specific handling is required,
		// the default visiting of children will suffice.
		public override void Visit(PostfixExpressionNode node)
		{
			base.Visit(node);

			var operandType = GetInferredType(node.Operand);
			if ((node.Operator & UnaryOperator.Numeric) != UnaryOperator.Unknown &&
				operandType?.Kind != TypeKind.Int)
			{
				Error($"'{node.OperatorNode.Operator}' can only be used with 'int' type.", node);
			}
		}

		public override void Visit(TypeNode node)
		{
			var type = _context.Types.GetType(node.Name);
			if (type == null &&
				!node.Name.StartsWith("?"))
			{
				Error($"Undefined type '{node}'", node);
			}
		}

		private void ConditionExpressionCheck(ExpressionNode expression)
		{
			var conditionType = GetInferredType(expression);
			if (conditionType?.Kind != TypeKind.Bool)
			{
				Error("Condition expression must resolve to a bool", expression);
			}
		}

		private TypeInfo? GetInferredType(ExpressionNode expression)
		{
			_inferredTypeVisitor.LocalIndex = LocalIndex;
			expression.Accept(_inferredTypeVisitor);
			return _inferredTypeVisitor.InferredType;
		}

		private TypeInfo? GetInferredType(List<ExpressionNode>? expressions)
		{
			if (expressions == null)
			{
				return null;
			}

			var elementTypes = expressions.Select(x =>
			{
				return GetInferredType(x)?.Name;
			}).ToList();

			if (!elementTypes.Any(x => x is null))
			{
				return _context.Types.GetTuple(elementTypes!);
			}
			else
			{
				return null;
			}
		}
	}
}
