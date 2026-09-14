# GameScript

**GameScript** is a small, indentation-based scripting language for games. Scripts compile to compact bytecode that runs on an embeddable C# VM, and the toolchain ships a language server so scripts get real editor support: diagnostics as you type, hover docs, navigation, rename, and a debugger.

It is built for the code games are full of — dialogue, quests, UI handlers, item logic. The host game decides what scripts can do by declaring `command`s; scripts stay small, safe to hot-reload, and cheap to run.

```gamescript
// core.gs — the host implements commands in C#; triggers are events it fires
command println(string text)
command ask_number() returns int
trigger on_talk

// items.gs — a named type and a constant table
type item : int
item ^item_sword  = 1
item ^item_shield = 2

table stock(item id, string name, int price)
    ^item_sword,  "Sword",  25
    ^item_shield, "Shield", 15

// shop.gs
int @gold = 1                      // a context variable, backed by a host slot

on_talk blacksmith                 // a handler: the host fires "on_talk blacksmith"
    println("Blacksmith: Need something forged?")
    int choice = ask_number()      // suspends until the player answers
    if choice == 5: return
    buy(item(choice))              // final statement: tail transfer

func buy(item id)
    if not stock.has(id): return
    if @gold < stock[id].price
        println("Blacksmith: Come back richer.")
        return
    @gold -= stock[id].price
    println("Blacksmith: One {stock[id].name}. {@gold} gold left.")
```

---

## Get started

**Writing scripts**

1. Install the editor extension — **GameScript Tools** in the VS Code marketplace ([details](GameScript.Vscode/README.md)) or the Visual Studio 2022 marketplace ([details](GameScript.VisualStudio/README.md)).
2. Clone this repo and open [samples/hello](samples/hello/) in the editor. You get diagnostics, hover docs, and navigation immediately.
3. Work through **[TUTORIAL.md](TUTORIAL.md)** — it builds that sample up one step at a time.

**Running scripts**

Scripts run inside a host program, not on their own. The repo includes a minimal one:

```
dotnet run --project samples/HelloHost -- samples/hello
```

**Embedding in your game**

Add the NuGet packages and follow **[EMBEDDING.md](EMBEDDING.md)**; [samples/HelloHost/Program.cs](samples/HelloHost/Program.cs) is its worked example.

| Package                       | Purpose                                                                       |
| ----------------------------- | ----------------------------------------------------------------------------- |
| **`GameScript.Bytecode`**     | The VM — register opcode handlers, create a `ScriptState`, run bytecode. netstandard2.1 / net8.0. |
| **`GameScript.Language`**     | The front end — parse, index, analyze, compile to bytecode. net8.0.            |
| **`GameScript.DebugAdapter`** | In-process Debug Adapter Protocol server — attach VS Code to a running game.  |

---

## Documentation

- **[TUTORIAL.md](TUTORIAL.md)** — learn the language by building a small project.
- **[LANGUAGE.md](LANGUAGE.md)** — the language reference: types, declarations, operators, control flow, tables, triggers, and common patterns.
- **[EMBEDDING.md](EMBEDDING.md)** — hosting GameScript in a C# game: compiling, running, suspending, context variables, debugging.
- **[CHANGELOG.md](CHANGELOG.md)** — release notes, including breaking changes and upgrade notes.
- **[CONTRIBUTING.md](CONTRIBUTING.md)** — building, testing, and submitting changes.

---

## Language at a glance

| Kind        | Keyword    | Returns? | Notes                                                        |
| ----------- | ---------- | -------- | ------------------------------------------------------------ |
| `func`      | `func`     | ✅        | Script routine; a call in tail position replaces the frame   |
| `command`   | `command`  | ✅        | Host-implemented operation; no body in script                |
| `trigger`   | `trigger`  | ❌        | Declares a game-event dispatch point                         |
| handler     | *(kind)*   | ❌        | `<kind> <subject>` — entry point fired by the host           |
| `table`     | `table`    | —        | Compile-time constant rows: `t[k].col`, `t.has(k)`, `t.at(i)`, `t.count`, `for r in t` |
| `type`      | `type`     | —        | Named type over `int` or `string`; erases at compile time    |

| Symbol       | Mark   | Declared by                            |
| ------------ | ------ | -------------------------------------- |
| Local var    | —      | `TYPE name` inside a func              |
| Constant     | `^`    | `TYPE ^name = literal` (top level)     |
| Context var  | `@`    | `TYPE @name = slot` (top level)        |
| Named type   | —      | `type NAME : int\|string` (top level)  |
| Table        | —      | `table NAME(...)` (top level)          |

Scalar types are `bool`, `int` (32-bit), and `string`; `func` holds a method reference for scheduling.

---

## Repo layout

| Folder                        | Purpose                                                                |
| ----------------------------- | ---------------------------------------------------------------------- |
| `GameScript.Language/`        | Lexer, parser, AST, analysis visitors, symbol indexing, bytecode compiler |
| `GameScript.Bytecode/`        | Bytecode VM and runtime (`ScriptState`, `ScriptRunner`)                |
| `GameScript.DebugAdapter/`    | DAP debug server — embed in your game to debug scripts from VS Code    |
| `GameScript.LanguageServer/`  | LSP server executable (bundled by both editor extensions)              |
| `GameScript.Language.Tests/`  | xUnit suite: parser, analysis, execution, and doc-sample tests         |
| `GameScript.Vscode/`          | VS Code extension                                                      |
| `GameScript.VisualStudio/`    | Visual Studio 2022 extension                                           |
| `samples/`                    | `hello/` script project and `HelloHost/` minimal C# host               |
| `Scripts/`                    | Release packaging scripts (language server publish, VSIX packaging)    |

---

## Building and testing

Requires the .NET 8 SDK.

```bash
git clone https://github.com/Juiix/GameScript.git
cd GameScript

# front end, VM, language server, and the test suite
dotnet test GameScript.Language.Tests/GameScript.Language.Tests.csproj

# debug adapter
dotnet build GameScript.DebugAdapter

# the sample host
dotnet run --project samples/HelloHost -- samples/hello 1 5
```

`GameScript.VisualStudio` is a classic VSIX project: build it from Visual Studio 2022 (with the *Visual Studio extension development* workload) or with `msbuild`, not the `dotnet` CLI. The VS Code extension builds with `npm install && npm run compile` inside `GameScript.Vscode`. See [CONTRIBUTING.md](CONTRIBUTING.md) for the release scripts.

---

## Editor support

Both extensions bundle the language server and provide:

- Semantic highlighting for `.gs` files, named types and casts included
- Completions, hover tooltips, signature help, and real-time diagnostics
- Go to Definition, Find All References, Document Highlights, Rename, Document and Workspace Symbols
- Sub-projects: a `gamescript.json` marker scopes its folder as an isolated project, so `content/server` and `content/client` can each have their own `core.gs`
- VS Code: attach the debugger to a running game — breakpoints, stepping, locals, context variables (see [EMBEDDING.md §10](EMBEDDING.md#10-debugging-dap))

---

## Contributing

Pull requests are welcome. Please open an issue first to discuss major changes.
See [CONTRIBUTING.md](CONTRIBUTING.md) for the build, test, and release workflow.

---

## License

GameScript is licensed under the **Apache License 2.0** — see [LICENSE](LICENSE) for details.
