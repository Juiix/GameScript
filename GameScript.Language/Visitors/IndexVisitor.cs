﻿using System.Collections.Generic;
using System.Linq;
using GameScript.Bytecode;
using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;

namespace GameScript.Language.Visitors
{
	public sealed class IndexVisitor(
		FileIndex fileIndex,
		VisitorContext context) : AstVisitorBase
	{
		private readonly FileIndex _fileIndex = fileIndex;
		private readonly VisitorContext _context = context;
		private readonly Dictionary<MethodDefinitionNode, LocalIndex> _localIndexes = [];
		private LocalIndex? _localIndex;

		public Dictionary<MethodDefinitionNode, LocalIndex> LocalIndexes => _localIndexes;

		public override void Visit(ConstantDefinitionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var symbol = new SymbolInfo(
				IdentifierType.Constant,
				node.Name.Name,
				_context.Types.GetType(node.Type.Name),
				null,
				null,
				null,
				node.Name.Summary,
				ParseLiteral(node.Initializer),
				_context.FilePath,
				node.Name.FileRange
			);

			_fileIndex.AddSymbol(symbol);
		}

		public override void Visit(ContextDefinitionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var symbol = new SymbolInfo(
				IdentifierType.Context,
				node.Name.Name,
				_context.Types.GetType(node.Type.Name),
				null,
				null,
				null,
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange
			);

			_fileIndex.AddSymbol(symbol);
		}

		public override void Visit(MethodDefinitionNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			// For triggers, we compose the symbol name to include trigger type for uniqueness.
			var symbol = new SymbolInfo(
				node.Name.Type,
				node.SymbolName,
				_context.Types.GetTuple(node.ReturnTypes?.Select(x => x.Type.Name)),
				node.ReturnTypes?.Select(x => x.Name?.Name ?? string.Empty).ToList(),
				_context.Types.GetTuple(node.Parameters?.Select(x => x.Type.Name)),
				node.Parameters?.Select(x => x.Name.Name).ToList(),
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange
			);

			_fileIndex.AddSymbol(symbol);

			_localIndex = new LocalIndex(node.FilePath, node.FileRange);

			base.Visit(node);

			_localIndexes.Add(node, _localIndex);
			_localIndex = null;
		}

		public override void Visit(VariableDefinitionNode node)
		{
			// Build symbols for the declared variables.
			var varType = _context.Types.GetType(node.VarType.Name);
			foreach (var (varName, initializer) in node.Vars)
			{
				if (InvalidSymbolName(varName.Name))
					return;

				var varSymbol = new SymbolInfo(
					IdentifierType.Local,
					varName.Name,
					varType,
					null,
					null,
					null,
					varName.Summary,
					null,
					_context.FilePath,
					varName.FileRange
				);

				_localIndex?.AddSymbol(varSymbol);
				initializer?.Accept(this);
			}
		}

		public override void Visit(ParameterNode node)
		{
			if (InvalidSymbolName(node.Name.Name))
				return;

			var paramSymbol = new SymbolInfo(
				node.Name.Type,
				node.Name.Name,
				_context.Types.GetType(node.Type.Name),
				null,
				null,
				null,
				node.Name.Summary,
				null,
				_context.FilePath,
				node.Name.FileRange
			);

			_localIndex?.AddSymbol(paramSymbol);
		}

		public override void Visit(IdentifierNode node)
		{
			// Check if the identifier has been declared in the current scope chain.
			var reference = new ReferenceInfo(
				node.Name,
				node.FilePath,
				node.FileRange
			);

			var symbol = _localIndex?.GetSymbol(node.Name);
			if (symbol != null)
			{
				_localIndex?.AddReference(reference);
			}
			else
			{
				_fileIndex.AddReference(reference);
			}
		}


		private static object? ParseLiteral(ExpressionNode node)
		{
			if (node is not LiteralNode literal) return Value.Null;
			return literal.Type switch
			{
				LiteralType.Number => int.TryParse(literal.Value, out var i) ? i : null,
				LiteralType.Boolean => bool.TryParse(literal.Value, out var b) ? b : null,
				LiteralType.String => literal.Value.Substring(1, literal.Value.Length - 2),
				_ => null
			};
		}
		private static bool InvalidSymbolName(string name) => name.StartsWith('?');
	}
}
