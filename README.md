# GameScript

**GameScript** is a lightweight, indentation-based scripting language and toolchain built for game development. It compiles to a compact bytecode format executed by a small embeddable VM, and ships with a full Language Server for first-class editor support.

---

## Language at a Glance

GameScript has three scalar types (`bool`, `int`, `string`), four method kinds, and a prefix-based identifier system that makes every symbol's role visible at a glance:

```gamescript
// constants (.const)
int ^npc_knight = 2

// context variables (.context) — backed by a host-provided slot
bool %logged_in = 0

// methods (.gs)
command print(string $value)          // host-implemented opcode, no body

func greet(string $name)             // regular function, returns to caller
    string $msg = "Hello, " + $name
    print($msg)

label do_login()                     // one-way jump (GOTO), never returns
    ~greet("adventurer")

mn_button_1 login:submit()            // trigger — UI event entry point
    @do_login()
```

| Kind        | Prefix | Returns? | Notes                                              |
| ----------- | ------ | -------- | -------------------------------------------------- |
| `func`      | `~`    | ✅        | Standard function; returns to caller               |
| `label`     | `@`    | ❌        | One-way jump; never returns                        |
| `command`   | —      | ✅        | Declares a host opcode; no body allowed            |
| `trigger`   | —      | ❌        | Event entry point; cannot be called from script    |

| Symbol       | Prefix | Declared in   |
| ------------ | ------ | ------------- |
| Local var    | `$`    | `.gs`         |
| Constant     | `^`    | `.const`      |
| Context var  | `%`    | `.context`    |

See **[GUIDE.md](GUIDE.md)** for the full language reference, including tuple returns, type rules, and the bytecode compiler API.

---

## Repo Layout

| Folder                       | Purpose                                                              |
| ---------------------------- | -------------------------------------------------------------------- |
| `GameScript.Language/`       | Lexer, parser, AST, visitors (index, semantic, type), bytecode compiler |
| `GameScript.Bytecode/`       | Bytecode VM and runtime (`ScriptState`, `ScriptRunner`)              |
| `GameScript.LanguageServer/` | LSP server executable                                                |
| `GameScript.Vscode/`         | VS Code extension                                                    |
| `GameScript.VisualStudio/`   | Visual Studio 2022 extension                                         |

---

## NuGet Packages

| Package                   | Purpose                                                                       |
| ------------------------- | ----------------------------------------------------------------------------- |
| **`GameScript.Bytecode`** | Embed the VM — register opcode handlers, create `ScriptState`, run bytecode.  |
| **`GameScript.Language`** | Full toolchain — parse source, build the symbol index, run analysis passes, compile to bytecode. |

---

## Editor Support

The **VS Code extension** (`GameScript.Vscode`) bundles the language server and provides:

- Semantic syntax highlighting for `.gs`, `.const`, and `.context` files
- Completions, hover tooltips, and real-time diagnostics
- Go to Definition, Find All References, Document Highlights
- Rename Symbol, Document Symbols, Workspace Symbols
- Syntax highlighting for Object Definition files (`.varp`, `.item`, `.npc`, `.menu`, `.obj`, `.tile`)

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
