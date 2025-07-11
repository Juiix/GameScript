# GameScript

**GameScript** is a lightweight scripting language + toolchain purpose-built for game development.
From NPC behaviors to UI logic, GameScript delivers fast parsing, compact bytecode, and first-class tooling for a smooth developer experience.

---

## ✨ Key Features

| Area              | Highlights                                                                              |
| ----------------- | --------------------------------------------------------------------------------------- |
| **Language Core** | Deterministic parser, small runtime footprint, familiar C-style syntax                  |
| **Bytecode VM**   | Ahead-of-time compiler → portable, sandboxed bytecode executed by a high-performance VM |
| **LSP**           | Full Language Server Protocol implementation: autocompletion, diagnostics, refactors    |
| **Editor Kits**   | VS Code & other IDE extensions with syntax highlighting, IntelliSense, snippets         |

---

## Repo Layout

| Folder                       | Purpose                                            |
| ---------------------------- | -------------------------------------------------- |
| `GameScript.Language/`       | AST, lexer, parser, type checker, bytecode emitter |
| `GameScript.Bytecode/`       | Cross-platform VM + standard library               |
| `GameScript.LanguageServer/` | Language server executable                         |
| `GameScript.VisualStudio/`   | Visual Studio extension                            |
| `GameScript.Vscode/`         | VS Code extension                                  |

---

### 📦 NuGet Packages

| Package                   | Purpose                                                                                                  |
| ------------------------- | -------------------------------------------------------------------------------------------------------- |
| **`GameScript.Bytecode`** | Lightweight runtime/VM – create `ScriptState`, register opcode handlers, and run compiled bytecode.      |
| **`GameScript.Language`** | Full tool‑chain: lexer, parser, AST visitors, indexers, semantic & type analysis, and bytecode compiler. |

---

## 📚 Documentation

* **[Comprehensive Guide](GUIDE.md)** – Full language, tooling, compilation, and runtime walkthrough.

---

## Quick Start

```bash
# Clone
git clone https://github.com/Juiix/GameScript.git
cd GameScript

# Build the project
dotnet build -c Release
```

---

## Contributing

Pull requests are welcome! Please open an issue first to discuss major changes.
See [CONTRIBUTING](CONTRIBUTING.md) for coding standards, branch workflow, and CLA details.

---

## License

GameScript is licensed under the **Apache License 2.0**.
See the [LICENSE](LICENSE) file for details.
