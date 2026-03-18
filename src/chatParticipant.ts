import * as vscode from 'vscode';
import type { UnityClient } from './unityClient.js';
import type { ActionName, ActionParams, BridgeResponse } from './commands.js';

// ── System prompt used to parse user intent into a JSON command ──

const SYSTEM_PROMPT = `You are a Unity Editor assistant. The user will describe what they want to do in Unity (in any language). 
Your job is to output ONLY a single JSON object that maps to one of these actions. Do not include markdown code fences, explanations, or extra text.

ACTIONS and their JSON shapes:

1. createPrefab
{ "action": "createPrefab", "params": { "name": string, "savePath"?: string, "components"?: string[], "modelPath"?: string, "tag"?: string, "layer"?: string } }

2. createScene
{ "action": "createScene", "params": { "name": string, "savePath"?: string, "addToBuildSettings"?: boolean } }

3. addComponent
{ "action": "addComponent", "params": { "gameObjectName": string, "componentType": string } }

4. createGameObject
{ "action": "createGameObject", "params": { "name": string, "parent"?: string, "primitiveType"?: "Cube"|"Sphere"|"Plane"|"Cylinder"|"Capsule"|"Quad"|"Empty", "position"?: {"x":number,"y":number,"z":number}, "rotation"?: {"x":number,"y":number,"z":number}, "scale"?: {"x":number,"y":number,"z":number}, "components"?: string[] } }

5. createScript
{ "action": "createScript", "params": { "scriptName": string, "template"?: "MonoBehaviour"|"ScriptableObject"|"Editor"|"Interface"|"Empty", "savePath"?: string, "attachTo"?: string } }

6. setProperty
{ "action": "setProperty", "params": { "gameObjectName": string, "position"?: {"x":number,"y":number,"z":number}, "rotation"?: {"x":number,"y":number,"z":number}, "scale"?: {"x":number,"y":number,"z":number}, "rename"?: string, "active"?: boolean } }

7. openScene
{ "action": "openScene", "params": { "scenePath": string, "additive"?: boolean } }

8. instantiatePrefab
{ "action": "instantiatePrefab", "params": { "prefabPath": string, "name"?: string, "parent"?: string, "position"?: {"x":number,"y":number,"z":number}, "rotation"?: {"x":number,"y":number,"z":number}, "scale"?: {"x":number,"y":number,"z":number} } }

9. getSceneHierarchy  — list all root GameObjects in the active scene
{ "action": "getSceneHierarchy", "params": {} }

10. listAssets  — list assets in a folder filtered by type
{ "action": "listAssets", "params": { "folder"?: string, "assetType"?: string } }
Common assetType values: "Prefab", "Scene", "Material", "AnimatorController", "AudioClip", "Texture2D", "Script"

11. deleteGameObject
{ "action": "deleteGameObject", "params": { "gameObjectName": string } }

12. setMaterial
{ "action": "setMaterial", "params": { "gameObjectName": string, "materialPath": string } }

13. setAnimatorController
{ "action": "setAnimatorController", "params": { "gameObjectName": string, "controllerPath": string } }

14. saveScene  — save the currently active scene
{ "action": "saveScene", "params": {} }

Rules:
- Use English component names as Unity recognizes them: "Rigidbody", "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider", "AudioSource", "Camera", "Light", "NavMeshAgent", "Animator", "CharacterController", etc.
- Default savePath for prefabs: "Prefabs"
- Default savePath for scenes: "Scenes"
- Default savePath for scripts: "Scripts"
- If the user asks for "a cube" without specifying components, use primitiveType "Cube".
- For createPrefab from a model, set modelPath if the user mentions an existing model file.
- Use instantiatePrefab (not createPrefab) when the user wants to place an existing prefab into a scene.
- Use getSceneHierarchy when the user asks what is in the scene, or to list objects.
- Use listAssets when the user wants to browse or discover asset files.
- Use saveScene whenever a save is requested or after a batch of changes.
- Output ONLY valid JSON. Nothing else.`;

// ── Help text shown by /help ──────────────────────────────────────

const HELP_TEXT = `## Unity Copilot — Available Commands

You can describe what you want in natural language. Examples:

**Create a Prefab**
\`\`\`
@unity create a prefab named Enemy with Rigidbody and BoxCollider
@unity tạo prefab Player với CharacterController và Animator
\`\`\`

**Instantiate a Prefab into the scene**
\`\`\`
@unity place the Player prefab at position 0 0 0
@unity instantiate Prefabs/Building_A.prefab at 10 0 5
\`\`\`

**Create a Scene**
\`\`\`
@unity create a new scene called Level2
@unity tạo scene MainMenu và thêm vào Build Settings
\`\`\`

**Create a GameObject**
\`\`\`
@unity add a Cube at position 0,1,0
@unity tạo một empty GameObject tên SpawnPoint trong Hierarchy
\`\`\`

**Add Component**
\`\`\`
@unity add AudioSource to the Enemy GameObject
@unity gán NavMeshAgent vào Player
\`\`\`

**Create a Script**
\`\`\`
@unity create a MonoBehaviour script named EnemyAI and attach it to Enemy
@unity tạo script GameManager dạng MonoBehaviour, lưu vào Scripts/Managers
\`\`\`

**Set Properties**
\`\`\`
@unity move Enemy to position 5,0,3
@unity scale Player to 1.5 1.5 1.5
@unity rename Cube to Platform
\`\`\`

**Open Scene**
\`\`\`
@unity open scene SampleScene
@unity mở scene Level1 additively
\`\`\`

**Set Material**
\`\`\`
@unity set material Materials/HeroMat.mat on Player
@unity gán vật liệu Metal cho Building_A
\`\`\`

**Set Animator Controller**
\`\`\`
@unity set animator controller Animations/HeroController.controller on Player
@unity gán AnimatorController cho nhân vật Player
\`\`\`

**Explore scene / assets**
\`\`\`
@unity list everything in the scene
@unity list all prefabs in 00GAME/Prefabs
@unity list all AnimatorControllers
\`\`\`

**Delete a GameObject**
\`\`\`
@unity delete the Cube from the scene
@unity xóa SpawnPoint khỏi scene
\`\`\`

**Save the scene**
\`\`\`
@unity save the scene
\`\`\`

**Built-in slash commands**
- \`/status\` — Check connection to Unity Editor
- \`/help\` — Show this help message

> **Setup**: Run \`Unity Copilot: Install Bridge into Unity Project\` from the Command Palette to install the C# bridge scripts into your Unity project.`;

