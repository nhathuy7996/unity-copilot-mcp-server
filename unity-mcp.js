#!/usr/bin/env node
/**
 * unity-mcp.js  —  MCP (Model Context Protocol) server for Unity Editor
 *
 * Architecture:
 *   AI / Copilot / Claude
 *     ←— JSON-RPC 2.0 over stdio —→
 *   this process  (unity-mcp.js)
 *     ←— WebSocket —→
 *   Unity Editor  (UnityBridgeServer  ws://127.0.0.1:6400)
 *
 * Register in .vscode/mcp.json  (workspace or user settings):
 * {
 *   "servers": {
 *     "unity": {
 *       "type": "stdio",
 *       "command": "node",
 *       "args": ["/Volumes/VAD/UNITY/unity-copilot/unity-mcp.js"]
 *     }
 *   }
 * }
 */
'use strict';

const WebSocket = require('ws');
const { randomUUID } = require('crypto');
const readline = require('readline');

// ── Config ────────────────────────────────────────────────────────
const BRIDGE_PORT = (() => {
  const a = process.argv.find(x => x.startsWith('--port='));
  return a ? parseInt(a.split('=')[1], 10) : 6400;
})();
const CMD_TIMEOUT = 20000;

// ── Unity WebSocket client ────────────────────────────────────────
let ws = null;
let wsReady = false;
const pending = new Map();

function connectBridge() {
  ws = new WebSocket('ws://127.0.0.1:' + BRIDGE_PORT);
  ws.on('open',    ()  => { wsReady = true; });
  ws.on('close',   ()  => { wsReady = false; setTimeout(connectBridge, 2000); });
  ws.on('error',   ()  => { wsReady = false; });
  ws.on('message', raw => {
    try {
      const msg = JSON.parse(raw.toString());
      const p = pending.get(msg.id);
      if (p) { clearTimeout(p.timer); pending.delete(msg.id); p.resolve(msg); }
    } catch { /* ignore malformed */ }
  });
}

function sendUnity(action, params) {
  return new Promise((resolve, reject) => {
    if (!wsReady) {
      return reject(new Error('Unity Editor not connected. Open Unity with the bridge installed.'));
    }
    const id = randomUUID();
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error('Timeout waiting for Unity action "' + action + '"'));
    }, CMD_TIMEOUT);
    pending.set(id, { resolve, reject, timer });
    ws.send(JSON.stringify({ id, action, params }));
  });
}

// ── Tool schema helper ────────────────────────────────────────────
const V3 = {
  type: 'object',
  properties: { x: { type: 'number' }, y: { type: 'number' }, z: { type: 'number' } },
};

