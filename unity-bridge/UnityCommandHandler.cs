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
                    case "refreshAssets":
                        message = RefreshAssets();
                        break;
                    case "findGameObjects":
                        message = FindGameObjects(p, out data);
                        break;
                    case "runMenuItem":
                        message = RunMenuItem(p);
                        break;
                    case "getComponentProperties":
                        message = GetComponentProperties(p, out data);
                        break;
                    case "setComponentProperty":
                        message = SetComponentProperty(p);
                        break;
                    case "captureScreenshot":
                        message = CaptureScreenshot(p, out data);
                        break;
                    case "undoRedo":
                        message = UndoRedo(p);
                        break;
                    case "getUndoHistory":
                        message = GetUndoHistory(out data);
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

        // ── 5. setProperty ──────────────────────────────────────────
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

        // ── 15. refreshAssets ──────────────────────────────────────────
        private static string RefreshAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return "AssetDatabase refreshed. Unity will compile any new or changed scripts.";
        }

        // ── 16. findGameObjects ─────────────────────────────────────────
        // Searches for GameObjects in the active scene by name (partial match).
        // Optionally filters by component type.
        private static string FindGameObjects(SimpleJson p, out object data)
        {
            string query = p.GetString("query");
            if (string.IsNullOrEmpty(query)) { throw new ArgumentException("'query' is required"); }

            string hasComponent = p.GetString("hasComponent");
            bool includeChildren = !p.HasKey("includeChildren") || p.GetBool("includeChildren");

            Type filterType = null;
            if (!string.IsNullOrEmpty(hasComponent))
            {
                filterType = ResolveComponentType(hasComponent);
                if (filterType == null) { throw new InvalidOperationException($"Component type '{hasComponent}' not recognised."); }
            }

            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var results = new List<GameObject>();

            foreach (var root in roots)
            {
                if (includeChildren)
                {
                    var allTransforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (var t in allTransforms)
                    {
                        if (t.gameObject.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            results.Add(t.gameObject);
                        }
                    }
                }
                else
                {
                    if (root.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(root);
                    }
                }
            }

            // Filter by component if requested
            if (filterType != null)
            {
                results.RemoveAll(go => go.GetComponent(filterType) == null);
            }

            // Build JSON result
            var sb = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (var go in results)
            {
                if (!first) { sb.Append(","); }
                first = false;

                string escapedName = go.name.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string path = GetHierarchyPath(go.transform);
                string escapedPath = path.Replace("\\", "\\\\").Replace("\"", "\\\"");

                // Collect component names
                var comps = go.GetComponents<Component>();
                var compNames = new System.Text.StringBuilder("[");
                bool cFirst = true;
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    if (!cFirst) { compNames.Append(","); }
                    cFirst = false;
                    compNames.Append($"\"{c.GetType().Name}\"");
                }
                compNames.Append("]");

                sb.Append($"{{\"name\":\"{escapedName}\",\"path\":\"{escapedPath}\",\"active\":{go.activeSelf.ToString().ToLower()},\"children\":{go.transform.childCount},\"components\":{compNames}}}");
            }
            sb.Append("]");

            data = null;
            return sb.ToString();
        }

        private static string GetHierarchyPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Insert(0, t.gameObject.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        // ── 14. saveScene ──────────────────────────────────────────────
        private static string SaveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            bool saved = EditorSceneManager.SaveScene(scene);
            return saved ? $"Scene '{scene.name}' saved." : $"Failed to save scene '{scene.name}'. Has the scene been saved to disk before?";
        }

        // ── 17. runMenuItem ────────────────────────────────────────────
        // Executes any Unity Editor menu item by its full menu path.
        private static string RunMenuItem(SimpleJson p)
        {
            string menuPath = p.GetString("menuPath");
            if (string.IsNullOrEmpty(menuPath)) { throw new ArgumentException("'menuPath' is required"); }

            bool ok = EditorApplication.ExecuteMenuItem(menuPath);
            if (!ok) { throw new InvalidOperationException($"Menu item '{menuPath}' not found or not currently executable (may be greyed out)."); }
            return $"Executed menu item '{menuPath}'.";
        }

        // ── 18. getComponentProperties ─────────────────────────────────
        // Returns all serialized properties of a component as a JSON object.
        private static string GetComponentProperties(SimpleJson p, out object data)
        {
            string goName   = p.GetString("gameObjectName");
            string compName = p.GetString("componentType");
            if (string.IsNullOrEmpty(goName))   { throw new ArgumentException("'gameObjectName' is required"); }
            if (string.IsNullOrEmpty(compName)) { throw new ArgumentException("'componentType' is required"); }

            GameObject go = FindGameObjectInScene(goName);
            if (go == null) { throw new InvalidOperationException($"GameObject '{goName}' not found. Use unity_getSceneHierarchy to list available objects."); }

            Type compType = ResolveComponentType(compName);
            if (compType == null) { throw new InvalidOperationException($"Component type '{compName}' not recognised."); }

            Component comp = go.GetComponent(compType);
            if (comp == null) { throw new InvalidOperationException($"'{goName}' does not have a '{compName}' component. Use unity_findGameObjects with hasComponent to verify."); }

            var so   = new SerializedObject(comp);
            var prop = so.GetIterator();
            var sb   = new System.Text.StringBuilder("{");
            bool first = true;

            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "m_Script") { continue; }
                    string jsonVal = SerializedPropToJson(prop);
                    if (jsonVal == null) { continue; }
                    if (!first) { sb.Append(","); }
                    first = false;
                    sb.Append($"\"{prop.name.Replace("\"", "\\\"")}\":{jsonVal}");
                }
                while (prop.NextVisible(false));
            }
            sb.Append("}");

            data = null;
            return sb.ToString();
        }

        private static string SerializedPropToJson(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:     return prop.intValue.ToString();
                case SerializedPropertyType.Boolean:     return prop.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:       return prop.floatValue.ToString("G6");
                case SerializedPropertyType.String:      return $"\"{prop.stringValue.Replace("\\","\\\\").Replace("\"","\\\"")}\"";
                case SerializedPropertyType.Enum:        return $"\"{prop.enumNames[prop.enumValueIndex]}\"";
                case SerializedPropertyType.Vector2:     var v2 = prop.vector2Value; return $"{{\"x\":{v2.x},\"y\":{v2.y}}}";
                case SerializedPropertyType.Vector3:     var v3 = prop.vector3Value; return $"{{\"x\":{v3.x},\"y\":{v3.y},\"z\":{v3.z}}}";
                case SerializedPropertyType.Vector4:     var v4 = prop.vector4Value; return $"{{\"x\":{v4.x},\"y\":{v4.y},\"z\":{v4.z},\"w\":{v4.w}}}";
                case SerializedPropertyType.Color:       var c  = prop.colorValue;   return $"{{\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";
                case SerializedPropertyType.LayerMask:   return prop.intValue.ToString();
                case SerializedPropertyType.Rect:        var r  = prop.rectValue;    return $"{{\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}";
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? $"\"{prop.objectReferenceValue.name.Replace("\"","\\\"")}\""
                        : "null";
                default: return null;
            }
        }

        // ── 19. setComponentProperty ────────────────────────────────────
        // Sets a single serialized property on a component by name.
        // Pass value as a string; it is parsed to match the property type.
        // For Vector3 use "x,y,z" format (e.g. "1.5,0,3").
        private static string SetComponentProperty(SimpleJson p)
        {
            string goName    = p.GetString("gameObjectName");
            string compName  = p.GetString("componentType");
            string propName  = p.GetString("propertyName");
            string rawValue  = p.GetString("value");
            if (string.IsNullOrEmpty(goName))   { throw new ArgumentException("'gameObjectName' is required"); }
            if (string.IsNullOrEmpty(compName)) { throw new ArgumentException("'componentType' is required"); }
            if (string.IsNullOrEmpty(propName)) { throw new ArgumentException("'propertyName' is required"); }
            if (rawValue == null)               { throw new ArgumentException("'value' is required"); }

            GameObject go = FindGameObjectInScene(goName);
            if (go == null) { throw new InvalidOperationException($"GameObject '{goName}' not found. Use unity_getSceneHierarchy to list available objects."); }

            Type compType = ResolveComponentType(compName);
            if (compType == null) { throw new InvalidOperationException($"Component type '{compName}' not recognised."); }

            Component comp = go.GetComponent(compType);
            if (comp == null) { throw new InvalidOperationException($"'{goName}' does not have a '{compName}' component."); }

            var so   = new SerializedObject(comp);
            var prop = so.FindProperty(propName);
            if (prop == null) { throw new InvalidOperationException($"Property '{propName}' not found on '{compName}'. Use unity_getComponentProperties to list available properties."); }

            Undo.RecordObject(comp, $"UnityCopilot: Set {compName}.{propName}");

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (!int.TryParse(rawValue, out int iv)) { throw new FormatException($"Cannot parse '{rawValue}' as int."); }
                    prop.intValue = iv;
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = rawValue.Trim().ToLower() == "true" || rawValue.Trim() == "1";
                    break;
                case SerializedPropertyType.Float:
                    if (!float.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv))
                    { throw new FormatException($"Cannot parse '{rawValue}' as float."); }
                    prop.floatValue = fv;
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = rawValue;
                    break;
                case SerializedPropertyType.Enum:
                    int enumIdx = System.Array.IndexOf(prop.enumNames, rawValue);
                    if (enumIdx < 0) { throw new InvalidOperationException($"Enum value '{rawValue}' not valid. Options: {string.Join(", ", prop.enumNames)}"); }
                    prop.enumValueIndex = enumIdx;
                    break;
                case SerializedPropertyType.Vector3:
                    var parts3 = rawValue.Split(',');
                    if (parts3.Length != 3) { throw new FormatException("Vector3 value must be in 'x,y,z' format e.g. '1.5,0,3'."); }
                    prop.vector3Value = new Vector3(
                        float.Parse(parts3[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts3[1].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts3[2].Trim(), System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case SerializedPropertyType.Vector2:
                    var parts2 = rawValue.Split(',');
                    if (parts2.Length != 2) { throw new FormatException("Vector2 value must be in 'x,y' format e.g. '1.5,3'."); }
                    prop.vector2Value = new Vector2(
                        float.Parse(parts2[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts2[1].Trim(), System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case SerializedPropertyType.Color:
                    var partsC = rawValue.Split(',');
                    if (partsC.Length < 3) { throw new FormatException("Color value must be in 'r,g,b' or 'r,g,b,a' format (0-1 range)."); }
                    float a = partsC.Length >= 4 ? float.Parse(partsC[3].Trim(), System.Globalization.CultureInfo.InvariantCulture) : 1f;
                    prop.colorValue = new Color(
                        float.Parse(partsC[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(partsC[1].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(partsC[2].Trim(), System.Globalization.CultureInfo.InvariantCulture), a);
                    break;
                default:
                    throw new NotSupportedException($"Property type '{prop.propertyType}' is not supported for direct editing. Edit the component manually in the Inspector.");
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            return $"Set '{compName}.{propName}' = '{rawValue}' on '{goName}'.";
        }

        // ── 20. captureScreenshot ───────────────────────────────────────
        // Renders the active Scene view to a PNG and returns it as base64.
        private static string CaptureScreenshot(SimpleJson p, out object data)
        {
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv == null) { throw new InvalidOperationException("No active Scene view found. Open the Scene view tab in the Unity Editor first."); }

            Camera cam    = sv.camera;
            int    width  = Mathf.Max(1, (int)sv.position.width);
            int    height = Mathf.Max(1, (int)sv.position.height);

            var rt   = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] bytes  = tex.EncodeToPNG();
            string base64 = Convert.ToBase64String(bytes);

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            data = new Dictionary<string, object>
            {
                { "imageBase64", base64 },
                { "mimeType",    "image/png" },
                { "width",       width },
                { "height",      height },
            };
            return $"Scene view screenshot captured ({width}x{height}).";
        }

        // ── 21. undoRedo ───────────────────────────────────────────────
        private static string UndoRedo(SimpleJson p)
        {
            string op = p.GetString("operation") ?? "undo";
            if (op == "redo") { Undo.PerformRedo(); return "Redo performed successfully."; }
            Undo.PerformUndo();
            return "Undo performed successfully.";
        }

        // ── 22. getUndoHistory ──────────────────────────────────────────
        private static string GetUndoHistory(out object data)
        {
            string currentGroup = Undo.GetCurrentGroupName();
            int    groupIndex   = Undo.GetCurrentGroup();

            var sb = new System.Text.StringBuilder("{");
            sb.Append($"\"currentGroup\":{groupIndex}");
            sb.Append($",\"currentGroupName\":\"{currentGroup.Replace("\"","\\\"").Replace("\\","\\\\").Replace("\n","\\n")}\"");
            sb.Append("}");

            data = null;
            return sb.ToString();
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
