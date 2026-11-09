# Technical Preferences

<!-- project.yaml at the repo root is the machine-readable source of truth for
     engine, specialists, naming, performance, platform, and testing.framework.
     This file is the human-readable LEGACY FALLBACK: agents and skills resolve
     each key from project.yaml first and fall back here only when the
     project.yaml key is absent. /setup-engine dual-writes both.
     Forbidden patterns and allowed libraries are NOT migrated — they live only
     in this file. Populated by /setup-engine; updated as decisions are made. -->

## Engine & Language

- **Engine**: Unity 6000.5.2f1 (6.5)
- **Language**: C#
- **Rendering**: URP
- **Physics**: PhysX (only for `Juice.Shatter` debris); balls use the custom `PoolPhysics`

## Input & Platform

<!-- Written by /setup-engine. Read by /ux-design, /ux-review, /test-setup, /team-ui, and /dev-story -->
<!-- to scope interaction specs, test helpers, and implementation to the correct input methods. -->

- **Target Platforms**: PC (Windows) now; mobile planned later
- **Input Methods**: Keyboard/Mouse
- **Primary Input**: Keyboard/Mouse
- **Gamepad Support**: Partial
- **Touch Support**: None (until the mobile port)
- **Platform Notes**: New Input System only (`activeInputHandler: 1`). Mouse-heavy aiming (right button + drag); a mobile port will need a touch aiming scheme.

## Naming Conventions

- **Classes**: PascalCase (`PoolPhysics`)
- **Variables**: camelCase for public and private fields, no underscore prefix (`airborne`, `beltSpeed`); methods PascalCase
- **Signals/Events**: PascalCase `On<Event>`
- **Files**: PascalCase matching the class (`PoolPhysics.cs`; partials `MultiplayerGame.Pool.cs`)
- **Scenes/Prefabs**: PascalCase (`Level01.unity`, `Modules/<Nome>.prefab`)
- **Constants**: PascalCase (`Radius`, `FloorStep`)

## Performance Budgets

- **Target Framerate**: [TO BE CONFIGURED]
- **Frame Budget**: [TO BE CONFIGURED]
- **Draw Calls**: [TO BE CONFIGURED]
- **Memory Ceiling**: [TO BE CONFIGURED]

## Testing

- **Framework**: NUnit (Unity Test Framework), EditMode tests in `Assets/Tests/EditMode`; run through the open editor (Test Runner or `Assets/Editor/TestBridge.cs`), never batchmode while the editor is open
- **Minimum Coverage**: [TO BE CONFIGURED]
- **Required Tests**: Balance formulas, gameplay systems, networking (if applicable)

## Forbidden Patterns

<!-- Add patterns that should never appear in this project's codebase -->
- [None configured yet — add as architectural decisions are made]

## Allowed Libraries / Addons

<!-- Add approved third-party dependencies here -->
- [None configured yet — add as dependencies are approved]

## Architecture Decisions Log

<!-- Quick reference linking to full ADRs in docs/architecture/ -->
- [No ADRs yet — use /architecture-decision to create one]

## Engine Specialists

<!-- Written by /setup-engine when engine is configured. -->
<!-- Read by /code-review, /architecture-decision, /architecture-review, and team skills -->
<!-- to know which specialist to spawn for engine-specific validation. -->

- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist (C# review — primary covers it)
- **Shader Specialist**: unity-shader-specialist (Shader Graph, HLSL, URP materials)
- **UI Specialist**: unity-ui-specialist (UGUI Canvas built from code, runtime UI)
- **Additional Specialists**: unity-dots-specialist (ECS, Jobs, Burst), unity-addressables-specialist (asset loading)
- **Routing Notes**: Primary for architecture and C# review. Shader specialist for rendering/VFX. UI specialist for interface work. DOTS/Addressables only if those systems are adopted (not used today).

### File Extension Routing

<!-- Skills use this table to select the right specialist per file type. -->
<!-- If a row says [TO BE CONFIGURED], fall back to Primary for that file type. -->

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs files) | unity-specialist |
| Shader / material files (.shader, .shadergraph, .mat) | unity-shader-specialist |
| UI / screen files (.uxml, .uss, Canvas prefabs) | unity-ui-specialist |
| Scene / prefab / level files (.unity, .prefab, Levels/*.txt) | unity-specialist |
| Native extension / plugin files (.dll, native plugins) | unity-specialist |
| General architecture review | Primary |
