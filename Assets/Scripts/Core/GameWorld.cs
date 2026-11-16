using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 全局管理器：持有战场、增倍区、子弹池、四个阵营，并驱动所有跨对象的规则
    /// （每 60 秒发球、护盾半径缓动、引力、复活回收、胜负判定）。
    /// </summary>
    public class GameWorld : MonoBehaviour
    {
        public static GameWorld Instance { get; private set; }

        /// <summary>
        /// 这一局已经打了多少秒（给「散弹解封」用：开局 8 分钟内单发数值封顶）。
        /// ★ 用 `Time.time - _matchStart`：
        ///   · `Time.time` 受 `timeScale` 影响，**暂停的那段不算**进对局时间（正合适）；
        ///   · `Build()` 里把起点重置成当前时间，所以设置面板重开一局之后
        ///     解封计时**从头开始** —— 否则玩到第 9 分钟再换个阵营数，
        ///     新开的一局会直接白送「已解封」。
        /// </summary>
        public static double MatchElapsed { get { return Time.time - _matchStart; } }
        static float _matchStart;

        public Battlefield Field;
        public MultiplierZone Zone;
        public BulletPool Bullets;
        public Transform BattleRoot;
        public readonly List<Faction> Factions = new List<Faction>();

        public int WinnerIndex = -1;
        public float Elapsed;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// ★ 设置面板换阵营数 / 换颜色时会**整棵树拆掉重搭**（见 `Bootstrap.TearDown`）。
        /// `Destroy` 是延迟到帧末的，但 `Instance` 必须**立刻**清掉 ——
        /// 否则下一帧新世界 `Build()` 之前，任何读 `Instance` 的代码都会摸到已经死掉的旧世界。
        /// </summary>
        public static void ClearInstance()
        {
            Instance = null;
        }

        // ─────────────────────────────────────────────────────────────
        // 构建
        // ─────────────────────────────────────────────────────────────

        public static GameWorld Build()
        {
            var go = new GameObject("GameWorld");
            var world = go.AddComponent<GameWorld>();

            // 不能只依赖 Awake：编辑器非 Play 状态下 AddComponent 不会调 Awake，
            // 那样 Instance 就是 null，亡语 / 复活这些回调会静默失效。
            Instance = world;

            // ★ 重开一局：对局计时归零（见 MatchElapsed）
            _matchStart = Time.time;

            var battleRoot = new GameObject("BattleRoot").transform;
            world.BattleRoot = battleRoot;

            // 战场（右栏）：1024×1024 像素
            var fieldGo = new GameObject("BattlefieldRoot");
            world.Field = fieldGo.AddComponent<Battlefield>();
            world.Field.Build(Cfg.MapTexels, Cfg.MapWorldSize, Cfg.FactionCount);

            // 增倍区（左栏）
            world.Zone = MultiplierZone.Build(Cfg.ZoneCenter, null);

            // 子弹池
            world.Bullets = BulletPool.Build(battleRoot);
            ScreenDecor.Build(battleRoot, Cfg.MapWorldSize);   // 画面装饰：飘的尘埃

            // 四个阵营
            for (int i = 0; i < Cfg.FactionCount; i++)
            {
                var f = new Faction(i);
                f.HomePosition = FactionSet.Get(i).Home;   // ★ 第 100 轮：出生点由阵营表给
                f.Field = world.Field;
                world.Factions.Add(f);
            }

            // 炮台
            for (int i = 0; i < world.Factions.Count; i++)
            {
                var f = world.Factions[i];
                f.Turret = Turret.Create(f, battleRoot);   // 炮台在**战场**四角，不在增倍区里
                f.NextBallGrantTime = Time.time + Cfg.MultiplierBallGrantInterval;
            }

            // 开局：各自涂抹周围领土
            for (int i = 0; i < world.Factions.Count; i++)
                world.Field.PaintStartingTerritory(world.Factions[i].HomePosition, i, Cfg.StartTerritoryRadius);

            // 开局：每阵营 2 个增倍球
            for (int i = 0; i < world.Factions.Count; i++)
                for (int k = 0; k < Cfg.StartMultiplierBalls; k++)
                    world.Zone.SpawnBall(world.Factions[i], Cfg.NewBallStartValue);

            return world;
        }

        // ─────────────────────────────────────────────────────────────
        // 每帧
        // ─────────────────────────────────────────────────────────────

        void Update()
        {
            Elapsed += Time.deltaTime;
            float now = Time.time;

            for (int i = 0; i < Factions.Count; i++)
            {
                var f = Factions[i];
                f.StepShieldRadius(Time.deltaTime);

                // 每 60 秒 +1 个增倍球 —— 但炮台死了、还没复活之前**不再发球**
                if (!f.Alive)
                {
                    f.NextBallGrantTime = now + Cfg.MultiplierBallGrantInterval;
                }
                else if (now >= f.NextBallGrantTime)
                {
                    f.NextBallGrantTime += Cfg.MultiplierBallGrantInterval;
                    Zone.SpawnBall(f, Cfg.NewBallStartValue);
                }

                CheckRespawn(f, now);
            }

            StepVolleys();
            StepCiws(Time.deltaTime);
            DrainDeathrattle();

            // 贴图上传节流：领土 30Hz 更新看不出差别，但省掉一半的 GPU 上传
            if (Time.frameCount % Mathf.Max(Cfg.MapFlushIntervalFrames, 1) == 0) Field.Flush();
            CheckWinner();
        }

        // ─────────────────────────────────────────────────────────────
        // 亡语：炮台死亡时，增倍球逐个变成独立的大球；子弹单独算成一颗大球
        // ─────────────────────────────────────────────────────────────

        struct PendingBall
        {
            public Faction Faction;
            public double Value;
            public Vector2 Pos;
        }

        readonly List<PendingBall> _pendingBalls = new List<PendingBall>();

        /// <summary>
        /// ★★ 第 93 轮（用户）：「**亡语球过多导致大球穿到墙里去**」。
        ///
        /// 根因是**一帧之内**在炮台同一个点上生出十几颗大球，然后它们一起长大：
        ///   · 同一帧 = 一帧里有十几次「建 GameObject + 爆炸特效 + 建刚体」，本身就是一次卡顿；
        ///   · 同一个点 = 求解器要把十几个重叠的圆一次性推开，推开的速度可能大到
        ///     让球**一步跨过 0.30m 厚的墙**（连续性检测救不了「被位置推出去」这种）。
        ///
        /// 所以改成**排队、每帧放几颗、并且沿一条向日葵螺旋线散开**：
        /// 出生点之间不再重合，重叠的成长也就不会互相弹飞。
        /// （再加一道 `BigBall` 里的「不许出界」兜底 —— 见 `ClampInsideArena`。）
        /// </summary>
        public void OnTurretDied(Faction f)
        {
            Vector2 origin = f.Turret != null ? f.Turret.WorldPosition : f.HomePosition;
            int k = 0;
            for (int i = 0; i < f.Balls.Count; i++)
            {
                var b = f.Balls[i];
                if (b == null || b.Consumed) continue;
                double v = b.Value * Cfg.DeathrattleBallValueScale;
                Explosion.Spawn(b.transform.position, b.Radius, f.Color, BattleRoot);   // 死亡动画
                b.Consume();
                if (v > 0.0002d) QueueBall(f, v, SpawnRing(origin, k++));
            }
            f.Balls.Clear();

            if (Cfg.DeathrattleSpawnBulletBall && f.Bullets > 0.0002d)
                QueueBall(f, f.Bullets * Cfg.DeathrattleBulletValueScale, SpawnRing(origin, k));

            f.Bullets = 0d;
            f.Shield = 0d;
        }

        /// <summary>向日葵螺旋上的第 k 个落点（黄金角 137.5°），并夹进战场内。</summary>
        public static Vector2 SpawnRing(Vector2 origin, int k)
        {
            float ang = k * 2.3999632f;                          // 黄金角
            float rad = Cfg.DeathrattleSpawnRadius
                        + Cfg.DeathrattleSpawnSpacing * Mathf.Sqrt(k);
            float m = Cfg.DeathrattleSpawnEdgeMargin;
            float w = Cfg.MapWorldSize;
            return new Vector2(
                Mathf.Clamp(origin.x + Mathf.Cos(ang) * rad, m, w - m),
                Mathf.Clamp(origin.y + Mathf.Sin(ang) * rad, m, w - m));
        }

        void QueueBall(Faction f, double value, Vector2 pos)
        {
            _pendingBalls.Add(new PendingBall { Faction = f, Value = value, Pos = pos });
        }

        /// <summary>把排队中的亡语大球每帧放出来几颗（一帧全放会卡，也容易挤穿墙）。</summary>
        void DrainDeathrattle()
        {
            if (_pendingBalls.Count == 0) return;
            int n = Mathf.Min(Cfg.DeathrattleSpawnPerFrame, _pendingBalls.Count);
            for (int i = 0; i < n; i++)
            {
                var p = _pendingBalls[i];
                SpawnBigBall(p.Faction, p.Value, p.Pos);
            }
            _pendingBalls.RemoveRange(0, n);
        }

        /// <summary>「选武器」时从炮台打出一把子弹，总数值 = 这颗增倍球的数值。</summary>
        public void FireWeapon(Faction f, double value, bool scatter)
        {
            if (f == null || f.Turret == null || Bullets == null) return;
            int n = Mathf.Max(1, Cfg.WeaponBurstBullets);
            double each = value / n;
            float baseAngle = f.Turret.AimAngle;
            float spread = scatter ? Cfg.WeaponScatterDeg : Cfg.WeaponDirectDeg;
            Vector2 origin = f.Turret.WorldPosition;

            for (int i = 0; i < n; i++)
            {
                float a = (baseAngle + Random.Range(-spread, spread)) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Bullets.Rent(f, origin + dir * (f.Turret.CoreRadius + 0.2f), dir,
                             Cfg.BulletSpeed * Random.Range(0.9f, 1.1f), each,
                             Cfg.BulletLifetime, false);
            }
        }

        /// <summary>
        /// **齐射**：一次打出 512 发，在 ±30 度的扇形里撒开。
        /// 每发的数值 = 齐射数值 / 512 + 1（和「单发子弹数值」同一条公式的形状）。
        /// </summary>
        /// <summary>正在分批撒出去的齐射。</summary>
        class PendingVolley
        {
            public Faction F;
            public double Value;
            public int Remaining;
            public float Angle;
        }

        readonly System.Collections.Generic.List<PendingVolley> _volleys =
            new System.Collections.Generic.List<PendingVolley>();

        /// <summary>还没撒完的齐射批次数（自检用）。</summary>
        public int PendingVolleys { get { return _volleys.Count; } }

        /// <summary>把待发的齐射全部撒完（自检用，正常是每帧自己走）。</summary>
        public void FlushVolleysForTest()
        {
            for (int guard = 0; guard < 64 && _volleys.Count > 0; guard++) StepVolleys();
        }

        public void FireVolley(Faction f, double value)
        {
            if (f == null || f.Turret == null || Bullets == null) return;
            // ★ 512 发**一帧全放会卡**，排进队列分十几帧撒完
            _volleys.Add(new PendingVolley
            {
                F = f, Value = value,
                Remaining = Mathf.Max(1, Cfg.VolleyBulletCount),
                Angle = f.Turret.AimAngle
            });
        }

        // ─────────────────────────────────────────────────────────────
        // ★★★ 近防炮（CIWS，第 103 轮新增）
        // ─────────────────────────────────────────────────────────────
        readonly List<Ciws> _ciws = new List<Ciws>();
        /// <summary>场上的近防炮（HUD 要画文字）。只读。</summary>
        public List<Ciws> CiwsUnits { get { return _ciws; } }

        /// <summary>
        /// 增倍球在武器区选了「近防炮」→ 从炮台打出一颗**不参与物理**的小球。
        /// 它会直线飞，直到碰到**非己方领土**才展开（见 `Ciws.Deploy`）。
        /// </summary>
        public Ciws SpawnCiws(Faction f, double payload)
        {
            if (f == null) return null;
            Vector2 pos = f.Turret != null ? f.Turret.WorldPosition : f.HomePosition;
            float ang = f.Turret != null ? f.Turret.AimAngle : 45f;
            Vector2 dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad),
                                      Mathf.Sin(ang * Mathf.Deg2Rad));
            var c = Ciws.Create(f, pos, dir, BattleRoot);
            c.Payload = payload;
            c.Pos = pos + dir * (Cfg.CiwsBodyRadius + 0.25f);
            c.SyncVisual(0f);
            _ciws.Add(c);
            return c;
        }

        /// <summary>近防炮每帧：飞行 → 展开判定；展开后 → 护盾判定 / 索敌开火 / 吸能。</summary>
        void StepCiws(float dt)
        {
            if (_ciws.Count == 0) return;
            for (int i = _ciws.Count - 1; i >= 0; i--)
            {
                var c = _ciws[i];
                if (c == null || c.Consumed)
                {
                    _ciws.RemoveAt(i);
                    continue;
                }

                if (c.Flying)
                {
                    StepCiwsFlight(c, dt);
                    c.SyncVisual(dt);
                    continue;
                }

                // ① 护盾 / 本体被击中
                StepCiwsShield(c);
                if (c.Consumed) { _ciws.RemoveAt(i); continue; }

                // ② 索敌：先打**敌方子弹**（近防炮的本职），没有再看敌方大球
                c.FireTimer -= dt;
                var tb = FindCiwsTargetBullet(c);
                if (tb != null)
                {
                    if (c.FireTimer <= 0f) FireCiwsAtBullet(c, tb);
                }
                else
                {
                    var tball = FindCiwsTargetBall(c);
                    if (tball != null)
                    {
                        if (c.FireTimer <= 0f) FireCiwsAtBall(c, tball);
                    }
                    else
                    {
                        // ③ 没有敌人 → 吸收范围内**己方领土**能量到散弹
                        AbsorbCiwsTerritory(c);
                    }
                }

                c.SyncVisual(dt);
            }
        }

        /// <summary>飞行形态：直线飞，碰到**非己方领土**就展开。</summary>
        void StepCiwsFlight(Ciws c, float dt)
        {
            c.Pos += c.Vel * dt;

            // 出界就反弹（别让它飞出地图后永远飞下去）
            float m = Cfg.MapWorldSize;
            if (c.Pos.x < 0f) { c.Pos.x = 0f; c.Vel.x = Mathf.Abs(c.Vel.x); }
            else if (c.Pos.x > m) { c.Pos.x = m; c.Vel.x = -Mathf.Abs(c.Vel.x); }
            if (c.Pos.y < 0f) { c.Pos.y = 0f; c.Vel.y = Mathf.Abs(c.Vel.y); }
            else if (c.Pos.y > m) { c.Pos.y = m; c.Vel.y = -Mathf.Abs(c.Vel.y); }

            if (Field == null || c.Owner == null) return;
            int owner = Field.OwnerAt(c.Pos);
            // 「非己方领土」= **敌方**领土（无主地不算，不然刚出门就展开了）
            if (owner >= 0 && owner != c.Owner.Index) c.Deploy();
        }

        /// <summary>
        /// 护盾 / 本体判定：
        ///   · 盾 &gt; 0 → 敌方子弹撞到护盾圈：扣盾、子弹消失；
        ///   · 盾 = 0 → 敌方子弹撞到**本体**：近防炮**死亡，无亡语**。
        /// </summary>
        void StepCiwsShield(Ciws c)
        {
            if (Bullets == null) return;
            var act = Bullets.Active;
            float sr = Cfg.CiwsShieldRadius;
            float br = Cfg.CiwsBodyRadius;
            for (int i = 0; i < act.Count; i++)
            {
                var b = act[i];
                if (b == null || !b.InUse || b.Owner == null || b.Owner == c.Owner) continue;
                float rr = b.VisualRadius;
                float d2 = (b.Pos - c.Pos).sqrMagnitude;
                if (c.Shield > 0d)
                {
                    float r = sr + rr;
                    if (d2 > r * r) continue;
                    c.Shield -= b.RemainingValue;
                    if (c.Shield < 0d) c.Shield = 0d;
                    b.Release();
                    c.Pulse = 0.10f;
                }
                else
                {
                    float r = br + rr;
                    if (d2 > r * r) continue;
                    b.Release();
                    c.Kill();          // ★ 无亡语
                    return;
                }
            }
        }

        Bullet FindCiwsTargetBullet(Ciws c)
        {
            if (Bullets == null) return null;
            var act = Bullets.Active;
            float best = Cfg.CiwsGunRange * Cfg.CiwsGunRange;
            Bullet found = null;
            for (int i = 0; i < act.Count; i++)
            {
                var b = act[i];
                if (b == null || !b.InUse || b.Owner == null || b.Owner == c.Owner) continue;
                float d2 = (b.Pos - c.Pos).sqrMagnitude;
                if (d2 < best) { best = d2; found = b; }
            }
            return found;
        }

        BigBall FindCiwsTargetBall(Ciws c)
        {
            BigBall found = null;
            float best = Cfg.CiwsGunRange * Cfg.CiwsGunRange;
            for (int f = 0; f < Factions.Count; f++)
            {
                var fac = Factions[f];
                if (fac == c.Owner) continue;
                for (int k = 0; k < fac.BigBalls.Count; k++)
                {
                    var b = fac.BigBalls[k];
                    if (b == null || b.Consumed) continue;
                    float d2 = ((Vector2)b.transform.position - c.Pos).sqrMagnitude;
                    if (d2 < best) { best = d2; found = b; }
                }
            }
            return found;
        }

        /// <summary>
        /// 开火（规格）：
        ///   · 近防炮散弹量 **&gt;= 敌方数值** → 发射**与敌方数值相同**的数值（剩下的留着）；
        ///   · **小于** → **一次性全部发射**。
        /// </summary>
        void FireCiwsAt(Ciws c, Vector2 targetPos, double targetValue)
        {
            if (c.Ammo <= 0d) return;
            double fire = c.Ammo >= targetValue ? targetValue : c.Ammo;
            if (fire <= 0d) return;
            c.Ammo -= fire;
            if (c.Ammo < 0d) c.Ammo = 0d;
            c.FireTimer = Cfg.CiwsFireInterval;
            c.Pulse = 0.14f;

            Vector2 d = targetPos - c.Pos;
            Vector2 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.right;
            var b = Bullets.Rent(c.Owner, c.Pos + dir * (Cfg.CiwsBodyRadius + 0.04f), dir,
                                 Cfg.CiwsBulletSpeed, fire, Cfg.BulletLifetime, false);
            if (b != null) b.MarkCiws();
        }

        void FireCiwsAtBullet(Ciws c, Bullet t)
        {
            FireCiwsAt(c, t.Pos, t.RemainingValue);
        }

        void FireCiwsAtBall(Ciws c, BigBall t)
        {
            FireCiwsAt(c, t.transform.position, t.Value);
        }

        /// <summary>吸收范围内**己方领土**能量：每格每帧 +`CiwsAbsorbPerCellPerFrame` 散弹。</summary>
        void AbsorbCiwsTerritory(Ciws c)
        {
            if (Field == null || c.Owner == null) return;
            int me = c.Owner.Index;
            float r = Cfg.CiwsGunRange;
            float ppu = Field.TexelsPerUnit;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((c.Pos.x - r) * ppu));
            int x1 = Mathf.Min(Field.Texels - 1, Mathf.CeilToInt((c.Pos.x + r) * ppu));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((c.Pos.y - r) * ppu));
            int y1 = Mathf.Min(Field.Texels - 1, Mathf.CeilToInt((c.Pos.y + r) * ppu));
            float r2 = r * r;
            int count = 0;
            for (int y = y0; y <= y1; y++)
            {
                float wy = (y + 0.5f) / ppu - c.Pos.y;
                float dy2 = wy * wy;
                int row = y;   // 只是让下面一行好读
                for (int x = x0; x <= x1; x++)
                {
                    float wx = (x + 0.5f) / ppu - c.Pos.x;
                    if (wx * wx + dy2 > r2) continue;
                    if (Field.OwnerAtTexel(x, row) == me) count++;
                }
            }
            if (count > 0)
            {
                c.Ammo += Cfg.CiwsAbsorbPerCellPerFrame * count;
                c.Pulse = Mathf.Max(c.Pulse, 0.05f);
            }
        }

        readonly System.Collections.Generic.List<BigBall> _gravityBalls =
            new System.Collections.Generic.List<BigBall>();

        /// <summary>
        /// ★★★ 引力随**引力球数值**放大的系数 —— 规格的**唯一来源**（子弹那一路和大球那一路都走它）。
        ///
        /// `factor = (数值 / Cfg.GravityValueRef) ^ Cfg.GravityValuePower`
        ///   · 参考点就是引力球门槛（100M）⇒ 「刚好算引力球」那一档正好 **×1**（观感不变）；
        ///   · 线性段：1B 会是 ×10，但拐点定在 300M（`NeutronStarGravityKnee`），
        ///     之后指数乘 `NeutronStarGravityPowerScale` ⇒ 1B ×4.6、10B ×10（线性是 ×100）；
        ///   · `GravityValuePower = 0` 就退回旧行为（完全不看数值）。
        ///
        /// ★ **不给下限**：数值掉到门槛以下（`HasGravity` 闩锁那种）照旧"公式不变"——
        ///   数值减半、引力也减半（第 103 轮的规格）。
        /// ★ 拐点处**连续**（值接得上），只是斜率变小，所以「数值越大引力越大」仍然成立。
        /// ★ **不给上限**：两条路各自已有安全阀 —— 大球那一路有
        ///   `GravityBigBallMaxAccel`（520 m/s²）+ `GravityBigBallMaxApproachSpeed`（6 m/s）
        ///   + `GravityBigBallMaxDeltaVPerTick`（每步 Δv 上限，防「一步翻转」的闪现），
        ///   子弹那一路有 `BulletMaxSpeed` 的速度夹顶 + `GravityBulletMaxTurnPerTickDeg`
        ///   （每步转角上限），所以再大的 factor 也不会把求解器甩飞。
        /// </summary>
        public static float GravityValueFactor(double value)
        {
            double r = System.Math.Max(value, 1d) / Cfg.GravityValueRef;
            double p = Cfg.GravityValuePower;
            double knee = System.Math.Max(Cfg.NeutronStarGravityKnee, 1.0001);
            if (r > knee)
                return (float)(System.Math.Pow(knee, p)
                               * System.Math.Pow(r / knee, p * Cfg.NeutronStarGravityPowerScale));
            return (float)System.Math.Pow(r, p);
        }

        /// <summary>
        /// 一颗引力球对一颗大球产生的**加速度**（米/秒²）—— 规格的**唯一来源**，
        /// 生产代码和自检都走它，别各写一份。
        ///
        /// `a = GravityOnBigBall × GravityValueFactor(引力球数值)
        ///      × (GravityValueRef / 被吸球数值) ^ GravityTargetValuePower / 距离²`
        ///
        /// ★★ 两个指数分工：
        ///   · `GravityValuePower` 管**引力源**（`GravityValueFactor`）：真引力里 `F ∝ m₁` ⇒ 线性，
        ///     所以它 = 1 ——「引力球数值越大吸得越狠」。
        ///   · `GravityTargetValuePower` 管**被吸球**：真引力里 `a = F/m₂` 与 m₂ 无关 ⇒ 它 = **0**。
        ///     旧值 1（反比、越小越显著）是"恒定力吸尘器"，已按用户要求关掉。
        /// </summary>
        /// <param name="gravityValue">引力球自己的数值（越大吸得越狠）。</param>
        /// <param name="attractedValue">被吸那颗的数值（`GravityTargetValuePower = 0` 时**完全不影响结果**）。</param>
        public static float GravityPullAccel(double gravityValue, double attractedValue, float dist)
        {
            double self = System.Math.Max(attractedValue, Cfg.GravityBigBallMinValue);
            float d2 = Mathf.Max(dist * dist, Cfg.GravityMinDistance * Cfg.GravityMinDistance);
            float a = Cfg.GravityOnBigBall * GravityValueFactor(gravityValue)
                      * (float)System.Math.Pow(Cfg.GravityValueRef / self, Cfg.GravityTargetValuePower)
                      / d2;
            return Mathf.Min(a, Cfg.GravityBigBallMaxAccel);
        }

        /// <summary>
        /// 引力球（或曾经到过 100M 的球）：对**所有阵营的子弹**吸引很强，
        /// 对**其他大球**按**真引力**吸引 —— `a = GravityOnBigBall × GravityValueFactor(引力球数值) / 距离²`，
        /// **与被吸球自己的数值无关**（见 `GravityPullAccel`；中子星段被压平，见 `Cfg.NeutronStarGravityKnee`）。
        /// ★ 引力球**之间也有引力**，用的就是同一条公式（第 103 轮起；第 88 轮那句"几乎没有"已作废）。
        /// </summary>
        void StepGravity()
        {
            _gravityBalls.Clear();
            for (int i = 0; i < Factions.Count; i++)
            {
                var f = Factions[i];
                for (int k = 0; k < f.BigBalls.Count; k++)
                {
                    var b = f.BigBalls[k];
                    // ★★★ 第 103 轮：收集判据是 **`HasGravity`（闩锁）**，不是 `IsGravityBall`（实时）——
                    //   用户要的是「数值掉回 100M 以下**也仍然有引力**」（见 BigBall.HasGravity）。
                    if (b != null && !b.Consumed && b.HasGravity) _gravityBalls.Add(b);
                }
            }
            if (_gravityBalls.Count == 0) return;

            // ★★★ 本函数是**每 GravityTickDivisor 个物理帧才被调一次**的（见 FixedUpdate 里那个
            //   `_gravityTick % divisor` 的提前返回），所以步长必须**乘回来**。
            //   ★ 第 102 轮踩到：子弹那一路一开始写的是 `Time.fixedDeltaTime`（1 帧），
            //     于是子弹实际只拿到 1/3 的转弯量，用户直接反馈「引力直接没了」。
            //     大球那一路一直是对的（`AddForce(acc * mass * tickScale)`）。
            float tickScale = Mathf.Max(Cfg.GravityTickDivisor, 1);

            // ★★ 第 102 轮：引力对子弹改成**只转弯、不加速**（详见 Cfg.GravityBulletMaxTurnDeg）
            if (Bullets != null)
                Bullets.ApplyGravity(_gravityBalls, Cfg.GravityOnBullet,
                                     Time.fixedDeltaTime * tickScale);

            // ★★★ 第 103 轮（用户）：「**现在引力球之间也需要一点引力**」——
            //   第 88 轮那句「引力球和引力球之间几乎没有引力作用」作废，这里**不再跳过**引力球。
            //
            // ★★ 本轮上真引力之后，这段的历史理由**变了**：「引力球之间」不再有任何特殊系数，
            //   它们和别的球走**同一条公式、同等待遇**，所以第 103 轮那条
            //   「『一点』引力（比吸小球弱得多）」的规格**已经作废**——
            //   现在两颗引力球互吸的强度就是 `a = GravityOnBigBall × (引力球数值/100M) / 距离²`，
    //   真正把它压住的只有**速度闸**（`GravityBigBallMaxApproachSpeed` / `BigBallMaxSpeed`）。
            //   ★ 这条循环**不跳过**引力球依然是必须的：真引力里两个物体本来就互相吸引，
            //     而且这样天然对称（两边都各自被对方拉），不用额外写"反作用"。
            //   ★ 仍然**没有**做的是**同一个引力球自己的反作用**（它不会被自己吸走）——
            //     那属于物理上更完整、但用户本轮明确说"不改"的部分。
            for (int i = 0; i < Factions.Count; i++)
            {
                var f = Factions[i];
                for (int k = 0; k < f.BigBalls.Count; k++)
                {
                    var b = f.BigBalls[k];
                    if (b == null || b.Consumed || b.Body == null) continue;
                    Vector2 p = b.transform.position;
                    Vector2 acc = Vector2.zero;
                    for (int g = 0; g < _gravityBalls.Count; g++)
                    {
                        var gb = _gravityBalls[g];
                        if (gb == null || gb == b || gb.Consumed) continue;
                        Vector2 d = (Vector2)gb.transform.position - p;
                        float dist2 = d.sqrMagnitude;
                        if (dist2 > Cfg.GravityMaxRange * Cfg.GravityMaxRange || dist2 < 1e-6f) continue;
                        float dist = Mathf.Sqrt(dist2);
                        Vector2 dir = d / dist;
                        // ★★★ 第 103 轮（用户）：「极小数值球被引力球吸引时会**弹动鬼畜**」——
                        //   加速度公式对"被吸球数值"有个下限，小到 100 的球也会拿到几百 m/s²，
                        //   于是被甩进去、按正常弹性弹出来、再被甩进去，来回抽搐。
                        //   这里给「**朝引力球靠近的速度**」加一道闸：已经在以
                        //   `GravityBigBallMaxApproachSpeed` 靠过去了，这一颗球的引力就**整份不加**。
                        //   ★ 只挡"加速"、不刹车：球往外飞的时候 `dot(v,dir) < 0`，照旧被完整拉回来 ——
                        //     要是加刹车，球出去的一路上会被减速，就变成"永远逃不掉"了
                        //     （子弹那条踩过一模一样的坑）。
                        //   ★ 当时"加速度公式一个字没改"（那时「被吸的越小越显著」照旧）。
                        //     ★★ 本轮上真引力之后这条规格已作废（见 `GravityTargetValuePower`），
                        //     但这道闸**留下来了、而且更关键了**：真引力下 a 会大到 1000+ m/s²，
                        //     真正决定观感的就是"顶到 6 m/s 之后匀速被吸走"。
                        if (Vector2.Dot(b.Body.velocity, dir)
                            >= Cfg.GravityBigBallMaxApproachSpeed) continue;
                        acc += dir * GravityPullAccel(gb.Value, b.Value, dist);
                    }
                    if (acc.sqrMagnitude > 1e-6f)
                    {
                        // ★★★ 第 103 轮（用户对照原版录像）：
                        //   「原版引力效果很大，但大球碰到引力球时还是能**弹得很远**；
                        //     我们的则是球的速度已经快到**瞬移**了」。
                        //
                        //   根因：引力每 `GravityTickDivisor` 个物理帧才加一次、一次补足 3 帧的量
                        //   （`tickScale`），而加速度最高 520 m/s² —— 一次就能给球塞进
                        //   `520 × 3/60 ≈ 26 m/s` 的速度，下一帧跨出 0.4m 以上，小球的位移看着就是瞬移。
                        //   ★ FixedUpdate 里那道 `BigBallMaxSpeed` 是**事后的**：它只能把下一帧的速度夹回来，
                        //     夹不住「这一 tick 被塞进去的那一大口」。
                        //
                        //   所以这里按**加完之后的最终速度**夹一次：这一 tick 的速度增量不许把球
                        //   推过 `BigBallMaxSpeed`。已经超上限的球（比如刚弹开）**不再加**，
                        //   免得把往外飞的球刹住。
                        float fdt = Time.fixedDeltaTime;
                        float gdt = fdt * tickScale;
                        Vector2 v0 = b.Body.velocity;
                        Vector2 dv = acc * gdt;
                        // ★ 单个引力步的速度增量也有上限：中子星旁边一步能给几十 m/s，
                        //   球会「一步从 +5 变成 −6」（方向整个翻过来）= 画面上闪一下。
                        //   限的是**每步**，所以只是把「瞬间加速」摊成几步，观感是连续加速。
                        float maxDv = Cfg.GravityBigBallMaxDeltaVPerTick;
                        float dvm = dv.magnitude;
                        if (maxDv > 0f && dvm > maxDv) dv *= maxDv / dvm;
                        float cap = Cfg.BigBallMaxSpeed;
                        Vector2 v1 = v0 + dv;
                        float s0 = v0.magnitude, s1 = v1.magnitude;
                        // ★ 只在「这一 tick 会让球**更快**」时才夹；
                        //   往外飞、被引力**拉回来/刹车**（s1 < s0）的时候整份照加 ——
                        //   不然球一旦超过上限就再也不受引力，永远逃得掉。
                        if (s1 > cap && s1 > s0 && s1 > 1e-5f)
                            dv = v1 * (cap / s1) - v0;
                        // AddForce 收的是**力**，加速度要自己乘质量 → 结果与质量无关，
                        // 「小球的加速度优势」完全由上面公式里的 selfValue 决定（一处来源）。
                        b.Body.AddForce(dv * b.Body.mass / fdt, ForceMode2D.Force);
                    }
                }
            }

            // ★★ 用户：「两颗引力球会一直『二人转』（互相绕着转、永远不撞）」——
            //   给它们的**相对切向速度**加一点阻尼，轨道就一圈圈往里收，最后撞上走碰撞规则。
            //   ★ 只动切向分量：正对着冲过来的那一对（切向 ≈ 0）完全不受影响，
            //     slingshot 那种「擦一下被甩出去」的手感也还在。
            //   ★ 每 `GravityTickDivisor` 步做一次，系数见 `Cfg.GravityBallOrbitDamp`（1 = 关掉）。
            if (Cfg.GravityBallOrbitDamp < 1f && _gravityBalls.Count >= 2)
            {
                float kHalf = (1f - Mathf.Clamp(Cfg.GravityBallOrbitDamp, 0f, 1f)) * 0.5f;
                for (int i = 0; i < _gravityBalls.Count; i++)
                {
                    var a = _gravityBalls[i];
                    if (a == null || a.Consumed || a.Body == null) continue;
                    for (int j = i + 1; j < _gravityBalls.Count; j++)
                    {
                        var c = _gravityBalls[j];
                        if (c == null || c.Consumed || c.Body == null) continue;
                        Vector2 d = (Vector2)c.transform.position - (Vector2)a.transform.position;
                        float dist = d.magnitude;
                        if (dist < 1e-4f || dist > Cfg.GravityMaxRange) continue;
                        Vector2 n = d / dist;
                        Vector2 rel = c.Body.velocity - a.Body.velocity;
                        Vector2 relT = rel - n * Vector2.Dot(rel, n);
                        Vector2 imp = relT * kHalf;      // 两边各承担一半
                        a.Body.velocity += imp;
                        c.Body.velocity -= imp;
                    }
                }
            }
        }

        int _gravityTick;

        /// <summary>引力按固定步长算，而且每隔 GravityTickDivisor 步才来一次（省开销）。</summary>
        void FixedUpdate()
        {
            _gravityTick++;
            if (Cfg.GravityTickDivisor > 1 && (_gravityTick % Cfg.GravityTickDivisor) != 0) return;
            StepGravity();
        }

        void StepVolleys()
        {
            for (int i = _volleys.Count - 1; i >= 0; i--)
            {
                var v = _volleys[i];
                if (v.F == null || v.F.Turret == null || Bullets == null) { _volleys.RemoveAt(i); continue; }

                int n = Mathf.Min(Mathf.Max(Cfg.VolleyBulletsPerTick, 1), v.Remaining);
                double each = Cfg.VolleyBulletValue(v.Value);
                Vector2 origin = v.F.Turret.WorldPosition;
                for (int k = 0; k < n; k++)
                {
                    float a = (v.Angle + Random.Range(-Cfg.VolleySpreadDeg, Cfg.VolleySpreadDeg))
                              * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    // ★ 规格：齐射的**每一颗**也各自带一点点初速差别（和普通射击同一个做法、
                    //   同一个 BulletSpeedJitter）—— 「微弱区分」是**每颗**的事，不是每排的事。
                    float vm = Random.Range(1f - Cfg.BulletSpeedJitter, 1f + Cfg.BulletSpeedJitter);
                    Bullets.Rent(v.F, origin + dir * (v.F.Turret.CoreRadius + 0.25f), dir,
                                 Cfg.VolleyProjectileSpeed * vm, each,
                                 Cfg.VolleyProjectileLife, true);
                }
                v.Remaining -= n;
                if (v.Remaining <= 0) _volleys.RemoveAt(i);
            }
        }

        public BigBall SpawnBigBall(Faction f, double value)
        {
            // 规格：大球是**从炮台那里出来**的，不是随机找个地方冒出来
            Vector2 pos = f.Turret != null ? f.Turret.WorldPosition : f.HomePosition;
            return SpawnBigBall(f, value, pos);
        }

        /// <summary>从炮台位置生一颗**吸血球**（第 111 轮）—— 就是上面那颗大球多带一个标记。</summary>
        public BigBall SpawnBigBall(Faction f, double value, bool vampire)
        {
            Vector2 pos = f.Turret != null ? f.Turret.WorldPosition : f.HomePosition;
            return SpawnBigBall(f, value, pos, vampire);
        }

        /// <summary>在指定的位置生一颗大球（亡语那十几颗要沿螺旋散开，不能全堆在炮台那一个点）。</summary>
        /// <param name="vampire">true = 生**吸血球**（第 111 轮）：外观/物理和大球一样，多一条碰撞规则。</param>
        public BigBall SpawnBigBall(Faction f, double value, Vector2 pos, bool vampire = false)
        {
            var ball = BigBall.Create(f, pos, value, BattleRoot, vampire);
            f.BigBalls.Add(ball);

            if (ball.Body != null)
            {
                // 出击速度很慢，别让它像大运一样冲出去
                float ang = Random.Range(0f, Mathf.PI * 2f);
                ball.Body.velocity = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Cfg.BigBallSpawnSpeed;
            }
            return ball;
        }

        // ─────────────────────────────────────────────────────────────
        // 复活：死亡 60 秒后，大球回到炮台即可复活
        // ─────────────────────────────────────────────────────────────

        void CheckRespawn(Faction f, float now)
        {
            if (f.Alive || f.DeathTime < 0f) return;
            if (now - f.DeathTime < Cfg.RespawnDelay) return;

            for (int i = 0; i < f.BigBalls.Count; i++)
            {
                var b = f.BigBalls[i];
                if (b == null || b.Consumed || b.Body == null) continue;

                Vector2 dir = (f.HomePosition - (Vector2)b.transform.position);
                float dist = dir.magnitude;

                if (dist <= Cfg.HomeRadius + b.Radius)
                {
                    ApplyRespawn(f, b);
                    return;
                }
                b.Body.AddForce(dir.normalized * Cfg.HomingForce, ForceMode2D.Force);
            }
        }

        /// <summary>
        /// ★★ 第 125 轮（用户）：「按下给各阵营增加一个左边的翻倍球」——
        ///   给**每个阵营**在左栏各补一颗**增倍球**（就是会弹跳、撞乘法门翻倍的那种）。
        ///
        /// ★ 走的是和「开局 / 每 80 秒 / 复活」**同一个入口** <see cref="MultiplierZone.SpawnBall"/>：
        ///   所以球从炮口按当前朝向打出去、数值 = <see cref="Cfg.NewBallStartValue"/>、
        ///   自动登记进 `Faction.Balls`，不需要另开一套生成逻辑。
        /// 返回一共发了几颗（= 阵营数），方便自检。
        /// </summary>
        public int GrantBallToAllFactions()
        {
            int n = 0;
            for (int i = 0; i < Factions.Count; i++)
            {
                if (Factions[i] == null) continue;
                Zone.SpawnBall(Factions[i], Cfg.NewBallStartValue);
                n++;
            }
            return n;
        }

        /// <summary>一颗大球抵到炮台 → 执行复活结算。公开出来方便调试和自检。</summary>
        public void ApplyRespawn(Faction f, BigBall ball)
        {
            // ★ 规格：**复活要有特效** —— 一圈白色冲击波从炮台位置扩出去 + 一团阵营色闪光，
            //   比阵亡爆炸更大更久（这是「重生」，值得看清楚）。
            Explosion.Spawn(f.HomePosition, Cfg.TurretRadius, f.Color, BattleRoot,
                            Cfg.RespawnFxLife, Color.white, Cfg.RespawnFxRadiusScale);

            double v = ball.Value;
            double logV = Cfg.LogV((float)System.Math.Max(v, 1d));

            int gain = Mathf.FloorToInt((float)(logV * Cfg.RespawnBallCountLogScale + Cfg.RespawnBallCountLogBias));
            for (int i = 0; i < gain; i++) Zone.SpawnBall(f, Cfg.NewBallStartValue);

            if (Cfg.RespawnAssignsBulletsAndShield)
            {
                f.Bullets = v * Cfg.RespawnBulletsFraction;
                f.Shield = v * Cfg.RespawnShieldFraction;
            }
            else
            {
                f.Bullets += v * Cfg.RespawnBulletsFraction;
                f.Shield += v * Cfg.RespawnShieldFraction;
            }

            ball.Value = v * Cfg.RespawnBallValueKeep;
            if (Cfg.RespawnConsumesBall || ball.Value <= 0.0002d) ball.Consume();

            if (f.Turret != null) f.Turret.Revive();
        }

        // ─────────────────────────────────────────────────────────────
        // 胜负
        // ─────────────────────────────────────────────────────────────

        void CheckWinner()
        {
            if (WinnerIndex >= 0) return;
            int total = Field.TotalPixels;
            for (int i = 0; i < Factions.Count; i++)
            {
                if (Field.PixelsOf(i) >= total * 0.98f)
                {
                    WinnerIndex = i;
                    return;
                }
            }
        }

        public Faction LeadingFaction()
        {
            Faction best = null;
            int bestTiles = -1;
            for (int i = 0; i < Factions.Count; i++)
            {
                if (Factions[i].TilesOwned > bestTiles)
                {
                    bestTiles = Factions[i].TilesOwned;
                    best = Factions[i];
                }
            }
            return best;
        }
    }
}
