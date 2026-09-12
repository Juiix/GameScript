# Changelog

All notable changes to GameScript will be documented in this file.

## [2.5.1]

**Tooling release — much smaller editor extensions.** No language, compiler, or VM change.

### Tooling
- VS Code: the marketplace now serves one platform-specific VSIX per OS/arch (`win32-x64`, `win32-arm64`, `darwin-x64`, `darwin-arm64`, `linux-x64`, `linux-arm64`) instead of a single package carrying all six language-server builds. Download size drops roughly 20×. `Scripts/package-vscode.sh` produces the per-platform packages from the folders `build-lsp.sh` publishes.
- Language server: self-contained publishes are now IL-trimmed (`TrimMode=partial`, invariant globalization, compressed single file) — about 15 MB on disk per platform instead of 71 MB. Applies to both the VS Code and Visual Studio extensions. Linux builds no longer need a system ICU library.

## [2.5.0]

**Feature release — named types, typed declarations, `.gs`-only sources.** Named types are aliases over a root type (`type item : int`) so content ids stop being interchangeable integers: `mn_open_dialogue(^item_bronze_sword)` becomes a compile error once the command is declared `(menu m)`, table columns become checkable, and hosts can emit typed constants straight from their content index. Named types erase at codegen — no new opcodes, no VM or `ScriptState` change. Alongside: constants, contexts, and types become ordinary top-level declarations of the one `.gs` grammar, retiring the `.const`/`.context` extensions.

### Language
- **`type NAME : int|string`** — a top-level declaration (anywhere a `func` or `table` may appear) introducing a *named type*: an alias over its root type. `type` is contextual (like `key`): it opens a declaration only at the start of a top-level line followed by `NAME :`; elsewhere it stays a plain identifier (content uses it as a parameter name). Type names share the func/command/trigger/table namespace, but a local or parameter may shadow a type name — type names are consulted only in type positions and as a cast callee when no local of that name is in scope. The `//` comment above the declaration is the type's doc. Root types are `int` and `string` only.
- **Assignability.** A named type widens to its root type implicitly (`item` → `int`: a typed id is still a number). The reverse, and any named-to-named conversion, requires an explicit cast `T(expr)`, legal only when the argument's root type matches (`item(x)` for an `int` or another int-rooted named type; `item("x")` is an error). The root zero literal (`0` / `""`) is implicitly assignable to every named type — it is the "none" id. Default parameter values, tuple destructuring (`(item a, int b) = f()`), table cells, and table key arguments all follow the same rule. This asymmetry is the feature: existing `command foo(int x)` keeps accepting typed constants, so hosts can retype declarations one at a time, while passing an untyped or wrong-kind value where a named type is expected fails at compile time.
- **Operators.** `==`/`!=` between two values of the same named type, or between a named type and its root (via widening) — `if portrait != 0` keeps working; comparing two different named types is an error. Arithmetic and ordering widen to the root and yield the root (`menu + 1` is `int`), so a computed id must be cast back to be passed to a typed parameter; for the same reason `x += 1` and `x++` on a named-typed variable are errors — write `x = menu(x + 1)`. String interpolation prints the root value. `switch` cases follow assignability to the subject type: on a named-type subject, constants of that type or the zero literal; on an `int` subject, typed constants are accepted by widening.
- **Casts.** `T(expr)` — the type name applied as a one-argument call. Compiles to nothing. Type names are not callable otherwise; a cast with zero or two-plus arguments is an error. A cast is not a constant expression: `case` values, table cells, and default parameter values must be `^constants` or literals.
- **Where named types appear.** Local declarations, func/command parameters and return types (tuple returns included), constant declarations (`item ^item_iron_sword = 42`), context declarations (`item @held_item = 1030` — the initializer remains the slot id and is not type-checked), and table columns (cells must be constants of that type or the zero literal; key arguments follow assignability; `for r in t` cursors read typed cells). Variadic-trigger handler parameters remain `int`/`string`/`bool`.
- **Overload resolution.** An exact named-type match outranks a widening match. `int`/`string` overload families are unaffected; a typed overload may sit beside an `int` one, and a call's result type follows the overload it resolved to. A bare `0`/`""` argument converts to every named type, so `f(0)` against `f(item)` and `f(menu)` alone is ambiguous; with an `int` overload present, `int` wins.
- **Top-level declarations unified.** `ParseProgram` now accepts constant declarations (`TYPE ^name = CONST`), context declarations (`TYPE @name = INT`), and `type` declarations as top-level nodes alongside methods and tables, in any order and in any file. Constants, contexts, types, tables, and methods may be co-located (`hit_type.gs` holding `type hit_type : int`, its constants, and a table). A `type` declared in one file is visible from every file regardless of index order.
- **Diagnostics.** Unknown named type; root type not `int`/`string`; duplicate type, or a type name colliding with a func/command/trigger/table; cast root mismatch / wrong arity; a type name used as a value; wrong-kind argument, assignment, return, cell, key, or `switch` case; comparison between two different named types; compound assignment or `++`/`--` on a named-typed variable; ambiguous call caused by a bare zero literal; calling a local that shadows a type name; a non-constant/non-context declaration reaching the obsolete `ParseConstants()`/`ParseContexts()`.

