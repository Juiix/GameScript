# Contributing to GameScript

Thank you for helping improve GameScript. This guide covers building, testing, and the conventions that keep changes easy to review.

---

## Contents

1. [Ground rules](#ground-rules)
2. [Prerequisites](#prerequisites)
3. [Building and testing](#building-and-testing)
4. [Project conventions](#project-conventions)
5. [Versioning and releases](#versioning-and-releases)
6. [Commits and pull requests](#commits-and-pull-requests)
7. [Reporting issues](#reporting-issues)

---

## Ground rules

- Be kind and respectful to other contributors.
- Search existing issues and pull requests first.
- Small, focused changes merge faster than sweeping ones. Open an issue before starting a large feature so the design can be discussed.
- Contributions are licensed under the repository's **Apache 2.0** license.

---

## Prerequisites

| Tool | Needed for | Version |
| --- | --- | --- |
| .NET SDK | Everything except the Visual Studio extension | 8.0 |
| Node.js + npm | The VS Code extension (`GameScript.Vscode`) | 20.x |
| Visual Studio 2022 with the *Visual Studio extension development* workload | The Visual Studio extension (`GameScript.VisualStudio`) only | 17.x |

```bash
git clone https://github.com/Juiix/GameScript.git
cd GameScript
```

---

## Building and testing

The solution contains one project that the `dotnet` CLI cannot build — `GameScript.VisualStudio` is a classic VSSDK project that needs `msbuild` and the VS SDK — so build and test per project rather than with a bare `dotnet build` at the root:

```bash
# front end, VM, language server, and the test suite (builds their dependencies)
dotnet test GameScript.Language.Tests/GameScript.Language.Tests.csproj

# debug adapter
dotnet build GameScript.DebugAdapter

# the sample host, run against the sample scripts
dotnet run --project samples/HelloHost -- samples/hello 1 5

# VS Code extension
cd GameScript.Vscode && npm install && npm run compile
```

Build `GameScript.VisualStudio` from Visual Studio (open `GameScript.sln`) or with `msbuild GameScript.VisualStudio/GameScript.VisualStudio.csproj`.

### The test suite

`GameScript.Language.Tests` is an xUnit suite covering the tokenizer, parser, analysis diagnostics, named types, end-to-end execution, position lookup, the language server's project registry, and the documentation samples. Things to know when adding tests:

- Scripts under test are inline C# raw strings. The shared `core.gs` (command and trigger declarations matching the `TestOp` enum) is `Harness/Fixtures.cs`; the fake host that records prints, queues, and suspensions is `Harness/TestRuntime.cs`.
- `Build`/`ErrorsFor` treat **warnings as failures**, so execution tests must be warning-free (for example, a constant table key that matches no row warns).
- `label` is a reserved word — don't use it as a local or column name in fixtures.
- `DocSamplesTests` parses every ```` ```gamescript ```` fence in the root `*.md` files and analyzes `samples/hello`. If you add a fence that is deliberately a fragment (a bare expression, statements outside a func), put `<!-- fragment -->` on the line before it.

CI (`.github/workflows/test.yml`) runs the suite and the sample host on every push and pull request.

---

## Project conventions

- **Two TextMate grammars must stay in lockstep.** `GameScript.Vscode/syntaxes/gamescript.tmLanguage.json` and `GameScript.VisualStudio/Grammars/gamescript.tmLanguage` (an XML plist twin) colour the same language. A grammar change goes into both; in the plist, top-level block rules use `\\` double-escaping and inner one-line rules single.
- **Language features erase where they can.** Tables, named types, `switch`, `for`, defaults, and tail calls all compile to existing opcodes. Prefer that over new VM opcodes; when a feature needs the VM, say so in the changelog's *Embedding* section.
- **Diagnostics are the product.** A new rule needs an analysis-visitor error with a message that says what to write instead, plus a test in `AnalysisTests.cs`.
- **Docs ship with the feature.** Update `LANGUAGE.md` (script authors), `EMBEDDING.md` (host authors), `TUTORIAL.md` if the on-ramp changes, and `CHANGELOG.md` in the same pull request.

---

## Versioning and releases

- The package version lives in `Directory.Build.props` (`GameScriptVersion`) and applies to the three NuGet packages. Feature commits fold the bump in: `feat(lang): …; bump 2.6.0`.
- The extension manifests (`GameScript.Vscode/package.json`, `GameScript.VisualStudio/source.extension.vsixmanifest`) are stamped from `GameScriptVersion` by the publish workflows, so their checked-in versions are not authoritative.
- Release workflows in `.github/workflows/`: `nuget-publish.yml` packs and pushes the packages; `publish-vscode.yml` runs `Scripts/build-lsp.sh` (self-contained, trimmed language server per platform) and `Scripts/package-vscode.sh` (one VSIX per platform) then publishes to the VS Code marketplace; `publish-visualstudio.yml` builds the VSIX with `msbuild` and publishes it. The shell scripts must stay bash-3.2 compatible (macOS runners): no associative arrays.

---

## Commits and pull requests

Commit messages follow Conventional Commits:

```
<type>(scope): <short summary>

<body – optional, wrapped at 72 chars>
```

Types: `feat`, `fix`, `docs`, `refactor`, `perf`, `chore`. Scopes in use: `lang`, `lsp`, `bytecode`, `ext`, `debug`, `docs`.

```
fix(bytecode): handle division by zero
feat(lsp): add hover documentation for enums
```

Before opening a pull request:

- [ ] `dotnet test GameScript.Language.Tests/GameScript.Language.Tests.csproj` passes.
- [ ] Both grammars updated if syntax changed.
- [ ] `LANGUAGE.md` / `EMBEDDING.md` / `TUTORIAL.md` / `CHANGELOG.md` updated if behaviour changed.
- [ ] Noisy history squashed.

---

## Reporting issues

Open an issue at <https://github.com/Juiix/GameScript/issues> with:

- clear steps to reproduce, expected versus actual behaviour;
- the smallest `.gs` snippet that shows the problem;
- for host or debugger problems, the exception text or stack trace.
