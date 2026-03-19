import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { UnityClient, ConnectionState } from './unityClient.js';
import { registerChatParticipant } from './chatParticipant.js';

let client: UnityClient | null = null;
let statusBarItem: vscode.StatusBarItem | null = null;

export function activate(context: vscode.ExtensionContext): void {
  // ── Status bar item ────────────────────────────────────────────
  statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  statusBarItem.command = 'gdt-unity-mcp.connect';
  statusBarItem.tooltip = 'Unity MCP Bridge status. Click to connect.';
  updateStatusBar('disconnected');
  statusBarItem.show();
  context.subscriptions.push(statusBarItem);

  // ── Create WebSocket client ────────────────────────────────────
  const port = vscode.workspace.getConfiguration('gdt-unity-mcp').get<number>('bridgePort', 6400);
  client = new UnityClient(port, (state: ConnectionState) => {
    updateStatusBar(state);
    if (state === 'connected') {
      void vscode.window.showInformationMessage('Unity Copilot: Connected to Unity Editor ✅');
    }
  });

  // ── Register chat participant ──────────────────────────────────
  registerChatParticipant(context, client);

  // ── Register commands ──────────────────────────────────────────
  context.subscriptions.push(
    vscode.commands.registerCommand('gdt-unity-mcp.connect', () => {
      client!.connect();
      void vscode.window.showInformationMessage('Unity MCP: Connecting to Unity Editor...');
    }),

    vscode.commands.registerCommand('gdt-unity-mcp.disconnect', () => {
      client!.disconnect();
      void vscode.window.showInformationMessage('Unity MCP: Disconnected from Unity Editor.');
    }),

    vscode.commands.registerCommand('gdt-unity-mcp.installBridge', async () => {
      await installBridge(context);
    }),
  );

  // ── Auto-connect if configured ─────────────────────────────────
  const autoConnect = vscode.workspace.getConfiguration('gdt-unity-mcp').get<boolean>('autoConnect', true);
  if (autoConnect) {
    client.connect();
  }
}

export function deactivate(): void {
  client?.disconnect();
}

// ── Status bar helper ──────────────────────────────────────────────

function updateStatusBar(state: ConnectionState): void {
  if (!statusBarItem) { return; }
  switch (state) {
    case 'connected':
      statusBarItem.text = '$(pass-filled) Unity';
      statusBarItem.backgroundColor = undefined;
      statusBarItem.tooltip = 'Unity MCP: Connected. Click to disconnect.';
      statusBarItem.command = 'gdt-unity-mcp.disconnect';
      break;
    case 'connecting':
      statusBarItem.text = '$(sync~spin) Unity';
      statusBarItem.backgroundColor = undefined;
      statusBarItem.tooltip = 'Unity MCP: Connecting...';
      statusBarItem.command = undefined;
      break;
    case 'error':
      statusBarItem.text = '$(error) Unity';
      statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
      statusBarItem.tooltip = 'Unity MCP: Connection error. Click to retry.';
      statusBarItem.command = 'gdt-unity-mcp.connect';
      break;
    default: // disconnected
      statusBarItem.text = '$(circle-slash) Unity';
      statusBarItem.backgroundColor = undefined;
      statusBarItem.tooltip = 'Unity MCP: Disconnected. Click to connect.';
      statusBarItem.command = 'gdt-unity-mcp.connect';
  }
}

// ── Install Bridge ─────────────────────────────────────────────────

async function installBridge(context: vscode.ExtensionContext): Promise<void> {
  // Find Unity project root from open workspace folders
  const unityRoot = await findUnityProjectRoot();
  if (!unityRoot) {
    const pick = await vscode.window.showOpenDialog({
      canSelectFiles: false,
      canSelectFolders: true,
      canSelectMany: false,
      title: 'Select Unity Project Root Folder',
    });
    if (!pick || pick.length === 0) { return; }
    await copyBridgeFiles(context, pick[0].fsPath);
    return;
  }
  await copyBridgeFiles(context, unityRoot);
}

