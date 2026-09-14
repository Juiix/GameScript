# GameScript — Embedding Guide

> **Scope** Hosting GameScript in a C# game: compiling source to bytecode, running scripts, suspending and resuming them, backing context variables, and attaching the VS Code debugger.
>
> The sections below follow the order a host executes them, and **[samples/HelloHost/Program.cs](samples/HelloHost/Program.cs)** is the complete, runnable version — about 150 lines, with comments numbered to match these sections. Read it alongside this guide.
>
> Writing scripts? See **[LANGUAGE.md](LANGUAGE.md)**.

---

## Contents

1. [Architecture Overview](#1-architecture-overview)
2. [The Command Enum](#2-the-command-enum)
3. [Parsing & Indexing](#3-parsing--indexing)
4. [Analysis](#4-analysis)
5. [Compilation](#5-compilation)
6. [Opcode Handlers & the Runner](#6-opcode-handlers--the-runner)
7. [Context Variables (`IScriptContext`)](#7-context-variables-iscriptcontext)
8. [ScriptState Lifecycle](#8-scriptstate-lifecycle)
9. [Frame Introspection](#9-frame-introspection)
10. [Debugging (DAP)](#10-debugging-dap)

---

## 1 Architecture Overview

The toolchain is a straight pipeline:

```
.gs source  →  parse  →  index  →  analyze  →  compile  →  run
               AstParser  IndexVisitor  4 visitors  BytecodeCompiler  ScriptRunner
```

| Package                       | Role                                                                    |
| ----------------------------- | ----------------------------------------------------------------------- |
| **`GameScript.Language`**     | Lexer, parser, AST, symbol indexing, analysis visitors, bytecode compiler (net8.0) |
| **`GameScript.Bytecode`**     | The embeddable VM — `Value`, `ScriptState`, `ScriptRunner`, host interfaces (netstandard2.1 / net8.0) |
| **`GameScript.DebugAdapter`** | Debug Adapter Protocol server — embed in your game to debug scripts from VS Code |

A minimal host only needs `GameScript.Bytecode` (ship precompiled programs). Add `GameScript.Language` to compile scripts at load time, and `GameScript.DebugAdapter` for debugging.

Two facts shape everything else:

- **All files compile together.** There are no imports; a name used in one file may be declared in any other. Index every file before analyzing any of them.
- **The host owns control flow.** Scripts run only when the host starts a method by name, and a `command` handler may pause a script for the host to resume later.

---

## 2 The Command Enum

Every `command` declared in script resolves to a case of an enum you define. Pick the enum before compiling: the compiler needs it to bind call sites.

```csharp
public enum ServerOpCode : ushort
{
    Print         = 1000,
    PrintInt      = 1001,
    Int2Str       = 1100,
    SuspendForInt = 1200,
    Queue         = 1300,
    QueueInt      = 1301,
    // …
}
```

**Numbering.** Core opcodes `0–99` are reserved for the VM (`CoreOpCode`); `ScriptRunnerBuilder.Register` rejects them. Command values must be **`>= 1000`** — the compiler ignores lower cases when building its command table, and a command mapped to one fails to compile with *"Command '…' is not a supported operation."*

**Naming.** The compiler derives each case's script name by inserting an underscore before every uppercase letter and before the first digit of each run of digits, then lowercasing:

```
Int2Str     →   int_2_str
StrLength   →   str_length
Vec3f       →   vec_3f
NPCName     →   n_p_c_name      (every uppercase letter, not every word — prefer NpcName)
```

**Overloads.** Script-side command overloads (same name, different parameter signatures) each bind to their own enum case via the `= internal_name` clause; the bound name goes through the same mapping. One script name fans out to several ops with no engine changes:

```gamescript
command queue(func method, int delay) = queue
command queue(func method, int delay, int arg0) = queue_int
```

---

## 3 Parsing & Indexing

*Program.cs step 3.* For each `.gs` file: parse it, then run the `IndexVisitor` to publish its declarations into the project-wide tables.

```csharp
var types      = new GlobalTypeIndex();      // built-in types; named types resolve through the symbols
var symbols    = new GlobalSymbolTable();
var references = new GlobalReferenceTable();

var parser = new AstParser(path, File.ReadAllText(path));
ProgramNode root = parser.ParseProgram();        // parser.Errors: syntax diagnostics

var fileIndex = new FileIndex();
var indexer   = new IndexVisitor(fileIndex, new VisitorContext(types, symbols, path));
root.Accept(indexer);                             // indexer.Errors, indexer.LocalIndexes

references.AddFile(path, fileIndex.FileReferences);
symbols.AddFile(path, fileIndex.FileSymbols);
```

- `ParseProgram()` returns a `ProgramNode` whose lists — `.Constants`, `.Contexts`, `.Types`, `.Methods`, `.Tables` — hold every declaration of that kind; `.Declarations` keeps them in source order. Every node carries its `FilePath` and `FileRange` for diagnostics.
- Keep `indexer.LocalIndexes` (a `Dictionary<MethodDefinitionNode, LocalIndex>`) per file; the analysis passes need it.
- Files may be indexed in any order, and in parallel: a named type or constant referenced from a file indexed earlier is resolved during analysis, not indexing.

---

## 4 Analysis

*Program.cs step 4.* Once **every** file is indexed, run the four analysis visitors over each file, in this order, with a fresh `VisitorContext` per file:

```csharp
var context     = new VisitorContext(types, symbols, path);
var typeVisitor = new TypeAnalysisVisitor(locals, context);
IAstVisitor[] passes =
[
    new NameResolutionVisitor(locals, context),   // MUST be first
    new SymbolAnalysisVisitor(locals, context),
    new SemanticAnalysisVisitor(locals, context),
    typeVisitor,
];
foreach (var pass in passes)
{
    root.Accept(pass);
    diagnostics.AddRange(pass.Errors);
}
foreach (var (call, symbol) in typeVisitor.ResolvedCalls)   // merge across files
    resolvedCalls[call] = symbol;
```

| Visitor                    | Checks                                               |
| -------------------------- | ---------------------------------------------------- |
| `NameResolutionVisitor`    | Classifies bare identifiers (local, func, command, table, type) — every later pass and the compiler rely on it |
| `SymbolAnalysisVisitor`    | Duplicate declarations, local/global name collisions, handler headers against trigger declarations |
| `SemanticAnalysisVisitor`  | Control flow, mark rules, `break`/`continue` scope, return paths, table shape (row arity, constant cells, >64-row warning), table/cursor misuse, duplicate context slots |
| `TypeAnalysisVisitor`      | Type inference, overload resolution (exact match outranks widening), named-type assignability and casts, table cell types, key uniqueness / key width, lookup arity and key types |

Every visitor collects `FileError`s (`Message`, `FileRange`, `Severity` — `Error`, `Warning`, `Information`, `Hint`). Stop on any `Error`; warnings are advisory.

`TypeAnalysisVisitor.ResolvedCalls` records which overload each call site chose. Merge the per-file dictionaries into one and hand it to the compiler — it is required whenever any name is overloaded.

---

## 5 Compilation

*Program.cs step 5.* Collect the declaration lists from **all** files and compile them in one call:

```csharp
var compiler = new BytecodeCompiler<ServerOpCode>(resolvedCalls);
BytecodeCompilerResult result = compiler.Compile(
    roots.SelectMany(r => r.Constants ?? []),
    roots.SelectMany(r => r.Contexts  ?? []),
    roots.SelectMany(r => r.Methods   ?? []),
    roots.SelectMany(r => r.Tables    ?? []),
    roots.SelectMany(r => r.Types     ?? []));

BytecodeProgram         prog = result.Program;   // methods + constant pool
BytecodeProgramMetadata meta = result.Metadata;  // per-method line/file maps, local names, context slot names
```

(Shorter `Compile` overloads without `tables` and `types` still exist for content that declares neither.)

What the compiler produces:

- `func`s and trigger handlers become `BytecodeMethod`s. Handlers are named `"<kind> <subject>"` — the string the host uses to start them.
- `command` declarations resolve to your opcode enum; `trigger` declarations produce no bytecode (they only validate handler headers).
- Constants fold into the constant pool; there is no init step. Constant tables produce no bytecode of their own — each access compiles to a compare chain over the rows, and all-constant keys fold to the cell. Named types erase to their root; the host sees plain `Value` slots.
- Every `BytecodeMethod` carries `ParamTypes`: one `ValueType` per parameter, aligned with locals `0..ParamCount-1` (`func` references report as `Int`). Hosts binding arguments by position — handlers of a variadic `trigger NAME(...)` — read it to decide how to parse each argument. If you persist bytecode yourself, serialize it; a `null` `ParamTypes` means "unknown".
- A call in tail position (`return f(...)` with matching return arity, or a call as the final statement of a void func) compiles to `TailCall`: the VM replaces the current frame instead of pushing one.
- Keep `meta` if you want stack traces or debugging — it maps every instruction back to a file and line and names every local and context slot.

---

## 6 Opcode Handlers & the Runner

*Program.cs step 6.* Register a handler for each command opcode, then build the runner:

```csharp
var builder = new ScriptRunnerBuilder<MyCtx>();

builder.Register((ushort)ServerOpCode.Int2Str, state =>
{
    var value = state.Pop();
    state.Push(Value.FromString(value.Int.ToString()));
});

builder.Register((ushort)ServerOpCode.SuspendForInt, state =>
{
    state.Execution = ScriptExecution.Paused;   // suspend — see §8.2
});

ScriptRunner<MyCtx> runner = builder.Build();
```

For stateful or allocation-sensitive handlers, implement `IScriptHandler<TContext>` instead of a lambda and pass it to the same `Register` overload.

> **Pop-push discipline:** Arguments are pushed left to right, so the **last** parameter is on top of the stack. A handler must pop all of its parameters and push exactly the return value(s) its `command` declaration promises — `Value.FromInt`, `FromBool`, `FromString`.

A `func`-typed argument arrives as an `int`: the index into `prog.Methods` of the referenced method. Store it and later `Start` that method (see the `queue` handler in the sample).

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

## 7 Context Variables (`IScriptContext`)

*Program.cs step 7.* `@context` variables are backed by host storage, keyed by the slot ID from the `TYPE @name = slot` declaration (the declared type may be a named type; the slot is still an `int`):

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

- **Dot prefix:** script can read/write `.@var` (one dot max). The dot flag arrives in the high 16 bits of `id` — conventionally it selects the *other* party's context in an interaction. Mask with `& 0xFFFF` if you don't use it.
- **Coercion:** the VM is forgiving about bool/int mismatches — `Value.Bool` treats any non-zero int as `true`, `Value.Int` reads `true` as `1`, and `Null` reads as `0`/`false`/`""`. Returning `Value.FromInt(1)` for a `bool @flag` works.
- The compiler rejects two declarations on one slot, so a slot id identifies exactly one variable; `meta.ContextNames` maps slots back to names for tooling.

---

## 8 ScriptState Lifecycle

*Program.cs steps 8–9.*

### 8.1 Create, start, run

`ScriptState` owns the value stack and call frames. Construct it once (sizes are fixed at construction — the defaults are a 1024-slot stack and 64 call frames), then `Start` it for each script execution:

```csharp
var state = new ScriptState<MyCtx>();            // or (stackSize: 1024, frameSize: 64)

var entry = prog.Methods.First(m => m.Name == "mn_button_1 hud:logout");
state.Start(prog, ctx, entry /*, args… */);

ScriptExecution exec = runner.Run(state);
```

`Run` executes until the script finishes, pauses, or throws:

| `ScriptExecution` | Meaning                                             |
| ----------------- | --------------------------------------------------- |
| `Finished`        | Ran to completion                                   |
| `Paused`          | A handler set `Execution = Paused` (suspended)      |
| `Aborted`         | A handler or the VM threw — the exception propagates to you (division by zero, more than `frameSize` nested calls, a handler's own exception) |
| `Running`         | Only observed mid-execution (e.g. from a debugger)  |

Tail transfers do not consume frames, so a script that recurses in tail position never hits the frame limit.

### 8.2 Suspend & resume

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

A paused `ScriptState` holds the whole call stack, so keep it around (one per in-flight script) until it finishes.

### 8.3 Reuse

`Start` fully resets the state, so a pooled `ScriptState` can be re-`Start`ed for a new script with no allocation. Call `Clear()` when parking a state long-term — it drops the program/context references so they can be collected.

---

## 9 Frame Introspection

For stack traces, watchdogs, or custom tooling, `ScriptState` exposes its call stack read-only:

| Member                                | Purpose                                              |
| ------------------------------------- | ---------------------------------------------------- |
| `FrameDepth`                          | Current call depth (0 = entry method)                |
| `CurrentFrameView`                    | `FrameView` of the active frame (method, ip, stack start) |
| `CopyFrames(Span<FrameView>)`         | Snapshot the whole call stack                        |
| `GetLocalInFrame(frame, local)`       | Read a local in any frame                            |
| `GetContextValue(slot)`               | Read a context variable through the state's context  |
| `OpCount`                             | Instructions executed — useful for runaway-script limits |

Pair frame data with `BytecodeProgramMetadata` (`MethodMetadata` — line numbers, file path, local names per method — and `ContextNames`) to render human-readable traces.

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

## Contributing

Corrections to this guide are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). When the host API changes, update the sample host in the same change; CI builds and runs it.
