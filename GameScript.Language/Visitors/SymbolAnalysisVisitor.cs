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
			{
				CheckSymbol(_context.Symbols, node.SymbolName, node.Name);
				CheckTriggerHandler(node);
			}
			else if (node.Name.Type == IdentifierType.TriggerDeclaration)
			{
				CheckSymbol(_context.Symbols, node.SymbolName, node.Name);
				CheckTriggerDeclarationCollision(node);
			}
			else
			{
				CheckOverloadableSymbol(node);
			}
			base.Visit(node);
		}

		// Validates a trigger handler's header against the declared trigger kind:
		// the kind must be declared, and the handler's parameters must be a prefix
		// of the declaration's. Subjects are deliberately not validated.
		private void CheckTriggerHandler(MethodDefinitionNode node)
		{
			var kind = node.Keyword.Keyword;
			if (InvalidSymbolName(kind))
				return;

			SymbolInfo? decl = null;
			foreach (var symbol in _context.Symbols.GetSymbols(kind))
			{
				if (symbol.IdentifierType == IdentifierType.TriggerDeclaration)
				{
					decl = symbol;
					break;
				}
			}

			if (decl == null)
			{
				Error($"Unknown trigger kind '{kind}'. Trigger kinds must be declared (e.g. in core.gs): 'trigger {kind}'.", node.Keyword);
				return;
			}

			var paramCount = node.Parameters?.Count ?? 0;
			if (paramCount > decl.Arity)
			{
				Error($"Handler declares {paramCount} parameter(s) but trigger '{kind}' only provides {decl.Arity}: {decl.ParamSignature}. Handler parameters must be a prefix of the trigger's.", node.Name);
				return;
			}

			if (node.Parameters == null || decl.ParamTypes == null)
				return;

			var i = 0;
			foreach (var declType in decl.ParamTypes.AllTypes)
			{
				if (i >= node.Parameters.Count)
					break;
				var param = node.Parameters[i++];
				var paramType = _context.Types.GetType(param.Type.Name);
				if (paramType != null && !paramType.Equals(declType))
				{
					Error($"Parameter '{param.Name.Name}' must be '{declType.Name}' to match trigger '{kind}' {decl.ParamSignature}. Handler parameters must be a prefix of the trigger's.", param);
				}
			}
		}

		// Trigger declarations share the flat global namespace with funcs/commands
		// but are never callable, so a shared name is always a conflict.
		private void CheckTriggerDeclarationCollision(MethodDefinitionNode node)
		{
			foreach (var symbol in _context.Symbols.GetSymbols(node.SymbolName))
			{
				if (symbol.IsCallable())
				{
					Error($"Trigger '{node.SymbolName}' conflicts with {symbol.IdentifierType} '{node.SymbolName}'. Triggers are never callable; rename one.", node.Name);
					return;
				}
			}
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
					if (other.IdentifierType == IdentifierType.TriggerDeclaration)
						Error($"{node.Name.Type} '{symbolName}' conflicts with trigger '{symbolName}'. Triggers are never callable; rename one.", node.Name);
					else
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

		public override void Visit(ForStatementNode node)
		{
			var name = node.Variable.Name;
			if (!InvalidSymbolName(name))
			{
				var existing = LocalIndex?.GetSymbol(name);
				if (existing == null)
				{
					Error($"Something went wrong with '{name}'", node.Variable);
				}
				else if ((!existing.FilePath.Equals(node.Variable.FilePath) ||
						  existing.FileRange != node.Variable.FileRange) &&
						 !(LocalIndex?.IsLoopVariable(name) ?? false))
				{
					// Carve-out: a later 'for' may reuse a name only when the earlier
					// declaration was itself a for-loop variable.
					Error($"'{name}' is already defined in this context.", node.Variable);
				}
				CheckLocalCollision(name, node.Variable);
			}
			base.Visit(node);
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
