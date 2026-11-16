using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 武器**种类**（不是格号！格数见 <see cref="Cfg.WeaponSlots"/>）。
    /// 用户第 87 轮 图二：**直射回来了**。
    /// </summary>
    public enum WeaponSlotKind
    {
        Direct = 0,        // 直射：炮台不转，朝一个方向打（±5° 抖）
        Scatter = 1,       // 散射：炮台边转边撒
        Volley = 2,        // 齐射
        Shield = 3,        // 护盾
        BigBall = 4,       // 大球
        Ciws = 5,          // 近防炮（第 103 轮新增）
        Vampire = 6,       // 吸血球（第 111 轮新增，在大球右边一格）
    }

    /// <summary>
    /// 武器区**一格**的定义。两种：
    ///   · `Gate &gt; 1` → 这一格是**乘法门**（x2）：球落进来**不消耗**，数值 ×Gate 后重投；
    ///   · 否则 → 这一格是武器，看 <see cref="Kind"/>。
    /// </summary>
    public struct WeaponSlotDef
    {
        public WeaponSlotKind Kind;
        public int Gate;
        public bool IsGate { get { return Gate > 1; } }
    }

    /// <summary>
    /// 一个阵营的全部运行时状态。炮台、子弹池、护盾、增倍球、大球都挂在这里。
    /// </summary>
    public class Faction
    {
        public readonly int Index;
        public readonly Color Color;
        public readonly Color TextColor;      // 黑字还是白字 —— 阵营表里手填（FactionDef.TextBlack）

        public double Bullets;                // 子弹数量
        public double Shield;                 // 护盾量
        /// <summary>
        /// 增倍球的**累计增益**（给左上角的「总值计数条」用）：
        /// 每次过乘法门 / 武器区的 x2 格，把 **(翻倍后 − 翻倍前)** 累加进来。
        /// 例：1 过 ×4 → +3；256 过 ×2 → +256。
        /// </summary>
        public double GainTotal;
        /// <summary>护盾半径（米）。缓动后已乘上 Cfg.ShieldVisualScale，可直接当世界半径用。</summary>
        public float ShieldRadius;

        public Turret Turret;
        public Vector2 HomePosition;          // 出生点（战场坐标）
        /// <summary>由 GameWorld 在构建时注入，用于实时读领土格数。</summary>
        public Battlefield Field;

        public readonly List<MultiplierBall> Balls = new List<MultiplierBall>();
        public readonly List<BigBall> BigBalls = new List<BigBall>();

        public bool Alive = true;
        /// <summary>炮台死亡时刻（Time.time）。存活时为 -1。</summary>
        public float DeathTime = -1f;
        /// <summary>下一次「每 60 秒 +1 个增倍球」的时间点。</summary>
        public float NextBallGrantTime;

        /// <summary>当前占有的像素数。实时从战场读，避免出现「快照过期」的坑。</summary>
        public int TilesOwned
        {
            get { return Field != null ? Field.PixelsOf(Index) : 0; }
        }

        // （武器槽的计数器已经挪到 WeaponZone 上了：区是四家共享的，
        //   计数本来就该是区级的，和乘法门的 PassCount 一个机制 —— 别再加回阵营上）
        /// <summary>炮台当前的射击模式（由增倍球落进武器区决定）。</summary>
        /// ★★ 规格（用户第 89 轮）：**开局是散射**（边转边撒）。
        ///   （第 87 轮我一度按「直射是基础状态」把它改成了直射 —— 用户纠正：还是散射开局。）
        ///   直射要去武器区选出来，选完炮台才停下来朝一个方向打。
        public WeaponSlotKind TurretMode = WeaponSlotKind.Scatter;


        public int ScatterBallIndex;
        public int VolleyBallIndex;

        public Faction(int index)
        {
            Index = index;
            Color = Palette.ColorOf(index);
            // ★ 文字黑/白是**手动**填的（阵营表 FactionDef.TextBlack，设置面板可覆盖），
            //   不再由颜色推算 —— 所以走 GameSettings（没改过就是阵营表原值）。
            TextColor = GameSettings.TextBlackOf(index) ? Color.black : Color.white;
            Bullets = Cfg.StartBullets;
            Shield = Cfg.StartShield;
            ShieldRadius = 0.1f;
            ResetShieldRadiusToTarget();
        }

        public Color Faded
        {
            get { return Palette.Alpha(Color, 0.45f); }
        }

        /// <summary>
        /// 护盾半径：`radius += ((ln(护盾值)/18 + 0.1) - radius) / 5`，每 1/30 秒一步
        /// （Algodoo 原式，它跑 30 fps）。
        /// 目标值上乘了 Cfg.ShieldVisualScale —— 否则 1M 护盾只有 0.868m，
        /// 投到 96m 宽的地图上几乎看不见（视频里占地图宽度约 12%）。
        /// </summary>
        public void StepShieldRadius(float dt)
        {
            float target = ShieldTargetRadius();
            // ★ 规格：**护盾的增长/减少/破盾都要有过渡动画**。
            //   Algodoo 里是「每 30fps 的一步收掉 1/5」（和帧率耦合）；
            //   这里换成等价、但**与帧率无关**的写法：一步收 1/5 ⇒ `1 - (1-1/5)^(dt×30)`。
            //   （60fps 和 120fps 观感完全一样，30fps 时正好等于原式。）
            float stepK = Mathf.Clamp(Cfg.ShieldRadiusLerpPerStep, 0.001f, 0.999f);
            float k = 1f - Mathf.Pow(1f - stepK, Mathf.Max(dt, 0f) * 30f);
            ShieldRadius = Mathf.Clamp(Mathf.Lerp(ShieldRadius, target, k), 0f, Cfg.RadiusMax);
        }

        /// <summary>
        /// 护盾半径的目标值（世界单位，已乘 `ShieldVisualScale`）。
        /// ★ 护盾归 0 时目标是 **0** —— 破盾就是让同一条缓动自己把半径缩到没有，
        ///   而不是另外播一段线性的收缩动画。
        /// </summary>
        float ShieldTargetRadius()
        {
            if (Shield <= 0d) return 0f;
            return Mathf.Max((Cfg.LogV((float)Shield) / Cfg.ShieldRadiusLogDivisor
                              + Cfg.ShieldRadiusBias) * Cfg.ShieldVisualScale,
                             Cfg.ShieldMinRadius);
        }

        public bool HasLiveTurret
        {
            get { return Turret != null && !Turret.IsDead; }
        }

        /// <summary>把护盾半径直接设成公式目标值（出生时用，免得从 0.1 慢慢长）。</summary>
        public void ResetShieldRadiusToTarget()
        {
            ShieldRadius = Mathf.Clamp(ShieldTargetRadius(), 0f, Cfg.RadiusMax);
        }

        /// <summary>还没有被亡语消耗掉的存活增倍球数量。</summary>
        public int LiveBallCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Balls.Count; i++)
                    if (Balls[i] != null && !Balls[i].Consumed) n++;
                return n;
            }
        }

        /// <summary>取第 i 个存活增倍球（越界返回 null）。</summary>
        public MultiplierBall BallAt(int i)
        {
            int n = 0;
            for (int k = 0; k < Balls.Count; k++)
            {
                var b = Balls[k];
                if (b == null || b.Consumed) continue;
                if (n == i) return b;
                n++;
            }
            return null;
        }
    }
}
