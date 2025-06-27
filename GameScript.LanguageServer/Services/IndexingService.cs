using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Visitors;
using GameScript.LanguageServer.Parsing;
using Microsoft.Extensions.Logging;

namespace GameScript.LanguageServer.Services
{
	/// <summary>
	/// Produces a <see cref="FileIndex"/> for a single AST, updates the global
	/// symbol/reference tables, and returns per-method local indexes plus any
	/// diagnostics encountered while traversing the tree.
	/// </summary>
	internal sealed class IndexingService
	{
		private readonly GlobalSymbolTable _symbols;
		private readonly GlobalReferenceTable _references;
		private readonly GlobalTypeIndex _types;
		private readonly ILogger<ParsingService> _logger;

		/// <summary>
		/// Creates a new <see cref="IndexingService"/>.
		/// </summary>
		/// <param name="symbols">Global table of declared symbols.</param>
		/// <param name="references">Global table of symbol references.</param>
		/// <param name="types">Global index of known types.</param>
		/// <param name="logger">Logger for unexpected errors.</param>
		public IndexingService(
			GlobalSymbolTable symbols,
			GlobalReferenceTable references,
			GlobalTypeIndex types,
			ILogger<ParsingService> logger)
		{
			_symbols = symbols;
			_references = references;
			_types = types;
			_logger = logger;
		}

		/// <summary>
		/// Walks the AST to build indexing data and propagates it to the global
		/// tables. Returns <c>null</c> if a fatal exception occurs (which is
		/// already logged).
		/// </summary>
		/// <param name="rootNode">Root of the file’s abstract syntax tree.</param>
		/// <returns>
		/// An <see cref="IndexResult"/> containing the file index, local
		/// method indexes, and any diagnostics; or <c>null</c> on failure.
		/// </returns>
		public IndexResult? Index(AstNode rootNode)
		{
			var filePath = rootNode.FilePath;
			try
			{
				var fileIndex = new FileIndex();
				var errors = new List<FileError>();
				var context = new VisitorContext(_types, _symbols, filePath);
				var visitor = new IndexVisitor(fileIndex, context);

				rootNode.Accept(visitor);
				errors.AddRange(visitor.Errors);

				// Merge results into global caches.
				_references.AddFile(filePath, fileIndex.FileReferences);
				_symbols.AddFile(filePath, fileIndex.FileSymbols);

				return new IndexResult(fileIndex, visitor.LocalIndexes, errors);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "An error occurred during indexing");
				return null;
			}
		}

		/// <summary>
		/// Enumerates every file that is <b>directly</b> related to
		/// <paramref name="rootData"/> through either
		/// <list type="bullet">
		///   <item><description>a <i>reference</i> to one of its declared symbols, or</description></item>
		///   <item><description>a <i>duplicate declaration</i> of one of those symbols.</description></item>
		/// </list>
		/// </summary>
		/// <remarks>
		/// The method performs a breadth-first walk over the global symbol and reference
		/// tables and returns each discovered file path exactly once.
		/// </remarks>
		/// <param name="rootData">
		/// The <see cref="RootFileData"/> of the file whose dependency set you want.
		/// </param>
		/// <param name="visited">
		/// A caller-supplied set used to de-duplicate results across recursive calls.
		/// Pass an empty <see cref="HashSet{T}"/> when you start the traversal.
		/// </param>
		/// <returns>
		/// A sequence of absolute file paths.  
		/// The original file is <i>not</i> included; only its neighbours are returned.
		/// </returns>
		public IEnumerable<string> GetDependencies(RootFileData rootData, HashSet<string> visited)
		{
			foreach (var symbol in rootData.Index.FileIndex.Symbols)
			{
				foreach (var reference in _references.GetReferences(symbol.Name))
				{
					if (visited.Add(reference.FilePath))
						yield return reference.FilePath;
				}

				foreach (var duplicate in _symbols.GetSymbols(symbol.Name))
				{
					if (duplicate != symbol &&
						visited.Add(duplicate.FilePath))
						yield return duplicate.FilePath;
				}
			}
		}

		/// <summary>
		/// Removes every symbol and reference that originated from
		/// <paramref name="filePath"/> so they won’t appear in future look-ups.
		/// </summary>
		/// <param name="filePath">
		/// Absolute path of the file being evicted from the caches.
		/// </param>
		public void RemoveFile(string filePath)
		{
			_references.RemoveFile(filePath);
			_symbols.RemoveFile(filePath);
		}
	}
}
