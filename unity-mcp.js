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
    description: 'Ping Unity Editor bridge to verify the connection is alive. Call this first to check if Unity Editor is running and reachable.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'unity_refreshAssets',
    description: 'Force Unity Editor to re-import assets and recompile scripts (AssetDatabase.Refresh). Call this after creating or modifying C# script files externally so Unity detects and compiles them without needing to switch focus to the Editor window.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'unity_createScene',
    description: 'Create a new Unity scene (.unity) file in the project. Optionally add it to Build Settings for inclusion in builds.',
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
    description: 'Open an existing Unity scene by name or path relative to Assets/. Supports single or additive mode.',
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
    description: 'Save the currently active scene to disk. Call after making changes to persist them.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'unity_createGameObject',
    description: 'Create a new GameObject in the active scene. Can be a primitive mesh (Cube, Sphere, Plane, etc.), an empty container, or include specific components like Rigidbody, Colliders, AudioSource etc. Supports setting parent, transform, and attaching multiple components at once.',
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
    description: 'Create a Unity prefab (.prefab) asset. Can create from scratch or from an existing FBX/GLB/OBJ model file. Supports adding components, tags, and layers. The prefab is saved to disk but NOT placed in the scene — use unity_instantiatePrefab to place it.',
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
    description: 'Instantiate (place) an existing prefab asset into the active scene. Use this when you want to add a saved prefab into the scene hierarchy at a specific position, rotation, and scale. Maintains prefab link for overrides.',
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
    description: 'Add a Unity component (built-in or custom script) to a GameObject in the active scene. Works with any compiled class in the project — built-in components like "Rigidbody", "BoxCollider", "AudioSource", "Camera", "Light", "NavMeshAgent", "Animator", "CharacterController", or any custom MonoBehaviour script already in the project such as "PlayerController", "EnemyAI", etc.',
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
    description: 'Update transform properties (position, rotation, scale), rename, or toggle active/inactive on a GameObject in the active scene. Supports Undo in the Editor.',
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
    name: 'unity_setMaterial',
    description: 'Assign a material (.mat) asset to the Renderer of a GameObject in the scene. The material must already exist in the project. Targets the first Renderer found on the object or its children.',
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
    description: 'Assign an AnimatorController (.controller) asset to a GameObject. Automatically adds an Animator component if one is not already present on the object.',
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
    description: 'Delete (destroy) a GameObject from the active scene by name. Supports Undo in the Unity Editor so it can be reversed with Ctrl+Z.',
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
    description: 'Return all root GameObjects in the active scene with their name, active state, and child count. Use this to discover what objects exist in the scene before performing operations on them.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'unity_listAssets',
    description: 'Search and list Unity project assets by type and optional folder. Use this to discover available prefabs, scenes, materials, scripts, textures, audio clips, animator controllers, and other assets before referencing them in other commands.',
    inputSchema: {
      type: 'object',
      properties: {
        folder:    { type: 'string', description: 'Sub-folder relative to Assets/ (empty = whole project)' },
        assetType: { type: 'string', description: '"Prefab","Scene","Material","AnimatorController","AudioClip","Texture2D","Script"' },
      },
      required: [],
    },
  },
  {
    name: 'unity_findGameObjects',
    description: 'Search for GameObjects in the active scene by name (supports partial/substring match). Returns matching objects with their full hierarchy path, active state, components list, and child count. Use this to locate objects before performing operations like addComponent, setProperty, setMaterial, etc.',
    inputSchema: {
      type: 'object',
      properties: {
        query:         { type: 'string', description: 'Search string to match against GameObject names (case-insensitive, partial match)' },
        hasComponent:  { type: 'string', description: 'Optional: only return objects that have this component, e.g. "Rigidbody", "Animator"' },
        includeChildren: { type: 'boolean', description: 'If true, also search children recursively (default true)' },
      },
      required: ['query'],
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
  unity_setMaterial:           'setMaterial',
  unity_setAnimatorController: 'setAnimatorController',
  unity_deleteGameObject:      'deleteGameObject',
  unity_getSceneHierarchy:     'getSceneHierarchy',
  unity_listAssets:            'listAssets',
  unity_findGameObjects:       'findGameObjects',
  unity_refreshAssets:         'refreshAssets',
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
