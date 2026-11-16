# Multiply or Release? — 现行规则速查

> 整理于 2026-11-08，依据当前工作区代码（Unity 2023.2.20f1）。
>
> **权威性顺序**：`Assets/Scripts/` 里的实现 > 本文件 > `README.md`。
> `README.md` 是 3452 行、111 轮的「按反馈逐条修正」日志，**开头 1~320 行的「规格 / 待确认 12 项 / 代码结构」属于早期版本，绝大多数已被后续轮次推翻**。本文件只写当前真正生效的规则，数值均可在 `Assets/Scripts/Core/Cfg.cs` 里核到。
>
> 想改规则：先改 `Cfg.cs`；想查「为什么是这样」：去 `README.md` 翻对应轮次；想确认没改坏：跑 `Assets/Editor/SmokeTest.cs`。

---

## 0. 一局游戏在干什么

```
左栏（增倍区，四家共用）              右栏（战场，1024×1024 像素）
─────────────────────────            ──────────────────────────────
发射炮台投出增倍球(值=1)              四角炮台持续旋转喷弹幕
        ↓ 浮力气泡托着往上飘                  ↓ 子弹沿弹道逐格涂领土
   x16 门（从下往上顶）×16                   ↓ 1 像素 = 1 点数值
        ↓ 立刻传回发射炮台重投               ↓ 弹幕 / 大球打敌方护盾
   左右两组乘法门（从上往下撞）×2/8/4/8/2     ↓ 护盾归零 → 本体被碰到 → 炮台死亡
        ↓ 立刻传回发射炮台重投               ↓ 亡语：增倍球各变独立大球 + 子弹一颗大球
   8 格武器栏：用整颗球的数值换一件武器        ↓ 大球相互作用、涂地、引力、吸血
        ↓ 数值归 1 → 重投                    ↓ 任一家占领 ≥98% 像素 → 胜负已分
```

**核心账法**：数值既是「球/子弹的大小」，也是「能涂多少格地」和「能造多大东西」。所有花销都从数值里扣，不存在凭空产生。

---

## 1. 对局与开局

| 项 | 当前值 | 出处 |
|---|---|---|
| 阵营数 | `FactionSet.Count`，默认 4；另备 2 / 8 / 16 档 | `FactionSet.cs`、`FactionSet2/4/8/16.cs` |
| 开局弹药 | `100000` | `Cfg.StartBullets` |
| 开局护盾 | `15388608` | `Cfg.StartShield` |
| 开局增倍球 | 每家 2 颗，数值 1 | `Cfg.StartMultiplierBalls` / `NewBallStartValue` |
| 开局射击模式 | **散射**（炮台边转边撒） | `Faction.TurretMode` |
| 开局领土 | 出生点周围半径 1.8m（16 家时 0.9m），**免费不走账** | `Cfg.StartTerritoryRadius`、`GameWorld.Build` |
| 地图 | 1024² 像素 = 1,048,576 格；世界边长 **14m** | `Cfg.MapTexels` / `MapWorldSize` |
| 涂地单价 | **1 格 = 1 点数值**（整图填满需 1,048,576 点） | `Cfg.PixelCaptureCost` |
| 补球 | 每 **80 秒** +1 颗增倍球；炮台死亡期间不发，复活后顺延 | `Cfg.MultiplierBallGrantInterval`、`GameWorld.Update` |
| 胜负 | 某家像素 ≥ 总像素 × **0.98** → `WinnerIndex`；无时间上限 | `GameWorld.CheckWinner` |

> 炮台在**右栏战场四角**，不在增倍区里；出生点由阵营表 `FactionSet.Get(i).Home` 给。

---

## 2. 增倍区（左栏）

### 2.1 空间与物理

