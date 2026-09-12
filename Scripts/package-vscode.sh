#!/usr/bin/env bash
set -euo pipefail

# Packages one platform-specific VSIX per RID published by build-lsp.sh, so each
# user downloads only the language server for their own OS/arch instead of all six.
#
# Usage (from Scripts/):  ./package-vscode.sh [out-dir]
#   RIDS="win-x64 osx-arm64" ./package-vscode.sh   # package a subset (local testing)
#
# Expects GameScript.Vscode/server/<rid>/ for every RID (run ./build-lsp.sh first),
# the extension compiled (npm run compile), and `vsce` on PATH (npm i -g @vscode/vsce).
# Plain bash 3.2 compatible (macOS runners) — no associative arrays.

VSCODE_EXT_ROOT="../GameScript.Vscode"
OUT_DIR="${1:-$VSCODE_EXT_ROOT}"
RIDS="${RIDS:-win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64}"

# .NET RID → vsce --target
target_for() {
  case "$1" in
    win-x64)     echo win32-x64 ;;
    win-arm64)   echo win32-arm64 ;;
    osx-x64)     echo darwin-x64 ;;
    osx-arm64)   echo darwin-arm64 ;;
    linux-x64)   echo linux-x64 ;;
    linux-arm64) echo linux-arm64 ;;
    *) echo "✗ Unknown RID: $1" >&2; exit 1 ;;
  esac
}

# Global vsce if installed (CI), otherwise fetch it through npx.
if command -v vsce >/dev/null 2>&1; then VSCE=(vsce); else VSCE=(npx --yes @vscode/vsce); fi

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
IGNORE_FILE="$(mktemp)"
trap 'rm -f "$IGNORE_FILE"' EXIT

for RID in $RIDS; do
  TARGET="$(target_for "$RID")"
  if [[ ! -d "$VSCODE_EXT_ROOT/server/$RID" ]]; then
    echo "✗ Missing $VSCODE_EXT_ROOT/server/$RID — run ./build-lsp.sh first" >&2
    exit 1
  fi

  # Base rules + exclude every other RID's server folder. (Positive patterns only:
  # a "!server/$RID/**" negation would re-include files the base rules drop.)
  cp "$VSCODE_EXT_ROOT/.vscodeignore" "$IGNORE_FILE"
  for OTHER in win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64; do
    [[ "$OTHER" == "$RID" ]] || echo "server/$OTHER/**" >> "$IGNORE_FILE"
  done

  VSIX="$OUT_DIR/gamescript-tools-$TARGET.vsix"
  echo "→ Packaging $TARGET (server/$RID) → $VSIX"
  (cd "$VSCODE_EXT_ROOT" && "${VSCE[@]}" package --target "$TARGET" --ignoreFile "$IGNORE_FILE" --out "$VSIX")
done

echo "✅ Platform-specific VSIXs written to $OUT_DIR"
