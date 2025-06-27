using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Visitors;
using GameScript.LanguageServer.Parsing;
using Microsoft.Extensions.Logging;

namespace GameScript.LanguageServer.Services
{
	/// <summary>
	/// Runs all static-analysis passes (symbol, semantic, and type) for a single file
	/// and aggregates any diagnostics that are produced.
	/// </summary>
	internal sealed class AnalysisService
	{
		private readonly GlobalSymbolTable _symbols;
		private readonly GlobalTypeIndex _types;
		private readonly ILogger<ParsingService> _logger;

		/// <summary>
		/// Initializes a new instance of <see cref="AnalysisService"/>.
		/// </summary>
		/// <param name="symbols">The shared global symbol table.</param>
		/// <param name="types">The shared global type index.</param>
		/// <param name="logger">Logger used to record unexpected failures.</param>
		public AnalysisService(
			GlobalSymbolTable symbols,
			GlobalTypeIndex types,
			ILogger<ParsingService> logger)
		{
			_symbols = symbols;
			_types = types;
			_logger = logger;
		}

		/// <summary>
		/// Executes all analysis passes on the provided syntax tree.
		/// </summary>
		/// <param name="rootNode">The root of the file’s abstract syntax tree.</param>
		/// <param name="localIndexes">
		/// A lookup of pre-computed local symbol indexes for each method definition in the file.
		/// </param>
		/// <returns>
		/// An <see cref="AnalysisResult"/> containing any diagnostics, or <c>null</c> if a
		/// fatal error occurred (which will have been logged).
		/// </returns>
		public AnalysisResult? Analyze(
			AstNode rootNode,
			IReadOnlyDictionary<MethodDefinitionNode, LocalIndex> localIndexes)
		{
			try
			{
				var context = new VisitorContext(_types, _symbols, rootNode.FilePath);
				var errors = new List<FileError>();

				// Run each analysis pass.
				VisitAst(rootNode, new SymbolAnalysisVisitor(localIndexes, context), errors);
				VisitAst(rootNode, new SemanticAnalysisVisitor(localIndexes, context), errors);
				VisitAst(rootNode, new TypeAnalysisVisitor(localIndexes, context), errors);

				return new(errors);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "An error occurred during analysis of file: {filePath}", rootNode.FilePath);
				return null;
			}
		}

		/// <summary>
		/// Executes a single visitor pass and appends any diagnostics it produces.
		/// </summary>
		/// <param name="node">The AST node to accept the visitor (normally the file root).</param>
		/// <param name="visitor">The visitor performing the analysis.</param>
		/// <param name="errors">The collection that accumulates diagnostics from all passes.</param>
		private void VisitAst(AstNode node, IAstVisitor visitor, List<FileError> errors)
		{
			try
			{
				node.Accept(visitor);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Error while visiting AST for analysis");
			}
			finally
			{
				errors.AddRange(visitor.Errors);
			}
		}
	}
}