- 房间 `18m × 26m`（`ZoneWidth` / `ZoneHeight`，`ZoneScale=1`），墙厚 0.6m，重力 0.5 倍，中心 `(-170, 0)`。
- 顶部是 x16 帽子（宽 3.86m、y=6.98），帽子上方坐着**发射炮台**：扫角 65°、扫描速度 42°/s、把球以 8.6 m/s 重新投出。
- 阻挡物：固定钉排 13 颗、间距 1.5m、y=−5.83；另有顶/壁阻挡球、移动阻挡球；被撞闪阵营色 0.35s。
- **浮力气泡**：每物理帧生成 3 颗，上限 400，寿命 3s，沿三条生成线上浮。**出生速度 4.0 m/s**，之后加速度 **30 m/s²**（加速度兼着「顶球推力」= `质量 × 加速度 = 0.025 × 30 = 0.75 N/颗`，最轻的球重 0.49 N）。**刚体无阻尼**（`drag = 0`，第 129 轮用户要求），所以用**速度上限 8 m/s** 兜底（否则一路加速到 27 m/s 会穿模）——0.13s 加到 8 m/s 后匀速，通道内到顶 ≈1.53s、稳态 ≈229 颗。原文 `vel += [0, 0.1]`（换算 3 m/s²）保留为 `BuoyancyBubbleFreeRiseAccel`，仅作参考 —— 那个量级顶不住球。加速度加在**刚体当前速度**上（第 126 轮）——被大球挡住时泡泡真的会被压慢/压下去，分开后从那个速度继续累计，不会被重置。横向照抄原文 `pos(0) > -6 ? vel -= [0.8,0] : pos(0) < -8 ? vel += [0.8,0]`：**死带 ±3.3m**（= 通道半宽 × 0.75，原文 [-8,-6] 只有 2m 是相对原版窄通道的，照搬会把泡泡挤成一条柱子），出带就每步 `∓0.8`（= 24 m/s²）硬推回，**无阻尼**。**超界直接回收，不反弹**；泡泡之间不互撞（`BubbleLayer`）。

### 2.2 乘法门

| 项 | 当前值 |
|---|---|
| 普通门 | 左右两组、**每组 5 块**，倍率 `{2, 8, 4, 8, 2}`，宽度 `{1.40, 0.81, 1.13, 0.81, 1.45}` m |
| x16 门 | 单独的「特殊门」，倍率 16 |
| 碰撞 | 门是**实心碰撞体**（球会被弹开，不是穿过去） |
| 方向判定 | 普通门**只认从上方撞下来**；x16 **只认从下往上顶**（`GateTopHitNormalMin = 0.35`） |
| 冷却 | 同一门 0.6s（`GateCooldown`）；`GateOncePerBall = false` → 离开再碰可**反复倍增** |
| 结算 | `Value ×= 倍率` → `Owner.GainTotal += 增量` → 门 `PassCount++`、闪阵营色 → **立刻传回发射炮台重投** |

> 增倍球数值**只增不减**；缩小的是显示半径（`×0.99995`/tick），不是数值。

### 2.3 武器栏 8 格（第 111 轮定稿）

分界（示意图 px）：`0 | 75.9 | 122.8 | 205.3 | 292.9 | 419.6 | 463.4 | 560 | 657`

| 格 | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| 默认内容 | 直射 | **x2 门** | 散射 | 齐射 | 护盾 | **x2 门** | 大球 | **吸血球** |

- `Cfg.CiwsEnabled = true` 时第 8 格换成**近防炮**（二选一）；**当前为 `false`，所以第 8 格是吸血球**。
- 落格判定：`edges[i] ≤ x < edges[i+1]`；部分格（x2 门 / 齐射）还要求**球的底边**沉过 `WeaponSlotTriggerDrop(格)` 才接受。
- **x2 格是乘法门不是武器**：球不消耗，`Value ×= 2` 后重投，可反复吃。
- 其余格：一颗球只选一次，把整颗球的数值 `payload` 换效果，然后 `Value = 1` 重投：

| 格 | 效果 |
|---|---|
| 直射 / 散射 | 切 `TurretMode`，`Bullets += payload` |
| 护盾 | `Shield += payload` |
| 大球 | 从炮台位置生一颗同数值大球 |
| 吸血球 | 同大球，多一个 `IsVampire` 标记 |
| 齐射 | **512 发**、±30° 扇形，每发 `payload/512 + 1`，每帧撒 32 发分十几帧放完 |
| 近防炮 | 从炮台打出一颗不参与物理的小球，碰到**非己方领土**才展开（默认关闭） |

- 计数器挂在**武器区**上（`WeaponZone.PickCount`），和乘法门的 `PassCount` 一个机制。
- ⚠️ `GameWorld.FireWeapon`（「选武器时另打 12 发」）**没有任何调用点，是死代码**；现行选武器只结算上表的 payload 效果，然后数值归 1 重投。

---

## 3. 发射与弹幕

**主模型（散射 / 近防瞄准）**：按「转过的角度」发弹，不是按时间。

- `BulletAngleStepDeg = 0.025°` → 每转 0.025° 发 1 发；转速 28°/s 时约 1120 发/秒/炮台。
- 一帧最多 **64 发**（`BulletMaxPerFrame`）；近防锁敌时炮管 420°/s 猛转，只把「正常自转」那一份计入角度累加器（≤ 转速 × dt × 1.5），避免弹流被放大 5 倍。
- 炮台几乎不转（<0.01°）时走时间兜底 **800 发/秒**，方向都是当前朝向。
- 一批子弹的角度**摊在这一帧扫过的那段弧上**，形成细扇形而不是叠成一条线。