// ── MCP tool definitions ──────────────────────────────────────────
const TOOLS = [
  {
    name: 'unity_ping',
    description: 'Ping Unity Editor bridge to verify the connection is alive.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'unity_createScene',
    description: 'Create a new Unity scene file. Optionally add it to Build Settings.',
    inputSchema: {
      type: 'object',
      properties: {
        name:               { type: 'string', description: 'Scene name without .unity extension' },
        savePath:           { type: 'string', description: 'Folder relative to Assets/, default "Scenes"' },
        addToBuildSettings: { type: 'boolean', description: 'Add to Build Settings' },
      },
      required: ['name'],
    },
  },
  {
    name: 'unity_openScene',
    description: 'Open a Unity scene by name or path relative to Assets/.',
    inputSchema: {
      type: 'object',
      properties: {
        scenePath: { type: 'string', description: 'Scene name or path, e.g. "MainScene" or "Scenes/Level1.unity"' },
        additive:  { type: 'boolean', description: 'Open without unloading current scene' },
      },
      required: ['scenePath'],
    },
  },
  {
    name: 'unity_saveScene',
    description: 'Save the currently active scene to disk.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'unity_createGameObject',
    description: 'Create a new GameObject in the active scene. Can be a primitive mesh, empty, or with specific components.',
    inputSchema: {
      type: 'object',
      properties: {
        name:          { type: 'string', description: 'GameObject name' },
        primitiveType: { type: 'string', enum: ['Cube','Sphere','Plane','Cylinder','Capsule','Quad','Empty'], description: 'Mesh primitive, or Empty for no mesh' },
        parent:        { type: 'string', description: 'Parent GameObject name in Hierarchy' },
        components:    { type: 'array',  items: { type: 'string' }, description: 'Unity component names e.g. ["Rigidbody","BoxCollider"]' },
        position:      V3,
        rotation:      V3,
        scale:         V3,
      },
      required: ['name'],
    },
  },
  {
    name: 'unity_createPrefab',
    description: 'Create a Unity prefab asset, optionally from an existing FBX/GLB model file.',
    inputSchema: {
      type: 'object',
      properties: {
        name:       { type: 'string', description: 'Prefab name without .prefab extension' },
        savePath:   { type: 'string', description: 'Folder relative to Assets/, default "Prefabs"' },
        modelPath:  { type: 'string', description: 'Source model path relative to Assets/, e.g. "00GAME/Models/hero.fbx"' },
        components: { type: 'array',  items: { type: 'string' }, description: 'Components to add' },
        tag:        { type: 'string' },
        layer:      { type: 'string' },
      },
      required: ['name'],
    },
  },
  {
    name: 'unity_instantiatePrefab',
    description: 'Instantiate an existing prefab asset into the active scene.',
    inputSchema: {
      type: 'object',
      properties: {
        prefabPath: { type: 'string', description: 'Prefab path relative to Assets/, e.g. "Prefabs/Enemy.prefab"' },
        name:       { type: 'string', description: 'Override the instance name' },
        parent:     { type: 'string', description: 'Parent GameObject name' },
        position:   V3,
        rotation:   V3,
        scale:      V3,
      },
      required: ['prefabPath'],
    },
  },
  {
    name: 'unity_addComponent',
    description: 'Add a Unity component to a GameObject in the active scene.',
    inputSchema: {
      type: 'object',
      properties: {
        gameObjectName: { type: 'string' },
        componentType:  { type: 'string', description: 'Unity component class, e.g. "Rigidbody", "AudioSource", "NavMeshAgent"' },
      },
      required: ['gameObjectName', 'componentType'],
    },
  },
  {
    name: 'unity_setProperty',
    description: 'Update transform (position/rotation/scale), rename, or toggle active on a GameObject.',
    inputSchema: {
      type: 'object',
      properties: {
        gameObjectName: { type: 'string' },
        position:       V3,
        rotation:       V3,
        scale:          V3,
        rename:         { type: 'string', description: 'New name for the GameObject' },
        active:         { type: 'boolean' },
      },
      required: ['gameObjectName'],
    },
  },
  {
    name: 'unity_createScript',
    description: 'Create a new C# MonoBehaviour (or other template) script in the Unity project.',
    inputSchema: {
      type: 'object',
      properties: {
        scriptName: { type: 'string', description: 'PascalCase class name, no .cs extension' },
        template:   { type: 'string', enum: ['MonoBehaviour','ScriptableObject','Editor','Interface','Empty'] },
        savePath:   { type: 'string', description: 'Folder relative to Assets/, default "Scripts"' },
        attachTo:   { type: 'string', description: 'Attach to this GameObject after compilation' },
      },
      required: ['scriptName'],
    },
  },
  {
    name: 'unity_setMaterial',
    description: 'Assign a material asset to the Renderer of a GameObject.',
    inputSchema: {
      type: 'object',
      properties: {
        gameObjectName: { type: 'string' },
        materialPath:   { type: 'string', description: 'Path to .mat file relative to Assets/' },
      },
      required: ['gameObjectName', 'materialPath'],
    },
  },
  {
    name: 'unity_setAnimatorController',
    description: 'Assign an AnimatorController to a GameObject. Adds Animator component if one is not present.',
    inputSchema: {
      type: 'object',
      properties: {
        gameObjectName: { type: 'string' },
        controllerPath: { type: 'string', description: 'Path to .controller file relative to Assets/' },
      },
      required: ['gameObjectName', 'controllerPath'],
    },
  },
  {
    name: 'unity_deleteGameObject',
    description: 'Delete a GameObject from the active scene. Supports Undo in the Editor.',
    inputSchema: {
      type: 'object',
      properties: {
        gameObjectName: { type: 'string' },
      },
      required: ['gameObjectName'],
    },
  },
  {
    name: 'unity_getSceneHierarchy',
    description: 'Return all root GameObjects in the active scene with name, active state, and child count.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'unity_listAssets',
    description: 'Search Unity project assets by type and optional folder. Useful for discovering available prefabs, scenes, materials, etc.',
    inputSchema: {
      type: 'object',
      properties: {
        folder:    { type: 'string', description: 'Sub-folder relative to Assets/ (empty = whole project)' },
        assetType: { type: 'string', description: '"Prefab","Scene","Material","AnimatorController","AudioClip","Texture2D","Script"' },
      },
      required: [],
    },
  },
];

