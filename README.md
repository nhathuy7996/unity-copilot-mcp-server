<p align="center">
   <img src="images/banner.png" alt="Unity MCP Server Banner" />
</p>

# Unity Copilot MCP Server

> Control Unity Editor from AI assistants (GitHub Copilot, Claude, etc.) via the Model Context Protocol (MCP).

## Demo Video

[![Unity Copilot MCP Server Demo](https://img.youtube.com/vi/mscQAL4Cr3A/hqdefault.jpg)](https://youtu.be/mscQAL4Cr3A)

[Watch the demo on YouTube](https://youtu.be/mscQAL4Cr3A)

## How It Works

```
AI Assistant (Copilot / Claude / etc.)
  ←— MCP (JSON-RPC over stdio) —→
unity-mcp.js (MCP Server)
  ←— WebSocket —→
Unity Editor (UnityBridgeServer on ws://127.0.0.1:6400)
  └─ UnityCommandHandler.cs (executes on main thread)
       └─ UnityEditor API (PrefabUtility, EditorSceneManager, …)
```

## Setup

### 1. Install the Unity Bridge

use the VS Code command: `Unity Copilot: Install Bridge into Unity Project`.

The bridge server starts automatically when Unity enters Editor mode and listens on `ws://127.0.0.1:6400`.

### 2. Configure MCP Server (optional)

Add to your `.vscode/mcp.json` (workspace or user settings):

```json
{
  "servers": {
    "unity": {
      "type": "stdio",
      "command": "node",
      "args": ["<path-to>/unity-mcp.js"]
    }
  }
}
```

### 3. Use with AI

The AI will automatically discover all available tools via MCP `tools/list`. Just ask it to do things in Unity — it knows what tools are available.

**Examples:**
- "Create a Cube at position 0,3,0 with a Rigidbody"
- "Find all GameObjects with Animator component"
- "Add BoxCollider to the Player object"
- "List all prefabs in the project"
- "Create a prefab called Enemy from Models/zombie.fbx"
- "Move the MainCamera to 0, 10, -5"

## Available MCP Tools

### Scene & Assets

| Tool | Description |
|---|---|
| `unity_ping` | Check if Unity Editor bridge is alive |
| `unity_refreshAssets` | Force Unity to re-import assets and recompile scripts |
| `unity_createScene` | Create a new scene, optionally add to Build Settings |
| `unity_openScene` | Open a scene by name or path |
| `unity_saveScene` | Save the active scene to disk |
| `unity_listAssets` | Search project assets by type and folder |

### GameObjects

| Tool | Description |
|---|---|
| `unity_createGameObject` | Create a primitive or empty GameObject in the scene |
| `unity_createPrefab` | Create a prefab asset from scratch or from a 3D model |
| `unity_instantiatePrefab` | Place an existing prefab into the scene |
| `unity_deleteGameObject` | Delete a GameObject from the scene (supports Undo) |
| `unity_setProperty` | Move, rotate, scale, rename, or toggle active state |
| `unity_getSceneHierarchy` | List all root GameObjects in the active scene |
| `unity_findGameObjects` | Search GameObjects by name with optional component filter |

### Components

| Tool | Description |
|---|---|
| `unity_addComponent` | Add any component (built-in or custom script) to a GameObject |
| `unity_setMaterial` | Assign a material to a GameObject's Renderer |
| `unity_setAnimatorController` | Assign an AnimatorController to a GameObject |
| `unity_getComponentProperties` | Read all serialized properties of a component (use before `setComponentProperty`) |
| `unity_setComponentProperty` | Set a specific property on a component (float, int, bool, string, enum, Vector3, Color) |

### Editor Operations

| Tool | Description |
|---|---|
| `unity_runMenuItem` | Execute any Unity Editor menu item by path (e.g. `"AI/Bake"`, `"Edit/Clear All PlayerPrefs"`) |
| `unity_captureScreenshot` | Capture the Scene view and return it as an inline PNG image |
| `unity_undoRedo` | Perform Undo or Redo in Unity Editor (`"undo"` / `"redo"`) |
| `unity_getUndoHistory` | Get the current undo group index and name |

### Workflow: Adding a Custom Script

Since the AI can write files directly, the workflow for adding a custom script is:

1. AI creates the `.cs` file in your Unity project's `Assets/` folder
2. AI calls `unity_refreshAssets` to force Unity to compile
3. AI calls `unity_addComponent` to attach the compiled script to a GameObject

### Workflow: Editing a Component Property

1. AI calls `unity_getComponentProperties` to read all property names and current values
2. AI calls `unity_setComponentProperty` with the exact `propertyName` and new `value`

## VS Code Extension (Chat Participant)

This project also includes a VS Code extension with a `@unity` chat participant for GitHub Copilot Chat.

**Commands:**
- `@unity /status` — Check connection to Unity Editor
- `@unity /help` — Show available commands and examples
- `@unity /undo` — Undo the last operation in Unity Editor
- `@unity /redo` — Redo the last undone operation
- `@unity /history` — Show the current undo group

## Settings

| Setting | Default | Description |
|---|---|---|
| `unity-copilot.bridgePort` | `6400` | WebSocket port the Unity bridge listens on |
| `unity-copilot.autoConnect` | `true` | Auto-connect when the extension activates |

## Requirements

- Unity 2022.3+ (any LTS)
- Node.js (for the MCP server)
- VS Code 1.90+ (optional, for the chat participant extension)

## License

MIT
