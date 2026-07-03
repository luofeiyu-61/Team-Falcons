# CODELY.md — Project Context for Codely CLI

## Project Overview

A **2D physics-based platformer/puzzle game** where the player controls a circle character that can run, jump, and dynamically place "anchor" objects that exert gravitational (attract) or repulsive (repel) forces on the player. The core gameplay loop involves using anchors to navigate levels by manipulating the player's trajectory through physics forces.

- **Unity Version**: 2021.3.21f1c1 (Tuanjie / Unity China variant)
- **Render Pipeline**: Built-in (Legacy)
- **Product Name**: My project (1)
- **Company Name**: DefaultCompany
- **Repository**: [github.com/luofeiyu-61/Team-Falcons](https://github.com/luofeiyu-61/Team-Falcons) (Team Falcons)
- **Target Platforms**: PC (default 1920×1080); Android/iOS settings present but not primary

---

## Key Scenes & Entry Point

| Scene | Path | Build Index |
|-------|------|-------------|
| SampleScene | `Assets/Scenes/SampleScene.unity` | 0 |

**Entry Point**: `SampleScene.unity` — the only scene in Build Settings.

### Scene Hierarchy (SampleScene)

```
Main Camera          — Orthographic 2D camera
Square               — Ground floor (Layer: Ground)
Square (1)           — Ceiling (Layer: Ground)
Square (2)           — Left wall (Layer: Ground)
Square (3)           — Right wall (Layer: Ground)
Circle               — Player character (Layer: Player)
  └── Square (4)     — Ground check child (for overlap detection)
Circle (1)           — Anchor template (Layer: Anchor, referenced as prefab by AnchorManager)
GameManager          — Holds AnchorManager component
```

---

## Core Scripts

All scripts live in `Assets/Scripts/` with no assembly definitions (single default assembly).

### PlayerController2D.cs
- **Class**: `PlayerController2D : MonoBehaviour`
- **Purpose**: 2D platformer character controller with polished movement feel.
- **Key Features**:
  - Acceleration/deceleration-based horizontal movement (A/D keys)
  - Variable-height jumping (W/Space) with jump-cut multiplier
  - Coyote time (0.1s grace period after leaving ground)
  - Jump buffering (0.1s input lookahead)
  - Ground detection via `Physics2D.OverlapCircle` on a child transform
- **Input**: Keyboard only — A/D for movement, W/Space for jump
- **Dependencies**: `Rigidbody2D`, a child `Transform` for ground check, `LayerMask` for ground layer

### Anchor.cs
- **Class**: `Anchor : MonoBehaviour`
- **Enum**: `AnchorMode { Attract, Repel }`
- **Purpose**: Physics force field that attracts or repels `Rigidbody2D` objects within a radius using an inverse-square law formula.
- **Key Features**:
  - Configurable effect radius, gravitational constant, anchor mass, minimum distance
  - Applies force via `Rigidbody2D.AddForce` in `FixedUpdate`
  - Deduplicates bodies (handles multiple colliders on same object)
  - Destroys objects that get too close (< 0.1 units)
  - Gizmo visualization of effect radius
- **Layer Mask**: `targetLayer` — which layers are affected by the anchor

### AnchorManager.cs
- **Class**: `AnchorManager : MonoBehaviour`
- **Purpose**: Manages anchor placement/removal via mouse input with a charge-based resource system.
- **Key Features**:
  - Left-click to place anchor at mouse world position
  - Right-click to remove anchor (optional charge refund)
  - Resource system: starting charges, placement cost, max active anchors
  - Mode switching: Attract/Repel (Repel locked behind `UnlockRepel()`)
  - Placement validation: blocked by walls/platforms, clear radius check
  - UI-safe: ignores clicks over EventSystem UI elements
  - Public API: `UnlockRepel()`, `SelectAttract()`, `SelectRepel()` for UI/level events
- **Dependencies**: `Camera` (for screen-to-world raycasting), `Anchor` prefab reference

---

## Layers & Tags

### Custom Layers
| Index | Name    | Usage |
|-------|---------|-------|
| 3     | Player  | Player character |
| 6     | Ground  | Walls, floor, ceiling — collision and ground check |
| 7     | Anchor  | Anchor objects — removable by right-click |

### Tags
Default Unity tags only (Untagged, MainCamera, Player, GameController, etc.) — no custom tags defined.

---

## Package & Dependency List

### Unity Registry Packages
| Package | Version | Purpose |
|---------|---------|---------|
| `com.unity.feature.2d` | 1.0.0 | 2D game development (sprites, physics 2D, tilemap, etc.) |
| `com.unity.textmeshpro` | 3.0.6 | Text rendering |
| `com.unity.timeline` | 1.6.4 | Cutscene/sequence editing |
| `com.unity.ugui` | 1.0.0 | UI system |
| `com.unity.test-framework` | 1.1.31 | Unity Test Framework |
| `com.unity.visualscripting` | 1.8.0 | Visual scripting |
| `com.unity.collab-proxy` | 2.0.1 | Unity Version Control |
| `com.unity.ide.rider` | 3.0.18 | JetBrains Rider IDE support |
| `com.unity.ide.visualstudio` | 2.0.17 | Visual Studio IDE support |
| `com.unity.ide.vscode` | 1.2.5 | VS Code IDE support |

### Tuanjie / Local Packages
| Package | Version | Purpose |
|---------|---------|---------|
| `cn.tuanjie.codely.bridge` | 1.0.66 | Codely CLI Unity bridge |
| `cn.tuanjie.ai.generators` | local (file:) | AI asset generation tools |

### Built-in Modules
Standard set including: `physics2d`, `tilemap`, `animation`, `audio`, `particlesystem`, `video`, `xr`, `vr`, `terrain`, `physics`, `ai`, and more.

---

## Building & Running

### Editor
1. Open the project folder in Unity Hub (Unity 2021.3.21f1c1).
2. Open `Assets/Scenes/SampleScene.unity`.
3. Press **Play** (▶) to test.

### Codely CLI (Unity Tools)
- **Play**: `unity_editor` → `action: "play"`
- **Stop**: `unity_editor` → `action: "stop"`
- **Compile & Validate**: `unity_workflow` → `action: "compile_and_validate"`

### CLI / Batchmode
No custom build script exists yet. Standard Unity CLI:
```bash
Unity -batchmode -quit -projectPath . -buildTarget Win64 -logFile build.log
```

### Testing
- Unity Test Framework (v1.1.31) is installed but **no test assemblies or test files** currently exist.
- No `run-tests.sh` or equivalent script found.

---

## Development Conventions

### Folder Structure
```
Assets/
├── Scenes/          # Scene files (.unity)
├── Scripts/         # C# scripts (flat structure, no sub-folders yet)
```
> **Note**: The 开发规范.md recommends splitting Scripts into `Player/`, `Enemy/`, `UI/`, `System/`, `Tools/` sub-folders as the project grows.

### Naming Conventions
- **Scripts/Classes**: PascalCase (e.g., `PlayerController2D`, `AnchorManager`)
- **GameObject instances**: Unity default naming with incrementing suffixes (e.g., `Square`, `Square (1)`, `Circle`, `Circle (1)`)
- **Serialized fields**: camelCase with `[SerializeField] private` (not public)
- **Headers**: Chinese language `[Header("...")]` attributes (e.g., `[Header("移动")]`, `[Header("跳跃")]`)

### Code Style
- `private` serialized fields with `[SerializeField]` — no public fields
- `[Header]` and `[Min]` attributes for inspector organization
- Chinese comments for section labels and gameplay logic explanations
- `FixedUpdate` for physics, `Update` for input detection
- `Mathf.MoveTowards` for smooth velocity transitions
- Gizmo drawing (`OnDrawGizmosSelected`) for debug visualization

### Assembly Definitions
None — all scripts compile into the default `Assembly-CSharp.dll`.

### Git Workflow (from 开发规范.md)
- **Branch strategy**: `main` (stable) ← `feature/xxx` (feature branches) via Pull Request
- **Commit message format**: `type: description` (e.g., `feat: add player movement`, `fix: fix jump bug`)
- **Commit types**: `feat`, `fix`, `refactor`, `art`, `ui`, `docs`
- **Rules**:
  - Never develop directly on `main`
  - Always `.meta` files must be committed
  - Communicate before modifying shared Prefabs/Scenes
  - Small incremental commits, push frequently

### Version Control
**Must commit**: `Assets/`, `Packages/`, `ProjectSettings/`, all `.meta` files, `.gitignore`, `README.md`
**Must NOT commit**: `Library/`, `Temp/`, `Logs/`, `Obj/`, `Build/`, `Builds/`, `UserSettings/`, `.vs/`

---

## Known Issues & Console

- **Console Error** (non-critical): `WindowsVideoMedia error unhandled Color Standard: 0` — video playback color space warning, does not affect gameplay.
- **Scene is dirty** (unsaved changes) at time of analysis.
- **No Prefab assets** — the anchor "prefab" is currently a scene object (`Circle (1)`) referenced by `AnchorManager.anchorPrefab`. This should be converted to a proper `.prefab` asset.
- **No README.md** exists in the project root.

---

## TODO / Open Questions

- [ ] Create proper Prefab assets for Player and Anchor (currently scene-only objects)
- [ ] Add a `README.md` with project description and setup instructions
- [ ] Implement UI for charge count display and mode selection buttons
- [ ] Add level progression / multiple scenes
- [ ] Set up Unity Test Framework tests
- [ ] Create a custom build script (`BuildScript.cs`) for CI/CD
- [ ] Define proper `companyName` and `productName` in Project Settings
- [ ] Consider splitting scripts into sub-folders per the 开发规范.md recommendation
- [ ] Add audio (BGM/SFX) for jumps, anchor placement, and ambient
- [ ] Add visual effects for anchor force fields
