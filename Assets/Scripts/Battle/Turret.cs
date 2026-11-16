using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 炮台。**驻在右栏战场的四角**（不是增倍区里 —— 左栏那个写着 x16 的灰色楔形是乘法门）。
    ///
    /// 视觉：外面一个大的半透明**护盾圈**，里面一个小的**炮台**——
    /// 炮台是**一条闭合的黑色轮廓线**（圆身子 + 贴着的小圆头，见 `Primitives.TurretOutline`），
    /// 里面只填一层很淡的阵营色。**中间没有别的圈**（用户第 101 轮：「炮座中间为啥还有一个圆边框」）。
    /// 子弹数量显示在上方、名称三行 + 护盾量在下方，都居中。
    ///
    /// 行为：
    ///   · 自转（直射方向 = 当前朝向）
    ///   · 近防系统：敌方**大球**进入作用半径 → 立刻转向它并开火
    ///   · 三种武器：直射 / 散射 / 齐射
    ///   · 护盾半径按公式缓动（已乘 Cfg.ShieldVisualScale）
    ///
    /// 死亡规则（用户规格）：护盾归零**只是没有护盾了**；此后敌方大球或子弹
    /// 碰到炮台**本体**才死亡。所以护盾判定框在护盾归零后会被关掉。
    /// </summary>
    public class Turret : MonoBehaviour
    {
        public Faction Owner;
        public float AimAngle;
        public bool IsDead;

        SpriteRenderer _coreOutline, _coreFill, _shieldFill, _shieldRing;
        Transform _aimT, _shieldTriggerT, _bodyTriggerT;
        CircleCollider2D _shieldTrigger, _bodyTrigger;

        float _nextDirect, _nextScatter, _nextVolley;

        /// <summary>**按角度**的开火累加器：攒够「单位角度」就发一发（见 StepFiring）。</summary>
        float _angleAccum;
        /// <summary>炮台几乎不转时的时间兜底累加器（近防锁死目标时用它）。</summary>
        float _timeAccum;
        /// <summary>上一帧的朝向 —— 用来算这一帧扫过了多少度。</summary>
        float _aimPrev;

        /// <summary>
        /// **上一帧**的炮管角度。同一帧的这一批子弹，角度会在
        /// 「上一帧角度 → 当前角度」之间均匀插值（见 StepFiring 里那段注释）——
        /// 这是「转着打时弹幕里那条缝」的解法。
        /// </summary>

        /// <summary>护盾被击碎后的缩小动画进度 —— **已删除**（用户：「破盾的动画可以去掉了」）。</summary>

        public bool IsShieldCollider(Collider2D c) { return _shieldTrigger != null && ReferenceEquals(c, _shieldTrigger); }
        public bool IsBodyCollider(Collider2D c) { return _bodyTrigger != null && ReferenceEquals(c, _bodyTrigger); }

        /// <summary>炮台半径**恒定**，不随护盾量变化（你特别强调过）。</summary>
        public float CoreRadius { get { return Cfg.TurretRadius; } }

        /// <summary>
        /// 这一帧**画出来**的护盾半径（含击碎动画）。炮台特效要贴着它走，
        /// 所以由 RefreshVisual 算好放在这里，别让特效自己再推一遍（两处算法一定会漂）。
        /// </summary>
        public float ShieldVisualRadius { get; private set; }

        /// <summary>持续特效（护盾边缘的呼吸圈 + 核心微光）。</summary>
        TurretFx _fx;

        public static Turret Create(Faction owner, Transform parent)
        {
            var go = new GameObject("Turret_" + owner.Index);
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = owner.HomePosition;

            var t = go.AddComponent<Turret>();
            t.Owner = owner;

            // 护盾：一层半透明填充 + 一圈亮边
            // 规格：护盾装上这张纹理（中心白 → 边缘黑），用阵营色一染就是「亮心深边」
            t._shieldFill = Primitives.MakeNode("ShieldFill", Primitives.ShieldDisc,
                Palette.Alpha(owner.Color, 0.92f), Cfg.ShieldFillSortingOrder, go.transform);
            // 外圈用**软边环**，不然最外围是一条硬边，看着就没羽化
            t._shieldRing = Primitives.MakeNode("ShieldRing", Primitives.SoftRing,
                Palette.Alpha(owner.Color, 0.75f), Cfg.ShieldRingSortingOrder, go.transform);

            // 护盾判定框
            var st = new GameObject("ShieldTrigger");
            st.transform.SetParent(go.transform, false);
            t._shieldTrigger = st.AddComponent<CircleCollider2D>();
            t._shieldTrigger.isTrigger = true;
            t._shieldTrigger.radius = 0.5f;
            var srb = st.AddComponent<Rigidbody2D>();
            srb.bodyType = RigidbodyType2D.Kinematic;
            srb.gravityScale = 0f;
            t._shieldTriggerT = st.transform;

            // ★★ 第 101 轮（用户）：「炮台怎么变成俩圆了」——
            //   上一版是「本体圆环 + 炮管圆环」两层叠出来的，两个环相交处会留下**两条内弧**，
            //   看着就是两个圆。图一其实是**一整条闭合的轮廓线**（圆身子 + 贴着的小圆头，
            //   中间收成一个"脖子"）。所以现在用 `Primitives.TurretOutline / TurretFill`：
            //   把「大圆 ∪ 小圆」这个并集烘成两张图（轮廓 / 填充），只有一条外轮廓。
            //   本体是圆的，所以整块跟着炮口一起转，看着和"只有炮管在转"完全一样。
            var pivot = new GameObject("AimPivot");
            pivot.transform.SetParent(go.transform, false);
            t._aimT = pivot.transform;

            // 图层：护盾 6/7 < 填充 9 < 轮廓 10 < 图标 11（轮廓压在填充上面，线才干净）
            t._coreFill = Primitives.MakeNode("CoreFill", Primitives.TurretFill,
                Palette.Alpha(owner.Color, Cfg.TurretCoreFillAlpha), 9, pivot.transform);
            t._coreOutline = Primitives.MakeNode("CoreOutline", Primitives.TurretOutline,
                Color.black, 10, pivot.transform);

            // 本体判定框
            var bt = new GameObject("BodyTrigger");
            bt.transform.SetParent(go.transform, false);
            t._bodyTrigger = bt.AddComponent<CircleCollider2D>();
            t._bodyTrigger.isTrigger = true;
            t._bodyTrigger.radius = 0.5f;
            var brb = bt.AddComponent<Rigidbody2D>();
            brb.bodyType = RigidbodyType2D.Kinematic;
            brb.gravityScale = 0f;
            t._bodyTriggerT = bt.transform;

            t.RefreshVisual();
            // ★ 规格：炮台的**持续特效**（护盾边缘的呼吸圈 + 核心微光），一直有
            t._fx = TurretFx.Attach(t, owner.Color);
            return t;
        }

        void RefreshVisual()
        {
            float shieldR = Owner.ShieldRadius;
            float coreR = CoreRadius;

            ShieldVisualRadius = shieldR;
            _shieldFill.transform.localScale = Vector3.one * (shieldR * 2f);
            _shieldRing.transform.localScale = Vector3.one * (shieldR * 2f);
            // 炮台外形：贴图横跨 2×TurretSpan 个炮台半径 → 缩放 = 炮台半径
            // （`Primitives.TurretOutline` 的 PPU 就是按这个定的，别在这里乘系数）
            Vector3 shapeScale = Vector3.one * coreR;
            _coreOutline.transform.localScale = shapeScale;
            _coreFill.transform.localScale = shapeScale;
            _coreOutline.transform.localPosition = Vector3.zero;
            _coreFill.transform.localPosition = Vector3.zero;
            _aimT.localScale = Vector3.one;
            _shieldTriggerT.localScale = Vector3.one * (shieldR * 2f);
            _bodyTriggerT.localScale = Vector3.one * (coreR * 2f);
        }

        public Vector2 WorldPosition { get { return transform.position; } }

        void Update()
        {
            if (Owner == null) return;
            float dt = Time.deltaTime;

            RefreshVisual();

            if (IsDead)
            {
                _shieldFill.enabled = false;
                _shieldRing.enabled = false;
                _coreOutline.color = Palette.Alpha(Color.black, 0.45f);
                _coreFill.color = Palette.Alpha(Palette.Shade(Owner.Color, 0.45f), 0.35f);
                if (_shieldTrigger != null) _shieldTrigger.enabled = false;
                return;
            }

            _coreOutline.color = Color.black;
            _coreFill.color = Palette.Alpha(Owner.Color, Cfg.TurretCoreFillAlpha);

            bool hasShield = Owner.Shield > 0d;

            // ★ 破盾：护盾值一归 0，**半径就按同一条非线性缓动缩到 0**（目标 = 0），
            //   缩到看不见才隐藏 —— 所以这里看的是**半径**，不是护盾值。
            //   判定（trigger）在归 0 那一刻就立刻关掉，显示和判定互不牵连。
            bool shieldVisible = hasShield || Owner.ShieldRadius > 0.02f;
            _shieldFill.enabled = shieldVisible;
            _shieldRing.enabled = shieldVisible;
            _shieldTrigger.enabled = hasShield;


            // ── 瞄准：近防优先 ──
            // ★ 规格：近防系统对**敌方子弹 / 尾流**同样有效 ——
            //   以前只会锁敌方大球，敌人的弹幕从旁边飞过去它理都不理。
            Vector2 aimPoint;
            if (FindCiwsAimPoint(out aimPoint))
            {
                Vector2 d = aimPoint - WorldPosition;
                float want = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                AimAngle = Mathf.MoveTowardsAngle(AimAngle, want, Cfg.CiwsTurnSpeed * dt);
            }
            else
            {
                // ★★ 规格（用户第 87 轮）：
                //   「直射状态下炮台**不旋转**并发射子弹……子弹**全部打完后**，炮台可以继续旋转」。
                //   所以：直射模式 + **还有弹药** → 停在这个角度；否则照常自转。
                bool holdDirect = Owner.TurretMode == WeaponSlotKind.Direct && CanShoot();
                if (!holdDirect)
                    AimAngle = Mathf.Repeat(AimAngle + Cfg.TurretSpinSpeed * dt, 360f);
            }
            _aimT.localRotation = Quaternion.Euler(0f, 0f, AimAngle);

            // 开火（跟着渲染帧走，见 StepFiring）。
            // ★ 用**子弹那套夹过的 dt**，不能用原始的 Time.deltaTime ——
            //   两者不一致的话，低帧率时每帧都会在弹流里留一道缝（「一会儿打一会儿不打」）。
            //
            // ★★ 规格（用户第 69 轮给的模型）：炮台是**按转过的角度**发弹的 ——
            //    「每一个单位角度只发一发」。所以要把这一帧**实际扫过的角度**传下去。
            float sweep = Mathf.DeltaAngle(_aimPrev, AimAngle);
            _aimPrev = AimAngle;
            StepFiring(BulletPool.StepDt(dt), sweep);

            // 齐射不再自动打：只有增倍球落进武器区的「齐射」格才会触发（见 GameWorld.FireVolley）
        }

        /// <summary>
        /// 规格：**射速按 1024 发 / 物理帧**（不是按秒）。
        /// 弹药消耗仍然按「发射次数」算（1 tick 扣 BulletsPerShot），
        /// 所以弹幕很密，但子弹库存不会被瞬间打空。
        /// </summary>
        /// <summary>
        /// 开火。★ 跟着子弹一起搬到了**渲染帧**（原来是 FixedUpdate）。
        ///
        /// 两个原因：
        ///   1. 发射和子弹自己的推进必须同一个节拍，否则「这一帧有物理步、下一帧没有」
        ///      会让画面上出现「发几发、停一下」；
        ///   2. 用**小数累加器**而不是 `round(射速 × dt)` —— round 会让每帧的发数在
        ///      两个整数之间跳（比如 13 / 14 / 13），累加器则是「攒够一发就发一发」，
        ///      **每一帧都真的在发**，长期平均还正好等于射速。
        /// </summary>
        /// <param name="dt">**必须**是 `BulletPool.StepDt(...)` 的结果（和子弹位移同一个 dt）。</param>
        void StepFiring(float dt, float sweepDeg)
        {
            if (IsDead || BulletPool.Instance == null) return;
            if (!CanShoot()) { _angleAccum = 0f; _timeAccum = 0f; return; }

            // ★★ 直射（用户第 87 轮）：**炮台不转**，所以「按转过的角度发弹」那套在这里
            //   一个角度都不会攒 —— 必须改回**按时间**发。方向 = 当前朝向 ± DirectFireSpreadDeg。
            if (Owner.TurretMode == WeaponSlotKind.Direct)
            {
                StepDirectFire(dt);
                return;
            }

            // ★★ 规格（用户第 69 轮）：**每一个「单位角度」只发一发子弹**。
            //   密度是靠**把单位角度做小**来提的（0.1° → 0.01° 就是 10 倍），
            //   不是靠「每单位角度多发几发」。所以这里是**按转角**发弹，不是按时间。
            float step = Mathf.Max(Cfg.BulletAngleStepDeg, 1e-5f);
            // ★★ 只把**正常自转**那一份计入角度累加器。
            //   近防系统锁到目标时炮管会以 420°/s 猛转，一帧扫过十几度 ——
            //   按那个角度发弹就是一圈 64 发的弹幕，实测把场上子弹从 6 千顶到 **3 万**
            //   （帧率直接从 38 掉到 20 出头）。所以按「自转速度 × dt」封顶。
            float emitSweep = Mathf.Min(Mathf.Abs(sweepDeg), Cfg.TurretSpinSpeed * dt * 1.5f);
            _angleAccum += emitSweep;
            int n = Mathf.FloorToInt(_angleAccum / step);

            if (n > 0)
            {
                _angleAccum -= n * step;
            }
            else if (Mathf.Abs(sweepDeg) < Cfg.BulletIdleSweepDeg)
            {
                // ② 炮台**几乎不转**了（近防锁死目标之后炮管是停住的）——
                //    纯按角度算那一帧一发都不发，炮台会「哑掉」。这时退回按时间发，
                //    方向都是当前朝向（它就是在瞄着那个点打，本来就该是一束）。
                _timeAccum += Cfg.BulletIdleFireRate * dt;
                n = Mathf.FloorToInt(_timeAccum);
                if (n > 0) _timeAccum -= n;
            }
            if (n <= 0) return;

            // 一帧最多这么多 —— 近防猛转（420°/s）时一帧能扫 21°，不封顶就是八百发的尖峰
            n = Mathf.Min(n, Cfg.BulletMaxPerFrame);

            // ★ 规格：炮台打出来的子弹**没有随机分布**，只有初速的轻微差别
            //   （散射是靠炮台本身在转，方向自然会散开）
            // ★ 规格「散弹解封」：前 5 分钟单发数值封顶 Cfg.ShotValueCap，5 分钟后一律取 /Cfg.BulletValueDivisor
            double value = Cfg.BulletValueFor(Owner.Bullets, GameWorld.MatchElapsed);
            float j = Cfg.BulletSpeedJitter;

            // 时间兜底那一档：方向都一样，只沿炮管铺开（不然会叠成一个小点）
            bool idleBurst = Mathf.Abs(sweepDeg) < Cfg.BulletIdleSweepDeg;
            float from = AimAngle - sweepDeg;

            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                // ★★ 这一批的角度**摊在「这一帧扫过的那段弧」上** —— 这是本模型的关键：
                //    每发子弹占一个角度，所以整条弹流是**一根细扇形**，
                //    而不是「同一个角度的十几发叠成一条放射线」。
                float ang = idleBurst ? AimAngle : Mathf.LerpAngle(from, AimAngle, t);
                float ahead = idleBurst ? Cfg.BulletSpeed * dt * t : 0f;

                // ★★ 规格：初速的微弱差别是**每颗子弹各自**的（随机一点点），
                //   不是「一排一条速度梯度」。（梯度会让整排被系统性地拉成一条长带。）
                float mul = Random.Range(1f - j, 1f + j);

                SpawnBulletAt(ang, value, Cfg.BulletSpeed * mul, Cfg.BulletLifetime, false, ahead);
            }
            // ★★ 扣的是**这一帧真正发出去的那些**（n 发 × 单发数值），不是「一发」。
            //   以前写成「每帧扣一发」：一帧发 13 发、账上只掉 1 发的钱 ——
            //   于是「子弹文字掉了 2M，实际打出去 24M」（差 13 倍，正好是一帧的发数）。
            SpendShots(n, value);
        }

        /// <summary>
        /// **直射**：炮台不转，按**时间**发弹，方向在当前朝向 ±`DirectFireSpreadDeg`（5°）里抖。
        ///
        /// 规格（用户第 87 轮）：「子弹发射的角度范围为 5° 左右浮动」。
        /// 近防触发时会被上面那段瞄准逻辑接管（炮管先转过去），转完继续沿**那个**方向直射 ——
        /// 因为这里读的永远是**当前**的 `AimAngle`，不需要额外记状态。
        /// </summary>
        void StepDirectFire(float dt)
        {
            _timeAccum += Cfg.DirectFireRate * FireRateScale() * dt;
            int n = Mathf.FloorToInt(_timeAccum);
            _timeAccum -= n;
            if (n <= 0) return;
            n = Mathf.Min(n, Cfg.BulletMaxPerFrame);

            double value = Cfg.BulletValueFor(Owner.Bullets, GameWorld.MatchElapsed);
            float j = Cfg.BulletSpeedJitter;
            float spread = Cfg.DirectFireSpreadDeg;

            for (int i = 0; i < n; i++)
            {
                float ang = AimAngle + Random.Range(-spread, spread);
                float mul = Random.Range(1f - j, 1f + j);
                // 同一帧这一批沿炮管铺开一点点，免得叠成一个点
                float ahead = Cfg.BulletSpeed * dt * ((i + 0.5f) / n);
                SpawnBulletAt(ang, value, Cfg.BulletSpeed * mul, Cfg.BulletLifetime, false, ahead);
            }
            SpendShots(n, value);
        }

        float FireRateScale()
        {
            double b = Owner.Bullets > 0d ? Owner.Bullets : 0d;
            return 1f + Mathf.Log((float)(1d + b)) * Cfg.BulletCountFireRateBonus;
        }

        /// <summary>
        /// 库存够不够开一枪。★ 和 HUD **用同一个取整**（`BigNumber.Grouped` 也是四舍五入）——
        /// 涂地是按小数扣子弹的（`Bullet.cs` 里 `Bullets -= painted × PixelCaptureCost`），
        /// 库存经常停在 0.6 这种；以前这里判的是**原始值 ≥ 1**，于是
        /// 「HUD 显示还有 1 发、却怎么都不发射」。现在显示几发就能打几发。
        /// </summary>
        public static bool EnoughBullets(double bullets)
        {
            if (double.IsNaN(bullets)) return false;
            return System.Math.Round(bullets) >= Cfg.BulletsPerShot;
        }

        bool CanShoot()
        {
            return EnoughBullets(Owner.Bullets) && BulletPool.Instance != null;
        }

        /// <summary>
        /// 开火扣弹药。规格：扣掉**每一发携带的数值** —— 而且必须按**实际发出去的条数**扣，
        /// 这样「子弹文字掉的值」和「真正送出去的数值」永远相等。
        /// </summary>
        /// <param name="count">这一帧真的发出去几发。</param>
        /// <param name="unit">单发携带的数值（发射时现算的那个）。</param>
        void SpendShots(int count, double unit)
        {
            if (count <= 0) return;
            double cost = Cfg.FiringConsumesBulletValue ? unit * count : Cfg.BulletsPerShot * count;
            Owner.Bullets = System.Math.Max(Owner.Bullets - cost, 0d);
        }

        void SpawnBullet(float angleOffset, double damage, float speed, float life, bool heavy)
        {
            SpawnBullet(angleOffset, damage, speed, life, heavy, 0f);
        }

        /// <param name="ahead">沿炮管方向再往前放多少米（同一帧的一批铺开用）。</param>
        void SpawnBullet(float angleOffset, double damage, float speed, float life, bool heavy, float ahead)
        {
            SpawnBulletAt(AimAngle + angleOffset, damage, speed, life, heavy, ahead);
        }

        /// <param name="angleAbs">**绝对**朝向（度）。同一批子弹要摊在这一帧扫过的那段弧上。</param>
        void SpawnBulletAt(float angleAbs, double damage, float speed, float life, bool heavy, float ahead)
        {
            float r = angleAbs * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            Vector2 origin = WorldPosition + dir * (CoreRadius + 0.2f + ahead);
            BulletPool.Instance.Rent(Owner, origin, dir, speed, damage, life, heavy);
        }

        void TryDirect(float now)
        {
            if (!CanShoot()) return;
            float interval = 1f / Mathf.Max(Cfg.DirectBaseFireRate * FireRateScale(), 0.01f);
            if (now < _nextDirect) return;
            _nextDirect = now + interval;
            for (int i = 0; i < Cfg.BulletsPerVolleyShot; i++)
            {
                float jitter = Random.Range(-4f, 4f);
                SpawnBullet(jitter, Cfg.BulletValueFor(Owner.Bullets, GameWorld.MatchElapsed),
                            Cfg.BulletSpeed * Random.Range(0.92f, 1.08f),
                            Cfg.BulletLifetime, false);
            }
            SpendShots(Cfg.BulletsPerVolleyShot,
                       Cfg.BulletValueFor(Owner.Bullets, GameWorld.MatchElapsed));
        }

        void TryScatter(float now)
        {
            if (!CanShoot()) return;
            float interval = 1f / Mathf.Max(Cfg.ScatterBaseFireRate * FireRateScale(), 0.01f);
            if (now < _nextScatter) return;
            _nextScatter = now + interval;

            // 散射：一个扇形里撒一把，密度才够看
            for (int i = 0; i < Cfg.BulletsPerVolleyShot; i++)
            {
                float a = Random.Range(-22f, 22f);
                SpawnBullet(a, Cfg.BulletValueFor(Owner.Bullets, GameWorld.MatchElapsed),
                            Cfg.BulletSpeed * Random.Range(0.85f, 1.15f),
                            Cfg.BulletLifetime, false);
            }
            SpendShots(Cfg.BulletsPerVolleyShot,
                       Cfg.BulletValueFor(Owner.Bullets, GameWorld.MatchElapsed));
        }

        void TryVolley(float now)
        {
            if (now < _nextVolley) return;
            // ★ 没弹药就不许打 —— 之前这里没检查，子弹归零了它还在每 6 秒打一把大数值弹丸
            if (!CanShoot()) { _nextVolley = now + 1f; return; }
            var ball = Owner.BallAt(Owner.VolleyBallIndex);
            if (ball == null) { _nextVolley = now + 1f; return; }
            _nextVolley = now + Cfg.VolleyInterval;
            for (int i = 0; i < Cfg.BulletsPerVolleyShot; i++)
            {
                float a = Random.Range(-14f, 14f);
                SpawnBullet(a, ball.Value * Cfg.VolleyUsesBallValueFraction, Cfg.VolleyProjectileSpeed,
                            Cfg.VolleyProjectileLife, true);
            }
        }

        /// <summary>近防系统：找最近的敌方大球。</summary>
        /// <summary>
        /// 近防系统的瞄准点。**大球和敌方子弹（含尾流）一起比，谁近锁谁**。
        /// 大球不再无条件优先：一颗已经贴到脸上的子弹显然比 4 米外的大球更该先打。
        /// </summary>
        bool FindCiwsAimPoint(out Vector2 point)
        {
            point = Vector2.zero;
            var world = GameWorld.Instance;
            if (world == null) return false;

            Vector2 me = WorldPosition;
            float best = Cfg.CiwsRange * Cfg.CiwsRange;
            bool found = false;

            var factions = world.Factions;
            for (int f = 0; f < factions.Count; f++)
            {
                var faction = factions[f];
                if (faction == Owner) continue;
                for (int i = 0; i < faction.BigBalls.Count; i++)
                {
                    var b = faction.BigBalls[i];
                    if (b == null || b.Consumed) continue;
                    Vector2 p = b.transform.position;
                    float d = (p - me).sqrMagnitude;
                    if (d < best) { best = d; point = p; found = true; }
                }
            }

            // 敌方子弹 / 尾流。只要比已经锁上的目标更近就改锁它。
            if (world.Bullets != null)
            {
                Vector2 bp;
                float bd;
                if (world.Bullets.TryFindNearestEnemyBullet(me, Cfg.CiwsRange, Owner, out bp, out bd)
                    && bd * bd < best)
                {
                    point = bp;
                    found = true;
                }
            }

            return found;
        }

        public void TakeShieldDamage(double amount)
        {
            if (IsDead || amount <= 0d) return;
            Owner.Shield = System.Math.Max(Owner.Shield - amount, 0d);
        }

        /// <summary>撞到炮台本体。规格：护盾归零之后这一下才致命。</summary>
        public void HitBody()
        {
            if (IsDead) return;
            if (Owner.Shield > 0d) return;
            if (!Cfg.TurretDiesOnBodyHit) return;
            Die();
        }

        public void Die()
        {
            if (IsDead) return;
            IsDead = true;
            Owner.Alive = false;
            Owner.DeathTime = Time.time;
            if (_shieldTrigger != null) _shieldTrigger.enabled = false;
            if (GameWorld.Instance != null) GameWorld.Instance.OnTurretDied(Owner);
        }

        public void Revive()
        {
            IsDead = false;
            Owner.Alive = true;
            Owner.DeathTime = -1f;
            _nextDirect = _nextScatter = _nextVolley = Time.time;
        }
    }
}
