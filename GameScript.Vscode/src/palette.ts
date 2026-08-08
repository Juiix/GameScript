import * as vscode from 'vscode';

// ──────────────────────────────────────────────────────────────
// Project color palette (*.palette files)
// ──────────────────────────────────────────────────────────────

export interface PaletteEntry {
    /** Normalized value, e.g. "0xE94560" or "0xE94560FF" */
    hex: string;
    /** Trailing comment text from the palette line, if any */
    description?: string;
}

const PALETTE_GLOB = '**/*.palette';

// A palette line: 0x + RRGGBB or RRGGBBAA, optionally followed by a # comment
const PALETTE_LINE = /^0[xX]([0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)$/;

// A hex color token inside a document
const HEX_COLOR = /\b0[xX]([0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)\b/g;

/**
 * Loads and watches every *.palette file in the workspace,
 * exposing the merged list of color entries.
 */
export class PaletteManager implements vscode.Disposable {
    private _entries: PaletteEntry[] = [];
    private readonly _watcher: vscode.FileSystemWatcher;

    constructor() {
        this._watcher = vscode.workspace.createFileSystemWatcher(PALETTE_GLOB);
        this._watcher.onDidCreate(() => this.load());
        this._watcher.onDidChange(() => this.load());
        this._watcher.onDidDelete(() => this.load());
    }

    get entries(): readonly PaletteEntry[] {
        return this._entries;
    }

    async load(): Promise<void> {
        const files = await vscode.workspace.findFiles(PALETTE_GLOB);
        const byHex = new Map<string, PaletteEntry>();
        for (const file of files.sort((a, b) => a.fsPath.localeCompare(b.fsPath))) {
            let text: string;
            try {
                text = Buffer.from(await vscode.workspace.fs.readFile(file)).toString('utf8');
            } catch {
                continue;
            }
            for (const rawLine of text.split(/\r?\n/)) {
                const commentIndex = rawLine.indexOf('#');
                const value = (commentIndex >= 0 ? rawLine.slice(0, commentIndex) : rawLine).trim();
                const match = PALETTE_LINE.exec(value);
                if (!match) {
                    continue;
                }
                const hex = '0x' + match[1].toUpperCase();
                if (byHex.has(hex)) {
                    continue;
                }
                const description = commentIndex >= 0
                    ? rawLine.slice(commentIndex + 1).trim()
                    : undefined;
                byHex.set(hex, { hex, description: description || undefined });
            }
        }
        this._entries = [...byHex.values()];
    }

    dispose(): void {
        this._watcher.dispose();
    }
}

// ──────────────────────────────────────────────────────────────
// Completion: palette popup while typing 0x…
// ──────────────────────────────────────────────────────────────

export class PaletteCompletionProvider implements vscode.CompletionItemProvider {
    /** Re-open the popup on any character that can extend a 0x… token */
    static readonly triggerCharacters = [...'xX0123456789abcdefABCDEF'];

    constructor(private readonly palette: PaletteManager) {}

    provideCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position
    ): vscode.CompletionList | undefined {
        const prefix = document.lineAt(position.line).text.slice(0, position.character);
        // Fires from the leading '0' onward ("0", "0x", "0x3F", …) so the
        // popup is open before 'x' is even pressed. The token must not be
        // part of a larger identifier (e.g. "max0x") or number (e.g. "10").
        const match = /(?:^|[^A-Za-z0-9_.])(0(?:[xX][0-9A-Fa-f]*)?)$/.exec(prefix);
        if (!match) {
            return undefined;
        }
        const token = match[1];
        const range = new vscode.Range(
            position.line, position.character - token.length,
            position.line, position.character
        );
        const items = this.palette.entries.map((entry, index) => {
            const item = new vscode.CompletionItem(entry.hex, vscode.CompletionItemKind.Color);
            // A CSS-style hex string here makes VS Code render a swatch in the list
            item.documentation = '#' + entry.hex.slice(2);
            item.detail = entry.description;
            item.range = range;
            item.filterText = entry.hex;
            item.sortText = index.toString().padStart(4, '0');
            return item;
        });
        // isIncomplete forces VS Code to re-query on every keystroke while
        // the token grows, so the range and filtering always stay correct
        return new vscode.CompletionList(items, true);
    }
}

// ──────────────────────────────────────────────────────────────
// Inline swatches + native picker on 0xRRGGBB[AA] values
// ──────────────────────────────────────────────────────────────

export class HexColorProvider implements vscode.DocumentColorProvider {
    provideDocumentColors(document: vscode.TextDocument): vscode.ColorInformation[] {
        const text = maskNonCode(document.getText(), document.languageId);
        const colors: vscode.ColorInformation[] = [];
        for (const match of text.matchAll(HEX_COLOR)) {
            const hex = match[1];
            const range = new vscode.Range(
                document.positionAt(match.index),
                document.positionAt(match.index + match[0].length)
            );
            const channel = (i: number) => parseInt(hex.slice(i, i + 2), 16) / 255;
            const alpha = hex.length === 8 ? channel(6) : 1;
            colors.push(new vscode.ColorInformation(
                range,
                new vscode.Color(channel(0), channel(2), channel(4), alpha)
            ));
        }
        return colors;
    }

    provideColorPresentations(
        color: vscode.Color,
        context: { document: vscode.TextDocument; range: vscode.Range }
    ): vscode.ColorPresentation[] {
        const toHex = (v: number) =>
            Math.round(Math.max(0, Math.min(1, v)) * 255).toString(16).toUpperCase().padStart(2, '0');
        const hadAlpha = context.document.getText(context.range).length === 10;
        let label = '0x' + toHex(color.red) + toHex(color.green) + toHex(color.blue);
        if (hadAlpha || color.alpha < 1) {
            label += toHex(color.alpha);
        }
        return [new vscode.ColorPresentation(label)];
    }
}

/**
 * Replaces comments (and gamescript string literals) with spaces so the
 * color regex only matches real code, while keeping every offset intact.
 */
function maskNonCode(text: string, languageId: string): string {
    const chars = [...text];
    const blank = (from: number, to: number) => {
        for (let i = from; i < to; i++) {
            if (chars[i] !== '\n' && chars[i] !== '\r') {
                chars[i] = ' ';
            }
        }
    };

    if (languageId === 'objectdef') {
        // '#' starts a line comment
        for (let i = 0; i < chars.length; i++) {
            if (chars[i] === '#') {
                let end = i;
                while (end < chars.length && chars[end] !== '\n') { end++; }
                blank(i, end);
                i = end;
            }
        }
        return chars.join('');
    }

    // gamescript: // and /* */ comments, "…" strings
    let i = 0;
    while (i < chars.length) {
        const c = chars[i];
        const next = chars[i + 1];
        if (c === '/' && next === '/') {
            let end = i;
            while (end < chars.length && chars[end] !== '\n') { end++; }
            blank(i, end);
            i = end;
        } else if (c === '/' && next === '*') {
            let end = i + 2;
            while (end < chars.length && !(chars[end] === '*' && chars[end + 1] === '/')) { end++; }
            end = Math.min(end + 2, chars.length);
            blank(i, end);
            i = end;
        } else if (c === '"') {
            let end = i + 1;
            while (end < chars.length && chars[end] !== '"' && chars[end] !== '\n') {
                end += chars[end] === '\\' ? 2 : 1;
            }
            end = Math.min(end + 1, chars.length);
            blank(i, end);
            i = end;
        } else {
            i++;
        }
    }
    return chars.join('');
}
