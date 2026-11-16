using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 子弹对象池 + **均匀网格**（就是「八叉树」在 2D 里的对应物）。
    ///
    /// ★ 规格：**不要子弹数量上限**。所以要能把「子弹的代价」从物理引擎里彻底拿出来：
    ///   · 子弹不再是刚体/碰撞体 —— 位置自己积分，物理求解器完全不参与；
    ///   · 子弹之间的碰撞本来就不需要；
    ///   · 子弹和「少数几个大东西」（4 个炮台 + 若干大球）的碰撞，
    ///     由**网格**来查：先把所有子弹塞进格子，再让炮台/大球去查自己附近那几格。
    ///     复杂度是 O(子弹数 + 需要查的物体数)，而不是 O(子弹 × 物体)。
    ///
    /// 这样子弹上万也只是「一万次加法 + 一万个顶点」，不会再卡爆。
    /// </summary>
    public class BulletPool : MonoBehaviour
    {
        public static BulletPool Instance { get; private set; }

        readonly List<Bullet> _active = new List<Bullet>(1024);
        readonly Stack<Bullet> _free = new Stack<Bullet>(1024);
        Transform _root;

        public int ActiveCount { get { return _active.Count; } }
        /// <summary>
        /// 活动子弹列表（**只读**：别在外面 Add/Remove/排序）。
        /// ★ 近防炮要遍历敌方子弹找目标 / 判护盾命中，所以开这一个口子。
        /// </summary>
        public List<Bullet> Active { get { return _active; } }
        public int FreeCount { get { return _free.Count; } }
        public int CreatedCount { get; private set; }
        public int RejectedCount { get; private set; }

        // ── 均匀网格 ──
        const int GridN = 48;
        int[] _head;                 // 每格链表头（-1 空）
        int[] _next;                 // 每个子弹在链表里的下一个（按 _active 下标）
        float _cell, _invCell;

        // ── ★★ 扁平数组（「终极优化」的核心）──────────────────────────────
        // 网格重建 + 子弹配对这两段是**被内存延迟卡住的**，不是被算术卡住的：
        // 它们要对每颗子弹去解引用一个散落在托管堆上的 `Bullet` 对象
        // （Pos / InUse / Owner / 半径 / 数值），每颗都是一次 cache miss，
        // 几万次下来就是十几毫秒。
        //
        // 现在这些量在**推进循环**里顺手抄进平行数组 —— 那一段本来就是顺序访问、
        // 对象已经在 cache 里，几乎不要钱；之后的网格和配对只碰这几条连续内存。
        // 一维数组在 C# 里是**连续**的，所以这一步把随机访存变成了顺序访存。
        Vector2[] _fPos;
        float[] _fVisR;
        double[] _fVal;
        Faction[] _fOwner;
        bool[] _fUse;

        void EnsureFlat()
        {
            int n = _active.Count;
            if (_fPos != null && _fPos.Length >= n) return;
            int cap = Mathf.Max(Mathf.NextPowerOfTwo(n + 256), 1024);
            _fPos = new Vector2[cap];
            _fVisR = new float[cap];
            _fVal = new double[cap];
            _fOwner = new Faction[cap];
            _fUse = new bool[cap];
        }

        /// <summary>把第 i 项从扁平数组里挪走（swap-remove 时和 `_active` 一起换）。</summary>
        void SwapFlat(int i, int last)
        {
            if (i == last || _fPos == null) return;
            _fPos[i] = _fPos[last];
            _fVisR[i] = _fVisR[last];
            _fVal[i] = _fVal[last];
            _fOwner[i] = _fOwner[last];
            _fUse[i] = _fUse[last];
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static BulletPool Build(Transform root)
        {
            var go = new GameObject("BulletPool");
            if (root != null) go.transform.SetParent(root, false);
            var pool = go.AddComponent<BulletPool>();
            pool._root = go.transform;
            Instance = pool;
            pool.InitGrid();
            pool.Prewarm(Cfg.BulletPoolPrewarm);
            return pool;
        }

        void InitGrid()
        {
            _cell = Cfg.MapWorldSize / GridN;
            _invCell = 1f / _cell;
            _head = new int[GridN * GridN];
            _next = new int[1024];

            // ★★ 必须显式填 -1：-1 才代表「链表空」。
            //   `new int[]` 是**全 0** 的，而遍历写的是 `for (int i = _head[c]; i >= 0; i = _next[i])`
            //   —— 0 会被当成「第一颗子弹的下标」，而 `_next[0]` 也是 0，
            //   于是 i 永远是 0、**死循环**（刚运行起来就卡死）。
            //   之前没炸是因为只有 ResolveTurrets / ResolveBigBalls 用网格，
            //   而它们紧跟在 RebuildGrid 之后；新加的近防查询是在 Turret.Update 里调的，
            //   完全可能跑在 BulletPool.Update 之前（组件 Update 顺序不保证），
            //   那时网格还没建过 —— 于是它一上手就是一片全 0。
            Fill(_head, -1);
            Fill(_next, -1);
        }

        static void Fill(int[] a, int v)
        {
            for (int i = 0; i < a.Length; i++) a[i] = v;
        }

        void Prewarm(int count) { Grow(count); }

        /// <summary>
        /// 槽位不够时**一批**建出来。
        /// 一颗子弹要建 2 个 GameObject + 一个 TrailRenderer，一颗一颗建的话
        /// 齐射那一瞬间就是几十次「单颗」的分配抖动 —— 这正是「偶尔跳帧」的来源之一。
        /// </summary>
        void Grow(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _free.Push(Bullet.CreateBlank(_root));
                CreatedCount++;
            }
        }

        /// <summary>规格：没有上限 —— 池里没有就新建。</summary>
        public Bullet Rent(Faction owner, Vector2 pos, Vector2 dir, float speed,
                           double damage, float life, bool heavy)
        {
            // ★ 扩容按**几何**增长（还是至少一个 batch）：池子越大、每次扩得越多、扩得越少。
            //   固定 +128 在子弹上千之后会变成「隔几秒就新建 128 个 GameObject + 重挂 5 条扁平数组」——
            //   那就是一次可听见的卡顿。几何增长把「经常小卡」换成「很久一次稍大」。
            if (_free.Count == 0) Grow(Mathf.Max(Cfg.BulletPoolGrowBatch, CreatedCount / 4));
            Bullet b = _free.Pop();
            // ★★ 终极优化：**不再 `SetActive(true)`**。
            //   合批画法下子弹的 GameObject 上只有一个 Transform + 这个脚本，
            //   既没有 Renderer 也没有 Unity 回调（Update/FixedUpdate 一个都没有，
            //   推进是池子自己调的），所以「激活」不带来任何东西，
            //   却在每次租/还时触发一次 Transform 层级登记 —— 每秒几千次就是白扔的时间。
            //   池里的对象**永远保持 inactive**，谁在用完全由 `InUse` 说了算。
            //
            // ★★★ 但这只对**合批画法**成立！`TrailRenderer` 画法下拖尾是真正的
            //   Renderer：**挂在一个未激活的 GameObject 上就是不渲染** ——
            //   症状是「子弹圆点都在飞，一条拖尾都看不见」。
            //   所以那条路必须把对象激活（激活过一次就一直开着，不在租/还时来回切）。
            if (Cfg.BulletTrailUsesTrailRenderer && !b.gameObject.activeSelf)
                b.gameObject.SetActive(true);
            b.Launch(owner, pos, dir, speed, damage, life, heavy);
            _active.Add(b);
            EnsureNextCapacity();
            EnsureFlat();
            int ai = _active.Count - 1;
            _fPos[ai] = b.Pos; _fVisR[ai] = b.VisualRadius;
            _fVal[ai] = b.RemainingValue; _fOwner[ai] = b.Owner; _fUse[ai] = true;
            return b;
        }

        /// <summary>
        /// 保证 `_next` 至少能按 `_active` 的下标索引。
        ///
        /// ★ 必须**每个用到 `_next` 的地方都先调一次**，不能只在 `Rent` 里做：
        ///   `InitGrid()` 会把 `_next` 重新变成 1024 长（它是给「重建网格」用的），
        ///   要是调用它的时候池里已经有一千多发子弹，紧接着的 `RebuildGrid()`
        ///   就会拿 `_active` 的下标去索引一个 1024 长的数组 → **越界抛异常**。
        ///   （这个是自检里真踩到的：自检给池子塞了 800 发之后调 `InitGrid` + `RebuildGrid`。）
        /// </summary>
        void EnsureNextCapacity()
        {
            if (_next != null && _next.Length >= _active.Count) return;
            int n = Mathf.Max(Mathf.NextPowerOfTwo(_active.Count + 64), 1024);
            _next = new int[n];
            Fill(_next, -1);      // 新数组是全 0，不填 -1 就会死循环
        }

        // ─────────────────────────────────────────────────────────────
        // 每物理帧
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ★ 子弹**按渲染帧推进**，不是按物理帧。
        ///
        /// 原来是 `FixedUpdate`（固定 60Hz），而画面是渲染帧率 —— 两者只要不是严格倍频，
        /// 就会出现「这一帧跑了 1 次物理、下一帧跑了 0 次」：
        ///   · 0 次的那一帧，子弹位置根本没动（插值到 alpha=1 也只能停在原地）→ **看着就是停顿一下**；
        ///   · 同一帧里炮台也发不出子弹 → 于是「发几发、停一下、再发几发」。
        /// 用户原话：「子弹密度并不是均匀的，发射 4 发，停顿极短时间，再发 4 发」。
        ///
        /// 子弹本来就不是物理体（位置自己积分、碰撞走自建网格），所以直接搬到渲染帧
        /// 是没有任何副作用的 —— 反过来还省掉了插值那套。
        /// </summary>
        /// <summary>
        /// 子弹这一帧推进多少秒。**所有跟子弹/开火有关的东西都必须用这一个值** ——
        /// 炮弹的发射、子弹的位移、尾流的长度、命中的判定，全是它。
        ///
        /// ★ 这就是「一会儿打一会儿不打」的真因：开火那边原来用的是**没夹过的**
        ///   `Time.deltaTime`，而子弹位移用的是夹到 0.05s 的。一帧超过 50ms（帧率低于 20）时：
        ///   开火按**真实** dt 把一帧的一批沿炮管铺开 `速度 × dt` 那么长，
        ///   而子弹这一帧只走了 `速度 × 0.05` —— 每帧就**空出一段**
        ///   `速度 × (dt − 0.05)` 的缝。帧率越低缝越大，看着就是「打一串、停一下、再打一串」。
        ///   （视频里量到的：白弹圆点间距 ≈0.12m，而正常应该是 0.037m —— 正好差着那一段。）
        /// </summary>
        public static float StepDt(float rawDt)
        {
            return Mathf.Min(rawDt, Cfg.BulletMaxStepTime);
        }

        void Update()
        {
            long _t0 = Prof.Tick();
            // 卡一下的时候别让子弹瞬移穿墙：一帧最多推进这么多秒。
            // ★ 开火那边也要用同一个值（见 Turret.StepFiring），否则每帧都会留缝。
            float dt = StepDt(Time.deltaTime);

            // 合批拖尾的单调时钟（点按它过期）。放在这里推进，编辑器里自检也能用。
            Bullet.TrailClock += dt;

            // ★ 刷地**不再轮转**（以前是「每帧只让 n/1200 分之一的子弹刷」）。
            //   轮转会让一颗子弹「刷一下、飘一段、再刷一下」→ 画出来的领土是**断断续续的斑点**
            //   （子弹一帧走 0.037m，等它轮到下一次的时候已经飘出去好几倍直径了）。
            //   现在有了 OwnedBy 预筛（在自家领土上飞的子弹直接跳过、不占预算），
            //   真正会做刷地判定的只有「飞在敌方/无主地上」的那些，而它们基本刷一次就耗尽数值，
            //   所以每帧的刷地次数天然被**发射速率**卡住，不需要再靠轮转限流。

            long _ts = Prof.Tick();
            // 优化：地图每帧只取一次（原来每颗子弹每帧都去 GameWorld 上取一遍）
            var field = GameWorld.Instance != null ? GameWorld.Instance.Field : null;

            EnsureFlat();
            for (int i = 0; i < _active.Count; i++)
            {
                var b = _active[i];
                if (b == null) { _fUse[i] = false; continue; }
                if (!b.InUse) { _fUse[i] = false; continue; }
                // ★ 必须吃返回值：数值耗尽的子弹是「返回 false」而不是自己 Release()。
                //   之前漏掉这一步 → 打空的子弹永远留在 _active 里 → stride 越涨越大 →
                //   allowPaint 几乎全是 false → 子弹还能飞、但再也不涂地了。
                if (!b.Step(dt, true, field)) b.Release();

                // ★ 顺手抄进扁平数组（这一段本来就在碰这些对象，几乎不要钱）
                _fUse[i] = b.InUse;
                _fPos[i] = b.Pos;
                _fVisR[i] = b.VisualRadius;
                _fVal[i] = b.RemainingValue;
                _fOwner[i] = b.Owner;
            }



            // ★★ 顺序很重要：**先回收，再建网格**。
            //   回收是 swap-remove，会把 `_active` 里的元素往前挪、总数变少；
            //   如果网格是在回收**之前**建的，它里面就留着回收前的大下标。
            //   以前读网格的都在池子内部、又都在回收之前，所以没事；
            //   但**近防查询是在 Turret.Update 里读的**，它跑在回收之后 ——
            //   `_active[i]` 一下就越界抛异常，`Turret.Update` 剩下的部分（**包括开火**）
            //   整段被跳过 → 那一帧不开火 → 看起来就是「一会儿打一会儿不打」。
            CleanupDead(dt);

            // 网格必须反映**回收之后**的 `_active`，外部读它才是安全的
            RebuildGrid();
            Prof.StepMs = Prof.Since(_ts);

            long _tr2 = Prof.Tick();
            ResolveBulletPairs();
            ResolveTurrets();
            ResolveBigBalls();
            Prof.ResolveMs = Prof.Since(_tr2);

            Prof.PoolMs = Prof.Since(_t0);
        }

        /// <summary>把死掉的子弹收回去（swap-remove）。必须在建网格**之前**跑。</summary>
        void CleanupDead(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var b = _active[i];
                if (b != null && b.InUse) continue;
                // ★ 数值刚耗尽的子弹**先留在场上**，让拖尾慢慢缩没（不发散了就立刻 SetActive(false) 的话，
                //   拖尾会「啪」一下整体消失）。这段期间它不再移动、不再判定、也不再画圆点（渲染那边只画 InUse 的）。
                if (b != null && b.FadeTimer > 0f)
                {
                    b.FadeTimer -= dt;
                    continue;
                }

                int last = _active.Count - 1;
                _active[i] = _active[last];
                SwapFlat(i, last);            // ★ 扁平数组必须跟着一起 swap
                _active.RemoveAt(last);
                if (b == null) continue;
                b.InUse = false;
                // ★ 也不 `SetActive(false)`（见 Rent 里的说明）：对象一直 inactive，不来回切
                _free.Push(b);        // 不做上限，也不 Destroy
            }
        }

        void LateUpdate()
        {
            long _t0 = Prof.Tick();
            // 位置已经是**这一帧**算出来的了（见 Update），所以不用再插值。
            float alpha = 1f;

            // ★ TrailRenderer 画法才需要每颗子弹同步 transform（TrailRenderer 是**跟着 transform 采样**的）。
            //   合批画法下拖尾直接读 `Bullet.TrailPts`，**一次 transform 写都不用** ——
            //   几千个 Transform 的写入本身也是掉帧的一大块。
            if (Cfg.BulletTrailUsesTrailRenderer)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    var b = _active[i];
                    if (b == null || !b.InUse) continue;
                    b.SyncTransform(alpha);
                }
            }
            else if (BulletTrailRenderer.Instance != null)
            {
                // 注意：这里要用 `_active` 全量 —— 包含「数值耗尽、拖尾正在收掉」的子弹
                BulletTrailRenderer.Instance.Render(_active);
            }

            if (BulletRenderer.Instance != null) BulletRenderer.Instance.Render(_active, alpha);

            // ★ 这笔开销在 `pool` 的计时之外（那是 Update 里的），单列一栏：
            //   「frame 减掉 pool/flush/hud 还剩多少」如果很大，先看这一栏是不是网格装配。
            Prof.ViewMs = Prof.Since(_t0);
        }

        // ─────────────────────────────────────────────────────────────
        // 网格
        // ─────────────────────────────────────────────────────────────

        void RebuildGrid()
        {
            EnsureNextCapacity();
            for (int i = 0; i < _head.Length; i++) _head[i] = -1;
            // ★ 只读扁平数组 —— 不再解引用 Bullet 对象（这一段原来是 cache-miss 大户）
            for (int i = 0; i < _active.Count; i++)
            {
                if (!_fUse[i]) continue;
                int c = CellOf(_fPos[i]);
                _next[i] = _head[c];
                _head[c] = i;
            }
        }

        int CellOf(Vector2 p)
        {
            int cx = Mathf.Clamp((int)(p.x * _invCell), 0, GridN - 1);
            int cy = Mathf.Clamp((int)(p.y * _invCell), 0, GridN - 1);
            return cy * GridN + cx;
        }

        /// <summary>
        /// 把 (中心, 半径) 覆盖到的格子走一遍，把命中的子弹**下标**收进一个复用的列表。
        ///
        /// ★ 原来这里是 `ForEachNear(center, radius, System.Action<Bullet>)`，
        ///   调用方传的是**捕获了局部变量的 lambda** —— 每调一次就分配一个闭包 + 委托
        ///   （炮台 4 次/帧、大球更多），全是 GC 垃圾。改成收下标之后**一次分配都没有**，
        ///   调用方拿 `_nearIdx` 自己遍历。
        /// </summary>
        readonly List<int> _nearIdx = new List<int>(256);

        void CollectNear(Vector2 center, float radius)
        {
            _nearIdx.Clear();
            int x0 = Mathf.Clamp((int)((center.x - radius) * _invCell), 0, GridN - 1);
            int x1 = Mathf.Clamp((int)((center.x + radius) * _invCell), 0, GridN - 1);
            int y0 = Mathf.Clamp((int)((center.y - radius) * _invCell), 0, GridN - 1);
            int y1 = Mathf.Clamp((int)((center.y + radius) * _invCell), 0, GridN - 1);
            for (int cy = y0; cy <= y1; cy++)
            {
                int row = cy * GridN;
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int i = _head[row + cx]; i >= 0; i = _next[i])
                    {
                        if (i >= _active.Count) continue;      // 防御越界
                        var b = _active[i];
                        if (b == null || !b.InUse) continue;
                        _nearIdx.Add(i);
                    }
                }
            }
        }

        /// <summary>
        /// 子弹打子弹（规格：规则和撞护盾一样）。
        ///
        /// 只和「自己这一格 + 右 / 下 / 右下 / 左下」四个方向比 ——
        /// 那样每一对**恰好算一次**，又不会漏掉跨格的一对（比只比同格要正确得多）。
        /// 子弹半径只有 0.032m，格子是 14/48 ≈ 0.29m，所以绝大多数格子是空的，开销很小。
        /// </summary>
        static readonly int[] PairDx = { 1, 0, 1, -1 };
        static readonly int[] PairDy = { 0, 1, 1, 1 };

        void ResolveBulletPairs()
        {
            // 同一格里的子弹数是 k，那这一格就是 O(k²)。刚出膛的一整条弹流会挤在少数几格里，
            // k 可以到几百 —— 所以给每帧的配对数封顶，宁可少判几个也别掉帧。
            //
            // ★★ 终极优化：整段**只在扁平数组上跑**，不再解引用 `Bullet` 对象。
            //   以前每次比较都要 `_active[j]` → `b.InUse` → `b.Owner` → `b.Pos`
            //   → `b.VisualRadius` → `b.RemainingValue`，六个分散的堆访问、六次 cache miss；
            //   现在 `_fUse/_fPos/_fVisR/_fVal/_fOwner` 全是连续内存，一次 miss 都没有。
            //   只有**真的撞上**了才去解引用那两个对象调 `HitEnemyBullet`（很少）。
            int budget = Cfg.MaxBulletPairChecks;
            int n = _active.Count;
            for (int cy = 0; cy < GridN; cy++)
            {
                int row = cy * GridN;
                for (int cx = 0; cx < GridN; cx++)
                {
                    int c = row + cx;
                    for (int i = _head[c]; i >= 0; i = _next[i])
                    {
                        if (i >= n || !_fUse[i]) continue;
                        Vector2 pa = _fPos[i];
                        float ra = _fVisR[i];
                        Faction oa = _fOwner[i];
                        double va = _fVal[i];

                        // ★ **同一格之内**也要比 —— 格子 0.29m 而子弹直径才 0.064m，
                        //   绝大多数真正撞上的两颗其实都在同一格里。
                        //   只和链表里排在 i 后面的比，保证每对只算一次。
                        for (int j = _next[i]; j >= 0; j = _next[j])
                        {
                            if (j >= n || !_fUse[j]) continue;
                            if (_fOwner[j] == oa) continue;
                            // ★ 预算要在**距离判定之前**扣：同一格里的子弹是 O(k²)，
                            //   刚出膛的一整条弹流会挤在少数几格里，k 能到几百 ——
                            //   距离判定本身就不便宜，不封顶的话这里是个无底洞。
                            if (--budget < 0) return;
                            float rr0 = ra + _fVisR[j];
                            if ((pa - _fPos[j]).sqrMagnitude > rr0 * rr0) continue;
                            HitPair(i, j);
                            if (!_fUse[i]) break;
                        }
                        if (!_fUse[i]) continue;

                        for (int k = 0; k < 4; k++)
                        {
                            int nx = cx + PairDx[k], ny = cy + PairDy[k];
                            if (nx < 0 || nx >= GridN || ny >= GridN) continue;

                            int nc = ny * GridN + nx;
                            for (int j = _head[nc]; j >= 0; j = _next[j])
                            {
                                if (j >= n || !_fUse[j]) continue;
                                if (_fOwner[j] == oa) continue;      // 不打自己人

                                if (--budget < 0) return;

                                float rr = ra + _fVisR[j];
                                if ((pa - _fPos[j]).sqrMagnitude > rr * rr) continue;

                                HitPair(i, j);
                                if (!_fUse[i]) break;
                            }
                            if (!_fUse[i]) break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 第 i 颗和第 j 颗真的撞上了：数值大的赢、小的消失，赢家扣掉对方那么多值并反弹。
        /// 这里才需要碰真正的对象（很少发生），结算完把扁平数组同步一下。
        /// </summary>
        void HitPair(int i, int j)
        {
            if (i >= _active.Count || j >= _active.Count) return;
            var a = _active[i];
            var b = _active[j];
            if (a == null || b == null) return;
            if (a.RemainingValue >= b.RemainingValue) a.HitEnemyBullet(b);
            else b.HitEnemyBullet(a);

            // 同步这一对（赢家数值变了、输家可能没了）
            _fUse[i] = a.InUse; _fVal[i] = a.RemainingValue;
            _fUse[j] = b.InUse; _fVal[j] = b.RemainingValue;
        }

        /// <summary>
        /// 找离中心最近的**敌方**子弹（尾流也是子弹，一样算）。给炮台近防系统用。
        /// 走同一张网格，**不分配内存**（不用闭包）。
        /// </summary>
        public bool TryFindNearestEnemyBullet(Vector2 center, float radius, Faction owner,
                                              out Vector2 pos, out float dist)
        {
            pos = Vector2.zero;
            dist = 0f;
            if (radius <= 0f || _head == null) return false;

            float best = radius * radius;
            bool found = false;

            int x0 = Mathf.Clamp((int)((center.x - radius) * _invCell), 0, GridN - 1);
            int x1 = Mathf.Clamp((int)((center.x + radius) * _invCell), 0, GridN - 1);
            int y0 = Mathf.Clamp((int)((center.y - radius) * _invCell), 0, GridN - 1);
            int y1 = Mathf.Clamp((int)((center.y + radius) * _invCell), 0, GridN - 1);

            for (int cy = y0; cy <= y1; cy++)
            {
                int row = cy * GridN;
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int i = _head[row + cx]; i >= 0; i = _next[i])
                    {
                        // 防御：网格万一过期（下标 ≥ 当前数量）也不能抛异常 ——
                        // 抛出去会把整个 Turret.Update 打断，那一帧就不开火了。
                        if (i >= _active.Count) continue;
                        var b = _active[i];
                        if (b == null || !b.InUse) continue;
                        if (b.Owner == owner) continue;
                        float d = (b.Pos - center).sqrMagnitude;
                        if (d >= best) continue;
                        best = d; pos = b.Pos; found = true;
                    }
                }
            }

            if (found) dist = Mathf.Sqrt(best);
            return found;
        }

        void ResolveTurrets()
        {
            var world = GameWorld.Instance;
            if (world == null) return;
            var factions = world.Factions;
            for (int f = 0; f < factions.Count; f++)
            {
                var t = factions[f].Turret;
                if (t == null || t.IsDead) continue;

                bool hasShield = t.Owner != null && t.Owner.Shield > 0d;
                float shieldR = hasShield ? t.Owner.ShieldRadius : 0f;
                float coreR = t.CoreRadius;
                float reach = Mathf.Max(shieldR, coreR) + Cfg.BulletDotRadius * 2f;
                Vector2 c = t.WorldPosition;

                CollectNear(c, reach);
                for (int k = 0; k < _nearIdx.Count; k++)
                {
                    int bi = _nearIdx[k];
                    if (bi >= _active.Count) continue;
                    var b = _active[bi];
                    if (b == null || !b.InUse || b.Owner == t.Owner) continue;
                    float d2 = (b.Pos - c).sqrMagnitude;
                    float rr;
                    if (hasShield)
                    {
                        rr = shieldR + b.VisualRadius;
                        if (d2 <= rr * rr)
                        {
                            // 传进去的是「子弹边缘探进护盾多深」（扣掉子弹自身半径），
                            // 护盾靠它算羽化 —— 刚好擦到最外沿时系数是 0，等于没碰到。
                            b.ResolveEnemyShield(t, shieldR, Mathf.Sqrt(d2) - b.VisualRadius);
                            continue;
                        }
                    }
                    rr = coreR + b.VisualRadius;
                    if (d2 <= rr * rr) { t.HitBody(); b.Release(); }
                }
            }
        }

        void ResolveBigBalls()
        {
            var world = GameWorld.Instance;
            if (world == null) return;
            var factions = world.Factions;
            for (int f = 0; f < factions.Count; f++)
            {
                var balls = factions[f].BigBalls;
                for (int k = 0; k < balls.Count; k++)
                {
                    var big = balls[k];
                    if (big == null || big.Consumed) continue;
                    float reach = big.Radius + Cfg.BulletDotRadius * 2f;
                    Vector2 c = big.transform.position;
                    CollectNear(c, reach);
                    for (int nk = 0; nk < _nearIdx.Count; nk++)
                    {
                        int bi = _nearIdx[nk];
                        if (bi >= _active.Count) continue;
                        var b = _active[bi];
                        if (b == null || !b.InUse) continue;
                        float rr = big.Radius + b.VisualRadius;
                        if ((b.Pos - c).sqrMagnitude <= rr * rr) b.HitBigBall(big);
                    }
                }
            }
        }

        /// <summary>引力球对**所有阵营的子弹**吸引（网格用不上，引力球很少）。</summary>
        /// <summary>
        /// 引力球对子弹的作用。**只比老实现多一条规则**（第 102 轮）。
        ///
        /// 老实现：`Vel += 方向 × strength/距离 × dt`，但速度到 `BulletMaxSpeed` 就**整条跳过**。
        /// 用户报的「子弹移速比较快的时候几乎不受引力影响（不转向了）」就是那条跳过造成的。
        ///
        /// 现在：
        ///   · 速度**没到上限** → 照旧**整个加速度都加上**（子弹被拽快，老手感不变）
        ///   · 速度**到了上限** → 只保留加速度的**横向分量**（把顺着速度的那一份整条去掉），
        ///     于是它既不加速也**不被刹车**、只是被掰弯；总共只掰
        ///     `Cfg.GravityBulletMaxTurnDeg` 度，掰完纯惯性飞走。
        /// ★ 别改成「把跳过删掉、加速度照加」——离线模拟里那样**每颗子弹都会被吸住绕圈**。
        /// ★ 也别只去掉「加速那一半」平行分量 —— 往外飞的时候会被刹车吸回去，同样出不来。
        ///
        /// ★★★ 「数值越大的球引力越大」：每颗引力球的贡献还要乘
        /// `GameWorld.GravityValueFactor(这颗球的数值)` —— 老实现里这一项**完全没有**，
        /// 所以 100M 和 100B 的球对子弹的引力一模一样（那是个实打实的洞）。
        /// </summary>
        /// <param name="strength">`Cfg.GravityOnBullet`（**参考数值 100M 那一档**的强度）。</param>
        /// <param name="dt">这一步的**真实时长**。★★ 调用方是「每 `GravityTickDivisor`
        /// 个物理帧才跑一次」的，所以这里收到的必须是 `Time.fixedDeltaTime × 那个除数` ——
        /// 直接写 `Time.fixedDeltaTime` 会让子弹只拿到 1/3 的引力（第 102 轮踩过，
        /// 用户看到的现象就是「引力直接没了」）。</param>
        public void ApplyGravity(List<BigBall> balls, float strength, float dt)
        {
            if (balls == null || balls.Count == 0) return;
            float maxR = Cfg.GravityMaxRange;      // ★ 别写死（原来硬编码 14）
            float max2 = maxR * maxR;
            float stop2 = Cfg.GravityBulletStopRadius * Cfg.GravityBulletStopRadius;
            float cap = Cfg.BulletMaxSpeed;
            float capOn = cap - Cfg.GravityBulletCapEpsilon;   // 到这儿就算"已经顶上了"
            for (int i = 0; i < _active.Count; i++)
            {
                var b = _active[i];
                if (b == null || !b.InUse) continue;

                Vector2 vel = b.Vel;
                float sp = vel.magnitude;
                if (sp < 1e-4f) continue;

                // ★★★ 第 103 轮（用户对照原版录像）：「**去掉那条转角额度**」——
                //   之前有一道「子弹这辈子最多被掰 `GravityBulletMaxTurnDeg` 度」的闸，
                //   于是子弹只会拐 ~100° 然后**笔直撞进球里**；录像里子弹是
                //   **绕着球转大圈**（右栏那一大片同心圆弧拖尾）。
                //   额度一去，子弹就能一直被横向分量掰弯、稳定地绕着球转。
                //   ★ `GravityOnBullet` 的强度**一个字没动**（用户：「之前我们的引力本身没大问题」）。
                bool atCap = sp >= capOn;

                // 整个加速度（老公式：强度 / 距离 —— 但★每颗引力球再各自乘上"数值放大"）
                Vector2 acc = Vector2.zero;
                for (int k = 0; k < balls.Count; k++)
                {
                    var g = balls[k];
                    if (g == null || g.Consumed) continue;
                    Vector2 d = (Vector2)g.transform.position - b.Pos;
                    float dist2 = d.sqrMagnitude;
                    if (dist2 > max2) continue;
                    if (dist2 < stop2) continue;
                    float dist = Mathf.Sqrt(dist2);
                    // ★★★ 「数值越大的球引力越大」：这一项以前**根本没有** ——
                    //   老公式 `strength / 距离` 里不含引力球的数值，于是 100M 的引力球
                    //   和 100B 的中子星对子弹的引力一模一样（查出来就是这条洞）。
                    //   现在乘上 `GravityValueFactor(这颗球的数值)`：门槛 100M = ×1（观感不变），
                    //   300M 以下线性；再往上（中子星）压平，见 `Cfg.NeutronStarGravityKnee`。
                    //   ★ 安全：最后那一下 `nv *= cap / ns` 会把速度夹回 `BulletMaxSpeed`，
                    //     所以 factor 再大也只是让子弹拐得更急，不会被打成炮弹。
                    acc += d / dist * (strength * GameWorld.GravityValueFactor(g.Value)
                                       / Mathf.Max(dist, Cfg.GravityMinDistance));
                }
                if (acc.sqrMagnitude < 1e-6f) continue;

                if (atCap)
                {
                    // ★ 到顶了：把「顺着速度」的那一份**整条**去掉（既不加速、也不刹车），
                    //   只留横向的 —— 子弹被掰弯，但速度不变，所以出得去。
                    Vector2 u = vel / sp;
                    acc -= u * Vector2.Dot(acc, u);
                }

                Vector2 nv = vel + acc * dt;
                float ns = nv.magnitude;
                if (ns > cap) nv *= cap / ns;          // 速度上限照旧（老代码那条）

                // ★ 单步转向限速：这一步最多把方向拧 `GravityBulletMaxTurnPerTickDeg` 度。
                //   中子星旁边 `acc × dt` 能到几十 m/s（子弹本身才 6），一步就能把方向
                //   拧过 90° —— 画面上就是「闪现」。限的是**每步**、不是一辈子，所以照样能绕圈。
                float maxTurn = Cfg.GravityBulletMaxTurnPerTickDeg;
                if (maxTurn > 0f && ns > 1e-5f)
                {
                    float delta = Vector2.SignedAngle(vel, nv);
                    if (Mathf.Abs(delta) > maxTurn)
                    {
                        float a0 = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
                        float a1 = (a0 + Mathf.Sign(delta) * maxTurn) * Mathf.Deg2Rad;
                        nv = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * ns;
                    }
                }

                if (atCap)
                {
                    // 记一下一共掰了多少度（**只记账，不再当额度用** —— 见上面那段）。
                    // 自检拿它验「子弹真的能绕起来」。
                    float d0 = Mathf.Atan2(vel.y, vel.x);
                    float d1 = Mathf.Atan2(nv.y, nv.x);
                    b.GravityTurnDeg += Mathf.Abs(Mathf.DeltaAngle(d0 * Mathf.Rad2Deg, d1 * Mathf.Rad2Deg));
                }
                b.Vel = nv;
            }
        }
    }
}
