using GameScript.Language.Ast;
using GameScript.Language.Index;
using System;
using System.Collections.Generic;

namespace GameScript.Language.Visitors
{
	public sealed class SemanticAnalysisVisitor(
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes,
		VisitorContext context) : AnalysisVisitorBase(localIndexes)
	{
		private readonly VisitorContext _context = context;
		private bool _inConstant = false;
		private int _loopDepth = 0;

		public override void Visit(MethodDefinitionNode node)
		{
			ReturnFlowCheck(node);

			// check if can have return types
			if (node.ReturnTypes != null &&
				(node.Name.Type == IdentifierType.Label || node.Name.Type == IdentifierType.Trigger))
			{
				Error($"{node.Name.Type} cannot have return values", node.ReturnTypes);
			}

			if (node.Parameters != null)
			{
				foreach (var param in node.Parameters)
				{
					if (param.Name.Type != IdentifierType.Local)
					{
						Error($"Parameter '{param.Name.Name}' must start with a '$'", param.FileRange);
					}
				}
			}


			if (node.Name.Type == IdentifierType.Command &&
				node.Body != null)
			{
				Error($"Commands cannot define a method body.", node.Body);
			}

			base.Visit(node);
		}

		public override void Visit(ConstantDefinitionNode node)
		{
			_inConstant = true;
			base.Visit(node);
			_inConstant = false;
		}

		public override void Visit(WhileStatementNode node)
		{
			_loopDepth++;
			base.Visit(node);
			_loopDepth--;
		}

		public override void Visit(BreakStatementNode node)
		{
			if (_loopDepth <= 0)
			{
				Error("Cannot use 'break' outside of a loop.", node);
			}
		}

		public override void Visit(ContinueStatementNode node)
		{
			if (_loopDepth <= 0)
			{
				Error("Cannot use 'continue' outside of a loop.", node);
			}
		}

		public override void Visit(AssignmentExpressionNode node)
		{
			// Check that the left-hand side is a valid lvalue (we assume it should be an IdentifierNode).
			if (node.Left is TupleExpressionNode leftTuple)
			{
				foreach (var child in leftTuple.Children)
				{
					if (!IsAssignableIdentifier(child))
					{
						Error("Tuple element must be an assignable variable.", child.FileRange);
					}
				}
			}
			else if (!IsAssignableIdentifier(node.Left))
			{
				Error("Left-hand side of assignment must be an assignable variable.", node.FileRange);
			}

			base.Visit(node);
		}

		public override void Visit(CallExpressionNode node)
		{
			base.Visit(node);

			if (node.FunctionName.Type == IdentifierType.Label &&
				Method?.Name.Type == IdentifierType.Func)
			{
				Error("Cannot goto a label inside of a Func, expecting a return value.", node);
			}
		}

		public override void Visit(IdentifierNode node)
		{
			if (_inConstant &&
				(node.Type & IdentifierType.Method) != IdentifierType.Unknown)
			{
				Error("Identifiers cannot be used in constant assignment.", node);
			}

			// check if called identifier type matches symbol type '~', '@', etc.
			var symbol = LocalIndex?.GetSymbol(node.Name) ??
				_context.Symbols.GetSymbol(node.Name);

			if (symbol == null)
			{
				Error($"{node.Type} '{node.Name}' is not declared.", node);
			}
			else if (symbol.IdentifierType != node.Type)
			{
				switch (symbol.IdentifierType)
				{
					case IdentifierType.Func:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be called with a '~' prefix.", node);
						break;
					case IdentifierType.Label:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be called with a '@' prefix.", node);
						break;
					case IdentifierType.Command:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be called without a prefix.", node);
						break;
					case IdentifierType.Trigger:
						Error($"{symbol.IdentifierType} '{symbol.Name}' cannot be called.", node);
						break;
					case IdentifierType.Local:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be referenced with a '$' prefix", node);
						break;
					case IdentifierType.Context:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be referenced with a '%' prefix", node);
						break;
					case IdentifierType.Constant:
						Error($"{symbol.IdentifierType} '{symbol.Name}' must be referenced with a '^' prefix", node);
						break;
				}
			}

			if (symbol?.IdentifierType == IdentifierType.Local &&
				node.FileRange.Start.Position < symbol.FileRange.Start.Position)
			{
				Error($"{symbol.PrefixedName} cannot be referenced before it's declared", node);
			}
		}

		private void ReturnFlowCheck(MethodDefinitionNode node)
		{
			if (node.Name.Type == IdentifierType.Command ||
				node.ReturnTypes == null ||
				node.ReturnTypes.Count <= 0)
			{
				return;
			}

			// Evaluate the body to see if it *must* return on all paths
			var mustReturn = MustReturn(node.Body);
			if (!mustReturn)
			{
				Error($"{node.Name.Type} '{node.Name.Name}' declares a return type but not all paths return.", node.Name);
			}
		}

		// Evaluate a block to see if it must return.
		private bool MustReturn(BlockNode? block)
		{
			bool guaranteed = false;
			if (block?.Statements != null)
			{
				foreach (var statement in block.Statements)
				{
					// Evaluate each statement
					if (MustReturn(statement))
					{
						guaranteed = true;
						// once we find a guaranteed return, subsequent statements are unreachable
						// so we can break if we want to optimize
						break;
					}
				}
			}
			return guaranteed;
		}

		// Evaluate a single statement to see if it must return
		private bool MustReturn(AstNode statement)
		{
			switch (statement)
			{
				case ReturnStatementNode returnNode:
					return true;  // A direct return means we definitely returned

				case IfStatementNode ifNode:
					// MustReturn if both if-block and else-block definitely return 
					// and there's an else. If there's no else, some path won't return.
					bool ifPath = MustReturn(ifNode.IfBlock);
					if (ifNode.ElseIfNodes != null)
					{
						foreach (var elseIf in ifNode.ElseIfNodes)
						{
							ifPath &= MustReturn(elseIf.Block);
						}
					}
					if (ifNode.ElseBlock != null)
					{
						ifPath &= MustReturn(ifNode.ElseBlock);
					}
					return ifPath;

				case WhileStatementNode whileNode:
					// Usually "while" is not guaranteed to return unless the DSL ensures infinite loop or break
					// For simplicity, we'll say it might not return => false
					return false;

				default:
					// By default, if the statement is a block or something else, just keep checking children
					bool subPath = false;
					foreach (var child in statement.Children)
					{
						if (MustReturn(child))
						{
							subPath = true;
							break;
						}
					}
					return subPath;
			}
		}

		private static bool IsAssignableIdentifier(AstNode node)
		{
			return node is IdentifierNode identifierNode &&
				(identifierNode.Type & IdentifierType.Assignable) != IdentifierType.Unknown;
		}
	}
}
