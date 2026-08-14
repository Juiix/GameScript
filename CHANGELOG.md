# Changelog

All notable changes to GameScript will be documented in this file.

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
