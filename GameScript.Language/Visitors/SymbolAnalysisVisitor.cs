using GameScript.Language.Ast;
using GameScript.Language.Index;
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
			CheckSymbol(_context.Symbols, node.SymbolName, node.Name);
			base.Visit(node);
		}

		public override void Visit(VariableDefinitionNode node)
		{
			foreach (var (varName, _) in node.Vars)
			{
				CheckSymbol(LocalIndex, varName.Name, varName);
			}
			base.Visit(node);
		}

		public override void Visit(ParameterNode node)
		{
			CheckSymbol(LocalIndex, node.Name.Name, node.Name);
			base.Visit(node);
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
