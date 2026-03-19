
<p align="center">
   <img src="images/banner.png" alt="Unity MCP Server Banner" />
</p>

# Unity Copilot MCP Server

> Control Unity Editor from AI assistants (GitHub Copilot, Claude, etc.) via the Model Context Protocol (MCP).

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

Copy the two C# files from `unity-bridge/` into your Unity project:

```
Assets/Editor/UnityBridge/UnityBridgeServer.cs
Assets/Editor/UnityBridge/UnityCommandHandler.cs
```

Or use the VS Code command: `Unity Copilot: Install Bridge into Unity Project`.

The bridge server starts automatically when Unity enters Editor mode and listens on `ws://127.0.0.1:6400`.

### 2. Configure MCP Server

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

| Tool | Description |
|---|---|
| `unity_ping` | Check if Unity Editor bridge is alive |
| `unity_refreshAssets` | Force Unity to re-import assets and recompile scripts |
| `unity_createScene` | Create a new scene, optionally add to Build Settings |
| `unity_openScene` | Open a scene by name or path |
| `unity_saveScene` | Save the active scene to disk |
| `unity_createGameObject` | Create a primitive or empty GameObject in the scene |
| `unity_createPrefab` | Create a prefab asset from scratch or from a 3D model |
| `unity_instantiatePrefab` | Place an existing prefab into the scene |
| `unity_addComponent` | Add any component (built-in or custom script) to a GameObject |
| `unity_setProperty` | Move, rotate, scale, rename, or toggle active state |
| `unity_setMaterial` | Assign a material to a GameObject's Renderer |
| `unity_setAnimatorController` | Assign an AnimatorController to a GameObject |
| `unity_deleteGameObject` | Delete a GameObject from the scene (supports Undo) |
| `unity_getSceneHierarchy` | List all root GameObjects in the active scene |
| `unity_listAssets` | Search project assets by type and folder |
| `unity_findGameObjects` | Search GameObjects by name, with optional component filter |

### Workflow: Adding a Custom Script

Since the AI can write files directly, the workflow for adding a custom script is:

1. AI creates the `.cs` file in your Unity project's `Assets/` folder
2. AI calls `unity_refreshAssets` to force Unity to compile
3. AI calls `unity_addComponent` to attach the compiled script to a GameObject

## VS Code Extension (Chat Participant)

This project also includes a VS Code extension with a `@unity` chat participant for GitHub Copilot Chat.

**Commands:**
- `@unity /status` — Check connection to Unity Editor
- `@unity /help` — Show available commands and examples

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
