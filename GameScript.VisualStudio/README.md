# GameScript Language Support for Visual Studio

Bring the full power of the **GameScript** language into Visual Studio 2022. The extension bundles the Language Server that powers syntax highlighting, completions, diagnostics, code navigation, and more—so you can write your game scripts without leaving your favourite IDE.

---

## ✨ Key Features

| Feature                         | Description                                                                                  |
| ------------------------------- | -------------------------------------------------------------------------------------------- |
| **Syntax Highlighting**         | Semantic colour-coding for keywords, types, literals, operators, and comments across `.gs`, `.const`, and `.context` files. |
| **Completions**                 | Context-aware completions for functions, labels, commands, variables, constants, and context vars. |
| **Diagnostics**                 | Real-time error squiggles for parse errors, undefined symbols, type mismatches, and semantic rule violations. |
| **Hover Tooltips**              | Hover any symbol to see its full signature and any doc-comment attached to its declaration.  |
| **Go to Definition**            | Jump to where any symbol is declared—works across files in the workspace.                    |
| **Find All References**         | See every usage of a symbol across the entire workspace.                                     |
| **Document Highlights**         | All occurrences of the symbol under the cursor are highlighted in the current file.          |
| **Rename Symbol**               | Safely rename any symbol across all files in the workspace.                                  |
| **Document Symbols**            | Full outline of the current file.                                                            |
| **Workspace Symbols**           | Search for any symbol across the whole workspace.                                            |
| **ObjectDef Highlighting**      | Syntax highlighting for Object Definition files (`.varp`, `.item`, `.npc`, `.menu`, `.obj`, `.tile`). |

*(Looking for **VS Code** support? → check out the **GameScript VS Code** extension.)*

---

## 📁 Supported File Types

| Extension           | Content                    |
| ------------------- | -------------------------- |
| `.gs`               | Method definitions (funcs, labels, commands, triggers) |
| `.const`            | Constant declarations      |
| `.context`          | Context variable declarations |
| `.varp` `.item` `.npc` `.menu` `.obj` `.tile` | Object Definition files (syntax highlighting only) |

---

## 🛠️ Getting Started

1. **Install**

   * Download from the Visual Studio Marketplace **or** use the in-IDE *Extensions › Manage Extensions* dialog and search for **GameScript**.

2. **Reload Visual Studio** when prompted.

3. Open a folder containing your GameScript files — the language server will index the workspace automatically and language features will activate on any supported file.

> **System Requirements**
> Visual Studio 2022 17.9 or later · Windows 10 v1903 or later

---

## 🐛 Known Issues / FAQ

If you hit a bug please [open an issue](https://github.com/Juiix/GameScript/issues) with reproduction steps.

---

## 📜 License

The extension and bundled Language Server are licensed under the **Apache License 2.0**.
