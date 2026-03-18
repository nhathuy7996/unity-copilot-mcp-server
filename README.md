# Unity Copilot for Unity Editor

> Điều khiển Unity Editor bằng ngôn ngữ tự nhiên qua VS Code và GitHub Copilot Chat.

## Cách sử dụng

1. **Cài đặt extension** trong VS Code và mở workspace Unity của bạn.
2. **Mở Unity Editor** và đảm bảo đã import `unity-bridge/UnityBridgeServer.cs` vào thư mục `Assets/Editor/UnityBridge/`.
3. **Kết nối:**
   - Nhấn vào status bar "Unity Copilot Bridge" hoặc dùng lệnh `Unity Copilot: Connect`.
   - Đảm bảo Unity Editor đang chạy và lắng nghe cổng `6400` (có thể đổi trong settings).
4. **Chat với @unity** trong Copilot Chat để ra lệnh cho Unity:
   - Ví dụ: `Tạo một prefab Enemy có Rigidbody và BoxCollider`
   - Ví dụ: `Thêm component Light vào GameObject MainCamera`

## Các lệnh hỗ trợ

| Lệnh              | Mô tả |
|-------------------|-------------------------------------------------------------|
| createPrefab      | Tạo prefab mới từ GameObject hoặc model 3D                  |
| createScene       | Tạo scene mới và lưu vào project                            |
| createGameObject  | Thêm GameObject (primitive hoặc empty) vào scene            |
| addComponent      | Gắn component vào GameObject hiện có                        |
| createScript      | Sinh script C# từ template và gắn vào GameObject (nếu muốn) |
| setProperty       | Đổi vị trí, xoay, scale, đổi tên, bật/tắt GameObject        |
| openScene         | Mở scene theo tên hoặc đường dẫn                            |
| getSceneHierarchy | Liệt kê các GameObject gốc trong scene hiện tại             |
| listAssets        | Liệt kê asset trong thư mục, lọc theo loại                  |

## Cài đặt

- `unity-copilot.bridgePort` (mặc định: 6400): Cổng WebSocket kết nối tới Unity
- `unity-copilot.autoConnect` (mặc định: true): Tự động kết nối khi mở extension

## Yêu cầu

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