**直射模式**（在武器栏选出来）：炮台停转，`DirectFireRate = 220` 发/秒 × `FireRateScale()`，方向 ±1° 抖；弹药打完后恢复自转。

| 项 | 当前值 |
|---|---|
| 单发数值 | `max(阵营总子弹数 / 32768 + 1, 1)` |
| 散弹解封 | 开局前 **480 秒（8 分钟）** 单发封顶 **192**；之后不封顶 |
| 扣费 | 一帧发 n 发就扣 `n × 单发数值`（`SpendShots`），保证「文字掉的值 = 真送出去的值」 |
| 初速 | 3.0 m/s，每颗 ±1.5% 抖动；速度上限 12 m/s |
| 寿命 / 反弹 | 都不限（`BulletLifetime=0`、`BulletMaxBounces=int.MaxValue`）；撞地图边界**反射** |
| 子弹推力 | `dv = 2.5e-6 × 子弹剩余数值 × 2×1M/(球数值+1M)` 的冲量传给球；**己方子弹打己方引力球不给推力** |

> ⚠️ `Turret.TryDirect / TryScatter / TryVolley` 以及它们依赖的 `BulletsPerVolleyShot`、`DirectBaseFireRate`、`ScatterBaseFireRate`、`VolleyInterval` **是死代码，没有任何调用点**（`Update` 只走 `StepFiring`）；`GameWorld.FireWeapon` 同理。别照着这几个常量理解现行规则。

---

## 4. 子弹、涂地与扣费

- 子弹**不是物理刚体**（第 29 轮去物理化）：`BulletPool` 在**一个 Update** 里按渲染帧统一推进，48×48 均匀网格做近邻配对，`Physics2D` 里子弹之间互不碰撞。
- 涂地 = 把这一帧 `PrevPos → Pos` 的线段**逐格标色**（`Battlefield.PaintLine`，`BulletPaintCells=1` 即 3 格宽），不是盖圆印章；线段两端都在自家领土时整段跳过。
- **账要平**：先算 `afford = PaintBudget(剩余数值)`（除以单价、夹进 int 范围），地图**最多只涂 afford 格**；实际涂 `painted` 格就扣 `painted × 1` 点。额度只够涂到线段中间时，子弹被收回到那个位置再消失——不会「死了还往前冲」。
- `PaintChargesBulletStock = false`：涂地**不再重复扣阵营子弹数**（发射时已经扣过）。
- 大球涂地：`BigBallPaintChargesOwnValue = true`（球身上的数字往下掉）、`BigBallPaintChargesFactionStock = false`。

**子弹撞到东西**（`Bullet.HitBigBall` / `BulletPool.ResolveTurrets`）：

| 撞到 | 规则 |
|---|---|
| 己方大球 `< 100M` | 数值并进大球（`Value += 剩余值`），子弹消失 |
| 己方引力球 `≥ 100M` | **30% 吸收**（同上）/ **70% 反弹**（`GravityBallAbsorbChance = 0.30`），不再必反弹 |
| 敌方大球 | 大球扣子弹剩余数值，子弹消失 |
| 敌方炮台（有盾） | 见 §6 羽化规则 |
| 敌方炮台（无盾，本体） | `HitBody()` → 炮台死亡，子弹消失 |
| 敌方子弹 | 同一套碰撞规则，按剩余数值结算 |

---

## 5. 大球

### 5.1 半径、质量、显示

| 对象 | 公式（`LogV` = 自然对数，`UseLog10=false`） |
|---|---|
| 护盾半径 | 目标 `ln(S)/18 + 0.1`，每步向目标收 1/5（已改成与帧率无关的 `1-(1-0.2)^(dt×30)`） |
| 大球半径 | `(ln(V)/8.3 − 0.5) × 0.95` |
| 增倍球半径 | `ln(V)/28 + 0.05`（**没有显示下限**） |
| 中子星半径 | 朝 `ln(√V)/30 − 0.04` 指数逼近（1B 时约 0.42m，看着像坍缩） |
| 球质量 | 半径 × 1.6，下限 0.10；大球质量另有 `BigBallMaxMass = 1e9` 夹顶 |

阈值：`BigBallMinValue = 100`（收尸线）、`GravityBallThreshold = 100M`（引力球）、`NeutronStarThreshold = 1B`（中子星）。

### 5.2 碰撞（只由 `InstanceID` 小的一方结算一次，`ClashCooldown=0.05s`）

