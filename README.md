# GameScript

**GameScript** is a lightweight, indentation-based scripting language and toolchain built for game development. It compiles to a compact bytecode format executed by a small embeddable VM, and ships with a full Language Server for first-class editor support.

---

## Language at a Glance

GameScript has three scalar types (`bool`, `int`, `string`), a `func` reference type, and a mark-based identifier system that makes every symbol's role visible at a glance:

```gamescript
// constants (.const)
int ^npc_knight = 2

// context variables (.context) — backed by a host-provided slot
bool @logged_in = 0

// methods (.gs)
command print(string value)           // host-implemented opcode, no body

// trigger kinds — one per engine dispatch point (core.gs)
trigger mn_button_1

func greet(string name, string suffix = "!")   // trailing params may default
    print("Hello, {name}{suffix}")

mn_button_1 login:submit              // trigger handler — UI event entry point
    greet("adventurer")               // final statement → tail transfer

// constant tables (.gs) — rows of constants with keyed lookup, no runtime cost
table skill(key int id, key string name, int jingle)
    ^skill_attack, "Attack", ^jingle_melee
    ^skill_mining, "Mining", ^jingle_gather

func level_up(int s)
    play(skill[s].jingle)             // t[key].col — compiles to a compare chain
```

| Kind        | Keyword    | Returns? | Notes                                              |
| ----------- | ---------- | -------- | -------------------------------------------------- |
| `func`      | `func`     | ✅        | Script routine; tail-position calls tail-transfer  |
| `command`   | `command`  | ✅        | Declares a host opcode; no body allowed            |
| `trigger`   | `trigger`  | ❌        | Declares an event dispatch point                   |
| handler     | *(kind)*   | ❌        | Event entry point; cannot be called from script    |
| `table`     | `table`    | —        | Compile-time constant rows; `t[k].col`, `t.at(i)`, `t.count`, `for r in t` |

| Symbol       | Mark   | Declared in   |
| ------------ | ------ | ------------- |
| Local var    | —      | `.gs`         |
| Constant     | `^`    | `.const`      |
| Context var  | `@`    | `.context`    |
| Table        | —      | `.gs`         |

**Learn more:**

- **[LANGUAGE.md](LANGUAGE.md)** — full language reference: types, operators, control flow, triggers, and common patterns
- **[EMBEDDING.md](EMBEDDING.md)** — hosting GameScript in your C# game: compiling, running, suspending, and debugging scripts

---

## Repo Layout

| Folder                       | Purpose                                                              |
| ---------------------------- | -------------------------------------------------------------------- |
| `GameScript.Language/`       | Lexer, parser, AST, visitors (index, semantic, type), bytecode compiler |
| `GameScript.Bytecode/`       | Bytecode VM and runtime (`ScriptState`, `ScriptRunner`)              |
| `GameScript.DebugAdapter/`   | DAP debug server — embed in your game to debug scripts from VS Code |
| `GameScript.LanguageServer/` | LSP server executable                                                |
| `GameScript.Vscode/`         | VS Code extension                                                    |
| `GameScript.VisualStudio/`   | Visual Studio 2022 extension                                         |

---

## NuGet Packages

| Package                   | Purpose                                                                       |
| ------------------------- | ----------------------------------------------------------------------------- |
| **`GameScript.Bytecode`** | Embed the VM — register opcode handlers, create `ScriptState`, run bytecode.  |
| **`GameScript.Language`** | Full toolchain — parse source, build the symbol index, run analysis passes, compile to bytecode. |
| **`GameScript.DebugAdapter`** | In-process DAP server — attach VS Code to a running game and debug live scripts. |

---

## Editor Support

The **VS Code extension** (`GameScript.Vscode`) bundles the language server and provides:

- Semantic syntax highlighting for `.gs`, `.const`, and `.context` files
- Completions, hover tooltips, and real-time diagnostics
- Sub-projects: a `gamescript.json` marker scopes its folder as an isolated project (e.g. `content/server` and `content/client` with separate core.gs command sets)
- Go to Definition, Find All References, Document Highlights
- Rename Symbol, Document Symbols, Workspace Symbols
- Script debugging — attach to a running game, set breakpoints, step, and inspect variables (see [EMBEDDING.md](EMBEDDING.md#10-debugging-dap))
- Syntax highlighting and constant completion for Object Definition files (`.varp`, `.varn`, `.item`, `.npc`, `.menu`, `.obj`, `.tile`, `.inv`, `.anim`, `.param`, `.tex`, `.rig`, `.fx`, `.option`)

A **Visual Studio 2022 extension** (`GameScript.VisualStudio`) is also available.

---

## Building

```bash
git clone https://github.com/Juiix/GameScript.git
cd GameScript
dotnet build
```

---

## Contributing

Pull requests are welcome. Please open an issue first to discuss major changes.
See [CONTRIBUTING.md](CONTRIBUTING.md) for coding standards and branch workflow.

---

## License

GameScript is licensed under the **Apache License 2.0** — see [LICENSE](LICENSE) for details.
