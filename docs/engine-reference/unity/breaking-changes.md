# Unity 6.3 LTS — Breaking Changes

**Last verified:** 2026-02-13

This document tracks breaking API changes and behavioral differences between Unity 2022 LTS
(likely in model training) and Unity 6.3 LTS (current version). Organized by risk level.

## HIGH RISK — Will Break Existing Code

### Entities/DOTS API Complete Overhaul
**Versions:** Entities 1.0+ (Unity 6.0+)

```csharp
// ❌ OLD (pre-Unity 6, GameObjectEntity pattern)
public class HealthComponent : ComponentData {
    public float Value;
}

// ✅ NEW (Unity 6+, IComponentData)
public struct HealthComponent : IComponentData {
    public float Value;
}

// ❌ OLD: ComponentSystem
public class DamageSystem : ComponentSystem { }

// ✅ NEW: ISystem (unmanaged, Burst-compatible)
public partial struct DamageSystem : ISystem {
    public void OnCreate(ref SystemState state) { }
    public void OnUpdate(ref SystemState state) { }
}
```

**Migration:** Follow Unity's ECS migration guide. Major architectural changes required.

---

### Input System — Legacy Input Deprecated
**Versions:** Unity 6.0+

```csharp
// ❌ OLD: Input class (deprecated)
if (Input.GetKeyDown(KeyCode.Space)) { }

// ✅ NEW: Input System package
using UnityEngine.InputSystem;
if (Keyboard.current.spaceKey.wasPressedThisFrame) { }
```

**Migration:** Install Input System package, replace all `Input.*` calls with new API.

---

### URP/HDRP Renderer Feature API Changes
**Versions:** Unity 6.0+

```csharp
// ❌ OLD: ScriptableRenderPass.Execute signature
public override void Execute(ScriptableRenderContext context, ref RenderingData data)

// ✅ NEW: Uses RenderGraph API
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
```

**Migration:** Update custom render passes to use RenderGraph API.

---

## MEDIUM RISK — Behavioral Changes

### Addressables — Asset Loading Returns
**Versions:** Unity 6.2+

Asset loading failures now throw exceptions by default instead of returning null.
Add proper exception handling or use `TryLoad` variants.

```csharp
// ❌ OLD: Silent null on failure
var handle = Addressables.LoadAssetAsync<Sprite>("key");
var sprite = handle.Result; // null if failed

// ✅ NEW: Throws on failure, use try/catch or TryLoad
try {
    var handle = Addressables.LoadAssetAsync<Sprite>("key");
    var sprite = await handle.Task;
} catch (Exception e) {
    Debug.LogError($"Failed to load: {e}");
}
```

---

### Physics — Default Solver Iterations Changed
**Versions:** Unity 6.0+

Default solver iterations increased for better stability.
Check `Physics.defaultSolverIterations` if you rely on old behavior.

---

## LOW RISK — Deprecations (Still Functional)

### UGUI (Legacy UI)
**Status:** Deprecated but supported
**Replacement:** UI Toolkit

UGUI still works but UI Toolkit is recommended for new projects.

---

### Legacy Particle System
**Status:** Deprecated
**Replacement:** Visual Effect Graph (VFX Graph)

---

### Old Animation System
**Status:** Deprecated
**Replacement:** Animator Controller (Mecanim)

---

## Platform-Specific Breaking Changes

### WebGL
- **Unity 6.0+**: WebGPU is now the default (WebGL 2.0 fallback available)
- Update shaders for WebGPU compatibility

### Android
- **Unity 6.0+**: Minimum API level raised to 24 (Android 7.0)

### iOS
- **Unity 6.0+**: Minimum deployment target raised to iOS 13

---

## Migration Checklist

When upgrading from 2022 LTS to Unity 6.3 LTS:

- [ ] Audit all DOTS/ECS code (complete rewrite likely needed)
- [ ] Replace `Input` class with Input System package
- [ ] Update custom render passes to RenderGraph API
- [ ] Add exception handling to Addressables calls
- [ ] Test physics behavior (solver iterations changed)
- [ ] Consider migrating UGUI to UI Toolkit for new UI
- [ ] Update WebGL shaders for WebGPU
- [ ] Verify minimum platform versions (Android/iOS)

---

**Sources:**
- https://docs.unity3d.com/6000.0/Documentation/Manual/upgrade-guides.html
- https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/upgrade-guide.html

---

## 6.3 → 6.5

Last verified: 2026-11-09. Sources: [New in Unity 6.4](https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity64), [New in Unity 6.5](https://docs.unity.com/en-us/engine/6000.5/manual/whats-new/unity65).
Full removed-member list: Upgrade guide 6.5 (`/en-us/engine/6000.5/manual/upgrade-guides/upgrade-guide-unity65`) — NOT fetched this run.

### HIGH — compile errors
- **`InstanceID` → `EntityId`** (6.4 deprecates, 6.5 errors). `Object.GetInstanceID`, `Resources.InstanceIDToObject`, `Selection.instanceIDs` no longer compile. `EntityId` is 64-bit: don't cast to/from `int`, don't round-trip through `ToString`/`int.Parse`, don't sort by it for creation order, don't use `GetHashCode` as an id. Exact replacement method names: NOT SOURCEABLE — not stated on the 6.5 page, see the upgrade guide.
- **Removed accessors**: `GameObject.rigidbody`, `GameObject.camera`, `Component.renderer`, `GameObject.AddComponent(string)` → use `GetComponent<T>` / `AddComponent<T>`. Some obsolete warnings promoted to errors (6.5).
- **URP Compatibility Mode removed** (6.4), and the `URP_COMPATIBILITY_MODE` define with it: custom render passes must use Render Graph.
- **`LowLevelPhysics2D` → `PhysicsCore2D`**, namespace `UnityEngine.LowLevelPhysics2D` → `Unity.U2D.Physics` (6.5).

### MEDIUM — build / behaviour
- Particle System Shape and Terrain Paint Detail textures need **Read/Write** enabled or the build fails (6.5).
- `[SerializeReference]` classes: a warning if the class or any ancestor lacks `[Serializable]` (6.4).
- PVRTC texture format removed — use ASTC/ETC (6.4).
- Unity Accelerator import-result caching off by default for new projects (6.5).
- **Editor log is now project-local**: `<Project>/Logs/Editor.log`; `-useGlobalLog` restores the global one (6.5).

### Platform (6.5)
- Android: min API 26, x86-64 removed, Gradle 9.1.0 / AGP 9.0.0, `proguard-android.txt` unsupported, plug-ins need a unique namespace, `unity.launch-fullscreen` removed (→ `unity.requested-visible-insets` + `unity.system-bars-behavior`).
- iOS: ReplayKit API removed.
- Web/iOS: `UnityWebRequest` now uses Mbed TLS (no longer exempt from App Store encryption export rules).
