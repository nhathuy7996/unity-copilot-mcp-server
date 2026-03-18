# Unity Copilot Bridge

A VS Code extension that connects GitHub Copilot Chat to Unity Editor, enabling you to create prefabs, scenes, GameObjects, scripts, and components using natural language.

# unity-copilot

A VS Code extension and Unity bridge for integrating AI-powered workflows with Unity projects.

## Features
- Chat with AI to assist Unity development
- Send commands from VS Code to Unity
- Unity bridge server in C# for communication

## Project Structure
- `src/` — TypeScript source for the VS Code extension
- `unity-bridge/` — C# server for Unity integration
- `images/` — Extension and documentation images
- `build-rpg.js`, `esbuild.js` — Build scripts

## Getting Started
1. **Install dependencies:**
     ```sh
     npm install
     ```
2. **Build the extension:**
     ```sh
     npm run build
     ```
3. **Start the Unity bridge server:**
     - Open `unity-bridge/UnityBridgeServer.cs` in Unity or build/run as a standalone server.

## Development
- Use `npm run watch` for live TypeScript compilation.
- Edit C# files in `unity-bridge/` for Unity-side logic.

## Contributing
Pull requests and issues are welcome!

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

## Publishing

1. Create a publisher at [marketplace.visualstudio.com](https://marketplace.visualstudio.com)
2. Update `"publisher"` in `package.json`
3. Run `npm run package` → produces a `.vsix` file
4. Upload to the marketplace or share the `.vsix` directly

## Requirements

- VS Code 1.90+
- GitHub Copilot extension (for chat participant & LM API)
- Unity 2022.3+ (any LTS)
