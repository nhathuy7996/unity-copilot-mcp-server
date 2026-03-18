/**
 * build-rpg.js
 * Connects directly to UnityBridgeServer (ws://127.0.0.1:6400)
 * and builds the City Hero RPG scene step by step.
 * Run: node build-rpg.js
 */

const WebSocket = require('ws');
const { randomUUID } = require('crypto');

const PORT = 6400;
const TIMEOUT = 15000;

// ── Helpers ────────────────────────────────────────────────────────

function send(ws, action, params) {
  return new Promise((resolve, reject) => {
    const id = randomUUID();
    const timer = setTimeout(() => reject(new Error(`Timeout: ${action}`)), TIMEOUT);
    ws.once('message', function handler(data) {
      // Keep listening until we get our own id back
      try {
        const msg = JSON.parse(data.toString());
        if (msg.id === id) {
          clearTimeout(timer);
          resolve(msg);
        } else {
          ws.once('message', handler); // re-attach for next message
        }
      } catch { ws.once('message', handler); }
    });
    ws.send(JSON.stringify({ id, action, params }));
  });
}

function log(step, res) {
  const icon = res.success ? '✅' : '❌';
  console.log(`${icon} [${step}] ${res.message}`);
  if (res.data) console.log('   →', JSON.stringify(res.data));
}

