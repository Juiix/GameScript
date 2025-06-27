# GameScript

**GameScript** is a lightweight scripting language + toolchain purpose-built for game development.  
From NPC behaviors to UI logic, GameScript delivers fast parsing, compact bytecode, and first-class tooling for a smooth developer experience.

---

## ✨ Key Features

| Area | Highlights |
|------|------------|
| **Language Core** | Deterministic parser, small runtime footprint, familiar C-style syntax |
| **Bytecode VM**   | Ahead-of-time compiler → portable, sandboxed bytecode executed by a high-performance VM |
| **LSP**           | Full Language Server Protocol implementation: autocompletion, diagnostics, refactors |
| **Editor Kits**   | VS Code & other IDE extensions with syntax highlighting, IntelliSense, snippets |

---

## Repo Layout

| Folder | Purpose |
|--------|---------|
| `GameScript.Language/`  | AST, lexer, parser, type checker, bytecode emitter |
| `GameScript.Bytecode/`   | Cross-platform VM + standard library |
| `GameScript.LanguageServer/`       | Language server executable |
| `GameScript.VisualStudio/`    | VisualStudio extension |
| `GameScript.Vscode/`    | VS Code extension |

---

## Quick Start

```bash
# Clone
git clone https://github.com/your-org/gamescript.git
cd gamescript

# Build the toolchain
dotnet build -c Release

# Run a sample script
dotnet run --project examples/HelloWorld.gsproj
````

> **Note**
> Detailed embedding guides are TBD.

---

## Contributing

Pull requests are welcome! Please open an issue first to discuss major changes.
See [CONTRIBUTING](CONTRIBUTING.md) for coding standards, branch workflow, and CLA details.

---

## License

GameScript is licensed under the **Apache License 2.0**.
See the [LICENSE](LICENSE) file for details.