### Language (breaking)
- `.const` and `.context` are no longer distinct source kinds. Their content is unchanged in syntax and parses as `.gs`; hosts and editors should rename the files (`item.const` → `item.gs`, `varp.context` → `varp.gs`) and stop routing by extension. `ParseConstants()` / `ParseContexts()` remain as filtered views over `ParseProgram()` for one release, marked `[Obsolete]`.
- A top-level line of the form `type NAME :` is now a type declaration. No other change to reserved words.
- Semantic tightening only where hosts opt in: an existing declaration that names only `int`/`string`/`bool` behaves exactly as before. Content breaks only when a host retypes a command/func/column and a call site passes the wrong kind — the point of the release.

### Tooling
- Language server: named types in outline/workspace symbols (per project, like every other symbol); hover on a type name (declaration + doc), on a typed constant/context/local/parameter (shows the named type, and the root in parentheses), and on a cast; go-to-definition, find-references, and rename from any use of a named type — typed columns and casts included — to its `type` line; completion of type names in type positions and after `(` when the callee is a type (cast); signature help on a cast; diagnostics as above. Semantic tokens: type names as `type`; casts as `type` on the callee. Extension filter is `.gs` only.
- Both TextMate grammars color `type` declarations and typed declarations (`item ^x`, `item @x`, `item x`, typed parameters, returns, and columns); casts are colored by semantic tokens. `.const`/`.context` associations are removed from both extension manifests — rename the files to `.gs`.

### Fixed
- Rename no longer drops the first character of bare references (funcs, tables, locals, and now types): the edit skips exactly the occurrence's mark(s) — `^`, `@`, or a dot prefix — instead of always skipping one column.

### Embedding
- `TypeKind.Named` with `TypeInfo.Name` / `.Underlying` (the root type; `.Root`/`.RootKind` fold the alias away); `IdentifierType.Type`; `ProgramNode` exposes `.Constants`, `.Contexts`, `.Types` alongside `.Methods`/`.Tables` (`.Declarations` stays source-ordered). `ConstantsNode`/`ContextsNode` are retained as the return types of the obsolete entry points and are populated from `ProgramNode`; those entry points report an error for any other declaration. Host builders should switch from `OfType<ConstantsNode>()`/`OfType<ContextsNode>()` over parse roots to `ProgramNode.Constants`/`.Contexts`.
- `BytecodeCompiler.Compile(constants, contexts, methods, tables, types)` — new overload taking `ProgramNode.Types`; it drives erasure (a `string`-rooted type erases to `String`, an `int`-rooted one to `Int`). The 4-argument overload still compiles content that declares no types. Constant and context nodes carry their declared `TypeInfo`, which may be a named type; a named type is recorded unresolved at index time and resolved during analysis through the symbol table, so files may be indexed in any order and hosts need no new registry. Hosts see the same bytecode and the same `Value` slots as before.
- Hosts that generate constants should emit one `type` declaration per content kind (a generated `types.gs`) and typed constant lines, and may accept named-type names wherever they previously accepted `int` in their own definition files (context-variable definitions, for instance). Nothing forces this: untyped generation keeps compiling.

