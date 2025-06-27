import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import * as fs from 'fs';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    Executable,
    Trace
} from 'vscode-languageclient/node';

// ──────────────────────────────────────────────────────────────
// Client bootstrap
// ──────────────────────────────────────────────────────────────

//const serverOptions = namedPipeServer('gamescript');

let client: LanguageClient;

/**
 * Extension entry-point.
 */
export function activate(context: vscode.ExtensionContext): void {
    // 1) Figure out OS + architecture
    const platform = process.platform;     // 'win32' | 'darwin' | 'linux'
    const arch     = os.arch();            // 'x64' | 'arm64' | ...

    // 2) Map to your published RID folders
    let ridFolder: string;
    if (platform === 'win32') {
      ridFolder = arch === 'arm64' ? 'win-arm64' : 'win-x64';
    } else if (platform === 'darwin') {
      ridFolder = arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
    } else {
      ridFolder = arch === 'arm64' ? 'linux-arm64' : 'linux-x64';
    }

    // 3) Pick executable name
    const exeName = platform === 'win32'
      ? 'GameScript.LanguageServer.exe'
      : 'GameScript.LanguageServer';

    // 4) Resolve the full path inside your extension
    const serverPath = context.asAbsolutePath(
      path.join('server', ridFolder, exeName)
    );

    // Log where we’re looking
    console.log(`GameScript LSP exe path = ${serverPath}`);

    // Fail fast if it’s missing
    if (!fs.existsSync(serverPath)) {
      vscode.window.showErrorMessage(
        `GameScript LSP binary not found at ${serverPath}. ` +
        `Check that you included server/** in your VSIX.`
      );
      return;  
    }
    
    // ensure exec bit, swallow errors
    try {
      fs.chmodSync(serverPath, 0o755);
    } catch (e) {
      console.error(`Could not chmod +x ${serverPath}:`, e);
    }

    // 5) Define run/debug server options
    const run: Executable = {
      command: serverPath,
      args: ['--stdio'],
      options: { env: process.env }
    };
    const debug: Executable = {
      command: serverPath,
      args: ['--stdio'],   // your --debug flag to wait for debugger
      options: { env: process.env }
    };

    // 6) Client options
    const serverOptions: ServerOptions = { run, debug };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { scheme: 'file',     language: 'gamescript' },
            { scheme: 'untitled', language: 'gamescript' }
        ],
        synchronize: {
            configurationSection: 'gamescript'
        },
        progressOnInitialization: true
    };

    // 7) Create & start the client
    client = new LanguageClient(
        'gamescript',           // ID
        'GameScript LSP',      // human-readable name
        serverOptions,
        clientOptions
    );

    client.setTrace(Trace.Verbose);  // or Trace.Messages / Trace.Off

    // Kick it off (ignore the returned Promise)
    client.start();

    // VS Code will call client.dispose() automatically on shutdown
    context.subscriptions.push(client);
}

/**
 * VS Code calls this on shutdown / reload.
 */
export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}