async function findUnityProjectRoot(): Promise<string | undefined> {
  const workspaceFolders = vscode.workspace.workspaceFolders;
  if (!workspaceFolders) { return undefined; }

  for (const folder of workspaceFolders) {
    const assetsPath = path.join(folder.uri.fsPath, 'Assets');
    const projectSettingsPath = path.join(folder.uri.fsPath, 'ProjectSettings');
    if (fs.existsSync(assetsPath) && fs.existsSync(projectSettingsPath)) {
      return folder.uri.fsPath;
    }
  }
  return undefined;
}

async function copyBridgeFiles(context: vscode.ExtensionContext, unityRoot: string): Promise<void> {
  const targetDir = path.join(unityRoot, 'Assets', 'Editor', 'UnityBridge');
  const sourceDir = path.join(context.extensionPath, 'unity-bridge');

  if (!fs.existsSync(sourceDir)) {
    void vscode.window.showErrorMessage(
      'Unity Copilot: Bridge source files not found. Please reinstall the extension.',
    );
    return;
  }

  // Create target directory
  fs.mkdirSync(targetDir, { recursive: true });

  // Copy all files
  const files = fs.readdirSync(sourceDir).filter(f => f.endsWith('.cs'));
  for (const file of files) {
    const src = path.join(sourceDir, file);
    const dst = path.join(targetDir, file);
    fs.copyFileSync(src, dst);
  }

  // Create .meta placeholder so Unity doesn't complain about missing metas
  // (Unity will generate proper .meta files on next import)

  // ── Create / update .vscode/mcp.json ──────────────────────────
  const mcpResult = await setupMcpJson(unityRoot, context.extensionPath);

  const fileList = files.join(', ');
  const mcpNote  = mcpResult === 'created'  ? ' MCP server registered in .vscode/mcp.json ✅'
                 : mcpResult === 'exists'   ? ' MCP server already registered in .vscode/mcp.json.'
                 : ' (Could not write .vscode/mcp.json — check permissions.)';

  const result = await vscode.window.showInformationMessage(
    `Unity MCP: Bridge installed at Assets/Editor/UnityBridge/ (${fileList}).${mcpNote}\n\nSwitch to Unity — it will recompile scripts automatically. The bridge starts listening on port ${client ? (vscode.workspace.getConfiguration('gdt-unity-mcp').get<number>('bridgePort', 6400)) : 6400} when Unity is in Editor mode.`,
    'Connect Now',
  );

  if (result === 'Connect Now') {
    client?.connect();
  }
}

// ── MCP JSON setup ─────────────────────────────────────────────────

async function setupMcpJson(
  unityRoot: string,
  extensionPath: string,
): Promise<'created' | 'exists' | 'error'> {
  try {
    const vscodeDir = path.join(unityRoot, '.vscode');
    const mcpPath   = path.join(vscodeDir, 'mcp.json');
    const mcpServerPath = path.join(extensionPath, 'dist', 'unity-mcp.js');

    fs.mkdirSync(vscodeDir, { recursive: true });

    // If file already exists, check if our server entry is already there
    if (fs.existsSync(mcpPath)) {
      const existing = fs.readFileSync(mcpPath, 'utf8');
      let json: Record<string, unknown>;
      try { json = JSON.parse(existing); } catch { json = {}; }

      const servers = (json['servers'] ?? {}) as Record<string, unknown>;
      if (servers['unity']) {
        return 'exists';  // already has our entry — don't overwrite
      }

      // Merge our entry in
      servers['unity'] = buildServerEntry(mcpServerPath);
      json['servers'] = servers;
      fs.writeFileSync(mcpPath, JSON.stringify(json, null, 2) + '\n', 'utf8');
      return 'created';
    }

    // Fresh file
    const content = {
      servers: {
        unity: buildServerEntry(mcpServerPath),
      },
    };
    fs.writeFileSync(mcpPath, JSON.stringify(content, null, 2) + '\n', 'utf8');
    return 'created';
  } catch {
    return 'error';
  }
}

function buildServerEntry(mcpServerPath: string): Record<string, unknown> {
  return {
    type: 'stdio',
    command: 'node',
    args: [mcpServerPath],
  };
}