| 情形 | 规则 |
|---|---|
| **不同阵营** | **双方各扣 `min(a,b) × 0.25`**（`BigBallClashLossFraction`）→ 掉到 100 以下收掉 |
| 同阵营、双方都 `<100M` | 只给物理推力，不转移数值 |
| 同阵营、一颗是引力球 | 小球把**自身的 0.75** 转给引力球 |
| 同阵营、两颗都是引力球 | 小的把 `min(a,b) × 0.75` 转给大的 |
| **吸血球**（第 111 轮） | 敌方 `< 自身 × 0.5` → 敌方掉**自身 50%**，自己等量加上（**自己不扣血**）；否则退回普通 0.25 规则。双向都判 |
| **吸血球 vs 吸血球**（第 124 轮） | **一律按普通大球扣血**（各扣 `min(a,b) × 1/4`），**互不吸血** —— 否则大吸血球会单方面吸干小吸血球 |

> **吸血的作用范围（第 112 轮）**：只在 `BigBall.OnCollisionEnter2D → ResolveCrossFaction`，也就是**大球撞大球**的物理碰撞里生效。`Bullet.HitBigBall` 刻意完全不读 `IsVampire` —— 子弹打吸血球和打普通大球一模一样（己方并数值 / 引力球掷骰，敌方扣数值），**子弹没有吸血机制**。`SmokeTest` 有一条断言守着这条不变式。
>
> **吸血球的持续特效（第 117 轮，按用户给的 Algodoo poststep 原文）**：`addcircle` + `addpen` 整段放在大球的 poststep 里，所以**每物理帧生成一套**「会自己走螺旋的圆 + 跟着盖章的笔」（第 130 轮起改成**每 2 帧一套**，`VampireFxSpawnPerFrame = 0.5`，同时在场约 42 套）：
> - 圆：`radius := 0.01`、`colorhsva := [0,0,0.2,1]` 深灰，**圆和笔同色**（没有白圆头）、`zdepth := zdepth + 1`；
> - 每步 `pos = bball.pos + [sin(_angle), cos(_angle)] * _distance`，`_angle *= 1.02`、`_distance *= 0.98`；
> - `_angle := rand.uniform01 * 2π` 每套独立随机，`_distance := radius + 0.2`（球外 20% 起转）；
> - `timetolive := 1.7` 一套活 1.7 秒；笔 `size := tx.radius * 2`、`fadetime := 0.2` 笔迹点 0.2 秒淡出、`zdepth := tx.zdepth + 1`。
>
> 实现：`TrailRenderer` = addpen（按时间采样 + alpha 渐隐，和子弹拖尾同一套），圆用 `SpriteRenderer`。**尾巴是圆自己走出来的运动轨迹**，不是预先画好的线段；每套有独立螺旋状态，所以铺满球面。`_pdsz <= 0` 时整套立刻收掉。
>
> ⚠️ 沿革（前几轮都错在把拖尾当**静态线段**）：114 轮所有特效共用一条螺旋 → 两簇刷子；115 轮删掉螺旋改随机静态折线 → 涂鸦；116 轮给折线加渐细渐淡 → 仍不像拖尾。117 轮按原文改回「每帧一个会动的圆 + 笔」。
>
> 尺度换算：`radius := 0.01` → **固定** `VampireFxPenRadius = 0.025m`（半径，直径 0.05m，略细于子弹 `BulletDotRadius = 0.027`；第 120 轮按用户要求从「相对球半径」改成固定值，只有球特别小时按球半径 12% 夹）；`radius + 0.2` → `VampireFxOrbitStartFrac = 1.2`。`×1.02` / `×0.98` 按 Algodoo 30fps 换算成与帧率无关。上限 160 套（1.7s × 60fps ≈ 102）。
>
> 第 118 轮修正：`VampireFxTrailMinVertex = 0`（addpen 每帧必盖章，否则螺旋上位移小的区段不采样、拖尾凭空消失）；`VampireFxAngleMin = 0.35 rad`（避开起始角趋近 0 时乘法增长让整套几乎不动）。第 120 轮：笔迹宽度改成**固定** `VampireFxTrailWidth = 0.02m`（≈1.5px，约子弹直径的 1/3）。
>
> 另有两个「看着像消失」但属正常：① 粒子从球外 20% 起转，深灰在近黑背景上对比低，刚生成一小段不易看见（可调 `VampireFxOrbitStartFrac → 1.0`）；② 球中央的数值文字是 **IMGUI**，永远画在最上层，粒子飘到字后面会被遮住。
>
> ⚠️ **渲染层级（第 119 轮）**：`HudOverlay.DrawBigBallOverlay` 会在 IMGUI 里把大球本体重画一遍（为了「大球盖住炮台文字」，第 33 轮规格）。IMGUI 压在所有 Sprite 之上，所以**吸血球被排除在这层重绘之外**（`!b.IsVampire`），否则球面内的 `SpriteRenderer`/`TrailRenderer` 特效会被整块盖掉，只剩球外一圈。代价：吸血球压到炮台文字时文字会盖住球。

