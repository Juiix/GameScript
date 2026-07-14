# GameScript — Embedding Guide

> **Scope** This guide covers hosting GameScript in a C# game: parsing source, compiling to bytecode, running scripts, backing context variables, and attaching the VS Code debugger.
>
> Writing scripts? See **[LANGUAGE.md](LANGUAGE.md)** for the language reference.

---

## Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Parsing Source Files](#2-parsing-source-files)
3. [Mapping Commands → Enum](#3-mapping-commands--enum)
4. [Bytecode Compilation](#4-bytecode-compilation)
5. [Opcode Handlers & the Runner](#5-opcode-handlers--the-runner)
6. [ScriptState Lifecycle](#6-scriptstate-lifecycle)
7. [Context Variables (`IScriptContext`)](#7-context-variables-iscriptcontext)
8. [Frame Introspection](#8-frame-introspection)
9. [Indexing & Static Analysis](#9-indexing--static-analysis)
10. [Debugging (DAP)](#10-debugging-dap)

---

## 1 Architecture Overview

The toolchain is a straight pipeline:

```
.gs / .const / .context  →  parse  →  (index & analyze)  →  compile  →  run
       source              AstParser    visitors            BytecodeCompiler   ScriptRunner
```

| Package                     | Role                                                                    |
| --------------------------- | ----------------------------------------------------------------------- |
| **`GameScript.Language`**   | Lexer, parser, AST, analysis visitors, symbol indexing, bytecode compiler |
| **`GameScript.Bytecode`**   | The embeddable VM — `Value`, `ScriptState`, `ScriptRunner`, host interfaces |
| **`GameScript.DebugAdapter`** | DAP server — embed in your game to debug scripts from VS Code           |

A minimal host only needs `GameScript.Bytecode` (ship precompiled programs) — add `GameScript.Language` to compile scripts at load time, and `GameScript.DebugAdapter` for debugging.

---

## 2 Parsing Source Files

```csharp
string path = "scripts/player.gs";
var parser  = new AstParser(path, File.ReadAllText(path));
ProgramNode ast = parser.ParseProgram();

if (parser.Errors.Count > 0)
    foreach (var e in parser.Errors)
        Console.WriteLine(e);
```

Each file type has its own entry point:

| Extension  | Entry point        | Returns         | Node list                |
| ---------- | ------------------ | --------------- | ------------------------ |
| `.gs`      | `ParseProgram()`   | `ProgramNode`   | `.Methods`               |
| `.const`   | `ParseConstants()` | `ConstantsNode` | `.Definitions`           |
| `.context` | `ParseContexts()`  | `ContextsNode`  | `.Definitions`           |

Every AST node stores its `FilePath` and `FileRange` for diagnostics.

---

## 3 Mapping Commands → Enum

`command` declarations in script resolve to cases of an enum you define. The compiler converts each enum case name to a command name by inserting an underscore before every uppercase letter and every digit group, then lowercasing:

```
Int2Str     →   int_2_str
StrLength   →   str_length
```

Two numbering rules:

- **Core opcodes `0–99` are reserved** for the VM (`CoreOpCode`); `ScriptRunnerBuilder.Register` rejects them.
- **Command enum values must be `>= 1000`** — the compiler ignores enum cases below 1000 when building its command table, so a command mapped to a lower value fails to compile with *"Command '…' is not a supported operation."*

```csharp
public enum ServerOpCode : ushort
{
    Print         = 1000,
    PrintInt      = 1001,
    Int2Str       = 1100,
    SuspendForInt = 1200,
    Queue         = 1300,
    // …
}
```

---

## 4 Bytecode Compilation

Collect the parsed nodes from **all** files, then compile them together in one call:

```csharp
var compiler = new BytecodeCompiler<ServerOpCode>();
var result   = compiler.Compile(constantNodes, contextNodes, methodNodes);

BytecodeProgram         prog = result.Program;   // methods + constant pool
BytecodeProgramMetadata meta = result.Metadata;  // per-method line/file maps, local names, context slot names
```

- Constant declarations are folded into the constant pool at compile time — there is no init step to run.
- `func`, `label`, and `trigger` methods compile to bytecode; `command` declarations resolve to your opcode enum.
- Keep `meta` if you want stack traces or debugging — it maps every instruction back to a file and line, and names every local and context slot.

---

## 5 Opcode Handlers & the Runner

Register a handler for each command opcode, then build the runner:

```csharp
var builder = new ScriptRunnerBuilder<MyCtx>();

builder.Register((ushort)ServerOpCode.Int2Str, state =>
{
    var value = state.Pop();
    state.Push(Value.FromString(value.Int.ToString()));
});

builder.Register((ushort)ServerOpCode.SuspendForInt, state =>
{
    state.Execution = ScriptExecution.Paused;   // suspend — see §6
});

ScriptRunner<MyCtx> runner = builder.Build();
```

For stateful or allocation-sensitive handlers, implement `IScriptHandler<TContext>` instead of a lambda and pass it to the same `Register` overload.

> **Pop-push discipline:** Arguments are pushed left to right, so the **last** parameter is on top of the stack. A handler must pop all of its parameters and push exactly the return value(s) its `command` declaration promises.

### Dot-prefixed commands

Script can call a command as `cmd(…)`, `.cmd(…)`, `..cmd(…)`, and so on. The dot count is delivered to your handler as `state.Operand` — same opcode, different operand. The meaning is entirely yours to define; a common convention is `0` = act on the current entity, `1` = act on the interaction target.

```csharp
builder.Register((ushort)ServerOpCode.Anim, state =>
{
    var animId = state.Pop().Int;
    var target = state.Operand == 0 ? Self : Other;   // dot count
    target.PlayAnimation(animId);
});
```

---

## 6 ScriptState Lifecycle

### 6.1 Create, start, run

`ScriptState` owns the value stack and call frames. Construct it once (sizes are fixed at construction), then `Start` it for each script execution:

```csharp
var state = new ScriptState<MyCtx>(stackSize: 1024, frameSize: 64);

var entry = prog.Methods.First(m => m.Name == "mn_button_1 hud:logout");
state.Start(prog, ctx, entry /*, args… */);

ScriptExecution exec = runner.Run(state);
```

`Run` executes until the script finishes, pauses, or throws:

| `ScriptExecution` | Meaning                                             |
| ----------------- | --------------------------------------------------- |
| `Finished`        | Ran to completion                                   |
| `Paused`          | A handler set `Execution = Paused` (suspended)      |
| `Aborted`         | A handler threw — the exception propagates to you   |
| `Running`         | Only observed mid-execution (e.g. from a debugger)  |

### 6.2 Suspend & resume

A handler suspends the script by setting `state.Execution = ScriptExecution.Paused` — typically after showing UI and before waiting for player input. When the response arrives, push the value(s) the suspending `command` promised to return, then run again:

```csharp
// handler: suspend_for_int() returns int
builder.Register((ushort)ServerOpCode.SuspendForInt, state =>
{
    state.Execution = ScriptExecution.Paused;
});

// later, when the player submits a number:
state.Push(Value.FromInt(playerInput));
runner.Run(state);   // resumes right after the suspending command
```

### 6.3 Reuse

`Start` fully resets the state, so a pooled `ScriptState` can be re-`Start`ed for a new script with no allocation. Call `Clear()` when parking a state long-term — it drops the program/context references so they can be collected.

---

## 7 Context Variables (`IScriptContext`)

`%context` variables are backed by host storage, keyed by the slot ID from the `.context` declaration:

```csharp
public sealed class MyCtx : IScriptContext
{
    private readonly Dictionary<int, Value> _slots = new();

    public Value GetValue(int id)
    {
        int slot = id & 0xFFFF;   // declared slot ID
        int dot  = id >> 16;      // 0 or 1 — dot prefix (see below)
        return _slots.TryGetValue(slot, out var v) ? v : Value.Null;
    }

    public void SetValue(int id, in Value value)
    {
        _slots[(id & 0xFFFF)] = value;
    }
}
```

- **Dot prefix:** script can read/write `.%var` (one dot max). The dot flag arrives in the high 16 bits of `id` — conventionally it selects the *other* party's context in an interaction. Mask with `& 0xFFFF` if you don't use it.
- **Coercion:** the VM is forgiving about bool/int mismatches — `Value.Bool` treats any non-zero int as `true`, `Value.Int` reads `true` as `1`, and `Null` reads as `0`/`false`. Returning `Value.FromInt(1)` for a `bool %flag` works.

---

## 8 Frame Introspection

For stack traces, watchdogs, or custom tooling, `ScriptState` exposes its call stack read-only:

| Member                                | Purpose                                              |
| ------------------------------------- | ---------------------------------------------------- |
| `FrameDepth`                          | Current call depth (0 = entry method)                |
| `CurrentFrameView`                    | `FrameView` of the active frame (method, ip, stack start) |
| `CopyFrames(Span<FrameView>)`         | Snapshot the whole call stack                        |
| `GetLocalInFrame(frame, local)`       | Read a local in any frame                            |
| `GetContextValue(slot)`               | Read a context variable through the state's context  |
| `OpCount`                             | Instructions executed — useful for runaway-script limits |

Pair frame data with `BytecodeProgramMetadata` (`LineNumbers`, `FilePath`, `LocalNames`, `ContextNames`) to render human-readable traces.

---

## 9 Indexing & Static Analysis

> **Multi-file builds:** Run `IndexVisitor` over every file first to populate global tables, then run the analysis passes. This two-phase approach ensures cross-file symbol lookups succeed.

### 9.1 Global indexes

```csharp
var types      = new GlobalTypeIndex();
var symbols    = new GlobalSymbolTable();
var references = new GlobalReferenceTable();
```

### 9.2 Per-file indexing

```csharp
var errors    = new List<FileError>();
var fileIndex = new FileIndex();
var context   = new VisitorContext(types, symbols, filePath);
var indexer   = new IndexVisitor(fileIndex, context);
VisitAst(rootNode, indexer, errors);

references.AddFile(filePath, fileIndex.FileReferences);
symbols.AddFile(filePath,    fileIndex.FileSymbols);
```

`indexer.LocalIndexes` maps each `MethodDefinitionNode` to its local symbol table.

### 9.3 Analysis passes

```csharp
VisitAst(rootNode, new SymbolAnalysisVisitor(indexer.LocalIndexes, context),   errors);
VisitAst(rootNode, new SemanticAnalysisVisitor(indexer.LocalIndexes, context), errors);
VisitAst(rootNode, new TypeAnalysisVisitor(indexer.LocalIndexes, context),     errors);

static void VisitAst<T>(AstNode n, T v, List<FileError> errs) where T : IAstVisitor
{
    n.Accept(v);
    errs.AddRange(v.Errors);
}
```

| Visitor                    | Checks                                               |
| -------------------------- | ---------------------------------------------------- |
| `SymbolAnalysisVisitor`    | Duplicate and undefined symbol declarations          |
| `SemanticAnalysisVisitor`  | Control flow, prefix rules, break/continue scope     |
| `TypeAnalysisVisitor`      | Type inference and assignment compatibility          |

All visitors collect `FileError` instances for easy aggregation and reporting.

---

## 10 Debugging (DAP)

`GameScript.DebugAdapter` embeds a Debug Adapter Protocol server in your game process, so VS Code can attach, set breakpoints, step, and inspect locals and context variables in live scripts.

### 10.1 Wire up the host

```csharp
// Once per game, kept alive for the game's lifetime:
var debugHost   = new ScriptDebugHost();
var breakpoints = new BreakpointIndex();

// After compiling:
debugHost.SetProgramInfo(prog, meta);

// Start the loopback TCP server (default port 4711):
var debugServer = new ScriptDebugServer(debugHost, breakpoints, port: 4711);
await debugServer.StartAsync();
```

On hot reload, call `debugHost.ReloadProgram(newProg, newMeta)` — the active session re-verifies breakpoints against the new program.

### 10.2 Run scripts through the debug runner

`DebugScriptRunner` is an `IScriptRunner` drop-in over your normal runner that checks breakpoints per instruction:

```csharp
var token    = new ScriptDebugToken();
int threadId = debugHost.Register(state, token, "hud:logout");

var debugRunner = new DebugScriptRunner<MyCtx>(runner, token, breakpoints, debugHost, threadId);
var exec = debugRunner.Run(state);   // blocks while paused at a breakpoint

if (exec != ScriptExecution.Paused)
    debugHost.Unregister(threadId);  // script finished or aborted
```

Each registered script appears as a *thread* in VS Code. `Run` blocks the calling thread while paused in the debugger, so run debugged scripts somewhere that can afford to block.

### 10.3 Attach from VS Code

With the GameScript extension installed, add a `launch.json`:

```json
{
    "type": "gamescript",
    "request": "attach",
    "name": "Attach to Game",
    "host": "127.0.0.1",
    "port": 4711
}
```

While paused you get stack traces, stepping, locals, and the script's context variables in the Variables panel.

---

## License / Contribution

Feel free to open PRs to improve this guide or the engine itself.
