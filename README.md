# Multiply or Release?

用 Unity **2023.2.20f1** 复刻 Algodoo「自动对战模拟」的可运行原型。

**零外部美术资源**：所有图形（方块 / 圆 / 环 / 楔形 / 图标）都由代码在运行时生成（`Primitives.cs`、`WeaponIcons.cs`），整个世界由 `Bootstrap.cs` 用 `[RuntimeInitializeOnLoadMethod]` 搭出来 —— 不依赖任何场景、预制体、材质。

| 文档 | 内容 |
|---|---|
| **本文件** | 怎么跑、怎么玩、代码在哪 |
| [RULES.md](RULES.md) | 现行规则速查（数值、公式、开关），**改玩法先看这份** |
| [README_HISTORY.md](README_HISTORY.md) | 开发日志：111 轮「按反馈逐条修正」，解释每条规则为什么长这样 |

> 权威性：`Assets/Scripts/` 的实现 > `RULES.md` > `README_HISTORY.md`（后者早期章节已被推翻，别照抄）。

---

## 一、怎么跑

1. Unity Hub → Add → 选本文件夹 → 用 2023.2.20f1 打开（首次编译 + 生成 `Library/` 约 1~3 分钟）。
2. **任意场景直接 Play 就能跑**，不需要打开特定场景。
3. 打包：菜单 `Multiply or Release → 创建主场景并加入 Build Settings`，再 `打包 Windows / macOS / Android 版`。

### 操作

| 键 | 作用 |
|---|---|
| `1` `2` `3` `4` | 聚焦左栏武器栏对应阵营 |
| `K` | 打死当前聚焦阵营的炮台（验证亡语 + 复活） |
| `B` / `V` | 刷一颗 999M 调试大球 / 99M 吸血球 |
| `L` | 给每个阵营补一颗增倍球 |
| `F1` / `F2` | 性能面板 / 录屏定步长模式 |
| `-` `+` | 模拟速度 ×0.5 / ×2 |
| 滚轮 / `WASD` / `R` | 战场缩放 / 平移 / 复位 |

> `空格`（暂停）、`Tab`（设置面板）目前被 `Cfg.EnableSettingsPanel = false` 总开关关掉，要用得先改成 `true`。

---

## 二、一局在干什么

```
左栏 · 增倍区（四家共用）                右栏 · 战场（1024×1024 像素）
──────────────────────────              ──────────────────────────────
发射炮台投出增倍球（数值 1）              四角炮台持续旋转喷弹幕
   ↓ 浮力气泡托着上飘                       ↓ 子弹沿弹道逐格涂领土
x16 门（从下往上顶）×16                     ↓ 1 像素 = 1 点数值
   ↓ 传回炮口重投                           ↓ 弹幕 / 大球磨敌方护盾
左右两组乘法门（往下撞）×2/8/4/8/2          ↓ 护盾归零 → 本体被碰 → 炮台死亡
   ↓ 传回炮口重投                           ↓ 亡语：增倍球各变独立大球 + 子弹一颗大球
8 格武器栏：整颗球的数值换一件武器           ↓ 大球互撞、涂地、引力、吸血
   ↓ 数值归 1 → 重投                        ↓ 任一家占 ≥98% 像素 → 胜负已分
```

**核心账法**：数值既是球/子弹的大小，也是能涂多少格地、能造多大东西。所有花销都从数值里扣，不凭空产生。

---

## 三、几条最容易踩的规则

- **地图** 1024² 像素，世界边长 **14m**，**1 格 = 1 点数值**。
- **不同阵营大球相撞**：双方各扣 `min(a,b) × 1/4`（**不是 3/4**，同阵营引力吸收才是 3/4）。
- **吸血球**只在**大球撞大球**时生效（敌方 < 自身 ×0.5 时吸走其 50%）；**子弹没有吸血**，两颗吸血球互撞按普通规则扣血。
- **子弹不是物理刚体**：`BulletPool` 在一个 `Update` 里统一推进，数量无上限。
- **单发数值** = `总子弹数 / 32768 + 1`（开局约 4），前 8 分钟封顶 192。
- 完整版见 [RULES.md](RULES.md)。

---

## 四、代码结构

```
Assets/Scripts/
  Core/
    Cfg.cs            全部可调参数（★ 改玩法先看这个）
    Bootstrap.cs      入口：代码搭世界
    GameWorld.cs      总管理：发球 / 引力 / 亡语 / 复活 / 胜负
    Faction.cs        单阵营状态
    FactionSet*.cs    阵营表（2 / 4 / 8 / 16 家各自的颜色、名字、出生点）
    Palette.cs        阵营配色 + 黑/白文字
    BigNumber.cs      数值格式化（2 531 742 / 1,0M / 34M）
    Primitives.cs     运行时生成图形，零美术资源
  Sim/                左栏增倍区
    MultiplierZone / MultiplierGate / WeaponZone / Launcher
    MultiplierBall / Bullet / BulletPool / BuoyancyZone / BlockerBall
    VampireFx / GravityFx / TurretFx / Explosion
  Battle/             右栏战场
    Battlefield.cs    1024² 像素地图（脏块增量上传）
    BigBall.cs        大球：碰撞规则 / 引力 / 喷子弹 / 吸血
    Turret.cs         炮台：武器 / 护盾 / 死亡
  View/
    CameraRig / HudOverlay / BulletRenderer / SettingsPanel
Assets/Editor/        EditorMenu / SmokeTest / LayoutDump / PlayCapture
Tools/CompileCheck.csproj   纯语法自检，不参与 Unity 构建
```

---

## 五、想改什么去哪儿改

| 想改 | 位置 |
|---|---|
| 玩法数值（速度、阈值、倍率、冷却…） | `Core/Cfg.cs` |
| 四家 / 八家的颜色与名字 | `Core/FactionSet4.cs` / `FactionSet8.cs` |
| 版面布局（墙、门、武器栏位置） | `Sim/MultiplierZone.cs`、`Cfg` 里的 `*Rect*` / `*Y*` |
| 战斗规则本体 | `Battle/BigBall.cs`、`Battle/Turret.cs` |
| HUD 文字 | `View/HudOverlay.cs` |

> 阵营色用 **HSV** 三元组（H 度 / S 饱和 / V 明度）定义，允许 S、V > 1（不夹到 1）。改阵营表后，旧的设置面板颜色覆盖会自动作废。

---

## 六、自检

```bash
# 1. 纯语法自检（对着本机 Unity 引用程序集）
cd Tools && dotnet build          # → 0 错误

# 2. Unity 内规则自检（批处理，不用人盯着）
Unity -batchmode -nographics -projectPath <工程路径> \
      -executeMethod MultiplyOrRelease.EditorTools.SmokeTest.Run -logFile -

# 3. 版面导出（查「画面跟录像不一样」这类问题）
Unity -batchmode -nographics -projectPath <工程路径> \
      -executeMethod MultiplyOrRelease.EditorTools.LayoutDump.Run -logFile -
# → /tmp/mor-layout/layout.json（再用 render.py 画成 PNG 看版面）
```

冒烟测试只验证**构建 + 规则计算**，不验证物理手感；手感要按 Play 亲自看。
