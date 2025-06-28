#!/usr/bin/env bash
set -euo pipefail

#######################
# CONFIGURATION
#######################

# Path to your language-server project
PROJECT_PATH="../GameScript.LanguageServer/GameScript.LanguageServer.csproj"

# Where your VS Code extension lives
VSCODE_EXT_ROOT="../GameScript.Vscode"
VSCODE_SERVER_ROOT="$VSCODE_EXT_ROOT/server"

# Where your Visual Studio extension’s server folder is
VSIX_SERVER_ROOT="../GameScript.VisualStudio/Server/win-x64"

# Build configuration
CONFIG=Release

# List of RIDs to publish for
RIDS=(
  "win-x64"
  "win-arm64"
  "osx-x64"
  "osx-arm64"
  "linux-x64"
  "linux-arm64"
)

#######################
# PUBLISH LOOP
#######################

echo "→ Cleaning existing published folders…"
rm -rf "$VSCODE_SERVER_ROOT" "$VSIX_SERVER_ROOT"
mkdir -p "$VSCODE_SERVER_ROOT"

for RID in "${RIDS[@]}"; do
  OUT="$VSCODE_SERVER_ROOT/$RID"
  echo "→ Publishing for $RID → $OUT"
  mkdir -p "$OUT"

  dotnet publish "$PROJECT_PATH" \
    -c "$CONFIG" \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$OUT"

  # macOS Code signing
  if [[ "$(uname)" == "Darwin" && "$RID" == osx-* ]]; then
    echo "→ Code signing macOS build for $RID"
    codesign --force --deep --sign - "$OUT/GameScript.LanguageServer"
  fi

  # If this is the Windows x64 build, also copy it to the VSIX folder
  if [[ "$RID" == "win-x64" ]]; then
    echo "→ Copying win-x64 build to Visual Studio extension at $VSIX_SERVER_ROOT"
    mkdir -p "$VSIX_SERVER_ROOT"
    # copy all files (exe, dlls, pdbs, etc.)
    cp -r "$OUT/"* "$VSIX_SERVER_ROOT/"
  fi
done

echo "✅ All RIDs published into $VSCODE_SERVER_ROOT"
echo "✅ win-x64 also copied into $VSIX_SERVER_ROOT"