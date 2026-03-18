
<p align="center">
   <img src="images/banner.png" alt="Unity MCP Server Banner" />
</p>


# Unity Copilot for Unity Editor

> Control Unity Editor with natural language via VS Code and GitHub Copilot Chat.

## Usage

1. **Install the extension** in VS Code and open your Unity workspace.
2. **Run the bridge install command:**
   - Press `Cmd+Shift+P` (or `Ctrl+Shift+P` on Windows/Linux), type `Unity Copilot: Install Unity Copilot Bridge` and select it.
   - The extension will automatically copy the Unity bridge script to `Assets/Editor/UnityBridge/` in your project.
3. **Connect:**
   - Click the "Unity Copilot Bridge" status bar item or run the command `Unity Copilot: Connect`.
   - Make sure Unity Editor is running and listening on port `6400` (can be changed in settings).
4. **Chat with @unity** in Copilot Chat to control Unity:
   - Example: `Create a prefab Enemy with Rigidbody and BoxCollider`
   - Example: `Add Light component to GameObject MainCamera`

## Supported Commands

| Command            | Description                                                |
|--------------------|-----------------------------------------------------------|
| createPrefab       | Create a new prefab from a GameObject or 3D model         |
| createScene        | Create and save a new scene                               |
| createGameObject   | Add a primitive or empty GameObject to the scene          |
| addComponent       | Attach a component to an existing GameObject              |
| createScript       | Generate a C# script from a template and (optionally) attach it |
| setProperty        | Move, rotate, scale, rename, or toggle GameObject state   |
| openScene          | Open a scene by name or path                              |
| getSceneHierarchy  | List all root GameObjects in the current scene            |
| listAssets         | List assets in a folder, filter by type                   |

## Settings

| Setting                     | Default | Description                                 |
|-----------------------------|---------|---------------------------------------------|
| `unity-copilot.bridgePort`  | `6400`  | WebSocket port the Unity bridge listens on   |
| `unity-copilot.autoConnect` | `true`  | Auto-connect when the extension activates    |

## Requirements

- VS Code 1.90+
- GitHub Copilot extension
- Unity 2022.3+ (LTS)

## License
MIT

|       Action       |                         Description                           |
|--------------------|---------------------------------------------------------------|
| `createPrefab`     | Create a prefab from scratch or an existing 3D model          |
| `createScene`      | Create and save a new scene, optionally add to Build Settings |
| `createGameObject` | Add a primitive or empty GameObject to the active scene       |
| `addComponent`     | Attach a component to an existing GameObject                  |
| `createScript`     | Generate a C# script from a template and optionally attach it |
| `setProperty`      | Move, rotate, scale, rename, or toggle active state           |
| `openScene`        | Open a scene by name or path                                  |

## Settings

| Setting | Default | Description |
|---|---|---|
| `unity-copilot.bridgePort` | `6400` | WebSocket port the Unity bridge listens on |
| `unity-copilot.autoConnect` | `true` | Auto-connect when the extension activates |

## Architecture

```
VS Code (@unity chat)
  └─ vscode.lm API  ──── parse intent → JSON command
       └─ WebSocket (ws://127.0.0.1:6400)
            └─ UnityBridgeServer.cs (TcpListener, background thread)
                 └─ UnityCommandHandler.cs (main thread via EditorApplication.update)
                      └─ UnityEditor API  (PrefabUtility, EditorSceneManager, …)
```

## Requirements

- VS Code 1.90+
- GitHub Copilot extension (for chat participant & LM API)
- Unity 2022.3+ (any LTS)
