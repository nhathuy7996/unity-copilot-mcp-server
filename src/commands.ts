// ─── Shared protocol types used by both the VS Code extension
//     and mirrored in the Unity C# bridge ────────────────────

export type ActionName =
  | 'createPrefab'
  | 'createScene'
  | 'addComponent'
  | 'createGameObject'
  | 'createScript'
  | 'setProperty'
  | 'openScene'
  | 'instantiatePrefab'
  | 'getSceneHierarchy'
  | 'listAssets'
  | 'deleteGameObject'
  | 'setMaterial'
  | 'setAnimatorController'
  | 'saveScene'
  | 'ping';

// ── Per-action parameter shapes ───────────────────────────────

export interface CreatePrefabParams {
  /** Name of the prefab file (without .prefab extension) */
  name: string;
  /** Save path relative to Assets/, e.g. "Prefabs" */
  savePath?: string;
  /** Unity component type names to add, e.g. ["Rigidbody", "BoxCollider"] */
  components?: string[];
  /**
   * Path to an existing 3D model asset (relative to Assets/).
   * When set, the prefab is created FROM this model instead of a plain GameObject.
   * e.g. "00GAME/Models/Enemy.fbx"
   */
  modelPath?: string;
  /** Optional tag to assign to the root GameObject */
  tag?: string;
  /** Optional layer name to assign */
  layer?: string;
}

export interface CreateSceneParams {
  /** Scene file name (without .unity extension) */
  name: string;
  /** Save path relative to Assets/, e.g. "00GAME/Scenes" */
  savePath?: string;
  /** Whether to add the new scene to Build Settings */
  addToBuildSettings?: boolean;
}

export interface AddComponentParams {
  /** Name of the GameObject in the currently active scene (exact match) */
  gameObjectName: string;
  /** Unity component type name, e.g. "Rigidbody", "AudioSource" */
  componentType: string;
}

export type PrimitiveType =
  | 'Cube'
  | 'Sphere'
  | 'Plane'
  | 'Cylinder'
  | 'Capsule'
  | 'Quad'
  | 'Empty';

export interface Vector3 {
  x: number;
  y: number;
  z: number;
}

export interface CreateGameObjectParams {
  /** Name for the new GameObject */
  name: string;
  /** Optional parent GameObject name in Hierarchy */
  parent?: string;
  /** Primitive mesh type; "Empty" creates a plain GameObject */
  primitiveType?: PrimitiveType;
  position?: Vector3;
  rotation?: Vector3;
  scale?: Vector3;
  /** Components to add after creation */
  components?: string[];
}

export type ScriptTemplate =
  | 'MonoBehaviour'
  | 'ScriptableObject'
  | 'Editor'
  | 'Interface'
  | 'Empty';

export interface CreateScriptParams {
  /** Class/file name for the script (PascalCase, no .cs extension) */
  scriptName: string;
  /** Script template type */
  template?: ScriptTemplate;
  /** Save path relative to Assets/, e.g. "Scripts" */
  savePath?: string;
  /** If set, attach the script to this GameObject in the active scene */
  attachTo?: string;
}

export interface SetPropertyParams {
  /** Target GameObject name in the active scene */
  gameObjectName: string;
  position?: Vector3;
  rotation?: Vector3;
  scale?: Vector3;
  /** Rename the GameObject */
  rename?: string;
  /** Set active state */
  active?: boolean;
}

export interface OpenSceneParams {
  /** Scene path relative to Assets/ OR just the scene name */
  scenePath: string;
  /** If true, opens scene additively (does not unload current) */
  additive?: boolean;
}

export interface InstantiatePrefabParams {
  /** Path to the prefab relative to Assets/, e.g. "Prefabs/Enemy.prefab" */
  prefabPath: string;
  /** Override the instance name (defaults to prefab name) */
  name?: string;
  /** Parent GameObject name in the active scene */
  parent?: string;
  position?: Vector3;
  rotation?: Vector3;
  scale?: Vector3;
}

/** Returns flat list of root GameObjects; no params required */
export type GetSceneHierarchyParams = Record<string, never>;

export interface ListAssetsParams {
  /** Sub-folder to search, relative to Assets/ (empty = search all Assets) */
  folder?: string;
  /**
   * Unity asset type filter, e.g. "Prefab", "Scene", "Material",
   * "AnimatorController", "AudioClip", "Texture2D".
   * Defaults to "GameObject" (prefabs).
   */
  assetType?: string;
}

export interface DeleteGameObjectParams {
  /** Exact name of the GameObject to delete from the active scene */
  gameObjectName: string;
}

export interface SetMaterialParams {
  /** Name of the target GameObject in the active scene */
  gameObjectName: string;
  /** Path to the material relative to Assets/, e.g. "Materials/HeroMat.mat" */
  materialPath: string;
}

export interface SetAnimatorControllerParams {
  /** Name of the target GameObject in the active scene */
  gameObjectName: string;
  /** Path to the AnimatorController relative to Assets/, e.g. "Animations/HeroController.controller" */
  controllerPath: string;
}

/** Saves the active scene; no params required */
export type SaveSceneParams = Record<string, never>;

// ── Union of all param types ───────────────────────────────────

export type ActionParams =
  | CreatePrefabParams
  | CreateSceneParams
  | AddComponentParams
  | CreateGameObjectParams
  | CreateScriptParams
  | SetPropertyParams
  | OpenSceneParams
  | InstantiatePrefabParams
  | GetSceneHierarchyParams
  | ListAssetsParams
  | DeleteGameObjectParams
  | SetMaterialParams
  | SetAnimatorControllerParams
  | SaveSceneParams
  | Record<string, never>; // ping

// ── Wire message shapes ────────────────────────────────────────

export interface BridgeRequest {
  id: string;
  action: ActionName;
  params: ActionParams;
}

export interface BridgeResponse {
  id: string;
  success: boolean;
  message: string;
  /** Optional structured data returned by the action */
  data?: Record<string, unknown>;
}
