// UnityCommandHandler.cs
// Handles all JSON commands received from the VS Code extension.
// Called on the Unity main thread, so all Editor API calls are safe here.
//
// Install location: Assets/Editor/UnityBridge/UnityCommandHandler.cs

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCopilot
{
    public static class UnityCommandHandler
    {
        // ── Entry point ────────────────────────────────────────────────
        public static string Handle(string json)
        {
            string id = "";
            try
            {
                var req = SimpleJson.Parse(json);
                id = req.GetString("id");
                string action = req.GetString("action");
                var p = req.GetObject("params");

                string message;
                object data = null;

                switch (action)
                {
                    case "ping":
                        message = "pong";
                        break;
                    case "createPrefab":
                        message = CreatePrefab(p, out data);
                        break;
                    case "createScene":
                        message = CreateScene(p, out data);
                        break;
                    case "addComponent":
                        message = AddComponent(p);
                        break;
                    case "createGameObject":
                        message = CreateGameObject(p, out data);
                        break;
                    case "createScript":
                        message = CreateScript(p, out data);
                        break;
                    case "setProperty":
                        message = SetProperty(p);
                        break;
                    case "openScene":
                        message = OpenScene(p);
                        break;
                    case "instantiatePrefab":
                        message = InstantiatePrefab(p, out data);
                        break;
                    case "getSceneHierarchy":
                        message = GetSceneHierarchy(out data);
                        break;
                    case "listAssets":
                        message = ListAssets(p, out data);
                        break;
                    case "deleteGameObject":
                        message = DeleteGameObject(p);
                        break;
                    case "setMaterial":
                        message = SetMaterial(p);
                        break;
                    case "setAnimatorController":
                        message = SetAnimatorController(p);
                        break;
                    case "saveScene":
                        message = SaveScene();
                        break;
                    default:
                        return BuildResponse(id, false, $"Unknown action: {action}", null);
                }

                return BuildResponse(id, true, message, data);
            }
            catch (Exception ex)
            {
                return BuildResponse(id, false, ex.Message, null);
            }
        }

        // ── 1. createPrefab ────────────────────────────────────────────
        private static string CreatePrefab(SimpleJson p, out object data)
        {
            string name = p.GetString("name");
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("'name' is required for createPrefab"); }

            string savePath = p.GetString("savePath") ?? "Prefabs";
            string modelPath = p.GetString("modelPath");

            EnsureAssetsFolder(savePath);
            string prefabPath = $"Assets/{savePath}/{name}.prefab";

            GameObject root;
            bool fromModel = !string.IsNullOrEmpty(modelPath);

            if (fromModel)
            {
                string fullModelPath = modelPath.StartsWith("Assets/") ? modelPath : $"Assets/{modelPath}";
                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fullModelPath);
                if (modelAsset == null)
                {
                    throw new FileNotFoundException($"Model not found at '{fullModelPath}'. Check the path relative to Assets/.");
                }
                root = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                root.name = name;
            }
            else
            {
                root = new GameObject(name);
            }

            // Apply tag/layer
            string tag = p.GetString("tag");
            string layer = p.GetString("layer");
            if (!string.IsNullOrEmpty(tag)) { try { root.tag = tag; } catch { /* tag not defined */ } }
            if (!string.IsNullOrEmpty(layer))
            {
                int layerIndex = LayerMask.NameToLayer(layer);
                if (layerIndex >= 0) { root.layer = layerIndex; }
            }

            // Add components
            var components = p.GetStringArray("components");
            foreach (string compName in components)
            {
                Type compType = ResolveComponentType(compName);
                if (compType != null)
                {
                    if (root.GetComponent(compType) == null) { root.AddComponent(compType); }
                }
                else
                {
                    Debug.LogWarning($"[UnityCopilot] Unknown component type: '{compName}'");
                }
            }

            // Save as prefab
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            AssetDatabase.Refresh();
            data = new Dictionary<string, object> { { "prefabPath", prefabPath } };
            return $"Prefab '{name}' created at {prefabPath}" + (fromModel ? $" (from model {modelPath})" : "") + ".";
        }

        // ── 2. createScene ─────────────────────────────────────────────
        private static string CreateScene(SimpleJson p, out object data)
        {
            string name = p.GetString("name");
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("'name' is required for createScene"); }

            string savePath = p.GetString("savePath") ?? "Scenes";
            bool addToBuild = p.GetBool("addToBuildSettings");

            EnsureAssetsFolder(savePath);
            string scenePath = $"Assets/{savePath}/{name}.unity";

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            scene.name = name;
            EditorSceneManager.SaveScene(scene, scenePath);

            if (addToBuild)
            {
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (!scenes.Exists(s => s.path == scenePath))
                {
                    scenes.Add(new EditorBuildSettingsScene(scenePath, true));
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
            }

            AssetDatabase.Refresh();
            data = new Dictionary<string, object> { { "scenePath", scenePath } };
            return $"Scene '{name}' created at {scenePath}" + (addToBuild ? " and added to Build Settings." : ".");
        }

        // ── 3. addComponent ────────────────────────────────────────────
        private static string AddComponent(SimpleJson p)
        {
            string goName = p.GetString("gameObjectName");
            string comp = p.GetString("componentType");
            if (string.IsNullOrEmpty(goName)) { throw new ArgumentException("'gameObjectName' is required"); }
            if (string.IsNullOrEmpty(comp)) { throw new ArgumentException("'componentType' is required"); }

            GameObject go = FindGameObjectInScene(goName);
            if (go == null) { throw new InvalidOperationException($"GameObject '{goName}' not found in the active scene."); }

            Type compType = ResolveComponentType(comp);
            if (compType == null) { throw new InvalidOperationException($"Component type '{comp}' not recognised by Unity."); }

            if (go.GetComponent(compType) != null)
            {
                return $"'{go.name}' already has component '{comp}' — nothing added.";
            }

            Undo.AddComponent(go, compType);
            EditorUtility.SetDirty(go);
            return $"Added '{comp}' to '{go.name}'.";
        }

        // ── 4. createGameObject ────────────────────────────────────────
        private static string CreateGameObject(SimpleJson p, out object data)
        {
            string name = p.GetString("name");
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("'name' is required for createGameObject"); }

            string primitiveType = p.GetString("primitiveType") ?? "Empty";
            string parentName = p.GetString("parent");

            GameObject go;
            if (primitiveType != "Empty" && Enum.TryParse(primitiveType, out PrimitiveType prim))
            {
                go = GameObject.CreatePrimitive(prim);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            // Parent
            if (!string.IsNullOrEmpty(parentName))
            {
                GameObject parent = FindGameObjectInScene(parentName);
                if (parent != null) { go.transform.SetParent(parent.transform, false); }
            }

            // Transform
            go.transform.localPosition = ParseVector3(p, "position", Vector3.zero);
            go.transform.localEulerAngles = ParseVector3(p, "rotation", Vector3.zero);
            go.transform.localScale = ParseVector3(p, "scale", Vector3.one);

            // Extra components
            var components = p.GetStringArray("components");
            foreach (string compName in components)
            {
                Type compType = ResolveComponentType(compName);
                if (compType != null && go.GetComponent(compType) == null) { go.AddComponent(compType); }
            }

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            EditorUtility.SetDirty(go);

            data = new Dictionary<string, object> { { "gameObjectName", go.name } };
            return $"GameObject '{name}' created in the active scene.";
        }

        // ── 5. createScript ────────────────────────────────────────────
        private static string CreateScript(SimpleJson p, out object data)
        {
            string scriptName = p.GetString("scriptName");
            if (string.IsNullOrEmpty(scriptName)) { throw new ArgumentException("'scriptName' is required"); }

            string savePath = p.GetString("savePath") ?? "Scripts";
            string template = p.GetString("template") ?? "MonoBehaviour";
            string attachTo = p.GetString("attachTo");

            EnsureAssetsFolder(savePath);
            string scriptPath = $"Assets/{savePath}/{scriptName}.cs";
            string fullPath = Path.Combine(Application.dataPath, $"../{scriptPath}");
            // Normalise path
            fullPath = Path.GetFullPath(fullPath);

            if (File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Script already exists at '{scriptPath}'. Delete it first or choose a different name.");
            }

            string content = BuildScriptTemplate(scriptName, template);
            File.WriteAllText(fullPath, content, Encoding.UTF8);
            AssetDatabase.Refresh();

            data = new Dictionary<string, object> { { "scriptPath", scriptPath } };
            string msg = $"Script '{scriptName}.cs' created at {scriptPath}.";

            // Attach to GameObject (deferred by one import cycle via AssetDatabase)
            if (!string.IsNullOrEmpty(attachTo))
            {
                // Import must complete first — we register a deferred callback
                EditorApplication.delayCall += () =>
                {
                    var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                    if (monoScript == null) { Debug.LogWarning($"[UnityCopilot] Could not load script asset at {scriptPath}"); return; }
                    Type scriptType = monoScript.GetClass();
                    if (scriptType == null) { Debug.LogWarning($"[UnityCopilot] Script class not yet compiled. Attach manually."); return; }

                    GameObject go = FindGameObjectInScene(attachTo);
                    if (go == null) { Debug.LogWarning($"[UnityCopilot] GameObject '{attachTo}' not found for script attachment."); return; }

                    Undo.AddComponent(go, scriptType);
                    EditorUtility.SetDirty(go);
                    Debug.Log($"[UnityCopilot] Attached '{scriptName}' to '{attachTo}'.");
                };
                msg += $" Will attach to '{attachTo}' after compilation.";
            }

            return msg;
        }

        // ── 6. setProperty ─────────────────────────────────────────────
        private static string SetProperty(SimpleJson p)
        {
            string goName = p.GetString("gameObjectName");
            if (string.IsNullOrEmpty(goName)) { throw new ArgumentException("'gameObjectName' is required"); }

            GameObject go = FindGameObjectInScene(goName);
            if (go == null) { throw new InvalidOperationException($"GameObject '{goName}' not found in the active scene."); }

            Undo.RecordObject(go.transform, "Unity Copilot: Set Property");
            var changed = new List<string>();

            if (p.HasKey("position"))
            {
                go.transform.localPosition = ParseVector3(p, "position", go.transform.localPosition);
                changed.Add("position");
            }
            if (p.HasKey("rotation"))
            {
                go.transform.localEulerAngles = ParseVector3(p, "rotation", go.transform.localEulerAngles);
                changed.Add("rotation");
            }
            if (p.HasKey("scale"))
            {
                go.transform.localScale = ParseVector3(p, "scale", go.transform.localScale);
                changed.Add("scale");
            }
            if (p.HasKey("rename"))
            {
                Undo.RecordObject(go, "Unity Copilot: Rename");
                go.name = p.GetString("rename");
                changed.Add("name");
            }
            if (p.HasKey("active"))
            {
                Undo.RecordObject(go, "Unity Copilot: SetActive");
                go.SetActive(p.GetBool("active"));
                changed.Add("active");
            }

            EditorUtility.SetDirty(go);
            return $"Updated '{goName}': {string.Join(", ", changed)}.";
        }

        // ── 7. openScene ───────────────────────────────────────────────
        private static string OpenScene(SimpleJson p)
        {
            string scenePath = p.GetString("scenePath");
            bool additive = p.GetBool("additive");

            if (string.IsNullOrEmpty(scenePath)) { throw new ArgumentException("'scenePath' is required"); }

            // Support bare scene name lookup
            string resolvedPath = ResolveScenePath(scenePath);
            if (resolvedPath == null)
            {
                throw new FileNotFoundException($"Scene '{scenePath}' not found in Assets/. Use the relative path from Assets/ or just the scene name.");
            }

            var mode = additive ? OpenSceneMode.Additive : OpenSceneMode.Single;
            EditorSceneManager.OpenScene(resolvedPath, mode);
            return $"Opened scene '{resolvedPath}'" + (additive ? " additively." : ".");
        }

        // ── 8. instantiatePrefab ───────────────────────────────────────
        // Instantiates a saved prefab into the active scene.
        private static string InstantiatePrefab(SimpleJson p, out object data)
        {
            string prefabPath = p.GetString("prefabPath");
            if (string.IsNullOrEmpty(prefabPath)) { throw new ArgumentException("'prefabPath' is required"); }

            string fullPath = prefabPath.StartsWith("Assets/") ? prefabPath : $"Assets/{prefabPath}";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (asset == null) { throw new FileNotFoundException($"Prefab not found at '{fullPath}'."); }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = p.GetString("name") ?? asset.name;

            string parentName = p.GetString("parent");
            if (!string.IsNullOrEmpty(parentName))
            {
                GameObject parent = FindGameObjectInScene(parentName);
                if (parent != null) { instance.transform.SetParent(parent.transform, false); }
            }

            instance.transform.localPosition = ParseVector3(p, "position", Vector3.zero);
            instance.transform.localEulerAngles = ParseVector3(p, "rotation", Vector3.zero);
            instance.transform.localScale = ParseVector3(p, "scale", Vector3.one);

            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {instance.name}");
            EditorUtility.SetDirty(instance);

            data = new Dictionary<string, object> { { "gameObjectName", instance.name } };
            return $"Instantiated prefab '{asset.name}' as '{instance.name}' in scene.";
        }

        // ── 9. getSceneHierarchy ───────────────────────────────────────
        // Returns a flat list of all root GameObjects with their child counts.
        private static string GetSceneHierarchy(out object data)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var list = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (var go in roots)
            {
                if (!first) { list.Append(","); }
                first = false;
                string escaped = go.name.Replace("\\", "\\\\").Replace("\"", "\\\"");
                list.Append($"{{\"name\":\"{escaped}\",\"active\":{go.activeSelf.ToString().ToLower()},\"children\":{go.transform.childCount}}}");
            }
            list.Append("]");
            data = null;
            return list.ToString();
        }

        // ── 10. listAssets ─────────────────────────────────────────────
        // Lists assets of a given type under a folder (relative to Assets/).
        private static string ListAssets(SimpleJson p, out object data)
        {
            string folder = p.GetString("folder") ?? "";
            string type   = p.GetString("assetType") ?? "GameObject";

            string searchFolder = string.IsNullOrEmpty(folder) ? "Assets" : $"Assets/{folder}";
            string[] guids = AssetDatabase.FindAssets($"t:{type}", new[] { searchFolder });

            var sb = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!first) { sb.Append(","); }
                first = false;
                string escaped = assetPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.Append($"\"{escaped}\"");
            }
            sb.Append("]");

            data = null;
            return sb.ToString();
        }

        // ── 11. deleteGameObject ───────────────────────────────────────
        private static string DeleteGameObject(SimpleJson p)
        {
            string goName = p.GetString("gameObjectName");
            if (string.IsNullOrEmpty(goName)) { throw new ArgumentException("'gameObjectName' is required"); }

            GameObject go = FindGameObjectInScene(goName);
            if (go == null) { throw new InvalidOperationException($"GameObject '{goName}' not found in scene."); }

            Undo.DestroyObjectImmediate(go);
            return $"Deleted GameObject '{goName}' from scene.";
        }

        // ── 12. setMaterial ────────────────────────────────────────────
        // Assigns a material asset to the first Renderer on a GameObject.
        private static string SetMaterial(SimpleJson p)
        {
            string goName   = p.GetString("gameObjectName");
            string matPath  = p.GetString("materialPath");
            if (string.IsNullOrEmpty(goName))  { throw new ArgumentException("'gameObjectName' is required"); }
            if (string.IsNullOrEmpty(matPath)) { throw new ArgumentException("'materialPath' is required"); }

            GameObject go = FindGameObjectInScene(goName);
            if (go == null) { throw new InvalidOperationException($"GameObject '{goName}' not found."); }

            string fullPath = matPath.StartsWith("Assets/") ? matPath : $"Assets/{matPath}";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(fullPath);
            if (mat == null) { throw new FileNotFoundException($"Material not found at '{fullPath}'."); }

            Renderer rend = go.GetComponentInChildren<Renderer>();
            if (rend == null) { throw new InvalidOperationException($"No Renderer found on '{goName}' or its children."); }

            Undo.RecordObject(rend, "UnityCopilot: Set Material");
            rend.sharedMaterial = mat;
            EditorUtility.SetDirty(rend);
            return $"Set material '{mat.name}' on '{goName}'.";
        }

        // ── 13. setAnimatorController ──────────────────────────────────
        // Assigns an AnimatorController asset to the Animator on a GameObject.
        private static string SetAnimatorController(SimpleJson p)
        {
            string goName         = p.GetString("gameObjectName");
            string controllerPath = p.GetString("controllerPath");
            if (string.IsNullOrEmpty(goName))         { throw new ArgumentException("'gameObjectName' is required"); }
            if (string.IsNullOrEmpty(controllerPath)) { throw new ArgumentException("'controllerPath' is required"); }

            GameObject go = FindGameObjectInScene(goName);
            if (go == null) { throw new InvalidOperationException($"GameObject '{goName}' not found."); }

            string fullPath = controllerPath.StartsWith("Assets/") ? controllerPath : $"Assets/{controllerPath}";
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(fullPath);
            if (ctrl == null) { throw new FileNotFoundException($"AnimatorController not found at '{fullPath}'."); }

            Animator anim = go.GetComponentInChildren<Animator>();
            if (anim == null)
            {
                anim = Undo.AddComponent<Animator>(go);
            }

            Undo.RecordObject(anim, "UnityCopilot: Set Animator Controller");
            anim.runtimeAnimatorController = ctrl;
            EditorUtility.SetDirty(anim);
            return $"Set AnimatorController '{ctrl.name}' on '{goName}'.";
        }

        // ── 14. saveScene ──────────────────────────────────────────────
        private static string SaveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            bool saved = EditorSceneManager.SaveScene(scene);
            return saved ? $"Scene '{scene.name}' saved." : $"Failed to save scene '{scene.name}'. Has the scene been saved to disk before?";
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static GameObject FindGameObjectInScene(string name)
        {
            // Search all root objects (supports nested search by exact name)
            return GameObject.Find(name);
        }

        private static string ResolveScenePath(string input)
        {
            // Already a full relative path?
            string candidate = input.StartsWith("Assets/") ? input : $"Assets/{input}";
            if (!candidate.EndsWith(".unity")) { candidate += ".unity"; }

            if (File.Exists(candidate)) { return candidate; }

            // Search by name in AssetDatabase
            string[] guids = AssetDatabase.FindAssets($"t:Scene {Path.GetFileNameWithoutExtension(input)}");
            if (guids.Length > 0) { return AssetDatabase.GUIDToAssetPath(guids[0]); }

            return null;
        }

        private static void EnsureAssetsFolder(string relativePath)
        {
            string fullPath = Path.Combine(Application.dataPath, relativePath);
            if (!Directory.Exists(fullPath)) { Directory.CreateDirectory(fullPath); }
        }

        private static Vector3 ParseVector3(SimpleJson p, string key, Vector3 fallback)
        {
            var obj = p.GetObject(key);
            if (obj == null) { return fallback; }
            return new Vector3(obj.GetFloat("x"), obj.GetFloat("y"), obj.GetFloat("z"));
        }

        // Resolves common shorthand component names to Unity types
        private static Type ResolveComponentType(string name)
        {
            // Try UnityEngine namespace first
            Type t = Type.GetType($"UnityEngine.{name}, UnityEngine");
            if (t != null) { return t; }

            t = Type.GetType($"UnityEngine.{name}, UnityEngine.PhysicsModule");
            if (t != null) { return t; }

            t = Type.GetType($"UnityEngine.AI.{name}, UnityEngine.AIModule");
            if (t != null) { return t; }

            // Try unqualified (in case it's in loaded assemblies)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name, false, true);
                if (t != null && typeof(Component).IsAssignableFrom(t)) { return t; }

                t = asm.GetType($"UnityEngine.{name}", false, true);
                if (t != null && typeof(Component).IsAssignableFrom(t)) { return t; }
            }

            return null;
        }

        private static string BuildScriptTemplate(string className, string template)
        {
            switch (template)
            {
                case "ScriptableObject":
                    return
$@"using UnityEngine;

[CreateAssetMenu(fileName = ""{className}"", menuName = ""{className}"")]
public class {className} : ScriptableObject
{{
    // TODO: Add your data fields here
}}
";
                case "Editor":
                    return
$@"using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MonoBehaviour))]
public class {className} : Editor
{{
    public override void OnInspectorGUI()
    {{
        base.OnInspectorGUI();
    }}
}}
";
                case "Interface":
                    return
$@"public interface {className}
{{
    // TODO: Add interface members
}}
";
                case "Empty":
                    return $"// {className}.cs\n";

                default: // MonoBehaviour
                    return
$@"using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class {className} : MonoBehaviour
{{
    void Start()
    {{
    }}

    void Update()
    {{
    }}
}}
";
            }
        }

        // ── JSON response builder ──────────────────────────────────────
        private static string BuildResponse(string id, bool success, string message, object data)
        {
            string escapedMsg = message?.Replace("\\", "\\\\").Replace("\"", "\\\"")
                                        .Replace("\n", "\\n").Replace("\r", "") ?? "";
            string dataJson = "null";
            if (data is Dictionary<string, object> dict)
            {
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) { sb.Append(","); }
                    first = false;
                    string val = kv.Value is string s
                        ? $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
                        : kv.Value.ToString().ToLower();
                    sb.Append($"\"{kv.Key}\":{val}");
                }
                sb.Append("}");
                dataJson = sb.ToString();
            }
            return $"{{\"id\":\"{id}\",\"success\":{success.ToString().ToLower()},\"message\":\"{escapedMsg}\",\"data\":{dataJson}}}";
        }
    }

    // ── Minimal JSON parser (no external dependencies) ────────────────
    /// <summary>
    /// A minimal JSON parser sufficient for the bridge protocol.
    /// Supports flat and nested objects, string arrays, no JSON arrays of objects.
    /// </summary>
    internal class SimpleJson
    {
        private readonly Dictionary<string, object> _data;

        private SimpleJson(Dictionary<string, object> data) { _data = data; }

        public static SimpleJson Parse(string json)
        {
            int pos = 0;
            SkipWs(json, ref pos);
            if (pos >= json.Length || json[pos] != '{') { throw new FormatException("Expected JSON object"); }
            var obj = ParseObject(json, ref pos);
            return new SimpleJson(obj);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int pos)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            pos++; // skip '{'
            SkipWs(json, ref pos);
            while (pos < json.Length && json[pos] != '}')
            {
                SkipWs(json, ref pos);
                string key = ParseString(json, ref pos);
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ':') { pos++; }
                SkipWs(json, ref pos);
                object value = ParseValue(json, ref pos);
                dict[key] = value;
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ',') { pos++; }
                SkipWs(json, ref pos);
            }
            if (pos < json.Length) { pos++; } // skip '}'
            return dict;
        }

        private static object ParseValue(string json, ref int pos)
        {
            if (pos >= json.Length) { return null; }
            char c = json[pos];
            if (c == '"') { return ParseString(json, ref pos); }
            if (c == '{')
            {
                var nested = ParseObject(json, ref pos);
                return new SimpleJson(nested);
            }
            if (c == '[') { return ParseArray(json, ref pos); }
            if (c == 't') { pos += 4; return true; }
            if (c == 'f') { pos += 5; return false; }
            if (c == 'n') { pos += 4; return null; }
            // Number
            int start = pos;
            while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '-' || json[pos] == '.' || json[pos] == 'e' || json[pos] == 'E' || json[pos] == '+')) { pos++; }
            string numStr = json.Substring(start, pos - start);
            if (float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) { return f; }
            return 0f;
        }

        private static List<object> ParseArray(string json, ref int pos)
        {
            var list = new List<object>();
            pos++; // skip '['
            SkipWs(json, ref pos);
            while (pos < json.Length && json[pos] != ']')
            {
                list.Add(ParseValue(json, ref pos));
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == ',') { pos++; }
                SkipWs(json, ref pos);
            }
            if (pos < json.Length) { pos++; } // skip ']'
            return list;
        }

        private static string ParseString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') { return ""; }
            pos++; // skip opening "
            var sb = new StringBuilder();
            while (pos < json.Length && json[pos] != '"')
            {
                if (json[pos] == '\\' && pos + 1 < json.Length)
                {
                    pos++;
                    switch (json[pos])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(json[pos]); break;
                    }
                }
                else { sb.Append(json[pos]); }
                pos++;
            }
            if (pos < json.Length) { pos++; } // skip closing "
            return sb.ToString();
        }

        private static void SkipWs(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) { pos++; }
        }

        // ── Accessors ─────────────────────────────────────────────────
        public bool HasKey(string key) => _data.ContainsKey(key) && _data[key] != null;

        public string GetString(string key)
        {
            if (_data.TryGetValue(key, out var val) && val is string s) { return s; }
            return null;
        }

        public SimpleJson GetObject(string key)
        {
            if (_data.TryGetValue(key, out var val) && val is SimpleJson j) { return j; }
            return null;
        }

        public float GetFloat(string key)
        {
            if (_data.TryGetValue(key, out var val) && val is float f) { return f; }
            if (_data.TryGetValue(key, out val) && val != null && float.TryParse(val.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result)) { return result; }
            return 0f;
        }

        public bool GetBool(string key)
        {
            if (_data.TryGetValue(key, out var val) && val is bool b) { return b; }
            return false;
        }

        public List<string> GetStringArray(string key)
        {
            var result = new List<string>();
            if (!_data.TryGetValue(key, out var val) || !(val is List<object> list)) { return result; }
            foreach (var item in list)
            {
                if (item is string s) { result.Add(s); }
            }
            return result;
        }
    }
}
#endif