### 5.3 引力与中子星

- 引力球 / 中子星吸引**所有阵营的子弹**，也吸引**其他大球**；引力球之间也互相吸。
- 真引力公式：`a = GravityOnBigBall(100) × GravityValueFactor(引力球数值) / 距离²`，**与被吸球自己的数值无关**（`GravityTargetValuePower = 0`）。
- `GravityValueFactor`：以 100M 为参考线性增长（`GravityValuePower=1`），超过 `NeutronStarGravityKnee=3` 后指数压平到 0.35 倍。
- 安全阀：大球 `MaxAccel=520`、`MaxApproachSpeed=6`、`MaxDeltaVPerTick=2.5`；子弹 `BulletMaxSpeed=12`、`GravityMaxRange=14`。
- 子弹侧引力强度 `GravityOnBullet = 36`（**恒定，不随速度² 变**，故意让子弹到速度上限后基本不拐弯）。
- 大球速度上限 6 m/s；低于 0.08 m/s 持续 1.2s 判「卡住」，朝离开最近墙的方向轻踢 0.35 m/s。

### 5.4 尾流自走

- 数值 `> 1M` 的大球每 1/90 秒往速度反方向喷 1 发**数值 4**、初速 1.7 m/s 的子弹，反冲提速（`BigBallJetSpeedGain = 0.0037`），并**从自身数值里扣 4**。
- 速度超过 `BigBallJetBoostMaxSpeed = 0.25 m/s` 就不喷；尾流方向固定（不再随机左右抖）。

---

## 6. 护盾与炮台死亡

**护盾半径**按 §5.1 缓动；护盾纹理与判定共用 `ShieldFeatherBand = 0.35` 的羽化带，`FeatherFactor` 从内侧 1 线性降到最外沿 0。

**子弹打敌方护盾**（`Bullet.ResolveEnemyShield`，用**剩余数值** `RemainingValue`，不是发射原始值）：

- 有效数值 `hit = 剩余值 × 羽化系数`。
- `hit > 护盾` → 护盾归零、子弹扣掉整个护盾值并**反弹**继续飞。
- `hit ≤ 护盾` → 护盾扣 `hit`，子弹带着剩下的数值继续飞（剩下 ≤0 才消失）。

**大球撞敌方护盾**（`BigBall.ResolveShieldHit`，三条）：

| 条件 | 结果 |
|---|---|
| `p/8 > s` | **直接破盾、不反弹**；速度 ×0.05 继续前冲；大球扣掉整个护盾值 |
| `p < s` | 反弹；护盾与大球各扣 `p × 3/4` |
| `p ≥ s`（但不到 8 倍） | 反弹；**护盾剩 1/4**，大球扣 `s × 3/4` |

**死亡**：护盾 >0 时只有护盾判定框生效；归零后判定框立刻关掉，敌方大球或子弹碰到**本体判定框**才 `HitBody() → Die()`（`TurretDiesOnBodyHit=true`）。死亡时不再发放增倍球；场上子弹数显示 0、三行名称隐藏。

---

## 7. 亡语 / 复活 / 胜负

**亡语**（`GameWorld.OnTurretDied`，1:1 换算，不合并）：

- 每颗存活的增倍球**各自**变成一颗独立大球（数值 ×`DeathrattleBallValueScale=1.0`），原地放爆炸特效；
- 阵营剩余**子弹单独**变成一颗大球（×`DeathrattleBulletValueScale=1.0`）；
- `Bullets = 0`、`Shield = 0`。
- 为避免一帧在同一点生十几颗把求解器挤穿墙：大球**排队、每帧放 3 颗**，沿黄金角向日葵螺旋散开（起始半径 0.45m + 0.42√k），并夹在地图内留 0.6m 边距。

**复活**：死亡 **60 秒**后，己方任意一颗大球被 `HomingForce=6` 牵引回到距出生点 `HomeRadius(0.7m) + 球半径` 以内即触发：

- 增倍球数 += `floor(ln(大球值) × 0.8 − 4)`，每颗数值 1；
- `Bullets = 大球值 × 0.1`、`Shield = 大球值 × 0.05`（`=` **覆盖赋值**，不是累加）；
- **大球留在场上**，数值变成原来的 **0.6**（`RespawnConsumesBall = false`）；
- 放白色冲击波（1.1s、半径 ×8）+ 阵营色闪光。

