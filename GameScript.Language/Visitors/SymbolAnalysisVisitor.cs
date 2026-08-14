using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using System.Collections.Generic;

namespace GameScript.Language.Visitors
{
	public sealed class SymbolAnalysisVisitor(
		IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes,
		VisitorContext context) : AnalysisVisitorBase(localIndexes)
	{
		private readonly VisitorContext _context = context;

		public override void Visit(ConstantDefinitionNode node)
		{
			CheckSymbol(_context.Symbols, node.Name.Name, node.Name);
			base.Visit(node);
		}

		public override void Visit(ContextDefinitionNode node)
		{
			CheckSymbol(_context.Symbols, node.Name.Name, node.Name);
			base.Visit(node);
		}

		public override void Visit(MethodDefinitionNode node)
		{
			// Triggers stay name-unique (their SymbolName embeds the trigger keyword);
			// funcs/labels/commands may overload as long as parameter signatures differ.
			if (node.Name.Type == IdentifierType.Trigger)
				CheckSymbol(_context.Symbols, node.SymbolName, node.Name);
			else
				CheckOverloadableSymbol(node);
			base.Visit(node);
		}

		private void CheckOverloadableSymbol(MethodDefinitionNode node)
		{
			var symbolName = node.SymbolName;
			if (InvalidSymbolName(symbolName))
				return;

			SymbolInfo? self = null;
			var others = new List<SymbolInfo>();
			foreach (var symbol in _context.Symbols.GetSymbols(symbolName))
			{
				if (self == null &&
					symbol.FilePath.Equals(node.Name.FilePath) &&
					symbol.FileRange == node.Name.FileRange)
				{
					self = symbol;
				}
				else
				{
					others.Add(symbol);
				}
			}

			if (self == null)
			{
				// something went wrong?
				Error($"Something went wrong with '{symbolName}'", node.Name);
				return;
			}

			foreach (var other in others)
			{
				if (!other.IsCallable())
				{
					Error($"'{symbolName}' is already defined in this context.", node.Name);
					return;
				}
				if (other.ParamSignature == self.ParamSignature)
				{
					Error($"'{symbolName}{self.ParamSignature}' is already defined in this context. Overloads must differ by parameter types (return types don't count).", node.Name);
					return;
				}
			}
		}

		public override void Visit(VariableDefinitionNode node)
		{
			foreach (var (varName, _) in node.Vars)
			{
				CheckSymbol(LocalIndex, varName.Name, varName);
				CheckLocalCollision(varName.Name, varName);
			}
			base.Visit(node);
		}

		public override void Visit(ParameterNode node)
		{
			CheckSymbol(LocalIndex, node.Name.Name, node.Name);
			CheckLocalCollision(node.Name.Name, node.Name);
			base.Visit(node);
		}

		public override void Visit(DeclarationExpressionNode node)
		{
			CheckSymbol(LocalIndex, node.Name.Name, node.Name);
			CheckLocalCollision(node.Name.Name, node.Name);
			base.Visit(node);
		}

		// Bare locals share the namespace with funcs/commands, so shadowing a global
		// callable is banned outright (calls and func refs would be ambiguous).
		private void CheckLocalCollision(string name, AstNode node)
		{
			foreach (var symbol in _context.Symbols.GetSymbols(name))
			{
				if (symbol.IsCallable())
				{
					Error($"Local '{name}' conflicts with {symbol.IdentifierType} '{name}'; rename the local.", node);
					return;
				}
			}
		}

		private void CheckSymbol(ISymbolIndex? index, string symbolName, AstNode node)
		{
			if (InvalidSymbolName(symbolName))
				return;

			var symbol = index?.GetSymbol(symbolName);
			if (symbol == null)
			{
				// something went wrong?
				Error($"Something went wrong with '{symbolName}'", node);
			}
			else if (!symbol.FilePath.Equals(node.FilePath) ||
				symbol.FileRange != node.FileRange)
			{
				Error($"'{symbolName}' is already defined in this context.", node);
			}
		}
		private static bool InvalidSymbolName(string name) => name.StartsWith('?');
	}
}
