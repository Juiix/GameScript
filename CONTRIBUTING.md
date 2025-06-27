# Contributing to GameScript

First off, **thank you** for thinking about contributing! Every bug report, feature suggestion, and pull-request makes GameScript better. This guide explains our workflow and coding standards so your changes can be merged quickly.

---

## Table of Contents

1. [Ground Rules](#ground-rules)  
2. [Getting Started](#getting-started)  
3. [Development Workflow](#development-workflow)  
4. [Code Style & Tooling](#code-style--tooling)  
5. [Commit & PR Guidelines](#commit--pr-guidelines)  
6. [Issue Reporting](#issue-reporting)  

---

## Ground Rules

- **Be kind and respectful** to other contributors.  
- **Search first**—avoid duplicate issues and PRs.  
- **Small, focused changes** merge faster than large, sweeping ones.  
- All contributions are licensed under the **Apache 2.0** license unless stated otherwise.

---

## Getting Started

### 1. Fork & Clone

```bash
git clone https://github.com/juiix/gamescript.git
cd gamescript
````

### 2. Install Prerequisites

| Tool                               | Minimum Version |
| ---------------------------------- | --------------- |
| .NET SDK                           | 8.0             |
| Node + npm (for VS Code extension) | 20.x            |
| Git                                | 2.40            |

### 3. Build the Project

```bash
dotnet build -c Debug            # Core + LSP + Runtime
npm install && npm run compile   # Editor extension (inside GameScript.Editor)
```

> **Note**
> Automated test suites are not yet in place—feel free to propose, add, or improve tests as part of your contribution!

---

## Development Workflow

1. **Create a branch**: `git checkout -b feat/<short-topic>`
1. **Make your changes**. Add documentation or tests if relevant.
1. **Commit** (see guidelines below).
1. **Push** and open a **pull request** against `main`.
1. A maintainer will review, suggest changes if needed, and merge.

> **Tip**: Keep your branch synced with upstream `main` to avoid conflicts.

---

## Code Style & Tooling

Style conventions are still being defined for **GameScript**. Until an official guide is published:

* Write clear, self-documenting code and concise comments.

Community proposals for lightweight, automated formatting rules are *very* welcome.

---

## Commit & PR Guidelines

### Commit Messages

```
<type>(scope): <short summary>

<body – optional, wrapped at 72 chars>
```

Examples:

```
fix(runtime): handle division by zero
feat(lsp): add hover documentation for enums
```

**Types**: `feat`, `fix`, `docs`, `refactor`, `perf`, `chore`.

### Pull-Request Checklist

* [ ] Follows coding standards & passes CI.
* [ ] Updates documentation (`README`, `docs/`) if behavior changes.
* [ ] Squash commits if PR has a noisy history.

---

## Issue Reporting

* Use **“Bug report”** or **“Feature request”** templates.
* Provide **clear steps to reproduce**, expected vs. actual behavior, and stack traces.
* Attach minimal code samples or screenshots when helpful.

High-quality issues get fixed faster!

---