using System;
using System.Collections.Generic;
using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.Bytecode;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Visitors;

namespace GameScript.Language.Tests.Harness;

/// <summary>
/// Drives the full front-end pipeline (parse -> index -> analyze -> compile) over
/// in-memory sources, mirroring how ContentBuilder's ScriptBuilder wires the
/// visitors. File "paths" are virtual; the extension selects the parse entry point.
/// </summary>
public sealed class TestCompilation
{
	private readonly GlobalSymbolTable _symbols = new();
	private readonly GlobalReferenceTable _references = new();
	private readonly GlobalTypeIndex _types = new();
	private readonly List<(string FilePath, AstNode Root, Dictionary<MethodDefinitionNode, LocalIndex> Locals)> _files = [];

	public List<FileError> ParseErrors { get; } = [];
	public List<FileError> AnalysisErrors { get; } = [];
	public IEnumerable<FileError> AllErrors => ParseErrors.Concat(AnalysisErrors);
	public Dictionary<CallExpressionNode, Symbols.SymbolInfo> ResolvedCalls { get; } = [];

	public TestCompilation AddFile(string filePath, string source)
	{
		var parser = new AstParser(filePath, source);
		AstNode root = System.IO.Path.GetExtension(filePath) switch
		{
			".const" => parser.ParseConstants(),
			".context" => parser.ParseContexts(),
			_ => parser.ParseProgram(),
		};
		if (parser.Errors is { Count: > 0 })
			ParseErrors.AddRange(parser.Errors);

		var fileIndex = new FileIndex();
		var context = new VisitorContext(_types, _symbols, filePath);
		var indexVisitor = new IndexVisitor(fileIndex, context);
		root.Accept(indexVisitor);
		ParseErrors.AddRange(indexVisitor.Errors);

		_references.AddFile(filePath, fileIndex.FileReferences);
		_symbols.AddFile(filePath, fileIndex.FileSymbols);

		_files.Add((filePath, root, indexVisitor.LocalIndexes));
		return this;
	}

	/// <summary>Runs the analysis visitors over every added file, collecting diagnostics.</summary>
	public TestCompilation Analyze()
	{
		AnalysisErrors.Clear();
		ResolvedCalls.Clear();
		foreach (var (filePath, root, locals) in _files)
		{
			var context = new VisitorContext(_types, _symbols, filePath);
			Run(new NameResolutionVisitor(locals, context));
			Run(new SymbolAnalysisVisitor(locals, context));
			Run(new SemanticAnalysisVisitor(locals, context));
			var typeVisitor = new TypeAnalysisVisitor(locals, context);
			Run(typeVisitor);
			foreach (var (call, symbol) in typeVisitor.ResolvedCalls)
				ResolvedCalls[call] = symbol;

			void Run<TVisitor>(TVisitor visitor) where TVisitor : IAstVisitor
			{
				root.Accept(visitor);
				AnalysisErrors.AddRange(visitor.Errors);
			}
		}
		return this;
	}

	/// <summary>Compiles all added files. Throws if any parse/analysis error exists.</summary>
	public BytecodeCompilerResult Compile()
	{
		var errors = AllErrors.ToArray();
		if (errors.Length > 0)
			throw new InvalidOperationException(
				"Cannot compile with errors:\n" + string.Join("\n", errors.Select(FormatError)));

		var roots = _files.Select(x => x.Root).ToArray();
		var constants = roots.OfType<ConstantsNode>().SelectMany(x => x.Definitions ?? []);
		var contexts = roots.OfType<ContextsNode>().SelectMany(x => x.Definitions ?? []);
		var methods = roots.OfType<ProgramNode>().SelectMany(x => x.Methods ?? []);
		var tables = roots.OfType<ProgramNode>().SelectMany(x => x.Tables ?? []);

		var compiler = new BytecodeCompiler<TestOp>(ResolvedCalls);
		return compiler.Compile(constants, contexts, methods, tables);
	}

	public static string FormatError(FileError error) =>
		$"({error.FileRange.Start.Line + 1},{error.FileRange.Start.Column + 1}): {error.Message}";
}