**胜负**：某家占领像素 ≥ 98% 总像素即记 `WinnerIndex`；`LeadingFaction()` 用于 HUD 显示当前领先者。

---

## 8. 数值显示与阵营

- `BigNumber` 三套格式：子弹数 `Grouped`（`2531742 → 2 531 742`，三位一组可换行堆叠）、大球/护盾 `Suffixed`（`1,0M` / `269M`，尾数 <10 保留一位小数）、增倍球 `SuffixedRounded`（`33554432 → 34M`）。分隔符：组间空格，小数逗号。
- 阵营色用 **HSV** 定义（`Palette.FactionHsv`，S/V 允许 >1 不夹到 1，四家都用黑字）；2/4/8/16 家各有独立表。设置面板改过的颜色持久化，`FactionSet` 出厂值一改，旧覆盖自动作废（第 110 轮）。
- 总值计数条（`GainTotal`）：每过一次乘法门 / 武器区 x2 格，累加「翻倍后 − 翻倍前」（`1→4` 记 `+3`）。

---

## 9. 操作、开关与性能

| 键 | 作用 |
|---|---|
| `1` `2` `3` `4` | 聚焦左栏底部武器栏对应阵营 |
| `K` | 直接打死当前聚焦阵营的炮台（验证亡语 + 复活） |
| `B` | 刷一颗 999M 调试大球（引力球但未到中子星） |
| `V` | 刷一颗 **99M 吸血球**（当前聚焦阵营、战场中央 ±0.6m），用来单独验证螺旋特效和吸血碰撞 |
| `L` | 给**每个阵营**在左栏各补一颗**增倍球**（数值 1，从炮口按当前朝向打出） |
| `F1` | 性能统计面板（分项 `frame/pool/flush/hud/view`） |
| `F2` | 录屏用「定步长」模式开关 |
| `-` / `+` | 模拟速度 ×0.5 / ×2（0.125× ~ 8×） |
| 滚轮 / `WASD` / `R` | 右侧战场缩放 / 平移 / 复位 |
| 空格 / `Tab` | 暂停 / 设置面板（**当前被总开关关掉了，见下**） |

**当前处于关闭状态的功能**：

- `Cfg.EnableSettingsPanel = false` → 设置面板**连物体都不创建**，`Tab` / 空格 / 换阵营数 / 换颜色全部不可用。要用得先把这个常量改 `true`。
- `Cfg.ShowSettingsButton = false`（只藏按钮；和上面是两级开关）。
- `Cfg.CiwsEnabled = false` → 武器栏第 8 格是吸血球，近防炮功能整体不进局。
- `Cfg.CaptureModeAtStart = false` → 默认实时模式，录屏按 `F2` 开定步长（60 步/秒，`FixedTimestep=1/60`）。
- `Cfg.VignetteStrength = 0`、`MapGridEvery = 0`、`BrushEdgeNoise = 0`、`TerritoryEdgeAA = 0` → 暗角、网格、领土毛边、边缘抗锯齿都关掉了。
- `MultiplierParticleThreshold = double.MaxValue` → 增倍球特效拖尾停用。
- `BulletTrailUsesTrailRenderer = true` → 用 Unity `TrailRenderer`（不是合批网格）。

**性能骨架**：地图是单张 `Texture2D`（1 个 DrawCall），按 64×64 脏块增量上传、`MapFlushIntervalFrames=1`（每帧传，节奏平）、单次最多 96 块；子弹对象池预热 4096、每次扩容 128、空闲保留 12000 不 Destroy；子弹打子弹配对一帧最多 20000 次；`Physics2D.bounceThreshold=0.05`、物理迭代 6/2、`Time.maximumDeltaTime=0.05`；目标帧率 60、关 vSync / 阴影 / 抗锯齿。

---

## 10. 已作废的旧规则（别再照 README 前半段抄）

