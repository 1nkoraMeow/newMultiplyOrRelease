using System;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 大球：在战场里滚动、涂色、和别的大球相撞结算。
    ///
    /// 半径公式（缓动）：radius += ((ln(值)/8.3 - 0.5) - radius) / 4.0
    ///
    /// 涂色消耗（规格）：每占领 1 个像素扣 1 点数值。
    ///
    /// 碰撞规则（规格）—— ★★ **三条的比例现在「不统一」，别顺手改回统一**：
    ///   · 不同阵营：双方各扣 min(a, b) × `BigBallClashLossFraction`（**1/4**）
    ///   · 同阵营且双方都 &lt; 100M：只给物理推力
    ///   · 同阵营且有一方是引力球：小球把**自身的 3/4** 转给引力球
    ///   · 同阵营两颗引力球相撞：小的把**自身的 3/4** 转给大的
    ///   （第 92 轮曾把第一条也改成 3/4，**第 99 轮用户又单独改回 1/4** —— 另两条没动。）
    ///
    /// ★★ 第 111 轮新增**吸血球**（`IsVampire`）：它和普通大球完全一样，只多一条
    ///   「不同阵营」的规则 —— 敌方数值 &lt; 自身的 50% 时，**吸走敌方 50% 的数值**
    ///   （不扣自己的血），否则按上面第一条正常扣血。
    ///   ★ 第 124 轮（用户）：**碰到的也是吸血球时，按正常大球扣血**（各扣 min 的 1/4），
    ///   互相不吸。生成后一直挂着 <see cref="VampireFx"/> 那圈深灰螺旋，和普通大球一眼能分开。
    ///
    /// 自走（规格）：数值 &gt; 1M 的大球会向后喷射子弹（1 发 = 128）反冲提速，防止原地不动。
    ///
    /// 中子星（第 89 轮起）：≥ 1B 就是中子星，半径走另一套公式，看着是**坍缩**下去。
    /// ★★ **第 101 轮（用户）：「大球变成中子星后不会再变回大球了」** ——
    ///   所以「是不是中子星」是个**单向闩锁**（见 <see cref="IsNeutronStar"/>），
    ///   数值后来掉回 1B 以下也不会长回大球的样子。
    /// </summary>
    public class BigBall : MonoBehaviour
    {
        public Faction Owner;
        public double Value = 1.0;
        /// <summary>
        /// ★★ 第 111 轮（用户）：**吸血球** —— 和普通大球一模一样，只多一条碰撞规则
        /// （见 <see cref="ResolveCrossFaction"/>）。生成后身上一直挂着
        /// <see cref="VampireFx"/> 那圈深灰螺旋特效，用来和普通大球区分。
        /// </summary>
        public bool IsVampire;
        /// <summary>
        /// **显示用的过渡值**：HUD 上的大球数字读它，不读 `Value`。
        /// 数值突变（吸收 / 乘法门）时让它滚几帧再到位（和半径一个平滑系数），
        /// 避免数字瞬间跳变。出生时 = `Value`。
        /// </summary>
        public double DisplayValue = 1.0;
        public float Radius = 0.4f;
        public bool Consumed;

        float _lastClashTime = -999f;
        Vector2 _lastPaintPos;
        float _paintEps2;
        float _jetCredit;
        /// <summary>尾流反冲的方向（慢速时沿用上一次，避免被随机方向踢出抖动）。</summary>
        Vector2 _recoilDir;
        float _stuckTimer;
        Vector2 _lastVel;

        Rigidbody2D _rb;
        SpriteRenderer _body;
        SpriteRenderer _tail;
        SpriteRenderer _outline;
        bool _wasNeutronStar;
        /// <summary>引力球的持续特效（一圈圈外扩的涟漪），只在 `HasGravity` 时开着
        /// （★ 闩锁：数值掉回 100M 以下也**照旧有引力**，所以特效也要留着）。</summary>
        GravityFx _gravityFx;

        public Rigidbody2D Body { get { return _rb; } }
        /// <summary>
        /// 当前数值**在门槛之上** —— 决定「30% 吸收 / 70% 反弹」和**球与球之间**的数值传递
        /// （`GravityBallAbsorbFraction` / `GravityBallMutualFraction`）。
        /// </summary>
        public bool IsGravityBall { get { return Value > Cfg.GravityBallThreshold; } }

        /// <summary>
        /// ★★★ 第 103 轮（用户）：「**当引力球数值低于 100M 时也可以具有引力**，引力公式不变，
        /// 但是可以吃己方子弹，**但不能被传递 / 传递数值**」。
        ///
        /// 所以「**有没有引力**」和「**算不算引力球**」现在是两个量：
        ///   · `HasGravity` —— **只置位、永不清零的闩锁**：一旦到过 100M 就永远有引力
        ///     （施加给子弹、施加给其他大球、涟漪特效，都读它）；
        ///   · `IsGravityBall` —— **实时**判数值，管数值传递那两条路。
        /// 于是数值掉回 100M 以下（被撞掉、被吸走）之后：**引力照旧**，
        /// 己方子弹照旧被它吃掉，但**不会再跟同阵营的球互相倒数值**。
        /// ★ 和 `IsNeutronStar` 一样，置位写在 getter 里 —— `Value` 到处都在直接改，
        ///   没有一个统一的写入口可以挂钩；这里只是个幂等的单向闩锁。
        /// </summary>
        public bool HasGravity
        {
            get
            {
                if (!_hasGravity && Value > Cfg.GravityBallThreshold) _hasGravity = true;
                return _hasGravity;
            }
        }
        bool _hasGravity;
        /// <summary>
        /// ★★ 中子星：数值到了门槛就是（**派生**，不是另存一个开关 ——
        /// 大球在场上长大和「增倍球选大球」两条路因此天然一致）。
        ///
        /// ★★ 第 101 轮（用户）：「大球变成中子星后**不会再变回大球**了」——
        ///   所以这里加了一个**只置位、永不清零的闩锁**：一旦到过 1B，
        ///   后来数值被撞架扣下去、被引力球吸走，掉回 1B 以下也**不会**重新长回大球的
        ///   尺寸/外观（半径公式、HUD 的光晕判断都读这一个属性）。
        ///   ★ 置位写在 getter 里是有意的：`Value` 是个到处都在直接改的公开字段，
        ///     没有一个统一的写入口可以挂钩；这里只是个幂等的单向闩锁，没有副作用风险。
        /// </summary>
        public bool IsNeutronStar
        {
            get
            {
                if (!_neutronStar && Value >= Cfg.NeutronStarThreshold) _neutronStar = true;
                return _neutronStar;
            }
        }
        bool _neutronStar;
        /// <summary>刚刚坍缩成中子星的那一瞬间（用来放一下闪），只读一次。</summary>
        public bool ConsumeCollapsePulse() { bool p = _collapsePulse; _collapsePulse = false; return p; }
        bool _collapsePulse;

        public static BigBall Create(Faction owner, Vector2 worldPos, double value, Transform parent,
                                     bool vampire = false)
        {
            var go = new GameObject(vampire ? "VampireBall_" + owner.Index : "BigBall_" + owner.Index);
            go.transform.position = worldPos;
            if (parent != null) go.transform.SetParent(parent, true);

            var ball = go.AddComponent<BigBall>();
            ball.Owner = owner;
            ball.IsVampire = vampire;
            ball.Value = Math.Max(value, 0.0001d);
            ball.DisplayValue = ball.Value;      // 出生时直接到位，不从 0 滚上来

            // 规格：大球的拖尾删掉（不要了）

            // 黑色描边（比本体略大、画在后面）
            ball._outline = Primitives.MakeNode("Outline", Primitives.Circle, Color.black, 60, go.transform);
            ball._outline.transform.localScale = Vector3.one * (1f + Cfg.BigBallOutlineFactor);

            // ★ 大球的颜色 = 阵营色按 HSV 调：饱和度 × `BigBallSaturation`、明度 V + `BigBallValueAdd`
            ball._body = Primitives.MakeNode("Body", Primitives.Circle,
                Palette.ShiftHsv(owner.Color, Cfg.BigBallSaturation, Cfg.BigBallValueAdd),
                61, go.transform);
            ball._body.transform.localScale = Vector3.one;

            // ★ 用户：「中子星上有一层白色的圆圈，不知道是何物」——那是我自己加的白芯 + 白晕，
            //   他并没有要求过。按「不加额外的改动」的原则**整个去掉**，中子星就是
            //   「一颗小得多的大球」（黑描边 + 阵营色，尺寸差异本身就说明了它是中子星）。

            // 规格：引力球要有**持续的特效**（普通大球没有）
            ball._gravityFx = GravityFx.Attach(go.transform, owner.Color);
            ball._gravityFx.gameObject.SetActive(false);

            // ★★ 第 111 轮：**吸血球**的持续特效（深灰螺旋，照用户给的 Algodoo 那段）。
            //   和引力球一样挂在球上，球一销毁它跟着走，不用单独回收。
            if (vampire) VampireFx.Attach(go.transform);

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;                      // 战场是俯视的，没有重力
            rb.drag = 0f;              // 规格：大球弹起来**不会越弹越慢**
            rb.angularDrag = 0f;
            rb.mass = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.freezeRotation = true;
            rb.sharedMaterial = Physics2DConfig.Perfect;
            ball._rb = rb;

            // 规格：大球从炮台出来时**从 0 开始长**，约 0.8 秒长到对应大小
            ball.Radius = 0.0005f;
            ball.ApplyRadius();
            ball._lastPaintPos = go.transform.position;
            ball._paintEps2 = 1f / (Cfg.TexelsPerUnit * Cfg.TexelsPerUnit);
            ball._jetCredit = 0f;
            ball._stuckTimer = 0f;
            return ball;
        }

        /// <summary>这个数值对应的**目标**半径（中子星和大球是两套公式）。</summary>
        float TargetRadius()
        {
            if (IsNeutronStar)
                return NeutronStarTargetRadius();
            // ★ `BigBallRadiusScale` 是**纯乘法**系数（等比缩放），见 Cfg 里的说明
            return (Cfg.LogV((float)Value) / Cfg.BigBallRadiusLogDivisor + Cfg.BigBallRadiusBias)
                   * Cfg.BigBallRadiusScale;
        }

        /// <summary>中子星的目标半径：`ln(√数值) / 30 - 0.04`。</summary>
        static float NeutronStarTargetRadius(double value)
        {
            float v = Mathf.Max((float)value, 1f);
            return Mathf.Log(Mathf.Sqrt(v)) / Cfg.NeutronStarRadiusLogDivisor
                   - Cfg.NeutronStarRadiusBias;
        }

        /// <summary>自检用：这一颗**当前**该有的目标半径（含中子星闩锁的判断）。</summary>
        public float TargetRadiusForSmoke() { return TargetRadius(); }
        /// <summary>
        /// 当前数值对应的**目标**半径（世界单位）。
        /// ★ 第 111 轮：吸血特效每一轮螺旋都从「球半径 + 0.2」起转 —— 但球出生时是从
        ///   0.0005 慢慢长到目标大小的（`Create` 里那一行），只读 `Radius` 的话头 1.7 秒
        ///   螺旋会缩在球里面看不见。所以取「当前半径」和「目标半径」里大的那个。
        /// </summary>
        public float TargetRadiusWorld { get { return TargetRadius(); } }
        /// <summary>自检用：**假如按大球公式**算，这个数值会是多大半径。</summary>
        public static float BigBallRadiusForSmoke(double value)
        {
            return (Cfg.LogV((float)value) / Cfg.BigBallRadiusLogDivisor + Cfg.BigBallRadiusBias)
                   * Cfg.BigBallRadiusScale;
        }
        float NeutronStarTargetRadius() { return NeutronStarTargetRadius(Value); }

        void RefreshRadiusImmediate()
        {
            Radius = Mathf.Clamp(TargetRadius(), 0.0005f, Cfg.RadiusMax);
            ApplyRadius();
        }

        void ApplyRadius()
        {
            transform.localScale = Vector3.one * (Radius * 2f);
            // ★★ 规格（用户，原话照抄）：**密度** = 数值 × 0.9 / (radius² × π) / 800
            //   —— 注意他特别强调「这是**密度**不是质量」。
            //   Unity 的 Rigidbody2D 收的是**质量**，所以还得乘回面积（π r²）：
            //       mass = density × π r² = 数值 × 0.9 / 800        ← r 被约掉了
            //   两步都写在代码里，就是为了以后一眼能看出「哪个是密度、哪个是质量」。
            //   （旧代码把质量夹在 3000：80M 要 90000，被夹成 3000；2M 只要 2250 ——
            //     两者差不多重，所以 2M 能把 80M 推走。这就是那个 bug。）
            if (_rb != null)
            {
                double r2pi = (double)Radius * Radius * System.Math.PI;
                double density = (r2pi > 1e-12) ? Value * 0.9d / r2pi / 800d : 0d;
                double mass = density * r2pi;
                _rb.mass = (float)System.Math.Min(
                    System.Math.Max(mass, 1e-4d), Cfg.BigBallMaxMass);
            }
            if (_outline != null) _outline.transform.localScale = Vector3.one * (1f + Cfg.BigBallOutlineFactor);
            // （大球拖尾已按规格删除）
        }

        /// <summary>
        /// 大球**数值文字**的过渡：数值突变（吸收 / 乘法门）时滚几帧再到位，
        /// 而不是一帧跳过去（几帧的滚动比瞬间跳更容易看清"变了多少"）。
        /// ★ 差到只剩 0.1% 就吸附：免得永远差一点点，那会让 HUD 每帧都重建字符串。
        /// ★ 单独一层是为了自检能直接调它（不用跑整条 FixedUpdate 的物理/涂色）。
        /// </summary>
        void StepDisplayValue()
        {
            if (DisplayValue == Value) return;
            // 「几帧」的量级：每物理帧收掉 1/4（≈4 帧走到 70%、10 帧到 95%）。
            //   ★ 别跟半径的 `BigBallRadiusSmooth`(10) 走 —— 那是 0.8 秒的成长，
            //     数字滚 0.8 秒就太拖了，读起来像掉帧。
            const float steps = 2f;
            double nd = DisplayValue + (Value - DisplayValue) / steps;
            if (System.Math.Abs(Value - nd) <= System.Math.Abs(Value) * 0.001d) nd = Value;
            DisplayValue = nd;
        }

        /// <summary>
        /// 兜底：大球的**圆心**必须在 `[半径, 地图边长 − 半径]` 之内。
        /// 墙的内壁正好在 `0` 和 `MapWorldSize`（墙是贴在它外面的），所以这条和物理墙等价，
        /// 只是**无条件成立** —— 求解器把球推出去的那一帧，这里会把它摆回来。
        /// </summary>
        void ClampInsideArena()
        {
            if (_rb == null) return;
            float w = Cfg.MapWorldSize;
            float lo = Radius, hi = w - Radius;
            if (hi < lo) { lo = hi = w * 0.5f; }        // 球比地图还大（理论上不会）：摆正中间

            Vector2 p = _rb.position;
            float cx = Mathf.Clamp(p.x, lo, hi);
            float cy = Mathf.Clamp(p.y, lo, hi);
            if (cx == p.x && cy == p.y) return;

            _rb.position = new Vector2(cx, cy);
            Vector2 v = _rb.velocity;
            if (p.x != cx) v.x = p.x < lo ? Mathf.Abs(v.x) : -Mathf.Abs(v.x);
            if (p.y != cy) v.y = p.y < lo ? Mathf.Abs(v.y) : -Mathf.Abs(v.y);
            _rb.velocity = v;
        }

        void FixedUpdate()
        {
            // 引力球特效：只在「**有引力**」的时候开着（只有状态变了才动 SetActive）
            // ★ 读 `HasGravity`（闩锁），不是 `IsGravityBall`（实时）—— 数值掉回 100M 以下
            //   之后引力还在，涟漪就得跟着留着，不然「还有引力却看不见特效」。
            if (_gravityFx != null)
            {
                bool want = !Consumed && HasGravity;
                if (_gravityFx.gameObject.activeSelf != want)
                {
                    _gravityFx.gameObject.SetActive(want);
                    if (!want) _gravityFx.Silence();
                }
            }

            if (Consumed) return;

            // ★★ 第 93 轮（用户）：「亡语球过多导致大球穿到墙里去」。
            //   排队 + 螺旋散开（见 GameWorld.OnTurretDied）是**治本**，但求解器在极端情况下
            //   （十几个球同时长大、互相深重叠）仍然可能把某一颗的**位置**一下顶到墙外 ——
            //   连续性检测只防「速度快到一步跨过墙」，防不了这种「被解算推出去」。
            //   所以每步最后再过一道兜底：越界就摆回场内，并把朝外的速度反过来。
            if (Cfg.BigBallContainInArena) ClampInsideArena();

            // ★★ 中子星：**两条路都在这里合流** ——
            //   ① 大球在场上长到 1B：`IsNeutronStar` 这一帧翻成 true，
            //      目标半径从「大球公式」跳到「中子星公式」（1B 时 2.00m → 0.42m），
            //      缓动会把它**坍缩**下去，正好就是用户要的「从大球坍缩成中子星」。
            //   ② 增倍球选大球直接生成：一出生 IsNeutronStar 就是 true。
            bool nowNeutron = IsNeutronStar;
            if (nowNeutron && !_wasNeutronStar) _collapsePulse = true;   // 坍缩那一瞬间闪一下
            _wasNeutronStar = nowNeutron;

            // 半径缓动
            float target = TargetRadius();
            float smooth = nowNeutron ? Cfg.NeutronStarRadiusSmooth : Cfg.BigBallRadiusSmooth;
            // 规格：radius = radius + ((target - radius) / 4.0)，**没有下限**（多小都可以）
            float nr = Mathf.Clamp(Radius + (target - Radius) / smooth,
                                   0.0005f, Cfg.RadiusMax);
            // ★ 抖动的一个来源：每帧都改 localScale / 碰撞体半径，等于每帧把物理状态推翻一次。
            //   变化小于 0.5% 就先不动。
            // ★ 平滑：半径缓动**每步都写**，不要「差 0.5% 才更新一次」的台阶 ——
            //   那个台阶本身就是一个肉眼能看出来的抖源（尤其是大球一边滚一边长大/缩小时）。
            Radius = nr;
            ApplyRadius();

            StepDisplayValue();

            _lastVel = _rb.velocity;
            if (_tail != null) Primitives.FaceVelocity(_tail.transform, _lastVel);

            // 涂色 + 按像素扣数值
            var field = GameWorld.Instance != null ? GameWorld.Instance.Field : null;
            // 没挪窝就不用重涂：大球的圆盘是 5 万像素，静止时每帧重算纯属浪费
            bool movedEnough = (_rb.position - _lastPaintPos).sqrMagnitude > _paintEps2;
            if (field != null && movedEnough)
            {
                Vector2 from = _lastPaintPos;
                Vector2 to = _rb.position;
                _lastPaintPos = to;
                // ★ 和子弹同一个账：**先看自己买得起几格，再让地图只涂这么多格**。
                //   以前是先涂满整个圆盘（几万格）再扣——数值不够就白刷。
                // ★★ 用户报的「正常的中子星涂了一点但涂不全」的**根因**：
                //   以前这里写的是 `(int)(Value / PixelCaptureCost)`，而 Unity 的 Mono
                //   对**超范围**的 double→int 给的是 `int.MinValue`（负数）——
                //   数值一过 21.4 亿，预算就成了负的，`left <= 0` 第一轮就 break，
                //   **一格都涂不上**。亡语那颗是从子弹数折算出来的、数值小，所以看不出问题；
                //   而从高数值增倍球直接变成的中子星必然过线。
                //   现在统一走 `Cfg.PaintBudget`（夹到 int 上限，永远不会负）。
                int afford = Cfg.PaintBudget(Value);

                // ★★ 沿「上一帧 → 这一帧」这条路**补中间圆盘**。
                //   用户报的「中子星的涂色断断续续」就是这么来的：中子星半径只有 0.3m 出头，
                //   一帧却能挪出好几个半径，只在**终点**画一个圆盘 → 画出来是一条**虚线**。
                //   大球半径大看不出来，越小越明显。步长取半个半径，最多 12 步（有上限，不失控）。
                float dist = (to - from).magnitude;
                int steps = Mathf.Clamp(
                    Mathf.CeilToInt(dist / Mathf.Max(Radius * 0.5f, 1e-4f)), 1, Cfg.BigBallPaintMaxSteps);
                int painted = 0;
                for (int s = 1; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(from, to, (float)s / steps);
                    int left = afford - painted;          // 预算也要跟着扣，别超支
                    if (left <= 0) break;
                    painted += field.Paint(p, Radius, Owner.Index, 1f, Vector2.right, left);
                }
                if (painted > 0)
                {
                    // 规格：1 像素扣 1 点数值。★ 扣**阵营的子弹数**（和子弹共用一个池），
                    // 否则大球拿自己的数值白刷一片，地图会「没花阵营的钱就填满了」。
                    double cost = painted * Cfg.PixelCaptureCost
                                  + Value * Cfg.PixelCaptureCostFraction * painted;
                    if (cost > 0d)
                    {
                        // ① 阵营账上付钱（「1 像素 1 数值」算在阵营的子弹数上）
                        if (Cfg.BigBallPaintChargesFactionStock && Owner != null)
                            Owner.Bullets = System.Math.Max(Owner.Bullets - cost, 0d);
                        // ② 球**自己**的数字也要往下掉（滚动涂地时看得见的那一路）
                        if (Cfg.BigBallPaintChargesOwnValue)
                            Value = Math.Max(Value - cost, 0.0001d);
                    }
                }
            }

            // 数值 > 1M 的球向后喷子弹反冲提速，避免原地不动
            // 规格：大球速度要有上限
            {
                var v = _rb.velocity;
                float sp = v.magnitude;
                if (sp > Cfg.BigBallMaxSpeed) _rb.velocity = v * (Cfg.BigBallMaxSpeed / sp);

                // 卡墙角自救：**真的**长时间一动不动，才朝**离开最近那面墙**的方向轻轻推一下。
                //
                // ★★ 用户第 90 轮问的「大球/中子星为什么**无缘无故**转向」就是这里：
                //   ① 门槛曾经是 0.35，比尾流加速的巡航速度（BigBallJetBoostMaxSpeed = 0.25）**还高** ——
                //      被尾流稳稳推着走的球在系统眼里一直是「卡住」，每 1.2 秒吃一次踢；
                //   ② 踢的方向是 `Random.Range(0, 2π)` —— **纯随机**，所以看着就是毫无理由地拐弯。
                //   现在：门槛压到真的几乎静止（见 Cfg 那条注释），方向也换成确定性的。
                if (sp < Cfg.BigBallStuckSpeed)
                {
                    _stuckTimer += Time.fixedDeltaTime;
                    if (_stuckTimer > Cfg.BigBallStuckRescueTime)
                    {
                        _stuckTimer = 0f;
                        _rb.velocity = StuckRescueDir() * Cfg.BigBallStuckKickSpeed;
                    }
                }
                else _stuckTimer = 0f;
            }

            if (Value > Cfg.BigBallJetThreshold) TryJet();

            // ★★ 用户第 92 轮：「数值 4.6 / 3.7 的大球**死不掉**，一直在战场上游荡」——
            //   现在数值掉到 `BigBallMinValue` 以下就收掉（见 Cfg 那条注释：
            //   撞球扣的是比例、子弹又几乎打不中这种尘点，两条路都杀不死它）。
            if (Value < Cfg.BigBallMinValue) Consume();
        }

        /// <summary>
        /// 卡住时往哪个方向踢：**离开最近的那一面墙**（确定性）。
        /// 以前是随机方向 —— 那会让球「无缘无故」拐一个弯，用户一眼就看出来了。
        /// </summary>
        Vector2 StuckRescueDir()
        {
            Vector2 p = _rb.position;
            float mx = Mathf.Min(p.x, Cfg.MapWorldSize - p.x);      // 离左右墙哪个近
            float my = Mathf.Min(p.y, Cfg.MapWorldSize - p.y);      // 离上下墙哪个近
            if (mx < my)
                return new Vector2(p.x < Cfg.MapWorldSize * 0.5f ? 1f : -1f, 0f);
            return new Vector2(0f, p.y < Cfg.MapWorldSize * 0.5f ? 1f : -1f);
        }

        /// <summary>向后喷射子弹：给反冲、提速、顺便把地面上「停住」的情况救回来。</summary>
        void TryJet()
        {
            // 规格：尾流是**一颗一颗随机**喷的，不是一排一排
            _jetCredit += Cfg.BigBallJetRate * Time.fixedDeltaTime;
            if (_jetCredit < 1f) return;
            _jetCredit -= 1f;

            // ★ 反冲方向：只在真的在动的时候才更新，慢下来就**沿用上一次的方向**。
            //   以前速度小的时候会**随机取一个方向**，而尾流是 90 发/秒 ——
            //   球一慢下来就被随机踢 90 次/秒，看着就是「微弱的左右抖动」。
            //   （真要救卡住，下面还有「长时间不动轻轻推一下」那一条，别靠随机反冲。）
            Vector2 dir;
            if (_lastVel.sqrMagnitude > 0.04f)
            {
                _recoilDir = -_lastVel.normalized;
                dir = _recoilDir;
            }
            else if (_recoilDir.sqrMagnitude > 0.5f)
            {
                dir = _recoilDir;
            }
            else
            {
                // 还没动过（刚生成）：朝地图中心，总比随机踢好
                Vector2 toCenter = new Vector2(Cfg.MapWorldSize * 0.5f, Cfg.MapWorldSize * 0.5f)
                                   - _rb.position;
                dir = toCenter.sqrMagnitude > 1e-4f ? toCenter.normalized : Vector2.up;
                _recoilDir = dir;
            }

            var pool = BulletPool.Instance;
            if (pool != null)
            {
                // 规格：尾流不是从一个点喷，而是从大球**整个背面**（固定距离上）的随机点喷
                Vector2 back = dir * (Radius + Cfg.BigBallJetMuzzleGap);
                Vector2 perp = new Vector2(-dir.y, dir.x);
                Vector2 muzzle = _rb.position + back + perp * UnityEngine.Random.Range(-Radius, Radius);
                float spread = UnityEngine.Random.Range(-Cfg.BigBallJetSpreadDeg, Cfg.BigBallJetSpreadDeg)
                               * Mathf.Deg2Rad;
                float cs = Mathf.Cos(spread), sn = Mathf.Sin(spread);
                var v = new Vector2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
                pool.Rent(Owner, muzzle, v, Cfg.BigBallJetBulletSpeed,
                          Cfg.BigBallJetBulletValue, 2.5f, false);
            }

            if (Cfg.BigBallJetConsumesValue)
                Value = Math.Max(Value - Cfg.BigBallJetBulletValue, 0.0001d);

            // ★★ 规格（用户第 88 轮）：尾流**照常喷**（上面那些一点都不动），
            //   但这份**加速**只在大球「几乎没动」的时候才给 ——
            //   一旦脱离慢速状态就不再提供加速（让它自己靠惯性/反弹走）。
            //   以前是无条件每发都加，球会一路被顶到速度上限、飞得像个炮弹。
            var cur = _rb.velocity;
            float curSp = cur.magnitude;
            if (curSp >= Cfg.BigBallJetBoostMaxSpeed) return;

            // 反冲：**直接给速度增量**。大球质量 = 值×0.9/800（1M 就是 1125kg），
            // 给冲量的话 Δv 只有 0.001 量级，等于没有加速。
            var nv = cur - dir * Cfg.BigBallJetSpeedGain;
            float sp2 = nv.magnitude;
            if (sp2 > Cfg.BigBallMaxSpeed) nv *= Cfg.BigBallMaxSpeed / sp2;
            _rb.velocity = nv;
        }

        /// <summary>撞到炮台：护盾判定框 → 磨护盾；本体判定框 → 护盾已归零就直接打死。</summary>
        void OnTriggerEnter2D(Collider2D other)
        {
            if (Consumed) return;
            var turret = other.GetComponentInParent<Turret>();
            if (turret == null || turret.IsDead || turret.Owner == Owner) return;

            if (turret.IsShieldCollider(other)) ResolveShieldHit(turret);
            else if (turret.IsBodyCollider(other)) turret.HitBody();
        }

        /// <summary>
        /// 大球撞护盾（规格给的三条规则）：
        ///   · 大球数值的 1/8 > 护盾  → **直接破盾，不反弹**：速度降到原来的 1/20、
        ///     方向不变继续往前冲；并且**扣掉整个护盾的数值**
        ///   · 大球数值的 1/8 < 护盾 且 大球数值 &lt; 护盾
        ///         → 护盾 -= 大球数值 × 3/4；大球也扣掉同样的量（**反弹**）
        ///   · 其余（大球数值 ≥ 护盾，但还不到 8 倍）
        ///         → 护盾 -= 护盾 × 3/4（剩 1/4）；大球也扣掉同样的量（**反弹**）
        /// </summary>
        void ResolveShieldHit(Turret turret)
        {
            if (turret == null || turret.Owner == null) return;
            double p = Value;                 // 大球数值（规格里的 _pdsz）
            double s = turret.Owner.Shield;
            if (s <= 0d) return;

            Vector2 away = ((Vector2)transform.position - turret.WorldPosition);
            if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
            away = away.normalized;

            if (p / 8d > s)
            {
                // ★ 用户：**破盾不反弹** —— 速度降到原来的 1/20，方向不变，继续往前冲。
                //   （原来这里是镜面反射 + 一脚推开，破盾的球会被弹回去。）
                // ★ 大球也要**扣掉被砸碎的那个护盾的数值** ——
                //   不然「大球砸盾」是免费的，小球磨盾反而两败俱伤。
                //   进这个分支的前提是 `p > 8s`，扣完还剩 ≥ 7/8，不会一撞就没。
                turret.Owner.Shield = 0d;
                Drain(s);
                if (_rb != null)
                {
                    Vector2 nv = _rb.velocity * Mathf.Clamp01(Cfg.BigBallShieldBreakSpeedScale);
                    _rb.velocity = nv;
                    _lastVel = nv;            // 撞墙的镜面反射读的是这个，得一起更新
                }
            }
            else if (p < s)
            {
                // ★ 反弹要**直接给速度**：大球质量 = 值×0.9/800，值一大给冲量根本弹不动
                //   （这就是你看到「大球撞护盾没有反弹」的原因）
                Rebound(away);
                double loss = p / 4d * 3d;
                turret.Owner.Shield = System.Math.Max(s - loss, 0d);
                Drain(loss);
                Push(away * Cfg.BigBallShieldRebound);
            }
            else
            {
                Rebound(away);
                // ★ 规格（用户）：护盾**剩 1/4**（原来是 1/8）；大球扣的量跟护盾掉的一样 = 3/4
                double loss = s * (1d - 1d / 4d);
                Drain(loss);
                turret.Owner.Shield = s / 4d;
                Push(away * Cfg.BigBallShieldRebound);
            }
        }

        void OnCollisionEnter2D(Collision2D c)
        {
            if (Consumed) return;

            // ★★ 规格（用户第 92 轮）：「大球的反弹有时**不严格遵循反射角等于入射角**」。
            //   交给物理求解器是不够的，它有三种让人「不遵守反射定律」的情况：
            //     ① 材质有摩擦 → 切向速度被啃掉，出射角被掰弯；
            //     ② `Physics2D.bounceThreshold`（0.2）→ 速度低于它的碰撞**干脆不弹**；
            //     ③ 尾流反冲/卡住自救会额外给速度，方向未必垂直于墙。
            //   所以撞边界墙时这里**自己算一次严格镜面反射**（速度大小不变，只改方向）。
            // ★★ 第 92 轮：这里一度加过「撞边界墙就自己算一次镜面反射」，
            //   结果用户报「**大球撞墙直接不反弹了**」。回退了 —— 反弹交回给物理引擎，
            //   我们只负责把**会让引擎反射不准的东西**去掉（墙的摩擦、低速阈值），
            //   而不是抢引擎的活。（`BigBall.SmokeTest` 里有一条**真跑物理**的撞墙用例守着。）
            _ = c;
            // ★★ 第 112 轮：这一句就是「**吸血机制只在大球碰撞时生效**」的结构性保证。
            //   只有带 BigBall 组件的物理刚体会走到这里；子弹早就去物理化了
            //   （`Bullet` 是纯数据类，走 `BulletPool.ResolveBigBalls → Bullet.HitBigBall`），
            //   所以吸血判定永远不会被子弹触发。
            var other = c.collider != null ? c.collider.GetComponentInParent<BigBall>() : null;
            if (other == null || other == this || other.Consumed) return;

            // 两个物体都会收到回调，只让 InstanceID 小的那个负责结算
            if (GetInstanceID() > other.GetInstanceID()) return;
            if (Time.time - _lastClashTime < Cfg.ClashCooldown) return;
            _lastClashTime = Time.time;
            other._lastClashTime = Time.time;

            if (other.Owner != Owner) ResolveCrossFaction(other);
            else ResolveSameFaction(other);
        }

        /// <summary>
        /// 不同阵营**大球相撞**（吸血判定的唯一入口）。
        ///
        /// ★ 第 112 轮：只由 <see cref="OnCollisionEnter2D"/> 在两颗大球的物理碰撞时调用，
        ///   **子弹路径不经过这里**（子弹走 `Bullet.HitBigBall`，那边刻意不读 `IsVampire`）。
        ///   自检里有一条断言守住「子弹打吸血球 = 打普通大球」。
        ///
        /// 普通大球：双方各扣 `min(a,b) × BigBallClashLossFraction`（第 92 轮起 = 1/4）。
        ///
        /// ★★ 第 111 轮（用户）**吸血球**：「碰撞敌方大球时，如果敌方大球数值小于自身的 50%，
        ///   则吸收敌方大球 50% 的数值，否则按正常大球扣血」。所以：
        ///   · 敌方 &lt; 自身 × `VampireDrainThreshold` → 敌方掉 50%，自己加同样多（不扣自己的血）；
        ///   · 否则 → 走下面普通大球那一条。
        ///   · **两颗都是吸血球 → 一律走普通大球扣血**（第 124 轮，用户）：
        ///     谁都不吸谁，各扣 `min(a,b) × 1/4`。否则「大吸血球 vs 小吸血球」会变成
        ///     单方面吸干，两颗吸血球互相碰反而没有对称的结果。
        ///   · 这条结算只由 InstanceID 小的那一方调用一次（见 `OnCollisionEnter2D`），
        ///     所以两个方向都要判：自己吸血 / 对方吸血。
        /// </summary>
        void ResolveCrossFaction(BigBall other)
        {
            // ★★ 第 124 轮（用户）：「如果碰到的球也是吸血球，按正常大球扣血方式」
            //   —— 吸血只对**普通**大球生效；吸血球之间按普通大球规则互扣。
            if (IsVampire && other.IsVampire)
            {
                ResolveNormalClash(other);
                return;
            }

            if (IsVampire && other.Value < Value * Cfg.VampireDrainThreshold)
            {
                DrainEnemy(other);
                return;
            }
            if (other.IsVampire && Value < other.Value * Cfg.VampireDrainThreshold)
            {
                other.DrainEnemy(this);
                return;
            }

            ResolveNormalClash(other);
        }

        /// <summary>
        /// **普通大球扣血**：双方各扣 `min(a,b) × Cfg.BigBallClashLossFraction`（第 92 轮起 = 1/4），
        /// 扣到收尸线以下就消失。
        /// 第 124 轮抽成独立方法 —— 吸血球互撞也要走这一条。
        /// </summary>
        void ResolveNormalClash(BigBall other)
        {
            if (other == null || other.Consumed) return;
            double m = Math.Min(Value, other.Value) * Cfg.BigBallClashLossFraction;
            if (m <= 0d) return;
            Value = Math.Max(Value - m, 0.0001d);
            other.Value = Math.Max(other.Value - m, 0.0001d);
            if (Value < Cfg.BigBallMinValue) Consume();
            if (other.Value < Cfg.BigBallMinValue) other.Consume();
        }

        /// <summary>
        /// 吸血：从 `victim` 身上吸走它数值的 `Cfg.VampireDrainFraction`（50%），
        /// **等量加到自己身上**。被吸到收尸线以下就按普通规则收掉。
        /// ★ 被吸的一方掉的是**它自己的**比例（不是自己的），所以球越大被吸得越多。
        /// </summary>
        void DrainEnemy(BigBall victim)
        {
            if (victim == null || victim.Consumed) return;
            double m = victim.Value * Cfg.VampireDrainFraction;
            if (m <= 0d) return;
            victim.Value = Math.Max(victim.Value - m, 0.0001d);
            Value += m;
            if (victim.Value < Cfg.BigBallMinValue) victim.Consume();
        }

        /// <summary>同阵营：小值只给力；有引力球时按规格传递数值。</summary>
        void ResolveSameFaction(BigBall other)
        {
            bool aGrav = IsGravityBall;
            bool bGrav = other.IsGravityBall;
            if (!aGrav && !bGrav) return;                 // 双方都 < 100M：只给物理推力

            BigBall big = Value >= other.Value ? this : other;
            BigBall small = ReferenceEquals(big, this) ? other : this;

            if (big.IsGravityBall && small.IsGravityBall)
            {
                double m = Math.Min(Value, other.Value) * Cfg.GravityBallMutualFraction;
                if (small.Value > m)
                {
                    small.Value -= m;
                    big.Value += m;
                }
            }
            else if (big.IsGravityBall)
            {
                double m = small.Value * Cfg.GravityBallAbsorbFraction;
                if (m > 0d)
                {
                    small.Value = Math.Max(small.Value - m, 0.0001d);
                    big.Value += m;
                    if (small.Value < Cfg.BigBallMinValue) small.Consume();
                }
            }
        }

        /// <summary>沿指定方向弹开（直接设速度，跟质量无关）。</summary>
        /// <summary>
        /// 沿法线反射，**弹性系数 1.0** —— 速度大小完全不变（既不加也不减）。
        /// 以前这里是把速度直接设成一个固定值，那等于凭空改速度。
        /// </summary>
        public void Rebound(Vector2 normal) { Rebound(_rb != null ? _rb.velocity : Vector2.zero, normal); }

        /// <summary>
        /// 按给定的**入射速度**做镜面反射（速度大小不变）。
        ///
        /// ★★ 为什么要有这个重载：`OnCollisionEnter2D` 是**求解器处理完碰撞之后**才回调的，
        ///   那时 `_rb.velocity` 已经是**弹出之后**的速度了 —— 拿它再反射一次等于把球
        ///   **又推回墙里**，球就贴着墙不动了（用户报的「**大球撞墙直接不反弹了**」）。
        ///   所以撞墙那条路必须传**碰撞前**的速度（`_lastVel`，它在 FixedUpdate 里、
        ///   物理步之前采的）。
        /// </summary>
        public void Rebound(Vector2 incoming, Vector2 normal)
        {
            if (_rb == null || Consumed) return;
            float sp = incoming.magnitude;
            Vector2 n = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector2.up;
            Vector2 r = Vector2.Reflect(incoming, n);
            if (r.sqrMagnitude < 1e-6f) r = n;
            _lastVel = r.normalized * Mathf.Max(sp, Cfg.BigBallSpawnSpeed);
            _rb.velocity = _lastVel;
        }

        public void Push(Vector2 impulse)
        {
            if (_rb != null && !Consumed) _rb.AddForce(impulse, ForceMode2D.Impulse);
        }

        /// <summary>被扣数值（子弹、护盾碰撞等）。</summary>
        public void Drain(double amount)
        {
            if (amount <= 0d) return;
            Value = Math.Max(Value - amount, 0.0001d);
            if (Value < Cfg.BigBallMinValue) Consume();
        }

        public void Consume()
        {
            if (Consumed) return;
            Consumed = true;
            if (Owner != null) Owner.BigBalls.Remove(this);
            Destroy(gameObject);
        }
    }
}