// ── Chat participant handler ──────────────────────────────────────

export function registerChatParticipant(
  context: vscode.ExtensionContext,
  client: UnityClient,
): vscode.Disposable {
  const participant = vscode.chat.createChatParticipant(
    'unity-copilot.unity',
    async (
      request: vscode.ChatRequest,
      _chatContext: vscode.ChatContext,
      stream: vscode.ChatResponseStream,
      token: vscode.CancellationToken,
    ) => {
      // ── /status ───────────────────────────────────────────────────
      if (request.command === 'status') {
        const connected = client.isConnected;
        if (connected) {
          const alive = await client.ping();
          if (alive) {
            stream.markdown('**Unity Editor: Connected** ✅\n\nThe bridge is running and responding.');
          } else {
            stream.markdown('**Unity Editor: Timeout** ⚠️\n\nSocket connected but ping timed out. Unity may be busy.');
          }
        } else {
          stream.markdown(`**Unity Editor: Disconnected** ❌\n\nState: \`${client.state}\`\n\nRun **Unity Copilot: Connect to Unity Editor** from the Command Palette, or open Unity with the bridge installed (it starts listening automatically).`);
        }
        return;
      }

      // ── /help ─────────────────────────────────────────────────────
      if (request.command === 'help') {
        stream.markdown(HELP_TEXT);
        return;
      }

      // ── Guard: must be connected ───────────────────────────────────
      if (!client.isConnected) {
        stream.markdown(
          '> **Unity Editor is not connected.**\n>\n' +
          '> 1. Make sure Unity is open with the bridge installed.\n' +
          '> 2. Run **Unity Copilot: Connect to Unity Editor** from the Command Palette.\n' +
          '> 3. Retry your request.\n\n' +
          'Use `@unity /status` to check connection state.',
        );
        return;
      }

      // ── Parse user intent using LLM ───────────────────────────────
      stream.progress('Parsing your request...');

      let command: { action: ActionName; params: ActionParams } | null = null;

      try {
        const models = await vscode.lm.selectChatModels({
          vendor: 'copilot',
          family: 'gpt-4o',
        });

        if (models.length === 0) {
          stream.markdown('> No Copilot language model available. Make sure GitHub Copilot is enabled.');
          return;
        }

        // VS Code LM API has no dedicated systemPrompt option;
        // include the instruction as the first User turn.
        const messages = [
          vscode.LanguageModelChatMessage.User(SYSTEM_PROMPT),
          vscode.LanguageModelChatMessage.Assistant(
            'Understood. I will output only a single JSON object, no explanations.',
          ),
          vscode.LanguageModelChatMessage.User(request.prompt),
        ];

        const response = await models[0].sendRequest(
          messages,
          {},
          token,
        );

        let rawJson = '';
        for await (const chunk of response.text) {
          rawJson += chunk;
        }

        // Strip markdown fences if model added them despite instructions
        rawJson = rawJson.trim().replace(/^```(?:json)?\s*/i, '').replace(/\s*```$/, '').trim();
        command = JSON.parse(rawJson) as { action: ActionName; params: ActionParams };
      } catch (err) {
        if (err instanceof vscode.LanguageModelError) {
          stream.markdown(`> LLM error: ${err.message}`);
        } else {
          stream.markdown(`> Failed to parse your request: ${String(err)}\n\nPlease try rephrasing.`);
        }
        return;
      }

      if (!command || !command.action) {
        stream.markdown('> Could not understand the request. Please try rephrasing.');
        return;
      }

      // ── Show a friendly preview of what will be executed ─────────
      stream.markdown(`**Executing:** \`${command.action}\`\n\`\`\`json\n${JSON.stringify(command.params, null, 2)}\n\`\`\`\n`);
      stream.progress('Sending to Unity Editor...');

      // ── Send command to Unity ──────────────────────────────────────
      let result: BridgeResponse;
      try {
        result = await client.sendCommand(command.action, command.params);
      } catch (err) {
        stream.markdown(`> **Error communicating with Unity:** ${String(err)}`);
        return;
      }

      // ── Display result ─────────────────────────────────────────────
      if (result.success) {
        stream.markdown(`**Success** ✅\n\n${result.message}`);
        if (result.data && Object.keys(result.data).length > 0) {
          stream.markdown(`\n\`\`\`json\n${JSON.stringify(result.data, null, 2)}\n\`\`\``);
        }
      } else {
        stream.markdown(`**Failed** ❌\n\n${result.message}`);
      }
    },
  );

  participant.iconPath = new vscode.ThemeIcon('symbol-namespace');
  participant.followupProvider = {
    provideFollowups(
      result: vscode.ChatResult,
      _context: vscode.ChatContext,
      _token: vscode.CancellationToken,
    ): vscode.ChatFollowup[] {
      return [
        { prompt: '/status', label: 'Check connection status', command: 'status' },
        { prompt: '/help', label: 'Show help & examples', command: 'help' },
      ];
    },
  };

  context.subscriptions.push(participant);
  return participant;
}