async function build(ws) {
  let r;

  console.log('\n═══════════════════════════════════════════════');
  console.log('  🎮  CITY HERO RPG — Auto Build via Bridge');
  console.log('═══════════════════════════════════════════════\n');

  // ── 1. Create main scene ─────────────────────────────────────────
  r = await send(ws, 'createScene', {
    name: 'CityHeroMain',
    savePath: '00GAME/Scenes',
    addToBuildSettings: true,
  });
  log('1. Create CityHeroMain scene', r);

  // ── 2. Open it ───────────────────────────────────────────────────
  r = await send(ws, 'openScene', { scenePath: '00GAME/Scenes/CityHeroMain.unity' });
  log('2. Open CityHeroMain', r);

  // ── 3. Ground plane ──────────────────────────────────────────────
  r = await send(ws, 'createGameObject', {
    name: 'Ground',
    primitiveType: 'Plane',
    position: { x: 0, y: 0, z: 0 },
    scale: { x: 10, y: 1, z: 10 },
  });
  log('3. Create Ground (Plane 100x100)', r);

  // ── 4. Player prefab from Mannequin_Medium model ─────────────────
  r = await send(ws, 'createPrefab', {
    name: 'PlayerPrefab',
    savePath: 'Prefabs',
    modelPath: '00GAME/Models/ThirdParty/KayKit_Character_Animations_1.1/Mannequin Character/characters/Mannequin_Medium.fbx',
    components: ['CharacterController', 'Animator'],
    tag: 'Player',
  });
  log('4. Create PlayerPrefab (from Mannequin_Medium)', r);

  // ── 5. Assign AnimatorController to player prefab ────────────────
  // First instantiate it so we can set the animator controller
  r = await send(ws, 'instantiatePrefab', {
    prefabPath: 'Prefabs/PlayerPrefab.prefab',
    name: 'Player',
    position: { x: 0, y: 0, z: 0 },
  });
  log('5. Instantiate Player into scene', r);

  r = await send(ws, 'setAnimatorController', {
    gameObjectName: 'Player',
    controllerPath: '00GAME/Models/ThirdParty/KayKit_Character_Animations_1.1/Animations/fbx/Rig_Medium/Rig_Medium_MovementBasic.controller',
  });
  log('6. Assign Rig_Medium_MovementBasic animator', r);

  // ── 7. Camera rig ────────────────────────────────────────────────
  r = await send(ws, 'createGameObject', {
    name: 'CameraRig',
    primitiveType: 'Empty',
    position: { x: 0, y: 0, z: 0 },
  });
  log('7. Create CameraRig', r);

  r = await send(ws, 'createGameObject', {
    name: 'Main Camera',
    parent: 'CameraRig',
    primitiveType: 'Empty',
    components: ['Camera', 'AudioListener'],
    position: { x: 0, y: 5, z: -8 },
    rotation: { x: 20, y: 0, z: 0 },
  });
  log('8. Create Main Camera (under CameraRig)', r);

  // ── 9. Directional Light ─────────────────────────────────────────
  r = await send(ws, 'createGameObject', {
    name: 'Sun',
    primitiveType: 'Empty',
    components: ['Light'],
    rotation: { x: 50, y: -30, z: 0 },
  });
  log('9. Create Sun (Directional Light)', r);

  // ── 10. SpawnPoint ───────────────────────────────────────────────
  r = await send(ws, 'createGameObject', {
    name: 'SpawnPoint',
    primitiveType: 'Empty',
    position: { x: 0, y: 0.1, z: 0 },
  });
  log('10. Create SpawnPoint', r);

  // ── 11. City buildings (using KayKit City Builder) ───────────────
  const buildings = [
    { name: 'Building_A_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/building_A.fbx', x: 15, z: 10 },
    { name: 'Building_B_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/building_B.fbx', x: -15, z: 10 },
    { name: 'Building_C_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/building_C.fbx', x: 15, z: -10 },
    { name: 'Building_D_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/building_D.fbx', x: -15, z: -10 },
    { name: 'Building_E_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/building_E.fbx', x: 0, z: 20 },
    { name: 'Building_F_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/building_F.fbx', x: 25, z: 0 },
    { name: 'Building_G_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/building_G.fbx', x: -25, z: 0 },
  ];

  // Create City parent object
  r = await send(ws, 'createGameObject', { name: 'City', primitiveType: 'Empty', position: { x: 0, y: 0, z: 0 } });
  log('11. Create City container', r);

  for (let i = 0; i < buildings.length; i++) {
    const b = buildings[i];
    r = await send(ws, 'createPrefab', {
      name: b.name,
      savePath: 'Prefabs/Buildings',
      modelPath: b.model,
    });
    log(`12.${i + 1}. Prefab ${b.name}`, r);

    r = await send(ws, 'instantiatePrefab', {
      prefabPath: `Prefabs/Buildings/${b.name}.prefab`,
      name: b.name,
      parent: 'City',
      position: { x: b.x, y: 0, z: b.z },
    });
    log(`12.${i + 1}b. Place ${b.name} at (${b.x},0,${b.z})`, r);
  }

  // ── 13. Roads ────────────────────────────────────────────────────
  const roads = [
    { name: 'Road_S_1',  model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/road_straight.fbx',  x: 0,  z: 5 },
    { name: 'Road_S_2',  model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/road_straight.fbx',  x: 0,  z: -5 },
    { name: 'Road_S_3',  model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/road_straight.fbx',  x: 5,  z: 0,  ry: 90 },
    { name: 'Road_S_4',  model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/road_straight.fbx',  x: -5, z: 0,  ry: 90 },
    { name: 'Road_J_1',  model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/road_junction.fbx',  x: 0,  z: 0 },
  ];

  r = await send(ws, 'createGameObject', { name: 'Roads', primitiveType: 'Empty', position: { x: 0, y: 0, z: 0 } });
  log('13. Create Roads container', r);

  for (let i = 0; i < roads.length; i++) {
    const rd = roads[i];
    r = await send(ws, 'createPrefab', {
      name: rd.name,
      savePath: 'Prefabs/Roads',
      modelPath: rd.model,
    });
    log(`13.${i + 1}. Prefab ${rd.name}`, r);

    r = await send(ws, 'instantiatePrefab', {
      prefabPath: `Prefabs/Roads/${rd.name}.prefab`,
      name: rd.name,
      parent: 'Roads',
      position: { x: rd.x, y: 0, z: rd.z },
      rotation: { x: 0, y: rd.ry || 0, z: 0 },
    });
    log(`13.${i + 1}b. Place ${rd.name}`, r);
  }

  // ── 14. Props ────────────────────────────────────────────────────
  const props = [
    { name: 'StreetLight_1', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/streetlight.fbx', x: 3, z: 3 },
    { name: 'StreetLight_2', model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/streetlight.fbx', x: -3, z: 3 },
    { name: 'Bench_1',       model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/bench.fbx',       x: 4, z: -3 },
    { name: 'TrafficLight_1',model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/trafficlight_A.fbx', x: -2, z: 4 },
    { name: 'Car_Police_1',  model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/car_police.fbx',  x: 6, z: 5 },
    { name: 'Car_Taxi_1',    model: '00GAME/Models/ThirdParty/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/car_taxi.fbx',    x: -6, z: 5 },
  ];

  r = await send(ws, 'createGameObject', { name: 'Props', primitiveType: 'Empty', position: { x: 0, y: 0, z: 0 } });
  log('14. Create Props container', r);

  for (let i = 0; i < props.length; i++) {
    const pr = props[i];
    r = await send(ws, 'createPrefab', {
      name: pr.name,
      savePath: 'Prefabs/Props',
      modelPath: pr.model,
    });
    log(`14.${i + 1}. Prefab ${pr.name}`, r);

    r = await send(ws, 'instantiatePrefab', {
      prefabPath: `Prefabs/Props/${pr.name}.prefab`,
      name: pr.name,
      parent: 'Props',
      position: { x: pr.x, y: 0, z: pr.z },
    });
    log(`14.${i + 1}b. Place ${pr.name}`, r);
  }

  // ── 15. Game Scripts ─────────────────────────────────────────────
  const scripts = [
    { scriptName: 'GameManager',         template: 'MonoBehaviour', savePath: 'Scripts' },
    { scriptName: 'PlayerInputHandler',  template: 'MonoBehaviour', savePath: 'Scripts' },
    { scriptName: 'CameraController',    template: 'MonoBehaviour', savePath: 'Scripts' },
    { scriptName: 'MobileJoystick',      template: 'MonoBehaviour', savePath: 'Scripts/UI' },
    { scriptName: 'MobileUIManager',     template: 'MonoBehaviour', savePath: 'Scripts/UI' },
  ];

  for (const s of scripts) {
    r = await send(ws, 'createScript', s);
    log(`15. Script ${s.scriptName}`, r);
  }

  // ── 16. UI Canvas (EventSystem, Canvas, Joystick) ────────────────
  r = await send(ws, 'createGameObject', {
    name: 'UICanvas',
    primitiveType: 'Empty',
    components: ['Canvas', 'CanvasScaler', 'GraphicRaycaster'],
  });
  log('16. Create UICanvas', r);

  r = await send(ws, 'createGameObject', {
    name: 'EventSystem',
    primitiveType: 'Empty',
    components: ['EventSystem', 'StandaloneInputModule'],
  });
  log('17. Create EventSystem', r);

  // ── 18. Save scene ───────────────────────────────────────────────
  r = await send(ws, 'saveScene', {});
  log('18. SAVE SCENE', r);

  console.log('\n═══════════════════════════════════════════════');
  console.log('  🏙️  City Hero RPG scene built successfully!');
  console.log('═══════════════════════════════════════════════');
  console.log('\nNext steps in Unity Editor:');
  console.log('  1. Assign ThirdPersonController.cs → Player');
  console.log('  2. Assign ThirdPersonCamera.cs     → Main Camera');
  console.log('  3. Wire Joystick UI on Canvas');
  console.log('  4. Set Camera Render Mode, AspectRatio for Portrait');
  console.log('  5. File > Build Settings → Switch to Android/iOS\n');
}

// ── Main ───────────────────────────────────────────────────────────
const ws = new WebSocket(`ws://127.0.0.1:${PORT}`);
ws.on('open', async () => {
  try {
    await build(ws);
  } catch (e) {
    console.error('❌ Error:', e.message);
  } finally {
    ws.close();
    process.exit(0);
  }
});
ws.on('error', e => {
  console.error('❌ Cannot connect to Unity Bridge:', e.message);
  console.error('   → Make sure Unity Editor is open with the bridge installed.');
  process.exit(1);
});