## [2.4.2]

**Feature release — variadic triggers and typed parameters in bytecode.** Lets a host dispatch raw input (chat commands) to handlers that each declare their own parameter list, parsing arguments by the handler's compiled signature.

### Language
- **`trigger NAME(...)`** — a *variadic* trigger declaration. Handlers of a variadic kind may declare any parameter list of `int`, `string` and `bool` (or none); the prefix rule does not apply. Other parameter types are an error ("may only declare int/string/bool parameters"). `...` anywhere else — on a `func`, `command`, or handler, or mixed with named parameters — is a parse error. `...` is a new token (`Ellipsis`); `..` remains the range operator.

### Embedding
- `BytecodeMethod.ParamTypes` (`ValueType[]?`) — the declared type of each parameter, aligned with locals `0..ParamCount-1`; `func`/label refs report as `Int`. Populated by the compiler for every method; the constructor's new trailing `paramTypes` argument defaults to `null` ("unknown") so hand-built or legacy programs still construct. Hosts that serialize bytecode should persist it.
- `MethodDefinitionNode.IsVariadic` / `SymbolInfo.IsVariadic`; `SymbolInfo.ParamSignature` and `.Signature` render `(...)` for variadic declarations (hover/outline pick this up unchanged).

## [2.4.1]

### Fixed
- The `not` operator is now emitted as a keyword semantic token, matching `and`/`or` and the grammar's `keyword.control` scope — it previously colored as a symbol operator in editors that prefer semantic tokens.

## [2.4.0]

**Feature release — constant tables.** One language feature, `table`, replacing data-in-code ladders (tier → outputs assignment blocks, menu-id ladders, name/jingle switches, desktop/mobile twin branches) with declared rows of constants. Additive parser/compiler work: no new opcodes, no VM changes; every table access lowers to the compare-chain / counted-loop bytecode the 2.1 `switch` and `for` emitters already produce.

