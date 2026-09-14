# GameScript Language Support for Visual Studio Code

Bring the full power of the **GameScript** language into Visual Studio Code. The extension bundles the Language Server that powers syntax highlighting, completions, diagnostics, code navigation, and more—so you can write your game scripts without leaving your favourite editor.

---

## ✨ Key Features

| Feature                         | Description                                                                                  |
| ------------------------------- | -------------------------------------------------------------------------------------------- |
| **Syntax Highlighting**         | Semantic colour-coding for keywords, types (named types and casts included), literals, operators, and comments in `.gs` files. |
| **Completions**                 | Context-aware completions for funcs, commands, tables, types, variables, constants, and context vars; signature help while typing arguments. |
| **Diagnostics**                 | Real-time error squiggles for parse errors, undefined symbols, type mismatches, and semantic rule violations. |
| **Hover Tooltips**              | Hover any symbol to see its full signature and any doc-comment attached to its declaration.  |
| **Go to Definition**            | Jump to where any symbol is declared—works across files in the workspace.                    |
| **Find All References**         | See every usage of a symbol across the entire workspace.                                     |
| **Document Highlights**         | All occurrences of the symbol under the cursor are highlighted in the current file.          |
| **Rename Symbol**               | Safely rename any symbol across all files in the workspace.                                  |
| **Document Symbols**            | Full outline of the current file (breadcrumbs, `Ctrl+Shift+O`).                             |
| **Workspace Symbols**           | Search for any symbol across the whole workspace (`Ctrl+T`).                                 |
| **Debugging**                   | Attach to a running game that embeds the GameScript debug adapter: breakpoints, stepping, locals, and context variables. |
| **ObjectDef Highlighting**      | Syntax highlighting for the host's Object Definition data files (`.item`, `.npc`, `.menu`, `.obj`, `.tile`, `.varp`, `.varn`, `.inv`, `.anim`, `.param`, `.tex`, `.rig`, `.fx`, `.option`, `.palette`). |

*(Looking for **Visual Studio 2022** support? → check out the **GameScript Tools** extension on the Visual Studio Marketplace.)*

---

## 📁 Supported File Types

| Extension           | Content                    |
| ------------------- | -------------------------- |
| `.gs`               | GameScript source: named types, constants, context variables, constant tables, funcs, commands, triggers, and handlers |
| `gamescript.json`   | Project marker — its folder (and subfolders) form one isolated project with its own namespace; the file's contents are ignored |
| Object Definition files (see above) | Syntax highlighting only |

---

## 🛠️ Getting Started

1. **Install**

   * Open the *Extensions* view (`Ctrl + Shift + X`) and search for **GameScript Tools** (publisher `giantblade`), **or**
   * Download the `.vsix` for your platform from the [releases page](https://github.com/Juiix/GameScript/releases) and run **Extensions › Install from VSIX…**.

2. **Reload VS Code** when prompted.

3. Open a folder containing `.gs` files — the language server indexes the workspace automatically. Put a `gamescript.json` in each script project's root if a workspace holds more than one (for example a server and a client project with different `core.gs` command sets).

New to the language? Start with the [tutorial](https://github.com/Juiix/GameScript/blob/main/TUTORIAL.md); the [language reference](https://github.com/Juiix/GameScript/blob/main/LANGUAGE.md) covers the rest.

> **System Requirements**
> Visual Studio Code 1.90 or later · Windows, macOS, or Linux (x64 / arm64). The bundled language server is self-contained — no .NET installation needed.

---

## 🐞 Debugging scripts

If your game embeds `GameScript.DebugAdapter` (see the [embedding guide](https://github.com/Juiix/GameScript/blob/main/EMBEDDING.md#10-debugging-dap)), add an attach configuration to `.vscode/launch.json`:

```json
{
    "type": "gamescript",
    "request": "attach",
    "name": "Attach to Game",
    "host": "127.0.0.1",
    "port": 4711
}
```

Start the game, press F5, and set breakpoints in any `.gs` file. Each running script appears as a thread; while paused you get the call stack, stepping, locals, and the script's context variables.

---

## 🐛 Known Issues / FAQ

If you hit a bug please [open an issue](https://github.com/Juiix/GameScript/issues) with reproduction steps and the smallest `.gs` snippet that shows the problem.

---

## 📜 License

The extension and bundled Language Server are licensed under the **Apache License 2.0**.