| README 前 320 行的说法 | 现行事实 |
|---|---|
| 地图世界尺寸 **96×96m** | **14m**（第 7 轮按录像反推） |
| `ShieldVisualScale = 10` / `BigBallVisualScale` | 第 7 轮**整个删掉**（=1），直接按公式 |
| 「一颗子弹数值 = 4」、`Cfg.BulletDamage = 4` | 单发 = `总子弹数/32768 + 1`；开局约 4.05，会随库存和解封时间变 |
| 己方球 `<100M`：加血 **+ 微弱推力**；`≥100M`：原路反射 + 推力 | `<100M` 直接吸收数值；`≥100M` 是 **30% 吸收 / 70% 反弹**（第 92 轮），推力改由子弹数值 × 大小系数算 |
| 敌方大球压进出生点 **6m** 判炮台死亡 | 现在是**本体判定框**命中即死；复活判定用 `HomeRadius = 0.7m` |
| 亡语 / 复活「大球数值变 60%」写在待确认里 | 已实现（`RespawnBallValueKeep=0.6`，大球**保留**在场上） |
| 「称号 / 名称整个去掉」 | 第 26 轮加回三行名称 |
| `MaxLiveBullets = 6000` | 第 29 轮**删除子弹数量上限** |
| 代码结构表里的 `BufferBubble.cs` / `Launcher.cs` / `MovingBlocker.cs` | `Launcher` 仍在用；`BufferBubble` / `MovingBlocker` 已停用成 `.cs.bak`；新增 `BuoyancyZone` / `GravityFx` / `TurretFx` / `VampireFx` / `ZoneProjection` / `Prof` 等 |
| 第 173 行「三条碰撞比例统一 **3/4**」 | **不同阵营已改回 1/4**（第 99 轮）；同阵营引力吸收两条仍是 3/4。`Cfg.cs:1144` 有注释专门警告别顺手统一回去 |
| 第 3~7 轮的布局（四角炮台在增倍区、7 格武器栏、x16 白框） | 第 9 轮按 657×1111 示意图**整体重建**；现在是 8 格武器栏 |
| 「乘倍后立刻传送回发射炮台」 | 仍然成立，但重投点由 `Launcher` 位置决定 |
| 第 56 轮「拖尾合批，默认 false」 | 代码现值 `BulletTrailUsesTrailRenderer = true` |

**README 里明确撤回过的方案**（别再捡回来）：第 60 轮撤回第 59 轮「一批摊在扫过的弧上」、第 61 轮撤回第 60 轮 `BulletEmissionBurst=5`、第 65 轮撤回第 64 轮「少而大」、第 108 轮撤回「气泡超界反弹」、第 103 轮作废旧引力公式 `GravityOnBigBall=0.05 + log10(1+被吸球数值)`。

**代码注释本身也有几处过时，别被带偏**：

- `Cfg.cs:188` 说「开局 10 万子弹时 `BulletDamage = 196.3`」→ 实际 `100000/32768+1 ≈ 4.05`（那是旧除数 512 时代的数）。
- `Cfg.cs:58` 写「每 80 秒」是对的，但 `GameWorld.cs:128` 和 `Faction.cs:64` 的注释还写「每 60 秒」。
- `GameWorld.cs:146` 说「领土 30Hz 更新」→ `MapFlushIntervalFrames = 1`，是每帧。
- `MultiplierZone.cs:146` 说「左右各 4 块乘法门」→ `GateMultipliers` 有 5 个，实际是**各 5 块**。
- `Faction.cs:106` 提到 `Cfg.ShieldVisualScale` 和 96m 地图 → 该常量和那个尺度都已删除。
- `Turret.cs:213` 还写着「射速按 1024 发/物理帧」→ 现行是「按单位角度一发」（第 69 轮）。
- `BigBall.cs:605` 注释写「第 92 轮起 = 1/4」→ 常量确实是 0.25，但那是**第 99 轮**从 3/4 改回来的。

---

## 11. 关键常量速查（改参数先看这里）