### Language
- **`table NAME([key] TYPE col, ...)`** — a top-level declaration (anywhere a `func` may appear) with one indented row of comma-separated constants per line (`^const` or `int`/`string`/`bool` literals; cell types must match their column; every row has the header's arity; at least one row). Columns are `int`, `string`, or `bool`. Table names share the func/command/trigger namespace and are visible wherever funcs are. The `//` comment above the declaration is the table's doc.
- **Keys.** With no modifier, the leading column(s) are the key: a table's *key width* is the smallest number of leading columns whose values are unique across rows, so `smith_tier[bar]` keys on column 0 while `choice_ui[mobile, three]` keys on the leading pair. Positional lookups pass at least the key width and at most the column count. Marking extra columns `key` (contextual — not a reserved word) makes them independently lookup-able via `t[name: k]`; each `key` column must be unique on its own. Two identical rows are an error.
- **Lookups.** `t[k].col` / `t[a, b].col` / `t[name: k].col` (column type), `t.has(...)` (`bool`, same key forms), `t.at(i).col` (positional, 0-based), `t.count` (`int`), and `for r in t` (positional iteration; `r.col` is the cursor's only valid use — same function-flat scoping and `break`/`continue` rules as `for … in a..b`; a later `for` may reuse the cursor name only over the same table). A missing key or out-of-range index yields the column's zero value (`0`, `""`, `false`) unless the table declares a `default:` row (same syntax as `switch`; not counted by `count`, not reachable via `at`, its key cells are ignored). `count`, `has` and `at` are reserved column names. A bare `t[k]` / `t.at(i)` is a row, not a value — select a column.
- **Codegen.** No runtime tables: each access compiles to a compare chain over the rows (keys evaluated once into hidden temps), `for r in t` to a counted loop with each `r.col` a positional chain on the hidden index. Lookups whose keys are all constants — and `.count` — fold to the cell value at compile time. Tables above 64 rows warn (compare chains, not hash lookups).
- **Diagnostics.** Column type/arity/constness, key uniqueness, lookup arity vs. key width, per-key type, named lookups only on `key` columns, unknown table/column, table used as a value, cursor misuse, and a warning when an all-constant key matches no row (suppressed by a `default:` row).

### Language (breaking)
- New reserved word: `table` — content using it as an identifier must rename. (`key` is contextual and stays usable as a name.)
- `[`, `]` are now tokens (previously "Unexpected character"), and a `.` glued to an identifier, `)` or `]` (`t.count`, `t[k].col`) is a member-access operator rather than the start of a dot-prefixed command; `.cmd` after whitespace or at line start is unchanged.

### Tooling
- Language server: tables in outline/workspace symbols, hover (`table name(key int id, ...)` + doc comment), go-to-definition on table names and on columns (jumps to the header column), member completion after `t.` / `t[k].` / `t.at(i).` / `r.` (columns, or `count`/`has`/`at` on the table itself), semantic tokens for table names (type) and columns (new `property` token type). Columns are deliberately not renameable/referenceable this wave.
- Both TextMate grammars color `table` declarations, the `key` modifier, `.count/.has/.at`, and `.col` member reads.

### Embedding
- `BytecodeCompiler.Compile(constants, contexts, methods, tables)` — new overload taking every `ProgramNode.Tables` of the compile root; the 3-argument overload still exists and compiles content without tables. `ProgramNode` now exposes `Declarations` (source order), `Methods`, and `Tables`; `IdentifierType` gains `Table`/`Column`; `TypeKind` gains `TableRow`.

## [2.3.0]

### Added
- **Command op bindings are highlighted and hoverable.** The `= engine_op` binding on a command declaration is now a real AST node (`MethodDefinitionNode.BindingOperator` / `.BindingName`, typed `IdentifierType.EngineOp`): both editor grammars color it, the language server emits semantic tokens for it (new `namespace` token type), and hovering the op name explains the binding. `InternalName` is unchanged for compiler/host consumers.

### Fixed
- Rename / references / highlight no longer treat the engine-op name as a script symbol — F2 on `queue_strong_int` previously attempted to rename any same-named func or command.

## [2.2.0]

### Added
- **Sub-project support in the language server.** A `gamescript.json` marker file makes its folder an isolated project root: each project gets its own symbol/reference tables, so a workspace like `content/` holding `server/` and `client/` script projects (each with its own core.gs commands and triggers) no longer produces cross-project name conflicts. Files resolve to the nearest ancestor marker; files outside every marker belong to the workspace-root default project. Completion, hover, go-to-definition, references, rename, diagnostics, and dependency re-analysis are all scoped per project; workspace-symbol search spans all projects. Adding/removing/renaming a marker re-scopes and re-indexes the workspace live.

## [2.1.1]

### Added
- **Implicit line joining**: newlines and indentation are not significant inside `(...)`, so method signatures, call sites, and conditions may wrap across lines (continuation-line indentation is unrestricted). The 2.1 spec's wrapped-signature examples now compile.

## [2.1.0]

**Feature release — declared triggers, `switch`, `for`, and default parameter values.** All four features are additive parser/compiler work: no new opcodes, no VM changes, and existing 2.0 bytecode is unaffected.

### Language
- **Declared triggers**: trigger kinds are declared like commands (`trigger obj_op_1`, `trigger mn_text(string text)`), one per engine dispatch point, conventionally in `core.gs`. Handler headers are unchanged, but the compiler now validates them: an undeclared kind is a compile error (typos no longer produce silently dead handlers), and handler parameters must be a prefix of the declaration's. Subjects are not validated. Trigger names share the global namespace with funcs/commands but are never callable.
- **`switch` / `case` / `default`**: constant cases (`^const` or literals), multiple values per case, inline (`case x: stmt`) or indented-block bodies, no fallthrough, optional trailing `default`. Duplicate case values are a compile error. Subjects may be `int`, `string`, or `bool`. Compiles to the same if-chain bytecode as the equivalent ladder, with the subject evaluated once.
- **`for VAR in START..END`**: iterates ints over the half-open range `[START, END)`; both bounds evaluated once before the first iteration. The header declares the loop variable (always `int`, function-flat scope; a later `for` may reuse the name). `break`/`continue` work in both `for` and `while` — `continue` in a `for` still increments.
- **Default parameter values**: trailing func/command parameters may declare `= literal` or `= ^const` defaults, baked into the call site by the compiler. Call sites omit arguments only from the end; an omission that makes multiple overloads match is an ambiguity error.
- **Inline `if`/`else` bodies**: `if cond: stmt` and `else: stmt` — the same single-statement-after-colon rule as `case`. An inline statement and an indented block cannot be combined.

### Language (breaking)
- New reserved words: `trigger`, `switch`, `case`, `default`, `for`, `in` — content using them as identifiers must rename.
- Every trigger handler's kind must now be declared; add the `trigger` declaration block to your game's `core.gs` (the "Unknown trigger kind" error names the missing declaration).
- Multi-dot identifiers (`..name`, previously always a downstream error) now lex as the `..` range token followed by a name.

### Tooling
- Hover, completion, and signature help render default parameter values (`func f(string a, int anim = ^anim_still)`); trigger declarations render with the `trigger` keyword and surface their doc comments.
- Both editor grammars highlight the new keywords and the `trigger` declaration form.

## [2.0.3]

### Fixed
- Find-references/rename now also work on symbols in the *later* parts of an interpolated string (e.g. `{after}` in `"your {name} level is now {after}!"`): the desugared concat-chain nodes carried the whole string's range, swallowing cursor lookups for parts to their right. Chain nodes now span exactly their own parts.

## [2.0.2]

### Fixed
- Find-references, rename, and document highlight now work on symbols inside interpolated strings: the cursor lookup previously landed on the synthesized string/operator nodes instead of the embedded identifier. Zero-width synthetic nodes are excluded from position lookups.

## [2.0.1]

### Fixed
- Interpolated strings no longer emit whole-string semantic tokens that override the grammar's interpolation highlighting: synthesized literal parts now carry their real sub-ranges, embedded expressions keep their own function/variable colors, and the `{` `}` braces fall through to TextMate scopes.

## [2.0.0]

**Breaking release — the GameScript 2.0 syntax redesign.** Old-syntax content does not compile; convert with the content codemod (`tools/gs-migrate`).

### Language (breaking)
- Locals, params, func calls, and func references are **bare identifiers**: `int count`, `skill_name(skill)` (the `$` and `~` sigils are removed).
- Context variables use `@name` (was `%name`), in both `.gs` and `.context` files. `.context` declarations are `int @name = N` with no semicolon.
- `%` is now the **modulo operator** (with `%=` compound assignment).
- **`label` is removed** — everything is a `func`. The `@name()` jump form is gone; a call in tail position (`return f(...)`, or a call as the final statement of a void func with matching return arity) compiles to a **tail transfer** that replaces the current frame, preserving the old zero-stack-growth behavior. The `label` parameter type becomes `func`; any func is queueable/suspendable.
- **Overloading**: funcs/commands may share a name when parameter signatures differ; call sites resolve by argument count and types. Return types don't participate. Command overloads bind to engine ops with `= internal_name`.
- **String interpolation**: `"lvl {x}!"` compiles to the same concatenation bytecode as `+` chains; `{{`/`}}` are literal braces.
- **Declare + destructure**: `(bool ok, string err) = send_login(...)`, including mixed forms with existing locals.
- Grammar tightening (now errors): semicolons; parentheses wrapping a whole `if`/`while` condition; the `!` prefix (use `not`); tabs and non-multiple-of-4 indentation; empty `()` on trigger headers; a local sharing a name with any func/command.
- The all-paths-return check now correctly rejects an `if` without `else` as a guaranteed return.

### Runtime
- New core opcodes: `Modulo` and `TailCall` (top-frame replacement). `Goto` is deprecated — the compiler no longer emits it; its handler remains for one release.

### Tooling
- New `NameResolutionVisitor` analysis pass (must run before the other passes) classifies bare identifiers.
- `BytecodeCompiler` accepts the resolved-overload map from `TypeAnalysisVisitor.ResolvedCalls`.
- LSP and both editor grammars updated for the new marks, `not`, `func` type, and interpolation.
- New `GameScript.Language.Tests` suite; CI runs `dotnet test`.

## [1.5.3]

### Added
- New data file extension: `.option`

## [1.5.2]

### Added
- New data file extension: `.fx`

## [1.5.1]

### Changed
- NuGet packages are now published via trusted publishing

## [1.5.0]

### Added
- Color palette picker and completion for hex values, backed by `.palette` files

## [1.4.7]

### Added
- Hex support for int constants, for example: `int $value = 0xff`

## [1.4.6]

### Added
- Context variables are now shown in the locals panel when paused in the debug adapter

## [1.4.5]

### Fixed
- `Value.Int` now coerces bool to int (true = 1, false = 0)

## [1.4.4]

### Fixed
- `IScriptContext` returning an int for a bool context variable is now coerced correctly (non-zero = true)

## [1.4.3]

### Fixed
- Negative integer literals now allowed in constant declarations

## [1.4.2]

### Added
- New data file extension: `.varn`

## [1.4.1]

### Added
- Constant syntax highlighting (`^`) in object definition data files

## [1.4.0]

### Added
- Constant auto-completion (`^`) in object definition data files
- New data file extensions: `.anim`, `.param`, `.tex`, `.rig`

### Changed
- LSP now registers for `objectdef` language in addition to `gamescript`

## [1.3.9]

### Added
- Dot-prefix support for identifiers and LSP completion (`.%context`, `.command`, `..command`)
- Dot-prefixed commands for command operands

### Fixed
- Debug runner execution to mirror normal script runner
- Same-line breakpoint stepping in DAP
- DAP line number 0-index and re-triggering breakpoint issues

## [1.2.3]

### Added
- Debug Adapter Protocol (DAP) support for VS Code debugging
- DAP program reload
- DAP local variable names
- `.inv` data file extension
- Full `_` underscore usage in identifiers
- `and` and `or` keyword support
- Signature help and skip LSP processing for non-file URIs
- Label references for labels with parameters

### Fixed
- DAP line numbers and map caching
- DAP 1-indexed line numbers

## [1.2.1]

### Added
- String `+` concatenation operators
- `IScriptHandler` and `ScriptState` reuse
- `ScriptState.Clear()`
- Label argument type

### Fixed
- Block node file range end
- Various core bugs and label type issues

## [1.0.8]

### Added
- Program metadata with debug line numbers and file paths
- Context variable support
- Hover highlighting support
- Parent:child identifiers for triggers
- Comment summaries for symbols
- Marketplace publishing via GitHub Actions

### Fixed
- `IContext` property to store typed value, removed script globals, fixed core ops registration
- Consumer op parsing
- Completion handler
- LSP handling of open documents vs processed documents
- Local identifier renaming
- Parser double `$$` in return type signature

## [1.0.0]

- Initial release
