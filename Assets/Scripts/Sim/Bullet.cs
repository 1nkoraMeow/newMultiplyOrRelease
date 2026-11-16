using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 子弹。规则（按用户规格）：
    ///   · 打到 **己方** 球，且该球 &lt; 100M  → 给球加血（+Damage）
    ///   · 打到 **己方** 球，且该球 ≥ 100M  → **弹开**（反射），还会临时加速一点
    ///   · 打到 **敌方** 球                  → 扣敌方的数值
    ///   · 打到 **敌方护盾**                 → 见 ResolveEnemyShield
    ///   · 打到 **敌方炮台本体**（护盾已归零）→ 炮台死亡
    ///   · 数值没耗尽就**不消失**
    ///
    /// ★ 这一版**不再是物理体**：没有 Rigidbody2D、没有 Collider2D。
    ///   位置自己积分，碰撞交给 BulletPool 的均匀网格去查。
    ///   这样子弹数量可以上万而物理求解器完全不参与 —— 就是「不要子弹上限」的前提。
    ///
    /// ★ 拖尾回到 **Unity 自带的 TrailRenderer**（规格）。它按时间采样，天然就是
    ///   「长度 = 速度 × 时间、快时该长就长」，不用自己算四边形。
    ///   代价是每颗子弹多一个 Renderer，所以：材质**全局共享**、`time` 很小、
    ///   闲置的子弹整只 GameObject 是 SetActive(false) 的（不产生任何开销）。
    /// </summary>
    public class Bullet : MonoBehaviour
    {
        public Faction Owner;
        public double Damage = Cfg.BulletDamage;
        public float Life;
        public bool Heavy;
        public bool InUse;

        /// <summary>
        /// ★★★ 这颗子弹**已经被引力球转过多少度**（第 102 轮）。到了
        /// `Cfg.GravityBulletMaxTurnDeg` 就不再受引力 —— 这既是"快慢子弹一视同仁"的来源，
        /// 也是「子弹不会被吸住绕圈」的保险（总转角 &lt; 180° ⇒ 绕不成一圈）。
        /// 复用池子里捞出来时在 `Launch` 里清零。
        /// </summary>
        public float GravityTurnDeg;

        /// <summary>位置（世界坐标，自己积分）。</summary>
        public Vector2 Pos;
        /// <summary>速度。</summary>
        public Vector2 Vel;

        /// <summary>上一次物理步的位置。渲染时和 Pos 之间做插值，去掉「走一下停一下」的抖动。</summary>
        public Vector2 PrevPos;

        TrailRenderer _trail;
        /// <summary>这条拖尾当前的 gradient 是哪个颜色（池子复用 + 换阵营时要重设）。</summary>
        Color32 _trailColorSet;

        int _bounces;
        /// <summary>这颗子弹还能涂多少个像素（和大球一样：1 像素 = 1 数值）。</summary>
        double _paintBudget;
        Vector2 _dir;

        // ── 合批拖尾的历史点 ──
        // 头在 [0]、尾在 [TrailCount-1]，按时间从新到旧。点会**按时间过期**
        // （见 BulletTrailRenderer），所以子弹数值耗尽之后不再加点，尾巴就一点点收掉。
        // 只在「合批」画法下才分配（TrailRenderer 画法不需要）。
        public Vector2[] TrailPts;
        public float[] TrailT;
        public int TrailCount;

        /// <summary>
        /// 这颗子弹的拖尾生存时间（秒）—— 发射时按**它自己的单发数值**算一次
        /// （`Cfg.BulletTrailTimeFor`），所以数值大的子弹尾巴更长。
        /// 飞行途中不再改：否则涂掉一点数值尾巴就缩一下，看着像在抽搐。
        /// </summary>
        public float TrailLife = Cfg.BulletTrailTime;

        /// <summary>
        /// 合批拖尾用的**单调时钟**。由 `BulletPool.Update` 每帧推进 `dt`。
        /// 不用 `Time.time` 是为了可自检 —— 编辑器里跑测试时 `Time.time` 根本不走。
        /// </summary>
        public static float TrailClock;

        // 给 BulletRenderer 读的显示数据
        float _visRadius;
        Color32 _visColor;

        /// <summary>
        /// ★★ 第 102 轮（用户）：「子弹拖尾改成阵营颜色（但饱和度调低）」。
        /// 在 `Launch` 里按阵营色算一次（拖尾是**逐顶点**画的，别在那儿现算）。
        /// </summary>
        public Color32 TrailColor { get; private set; }

        /// <summary>
        /// ★★★ 第 103 轮（用户）：「**近防炮的子弹需要与普通子弹的视觉上的区分**」。
        /// 近防炮弹丸 = 白色圆点 + 白色拖尾（普通子弹是阵营色），一眼分得开。
        /// </summary>
        public bool IsCiws;
        /// <summary>把已经发射的这颗子弹标记成近防炮弹丸（颜色改白）。</summary>
        public void MarkCiws()
        {
            IsCiws = true;
            TrailColor = Cfg.CiwsBulletColor;
            if (_trail != null) _trail.Clear();
        }

        public Vector2 Dir { get { return _dir; } }
        public float VisualRadius { get { return _visRadius; } }
        public Color32 VisualColor { get { return _visColor; } }
        /// <summary>渲染用的位置：在上一物理步和当前步之间按 alpha 插值。</summary>
        public Vector2 RenderedPos(float alpha)
        {
            if (!Cfg.BulletInterpolate) return Pos;
            return Vector2.Lerp(PrevPos, Pos, Mathf.Clamp01(alpha));
        }

        // ★ 拖尾材质**全局共享一份**。每颗子弹各一份 Material = 每颗一次 SetPass，
        //   上千颗就是上千个 setpass call，这才是 TrailRenderer 的代价所在。
        static Material _trailMat;
        static Gradient _trailGradient;

        /// <summary>
        /// ★★★ 每家的拖尾颜色**不一样**（阵营色提饱和，太暗的换成白色），
        /// 但**材质是全阵营共享一份**的 —— 所以颜色绝不能挂在材质上。
        /// TrailRenderer 画法下颜色挂的是**每颗 Renderer 自己的 `colorGradient`**，
        /// 这里按颜色缓存 gradient：同一家共用一份，池子轮转时也不重建。
        /// ★ 走的口子和画法无关，永远只有 `Palette.BulletTrail` 一个。
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<int, Gradient> _trailGradients =
            new System.Collections.Generic.Dictionary<int, Gradient>();

        static Gradient GradientFor(Color32 c)
        {
            int key = (c.r << 16) | (c.g << 8) | c.b;
            Gradient g;
            if (_trailGradients.TryGetValue(key, out g)) return g;
            var col = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
            g = new Gradient();
            MakeGradientKeys(g, col);
            _trailGradients[key] = g;
            return g;
        }

        /// <summary>头亮尾淡：颜色整条用 `col`，只让 alpha 从头到尾掉下去。</summary>
        static void MakeGradientKeys(Gradient g, Color col)
        {
            float tail = Cfg.BulletTrailTailAlpha;
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(col, 0f),
                    new GradientColorKey(col, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(Cfg.BulletTrailHeadAlpha, 0f),
                    new GradientAlphaKey(Cfg.BulletTrailHeadAlpha * 0.35f, 0.45f),
                    new GradientAlphaKey(tail, 1f)
                });
        }

        static void EnsureTrailAssets()
        {
            if (_trailMat != null) return;
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            _trailMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _trailMat.mainTexture = Primitives.Streak.texture;

            // 默认（白）那把 —— 还没发射的子弹先拿它顶着，`Launch` 里会按阵营色换掉。
            _trailGradient = new Gradient();
            MakeGradientKeys(_trailGradient, Color.white);
        }

        public static Bullet CreateBlank(Transform parent)
        {
            var go = new GameObject("Bullet");
            if (parent != null) go.transform.SetParent(parent, false);
            // ★ 子弹整体放 BulletLayer：左栏那个「游荡投影」不看这一层 ——
            //   战场里上千颗子弹 + 拖尾是画面上最贵的一块，而投影只是 25% 的底纹。
            go.layer = Cfg.BulletLayer;
            var b = go.AddComponent<Bullet>();

            // ★ 只有「TrailRenderer 画法」才建拖尾子物体。
            //   合批画法下这整段都不执行 —— 少掉的正是「每颗子弹一个 Renderer」，
            //   不但省 draw call，连池子预热都快得多（不用建几千个 TrailRenderer）。
            if (!Cfg.BulletTrailUsesTrailRenderer)
            {
                go.SetActive(false);
                return b;
            }

            EnsureTrailAssets();

            // 规格：拖尾用 Unity 自带的 TrailRenderer
            var tgo = new GameObject("Trail");
            tgo.transform.SetParent(go.transform, false);
            tgo.layer = Cfg.BulletLayer;                       // 拖尾同理（投影不画它）
            var tr = tgo.AddComponent<TrailRenderer>();
            tr.sharedMaterial = _trailMat;
            tr.colorGradient = _trailGradient;
            tr.time = Cfg.BulletTrailTime;               // 长度 = 速度 × time，快时自然长
            tr.minVertexDistance = Cfg.BulletTrailMinVertex;
            // 宽度在 Launch 里按**这颗子弹自己的显示半径**设
            // （规格：拖尾不比子弹细 = 半径 × `Cfg.BulletTrailRadiusFactor`，系数 1 = 一样宽）。
            // 这里先给个安全值，免得有谁拿到还没发射的子弹。
            tr.startWidth = Cfg.BulletDotRadius * 2f * Cfg.BulletTrailRadiusFactor;
            tr.endWidth = Cfg.BulletDotRadius * 2f * Cfg.BulletTrailEndRadiusFactor;
            tr.numCapVertices = 0;                       // 圆头/折角都关掉，顶点数才是最小
            tr.numCornerVertices = 0;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.autodestruct = false;                     // 对象池复用，绝不自杀
            tr.emitting = false;                         // 没发射就不记录任何点
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.sortingOrder = Cfg.BulletTrailSortingOrder;   // 规格：墙在拖尾上面
            b._trail = tr;

            go.SetActive(false);
            return b;
        }

        /// <summary>从池里取出来后用它初始化。</summary>
        public void Launch(Faction owner, Vector2 pos, Vector2 dir, float speed,
                           double damage, float life, bool heavy)
        {
            Owner = owner;
            Damage = damage;
            Life = life;
            Heavy = heavy;
            InUse = true;
            FadeTimer = 0f;
            _bounces = 0;
            GravityTurnDeg = 0f;        // ★ 池子复用：引力转角的额度要reset，别带着上一世的
            IsCiws = false;             // ★ 池子复用：别让上一世的近防炮弹丸身份串过来
            _paintBudget = Damage;
            // ★ 拖尾长度跟着**这颗子弹自己的数值**走（对数，见 `Cfg.BulletTrailTimeFor`）。
            TrailLife = Cfg.BulletTrailTimeFor(Damage);
            _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
            Vel = _dir * speed;
            Pos = pos;
            PrevPos = pos;              // 别让复用出来的子弹从上一世的终点拖一条线过来

            float r = heavy ? Cfg.VolleyProjectileRadius : Cfg.BulletDotRadius;
            _visRadius = r;
            // ★ 圆点本体：阵营色**降饱和**（S × `Cfg.BulletSaturation`，明度不变）
            _visColor = Palette.ShiftHsv(owner.Color, Cfg.BulletSaturation, 0f);
            // ★ 阵营色提饱和；**太暗的（黑家）换成白色**，不然拖尾看不见（见 Palette.BulletTrail）
            TrailColor = Palette.BulletTrail(owner.Color);

            // 合批拖尾：清掉上一世的历史点（不清的话会和上一世的点连成一条长线）
            TrailCount = 0;
            if (!Cfg.BulletTrailUsesTrailRenderer) RecordTrailPoint();

            // ★ 规格：**拖尾不比子弹细**（系数 1 = 和子弹一样宽）。
            //   从 _visRadius 推，所以不管这颗子弹是什么半径（齐射弹丸 / 以后调大的普通子弹），
            //   拖尾都跟着它变粗 / 变细，不会被写死成普通子弹的宽度。
            if (_trail != null)
            {
                _trail.startWidth = _visRadius * 2f * Cfg.BulletTrailRadiusFactor;
                _trail.endWidth = _visRadius * 2f * Cfg.BulletTrailEndRadiusFactor;
                _trail.time = TrailLife;
            }
            // ★ 终极优化：合批画法下**根本没人读这个 Transform**（圆点和拖尾都从 Pos/TrailPts 画），
            //   而 GameObject 一直是 inactive 的 —— 这里的两次写入纯属白干。
            if (Cfg.BulletTrailUsesTrailRenderer)
            {
                transform.localScale = Vector3.one;
                transform.position = pos;
            }

            if (_trail != null)
            {
                // ★ 颜色：材质是**全阵营共享**的，所以阵营色只能写在这颗子弹自己的 gradient 上。
                //   池子里轮转的子弹常常换阵营（甚至跨版本换皮），所以每次发射都比一次；
                //   同色就不写（Setter 是原生调用，几千发/秒下省下来是实打实的）。
                if (_trailColorSet.r != TrailColor.r || _trailColorSet.g != TrailColor.g
                    || _trailColorSet.b != TrailColor.b)
                {
                    _trail.colorGradient = GradientFor(TrailColor);
                    _trailColorSet = TrailColor;
                }
                _trail.Clear();          // ★ 复用前必须清：不清的话会和上一世的点连成一条长线
                _trail.emitting = true;
            }
        }

        /// <summary>
        /// 合批拖尾：把当前位置记成一个新的历史点。
        /// 走不到 <see cref="Cfg.BulletTrailMinVertex"/> 就**不记** —— 等价于
        /// TrailRenderer 的 `minVertexDistance`，是控制顶点数的关键。
        /// </summary>
        public void RecordTrailPoint()
        {
            if (TrailPts == null)
            {
                TrailPts = new Vector2[Cfg.BulletTrailMaxPoints];
                TrailT = new float[Cfg.BulletTrailMaxPoints];
            }

            if (TrailCount > 0)
            {
                float mv = Cfg.BulletTrailMinVertex;
                if ((TrailPts[0] - Pos).sqrMagnitude < mv * mv) return;
            }

            int cap = Cfg.BulletTrailMaxPoints;
            int keep = Mathf.Min(TrailCount, cap - 1);
            for (int i = keep; i > 0; i--)
            {
                TrailPts[i] = TrailPts[i - 1];
                TrailT[i] = TrailT[i - 1];
            }
            TrailPts[0] = Pos;
            TrailT[0] = TrailClock;
            TrailCount = keep + 1;
        }

        /// <summary>由池每物理帧调一次。返回 false 表示该回收了。</summary>
        /// <param name="field">战场地图。**由池子每帧取一次传进来** ——
        /// 以前这里是每颗子弹每帧 `GameWorld.Instance.Field` 取一遍（上千颗就是上千次属性 + 空判断）。</param>
        public bool Step(float dt, bool allowPaint, Battlefield field)
        {
            // ★★★ 第 103 轮（用户纠正）：「子弹推力**不是通过提速来的**，速度不变」——
            //   所以这里**不再**给子弹加任何加速；推力改成命中大球/中子星时
            //   传给对方的**冲量**（见 `HitBigBall` 和 `Cfg.BulletPushSpeedPerValue`）。

            // 位置自己积分（没有刚体了）
            PrevPos = Pos;
            Pos += Vel * dt;

            // 合批拖尾：把这一帧的位置记进历史（在「可能返回 false」之前记，
            // 这样数值刚好耗尽的那一帧尾巴也是完整的）。
            if (!Cfg.BulletTrailUsesTrailRenderer) RecordTrailPoint();

            // 速度上限：别让引力把子弹加速到失控
            float sp = Vel.magnitude;
            if (sp > Cfg.BulletMaxSpeed) { Vel *= Cfg.BulletMaxSpeed / sp; sp = Cfg.BulletMaxSpeed; }
            if (sp > 1e-5f) _dir = Vel / sp;

            // ★ 刷地：**格子检测**模型 —— 把这一帧从 PrevPos 走到 Pos 的那条线段逐格标色。
            //   不是「盖印章」：印章之间会留缝（用户看到的斑点），线段是连着的一条。
            //
            // ★★ 预筛（性能，见 F1 的 `pool` 一栏）：**两端都已经在自家领土上**的子弹
            //    直接跳过整条线段。飞在自家领土上的子弹刷出来的全是「已经是自己的格子」，
            //   一个都不要改，却要走完「逐格 × 3×3」的全部开销 ——
            //   场上九千颗子弹时这一条就是十几毫秒。
            //   （代价只有贴着自己边界飞的那一两格描边，可以忽略。）
            if (allowPaint && field != null && Owner != null && _paintBudget > 0d
                && !(field.OwnedBy(PrevPos, Owner.Index) && field.OwnedBy(Pos, Owner.Index)))
            {
                // ★★ 账要平：**先算「还买得起几格」，再让地图只涂这么多格**。
                //   以前是先涂完整条线、再拿涂出来的格数扣钱 —— 但格子在扣之前已经写进地图了：
                //   一颗带 10 点数值的子弹一帧能涂五六十格（几百点数值），多出来的全白送。
                //   这就是「60 万数值就把整张图刷满」（本该 8388608）的根因。
                // ★★ 同一个坑：直接 `(int)` 超范围会变负数（Mono 给 int.MinValue）——
                //   数值大的子弹会被判成「一格都买不起」→ 原地冻住。统一走 Cfg.PaintBudget。
                int afford = Cfg.PaintBudget(_paintBudget);
                if (afford <= 0)
                {
                    // ★ 这一帧它一格都买不起 —— **别让它再往前走**：
                    //   否则子弹的头会停在「最后涂出来的那格」的前面，看着像「死在自己领土外面」。
                    Pos = PrevPos;
                    return false;
                }

                float reachT;
                int painted = field.PaintLine(PrevPos, Pos, Cfg.BulletPaintCells,
                                              Owner.Index, afford, out reachT);
                if (reachT < 1f)
                {
                    // ★ 额度只够涂到线段中间 —— 把子弹**收回**到那里。
                    //   不然它会停在「涂出来的领土」前面，看起来像「死了还往前冲一段」。
                    //   （拖尾的头是刚记下的那个点，得一起改，不然尾巴还留在前面。）
                    Pos = Vector2.Lerp(PrevPos, Pos, reachT);
                    if (!Cfg.BulletTrailUsesTrailRenderer && TrailCount > 0) TrailPts[0] = Pos;
                }
                if (painted > 0)
                {
                    double cost = painted * Cfg.PixelCaptureCost;
                    _paintBudget -= cost;
                    // 规格：涂地要花掉**阵营自己的子弹数**
                    if (Cfg.PaintChargesBulletStock)
                        Owner.Bullets = System.Math.Max(Owner.Bullets - cost, 0d);
                }
                if (_paintBudget <= 0d) return false;      // 数值耗尽 → 消失
            }

            // 规格：碰到地图边界要反弹，不是直接飞出去
            float m = Cfg.MapWorldSize;
            bool hit = false;
            Vector2 n = Vector2.zero;
            if (Pos.x < 0f) { Pos.x = 0f; n = Vector2.right; hit = true; }
            else if (Pos.x > m) { Pos.x = m; n = Vector2.left; hit = true; }
            if (Pos.y < 0f) { Pos.y = 0f; n = Vector2.up; hit = true; }
            else if (Pos.y > m) { Pos.y = m; n = Vector2.down; hit = true; }
            if (hit) Reflect(n);
            if (!InUse) return false;

            // 规格：**数值没耗尽就不消失**。Life 只是可选安全网（0 = 不限）。
            if (Cfg.BulletLifetime > 0f)
            {
                Life -= dt;
                if (Life <= 0f) return false;
            }
            return _paintBudget > 0d;
        }

        /// <summary>把 transform 挪到**插值后**的位置（渲染用）。
        /// 拖尾是跟随 transform 的，所以这个必须在每帧的 LateUpdate 里调，不能只在物理帧调 ——
        /// 只在物理帧调的话，拖尾的采样点也是 60Hz 的台阶，看起来就是拖尾一跳一跳。</summary>
        public void SyncTransform(float alpha)
        {
            transform.position = RenderedPos(alpha);
        }

        /// <summary>
        /// 子弹撞**敌方护盾**（规格）：
        ///   · 子弹数值 &gt; 护盾  → 护盾归零；子弹扣掉护盾那么多值，并**反弹**回去
        ///   · 子弹数值 ≤ 护盾  → 子弹**全部数值都没了**；护盾扣掉相应的量
        /// </summary>
        public void ResolveEnemyShield(Turret turret, float shieldR, float penet)
        {
            if (turret == null || turret.Owner == null) return;

            // ★ 羽化：护盾最外 ShieldFeatherBand 那一段不是硬边。
            float f = FeatherFactor(shieldR, penet);
            if (f <= 0f) return;                       // 完全在最外沿之外，等于没碰到

            // ★ 用 **RemainingValue**（这颗子弹**还剩**的数值），不是 `Damage`（发射时的原始值）。
            //   `Damage` 不会被刷地扣减，拿它算的话：一颗已经把数值涂掉大半的子弹，
            //   撞护盾时仍然按满值结算 —— 那就是唯一一条还在用「老值」的路径，属于漏改。
            //   现在护盾 / 大球 / 子弹打子弹三处统一用剩余值。
            double mine = RemainingValue;
            if (mine <= 0d) return;

            double s = turret.Owner.Shield;
            double hit = mine * f;                     // 打在护盾上的**有效**数值
            if (hit <= 0d) return;

            if (hit > s)
            {
                turret.Owner.Shield = 0d;
                double left = mine - s;
                _paintBudget = left;
                Damage = left;
                Vector2 d = Pos - turret.WorldPosition;
                if (d.sqrMagnitude < 1e-4f) d = -_dir;
                Reflect(d.normalized);
            }
            else
            {
                turret.Owner.Shield = System.Math.Max(s - hit, 0d);
                // ★ 羽化：只被吃掉 hit 那么多，**剩下的自己带着继续飞**
                //   （硬边的版本是「碰到就全没」，那才是「纹理有羽化、真护盾没有」）
                double left = mine - hit;
                _paintBudget = left;
                Damage = left;
                if (_paintBudget <= 1e-9d) Release();
            }
        }

        /// <summary>
        /// 护盾羽化系数：内侧（1 - ShieldFeatherBand 以内）恒为 1，
        /// 往外线性降到最外沿的 <see cref="Cfg.ShieldFeatherMin"/>。
        /// 视觉上的羽化和这里的判定用的是**同一个带宽**。
        /// </summary>
        public static float FeatherFactor(float shieldR, float penet)
        {
            if (shieldR <= 1e-4f) return 1f;
            float inner = shieldR * (1f - Cfg.ShieldFeatherBand);
            if (penet <= inner) return 1f;
            float u = Mathf.Clamp01(Mathf.InverseLerp(inner, shieldR, penet));
            return Mathf.Lerp(1f, Cfg.ShieldFeatherMin, u);
        }

        /// <summary>
        /// 和另一颗**敌方**子弹撞上。规则**和撞护盾一样**（用户规格）：
        ///   · 我的数值 &gt; 对方的 → 对方消失；我扣掉对方那么多值，并**反弹**；
        ///   · 我的数值 ≤ 对方的 → 我消失；对方扣掉我这么多值。
        /// 同阵营子弹之间不判定（不打自己人）。
        /// </summary>
        public void HitEnemyBullet(Bullet other)
        {
            if (other == null || other == this) return;
            if (!InUse || !other.InUse) return;
            if (other.Owner == Owner) return;

            double mine = RemainingValue;
            double theirs = other.RemainingValue;
            if (mine <= 0d || theirs <= 0d) return;

            if (mine > theirs)
            {
                other.Release();
                double left = mine - theirs;
                _paintBudget = left;
                Damage = left;
                if (_paintBudget <= 1e-9d) { Release(); return; }
                Vector2 n = Pos - other.Pos;
                if (n.sqrMagnitude < 1e-8f) n = -_dir;
                Reflect(n.normalized);
            }
            else
            {
                double left = theirs - mine;
                other._paintBudget = left;
                other.Damage = left;
                if (other._paintBudget <= 1e-9d) other.Release();
                Release();
            }
        }

        /// <summary>原路反射，速度大小不变。</summary>
        public void Reflect(Vector2 normal)
        {
            _bounces++;
            if (_bounces > Cfg.BulletMaxBounces) { Release(); return; }

            Vector2 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector2.up;
            Vector2 r = Vector2.Reflect(_dir, n);
            if (r.sqrMagnitude < 0.01f) r = -_dir;
            _dir = r.normalized;
            Vel = _dir * Vel.magnitude;
        }

        /// <summary>
        /// 打到大球：
        ///   己方 &lt;100M → 把数值全部喂给球；
        ///   己方 ≥100M（引力球 / 中子星）→ **30% 吸收、70% 弹开**（第 92 轮用户改的）；
        ///   敌方       → 扣球这么多数值；
        /// 子弹自己消失，除非是「弹开」那一条。
        /// </summary>
        public void HitBigBall(BigBall big)
        {
            if (big == null || big.Consumed) return;

            // ★★ 第 112 轮（用户）：**吸血只在「大球 ↔ 大球」的碰撞里生效，子弹没有**。
            //   这里刻意**完全不读** `big.IsVampire`：子弹打吸血球，和打普通大球一模一样 ——
            //   己方 = 并数值 / 引力球掷骰，敌方 = 扣数值。
            //   吸血的唯一入口是 `BigBall.OnCollisionEnter2D → ResolveCrossFaction`
            //   （那边有 `GetComponentInParent<BigBall>()` 守卫，子弹根本进不来）。
            //   别把吸血搬到这里，也别给子弹加 IsVampire 之类的标记 —— 自检里有一条断言守着。
            //
            // ★★★ 第 103 轮（用户）：
            //   ① 子弹推力**由子弹自身的数值决定**（「根据自身数值决定的」）；
            //   ② **己方子弹打到己方引力球时不提供推力**。
            //   子弹自己的速度一个字都不改；这里把冲量传给**球**：
            //   沿飞行方向、按球的质量缩放 ⇒ 球的速度增量 = 系数 × 子弹数值。
            bool ownGravityBall = big.Owner == Owner && big.HasGravity;
            if (Cfg.BulletPushSpeedPerValue > 0f && big.Body != null && !ownGravityBall)
            {
                Vector2 pd = Vel.sqrMagnitude > 1e-8f ? Vel.normalized : _dir;
                // ★★★ 用户第 103 轮：「推力也与**被推的大球数值**有关。数值越大越难被推动。」
                //   系数 = 2×参考值 / (球数值 + 参考值)：
                //     球 = 参考值（1M）→ 1；更小的球最多 2 倍；远大于 1M 的球趋近 0。
                double bv = System.Math.Max(big.Value, 1d);
                double refV = Cfg.BulletPushReferenceValue;
                float sizeFactor = refV > 0d ? (float)(2d * refV / (bv + refV)) : 1f;
                float dv = Cfg.BulletPushSpeedPerValue * (float)RemainingValue * sizeFactor;
                big.Body.AddForce(pd * (dv * big.Body.mass), ForceMode2D.Impulse);
            }

            double v = RemainingValue;
            if (big.Owner == Owner)
            {
                if (big.Value < Cfg.GravityBallThreshold)
                {
                    big.Value += v;
                    Release();
                }
                else
                {
                    // ★★ 第 92 轮用户：「引力球（大球和中子星）现在改为 30% 概率吸收子弹、
                    //   70% 概率反弹」。所以这一分支**不再是必反弹** —— 先掷骰子。
                    //   吸收 = 数值并进大球、子弹当场消失（和上面小大球那条一样）。
                    if (Random.value < Cfg.GravityBallAbsorbChance)
                    {
                        big.Value += v;
                        Release();
                    }
                    else
                    {
                        Vector2 n = (big.transform.position - (Vector3)Pos);
                        Reflect(((Vector2)n).normalized);
                        if (InUse)
                        {
                            float sp = Vel.magnitude * Cfg.BulletGravityBallBounceBoost;
                            Vel = _dir * Mathf.Min(sp, Cfg.BulletMaxSpeed);
                        }
                    }
                }
            }
            else
            {
                big.Drain(v);
                Release();
            }
        }

        /// <summary>还能在场上留多久（只在「数值耗尽」这种自然消失时用来放拖尾）。</summary>
        public float FadeTimer;

        /// <summary>这颗子弹携带的全部数值都转给目标了 → 自己消失。</summary>
        public void Release()
        {
            if (!InUse && FadeTimer > 0f) return;
            InUse = false;
            if (_trail != null) _trail.emitting = false;   // 别再往上加点
            FadeTimer = Cfg.BulletTrailFadeOut;            // ★ 拖尾**逐渐缩短到没有**，不是啪一下没了
        }

        public double RemainingValue { get { return _paintBudget > 0d ? _paintBudget : 0d; } }
    }
}