| 常量 | 值 | 含义 |
|---|---|---|
| `StartBullets` / `StartShield` | `100000` / `15388608` | 开局资源 |
| `MultiplierBallGrantInterval` | `80f` | 补球间隔（秒） |
| `MapTexels` / `MapWorldSize` / `PixelCaptureCost` | `1024` / `14f` / `1.0` | 地图规模与单价 |
| `GateMultipliers` / `GateWidths` | `{2,8,4,8,2}` / `{1.40,0.81,1.13,0.81,1.45}` | 两组乘法门 |
| `X16Multiplier` / `GateCooldown` | `16` / `0.6f` | x16 门与冷却 |
| `BulletValueDivisor` / `ShotValueCap` / `ShotValueUnlockTime` | `32768` / `192` / `480` | 单发数值与散弹解封 |
| `BulletAngleStepDeg` / `BulletMaxPerFrame` / `BulletIdleFireRate` | `0.025f` / `64` / `800f` | 按角度发射模型 |
| `BulletSpeed` / `BulletMaxSpeed` / `BulletDotRadius` | `3.0f` / `12f` / `0.027f` | 子弹运动 |
| `BulletPaintCells` / `PaintBudget` | `1` / `值/单价`（夹 int） | 涂地宽度与预算 |
| `ShieldRadiusLogDivisor` / `ShieldFeatherBand` | `18f` / `0.35f` | 护盾半径与羽化 |
| `BigBallRadiusLogDivisor` / `BigBallRadiusScale` | `8.3f` / `0.95f` | 大球半径 |
| `MultiplierBallRadiusLogDivisor` | `28f` | 增倍球半径 |
| `BigBallClashLossFraction` | `0.25` | **不同阵营**碰撞扣血 |
| `GravityBallAbsorbFraction` / `GravityBallMutualFraction` | `0.75` / `0.75` | 同阵营数值转移 |
| `VampireDrainThreshold` / `VampireDrainFraction` | `0.5` / `0.5` | 吸血球碰撞门槛 / 吸取比例 |
| `VampireFxSpawnPerFrame` / `VampireFxMaxPens` | `0.5` / `160` | 每帧生成几套「圆 + 笔」（0.5 = 每 2 帧一套） / 在场上限 |
| `VampireFxLife` / `VampireFxTrailTime` | `1.7` / `0.2` | `timetolive`（一套寿命）/ `fadetime`（笔迹淡出），秒 |
| `VampireFxPenRadius` / `VampireFxOrbitStartFrac` | `0.025` / `1.2` | 圆半径（米，固定）/ 螺旋起始距离（相对球半径） |
| `VampireFxAngleGainPerStep` / `DistanceGainPerStep` / `AlgodooFps` | `1.02` / `0.98` / `30` | 每步角度倍率 / 距离倍率 / 帧率换算基准 |
| `VampireFxTrailMinVertex` / `VampireFxAngleMin` | `0` / `0.35` | 每帧必采样（防断尾）/ 起始角下限（防退化） |
| `VampireFxTrailWidth` | `0.02` | 笔迹宽度（米，固定，≈1.5px，约子弹的 1/3） |
| `VampireFxColor` / `SortingOrder` / `TrailSortingOrder` | `[0,0,0.2,1]` / `62` / `63` | 圆笔同色深灰 / 圆图层 / 笔图层 |
| `GravityBallThreshold` / `NeutronStarThreshold` / `BigBallMinValue` | `100M` / `1B` / `100` | 三条阈值 |
| `DebugBallValue` / `DebugVampireBallValue` | `999M` / `99M` | 调试键 `B` / `V` 刷出的球（99M 刻意低于引力门槛） |
| `GravityOnBullet` / `GravityOnBigBall` / `GravityMaxRange` | `36f` / `100f` / `14f` | 引力 |
| `BigBallJetRate` / `BigBallJetBulletValue` / `BigBallJetSpeedGain` | `90f` / `4` / `0.0037f` | 尾流自走 |
| `DeathrattleSpawnPerFrame` / `RespawnDelay` / `RespawnBallValueKeep` | `3` / `60f` / `0.6` | 亡语与复活 |
| `RespawnBulletsFraction` / `RespawnShieldFraction` | `0.1` / `0.05` | 复活资源（覆盖赋值） |
| `HomeRadius` / `BigBallMaxSpeed` | `0.7f` / `6.0f` | 复活判定与大球限速 |
| `BuoyancyBubbleSpawnPerFrame` / `Max` / `Life` | `3f` / `400` / `3f` | 浮力气泡 |
| `BuoyancyBubbleRiseStepGain` / `AlgodooFps` / `RiseAccel` | `0.1` / `30` / `3.0` | 原文 `vel += [0, 0.1]` / 步频 / 换算后的加速度（m/s²） |
| `BuoyancyBubbleRiseSpeed` / `RiseSpeedMax` | `4.0` / `0` | 出生速度 / 上限（**0 = 不封顶**，原文语义） |
| `BuoyancyBubbleLateralBand` / `PushBackStepGain` / `PushBack` | `3.3`（半宽×0.75） / `0.8` / `24.0` | 横向死带半宽 / 每步推回量 / 换算后加速度（m/s²） |
| `BuoyancyBubbleSideDamp` | `1.0` | 横向阻尼（**1 = 无阻尼**，原文语义） |
| `BuoyancyBubbleDrag` / `RiseSpeedMax` | `0` / `8.0` | **无阻尼** + 速度上限 8 m/s 兜底 |
| `BuoyancyBubbleRiseSpeed` / `RiseAccel` / `FreeRiseAccel` | `4.0` / `30` / `3.0` | 出生速度 / 实际加速度（决定推力）/ 原文换算值（仅参考） |
| `FixedTimestep` / `CaptureFramerate` / `TargetFrameRate` | `1/60` / `60` / `60` | 时间步长 |
| `EnableSettingsPanel` / `CiwsEnabled` / `CaptureModeAtStart` | `false` / `false` / `false` | 三个总开关 |
