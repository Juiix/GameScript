# GameScript — End-to-End How-To

> **Scope** This single guide walks you through *every* step needed to load, analyse, compile, and run GameScript code inside your own C# project.

---

## Contents

1. [Language Surface](#1-language-surface)
2. [File-Type Rules](#2-file-type-rules)
3. [Parsing Source Files](#3-parsing-source-files)
4. [Mapping Commands → Enum](#4-mapping-commands--enum)
5. [Bytecode Compilation](#5-bytecode-compilation)
6. [Building & Running Scripts](#6-building--running-scripts)
7. [Extending the Runtime](#7-extending-the-runtime)
8. [Indexing & Static Analysis](#8-indexing--static-analysis)
9. [License / Contribution](#license--contribution)

---

## 1 Language Surface

| Concept          | Syntax / Prefix         | Returns? | Notes                                                                          |
| ---------------- | ----------------------- | -------- | ------------------------------------------------------------------------------ |
| **Data types**   | `bool`, `int`, `string` | –        | Only these three scalar types exist.                                           |
| **Constants**    | `int ^npc_knight = 2`   | –        | Literals only; defined in **.const** files.                                    |
| **Context vars** | `bool %logged_in = 0`   | –        | `%`-prefixed; slot-id given by the initializer; defined in **.context** files. |
| **func**         | `~do_math()`            | ✅        | Regular function; returns to caller.                                           |
| **label**        | `@do_login()`           | ❌        | One-way jump (GOTO); never returns.                                            |
| **command**      | *(internal)*            | ✅        | Declares a host-implemented opcode; no body allowed.                           |
| **trigger**      | *(host-only)*           | ❌        | Entry points only; cannot be called in script & **cannot return values**.      |

### Trigger Name Format

```
<trigger-type> <trigger-name>()
```

Example: `mn_text login:password()` ⇒ stored as **`"mn_text login:password"`**.

### Automatic Summaries

The parser captures a comment **immediately above** a declaration and stores it in `Summary`:

```gamescript
// An honorable knight
int ^npc_knight = 2  // Summary = "An honorable knight"
```
---

### Example Method Declarations

```gamescript
// Func: traditional function, returns a value
func add(int $a, int $b) returns int
    return $a + $b

// Label: one-way jump (GOTO)
label do_login()
    // ...login flow...

// Command: host‑implemented opcode (no body allowed)
command print(string $value)

// Trigger: UI event "login:submit" of button 1
mn_button_1 login:submit()
    @do_login()
```

### Tuple Return Values & Assignment

GameScript supports **tuples** for returning (and receiving) multiple values.

```gamescript
// Swap two integers
func swap(int $a, int $b) returns (int $b, int $a)
    return ($b, $a)

int $x = 3
int $y = 7

// Variables must already be declared before tuple-assignment
($x, $y) = ~swap($x, $y)
```

* You may attach **helper names** to each returned element (`$b`, `$a`) for clarity.
* At the call-site the left-hand side **must** be pre-declared variables; tuple literals cannot declare variables.

### Example Scripts

#### Constants (.const)
```gamescript
int ^version = 1
int ^one_hundred = 100
int ^magic_number = 123123
```

#### Context vars (.context)
```gamescript
bool %logged_in = 0
string %username = 1
```

#### Methods (.gs)
```gamescript
// --- commands ---
command print(string $value)

// --- functions ---
func greet(string $name)
    string $msg = "Hello, " + $name
    print($msg)

// --- trigger event (entry‑point) ---
mn_show login:background()
    if %logged_in
        print("Welcome back!")
    else
        ~greet("guest")
```

---

## 2 File-Type Rules

| Extension  | Allowed content                | Parser entry-point |
| ---------- | ------------------------------ | ------------------ |
| `.const`   | **Only** constant declarations | `ParseConstants()` |
| `.context` | **Only** context declarations  | `ParseContexts()`  |
| `.gs`      | **Only** method declarations   | `ParseProgram()`   |

> Mixing categories in the same file is invalid and yields parser errors.

---

## 3 Parsing Source Files

```csharp
string path = "scripts/player.gs";
var parser  = new AstParser(path, File.ReadAllText(path));
ProgramNode ast = parser.ParseProgram();

if (parser.Errors.Count > 0)
    foreach (var e in parser.Errors)
        Console.WriteLine(e);
```

Every AST node stores its `FilePath` and `FileRange` for diagnostics.

---

## 4 Mapping Commands → Enum

Convert a command identifier to an enum case by **removing underscores** and **capitalising** the next letter:

```
int_2_str   →   Int2Str
str_length  →   StrLength
```

Numbers stay in place (`2`). Core opcodes `< 100` are reserved by the engine.

```csharp
public enum ServerOpCode
{
    Print       = 1000,
    PrintInt,
    Int2Str     = 1100,
    // …
}
```

---

## 5 Bytecode Compilation

```csharp
var compiler = new BytecodeCompiler<ServerOpCode>();
var result   = compiler.Compile(constants, contexts, methods);
BytecodeProgram prog  = result.Program;      // op stream + const pool
BytecodeProgramMetadata meta = result.Metadata; // line ↔ source map
```

Only `func`, `label`, and **trigger** methods become bytecode; `command` declarations are for the host at runtime.

---

## 6 Building & Running Scripts

### 6.1 Register opcode handlers

```csharp
var builder = new ScriptRunnerBuilder<MyCtx>();
builder.Register((ushort)ServerOpCode.Int2Str, state =>
{
    var b = state.Pop();           // last arg
    var a = state.Pop();           // first arg
    state.Push(Value.FromString(a.Int.ToString()));
});
ScriptRunner<MyCtx> runner = builder.Build();
```

> **Pop-push discipline:** A handler *must* pop its parameters (top-of-stack order) and push its return value(s) back.

### 6.2 Create a `ScriptState` & run

```csharp
var entry  = prog.Methods.First(m => m.Name == "mn_text login:password"); // trigger
var ctx    = new MyCtx();
var state  = new ScriptState<MyCtx>(prog, ctx, entry /* args… */);

ScriptExecution exec = runner.Run(state);
```

`runner.Run` loops until `state.Execution` becomes `Finished`, `Aborted`, or custom-yield.

---

## 7 Extending the Runtime

* Implement **`IScriptContext`** to back `%context` variables.
* Register more opcodes through `ScriptRunnerBuilder.Register`.
* Tweak stack / frame sizes in `ScriptState` if your scripts are huge.

---

## 8 Indexing & Static Analysis

GameScript provides visitor classes that build symbol / reference tables and enforce semantic and type rules.

> **Multi-File builds:** When processing multiple files at once, run the IndexVisitor over every file first to populate the global symbol/reference tables, then execute the analysis passes. This two-phase approach guarantees that cross-file symbol look-ups succeed.

### 8.1 Global indexes (singletons)

```csharp
var types      = new GlobalTypeIndex();
var symbols    = new GlobalSymbolTable();
var references = new GlobalReferenceTable();
```

### 8.2 Per-file indexing

```csharp
var errors = new List<FileError>();
var fileIndex = new FileIndex();
var context   = new VisitorContext(_types, _symbols, filePath);
var indexVisitor   = new IndexVisitor(fileIndex, context);
VisitAst(rootNode, indexVisitor, errors);

// Merge into globals
references.AddFile(filePath, fileIndex.FileReferences);
symbols.AddFile(filePath,     fileIndex.FileSymbols);
```

`visitor.LocalIndexes` maps each method node to its local symbol table.

### 8.3 Further analysis passes

```csharp
VisitAst(rootNode, new SymbolAnalysisVisitor(indexVisitor.LocalIndexes, context),   errors);
VisitAst(rootNode, new SemanticAnalysisVisitor(indexVisitor.LocalIndexes, context), errors);
VisitAst(rootNode, new TypeAnalysisVisitor(indexVisitor.LocalIndexes, context),     errors);

// Helper
static void VisitAst<T>(AstNode n, T v, List<FileError> errs) where T : IAstVisitor
{
    n.Accept(v);
    errs.AddRange(v.Errors);
}
```

* **`SymbolAnalysisVisitor`** – duplicate / undefined symbol checks.
* **`SemanticAnalysisVisitor`** – control-flow, prefix rules, break/continue contexts.
* **`TypeAnalysisVisitor`** – type inference & compatibility checks.

All visitors collect `FileError` instances for easy aggregation.

---

## 9 License / Contribution

Feel free to open PRs to improve this guide or the engine itself.
