# Changelog

All notable changes to GameScript will be documented in this file.

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
