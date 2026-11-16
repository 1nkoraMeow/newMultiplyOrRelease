using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 全局可调参数。单位统一「米 / 秒」。
    ///
    /// 右栏（战场）：1024×1024 涂色地图 + 四角炮台/护盾 + 大球 + 子弹。
    /// 左栏（增倍区）：增倍机器（x16 阻块和发射炮台 → 两侧乘法门 → 移动阻挡球 → 武器栏）。
    /// 灰色的东西全部是可碰撞的阻挡物。
    /// </summary>
    public static class Cfg
    {
        // ─────────────────────────────────────────────────────────────
        // 一、对局规模
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 阵营数。★ 运行时可改（设置面板 → 阵营数，值存在 `GameSettings.FactionCount`），
        /// 改完要**重开一局** —— 战场像素数组、炮台、领土都是按这个数分配的。
        /// </summary>
        public static int FactionCount { get { return FactionSet.Count; } }

        // ─────────────────────────────────────────────────────────────
        // 一·五、设置面板（阵营数 / 阵营颜色 / 暂停继续）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ★ 设置面板总开关。`false` = 连右上角那个入口按钮都不建，
        /// 整个功能（含键盘 Tab / 空格）都不存在，HUD 布局一点不受影响。
        /// </summary>
        public const bool EnableSettingsPanel = true;
        /// <summary>
        /// 右上角那个「设置」**入口按钮**要不要画。
        ///   和 `EnableSettingsPanel` 的区别：
        ///     · `EnableSettingsPanel = false` → 整个功能都没有（物体不建、按钮和键盘都不存在）；
        ///     · `ShowSettingsButton = false` → **只藏按钮**，键盘（`SettingsPanelKey`）照样能开面板。
        /// ★ `static readonly` 而不是 `const`：`const false` 会让 `DrawLauncher` 里那个 `if`
        ///   变成 unreachable，白多一条警告（和 `GameSettings.Persist` 同一个理由）。
        /// </summary>
        public static readonly bool ShowSettingsButton = true;
        /// <summary>开关设置面板的键。手机上点右上角按钮。</summary>
        public static readonly KeyCode SettingsPanelKey = KeyCode.Tab;
        /// <summary>暂停 / 继续的快捷键。</summary>
        public static readonly KeyCode PauseKey = KeyCode.Space;
        /// <summary>设置面板里「色相」滑条的范围（度）。</summary>
        public const float ColorHueMax = 360f;
        /// <summary>S / V 滑条的上限 —— 阵营表里有 S = 1.1、V = 1.5 的「溢出亮色」，别夹到 1。</summary>
        public const float ColorSvMax = 1.6f;

        // ─────────────────────────────────────────────────────────────
        // 二、开局资源
        // ─────────────────────────────────────────────────────────────

        public const double StartBullets = 100000.0;      // 开局弹药
        public const double StartShield = 15388608.0;     // 开局护盾数值
        public const int StartMultiplierBalls = 2;        // 开局增倍球数量
        public const float MultiplierBallGrantInterval = 80f;   // 每 80 秒补一颗增倍球
        public const double NewBallStartValue = 1.0;

        /// <summary>
        /// 开局涂抹自身周围领土的半径（世界单位）。
        /// 16 家版相邻炮台只隔 ≈3.45m，1.8m 会互相重叠 → 缩到 0.9m。
        /// ★ 属性而不是 `static readonly`：阵营数现在运行时可改，这个值得跟着变。
        /// </summary>
        public static float StartTerritoryRadius { get { return FactionSet.Count >= 16 ? 0.9f : 1.8f; } }
        /// <summary>
        /// 笔刷边缘随机抖动（相对刷子半径）。**0 = 正圆** —— 领土要一格一格清楚，
        /// 抖动会让边界像毛茸茸的画笔。
        /// </summary>
        public const float BrushEdgeNoise = 0f;

        /// <summary>领土花纹的周期（像素）。注意：花纹本身已按规格删除。</summary>
        public const int TerritoryPatternStep = 8;
        /// <summary>
        /// 领土边缘抗锯齿宽度（像素）。**0 = 关**。
        /// 开过 1.6，结果每个笔刷印章边缘都和旧颜色混一圈，像盖小圆章。
        /// </summary>
        public const float TerritoryEdgeAA = 0f;

        // ─────────────────────────────────────────────────────────────
        // 三、战场（右栏，1024×1024 像素，单像素数值 1）
        // ─────────────────────────────────────────────────────────────

        public const int MapTexels = 1024;
        public const float MapWorldSize = 14f;   // ★ 对着录像反推出来的：用 14m 时护盾和大球的公式同时对上录像
        public const float TexelsPerUnit = MapTexels / MapWorldSize;
        /// 规格（用户）：**地图上不要网格线**（"我不需要网格，建议都去掉"）——
        /// 设成 0 = 关；>= 2 才会去画。中立区和领土上的网格都用这一个开关。
        public const int MapGridEvery = 0;

        public const double PixelCaptureCost = 1.0;   // 规格：一个像素扣 1 点数值（曾经是 8）

        /// <summary>
        /// 「这点数值还买得起几格」。**所有**拿数值换像素的地方都必须走这里。
        /// 必须夹进 int 范围：Unity 的 double→int 溢出会得到 `int.MinValue`（负数），
        /// 于是大球一格都涂不上、子弹原地冻住（数值超过 21.4 亿时）。
        /// </summary>
        public static int PaintBudget(double value)
        {
            double n = value / PixelCaptureCost;
            if (double.IsNaN(n) || n <= 0d) return 0;
            return (int)System.Math.Min(n, int.MaxValue / 4d);
        }
        /// <summary>
        /// 涂地时**另外**再扣一次阵营子弹数。**关**：
        /// 数值在发射时已经扣过一遍，再按像素扣就是重复收费（还是固定值，小存货会被瞬间清空）。
        /// </summary>
        public static readonly bool PaintChargesBulletStock = false;
        /// <summary>装满整张 1024² 地图一共需要多少数值。</summary>
        public static double CostToFillMap() { return (double)MapTexels * (double)MapTexels * PixelCaptureCost; }

        /// <summary>
        /// 子弹涂地的宽度：沿弹道每一格往外再扩几格。
        /// 0 = 1 格宽（最细），1 = 3 格宽，以此类推。
        /// </summary>
        public const int BulletPaintCells = 1;
        /// <summary>大球的黑色描边宽度（相对半径）。</summary>
        public const float BigBallOutlineFactor = 0.02f;   // 规格：描边要很细
        public const double PixelCaptureCostFraction = 0.0;
        /// <summary>大球涂地是否扣阵营子弹数。**关**：同上，别重复收费。</summary>
        public static readonly bool BigBallPaintChargesFactionStock = false;
        /// <summary>大球涂地是否扣**自己**的数值（球上的数字往下掉，这是看得见的那一路）。</summary>
        public static readonly bool BigBallPaintChargesOwnValue = true;

        /// <summary>炮台在战场四角的位置比例（越小越贴角）。</summary>
        public const float HomeCornerFraction = 0.13f;
        public const float HomeMarkerRadius = 1.0f;

        // ─────────────────────────────────────────────────────────────
        // 四、炮台（现在在**右栏战场**的四角）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 护盾半径的显示倍数（视频里护盾圈约占地图宽 12%）。
        /// ★ 属性而不是 `static readonly`：阵营数运行时可改，这个值得跟着变。
        /// </summary>
        public static float ShieldVisualScale
        {
            get { return FactionSet.Count >= 16 ? 0.60f : 1.3f; }
        }
        /// <summary>
        /// 护盾的羽化带宽（占半径的比例）。**视觉和判定共用这一个值** ——
        /// 纹理在最外这一带渐隐，判定也在这一带按比例吃子弹，不会到半径就一刀切。
        /// </summary>
        public const float ShieldFeatherBand = 0.35f;
        /// <summary>羽化带里被吃掉的数值比例下限（0 = 最外沿完全不挡）。</summary>
        public const float ShieldFeatherMin = 0f;
        /// <summary>
        /// 护盾纹理最外圈能暗到什么程度（1 = 和中心一样亮，0 = 纯黑）。
        /// 「亮心深边」那圈深边别做成黑框：太小看着就是护盾外面套了个黑圈。
        /// </summary>
        public const float ShieldEdgeBrightness = 0.2f;
        /// <summary>
        /// 炮台本体半径（恒定，不随护盾量变化）。
        /// 它同时是死亡判定框和炮管圆头的尺寸基准；**文字字号不跟它走**
        /// （数字用 `TurretTextHeight`、名称用 `TurretNameTextHeight`）。
        /// </summary>
        public const float TurretRadius = 0.11f;
        /// <summary>炮台本体的半透明填充浓度。</summary>
        public const float TurretCoreFillAlpha = 0.35f;
        /// <summary>
        /// 护盾圈的绝对下限，免得护盾很少时圈比炮台还小。
        /// ★ 属性（阵营数运行时可改）。
        /// </summary>
        public static float ShieldMinRadius
        {
            get { return FactionSet.Count >= 16 ? 0.30f : 0.78f; }
        }

        /// <summary>炮台自转速度（度/秒）。直射且还有弹药时不转，见 `Turret`。</summary>
        public const float TurretSpinSpeed = 28f;
        /// <summary>
        /// 近防系统作用半径（世界单位）。8 家版相邻炮台只隔 ≈4.2m，
        /// 用 4m 会变成邻居互相锁着对射，所以 8 家以上收到 1.2m。
        /// ★ 属性（阵营数运行时可改）。
        /// </summary>
        public static float CiwsRange
        {
            get { return FactionSet.Count >= 8 ? 1.2f : 4.0f; }
        }
        /// <summary>近防锁定目标时，炮口转过去的速度（度/秒）。</summary>
        public const float CiwsTurnSpeed = 420f;

        // 子弹：阵营色的小圆点，后面拖着白色条纹
        public const int BulletsPerVolleyShot = 6;   // 一次齐射几发
        public const double BulletsPerShot = 1d;     // 弹药消耗按「发射次数」算，不按弹丸数
        /// <summary>默认单发数值（开局 10 万子弹时 = 196.3）。实际发射走 `BulletValueFor`。</summary>
        public static readonly double BulletDamage = BulletValueFor(100000d);
        /// <summary>每物理帧发射几发（1 = 120 发/秒/炮台）。</summary>
        public const int BulletsPerTick = 1;
        /// <summary>直射的散角（度）。</summary>
        public const float DirectSpreadDeg = 2.5f;
        /// <summary>每颗子弹初速的随机抖动（±比例）。</summary>
        public const float BulletSpeedJitter = 0.015f;
        /// <summary>
        /// 子弹速度上限（被引力球吸的时候不要无限加速）。
        ///
        /// ★★ 用户最终的规格（原话）：「**引力 12 不变，速度上限改回 36**」，确认后是 A 方案：
        ///   **速度上限 = 12，引力恒定 = 36（不再跟着速度² 变）**。
        ///   这样 36 &lt; 12² = 144，引力**只有绕圆所需的 1/4** —— 子弹到上限后基本不拐弯、
        ///   直直飞出去。这正是用户要的：**子弹不需要绕圆**。
        ///
        /// ⚠️ 所以 `GravityOnBullet` **故意不再由本值推导**。别再「顺手修」成 144，
        ///   那会让子弹重新绕起圈来（之前就是这么踩的）。
        /// </summary>
        public const float BulletMaxSpeed = 12f;
        /// <summary>
        /// 子弹撞大球时给的推力（每个单位数值）。球的**速度增量**：
        ///     dv = 本值 × 子弹数值 × [ 2 × 参考值 / (被推球数值 + 参考值) ]
        /// 方括号让「被推的球数值越大越难推动」：等于参考值时系数 = 1，更小最多 2 倍，更大趋近 0。
        /// 己方子弹打己方引力球不给推力。`0` = 关掉。
        /// </summary>
        public const float BulletPushSpeedPerValue = 0.0000025f;
        /// <summary>推力公式里的参考球数值（正好这个数值的球，系数 = 1）。</summary>
        public const double BulletPushReferenceValue = 1000000d;   // 1M
        /// <summary>齐射弹丸的半径（米）—— 和普通子弹一样，别被放大。</summary>
        public const float VolleyProjectileRadius = BulletDotRadius;
        /// <summary>子弹出膛速度。</summary>
        public const float BulletSpeed = 3.0f;
        /// <summary>
        /// 子弹寿命（秒）。**0 = 不限** —— 子弹数值没耗尽就不该消失，
        /// 只会因为涂色额度用完或飞出地图被回收。
        /// </summary>
        public const float BulletLifetime = 0f;
        /// <summary>
        /// 右栏（战斗区）大球本体的颜色：**阵营色按 HSV 调** ——
        ///   · 饱和度 × 本值（`BigBallSaturation`）
        ///   · 明度 V **+** `BigBallValueAdd`（比阵营色更亮）
        /// 大球和中子星共用这条；`HudOverlay` 里压住文字时重画的那份也必须用同一条
        /// （`Palette.ShiftHsv`），否则球压到字上的一瞬间颜色会跳一下。
        /// </summary>
        public const float BigBallSaturation = 0.7f;
        /// <summary>大球的明度 V 在阵营色基础上**加**这么多（0.3；夹到 1 为止）。</summary>
        public const float BigBallValueAdd = 0.3f;
        /// <summary>
        /// 拖尾色 = `Lerp(阵营色, 白, 本值)`。参考图里拖尾是**发白的浅色**（色相没变、饱和掉一半、明度拉满）。
        /// 0 = 原色，1 = 纯白，0.55 ≈ 参考图。
        /// </summary>
        public const float BulletTrailWhitening = 0.55f;
        /// <summary>
        /// **阵营色本身**的亮度（Rec.601）低于这个值，整条拖尾直接换成白色（黑家那种看不见的颜色）。
        /// 判的是原色、不是提亮后的颜色。0.25：黑家变白，棕家还是棕的。
        /// </summary>
        public const float BulletTrailMinLuminance = 0.25f;
        public const float BulletDotRadius = 0.027f;
        /// <summary>
        /// 子弹**圆点本体**的饱和度倍数：`Palette.ShiftHsv(阵营色, 本值, 0)` —— 0.5 = 比阵营色淡一半。
        /// 只管圆点本身；拖尾是另一套（`BulletTrailWhitening`），别混。
        /// </summary>
        public const float BulletSaturation = 0.5f;
        /// <summary>
        /// 单发子弹携带的数值 = 总子弹数 / 本值 + 1（见 `BulletValueFor`）。
        /// 它和**发射密度**是一对：密度翻倍，这个数也翻倍，「每秒泼出去的数值总量」不变。
        /// ★ 解封前那一半还受 `ShotValueCap` 限制，调的时候两个一起动。
        /// </summary>
        public const double BulletValueDivisor = 32768.0;
        public const double BulletValueBias = 1.0;
        public static double BulletValueFor(double totalBullets)
        {
            return Mathf.Max((float)(totalBullets / BulletValueDivisor + BulletValueBias), 1f);
        }

        // ── 散弹解封 ──
        /// <summary>解封时间（秒）：开局前 8 分钟单发数值封顶，之后不封顶。</summary>
        public const double ShotValueUnlockTime = 480.0;
        /// <summary>封顶值：**解封前**单发数值最多这么多。</summary>
        public const double ShotValueCap = 192.0;

        /// <summary>
        /// 单发数值的完整规则：
        ///   · **解封前**（未到 `ShotValueUnlockTime`）—— 超过 `ShotValueCap` 就压回 `ShotValueCap`；
        ///   · **解封后** —— 一律取 `/32768` 的结果，**不封顶**。
        /// </summary>
        public static double BulletValueFor(double totalBullets, double matchElapsed)
        {
            double v = BulletValueFor(totalBullets);
            if (matchElapsed < ShotValueUnlockTime && v > ShotValueCap) v = ShotValueCap;
            return v;
        }
        // ── 子弹拖尾 ──
        /// <summary>
        /// `true` = Unity 的 `TrailRenderer`（语义标准，但**每颗子弹一次 draw call**，多了会掉帧）；
        /// `false` = 合批网格（`BulletTrailRenderer`，全部一次 draw call）。
        /// 两种画法共用下面所有常量；取色都必须走 `Palette.BulletTrail`。
        /// </summary>
        public static readonly bool BulletTrailUsesTrailRenderer = true;
        /// <summary>拖尾长度 = 速度 × 本值（秒）。按时间采样，速度快就自然长。</summary>
        public const float BulletTrailTime = 0.3f;
        /// <summary>
        /// 拖尾长度随**单发数值**增长（对数，和引力涟漪同一套写法）：
        ///     time × (1 + BulletTrailValueGain × ln(数值 / 本值))，上限 BulletTrailTimeScaleCap 倍。
        /// ★ 参考值取**单发数值的下限 1**：只要子弹数值 &gt; 1 尾巴就更长，开局那一段也看得见。
        ///   别把参考值放回「开局数值（≈4）」—— 那一档的 ln 正好是 0，gain 怎么调都不动，等于没效果。
        /// 数值比参考值小也不会变短（下限 1 倍）。
        /// 用 ln 而不是直接乘：数值能跨好几个数量级，直接乘会拖出横穿地图的长线。
        /// </summary>
        public static readonly double BulletTrailValueRef = 1d;
        public const float BulletTrailValueGain = 0.25f;
        /// <summary>上面那个倍数的上限（别让大数值子弹的尾巴铺满整张地图）。</summary>
        public const float BulletTrailTimeScaleCap = 6f;

        /// <summary>一颗子弹（按它的单发数值）对应的拖尾生存时间 —— 生产代码和自检共用这一个来源。</summary>
        public static float BulletTrailTimeFor(double value)
        {
            double r = System.Math.Max(value, 1d) / System.Math.Max(BulletTrailValueRef, 1e-9);
            float k = 1f + BulletTrailValueGain * (float)System.Math.Log(System.Math.Max(r, 1d));
            return BulletTrailTime * Mathf.Clamp(k, 1f, Mathf.Max(BulletTrailTimeScaleCap, 1f));
        }
        /// <summary>合批模式下每颗子弹最多留几个折点（顶点数 = 子弹数 × 折点 × 4）。</summary>
        public const int BulletTrailMaxPoints = 24;
        /// <summary>合批拖尾每颗子弹最多画几段（顶点数 = 子弹数 × 段数 × 4）。</summary>
        public const int BulletTrailMaxSegments = 2;
        /// <summary>子弹数超过这个量，拖尾一律退化成一段直线（先保条数、再保形状）。</summary>
        public const int BulletTrailCrowdCount = 12000;
        /// <summary>拖尾网格每帧的顶点预算（硬上限，超了就不再画拖尾）。</summary>
        public const int BulletTrailVertexBudget = 300000;
        /// <summary>
        /// 子弹数值耗尽后，先在场上多留这么久让拖尾自然缩短到没有（期间不再移动 / 判定 / 画圆点）。
        /// </summary>
        public const float BulletTrailFadeOut = 0.35f;
        /// <summary>
        /// 最小顶点间距。必须明显小于子弹一个物理帧走的距离（否则整条拖尾只剩几个折点）。
        /// </summary>
        public const float BulletTrailMinVertex = 0.055f;
        /// <summary>拖尾头部的半径倍数（拖尾半径 = 子弹半径 × 本值；1 = 和子弹一样宽）。</summary>
        public const float BulletTrailRadiusFactor = 1f;
        /// <summary>拖尾尾端的半径倍数（同上；尾尖还会再乘贴图自带的锥度）。</summary>
        public const float BulletTrailEndRadiusFactor = 1f;
        /// <summary>拖尾尾端透明度（头白尾淡）。</summary>
        public const float BulletTrailTailAlpha = 0.0f;
        /// <summary>拖尾头部透明度（太密时几百条叠起来会糊成一片白）。</summary>
        public const float BulletTrailHeadAlpha = 0.55f;
        // ── 右栏（战场）的图层表：值小的在下面 ──
        // 说明：护盾填充/亮边在拖尾之下，拖尾在边界墙之下。
        // （炮台本体/图标/炮管是 10 / 11 / 12，都在拖尾之上，炮台那里用不到这几个常量就没搬。）
        public const int ShieldFillSortingOrder = 6;
        public const int ShieldRingSortingOrder = 7;

        /// <summary>
        /// 拖尾的图层。两条规格同时成立：
        ///   · 「子弹拖尾不能被护盾挡住」→ 必须在护盾填充/亮边（6 / 7）**之上**；
        ///   · 「墙要在子弹拖尾上面」→ 必须在边界墙（见 BattleWallSortingOrder = 15）**之下**。
        /// 所以是 8：护盾 7 &lt; 拖尾 8 &lt; 墙 15 &lt; 圆点 40 &lt; 大球 61。
        /// </summary>
        public const int BulletTrailSortingOrder = 8;
        /// <summary>战场边界墙的图层。以前硬编码成 2（在拖尾之下），现在抬到拖尾之上。</summary>
        public const int BattleWallSortingOrder = 15;
        /// <summary>子弹圆点的图层：在墙之上，在大球（60）之下。</summary>
        public const int BulletDotSortingOrder = 40;
        /// <summary>
        /// 己方子弹打到己方引力球、走「70% 反弹」那条分支时，反弹速度乘这个。
        /// **1.0 = 正常弹性**（反弹速度 = 入射速度），往上调会更弹。
        /// </summary>
        public const float BulletGravityBallBounceBoost = 1.0f;
        /// <summary>
        /// 己方子弹打到己方引力球（≥ `GravityBallThreshold` 的大球/中子星）时，
        /// 这个比例**吸收**、其余**反弹**。小于门槛的大球照旧全吸收，敌方子弹照旧扣血。
        /// </summary>
        public const float GravityBallAbsorbChance = 0.30f;
        public const int BulletMaxBounces = int.MaxValue;   // 反弹次数不该是子弹消失的原因

        public const float ScatterBaseFireRate = 16.0f;   // 密度要够
        public const float BulletCountFireRateBonus = 0.18f;
        public const float DirectBaseFireRate = 9.0f;

        public const float VolleyInterval = 6f;
        public const float VolleyProjectileSpeed = BulletSpeed;   // 规格：和散射的子弹速度一致
        public const float VolleyProjectileLife = 30f;   // 齐射弹丸要活到把涂色额度用完
        public const double VolleyUsesBallValueFraction = 1.0d;

        public const double ShieldDamagePerValue = 0.05d;
        /// <summary>大球撞护盾被弹开的冲量。</summary>
        public const float BigBallShieldRebound = 3.0f;   // 反弹速度（m/s，直接给速度不靠冲量）
        /// <summary>
        /// **破盾**（大球 ≥ 8 倍护盾）之后的速度倍数：**不反弹**，速度乘上本值、
        /// 方向不变继续往前冲。0.05 = 原来的 1/20。
        /// ★ 只有「直接破盾」那一条走它；磨盾的另外两条照旧反弹。
        /// </summary>
        public const float BigBallShieldBreakSpeedScale = 0.05f;

        // ─────────────────────────────────────────────────────────────
        // 五、增倍区（左栏）
        //
        // 版式照原版截图 1:1 量的（843.5 px ↔ ZoneWidth 18m → 46.86 px/m），
        // 下面所有米数都是这么换算来的。单位是米。
        // ─────────────────────────────────────────────────────────────

        /// <summary>增倍区整体缩放。**保持 1.0**：缩放不改比例，只会把物理尺度搞坏。</summary>
        public const float ZoneScale = 1.0f;

        public static readonly Vector2 ZoneCenter = new Vector2(-170f, 0f);
        /// <summary>内腔尺寸（灰外墙贴在**外面**，不会压在乘倍区里）。</summary>
        public const float ZoneWidth = 18f * ZoneScale;
        public const float ZoneHeight = 26f * ZoneScale;
        public const float ZoneWallThickness = 0.6f * ZoneScale;
        /// <summary>战场四周边界墙的厚度（很薄）。</summary>
        public const float BattleBoundThickness = 0.30f;
        public const float ZoneOuterWidth = ZoneWidth + ZoneWallThickness * 2f;
        public const float ZoneOuterHeight = ZoneHeight + ZoneWallThickness * 2f;
        public const float ZoneGravityScale = 0.5f;

        /// <summary>背景方格的边长（米）—— 截图里量出来 78.8px ÷ 46.86px/m。</summary>
        public const float ZoneGridStep = 1.68f;
        /// <summary>方格线的粗细（米）。很细：它只是底纹，不能抢主体。</summary>
        public const float ZoneGridLineWidth = 0.028f;

        // ── 总值计数条（增倍区**最顶部**那一排空心方框）──
        // 每家一格，显示这颗阵营增倍球的累计增益（见 `Faction.GainTotal`）。
        /// <summary>一行分几格（用户规格：4 家的版本就是 4 格；8/16 家往下铺）。</summary>
        public const int GainBarColumns = 4;
        /// <summary>整排**紧贴**增倍区顶边（0 = 贴着，不再留空）。</summary>
        public const float GainBarTopMargin = 0f;
        /// <summary>
        /// 每个方框的高度（米）。
        /// ★ 用户要求：「总值计数条高度小一点，然后还是贴顶部」——
        ///   1.05 → 0.72（矮了约 31%）。**贴顶靠的是上一条 GainBarTopMargin = 0**，
        ///   这里只动高度：顶边位置不动（DrawGainBar 里 topLocal 不变），往下收。
        ///   字号 = r.height * 0.55，会跟着一起变小，这是预期的。
        /// </summary>
        public const float GainBarHeight = 0.85f;
        /// <summary>方框之间的缝隙（米）—— 0 = 紧挨着，4 家正好铺满整宽。</summary>
        public const float GainBarGap = 0f;
        /// <summary>
        /// 框内那根**增益占比条**的透明度（Algodoo 原版 colorhsva 的 a = 0.2）。
        /// 半透明是刻意的：能看见后面的场景，和框线「内部透明」是一套。
        /// </summary>
        public const float GainFillAlpha = 0.4f;
        /// <summary>
        /// 占比条宽度的缓动系数（Algodoo 原版 poststep 里的 0.2）。
        /// 每帧朝目标靠拢这么多比例，所以占比一变，条是**滑过去**而不是瞬跳。
        /// （原版还同时缓动 pos，但这里左端固定、宽度决定右端，缓动宽度就等价了。）
        /// </summary>
        public const float GainFillEase = 0.15f;

        // ── 示意图（657 × 1111 px）坐标 → 世界坐标 ──
        public const float DiagW = 657f;
        public const float DiagH = 1111f;
        public static float DX(float px) { return (px / DiagW - 0.5f) * ZoneWidth; }
        public static float DY(float py) { return (0.5f - py / DiagH) * ZoneHeight; }

        // ── 乘法门那一排 ──
        /// <summary>左右各 5 块，**从贴墙往里**的倍率（每侧乘积 2048）。</summary>
        public static readonly int[] GateMultipliers = { 2, 8, 4, 8, 2 };
        /// <summary>
        /// 每块对应的宽度（米），下标同 <see cref="GateMultipliers"/>。
        /// 排列是**从贴墙那一侧往里**码，所以：
        ///   下标 0 = 贴墙的 x2，下标 4 = **最靠区中心**的那块 x2。
        /// 原版高清帧量出来是 1.40 / 0.81 / 1.13 / 0.81 / 1.54。
        ///
        /// ★★ 用户：「把**最靠近中间的两个 x2** 宽度改小一点」，随后定案为「**改成 90%**」——
        ///   那两个就是左右两组各自的**下标 4**（原版量出来 1.54，整组最宽的那块）。
        ///   1.54 × 0.9 = **1.386** → 取 **1.39**。
        ///   左右两组共用这一份数组，所以只改下标 4 就同时收窄了两侧的最内侧 x2，
        ///   正合用户说的「两个」。其余四块保持不变（贴墙 x2 = 1.40、x8 = 0.81、x4 = 1.13）。
        /// </summary>
        public static readonly float[] GateWidths = { 1.40f, 0.81f, 1.13f, 0.81f, 1.45f };

        /// <summary>哪些倍率的门顶上有一颗固定阻挡球（按倍率认，左右两组各长一颗）。现在为空。</summary>
        public static readonly int[] GateTopBlockerMultipliers = { };
        /// <summary>柱顶固定阻挡球的半径（米）。</summary>
        public const float GateTopBlockerRadius = 0.05f;
        /// <summary>同一组里相邻两块之间的缝。**等于柱子宽度**，柱子正好填满、不啃白牌。</summary>
        public const float GateGap = 0.20f;
        /// <summary>白色牌子（"x2/x8/..." 写在上面）的高度。</summary>
        public const float GatePlateHeight = 1.195f;
        /// <summary>白牌**中心**的高度。</summary>
        public const float GatePlateY = 0.459f;
        /// <summary>贴着墙留的边（截图里量出来≈0）。</summary>
        public const float GateSideMargin = 0f;
        /// <summary>普通门的计数器离**牌子顶边**多远（截图里量出来的 1.568m - 牌子顶 1.057m）。</summary>
        public const float GateCounterGap = 0.52f;
        /// <summary>x16 的计数器必须写在**下面**（上面坐着发射炮台）—— 偏移量（截图里量的）。</summary>
        public const float X16CounterOffsetY = -1.29f;

        public const float GateCooldown = 0.6f;
        public static readonly bool GateOncePerBall = false;
        /// <summary>判定条（白牌下面那条深灰**阻挡条**）的高度 = 牌子高度 × 这个系数。</summary>
        public const float GateTriggerHeightFactor = 0.46f;
        /// <summary>判定框比白牌宽出来的比例。</summary>
        public const float GateTriggerWidthBonus = 0.06f;
        /// <summary>
        /// 柱子顶那颗阻挡球的半径 —— **柱子宽度就从它推出来**（宽度 = 2×半径），
        /// 这样柱子和阻挡球的粗细永远不会对不上。
        /// </summary>
        public const float PostCapRadius = 0.10f;
        /// <summary>每两块乘法门之间立一根柱子（带碰撞），宽度由 <see cref="PostCapRadius"/> 派生。</summary>
        public const float GatePostWidth = PostCapRadius * 2f;
        // 增倍区里一切灰色结构件都读 `BlockerGrey`，别再另开颜色或写别名
        //（C# 静态字段按书写顺序初始化，别名写在前面会拿到全 0）。

        /// <summary>
        /// 判定体的**顶边**落在白牌从底边往上这个比例处（0.5 = 正中间）：
        /// 白牌上半截没有碰撞，球要陷进去将近一半才算乘倍成功。
        /// </summary>
        public const float GateTriggerTopFraction = 0.5f;

        /// <summary>门 / 计数器文字的字号上限（像素）。太小会把 GateLabelHeight 截掉。</summary>
        public const int HudFontSizeMax = 120;
        /// <summary>门上的 "x8" 文字高度（世界单位，固定值，不跟方块大小走）。</summary>
        public const float GateLabelHeight = 0.58f;
        /// <summary>门上方计数器的文字高度（世界单位，固定值）。</summary>
        public const float GateCounterHeight = 0.38f;

        // ── 浮力区 ──
        // 一条从固定阻挡球排通到 x16 下方的长通道。球不是被"施力"顶上去的，
        // 而是被通道里**真的物理泡泡**顶上去的（见下面 BuoyancyBubble*）。
        /// <summary>
        /// 浮力通道的**横向半宽**（以区中心对称，米）。总宽 = 本值 × 2。
        /// 泡泡是纯物理体，窄了会堆在门缝里、也托不住大球。
        ///
        /// ★★ 用户：「左右范围改大成 **3 倍**」—— 5.0 → **15.0**（总宽 10m → 30m）。
        ///   泡泡仍然只从**中间一条线**出生，宽出来的是它们出生后能横向飘散的空间
        ///   （见 `BuoyancyZone` 里的边界判定）。注意这道边界本身**不产生推力**，
        ///   只是「飘出这条边界就**回收**」，不让泡泡飞出通道去撞门。
        ///
        /// ⚠️ 上限：整个增倍区只有 `ZoneWidth = 18m`（半宽 9m）。
        ///   15m 半宽会让泡泡飘到区外面去，所以夹到 `ZoneWidth/2 - 一点余量`。
        ///   想再宽就得同时加 `ZoneWidth`（那会动整个左栏的版式）。
        /// </summary>
        public static readonly float BuoyancyHalfWidth = Mathf.Min(5.0f, ZoneWidth * 0.3f - 1.0f);
        /// <summary>
        /// 泡泡**生成/回收**的高度（相对通道底面抬高一点），免得直接卡在阻挡球里。
        /// 贴着阻挡球那一排的顶面出生（= 阻挡球半径）。
        /// </summary>
        public const float BuoyancyBubbleSpawnLift = PegRadius;
        /// <summary>
        /// 在 `SpawnLift` 之上**再多抬多少**（米）。
        ///
        /// ★★ 用户：「生成位置**上移一点**」——
        ///   `SpawnLift` 是「刚好贴着阻挡球顶面」的物理下限，再往上抬就离那排球远一点，
        ///   泡泡冒出来时不会挤在球和球的缝隙里。0 = 不额外抬高（旧行为）。
        /// </summary>
        public const float BuoyancyBubbleSpawnRaise = 0.05f;
        /// <summary>通道**底部** = 固定阻挡球那一排的高度。</summary>
        public const float BuoyancyBottomY = PegRowY;
        /// <summary>通道**顶部** = 快到 x16 阻块下沿就结束（球到这里就飘出去了）。</summary>
        public const float BuoyancyTopY = X16BarrierY - X16BarrierHeight * 0.5f - 0.3f;
        public const int BuoyancyArrowCount = 0;   // 浮力区的「⬆️」符号已删掉

        /// <summary>
        /// ★★ 第 107 轮（用户）：「生成逻辑改成**每帧生成 x 个**，而不是固定的数量」——
        ///   每个**物理帧**（`FixedUpdate`，默认 50Hz）从底部生成几颗泡泡。
        ///
        /// 可以写小数：2.5 = 有的帧 2 颗、有的帧 3 颗（攒够一颗才生成）。
        /// **稳态数量 ≈ 本值 ÷ `Time.fixedDeltaTime` × 一颗泡泡在通道里的时间**
        /// （3 颗/帧、通道 ≈ 2.1 秒走完 ⇒ 约 320 颗，和以前固定 300 颗的密度差不多），
        /// 实际会上下浮动几颗 —— 这正是「流水线」的样子。
        /// 0 = 不生成（通道会慢慢空掉，调试用）。
        /// </summary>
        public const float BuoyancyBubbleSpawnPerFrame = 3f;

        /// <summary>
        /// 通道里**最多同时存在**多少颗（硬上限，也是对象池上限）。
        /// 生成速率 × 通过时间超过它时，多出来的就不再生成 —— 防止把物理步拖垮。
        /// </summary>
        public const int BuoyancyBubbleMax = 400;

        /// <summary>
        /// 泡泡出生/回收的横向半宽（米）：只从中间那三颗阻挡球那里冒出来，所以取一个间距。
        /// </summary>
        public const float BuoyancyBubbleSpawnHalfWidth = PegRowSpacing;
        /// <summary>
        /// 气泡出生时，相对**竖直方向**的偏角上限（度）。
        /// 注意它同时会把纵向速度带小一点（`RiseSpeed × cos(角)`），
        /// 所以「想让泡泡散得更开」优先调下面的 `SideSpeed`，而不是一直加这个角度。
        /// </summary>
        public const float BuoyancyBubbleSpreadDeg = 15f;
        /// <summary>
        /// 出生时**额外的水平初速度**（米/秒，正负随机）。
        ///
        /// ★★ 用户：「再给一点**左右的初速度**」—— 这个值之前是 0 起始的「备用」项（没被用到），
        ///   现在真正加进了 `EmitVelocity()`：`v.x += Random.Range(-SideSpeed, +SideSpeed)`。
        ///   它是**叠加**在角度扰动之上的，加在速度而不是角度上 —— 纵向速度不受影响。
        /// 0 = 回到「只有角度扰动」的旧行为；调大 → 泡泡一出生成点就左右铺开得更开。
        /// </summary>
        public const float BuoyancyBubbleSideSpeed = 1.5f;
        /// <summary>
        /// 横向速度每个物理步乘这个系数。**1.0 = 不衰减**（= 原文语义：Algodoo 那段 poststep
        /// 里只有下面那条「出带就往回加」的规则，没有任何阻尼）。
        /// 调小于 1 会让泡泡慢慢收向中心、不再来回荡。
        /// </summary>
        public const float BuoyancyBubbleSideDamp = 1.0f;

        // ── 横向：照抄 Algodoo poststep 的阈值判断 ──
        //
        // 原文（用户给的，整段在 poststep 里）：
        //     pos(0) > -6 ? {
        //         vel = vel - [0.8, 0]
        //     } : {
        //         pos(0) < -8 ? { vel = vel + [0.8, 0] } : {}
        //     }
        // 也就是：横向坐标落在 **[-8, -6]** 这条 2m 宽的**死带**里什么都不做；
        // 一旦**出带**，每步给横向速度加 `∓0.8` 把它推回带内。
        // ★ 是**硬阈值**，不是「越远推得越猛」的软推回 —— 之前那个 8 m/s² 的软推回是我自己加的。
        /// <summary>
        /// 死带的**半宽**（米）。原版带是 [-8, -6]、宽 2m、中心 -7 ⇒ 半宽 1.0。
        ///
        /// ★★ 第 123 轮（用户，附实机截图）：「边收的太小了」——
        ///   直接照搬原版的 1.0m 是把**原版的绝对宽度**搬过来，但本项目的通道是
        ///   `BuoyancyHalfWidth = 4.4m`（8.8m 宽，比原版宽得多），结果泡泡被挤成
        ///   中间一条 2m 宽的柱子、出带就被 24 m/s² 硬拽回来，边上还结出 V 形。
        ///   改成**按通道比例**取 `0.75 × 半宽`（≈ 3.3m），占通道宽度的 75%，
        ///   通道再改宽也会自动跟着变。
        /// </summary>
        public static readonly float BuoyancyBubbleLateralBand = BuoyancyHalfWidth * 0.75f;
        /// <summary>原文 `vel = vel ∓ [0.8, 0]` 里的那个 **0.8**（米/秒，每 Algodoo 步）。</summary>
        public const float BuoyancyBubblePushBackStepGain = 0.8f;
        /// <summary>
        /// 横向推回的**加速度**（米/秒²）= `PushBackStepGain × AlgodooFps` = **24.0**。
        /// 这是原文那一行按秒展开的结果；代码里每步做 `vx ∓ 本值 × dt`。
        /// 0 = 关掉（**超界仍然直接回收**，见 `BuoyancyZone.FixedUpdate`）。
        /// </summary>
        public const float BuoyancyBubblePushBack =
            BuoyancyBubblePushBackStepGain * BuoyancyBubbleAlgodooFps;

        /// <summary>
        /// 泡泡**出生那一刻**如果正压着某颗球，就临时把这一对碰撞体设成「互不碰撞」，
        /// 等彻底分开再恢复。
        ///
        /// ★★ 用户：「能不能让气泡重生的时候如果碰到球就先关掉碰撞层，离开后恢复」——
        ///   思路对，但**不能改层**（层是全局的，改了一下所有泡泡对所有球都失去碰撞，
        ///   球会从泡泡堆里直接掉下去）。这里用的是 `Physics2D.IgnoreCollision(colA, colB)`，
        ///   只影响**这一对**碰撞体，别的泡泡和那颗球照常碰。
        /// false = 关掉这套机制。
        /// </summary>
        public static readonly bool BuoyancyBubbleIgnoreOnRespawn = true;
        /// <summary>
        /// 恢复碰撞的**余量**（米）：分开后要多留这么远才把碰撞打开，
        /// 免得在边界上反复开关（抖）。0 = 一分界就恢复。
        /// </summary>
        public const float BuoyancyBubbleIgnoreMargin = 0.05f;

        /// <summary>泡泡颜色：半透明灰白，一眼能看出是泡泡而不是阻挡球。</summary>
        public static readonly Color BuoyancyBubbleColor = new Color(0.88f, 0.90f, 0.94f, 0.42f);
        /// <summary>泡泡半径范围（米）。</summary>
        public const float BuoyancyBubbleRadiusMin = 0.10f;
        public const float BuoyancyBubbleRadiusMax = 0.22f;
        /// <summary>
        /// 泡泡上浮的**出生速度**（米/秒）。第 129 轮改回 4.0（用户：「改回之前的」）。
        /// </summary>
        public const float BuoyancyBubbleRiseSpeed = 4.0f;

        // ── 上浮：原文 poststep 的 `vel = vel + [0, 0.1]` ──
        //
        // 原文一行：`vel = vel + [0, 0.1]`（每个 Algodoo 步给竖直速度加 0.1，不封顶），
        // 换算成与帧率无关 = `RiseStepGain × AlgodooFps` = **3.0 m/s²**。
        //
        // ★★ 但**加速度还兼着「顶球的推力」**：泡泡被大球压住时速度接近 0，
        //   每步能提供的持续推力就是 `质量 × 加速度` = 0.025 × 3 = **0.075 N**，
        //   而最轻的球重量就有 `BallMassMin × 9.81 × 重力0.5 = 0.49 N` —— 根本推不动。
        //   第 126 轮之前之所以「有劲」，是因为每帧把速度**强行写回** 4~9 m/s，
        //   等效几十牛的假冲量；那套写法会吞掉碰撞结果（就是上一轮修掉的问题）。
        //
        // ★★ 第 127 轮（用户：「改完之后没推力了，推力大一点」）：
        //   把加速度提上去 ⇒ 单颗泡泡顶球推力 = 质量 × 加速度（3 m/s² 时只有 0.075 N，
        //   压不住最轻的球 0.49 N）；同时把阻尼同比例提上去，让自由上浮的**速度**不变。
        //
        // ★★ 第 128 轮（用户：「初速度小一点，加速度大一点」）：
        //   加速度 30 → 60、阻尼 4 → 8、出生速度 4 → 2（终端速度仍是 7.5 m/s）。
        //
        // ★★ 第 129 轮（用户：「不要阻尼，改回之前的」）：
        //   · `BuoyancyBubbleDrag` 归 **0**（不要阻尼，刚体不再有线性阻力）；
        //   · 出生速度 / 加速度改回 **4.0 / 30**；
        //   · 无阻尼时速度会一直涨（跑到顶会到 27 m/s，一步走 0.54m 直接穿模），
        //     所以重新启用**速度上限 8 m/s** —— 这正是第 126 轮之前那套「加速度 + 上限」的写法。
        //   推力仍由加速度决定：0.025 × 30 = **0.75 N/颗**（最轻球重 0.49 N）。
        /// <summary>原文 `vel = vel + [0, 0.1]` 里的那个 **0.1**（米/秒，每 Algodoo 步）。</summary>
        public const float BuoyancyBubbleRiseStepGain = 0.1f;
        /// <summary>
        /// Algodoo 场景的步频（Hz）。只用来把「每步 0.1」换算成与帧率无关的加速度 ——
        /// 改这里等于改整条通道的上升快慢。
        /// </summary>
        public const float BuoyancyBubbleAlgodooFps = 30f;
        /// <summary>
        /// 原文换算出来的**自由上浮**加速度（= `RiseStepGain × AlgodooFps` = 3.0）。
        /// ★ 只作参考：实际用的 <see cref="BuoyancyBubbleRiseAccel"/> 比它大得多，
        ///   因为加速度还决定「顶球推力」（见上面的推导）。
        /// </summary>
        public const float BuoyancyBubbleFreeRiseAccel =
            BuoyancyBubbleRiseStepGain * BuoyancyBubbleAlgodooFps;
        /// <summary>
        /// 实际使用的向上**加速度**（米/秒²）。两个作用同时由它决定：
        /// · **顶球推力** = `BuoyancyBubbleMass × 本值`（0.025 × 30 = 0.75 N/颗）；
        /// · **起速快慢**：有阻尼时终端速度 = 本值 ÷ `Drag`；无阻尼时由
        ///   <see cref="BuoyancyBubbleRiseSpeedMax"/> 封顶。
        /// 调大 → 顶球更有劲、起速更快（上限会挡住实际速度）。
        /// </summary>
        public const float BuoyancyBubbleRiseAccel = 30f;

        /// <summary>
        /// 上浮**速度上限**（米/秒）。**0 = 不封顶**。
        ///
        /// ★★ 第 129 轮（用户：「不要阻尼，改回之前的」）：阻尼归 0 之后速度会一直涨
        ///   （`4 + 30t`，走到顶会到 27 m/s，一步 0.54m —— 比泡泡直径还大，直接穿模），
        ///   所以重新启用上限 **8 m/s**：这正是第 126 轮之前那套「加速度 + 速度上限」的写法，
        ///   泡泡在 0.13s 内加速到 8 m/s，之后匀速上升。
        /// 填 0 会变成纯加速（配合 `Drag = 0` 不推荐）。
        /// </summary>
        public const float BuoyancyBubbleRiseSpeedMax = 8f;
        /// <summary>
        /// 泡泡质量。推力 = 每次撞击传出的动量（∝ m×v）× 单位时间撞几下（∝ 1/通过时间），
        /// 合起来 **∝ m × 通道高度 / 通过时间²** —— 所以有了上浮加速度之后，
        /// 通过时间从 ≈3.0s 缩到 ≈1.79s（第 121 轮把加速度提到 3.0 m/s²），
        /// 同样的质量推力会翻倍：这里从 0.034 配到 0.020 抵掉。
        /// 比最轻的增倍球还轻。球在浮力区升不上去 → 调大；被弹得太猛 → 调小。
        /// </summary>
        public const float BuoyancyBubbleMass = 0.05f;
        /// <summary>
        /// 泡泡的线性阻尼（`Rigidbody2D.drag`）。
        ///
        /// ★★ 第 126 轮起它**真正起作用**了：以前每帧用自己维护的纵向累加器
        ///   `velocity = (vx, Vy)` 覆盖刚体速度，阻尼在纵向被绕过；现在改成从
        ///   **刚体当前速度**继续累加（这样被大球挡住后能从碰撞后的速度接着来），
        ///   阻尼也就自然生效。
        /// ★★ 第 129 轮（用户：「不要阻尼，改回之前的」）：**归 0 = 完全没有线性阻力**。
        /// 代价是速度会一直涨，所以改用 `BuoyancyBubbleRiseSpeedMax` 封顶（见下）。
        /// 想要回「靠阻尼收敛到某个终端速度」的写法，把它填回正数即可
        /// （终端速度 = `RiseAccel / Drag`，例如 30 / 4 = 7.5 m/s）。
        /// </summary>
        public const float BuoyancyBubbleDrag = 0f;
        /// <summary>泡泡在通道底部这一段（比例）里随机撒，免得全挤在一条线上。</summary>
        public const float BuoyancyBubbleSpawnBand = 0.3f;
        /// <summary>每颗泡泡的寿命（秒），到点就**消失**（不再瞬移回底部，见 `BuoyancyBubbleSpawnPerFrame`）。</summary>
        public const float BuoyancyBubbleLife = 3f;
        // ★★ 用户：「现在正常生成，**把以前的机制删掉**」——
        //   下面这几个参数服务的是一套**已经删除**的机制（出生点找空位 / 挡住就藏起来等），
        //   现在重生只落在三道源头上，万一压着球就由 `IgnoreCollision` 临时忽略掉。
        //   （已删除：BuoyancyBubbleSpawnClearance / SpawnRetryMax / ParkOffset /
        //     BubbleSpawnDebug / SpawnSearchRise，连同 FindFreeSpawn、SpawnBlocked、_retry。）

        // ── 一排等距固定阻挡球（13 颗，正好从 -9 铺到 +9）──
        public const int PegRowCount = 13;
        /// <summary>相邻两颗的中心距（= 18 / 12，正好铺满整幅宽）。</summary>
        public const float PegRowSpacing = 1.5f;
        public const float PegRowY = -5.83f;
        /// <summary>球的半径（米）。</summary>
        public const float PegRadius = 0.117f;

        // ── 四颗固定在半空的阻挡球（灰色、可碰撞）──
        /// <summary>最上面那两颗，左右对称。</summary>
        public static readonly float[] TopBlockerX = { -6.0f, 6.0f };
        public const float TopBlockerY = 8.49f;
        /// <summary>
        /// 贴着左右墙的那两颗：圆心正好在墙面上（±ZoneWidth/2），一半嵌进墙里。
        /// </summary>
        public static readonly float[] WallBlockerX = { -9.0f, 9.0f };
        public const float WallBlockerY = 5.19f;
        public const float BlockerRadius = 0.208f;

        // ── 移动阻挡球：只会水平移动，碰到墙就折返 ──
        public const float MovingBlockerRadius = 0.29f;
        /// <summary>所在的高度（截图里量的）。</summary>
        public static readonly float[] MovingBlockerY =
        {
            1.09f,                        // 乘法门那一排
            PegRowY,                      // 固定阻挡球那一排
            DY(WeaponZoneTopPx),          // 武器区（中心 = 白框顶边）
        };
        /// <summary>水平移动速度（米/秒）。</summary>
        public const float MovingBlockerSpeed = 2.6f;
        // 注：闪色机制直接复用 BlockerBall（Cfg.BlockerGrey / Cfg.BlockerFlashTime），
        //     这里不再另开一套常量，免得两边的「闪多久」对不上。

        // ── 发射炮台（坐在 x16 阻块上面）+ x16 阻块 ──
        /// <summary>炮管扫到的最大偏角（度，0 = 正上方）。**-65° ~ +65°** 来回转。</summary>
        public const float LauncherSweepAngle = 65f;
        /// <summary>炮口的平均扫描速度（度/秒）—— sin 扫动下这是**平均**值。</summary>
        public const float LauncherSweepSpeed = 42f;
        /// <summary>
        /// sin 扫动的角频率（弧度/秒）：`偏角 = LauncherSweepAngle × sin(ωt)`。
        /// ω 取成「**平均角速度** = `LauncherSweepSpeed`」（sin 的平均 |速度| = 2/π × 幅值 × ω），
        /// 所以来回一趟的周期和以前的匀速三角波一样（≈ 4×幅值/速度 ≈ 6.2 s），
        /// 只是中间快、两端慢：端点处会自然停一下再折回，峰值角速度 ≈ 66°/s。
        /// </summary>
        public static readonly float LauncherSweepOmega =
            LauncherSweepSpeed * Mathf.PI * 0.5f / Mathf.Max(LauncherSweepAngle, 1f);
        /// <summary>炮台本体半径（米）。大圆身子 + 又短又粗的炮管。</summary>
        public const float LauncherRadius = 0.39f;
        /// <summary>炮管长度（米）。</summary>
        public const float LauncherBarrelLength = 0.85f;
        public const float LauncherBarrelWidth = 0.28f;
        /// <summary>
        /// 炮台中心相对「帽子最上面那个尖」的纵向偏移。0 = 圆心正好在尖上，
        /// 下半颗嵌进锥面里（规格：炮座一半在阻挡物里、一半在外面）。
        /// </summary>
        public const float LauncherSeatOffsetY = 0f;
        /// <summary>增倍球从炮口打出去的初速度（米/秒）。</summary>
        public const float LaunchSpeed = 8.6f;

        /// <summary>
        /// x16 **阻块**：灰色实心阻挡物，上尖下宽的「帽子」，底边正中挖掉一个梯形缺口。
        /// 它只是阻挡物；x16 的判定在白框里（见 `X16PlateWidth`）。
        /// </summary>
        public const float X16BarrierWidth = 3.86f;
        public const float X16BarrierAspect = 3.44f;
        public const float X16BarrierHeight = X16BarrierWidth / X16BarrierAspect;
        /// <summary>阻块**中心**的高度（截图里量的）。</summary>
        public const float X16BarrierY = 6.98f;

        /// <summary>
        /// x16 那块白底牌子（黑字 "x16"）：**它的中间**才是判定点，尺寸/位置是对着截图量的。
        /// </summary>
        public const float X16PlateWidth = 1.28f;
        public const float X16PlateHeight = 0.64f;
        /// <summary>白牌中心相对阻块中心的纵向偏移（负 = 偏下）。</summary>
        public const float X16PlateOffsetY = -0.38f;
        /// <summary>x16 的倍率。</summary>
        public const int X16Multiplier = 16;

        /// <summary>增倍球重新投放的备用点（没有炮台时兜底用）。</summary>
        public const float RelaunchXPx = 319f;
        public const float RelaunchYPx = 108f;
        public static float RelaunchX { get { return DX(RelaunchXPx); } }
        public static float RelaunchY { get { return DY(RelaunchYPx); } }

        // ── 底部武器区：5 格 ──
        // 计数器机制和乘法门一样：挂在区上（四家共享），数「这一格被增倍球选中过多少次」。
        public const float WeaponCounterHeight = 0.38f;   // 和 GateCounterHeight 一致

        // ── 增倍球的密度（用户给的公式）──
        /// <summary>
        /// 增倍球的质量 = 密度 × 面积，`density = ln(数值) × 0.8 + 0.05`。
        /// Rigidbody2D 没有 density，所以直接算 mass。
        /// ★ 浮力那边必须调这个函数，**不许自己再抄一份**（抄旧公式会差 5 倍多）。
        /// </summary>
        public static float MultiplierBallMass(double value)
        {
            float v = Mathf.Max((float)value, 1f);
            float r = Mathf.Log(v) / MultiplierBallRadiusLogDivisor + MultiplierBallRadiusBias;
            float density = Mathf.Log(v) * BallDensityLogScale + BallDensityBias;
            return Mathf.Max(BallMassMin, density * Mathf.PI * r * r);
        }

        public const float BallDensityLogScale = 0.8f;
        /// <summary>增倍区里「挡板」的碰撞体比看得见的那块**上下各厚这么多米**（防高速穿模）。</summary>
        public const float ZoneBlockerExtraThickness = 0.03f;

        // ── 大球的光效 ──
        /// <summary>贴着球体那圈亮边的倍率 / 透明度（会呼吸）。</summary>
        public const float BigBallGlowScale = 1.02f;
        public const float BigBallGlowAlpha = 0.32f;
        /// <summary>外面那圈更淡的光晕。</summary>
        public const float BigBallHaloScale = 1.18f;
        public const float BigBallHaloAlpha = 0.13f;
        /// <summary>
        /// 大球在屏幕上小于这个半径（像素）就不画那两圈光效
        /// —— 那么小根本看不见，却要白烧两张半透明大图的填充率。
        /// </summary>
        public const float BigBallGlowMinScreenPx = 7f;
        /// <summary>呼吸快慢（弧度/秒）。</summary>
        public const float BigBallGlowPulseSpeed = 1.6f;
        public const float BallDensityBias = 0.05f;

        // ── 阻挡球：一律灰色，被增倍球撞到时闪一下阵营色 ──
        public static readonly Color BlockerGrey = new Color(0.62f, 0.62f, 0.65f);
        /// <summary>被撞之后从阵营色淡回灰色的时间（秒）。</summary>
        public const float BlockerFlashTime = 0.35f;

        /// <summary>
        /// 武器区顶上那排**平衡阻挡球**：左半边留宽口、右半边加密，
        /// 让落入概率是「护盾 &gt; 散射 &gt; 大球 &gt; 齐射」。位置是按图二量的。
        /// 单位：示意图 px（657 = 18m）。
        ///
        /// 各球落在哪一格（槽位边界见 `WeaponSlotXPx`）：
        ///   249.5 → 齐射格中心 ｜ **334.8、371.9 → 护盾格** ｜ 508 / 557.3 / 606.5 → 大球格
        ///
        /// ★★ 用户：「**盾区中间的两个阻挡球往中间靠一点点，大概自己的半径**」——
        ///   护盾格里那两颗（334.8 / 371.9）各自**向格子中心**挪了**一个半径**。
        ///   半径 0.10m ÷ (18m/657px) ≈ **3.65 px**，所以：
        ///     334.8 → **338.4**（向右挪 3.65）
        ///     371.9 → **368.3**（向左挪 3.65）
        ///   两球间距 37.1 → 29.8 px，中间那道缝变窄，护盾格更容易接住球。
        ///   ⚠️ 注意：这**会改变护盾的落入概率**（缝窄了，球更容易被弹开而不是掉进护盾格）。
        /// </summary>
        public static readonly float[] WeaponBalancePegXPx = { 249.5f, 339.4f, 369.3f, 508.0f, 557.3f, 606.5f };
        /// <summary>平衡球半径（米）—— 和柱顶球同一个大小。</summary>
        public const float WeaponBalancePegRadius = 0.10f;

        /// <summary>球的中心正好卡在白框的顶边上。</summary>
        public const float WeaponBalanceYPx = WeaponZoneTopPx;

        /// <summary>
        /// 判定区的**顶边** = 白框顶边 − PostCapRadius − 本值
        /// （规格：判定在阻挡球底部下方一点，球得落下来才接受）。
        /// </summary>
        public const float WeaponTriggerTopGap = 0.12f;
        /// <summary>      
        /// 武器区整排只有一个总触发框，所以 x2 这两格在 `WeaponZone.Accept` 里
        /// **再判一次高度**：球落在 x2 格上、但**球底**还没沉到这条线以下就先不接受。单位：米。
        /// </summary>
        public const float WeaponGateTriggerDrop = 0.22f;
        /// <summary>
        /// 齐射格同理：**球底**比整排触发框顶边再往下沉这么多（米）。
        /// ★ 0 = 和其他格完全一样（不做这次额外判定）。
        /// </summary>
        public const float WeaponVolleyTriggerDrop = 0.05f;

        /// <summary>这一格的判定线比整排触发框顶边再低多少（米）—— 0 = 不额外降。</summary>
        public static float WeaponSlotTriggerDrop(int slot)
        {
            if (WeaponSlots == null || WeaponSlots.Length == 0) return 0f;
            var d = WeaponSlots[Mathf.Clamp(slot, 0, WeaponSlots.Length - 1)];
            if (d.IsGate) return WeaponGateTriggerDrop;
            if (d.Kind == WeaponSlotKind.Volley) return WeaponVolleyTriggerDrop;
            return 0f;
        }
        /// <summary>
        /// 格子之间那根柱子的宽度。比阻挡球直径（0.20）明显粗一点 ——
        /// HUD 的牌子画在 IMGUI 里、盖在一切之上，柱子太细会被遮得只剩一条缝。
        /// </summary>
        public const float WeaponPostWidth = PostCapRadius * 2.4f;   // = 0.24m

        /// <summary>
        /// 柱子顶端（= 柱顶球的球心）从白牌顶边往下让这么多米：
        /// 球顶只比白牌高一点点，柱子整体变短（底端不动）。
        /// 球自带碰撞，正好堵住柱顶那截缝。
        /// </summary>
        public const float GatePostTopDrop = 0.06f;
        /// <summary>白格下面那条深灰底板的高度（实心阻挡）。</summary>
        public const float WeaponBaseBarHeight = 0.28f;

        /// <summary>门只认「从上面撞下来」：接触法线的 y 要大于这个值（1 = 正上方）。</summary>
        public const float GateTopHitNormalMin = 0.35f;

        // ── 领土花纹（四个阵营各不相同；形状/尺寸都在 `TerritoryDeco.Styles` 里）──
        /// <summary>总开关（关掉就纯阵营色）。</summary>
        public static readonly bool TerritoryDecoEnable = true;

        public const float WeaponCounterGapPx = 30f;      // 计数器中心离武器区顶边多少「图内像素」
        public const float WeaponZoneTopPx = 1044f;
        public const float WeaponZoneBottomPx = 1092f;
        /// <summary>近防炮是否占用武器区一格（false = 最后一格是**吸血球**；改 true 就换成近防炮）。</summary>
        public const bool CiwsEnabled = false;

        /// <summary>
        /// 武器区格子的 x 分界（示意图 px，共 N+1 个值），顺序（图二）：
        /// 直射 | x2 | 散射 | 齐射 | 护盾 | x2 | 大球 | **吸血球**（近防炮开着时最后一格是近防炮）。
        /// ★ 第 111 轮：最后一格沿用当年近防炮那条分界线（560）——
        ///   大球 463.4~560、第 8 格 560~657，两边正好各占一半。
        /// </summary>
        public static readonly float[] WeaponSlotXPx = BuildWeaponSlotXPx();
        static float[] BuildWeaponSlotXPx()
        {
            return new[] { 0f, 75.9f, 122.8f, 205.3f, 292.9f, 419.6f, 463.4f, 560f, 657f };
        }

        /// <summary>「选武器」时每种武器打出多少发子弹。</summary>
        public const int WeaponBurstBullets = 12;
        public const float WeaponDirectDeg = 4f;
        public const float WeaponScatterDeg = 26f;
        /// <summary>齐射：一次 512 发，在 ±30 度的扇形里撒开。</summary>
        public const int VolleyBulletCount = 512;
        public const float VolleySpreadDeg = 30f;
        /// <summary>512 发**分多少帧撒完**（一帧全放会卡）。48/帧 → 约 11 帧打完。</summary>
        public const int VolleyBulletsPerTick = 32;
        /// <summary>齐射弹丸的拖尾：它的数值很小，按数值算出来的拖尾几乎看不见，所以单独给。</summary>
        public const float VolleyTrailLength = 0.45f;
        public const float VolleyTrailThickness = 0.045f;
        /// <summary>齐射每发的数值 = 齐射数值 / 512 + 1（和子弹同一条公式的形状）。</summary>
        public static double VolleyBulletValue(double volleyValue)
        {
            return System.Math.Max(volleyValue / VolleyBulletCount + 1d, 1d);
        }
        public static readonly string[] WeaponSlotNames = BuildWeaponSlotNames();
        static string[] BuildWeaponSlotNames()
        {
            return CiwsEnabled
                ? new[] { "直射", "x2", "散射", "齐射", "护盾", "x2", "大球", "近防炮" }
                : new[] { "直射", "x2", "散射", "齐射", "护盾", "x2", "大球", "吸血" };
        }

        /// <summary>
        /// 武器区每一格是什么，顺序照图：直射 | x2 门 | 散射 | 齐射 | 护盾 | x2 门 | 大球 | 吸血球。
        /// `Gate &gt; 1` 的格子是**乘法门**（不是武器）：球落进去不消耗，数值 ×Gate 后重投。
        ///
        /// ★★ 第 111 轮（用户）：「吸血球加在**大球选择区的最右边一格**」——
        ///   所以第 8 格就是它（近防炮开着的时候那一格仍然是近防炮，二选一）。
        /// </summary>
        public static readonly WeaponSlotDef[] WeaponSlots = BuildWeaponSlots();
        static WeaponSlotDef[] BuildWeaponSlots()
        {
            WeaponSlotDef direct0 = new WeaponSlotDef { Kind = WeaponSlotKind.Direct, Gate = 0 };
            WeaponSlotDef direct2 = new WeaponSlotDef { Kind = WeaponSlotKind.Direct, Gate = 2 };
            WeaponSlotDef scatter = new WeaponSlotDef { Kind = WeaponSlotKind.Scatter, Gate = 0 };
            WeaponSlotDef volley  = new WeaponSlotDef { Kind = WeaponSlotKind.Volley,  Gate = 0 };
            WeaponSlotDef shield  = new WeaponSlotDef { Kind = WeaponSlotKind.Shield,  Gate = 0 };
            WeaponSlotDef bigBall = new WeaponSlotDef { Kind = WeaponSlotKind.BigBall, Gate = 0 };
            WeaponSlotDef ciws    = new WeaponSlotDef { Kind = WeaponSlotKind.Ciws,    Gate = 0 };
            WeaponSlotDef vampire = new WeaponSlotDef { Kind = WeaponSlotKind.Vampire, Gate = 0 };
            return CiwsEnabled
                ? new[] { direct0, direct2, scatter, volley, shield, direct2, bigBall, ciws }
                : new[] { direct0, direct2, scatter, volley, shield, direct2, bigBall, vampire };
        }

        // ─────────────────────────────────────────────────────────────
        // 近防炮（CIWS）
        // 飞行中是不做物理判定的小球，撞到非己方领土就展开：
        // 数值 50% 变盾、50% 变散弹；范围里有敌方就开火，否则吸己方领土能量回散弹。
        // 开火时：自己数值 ≥ 敌方 → 打出相同数值；不够 → 一次性全打出去。
        // 护盾归零后本体再被打中就死，无亡语。
        // ─────────────────────────────────────────────────────────────
        /// <summary>初始形态（飞行中）的半径（「大小相当于中子星」）。</summary>
        public const float CiwsBodyRadius = 0.30f;
        /// <summary>展开后的护盾半径 —— 比炮台护盾（最低 `ShieldMinRadius`）小。</summary>
        public const float CiwsShieldRadius = 0.62f;
        /// <summary>数值在盾 / 散弹之间怎么分。</summary>
        public const float CiwsShieldFraction = 0.5f;
        /// <summary>飞行阶段的速度（不参与物理，直线飞）。</summary>
        public const float CiwsLaunchSpeed = 4.5f;
        /// <summary>索敌 / 吸能的半径（别和炮台的 `CiwsRange` 混淆）。</summary>
        public const float CiwsGunRange = 3.0f;
        /// <summary>开火间隔（秒）。</summary>
        public const float CiwsFireInterval = 0.05f;
        /// <summary>近防炮弹丸的速度。</summary>
        public const float CiwsBulletSpeed = 5.5f;
        /// <summary>
        /// 吸收己方领土时**每格每帧**加多少散弹（会乘上范围内的格数，领地大时很猛）。
        /// </summary>
        public const double CiwsAbsorbPerCellPerFrame = 0.5d;
        /// <summary>近防炮弹丸的颜色：白色（和阵营色的普通子弹区分开）。</summary>
        public static readonly Color CiwsBulletColor = new Color(1f, 1f, 1f, 1f);
        /// <summary>近防炮弹丸半径的倍数（相对普通子弹）。</summary>
        public const float CiwsBulletRadiusScale = 1.6f;
        /// <summary>近防炮本体的图层（在领土之上、大球之下）。</summary>
        public const int CiwsSortingOrder = 45;
        /// <summary>护盾圈的图层。</summary>
        public const int CiwsShieldSortingOrder = 46;
        /// <summary>近防炮文字（盾 / 散弹）的世界高度 —— 和炮台数字一样用**固定值**。</summary>
        public const float CiwsTextHeight = 0.22f;
        /// <summary>文字离护盾上沿多远。</summary>
        public const float CiwsTextGap = 0.12f;

        /// <summary>落在第几格（越界夹到两端）。</summary>
        public static WeaponSlotDef WeaponSlot(int i)
        {
            if (WeaponSlots == null || WeaponSlots.Length == 0) return default(WeaponSlotDef);
            return WeaponSlots[Mathf.Clamp(i, 0, WeaponSlots.Length - 1)];
        }

        /// <summary>
        /// 某件武器在**第几格**。别用 `(int)WeaponSlotKind.X` 当格号 ——
        /// 那是种类编号，不是格号（中间还夹着两个 x2 门）。
        /// </summary>
        public static int WeaponSlotIndexOf(WeaponSlotKind kind)
        {
            if (WeaponSlots != null)
                for (int i = 0; i < WeaponSlots.Length; i++)
                    if (!WeaponSlots[i].IsGate && WeaponSlots[i].Kind == kind) return i;
            return 0;
        }

        // ── 直射模式 ──
        /// <summary>直射：炮台不转，只在当前朝向 ±这个角度里抖（度）。</summary>
        public const float DirectFireSpreadDeg = 1f;
        /// <summary>
        /// 直射的射速（发/秒）。直射按**时间**发 —— 炮台不转，按角度那套会一发都不发。
        /// </summary>
        public const float DirectFireRate = 220f;

        /// <summary>
        /// 炮台下方的**名称**：头衔 / 名称 / 称号 三段，居中叠在炮台下面。
        /// 名称必填，头衔和称号可空（空的整行不画）。
        /// 顺序 = 阵营 Index：0 左上 / 1 右上 / 2 左下 / 3 右下。
        /// </summary>
        public struct TurretNameDef
        {
            public string Title;     // 头衔（King / Queen……），可空
            public string Name;      // 名称（中间那行，字略大）
            public string Epithet;   // 称号（the Ruinous……），可空
        }

        /// <summary>
        /// 从阵营表读（改名字去 FactionSet4.cs / FactionSet8.cs）。
        /// ★ 属性 + 按阵营数缓存：阵营数现在运行时可改（设置面板），
        ///   写成 `static readonly` 会把第一次用到的 4 家表一直留在内存里。
        /// </summary>
        public static TurretNameDef[] TurretNames
        {
            get
            {
                if (_turretNames == null || _turretNamesCount != FactionSet.Count)
                {
                    _turretNames = BuildTurretNames();
                    _turretNamesCount = FactionSet.Count;
                }
                return _turretNames;
            }
        }

        static TurretNameDef[] _turretNames;
        static int _turretNamesCount = -1;

        static TurretNameDef[] BuildTurretNames()
        {
            var d = FactionSet.Defs;
            var a = new TurretNameDef[d.Length];
            for (int i = 0; i < d.Length; i++)
                a[i] = new TurretNameDef { Title = d[i].Title, Name = d[i].Name, Epithet = d[i].Epithet };
            return a;
        }
        /// <summary>炮台下面那三行名称的字高（世界单位，**固定值**，不跟炮台大小走）。</summary>
        public const float TurretNameTextHeight = 0.20f;
        /// <summary>炮台牌子上**数字**的字高（世界单位，固定值）。这里存的是 em 高度。</summary>
        public const float TurretTextHeight = 0.37f;
        /// <summary>字号的**像素上限**（4K 下 pxPerUnit 很大，不封顶会糊成一片）。</summary>
        public const int TurretTextSizeMax = 96;
        /// <summary>
        /// 「字高 → IMGUI 字号」的换算系数：字号 = 字高(世界单位) × pxPerUnit × 本值。
        /// 略小于 1，因为 IMGUI 的字号是**行框**高度，实际字形只占其中一部分
        /// （行高 ≈ `HudLineHeightFactor × 字号`），乘小了字才对得上那个世界高度。
        /// ★ 自检算同一字号时要用这个常量，别再抄一遍 0.95（抄了阵营表一改就假失败）。
        /// </summary>
        public const float TurretTextSizeFactor = 0.95f;
        /// <summary>名称三行之间的行距 = 行高 × 这个系数（比默认紧一点）。</summary>
        public const float TurretNameLineAdvance = 1.06f;
        /// <summary>
        /// 护盾数值固定摆在「两行名称」的高度上 —— **不随各阵营实际名称行数变化**，
        /// 否则各家护盾会上下乱跑、对不齐。
        /// </summary>
        public const int TurretShieldNameLines = 2;
        /// <summary>名称三行 + 下面的护盾整体往上拉多少（单位 = 炮台屏幕半径的比例）。</summary>
        public const float TurretLowerBlockLiftFactor = 0.45f;
        /// <summary>
        /// 名称块再往下让一点（单位 = 名称基础字号的 em）——
        /// 头衔留空时最大那行（名字）紧贴炮台，用户要求往下挪一点。
        /// </summary>
        public const float TurretNameDropEm = 0.18f;
        /// <summary>
        /// 护盾数值单独再往上提一点（单位 = 名称基础字号的 em）—— 它跟着名称块一起被
        /// `TurretNameDropEm` 往下带过，用户要求单独收回去一点。
        /// </summary>
        public const float TurretShieldLiftEm = 0.15f;
        // ── 大球 / 增倍球上数值的字号 ──
        // 字号(px) = 球的屏幕直径(px) × 本值（只跟直径成正比，不按位数分档）。
        // 太长会伸出球外：想让 4 个字符正好填满就取 ≈0.40。
        public const float BallTextScale = 0.39f;
        /// <summary>
        /// 大球数值字号的上下限（px）。下限：再小就糊成一团看不清；
        /// 上限：球再大也不能让数字铺满整个屏幕。
        /// </summary>
        public const int BallTextSizeMin = 6;
        public const int BallTextSizeMax = 220;

        /// <summary>
        /// HUD 字号量化到几的倍数（1 = 不量化）。HUD 是 IMGUI + 动态字体，
        /// 每个新字号都要单独光栅化一遍字形。
        /// 1 = 字号逐像素变化（大球数字长大时不跳台阶）；3 = 省字体图集但一次跳 3px，肉眼可见。
        /// </summary>
        public const int HudFontSizeStep = 1;
        /// <summary>
        /// IMGUI 里一行文字占的高度 = 字号 × 本值（Arimo / Rajdhani 的 `lineHeight` 就是 1.22 倍字号）。
        /// HUD 里所有「按行高垂直居中 / 叠行」的地方都用它 —— 别再各处写 1.22f，
        /// 以后换字体要改行高就只改这一处（自检也读它）。
        /// </summary>
        public const float HudLineHeightFactor = 1.22f;
        public const float TurretNameMidScale = 1.25f;
        /// <summary>武器区格子数（跟着 `WeaponSlots` 走）。</summary>
        public static readonly int WeaponSlotCount = WeaponSlots.Length;

        // ─────────────────────────────────────────────────────────────
        // 六、半径公式
        //
        // 自然对数：Mathf.Log 就是 ln（Algodoo 的 math.log 也是），统一走 `LogV`。
        // 万一要改成以 10 为底，把 `UseLog10` 置 true。
        // ─────────────────────────────────────────────────────────────

        public static readonly bool UseLog10 = false;

        /// <summary>半径 / 引力公式里统一用这个，避免有人以为用了 log10。</summary>
        public static float LogV(float v)
        {
            float x = Mathf.Max(v, 0.0001f);
            return UseLog10 ? Mathf.Log10(x) : Mathf.Log(x);
        }


        public const float ShieldRadiusLogDivisor = 18f;
        public const float ShieldRadiusBias = 0.1f;
        /// <summary>
        /// 护盾半径的缓动（Algodoo 原式）：`radius += (target - radius) / 5` ——
        /// 也就是**每 1/30 秒**把差距收掉 1/5（Algodoo 跑 30 fps）。
        /// Unity 这边换算成与帧率无关的形式：`k = 1 - (1 - 本值)^(dt × 30)`，
        /// 60fps / 120fps 观感一样。0.2 = 1/5，调小更慢、调大更快。
        /// </summary>
        public const float ShieldRadiusLerpPerStep = 0.2f;

        public const float BigBallRadiusLogDivisor = 8.3f;
        public const float BigBallRadiusBias = -0.5f;
        /// <summary>
        /// 大球半径的**纯乘法系数**：`半径 = (ln(数值)/除数 + 偏置) × 本值`。
        /// 1 = 不变；只作用于大球（中子星和增倍球有自己的公式）。
        /// </summary>
        public const float BigBallRadiusScale = 0.95f;
        public const float BigBallRadiusSmooth = 10f;   // 大球出炮台后约 0.8 秒长到目标大小

        /// <summary>
        /// 增倍球半径 = ln(数值) / 本值 + 0.05。**除数越小、同样数值的球越大**。
        /// 大小没有下限（唯一的"地板"就是公式里那个 +0.05）。
        /// </summary>
        public const float MultiplierBallRadiusLogDivisor = 28f;
        public const float MultiplierBallRadiusBias = 0.05f;

        public const float RadiusMin = 0.06f;
        public const float RadiusMax = 40f;
        /// <summary>增倍球的显示下限。规格：**没有下限**（见上面的半径公式）。</summary>
        public const float MultiplierBallRadiusMin = 0f;
        public const float BigBallRadiusMin = 0f;    // 规格：大球半径**没有下限**，多小都可以
        /// <summary>质量随数值增长（数值只通过半径体现），所以直接用「半径 × 系数」当质量。</summary>
        public const float BallMassPerRadius = 1.6f;
        public const float BallMassMin = 0.10f;   // 小球的底质量要够轻，不然浮力托不动它

        /// <summary>
        /// 增倍球每 tick 缩小的是**显示半径**、不是数值（乘倍 / 选武器时归 1）。
        /// </summary>
        public const float MultiplierBallShrinkPerTick = 0.99995f;
        /// <summary>增倍球彗星尾巴的长度（相对头部直径的倍数）。</summary>
        public const float MultiplierBallTailLength = 3.5f;
        /// <summary>大球的彗星尾巴长度（相对直径的倍数）。</summary>
        public const float BigBallTailLength = 4.0f;

        // ─────────────────────────────────────────────────────────────
        // 七、大球碰撞规则
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 不同阵营大球相撞：**双方各扣** `min(a,b) × 本值`。
        /// 注意三条不再是同一个数：这条 0.25，而 `GravityBallAbsorbFraction` /
        /// `GravityBallMutualFraction` 是 0.75，别顺手"统一"回去。
        /// </summary>
        public const double BigBallClashLossFraction = 0.25d;

        /// <summary>
        /// 大球的**收尸线**：数值掉到这以下就直接收掉。否则会剩下打不死的尘点
        /// —— 碰撞扣的是自身比例（永远到不了 0），而半径早被公式钳成负数、几乎不可能被命中。
        /// 取 100：`r = ln(v)/8.3 − 0.5` 要为正也需要 v &gt; 63，更小的球本来就是个看不见的点。
        /// </summary>
        public const double BigBallMinValue = 100d;
        public const double GravityBallThreshold = 100000000d;   // 100M

        // ── 中子星 ──
        // 两条路都会变成中子星：① 大球逐渐长到 1B 时坍缩；② 增倍球 ≥ 1B 选「大球」格。
        // 判定是派生的（`Value >= 门槛`），两条路自动一致。
        /// <summary>到这个数值就是中子星。</summary>
        public const double NeutronStarThreshold = 1000000000d;   // 1B
        /// <summary>
        /// 调试键 **B** 刷出来的大球数值：999M —— 已经是引力球（&gt;100M），但**还不到中子星**，
        /// 可以单独看「大引力球」的样子。想换成中子星就改成 `NeutronStarThreshold`。
        /// </summary>
        public const double DebugBallValue = 999000000d;           // 999M
        /// <summary>
        /// 调试键 **V** 刷出来的吸血球数值：**99M**（第 113 轮）。
        /// 刻意压在 `GravityBallThreshold`（100M）之下 —— 这样它**只是**一颗带
        /// `IsVampire` 标记的普通大球，不会同时挂上引力球涟漪 / 中子星那套东西，
        /// 方便单独检查第 112 轮的螺旋笔迹和吸血碰撞。
        /// </summary>
        public const double DebugVampireBallValue = 99000000d;     // 99M
        /// <summary>
        /// 中子星半径：朝 `ln(√数值)/30 − 0.04` 指数逼近
        /// （1B：大球 2.00m → 中子星 0.42m，看着就是坍缩）。
        /// </summary>
        public const float NeutronStarRadiusLogDivisor = 30f;
        public const float NeutronStarRadiusBias = 0.04f;
        public const float NeutronStarRadiusSmooth = 5f;
        /// <summary>中子星芯子（白色亮点）占半径的比例。</summary>
        public const float NeutronStarCoreScale = 0.55f;
        /// <summary>
        /// 大球一帧最多沿路径补几个圆盘。中子星又小又快，只画终点会画成虚线。
        /// </summary>
        public const int BigBallPaintMaxSteps = 12;
        /// <summary>同阵营：小球撞到引力球/中子星时，把自身这个比例转给对方。</summary>
        public const double GravityBallAbsorbFraction = 0.75d;
        /// <summary>同阵营两颗引力球/中子星相撞时，小的把自身这个比例转给大的。</summary>
        public const double GravityBallMutualFraction = 0.75d;
        public const float ClashCooldown = 0.05f;

        // ─────────────────────────────────────────────────────────────
        // 八、吸血球（第 111 轮）
        //
        // 用户：「和普通大球相似，不同的是在碰撞敌方大球时，如果敌方大球数值
        //       小于自身的 50%，则吸收敌方大球 50% 的数值，否则按正常大球扣血。」
        //
        // 也就是说：吸血球 = **普通大球 + 一条额外的碰撞规则**（涂地/尾流/中子星
        // 那一整套全都照旧）。两条门槛都写成常量，改比例不用翻代码。
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 触发吸血的条件：**敌方大球数值 &lt; 自身数值 × 本值**（0.5 = 50%）。
        /// 不满足就退回普通大球那套「双方各扣 min 的 1/4」。
        /// </summary>
        public const double VampireDrainThreshold = 0.5d;
        /// <summary>一次吸走**敌方**大球数值的这个比例（0.5 = 50%），同时等量加到自己身上。</summary>
        public const double VampireDrainFraction = 0.5d;

        // ── 吸血球的持续特效（第 117 轮：按用户给的 Algodoo poststep 原文重写）──
        //
        // 原文（**整段放在大球的 poststep 里**，所以每个物理步都执行一次）：
        //   tx = scene.addcircle({ radius := 0.01; colorhsva := [0,0,0.2,1];
        //                         _angle := rand.uniform01*2*pi; _distance := radius+0.2;
        //                         timetolive := 1.7;
        //          poststep := (e)=>{ pos = _bball.pos + [sin(_angle),cos(_angle)]*_distance;
        //                             _angle *= 1.02; _distance *= 0.98 } });
        //   scene.addpen({ pos := tx.pos; size := tx.radius*2; fadetime := 0.2;
        //                  colorhsva := tx.colorhsva });
        //
        // 也就是说：**每帧生成的是一个会自己动的圆 + 一支跟着它盖章的笔**。
        //   · 圆每步沿自己的螺旋走（角度 ×1.02、距离 ×0.98），活 1.7 秒；
        //   · 笔每步在圆的当前位置盖一个点，点 0.2 秒淡出 → 尾巴是**运动轨迹**；
        //   · 每帧新建一套、各自随机起始角 → 铺满球面。
        //
        // ★★ 前几轮全错在：把「拖尾」当成预先画好的静态线段，而且所有特效
        //    **共用同一条螺旋**（第 114 轮挤成两簇）→ 后来又干脆删掉螺旋（115/116 轮）。
        //    正确的是**每个特效有自己的螺旋状态**，尾巴由它自己走出来。
        //
        // 实现：Unity 的 `TrailRenderer` 就是 addpen（按时间采样 + 渐隐），
        //   和子弹拖尾同一套画法；SpriteRenderer 圆 = addcircle。
        /// <summary>
        /// 每个**物理帧**生成几套「圆 + 笔」（poststep 每步一次）。
        /// 允许小数：攒够一套才生成（`VampireFx` 内部用累加器）。
        ///
        /// ★★ 第 130 轮（用户）：「每 2 帧生成一个」—— **0.5**：
        ///   第 1 帧攒到 0.5（不生成）、第 2 帧攒到 1.0 生成一套并清零，如此循环。
        ///   于是同时在场套数减半（1.7s × 50Hz × 0.5 ≈ **42 套**，原来约 85），
        ///   球面上那层细弧线会稀疏一档。填 1 = 每帧一套（原文语义）。
        /// </summary>
        public const float VampireFxSpawnPerFrame = 0.5f;
        /// <summary>同时在场的套数上限。1.7s × 60fps ≈ 102，留余量。</summary>
        public const int VampireFxMaxPens = 160;
        /// <summary>一套的寿命（秒）：Algodoo `timetolive := 1.7`。</summary>
        public const float VampireFxLife = 1.7f;
        /// <summary>笔迹点淡出时间（秒）：Algodoo `fadetime := 0.2` = TrailRenderer.time。</summary>
        public const float VampireFxTrailTime = 0.2f;
        /// <summary>
        /// TrailRenderer 采样间距（米）。**必须是 0**：Algodoo 的 addpen 是每步都盖章，
        /// 而 Unity 默认要位移超过 minVertexDistance 才补顶点。螺旋上有位移很小的区段
        /// （尤其起始角小、或半径已收缩时），给 0.004 会让那些区段一个点都不盖 ——
        /// 表现就是「粒子走到某些位置，拖尾直接消失」（第 118 轮用户反馈）。
        /// 0 = 每帧必采样，每套 0.2s × 60fps ≈ 12 个顶点，开销可忽略。
        /// </summary>
        public const float VampireFxTrailMinVertex = 0f;
        /// <summary>
        /// **笔迹宽度（米，固定值）**。原文 `size := tx.radius * 2` 是等宽，
        /// 用户要拖尾比本体再细一点：0.02m ≈ 1.5px（约子弹直径 0.054m 的 1/3）。
        /// </summary>
        public const float VampireFxTrailWidth = 0.02f;
        /// <summary>
        /// **圆的半径（米，固定值）**。用户要求「本体和拖尾都改小，和子弹差不多粗」——
        /// 子弹点半径是 `BulletDotRadius = 0.027`，这里取 0.025（直径 0.05m，略细于子弹）。
        /// 原文 `radius := 0.01` 本来就是绝对单位、不随球大小变，所以这里也用固定值；
        /// 只有球特别小时才按球半径的 12% 夹一下，免得特效比球还大。
        /// </summary>
        public const float VampireFxPenRadius = 0.015f;
        /// <summary>
        /// 螺旋起始距离 / 球半径。Algodoo `_distance := radius + 0.2`，
        /// 0.2 相对球半径 1 就是 +20% ⇒ 从球外 20% 起转，再 ×0.98 收进球内。
        /// </summary>
        public const float VampireFxOrbitStartFrac = 1.2f;
        /// <summary>
        /// 起始角下限（弧度）。原文 `rand.uniform01 * 2π` 可能抽到接近 0，
        /// 而角度是**乘法**增长：起始 0.01 rad 时整套 1.7 秒也只转 0.02 rad，
        /// 粒子几乎不动、笔迹全堆在一个点上，看着也像「特效消失」。
        /// 给 0.35 rad 下限只避开这种退化，不改变正常随机分布。
        /// </summary>
        public const float VampireFxAngleMin = 0.35f;
        /// <summary>角度每 Algodoo 步乘这个数（`_angle *= 1.02`）。</summary>
        public const float VampireFxAngleGainPerStep = 1.02f;
        /// <summary>距离每 Algodoo 步乘这个数（`_distance *= 0.98`）。</summary>
        public const float VampireFxDistanceGainPerStep = 0.98f;
        /// <summary>Algodoo 的 poststep 频率（Hz），用来把 ×1.02 / ×0.98 换算成与帧率无关。</summary>
        public const float VampireFxAlgodooFps = 30f;
        /// <summary>圆和笔迹同色：Algodoo `colorhsva := [0, 0, 0.2, 1]` 深灰。没有白圆头。</summary>
        public static readonly Color VampireFxColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        /// <summary>圆的图层：Algodoo `zdepth := zdepth + 1`（大球 61 之上）。</summary>
        public const int VampireFxSortingOrder = 62;
        /// <summary>笔迹的图层：Algodoo `zdepth := tx.zdepth + 1`（压在圆之上）。</summary>
        public const int VampireFxTrailSortingOrder = 63;

        // ── 引力随「引力源数值」放大 ──
        // 两条引力路共用同一个系数（唯一来源：`GameWorld.GravityValueFactor`）：
        //     factor(数值) = (数值 / GravityValueRef) ^ GravityValuePower
        // 参考点 = 引力球门槛（100M），所以「刚好算引力球」那一档 = ×1。
        // ★ 超过 `NeutronStarGravityKnee` 之后改用更缓的指数（中子星段，见下）。
        // 安全阀：大球路有 `GravityBigBallMaxAccel` + 6 m/s 接近速度闸 + 每步 Δv 上限，
        //         子弹路有 `BulletMaxSpeed` + 每步转角上限（`GravityBulletMaxTurnPerTickDeg`）。
        // ★ 这一项只管**引力源**；被吸球那一项见 `GravityTargetValuePower`。
        /// <summary>引力随数值放大的参考点：取引力球门槛（这一档 = ×1）。</summary>
        public const double GravityValueRef = GravityBallThreshold;   // 100M
        /// <summary>
        /// 引力随数值放大的指数：`加速度 ∝ (数值 / 参考点) ^ 本值`。
        /// 1 = 线性（真引力 `F ∝ m₁`）；调小更平缓；0 = 完全不看数值。
        /// </summary>
        public const float GravityValuePower = 1f;
        /// <summary>
        /// 拐点：数值到 `本值 × GravityValueRef`（= 300M）之前完全线性；
        /// 之后按 `NeutronStarGravityPowerScale` 压平（中子星引力太大，用户要求改小）。
        /// 调小 = 更早开始压平、中子星更弱。
        /// </summary>
        public const float NeutronStarGravityKnee = 3f;
        /// <summary>
        /// 拐点之后的指数倍率：`0.35` ⇒ 1B ×4.6（线性 ×10）、10B ×10（线性 ×100）、
        /// 100B ×23（线性 ×1000）。调小 = 中子星越弱；1 = 关掉压平（回到纯线性）。
        /// </summary>
        public const float NeutronStarGravityPowerScale = 0.35f;
        /// <summary>
        /// 「**被吸球数值**」那一项的指数：`(GravityValueRef / 被吸球数值) ^ 本值`。
        ///   · `0` = **真引力**（`a = G·m₁/r²`，与被吸球质量无关）—— 现在就是这个。
        ///   · `1` = 反比（越小吸得越狠）。那不是引力，是**恒定力**（像吸尘器）。
        /// </summary>
        public const float GravityTargetValuePower = 0f;

        // ── 引力对**子弹**的作用 ──
        // 速度没到上限 → 整个加速度都加上（子弹被拽快）。
        // 速度到上限 → 去掉「顺着速度方向」的那一份，只留横向：不加速也不刹车，只是被掰弯，
        //   所以能一直绕着球转（形成同心圆弧）。
        // ★ 不能"到上限就不施力"（子弹再也不拐弯），也不能整条跳过错开限速（子弹会被吸住绕圈）。

        /// <summary>
        /// 引力球对**子弹**的吸引：`a = 本值 × GravityValueFactor(引力球数值) / 距离`。
        ///
        /// 绕圆的条件是「引力 = v²」：速度顶到上限 `v` 后，圆周运动需要 `v²/r`，
        /// 而引力给的正好是 `本值/r` —— 两者相等时参考点（100M）上**任意半径**都能绕成圆。
        /// 比 v² 大 → 螺旋着往里收；小 → 往外飘、绕不圆。
        ///
        /// ★★ 用户最终的规格：**固定 36，不随速度上限变**。
        ///   现在 `BulletMaxSpeed = 12`，v² = 144，而引力只有 36 —— 是绕圆所需的 **1/4**。
        ///   于是子弹到上限后基本不拐弯、直直飞出去。
        ///   **这正是要的效果**：用户明确说过「子弹**不需要**绕圆」。
        ///
        /// ⚠️ 所以这里**故意不写成 `BulletMaxSpeed * BulletMaxSpeed`**。
        ///   别「顺手修」成派生或 144 —— 那会让子弹重新绕起圈来（之前就是这么踩的）。
        /// </summary>
        public const float GravityOnBullet = 36f;
        /// <summary>
        /// 判定「已经在速度上限上」的余量。必须有：否则会在 11.999 / 12.000 反复横跳，
        /// 子弹一会儿加速一会儿被刹车，最后直直栽进球里来回穿。
        /// </summary>
        public const float GravityBulletCapEpsilon = 0.05f;
        /// <summary>
        /// 子弹**单个引力步**最多被掰多少度（0 = 不限制）。
        /// 中子星旁边加速度能一步把方向拧过 90°，画面上就是「闪现」；
        /// 限的是**每步**、不是一辈子，所以照样能绕圈（25° / 步 ≈ 500°/s，半径 ≥0.7m 的圆轨道不受影响）。
        /// </summary>
        public const float GravityBulletMaxTurnPerTickDeg = 25f;

        /// <summary>
        /// 引力球对**其他大球**的引力（真引力）：
        ///     a = 本值 × (引力球数值 / GravityValueRef) / 距离²
        /// 与被吸球自己的数值**无关**。参考点 100M、1m 处 = 本值。
        ///
        /// 按 100 算：103M 在 1/3/5/8m 处 = 103 / 11.4 / 4.1 / 1.6 m/s²；
        /// 1B = 1000 / 111 / 40 / 15.6；10B = 10000 / 1111 / 400 / 156。
        /// 真正决定观感的是 `GravityBigBallMaxApproachSpeed`（6 m/s）那道闸：
        /// 这个数主要决定「多快被抓住」，不是「吸得多快」。手感不对就只调这一个数。
        /// </summary>
        public const float GravityOnBigBall = 100f;
        /// <summary>
        /// 被吸球数值的下限。真引力（指数 0）下**不影响引力大小**，只是给 Pow 的底数兜底；
        /// 把 `GravityTargetValuePower` 调回非 0 时才起作用。
        /// </summary>
        public const double GravityBigBallMinValue = 1000d;
        /// <summary>
        /// 「朝引力球靠近」的速度闸（米/秒）：到这个速度就不再施加引力。
        /// 只削加速、不刹车（球往外飞时照旧被完整拉回来），防止小球被甩成炮弹来回抽搐。
        /// 真引力下这其实是主要的速度钳制。
        /// </summary>
        public const float GravityBigBallMaxApproachSpeed = 6.0f;
        /// <summary>
        /// 吸过来的加速度上限（防极端数值比把物理求解器甩飞）。
        /// 真引力下基本用不到 —— 6 m/s 的速度闸会先到，留着兜底。
        /// </summary>
        public const float GravityBigBallMaxAccel = 520f;
        /// <summary>
        /// 大球**单个引力步**里速度最多变多少（米/秒，0 = 不限制）。
        /// 加速度再大也只是一步一步加上去 —— 否则一颗正往外飞的球会被一步拧成
        /// 「朝里 6 m/s」，画面上就是直接闪一下（中子星旁边尤其明显）。
        /// </summary>
        public const float GravityBigBallMaxDeltaVPerTick = 2.5f;
        /// <summary>
        /// 两颗**引力球之间**的轨道阻尼：每次引力步把它们「相对的**切向**速度」乘上本值。
        /// 1 = 不阻尼（两颗球会一直互绕，用户说的「二人转」）；
        /// 0.98 ⇒ 约 2~3 秒轨道收进去、最后撞上。
        /// ★ 只削切向：正对着冲过来的那一对不受影响，擦身而过的 slingshot 还在。
        /// </summary>
        public const float GravityBallOrbitDamp = 0.98f;
        /// <summary>引力每隔几个物理帧算一次。</summary>
        public const int GravityTickDivisor = 3;
        /// <summary>子弹吸到这个距离以内就不再施力，免得在球心附近来回加速。</summary>
        public const float GravityBulletStopRadius = 1.2f;
        public const float GravityMinDistance = 0.30f;
        public const float GravityMaxRange = 14f;

        // ── 引力球的持续特效（一圈一圈往外扩的涟漪）──
        /// <summary>一颗涟漪从里扩到外要多久（秒）。</summary>
        public const float GravityFxPeriod = 1.6f;
        // 「增倍球卡住 5 秒向上踢一脚」那套机制已删除，别再翻出来。

        // ── 增倍球的**白色粒子拖尾**（已经停用，见下面阈值）──
        /// <summary>
        /// 超过这个数值就把 TrailRenderer 换成白色粒子。
        ///
        /// ★★ 用户：「把这种拖尾都改成**原来的**，不好看」——
        ///   白色小圆点盖出来的拖尾在球后面糊成一串白团（球越大越明显），
        ///   所以**关掉整套粒子拖尾**，所有球一律用原来那条 TrailRenderer 丝带拖尾。
        ///
        /// 实现方式：把阈值抬到 double 的极大值 —— 任何真实数值都到不了，
        ///   `wantParticles` 恒为 false，粒子池永远不会被建出来（`EnsureParticles` 也就不跑）。
        ///   想恢复的话把这里改回 `100000000d`（100M）即可，下面的参数都还在。
        /// </summary>
        public const double MultiplierParticleThreshold = double.MaxValue;   // 永远达不到 = 停用
        /// <summary>粒子池大小（循环复用，不会增长）。</summary>
        public const int MultiplierParticleCount = 28;
        /// <summary>一颗粒子从亮到灭的时间（秒）。</summary>
        public const float MultiplierParticleLife = 0.55f;
        /// <summary>每隔多久盖一颗（秒）。</summary>
        public const float MultiplierParticleInterval = 0.022f;
        /// <summary>粒子大小 = 球半径 × 本系数。</summary>
        public const float MultiplierParticleScale = 0.55f;
        /// <summary>刚盖下去时的白度。</summary>
        public const float MultiplierParticleAlpha = 0.9f;
        /// <summary>粒子拖尾的图层：压在球本体（20）之下、区背景之上。</summary>
        public const int MultiplierParticleSortingOrder = 18;
        // ── 左栏背景的「游荡投影相机」──
        /// <summary>投影方框的边长（战场世界单位）。</summary>
        public const float WanderCamSize = 7f;
        /// <summary>投影方框的移动速度（米/秒，全程匀速，碰墙原速反弹）。</summary>
        public const float WanderCamSpeed = 0.45f;
        /// <summary>投影到左栏背景的透明度。</summary>
        public const float WanderCamAlpha = 0.25f;
        /// <summary>投影渲染的分辨率（正方形）。</summary>
        public const int WanderCamResolution = 384;
        /// <summary>投影的图层：增倍区背景之上、其它一切之下。</summary>
        public const int WanderCamSortingOrder = -9;
        /// <summary>
        /// 投影相机每 N 帧渲一次（**保持 1**）。调低会让匀速移动的取景框一卡一卡，
        /// 而它本来就很便宜（384²、不看子弹层）。
        /// </summary>
        public const int WanderCamInterval = 1;

        /// <summary>画面装饰：暗角强度（0 = 关）。</summary>
        public const float VignetteStrength = 0f;   // 关掉：世界里的球和 IMGUI 副本一个吃暗角一个不吃，球压到文字上会亮一下

        // ── 增倍区的特效 ──
        /// <summary>球过门时，那一块方块闪一下的时长（秒）。</summary>
        public const float GateFlashTime = 0.28f;
        /// <summary>乘法门被「点亮」时的颜色（平时是深灰）。</summary>
        public static readonly Color GateFlashColor = new Color(1f, 0.94f, 0.72f);
        /// <summary>画面装饰：战场里飘的尘埃数量。</summary>
        public const int DustCount = 46;
        /// <summary>尘埃的图层：压在地图之上、其它一切之下。</summary>
        public const int DustSortingOrder = -60;
        /// <summary>涟漪最大扩到球半径的多少倍。</summary>
        public const float GravityFxMaxScale = 2.1f;
        /// <summary>
        /// 涟漪上限倍率 ×= `1 + 本值 × ln(GravityValueFactor)`（参考点及以下 ×1）。
        /// 用 ln 而不是直接乘：数值跨好几个数量级，直接乘会瞬间糊满屏。
        /// 100M ×1.00 / 1B ×1.50 / 10B ×1.81 / 100B ×2.13（中子星段压平后）。
        /// </summary>
        public const float GravityFxMaxScaleGain = 0.25f;
        /// <summary>上面那个放大倍数的上限（别让大球把涟漪铺满战场）。</summary>
        public const float GravityFxMaxScaleGainCap = 3f;
        /// <summary>刚冒出来时的透明度。</summary>
        public const float GravityFxAlpha = 0.55f;
        /// <summary>同时存在几圈。</summary>
        public const int GravityFxRingCount = 3;
        /// <summary>涟漪的图层：在大球本体（60/61）之下，这样只看得见球外面那一圈。</summary>
        public const int GravityFxSortingOrder = 57;

        // ── 炮台的持续特效 ──
        /// <summary>护盾边缘往里一圈的涟漪周期（秒），是「慢慢呼吸」不是「发功」。</summary>
        public const float TurretFxPeriod = 2.4f;
        /// <summary>同时存在几圈（相位错开）。</summary>
        public const int TurretFxRingCount = 2;
        /// <summary>呼吸圈的外扩倍率（1 = 正好贴护盾边）。</summary>
        public const float TurretFxMaxScale = 1.10f;
        /// <summary>呼吸圈起始倍率（< 1 = 从护盾里侧往外扩）。</summary>
        public const float TurretFxMinScale = 0.82f;
        /// <summary>呼吸圈的透明度（要**很轻** —— 它是常驻的，重了会腻）。</summary>
        public const float TurretFxAlpha = 0.30f;
        /// <summary>本体上那圈**自转**的细环，转一圈几秒。0 = 不转。</summary>
        public const float TurretFxSpinPeriod = 6f;
        /// <summary>自转环的半径 = 本体半径 × 几。</summary>
        public const float TurretFxSpinRadius = 0.92f;
        public const float TurretFxSpinAlpha = 0.35f;
        /// <summary>炮台特效的图层：在护盾亮边（7）之上、炮台本体（10）之下。</summary>
        public const int TurretFxSortingOrder = 9;

        // ── ★ 复活特效 ──
        public const float RespawnFxLife = 1.10f;
        /// <summary>复活冲击环最大扩到炮台半径的几倍（炮台半径才 0.30m，所以这个数不小）。</summary>
        public const float RespawnFxRadiusScale = 8.0f;

        public const double BigBallJetThreshold = 1000000d;      // 1M
        public const double BigBallJetBulletValue = 4d;   // 规格：尾流单发数值 = 4
        public const float BigBallJetBulletSpeed = 1.7f;   // 规格：尾流初速继续砍（2.6 → 1.7）
        /// <summary>
        /// 尾流的反冲：**每喷一发**直接给这么多速度增量（大球质量太大，给冲量推不动）。
        /// 只有在大球「几乎没动」时才给，脱离慢速就只喷子弹（见 `BigBallJetBoostMaxSpeed`）。
        /// </summary>
        public const float BigBallJetSpeedGain = 0.0037f;
        /// <summary>
        /// 速度低于这条线（m/s）才吃尾流加速；到它以上就只喷子弹、不再加速。
        /// </summary>
        public const float BigBallJetBoostMaxSpeed = 0.25f;
        public static readonly bool BigBallJetConsumesValue = true;
        /// <summary>尾流是**一颗一颗随机**喷的，不是一排一排。这个值 = 每秒喷几发。</summary>
        public const float BigBallJetRate = 90f;
        public const int BigBallJetBulletCount = 22;   // （保留给自检用）   // 规格：密度提升
        /// <summary>喷口离球心多远（在半径之外再留一点）。</summary>
        public const float BigBallJetMuzzleGap = 0.15f;
        public const float BigBallJetSpreadDeg = 22f;
        /// <summary>规格：开火要从子弹数里扣掉**这一发的数值**（这样大球加到的值 = 文字掉的值）。</summary>
        public static readonly bool FiringConsumesBulletValue = true;

        // ─────────────────────────────────────────────────────────────
        // 八、死亡 / 亡语 / 复活
        // ─────────────────────────────────────────────────────────────

        public static readonly bool TurretDiesOnBodyHit = true;
        public const float RespawnDelay = 60f;
        public const double DeathrattleBallValueScale = 1.0d;
        public const double DeathrattleBulletValueScale = 1.0d;
        public static readonly bool DeathrattleSpawnBulletBall = true;

        // ── 亡语大球「怎么生」──
        // 一帧里在同一个点生十几颗，会被求解器瞬间推开、一步跨过 0.30m 的墙，
        // 所以改成「排队 + 每帧放几颗 + 沿向日葵螺旋散开」。
        /// <summary>排队中的亡语大球每帧最多放几颗。</summary>
        public const int DeathrattleSpawnPerFrame = 3;
        /// <summary>向日葵螺旋的起始环半径（米）。</summary>
        public const float DeathrattleSpawnRadius = 0.45f;
        /// <summary>第 k 颗的环半径 = 起始 + 这个值 × √k。</summary>
        public const float DeathrattleSpawnSpacing = 0.42f;
        /// <summary>出生点离地图边缘至少留这么多米。</summary>
        public const float DeathrattleSpawnEdgeMargin = 0.6f;

        /// <summary>
        /// 兜底：每物理步把大球的圆心夹回 `[半径, 地图边长 − 半径]`（墙的内壁就在 0 和边长上）。
        /// 防的是「求解器在深重叠时把球的位置一下顶到墙外」—— 连续性检测防不了这一种。
        /// </summary>
        public static readonly bool BigBallContainInArena = true;

        /// <summary>复活时给的增倍球个数 = `floor(ln(大球值) × 系数 + 偏移)`。</summary>
        public const float RespawnBallCountLogScale = 0.8f;
        public const float RespawnBallCountLogBias = -4f;
        public const double RespawnBulletsFraction = 0.1d;
        public const double RespawnShieldFraction = 0.05d;
        public const double RespawnBallValueKeep = 0.6d;
        public static readonly bool RespawnAssignsBulletsAndShield = true;
        public static readonly bool RespawnConsumesBall = false;

        /// <summary>亡语大球出现时的初速度（很慢，别让它像大运一样冲出去）。</summary>
        public const float BigBallSpawnSpeed = 0.12f;   // 规格：大球初始速度太快
        /// <summary>
        /// 大球速度上限（米/秒）。给到 6（= BulletMaxSpeed）：撞上引力球能正常弹开，
        /// 而 60fps 下一帧只走 0.1m，不会瞬移。
        /// </summary>
        public const float BigBallMaxSpeed = 6.0f;
        /// <summary>
        /// 大球慢到这个速度以下持续一会儿，就轻轻推一下（防卡墙角）。
        /// ★ 必须**低于** `BigBallJetBoostMaxSpeed`（0.25）：否则尾流巡航速度会被判成"卡住"、
        /// 每隔一会儿被随机换方向。自检里有断言守着这条。
        /// </summary>
        public const float BigBallStuckSpeed = 0.08f;
        /// <summary>低于上面那个速度要**持续这么久**才判定为卡住（秒）。</summary>
        public const float BigBallStuckRescueTime = 1.2f;
        /// <summary>卡住时踢一下的速度（米/秒），方向是**离开最近那面墙**。</summary>
        public const float BigBallStuckKickSpeed = 0.35f;
        /// <summary>
        /// 大球质量上限。质量公式是 `数值 × 0.9 / 800`（密度 × 面积，半径约掉）；
        /// 上限太低会把大球压成和小的差不多重（「2M 推走 80M」就是这么来的）。
        /// 1e9 足够让 8900 亿以下都按公式走。
        /// </summary>
        public const float BigBallMaxMass = 1e9f;
        public const float HomingForce = 6f;
        /// <summary>复活判定：大球开到离出生点这么近（再 + 球半径）才算抵到炮台。</summary>
        public const float HomeRadius = 0.7f;

        // ─────────────────────────────────────────────────────────────
        // 九、性能
        // ─────────────────────────────────────────────────────────────

        /// <summary>渲染网格的顶点缓冲容量（**不是**子弹上限，子弹没有上限；超了会自动扩容）。</summary>
        public const int BulletRenderCapacity = 4096;
        public const int BulletPoolPrewarm = 4096;   // 齐射一次 512 发；预热不够会当场 new 一堆 GameObject（跳帧）
        /// <summary>池空了一次建这么多（一颗子弹要建 2 个 GameObject + 一个 TrailRenderer，散着建会卡）。</summary>
        public const int BulletPoolGrowBatch = 128;
        /// <summary>
        /// 每帧最多做多少次「子弹打子弹」的配对比较。同一格扎堆时是 O(k²)，封顶宁可漏判也别掉帧。
        /// </summary>
        public const int MaxBulletPairChecks = 20000;
        // 子弹已改成按渲染帧推进（见 BulletPool.Update），位置本来就是这一帧的，无需插值。
        public static readonly bool BulletInterpolate = false;
        /// <summary>一帧最多让子弹推进多少秒。卡一下的时候别让子弹瞬移穿墙。</summary>
        public const float BulletMaxStepTime = 0.05f;
        /// <summary>回收时保留的空闲对象数。**不再 Destroy** —— 海量子弹下那个开销会直接拖垮帧率。</summary>
        public const int BulletPoolKeepFree = 12000;
        /// <summary>
        /// 炮台每转过这个角度就发一发（发数/秒 = 转速 ÷ 本值）。
        /// 「密度」靠把这个角度做小，而不是每单位角度多发几发；和 `BulletValueDivisor` 成对。
        /// </summary>
        public const float BulletAngleStepDeg = 0.025f;
        /// <summary>一帧最多发几发（近防猛转时一帧能扫过 21°，不封顶会瞬间喷出几百发）。</summary>
        public const int BulletMaxPerFrame = 64;
        /// <summary>
        /// 炮台**几乎不转**时的时间兜底（发/秒）。炮管停住时纯按角度算会一发都不发（哑掉），
        /// 所以退回按时间发、方向就是当前朝向。
        /// </summary>
        public const float BulletIdleFireRate = 800f;
        /// <summary>低于这个转角（度）就当「没在转」，走时间兜底。</summary>
        public const float BulletIdleSweepDeg = 0.01f;

        // ── 性能相关的开关 ──
        /// <summary>子弹占用的 layer。子弹之间互不碰撞（上万颗两两检测是最大的开销）。</summary>
        public const int BulletLayer = 8;
        /// <summary>
        /// `Physics2D.bounceThreshold`：相对速度低于它的碰撞干脆不弹。
        /// 工程默认 1.0 会把慢速撞墙的大球直接"钉"在墙上。自检也读这里，保证同源。
        /// </summary>
        public const float BounceThreshold = 0.05f;
        /// <summary>浮力泡泡占用的层（泡泡之间不互撞：既不好看也没用，关了能省一大截物理）。</summary>
        public const int BubbleLayer = 10;
        // 涂色不再按帧限额轮转（那正是「涂色断断续续」的来源），
        // 现在靠 OwnedBy 预筛 + 「刷一次就耗尽数值」天然限流。
        /// <summary>
        /// 领土贴图每几帧上传一次（**保持 1**）。地图 1024² RGBA = 4MB，
        /// 隔帧传会让相邻两帧一轻一重 → 录出来一顿一顿；每帧都传节奏才是平的。
        /// </summary>
        public const int MapFlushIntervalFrames = 1;
        /// <summary>一次 Flush 最多拷贝多少个 64×64 块（脏块留到下一帧继续，边界看不出来）。</summary>
        public const int MapMaxBlocksPerFlush = 96;
        // （BulletPoolKeepFree 见上面性能开关那一组）
        public const int MaxDirtyUploadTexels = 1024 * 1024;
        public const int MaxClashResolvePerFrame = 64;

        // ─────────────────────────────────────────────────────────────
        // 十、视觉与显示
        // ─────────────────────────────────────────────────────────────

        public const float LeftPanelViewportWidth = 0.42f;
        /// <summary>
        /// 左栏只在**宽度**方向留的白边。高度方向故意不留 ——
        /// 两边都顶满栏高才等高（见 `CameraRig.Build`）。
        /// </summary>
        public const float LeftPanelWidthMargin = 1.03f;
        public static readonly bool LeftPanelUsesBattlefieldProjection = false;

        public const float BallLabelScale = 0.024f;
        public const float ResourceLabelScale = 0.030f;

        public static readonly bool ShowOneDecimalUnder10 = true;
        public static readonly bool StripTrailingZeroDecimal = false;

        public const char GroupSeparator = ' ';
        public const char DecimalSeparator = ',';

        public const float TimeScale = 1f;

        // ── 录屏用的「定步长」模式（F2 开关）──
        // Unity 默认「实时优先」：卡顿时一帧连跑几个物理步，丢的是帧。
        // 开 `Time.captureFramerate` 后 `Time.deltaTime` 被钉死成 1/帧率，
        // 于是一帧 = 一个固定模拟步，永不跳帧（机器慢就变慢动作）。
        /// <summary>
        /// 物理步长（秒）。和 `CaptureFramerate` 是一对：一帧 = 一个物理步时，
        /// 必须 `CaptureFramerate == 1/本值`（自检有断言守着）。
        /// </summary>
        public const float FixedTimestep = 1f / 60f;
        /// <summary>定步长模式：一帧固定推进多少分之一秒（= 每秒模拟步数）。</summary>
        public const int CaptureFramerate = 60;
        /// <summary>启动时就打开定步长（平时关着，录屏时按 F2 开）。</summary>
        public static readonly bool CaptureModeAtStart = false;
        /// <summary>非定步长时的目标帧率（定步长模式下会被换成 `CaptureFramerate`）。</summary>
        public const int TargetFrameRate = 60;

        // ── 炮台牌子上「子弹数」堆叠文字的间距 ──
        /// <summary>子弹数那几行之间的行高系数。</summary>
        public const float TurretBulletLineFactor = 0.98f;
        /// <summary>
        /// 子弹数的字间距系数（1 = 字体默认）。IMGUI 没有 letter-spacing，
        /// 所以是逐字符按「字符宽度 × 本值」步进画的。
        /// </summary>
        public const float TurretBulletCharFactor = 0.94f;

        /// <summary>调试统计面板（F1）。默认关，免得盖住左栏。</summary>
        public static bool ShowStats = false;
    }
}
