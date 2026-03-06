# GameScript — Language Reference & How-To

> **Scope** This guide covers the GameScript language, its type system, and how to embed the compiler/VM in your C# project.

---

## Contents

1. [Types & Symbols](#1-types--symbols)
2. [Method Kinds](#2-method-kinds)
3. [File-Type Rules](#3-file-type-rules)
4. [Language Examples](#4-language-examples)
5. [Parsing Source Files](#5-parsing-source-files)
6. [Mapping Commands → Enum](#6-mapping-commands--enum)
7. [Bytecode Compilation](#7-bytecode-compilation)
8. [Building & Running Scripts](#8-building--running-scripts)
9. [Extending the Runtime](#9-extending-the-runtime)
10. [Indexing & Static Analysis](#10-indexing--static-analysis)

---

## 1 Types & Symbols

GameScript has three scalar value types and a special `label` type for method references.

| Type     | Description                                               |
| -------- | --------------------------------------------------------- |
| `bool`   | Boolean — `true` / `false`                                |
| `int`    | 32-bit signed integer                                     |
| `string` | Immutable text string                                     |
| `label`  | Reference to a `label` method (for passing/scheduling)    |

Every symbol carries a prefix that makes its role visible at a glance:

| Prefix | Kind        | Declared in   | Example                      |
| ------ | ----------- | ------------- | ---------------------------- |
| `$`    | Local var   | `.gs` body    | `int $counter = 0`           |
| `^`    | Constant    | `.const`      | `int ^max_level = 99`        |
| `%`    | Context var | `.context`    | `int %tutorial_progress = 1` |
| `~`    | Func call   | call site     | `~get_input_str("…")`        |
| `@`    | Label call  | call site     | `@entry()`                   |

### Doc-Comments

A comment immediately above a declaration is captured as the symbol's summary and shown in hover tooltips:

```gamescript
// Kills the player and resets their position
command kill_player()
```

---

## 2 Method Kinds

| Kind      | Keyword   | Call Syntax | Returns? | Notes                                              |
| --------- | --------- | ----------- | -------- | -------------------------------------------------- |
| `func`    | `func`    | `~name()`   | ✅        | Regular function; returns to caller               |
| `label`   | `label`   | `@name()`   | ❌        | One-way jump (GOTO); execution never returns      |
| `command` | `command` | `name()`    | ✅        | Host-implemented opcode; no body in script        |
| `trigger` | *(type)*  | —           | ❌        | Game-event entry point; cannot be called from script |

### Funcs vs Labels

Use `func` when the caller needs execution to resume (returning a value, querying the host).
Use `label` for one-way control flow — jumping to the next state, starting a new interaction chain, or as a continuation after a suspend.

```gamescript
func multiply_and_add(int $x, int $y) returns int
    return ($x * $y) + ^const_value

label entry()
    int $result = ~multiply_and_add(10, 15)
    println_int($result)
    @next_step()         // one-way jump — never returns here

label next_step()
    println("Done.")
```

### Triggers

Triggers are entry points fired by the host (UI events, NPC interactions, etc.). They cannot be called from script.

The name format is `<trigger-type> <trigger-name>()`, stored internally as `"<trigger-type> <trigger-name>"`.

```gamescript
// NPC right-click action 1 — no component
npc_op_1 test_runner()
    @entry()

// Button 1 on the "hud" menu, "logout" component
mn_button_1 hud:logout()
    logout()
```

---

## 3 File-Type Rules

| Extension  | Allowed content                | Parser entry-point  |
| ---------- | ------------------------------ | ------------------- |
| `.const`   | **Only** constant declarations | `ParseConstants()`  |
| `.context` | **Only** context declarations  | `ParseContexts()`   |
| `.gs`      | **Only** method declarations   | `ParseProgram()`    |

Mixing categories in the same file is a parser error.

---

## 4 Language Examples

### Constants (`.const`)

Constants are compile-time literals — `int`, `bool`, or `string`.

```gamescript
int ^tutorial_killed_rat = 10
int ^const_value = 42
string ^example_message = "Hello, world"
```

### Context variables (`.context`)

Context variables map to player-specific slots provided by the host. The initializer is the **slot ID**, not a default value.

```gamescript
int %tutorial_progress = 1
// A skill value determining the player's damage output
int %skill_strength = 4
```

### Commands (`.gs`)

Commands declare host-implemented opcodes. No body is allowed in script — they are registered as C# handlers at runtime.

The `label` type accepts a method reference and is used for scheduling.

```gamescript
// Print a string value
command print(string $value)
// Convert an integer to its string representation
command int_2_str(int $value) returns string
// Suspend until the player submits a number; resume with the result
command suspend_for_int() returns int
// Enqueue a label to run after a delay (in game ticks)
command queue(label $method, int $delay)
```

### Functions (`.gs`)

```gamescript
func get_input_int(string $label) returns int
    open_dialogue(^menu_input_int)
    set_text(^menu_input_int, ^menu_input_int_title, $label)
    int $input = suspend_for_int()
    close_dialogue(^menu_input_int)
    return $input
```

### Tuple Return Values

Declare multiple return values with helper names for documentation:

```gamescript
func get_numbers() returns (int $num1, int $num2)
    return (10, 15)
```

Receive them with a tuple assignment — variables must be declared before the assignment:

```gamescript
int $a, $b
($a, $b) = ~get_numbers()
```

### Labels & One-Way Jumps (`.gs`)

Labels chain control flow without pushing a return frame. Useful for multi-step sequences, state machines, and tail-recursive loops.

```gamescript
label entry()
    int $a, $b
    ($a, $b) = ~get_numbers()
    print("Got: ")
    println_int($a)
    @conditional_branch(true)

label conditional_branch(bool $do_jump)
    if $do_jump
        @loop_test(0)
    else
        println("Skipping loop")

label loop_test(int $i)
    if $i >= 3
        println("Loop finished")
        @final()
    println_int($i)
    @loop_test($i + 1)     // tail-recursive — no stack growth

label final()
    println("Script complete.")
```

### Triggers (`.gs`)

Triggers call funcs and labels but cannot return values.

```gamescript
mn_button_1 hud:input_str()
    string $value = ~get_input_str("Enter some text!")
    print($value)

mn_button_1 hud:input_choice()
    int $choice = ~get_input_choice_2("Pick one", "Option A", "Option B")
    println_int($choice)
```

### Variable Declarations

Declare multiple variables of the same type on one line:

```gamescript
int $a, $b
string $first, $last
```

With initialisers:

```gamescript
int $x = 0
bool $active = true
string $msg = "Hello, " + $name
```

### Control Flow

```gamescript
if %logged_in
    println("Welcome back!")
else if %guest_mode
    println("Browsing as guest.")
else
    @login_flow()

while $i < 10
    println_int($i)
    $i++
```

---

## 5 Parsing Source Files

```csharp
string path = "scripts/player.gs";
var parser  = new AstParser(path, File.ReadAllText(path));
ProgramNode ast = parser.ParseProgram();

if (parser.Errors.Count > 0)
    foreach (var e in parser.Errors)
        Console.WriteLine(e);
```

Every AST node stores its `FilePath` and `FileRange` for diagnostics.

Use `ParseConstants()` for `.const` files and `ParseContexts()` for `.context` files.

---

## 6 Mapping Commands → Enum

Convert a command identifier to an enum case by **removing underscores** and **capitalising** the next letter:

```
int_2_str   →   Int2Str
str_length  →   StrLength
```

Numbers stay in place. Core opcodes `< 100` are reserved by the engine.

```csharp
public enum ServerOpCode
{
    Print           = 100,
    PrintInt        = 101,
    Int2Str         = 200,
    SuspendForInt   = 300,
    Queue           = 400,
    // …
}
```

---

## 7 Bytecode Compilation

```csharp
var compiler = new BytecodeCompiler<ServerOpCode>();
var result   = compiler.Compile(constants, contexts, methods);
BytecodeProgram prog         = result.Program;    // op stream + const pool
BytecodeProgramMetadata meta = result.Metadata;   // line ↔ source map
```

`func`, `label`, and `trigger` methods are compiled to bytecode. `command` declarations are resolved at runtime via the opcode enum.

---

## 8 Building & Running Scripts

### 8.1 Register opcode handlers

```csharp
var builder = new ScriptRunnerBuilder<MyCtx>();

builder.Register((ushort)ServerOpCode.Int2Str, state =>
{
    var value = state.Pop();
    state.Push(Value.FromString(value.Int.ToString()));
});

builder.Register((ushort)ServerOpCode.SuspendForInt, state =>
{
    state.Execution = ScriptExecution.Suspended;
});

ScriptRunner<MyCtx> runner = builder.Build();
```

> **Pop-push discipline:** A handler must pop its parameters in top-of-stack order and push its return value(s) back.

### 8.2 Create a `ScriptState` & run

```csharp
var entry = prog.Methods.First(m => m.Name == "mn_button_1 hud:logout");
var ctx   = new MyCtx();
var state = new ScriptState<MyCtx>(prog, ctx, entry /* args… */);

ScriptExecution exec = runner.Run(state);
// exec is Finished, Aborted, or Suspended
```

### 8.3 Resuming after a suspend

Push the host's response value before resuming:

```csharp
// Player submitted a number — push the result and resume
state.Push(Value.FromInt(playerInput));
runner.Run(state);
```

### 8.4 Reusing a ScriptState

To run a new script without allocating, call `Clear()` and reinitialise:

```csharp
state.Clear();
// re-initialise with new entry point and args, then Run again
```

---

## 9 Extending the Runtime

- Implement **`IScriptContext`** to back `%context` variables with player-specific storage.
- Register opcodes via `ScriptRunnerBuilder.Register`.
- Implement **`IScriptHandler`** for custom execution-state handling (suspend, yield, abort).
- Reuse `ScriptState` across executions with `state.Clear()` to avoid per-script allocations.

---

## 10 Indexing & Static Analysis

> **Multi-file builds:** Run `IndexVisitor` over every file first to populate global tables, then run the analysis passes. This two-phase approach ensures cross-file symbol lookups succeed.

### 10.1 Global indexes

```csharp
var types      = new GlobalTypeIndex();
var symbols    = new GlobalSymbolTable();
var references = new GlobalReferenceTable();
```

### 10.2 Per-file indexing

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

### 10.3 Analysis passes

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

## License / Contribution

Feel free to open PRs to improve this guide or the engine itself.
