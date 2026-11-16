# Multiply or Release?

[中文](README.md) | **English**

A runnable Unity **2023.2.20f1** prototype that recreates Algodoo's "auto-battle simulation".

**Zero external art assets**: every shape (quads / circles / rings / wedges / icons) is generated at runtime by code (`Primitives.cs`, `WeaponIcons.cs`), and the whole world is assembled by `Bootstrap.cs` via `[RuntimeInitializeOnLoadMethod]` — no scenes, prefabs or materials required.

| Doc | Contents |
|---|---|
| **This file** | How to run, how to play, where the code is |
| [RULES.md](RULES.md) | Current rules cheat-sheet (numbers, formulas, switches). **Read this first when changing gameplay.** (Chinese) |
| [README_HISTORY.md](README_HISTORY.md) | Dev log: 111 rounds of "fix per feedback", explains why each rule looks the way it does. (Chinese) |

> Authority: the implementation in `Assets/Scripts/` > `RULES.md` > `README_HISTORY.md` (early chapters of the log are outdated — don't copy them).

---

## 1. Running it

1. Unity Hub → Add → pick this folder → open with 2023.2.20f1 (first compile + `Library/` generation takes ~1–3 min).
2. **Press Play in any scene** — the game builds itself at runtime, no specific scene needed.
3. Build: menu `Multiply or Release → 创建主场景并加入 Build Settings`, then `打包 Windows / macOS / Android 版`.

### Controls

| Key | Action |
|---|---|
| `1` `2` `3` `4` | Focus the matching faction in the left weapon bar |
| `K` | Kill the focused faction's turret (test deathrattle + respawn) |
| `B` / `V` | Spawn a 999M debug big ball / a 99M vampire ball |
| `L` | Give every faction one extra multiplier ball |
| `F1` / `F2` | Stats panel / fixed-timestep (recording) mode |
| `-` `+` | Simulation speed ×0.5 / ×2 |
| `Tab` / `Space` | Settings panel (faction count / colors) / pause |

> The battlefield camera uses a **fixed framing** (computed once by `CameraRig.FitSize`); there is no zoom / pan / reset.

---

## 2. What a match looks like

```
LEFT · multiplier zone (shared by all)      RIGHT · battlefield (1024×1024 px)
──────────────────────────────────          ────────────────────────────────────
Launcher fires a ball (value 1)              Turrets in the 4 corners spray bullets
   ↓ buoyancy bubbles lift it                  ↓ bullets paint territory cell by cell
x16 gate (hit from below) ×16                  ↓ 1 cell = 1 point of value
   ↓ teleported back to the muzzle             ↓ barrage / big balls grind enemy shields
Two banks of gates (hit from above)            ↓ shield hits 0 → body touched → turret dies
   ×2/8/4/8/2                                  ↓ deathrattle: each ball → its own big ball
   ↓ teleported back to the muzzle             ↓ big balls collide, paint, attract, drain
8-slot weapon bar: trade the whole
   ball's value for one weapon
   ↓ value resets to 1 → re-fired
                                               ↓ any faction holds ≥98% of cells → win
```

**The core accounting**: a value is at once the size of a ball/bullet, the number of cells it can paint, and how big a thing it can build. Every cost is paid out of that value — nothing appears from thin air.

---

## 3. Rules most easily misread

- **Map** is 1024² pixels, world side length **14 m**, **1 cell = 1 point of value**.
- **Big balls of different factions collide**: both lose `min(a, b) × 1/4` (**not 3/4** — 3/4 is only for same-faction gravity absorption).
- **Vampire balls** only work on **big-ball vs big-ball** collisions (drains 50% when the enemy is below 0.5× own value). **Bullets never drain**, and two vampire balls just use the normal rule against each other.
- **Bullets are not rigidbodies**: `BulletPool` advances all of them in a single `Update`; there is no cap on the count.
- **Per-shot value** = `total bullets / 32768 + 1` (≈4 at start), capped at 192 for the first 8 minutes.
- Full version: [RULES.md](RULES.md) (Chinese).

---

## 4. Code layout

```
Assets/Scripts/
  Core/
    Cfg.cs            All tunable parameters (★ start here to change gameplay)
    Bootstrap.cs      Entry point: builds the world in code
    GameWorld.cs      Orchestrator: spawning / gravity / deathrattle / respawn / win
    Faction.cs        Per-faction state
    FactionSet*.cs    Faction tables (2 / 4 / 8 / 16: colors, names, home points)
    Palette.cs        Faction colors + black/white label choice
    BigNumber.cs      Number formatting (2 531 742 / 1,0M / 34M)
    Primitives.cs     Runtime shape generation, no art assets
  Sim/                Left panel — multiplier zone
    MultiplierZone / MultiplierGate / WeaponZone / Launcher
    MultiplierBall / Bullet / BulletPool / BuoyancyZone / BlockerBall
    VampireFx / GravityFx / TurretFx / Explosion
  Battle/             Right panel — battlefield
    Battlefield.cs    1024² pixel map (dirty-tile incremental upload)
    BigBall.cs        Big balls: collision rules / gravity / jet / drain
    Turret.cs         Turret: weapons / shield / death
  View/
    CameraRig / HudOverlay / BulletRenderer / SettingsPanel
Assets/Editor/        EditorMenu / SmokeTest / LayoutDump / PlayCapture
Tools/CompileCheck.csproj   Syntax-only self-check, not part of the Unity build
```

---

## 5. Where to change what

| Want to change | Where |
|---|---|
| Gameplay numbers (speed, thresholds, multipliers, cooldowns…) | `Core/Cfg.cs` |
| Colors and names for the 4- / 8-faction setups | `Core/FactionSet4.cs` / `FactionSet8.cs` |
| Layout (walls, gates, weapon bar positions) | `Sim/MultiplierZone.cs`, the `*Rect*` / `*Y*` values in `Cfg` |
| Combat rules themselves | `Battle/BigBall.cs`, `Battle/Turret.cs` |
| HUD text | `View/HudOverlay.cs` |

> Faction colors are **HSV** triples (H degrees / S saturation / V value); S and V above 1 are allowed and are *not* clamped. After editing a faction table, any older color override saved by the settings panel is discarded automatically.

---

## 6. Self-checks

```bash
# 1. Syntax-only check (against the local Unity reference assemblies)
cd Tools && dotnet build          # → 0 errors

# 2. In-Unity rule self-test (batch mode, unattended)
Unity -batchmode -nographics -projectPath <project path> \
      -executeMethod MultiplyOrRelease.EditorTools.SmokeTest.Run -logFile -

# 3. Layout dump (for "the screen doesn't match the recording" issues)
Unity -batchmode -nographics -projectPath <project path> \
      -executeMethod MultiplyOrRelease.EditorTools.LayoutDump.Run -logFile -
# → /tmp/mor-layout/layout.json (render it to PNG with render.py to eyeball the layout)
```

The smoke test validates **build + rule math** only, not physics feel — press Play to judge that yourself.
