using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// **浮力区**：从固定阻挡球那一排一直通到 x16 下方的一条**泡泡通道**。
    ///
    /// ★★ 规格（用户，第 80 轮）：
    ///   「泡泡改大、向上速度增大，而且**不要再**用『进了浮力区就施力』，
    ///     改成**泡泡自己就是有碰撞、有一定密度的物体**」
    ///   —— 所以这里**没有**任何「看不见的盒子在推球」。通道里飘的是一颗颗**真泡泡**
    ///   （Dynamic 刚体 + 圆形碰撞体 + 质量，重力为 0），它们自己往上跑，
    ///   增倍球是被它们**顶**上去的。
    ///
    /// ★★ 第 107 轮（用户）：「生成逻辑改成**每帧生成 x 个**，而不是固定的数量」——
    ///   以前是**固定一池**（`BuoyancyBubbleCount` 颗常驻）：升到顶 / 活满寿命就**瞬移回底部**，
    ///   通道里的数量永远是那个常数。
    ///   现在改成**流水线**：
    ///     · 每个**物理帧**（`FixedUpdate`，默认 50Hz）从底部生成 `BuoyancyBubbleSpawnPerFrame` 颗；
    ///     · 升到顶 / 活满 `BuoyancyBubbleLife` 就**消失**（收回对象池，不销毁、零 GC）；
    ///     · 通道里的数量 = 生成速率 × 寿命，自然浮动，只受 `BuoyancyBubbleMax` 这个硬上限约束。
    ///   开局仍然把通道**预热**到稳态数量（否则前 3 秒只有底部几颗，看着像坏了）。
    /// </summary>
    public class BuoyancyZone : MonoBehaviour
    {
        /// <summary>
        /// 一颗泡泡。**池化**：建过就一直在 `_free` / `_active` 之间来回，不 new 不 Destroy
        /// （每秒生成上百颗，真销毁的话 GC 和物理重建都受不了）。
        /// </summary>
        class Bubble
        {
            public Transform Tr;
            public Rigidbody2D Rb;
            public CircleCollider2D Col;
            /// <summary>已经活了多久 —— 到 `BuoyancyBubbleLife` 就消失。</summary>
            public float Age;
            // ★★ 第 126 轮：删掉了「自己维护的向上速度 `Vy`」。
            //   原来的写法每帧 `Vy += accel·dt` 再把刚体速度覆盖成 `(vx, Vy)`，
            //   于是**碰撞造成的纵向速度变化被整帧丢掉** —— 大球压住泡泡时它本该被压慢/压下去，
            //   下一帧又被累加器拉回原速度，看起来像根本没被挡住。
            //   现在直接读 `Rb.velocity.y` 再往上加，天然从「碰撞后的速度」继续累计。
            /// <summary>出生那一刻压着的动态球碰撞体（分开后恢复碰撞，见 `RefreshIgnoredColliders`）。</summary>
            public readonly List<Collider2D> Ignoring = new List<Collider2D>(1);
        }

        public float Width { get; private set; }
        public float Height { get; private set; }
        /// <summary>通道底面 / 顶面的**世界** y。</summary>
        public float BottomY { get; private set; }
        public float TopY { get; private set; }
        /// <summary>**当前活着**的泡泡（自检和调试用）。数量会随生成 / 消失上下浮动。</summary>
        public readonly List<Transform> Bubbles = new List<Transform>();
        /// <summary>累计生成过多少颗（自检 / 调试用：它只增不减，能直接看出「每帧生成 x 颗」）。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>活着的泡泡（和 <see cref="Bubbles"/> 一一对应）。</summary>
        readonly List<Bubble> _active = new List<Bubble>();
        /// <summary>活着的泡泡的刚体（自检按它看位置 / 速度）。</summary>
        readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>();
        /// <summary>池里已经建好、暂时没用的泡泡。</summary>
        readonly Stack<Bubble> _free = new Stack<Bubble>();
        Transform _root;
        /// <summary>建过多少颗（只用来给 GameObject 起个不重名的名字，方便调试）。</summary>
        int _created;
        /// <summary>生成速率的小数部分（`SpawnPerFrame` 可以写 2.5，攒够一颗才生成）。</summary>
        float _spawnCarry;

        public static BuoyancyZone Create(Transform parent)
        {
            float bottom = Cfg.BuoyancyBottomY;
            float top = Mathf.Max(Cfg.BuoyancyTopY, bottom + 1f);

            var go = new GameObject("BuoyancyZone");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, (bottom + top) * 0.5f, 0f);

            var bz = go.AddComponent<BuoyancyZone>();
            bz.Width = Cfg.BuoyancyHalfWidth * 2f;
            bz.Height = top - bottom;
            bz.BottomY = bottom;
            bz.TopY = top;

            bz._root = new GameObject("Bubbles").transform;
            bz._root.SetParent(go.transform, false);
            bz.Prewarm();
            return bz;
        }

        /// <summary>
        /// 通道的**高度**（米）—— 和 <see cref="Create"/> 里算的是同一个式子。
        /// 做成静态是为了让 <see cref="TransitTime"/> / <see cref="SteadyCount"/> 不依赖实例（自检要用）。
        /// </summary>
        public static float CorridorHeight
        {
            get
            {
                float bottom = Cfg.BuoyancyBottomY;
                float top = Mathf.Max(Cfg.BuoyancyTopY, bottom + 1f);
                return top - bottom;
            }
        }

        /// <summary>
        /// 一颗泡泡**不被球挡**时从底走到顶要多久（秒）。
        /// 运动模型：出生速度 `RiseSpeed` 起，之后受向上加速度 `RiseAccel`
        /// 和线性阻尼 `Drag` 共同作用 —— `dv/dt = a − k·v`，
        /// 终端速度 = `a / k`（第 127 轮起 a = 20、k = 2.7 ⇒ ≈7.4 m/s）。
        /// `RiseSpeedMax` 填了正数才走「加速 + 匀速」两段式（默认 0 = 不封顶）。
        /// 寿命更短的话按寿命算（到点就消失）。
        /// </summary>
        public static float TransitTime
        {
            get
            {
                float d = Mathf.Max(CorridorHeight, 0.01f);
                float v0 = Mathf.Max(Cfg.BuoyancyBubbleRiseSpeed, 0.01f);
                float a = Mathf.Max(Cfg.BuoyancyBubbleRiseAccel, 0f);
                float k = Mathf.Max(Cfg.BuoyancyBubbleDrag, 0f);
                float vmax = Cfg.BuoyancyBubbleRiseSpeedMax;

                float t;
                if (a <= 0f) t = d / v0;                       // 恒速
                else if (vmax > v0)
                {
                    // 有硬上限：加速段按「无阻尼」近似（上限路径只在调试时用）
                    float tAcc = (vmax - v0) / a;
                    float sAcc = v0 * tAcc + 0.5f * a * tAcc * tAcc;
                    t = sAcc >= d
                        ? SolveRise(d, v0, a, k)
                        : tAcc + (d - sAcc) / vmax;
                }
                else t = SolveRise(d, v0, a, k);               // 不封顶（默认）

                return Mathf.Min(t, Mathf.Max(Cfg.BuoyancyBubbleLife, 0.1f));
            }
        }

        /// <summary>
        /// 解 `dv/dt = a − k·v`（v(0)=v0）走到距离 `d` 需要多久。
        /// 解析式：`s(t) = V·t + (v0−V)(1−e^{−k·t})/k`，其中 `V = a/k`；
        /// 用二分求根（只在预热 / 自检时调用，几十次迭代的开销可以忽略）。
        /// `k = 0` 时退化成匀加速：`t = (−v0 + √(v0²+2ad)) / a`。
        /// </summary>
        static float SolveRise(float d, float v0, float a, float k)
        {
            if (a <= 0f) return d / Mathf.Max(v0, 0.01f);
            if (k <= 1e-4f) return (-v0 + Mathf.Sqrt(v0 * v0 + 2f * a * d)) / a;

            float V = a / k;
            float lo = 0f, hi = Mathf.Max(d / Mathf.Max(v0, 0.01f), 1f) * 4f;
            for (int i = 0; i < 48; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float s = V * mid + (v0 - V) * (1f - Mathf.Exp(-k * mid)) / k;
                if (s < d) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        /// <summary>
        /// 稳态时通道里大约有多少颗 = **每秒生成数 × 一颗泡泡在通道里待的时间**。
        /// ★ 只是估算（被球挡一下会待得更久、实际会上下浮动几颗）；
        ///   预热和自检都读它，别在两处各算一遍。
        /// </summary>
        public static float SteadyCount
        {
            get
            {
                float dt = Mathf.Max(Time.fixedDeltaTime, 0.001f);
                return Cfg.BuoyancyBubbleSpawnPerFrame / dt * TransitTime;
            }
        }

        /// <summary>
        /// 开局把通道**铺满**到稳态数量：位置沿通道随机、年龄随机（像是一直在冒）。
        /// ★ 这一步只是初始化 —— 之后的数量完全由「每帧生成 / 到顶消失」决定。
        /// </summary>
        void Prewarm()
        {
            int want = Mathf.Clamp(Mathf.CeilToInt(SteadyCount), 0, Cfg.BuoyancyBubbleMax);
            float life = Mathf.Max(Cfg.BuoyancyBubbleLife, 0.1f);
            float spawnY = BottomY + Cfg.BuoyancyBubbleSpawnLift + Cfg.BuoyancyBubbleSpawnRaise;
            for (int i = 0; i < want; i++)
            {
                var b = SpawnOne();
                if (b == null) break;
                float y = Mathf.Lerp(spawnY, TopY, Random.value);
                b.Tr.position = new Vector3(transform.position.x + EmitLineOffset(), y, 0f);
                b.Rb.position = b.Tr.position;
                // 起始寿命错开，免得整批泡泡同时消失
                b.Age = Random.Range(0f, life);
                // 位置改了 -> 重新看一眼「是不是压着球」（出生点那次的记录作废）
                RefreshIgnoredColliders(b);
            }
        }

        /// <summary>从池里取一颗（没有就新建），放到源头、给初速度，挂进活跃表。</summary>
        Bubble SpawnOne()
        {
            if (_active.Count >= Cfg.BuoyancyBubbleMax) return null;

            Bubble b = _free.Count > 0 ? _free.Pop() : NewBubble();
            if (b == null) return null;

            b.Age = 0f;
            // ★ 半径每颗随机（池子复用同一颗时也重掷一次，别让一颗泡泡永远是同一个大小）
            float r = Random.Range(Cfg.BuoyancyBubbleRadiusMin, Cfg.BuoyancyBubbleRadiusMax);
            b.Tr.localScale = Vector3.one * (r * 2f);

            Vector2 p = new Vector2(transform.position.x + EmitLineOffset(),
                                    BottomY + Cfg.BuoyancyBubbleSpawnLift + Cfg.BuoyancyBubbleSpawnRaise);
            b.Tr.gameObject.SetActive(true);
            b.Tr.position = p;
            b.Rb.position = p;

            var v0 = EmitVelocity();
            b.Rb.velocity = v0;       // ★ 出生速度；之后每步都从**刚体当前速度**继续累加
            RefreshIgnoredColliders(b);

            _active.Add(b);
            Bubbles.Add(b.Tr);
            _bodies.Add(b.Rb);
            SpawnedCount++;
            return b;
        }

        /// <summary>建一颗**池对象**（不激活）。半径 / 位置由每次 <see cref="SpawnOne"/> 重设。</summary>
        Bubble NewBubble()
        {
            var b = new Bubble();
            var go = new GameObject("Bubble" + _created++);
            go.layer = Cfg.BubbleLayer;          // ★ 独立层：泡泡之间互不碰撞（省掉几十万对）
            b.Tr = go.transform;
            b.Tr.SetParent(_root, false);

            // 灰白、半透明：一眼能看出是「泡泡」而不是阻挡球
            var sr = Primitives.MakeNode("Body", Primitives.Circle,
                Cfg.BuoyancyBubbleColor, 9, b.Tr);
            sr.transform.localScale = Vector3.one;

            b.Col = go.AddComponent<CircleCollider2D>();
            b.Col.radius = 0.5f;                     // 外面那层 scale 才是真半径
            b.Col.sharedMaterial = Physics2DConfig.Slippery;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;                    // 浮力区里没有重力，只有上浮
            rb.drag = Cfg.BuoyancyBubbleDrag;        // ★ 第 126 轮起纵向也会被它收（见 Cfg 注释）
            rb.angularDrag = 0.5f;
            rb.mass = Cfg.BuoyancyBubbleMass;        // ★「一定密度」
            // ★ 终极优化：泡泡用 **Discrete** 就够 —— 它们 4 m/s、一物理帧走 0.08m，
            //   比自身半径（0.10~0.22）还小，不会穿模；而 Continuous(CCD) 是逐颗做
            //   连续扫掠，几百颗一起开是白白烧 CPU。
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            b.Rb = rb;

            go.SetActive(false);                     // 先躺着，等 SpawnOne 激活
            return b;
        }

        /// <summary>这个点是不是在浮力区里（世界坐标）—— 只看范围，和推力无关了。</summary>
        public bool Contains(Vector2 worldPos)
        {
            Vector2 c = transform.position;
            return Mathf.Abs(worldPos.x - c.x) <= Width * 0.5f
                && Mathf.Abs(worldPos.y - c.y) <= Height * 0.5f;
        }

        /// <summary>
        /// 重生时选哪一道源头（x 偏移）：−1 / 0 / +1 × `PegRowSpacing`。
        /// ★ 必须**随机**：固定从最左那道开始试（`for (int e = -1; ...)`）就是之前
        ///   「只有第一处生成」那个 bug 的根因。
        /// </summary>
        static float EmitLineOffset()
        {
            return (Random.Range(0, 3) - 1) * Cfg.PegRowSpacing;
        }

        /// <summary>
        /// 出发速度 = **竖直 ±`BuoyancyBubbleSpreadDeg` 的角度扰动**
        ///          + **一个独立的随机横向初速度 `BuoyancyBubbleSideSpeed`**。
        ///
        /// ★★ 用户：「再给一点左右的初速度」——
        ///   原来只有角度扰动（±15°），横向分量最大也就 `RiseSpeed × sin15° ≈ 1.0 m/s`，
        ///   泡泡几乎还是笔直往上。现在额外叠一个 ±SideSpeed 的横向速度，
        ///   出生那一刻就左右散开，配合 `BuoyancyBubbleLateralBand` 的软推回，
        ///   能迅速铺满整条通道的宽度。
        ///
        /// ★ `SideSpeed` 加在**速度**上而不是角度上：角度扰动会把纵向速度也带上一起缩，
        ///   而用户要的只是「多给点左右」，纵向不该跟着变。
        /// </summary>
        static Vector2 EmitVelocity()
        {
            float a = Random.Range(-Cfg.BuoyancyBubbleSpreadDeg, Cfg.BuoyancyBubbleSpreadDeg)
                      * Mathf.Deg2Rad;
            Vector2 v = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Cfg.BuoyancyBubbleRiseSpeed;
            // ★ 额外的横向初速度（正负随机）
            v.x += Random.Range(-Cfg.BuoyancyBubbleSideSpeed, Cfg.BuoyancyBubbleSideSpeed);
            return v;
        }

        void FixedUpdate()
        {
            SpawnDue();

            float dt = Time.fixedDeltaTime;
            float life = Mathf.Max(Cfg.BuoyancyBubbleLife, 0.1f);
            float halfW = Width * 0.5f;

            // 倒着走：消失时用「最后一个补到当前位置」删，倒序不会漏掉任何一颗
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var b = _active[i];
                b.Age += dt;
                Vector2 p = b.Rb.position;

                // ★ 规格（用户）：「泡泡过 3 秒就消失」——
                //   现在升到顶 / 活满寿命就**真的消失**（收回池子），
                //   通道里的数量靠「每帧生成」补回来。
                if (p.y >= TopY || b.Age >= life)
                {
                    Despawn(i);
                    continue;
                }

                // ★★ 第 126 轮（用户）：「气泡被大球挡住之后速度应该从碰撞后的速度开始累计」——
                //   所以这里**读刚体当前速度**（= 上一步物理 + 碰撞之后的结果）再加加速度，
                //   不再用自己维护的累加器。球压住泡泡时泡泡真的会被压慢/压下去，
                //   分开之后就从那个被压到的速度重新往上加，而不是立刻跳回原来的速度。
                Vector2 v = b.Rb.velocity;
                float vx = v.x * Cfg.BuoyancyBubbleSideDamp;

                // ★ 上浮：原文 `vel = vel + [0, 0.1]`（每 Algodoo 步）⇒ 每秒 +3 m/s²。
                //   上限只有 `RiseSpeedMax > 0` 时才夹（默认 0 = 不封顶）。
                float vy = v.y + Cfg.BuoyancyBubbleRiseAccel * dt;
                if (Cfg.BuoyancyBubbleRiseSpeedMax > 0f)
                {
                    float cap = Mathf.Max(Cfg.BuoyancyBubbleRiseSpeedMax,
                                          Cfg.BuoyancyBubbleRiseSpeed);
                    vy = Mathf.Min(vy, cap);
                }

                // ── 横向边界 ──
                // ★★ 第 108 轮（用户）：「气泡超界就删除回收，不再反弹」——
                //   以前飘到通道左右边缘会把横向速度**取反**（掉头继续飘），
                //   现在直接**消失**（收回对象池），缺的由「每帧生成」补上。
                //   ★ 判断必须在夹位置之前：先夹到 lim 就永远判不到「超界」了。
                float off = p.x - transform.position.x;
                float lim = halfW - Cfg.BuoyancyBubbleRadiusMax;      // 物理边界（不越出通道）
                if (off > lim || off < -lim)
                {
                    Despawn(i);
                    continue;
                }

                // ── 横向：照抄 Algodoo poststep 的阈值判断 ──
                //   原文：pos(0) > -6 → vel -= [0.8,0]；pos(0) < -8 → vel += [0.8,0]；带内不动。
                //   即「死带之外每步给 0.8 把它推回带内」——**硬阈值**，不是按距离加权的软推回。
                //   按 30 步/秒换算成 `vx ∓ PushBack × dt`（PushBack = 0.8 × 30 = 24 m/s²）。
                //   ★ 死带宽度 = `BuoyancyHalfWidth × 0.75`（原版那 2m 是相对它自己的窄通道）。
                float band = Mathf.Clamp(Cfg.BuoyancyBubbleLateralBand, 0f, lim);
                if (off > band)      vx -= Cfg.BuoyancyBubblePushBack * dt;
                else if (off < -band) vx += Cfg.BuoyancyBubblePushBack * dt;

                p.x = transform.position.x + off;

                b.Rb.position = p;
                b.Rb.velocity = new Vector2(vx, vy);

                // ★ 临时忽略的那些球：已经不再压着就恢复碰撞
                HealIgnoredColliders(b);
            }
        }

        /// <summary>
        /// 每帧按 `BuoyancyBubbleSpawnPerFrame` 生成（可以带小数：攒够一颗才生成）。
        /// ★ 用 `FixedUpdate`（物理帧）而不是渲染帧：不然 60Hz / 144Hz 屏幕上密度会差一倍多。
        /// </summary>
        void SpawnDue()
        {
            float rate = Cfg.BuoyancyBubbleSpawnPerFrame;
            if (rate <= 0f) return;

            _spawnCarry += rate;
            int n = Mathf.FloorToInt(_spawnCarry);
            if (n <= 0) return;
            _spawnCarry -= n;

            for (int k = 0; k < n; k++)
                if (SpawnOne() == null) break;   // 到 `BuoyancyBubbleMax` 了，这一帧剩下的不生成
        }

        /// <summary>这颗泡泡到顶 / 到寿命：收回池子（不销毁），并从三个活跃表里同步删掉。</summary>
        void Despawn(int i)
        {
            var b = _active[i];
            ClearIgnored(b);                 // ★ 先把「临时忽略」的关系还回去，再关掉它
            b.Age = 0f;
            b.Rb.velocity = Vector2.zero;
            b.Rb.angularVelocity = 0f;
            b.Tr.gameObject.SetActive(false);

            // 三个表一一对应：都拿最后一个补到 i，再砍掉尾巴（倒序遍历时不会漏）
            int last = _active.Count - 1;
            _active[i] = _active[last];
            _active.RemoveAt(last);
            _bodies[i] = _bodies[last];
            _bodies.RemoveAt(last);
            Bubbles[i] = Bubbles[last];
            Bubbles.RemoveAt(last);

            _free.Push(b);
        }

        // ─────────────────────────────────────────────────────────────
        // 泡泡出生时的「临时穿模」
        //
        // ★★ 用户：「能不能让气泡重生的时候如果碰到球就先关掉碰撞层，离开后恢复」
        //   思路完全正确 —— 但**不要动层**（`Physics2D.IgnoreLayerCollision` 是全局的，
        //   一改所有泡泡对所有球都失去碰撞，球会直接从泡泡堆里穿过去掉下来）。
        //   正确做法是 `Physics2D.IgnoreCollision(colA, colB, true)`：**只对着一对碰撞体**生效，
        //   其余泡泡和那颗球照常碰撞。
        //
        //   生命周期：出生那一刻把「压着的球」记进 `b.Ignoring` → 每帧检查是否还重叠 →
        //   一旦分开就 `IgnoreCollision(..., false)` 恢复。泡泡消失时也会先清空。
        // ─────────────────────────────────────────────────────────────

        /// <summary>出生那一刻：把当前压着的动态球全部设为「忽略碰撞」。</summary>
        void RefreshIgnoredColliders(Bubble b)
        {
            ClearIgnored(b);
            if (!Cfg.BuoyancyBubbleIgnoreOnRespawn) return;
            var col = b.Col;
            if (col == null) return;

            var hits = Physics2D.OverlapCircleAll(b.Rb.position, col.bounds.extents.x);
            for (int k = 0; k < hits.Length; k++)
            {
                var other = hits[k];
                if (other == null || other == col) continue;
                if (other.gameObject.layer == Cfg.BubbleLayer) continue;   // 泡泡之间本来就不碰
                var orb = other.attachedRigidbody;
                if (orb == null || orb == b.Rb) continue;
                if (orb.bodyType != RigidbodyType2D.Dynamic) continue;     // 只处理会被顶飞的球
                Physics2D.IgnoreCollision(col, other, true);
                b.Ignoring.Add(other);
            }
        }

        /// <summary>每帧检查：不再重叠的条目立刻恢复碰撞。</summary>
        void HealIgnoredColliders(Bubble b)
        {
            var list = b.Ignoring;
            if (list.Count == 0) return;
            var col = b.Col;
            if (col == null) { ClearIgnored(b); return; }

            for (int k = list.Count - 1; k >= 0; k--)
            {
                var other = list[k];
                // other 被销毁 / 自己被销毁 → 直接移除（IgnoreCollision 不用再调，对象都没了）
                if (other == null) { list.RemoveAt(k); continue; }
                // 恢复余量：比「刚好分开」再多留一点点，避免边界上反复开关
                float clear = Cfg.BuoyancyBubbleIgnoreMargin;
                if (!col.bounds.Intersects(Expand(other.bounds, clear)))
                {
                    Physics2D.IgnoreCollision(col, other, false);
                    list.RemoveAt(k);
                }
            }
        }

        /// <summary>把所有「忽略中」的关系恢复掉（泡泡消失前调用）。</summary>
        void ClearIgnored(Bubble b)
        {
            var list = b.Ignoring;
            if (list.Count == 0) return;
            var col = b.Col;
            for (int k = 0; k < list.Count; k++)
            {
                if (col == null || list[k] == null) continue;
                Physics2D.IgnoreCollision(col, list[k], false);
            }
            list.Clear();
        }

        /// <summary>把 bounds 向外扩 <paramref name="m"/> 米（恢复余量用）。</summary>
        static Bounds Expand(Bounds b, float m)
        {
            b.Expand(m * 2f);
            return b;
        }
    }
}