// ── Bridge action map ─────────────────────────────────────────────
const TOOL_ACTION = {
  unity_ping:                  'ping',
  unity_createScene:           'createScene',
  unity_openScene:             'openScene',
  unity_saveScene:             'saveScene',
  unity_createGameObject:      'createGameObject',
  unity_createPrefab:          'createPrefab',
  unity_instantiatePrefab:     'instantiatePrefab',
  unity_addComponent:          'addComponent',
  unity_setProperty:           'setProperty',
  unity_createScript:          'createScript',
  unity_setMaterial:           'setMaterial',
  unity_setAnimatorController: 'setAnimatorController',
  unity_deleteGameObject:      'deleteGameObject',
  unity_getSceneHierarchy:     'getSceneHierarchy',
  unity_listAssets:            'listAssets',
};

// ── MCP JSON-RPC 2.0 over stdio ───────────────────────────────────
function rpcOut(msg) {
  process.stdout.write(JSON.stringify(msg) + '\n');
}

async function handleRpc(req) {
  const { id, method, params } = req;

  // MCP lifecycle handshake
  if (method === 'initialize') {
    return rpcOut({
      jsonrpc: '2.0', id,
      result: {
        protocolVersion: '2024-11-05',
        capabilities: { tools: {} },
        serverInfo: { name: 'unity-mcp', version: '1.0.0' },
      },
    });
  }
  if (method === 'notifications/initialized' || method === 'initialized') return;
  if (method === 'ping') return rpcOut({ jsonrpc: '2.0', id, result: {} });

  // Tool discovery
  if (method === 'tools/list') {
    return rpcOut({ jsonrpc: '2.0', id, result: { tools: TOOLS } });
  }

  // Tool call
  if (method === 'tools/call') {
    const toolName = params && params.name;
    const toolArgs = (params && params.arguments) || {};
    const action   = TOOL_ACTION[toolName];

    if (!action) {
      return rpcOut({ jsonrpc: '2.0', id, error: { code: -32601, message: 'Unknown tool: ' + toolName } });
    }

    let resp;
    try {
      resp = await sendUnity(action, toolArgs);
    } catch (err) {
      return rpcOut({
        jsonrpc: '2.0', id,
        result: { content: [{ type: 'text', text: '\u274c ' + err.message }], isError: true },
      });
    }

    let text = resp.success
      ? ('\u2705 ' + resp.message)
      : ('\u274c ' + resp.message);
    if (resp.data) text += '\n' + JSON.stringify(resp.data, null, 2);

    return rpcOut({
      jsonrpc: '2.0', id,
      result: { content: [{ type: 'text', text }], isError: !resp.success },
    });
  }

  rpcOut({ jsonrpc: '2.0', id, error: { code: -32601, message: 'Method not found: ' + method } });
}

// ── Main stdio loop ───────────────────────────────────────────────
const rl = readline.createInterface({ input: process.stdin, terminal: false });

rl.on('line', async line => {
  const t = line.trim();
  if (!t) return;
  let req;
  try { req = JSON.parse(t); }
  catch { return rpcOut({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error: invalid JSON' } }); }
  try { await handleRpc(req); }
  catch (e) { rpcOut({ jsonrpc: '2.0', id: (req && req.id) || null, error: { code: -32603, message: e.message } }); }
});

rl.on('close', () => process.exit(0));

// Connect to Unity bridge (auto-reconnects if Unity not yet open)
connectBridge();

// Don't crash on WebSocket errors during reconnect cycles
process.on('uncaughtException', () => {});
