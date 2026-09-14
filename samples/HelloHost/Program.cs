// samples/HelloHost/Program.cs — the smallest useful GameScript host.
//
// Loads every .gs file under a script folder (default: samples/hello), runs the
// front end (parse -> index -> analyze -> compile), then executes the compiled
// program against a four-command "standard library". EMBEDDING.md walks through
// the same steps in the same order; the numbered comments below match its sections.
//
//   dotnet run --project samples/HelloHost -- [scriptDir] [answer ...]
//
// Each 'answer' is handed to the script when it calls ask_number(). When they run
// out the host reads stdin, and at end of input it answers 5 ("Leave").

using GameScript.Bytecode;
using GameScript.Language.Ast;
using GameScript.Language.Bytecode;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.Language.Visitors;

// ---------------------------------------------------------------------------
// 2. The command set. One enum case per 'command' in core.gs; values must be
//    >= 1000 (0-99 belong to the VM). Case names map to script names by inserting
//    '_' before each uppercase letter and lowercasing: AskNumber -> ask_number.
// ---------------------------------------------------------------------------
enum HelloOp : ushort
{
	Print = 1000,
	Println,
	AskNumber,
	Queue,
}

// ---------------------------------------------------------------------------
// 7. The context: storage behind '@' variables, keyed by the slot id each
//    declaration names ('int @gold = 1' -> slot 1). The high 16 bits carry the
//    '.@var' dot prefix; this host has no interaction target, so mask it off.
// ---------------------------------------------------------------------------
sealed class HelloContext : IScriptContext
{
	private readonly Dictionary<int, Value> _slots = new();

	public Value GetValue(int id) => _slots.TryGetValue(id & 0xFFFF, out var v) ? v : Value.Null;
	public void SetValue(int id, in Value value) => _slots[id & 0xFFFF] = value;
}

static class Program
{
	// funcs handed to queue(func, delay); run once the entry scripts finish
	private static readonly List<(int MethodIndex, int Delay)> Queued = new();

	static int Main(string[] args)
	{
		string scriptDir = args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "samples", "hello");
		var answers = new Queue<int>(args.Skip(1).Select(int.Parse));

		// -------------------------------------------------------------------
		// 3. Parse and index EVERY file before analyzing any of them: there are
		//    no imports, so a name used in one file may be declared in another.
		// -------------------------------------------------------------------
		var types = new GlobalTypeIndex();
		var symbols = new GlobalSymbolTable();
		var references = new GlobalReferenceTable();
		var files = new List<(string Path, ProgramNode Root, Dictionary<MethodDefinitionNode, LocalIndex> Locals)>();
		var diagnostics = new List<(string Path, FileError Error)>();

		foreach (var path in Directory.GetFiles(scriptDir, "*.gs", SearchOption.AllDirectories).OrderBy(p => p))
		{
			var parser = new AstParser(path, File.ReadAllText(path));
			ProgramNode root = parser.ParseProgram();
			diagnostics.AddRange(parser.Errors.Select(e => (path, e)));

			var fileIndex = new FileIndex();
			var indexer = new IndexVisitor(fileIndex, new VisitorContext(types, symbols, path));
			root.Accept(indexer);
			diagnostics.AddRange(indexer.Errors.Select(e => (path, e)));

			references.AddFile(path, fileIndex.FileReferences);
			symbols.AddFile(path, fileIndex.FileSymbols);
			files.Add((path, root, indexer.LocalIndexes));
		}

		// -------------------------------------------------------------------
		// 4. Analyze. NameResolutionVisitor must run first (it decides whether a
		//    bare name is a local, a func, or a command). TypeAnalysisVisitor
		//    records which overload every call resolved to; the compiler needs
		//    that map.
		// -------------------------------------------------------------------
		var resolvedCalls = new Dictionary<CallExpressionNode, SymbolInfo>();
		foreach (var (path, root, locals) in files)
		{
			var context = new VisitorContext(types, symbols, path);
			var typeVisitor = new TypeAnalysisVisitor(locals, context);
			IAstVisitor[] passes =
			[
				new NameResolutionVisitor(locals, context),
				new SymbolAnalysisVisitor(locals, context),
				new SemanticAnalysisVisitor(locals, context),
				typeVisitor,
			];
			foreach (var pass in passes)
			{
				root.Accept(pass);
				diagnostics.AddRange(pass.Errors.Select(e => (path, e)));
			}
			foreach (var (call, symbol) in typeVisitor.ResolvedCalls)
				resolvedCalls[call] = symbol;
		}

		foreach (var (path, error) in diagnostics)
			Console.Error.WriteLine($"{Path.GetFileName(path)}({error.FileRange.Start.Line + 1},{error.FileRange.Start.Column + 1}): {error.Severity}: {error.Message}");
		if (diagnostics.Any(d => d.Error.Severity == FileErrorSeverity.Error))
			return 1;

		// -------------------------------------------------------------------
		// 5. Compile all files together, in one call. Constants fold into the
		//    constant pool, tables into compare chains, named types erase.
		// -------------------------------------------------------------------
		var roots = files.Select(f => f.Root).ToArray();
		BytecodeCompilerResult compiled = new BytecodeCompiler<HelloOp>(resolvedCalls).Compile(
			roots.SelectMany(r => r.Constants ?? []),
			roots.SelectMany(r => r.Contexts ?? []),
			roots.SelectMany(r => r.Methods ?? []),
			roots.SelectMany(r => r.Tables ?? []),
			roots.SelectMany(r => r.Types ?? []));
		BytecodeProgram program = compiled.Program;   // compiled.Metadata maps every instruction back to file:line

		// -------------------------------------------------------------------
		// 6. Opcode handlers. Arguments are pushed left to right, so the LAST
		//    parameter is on top; a handler pops every parameter and pushes
		//    exactly what its 'returns' clause promises.
		// -------------------------------------------------------------------
		var builder = new ScriptRunnerBuilder<HelloContext>();
		builder.Register((ushort)HelloOp.Print, state => Console.Write(state.Pop().String));
		builder.Register((ushort)HelloOp.Println, state => Console.WriteLine(state.Pop().String));
		builder.Register((ushort)HelloOp.AskNumber, state =>
		{
			// suspend: the script sleeps until the host pushes the int and runs it again
			state.Execution = ScriptExecution.Paused;
		});
		builder.Register((ushort)HelloOp.Queue, state =>
		{
			int delay = state.Pop().Int;
			int method = state.Pop().Int;   // a 'func' value is the method's index
			Queued.Add((method, delay));
		});
		ScriptRunner<HelloContext> runner = builder.Build();

		// -------------------------------------------------------------------
		// 8. One reusable state (Start resets it) and a context seeded with the
		//    slots shop.gs declares.
		// -------------------------------------------------------------------
		var state = new ScriptState<HelloContext>();
		var ctx = new HelloContext();
		ctx.SetValue(1, Value.FromInt(40));   // int  @gold = 1
		ctx.SetValue(2, Value.FromInt(0));    // item @held = 2   (0 = nothing)

		// -------------------------------------------------------------------
		// 9. Run. The host picks entry points by name: a func, then a trigger
		//    handler (named "<kind> <subject>"), then a handler with an argument.
		// -------------------------------------------------------------------
		RunToCompletion(runner, state, program, ctx, "main", answers);
		RunToCompletion(runner, state, program, ctx, "on_talk blacksmith", answers);
		RunToCompletion(runner, state, program, ctx, "on_tick world", answers, Value.FromInt(1));

		foreach (var (method, delay) in Queued.ToArray())   // a real game would wait 'delay' ticks
		{
			Console.WriteLine($"[host] {delay} ticks later...");
			state.Start(program, ctx, program.Methods[method]);
			runner.Run(state);
		}

		Console.WriteLine($"[host] gold is now {ctx.GetValue(1).Int}");
		return 0;
	}

	/// <summary>
	/// Starts the named method and, each time the script pauses inside
	/// ask_number(), pushes the next answer and resumes it.
	/// </summary>
	private static void RunToCompletion(
		ScriptRunner<HelloContext> runner, ScriptState<HelloContext> state, BytecodeProgram program,
		HelloContext ctx, string methodName, Queue<int> answers, params Value[] args)
	{
		var method = program.Methods.First(m => m.Name == methodName);
		state.Start(program, ctx, method, args);
		var execution = runner.Run(state);
		while (execution == ScriptExecution.Paused)
		{
			int answer = NextAnswer(answers);
			Console.WriteLine($"[host] > {answer}");
			state.Push(Value.FromInt(answer));   // the value ask_number() returns
			execution = runner.Run(state);
		}
	}

	private static int NextAnswer(Queue<int> answers)
	{
		if (answers.Count > 0)
			return answers.Dequeue();
		Console.Write("[host] enter a number: ");
		return int.TryParse(Console.ReadLine(), out var n) ? n : 5;
	}

	private static string FindRepoRoot()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			if (File.Exists(Path.Combine(dir.FullName, "GameScript.sln")))
				return dir.FullName;
		return Directory.GetCurrentDirectory();
	}
}
