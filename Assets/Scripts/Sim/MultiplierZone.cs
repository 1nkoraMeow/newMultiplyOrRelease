using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 增倍区（左栏）—— **第一版**（用户：「再战我们的第一版增倍区，要求 1:1 还原」）。
    /// 从上到下：
    ///   ① 两颗灰阻挡球（左右对称，高处）
    ///   ② **x16 阻块**（那块灰色「帽子」，形状按用户给的第二张图）+ 坐在它顶上的**发射炮台**
    ///   ③ 贴着左右墙的两颗灰阻挡球
    ///   ④ 左右各 **5 块乘法门**：从外往内 **x2 x8 x4 x8 x2**（白牌 + 下面的深灰判定条）
    ///   ⑤ 两颗**移动阻挡球**（只会水平移动，碰到墙折返）之一 —— 就地滚在门顶上
    ///   ⑥ 一排 **13 颗**固定阻挡球（从 -9 铺到 +9，间距 1.5m）
    ///   ⑦ 另一颗移动阻挡球 —— 滚在这一排上
    ///   ⑧ 底部**武器区**（5 格，这一版先不动）
    /// 四周有墙。**这里没有阵营炮台。**
    ///
    /// ★ 所有米数都是从用户那张截图上量出来再换算的（换算过程写在 Cfg 的「五、增倍区」开头）。
    /// ★ 灰色物件**全部是可碰撞的阻挡物**（用户特别强调过）。
    /// </summary>
    public class MultiplierZone : MonoBehaviour
    {
        public Vector2 Center;
        public readonly List<MultiplierGate> Gates = new List<MultiplierGate>();
        public WeaponZone Weapons { get; private set; }
        /// <summary>浮力区：从固定阻挡球那一排一直通到 x16 下方的一条通道。</summary>
        public BuoyancyZone Buoyancy { get; private set; }
        /// <summary>顶部那扇 x16（它同时是阻挡物）。</summary>
        public MultiplierGate X16 { get; private set; }
        public Launcher Launcher { get; private set; }
        public readonly List<MovingBlocker> Movers = new List<MovingBlocker>();

        Transform _root;

        /// <summary>区根节点 —— 粒子拖尾挂在它下面，这样坐标和球的 localPosition 同一空间。</summary>
        public Transform Root { get { return _root; } }

        public float Width { get { return Cfg.ZoneWidth; } }
        public float Height { get { return Cfg.ZoneHeight; } }

        public static MultiplierZone Build(Vector2 center, Transform parent)
        {
            var go = new GameObject("MultiplierZone");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = center;

            var zone = go.AddComponent<MultiplierZone>();
            zone.Center = center;
            zone._root = go.transform;

            zone.BuildBackdrop();
            zone.BuildWalls();
            zone.BuildX16AndLauncher();
            zone.BuildGates();
            // ★ 规格（用户，第 78 轮）：**浮力区保留，还是要的** ——
            //   从固定阻挡球那一排一直通到 x16 下方，球在里面往上飘。
            zone.Buoyancy = BuoyancyZone.Create(zone._root);
            zone.BuildFixedBlockers();
            zone.BuildPegRow();
            zone.BuildMovingBlockers();
            zone.Weapons = WeaponZone.Create(zone._root, zone);
            return zone;
        }

        // ─────────────────────────────────────────────────────────────
        // 构建
        // ─────────────────────────────────────────────────────────────

        void BuildBackdrop()
        {
            // 一块深一档的背景板，让增倍区和右栏战场分得开
            var bg = Primitives.MakeNode("Backdrop", Primitives.Square,
                new Color(0.10f, 0.10f, 0.11f), -10, _root);
            bg.transform.localScale = new Vector3(Cfg.ZoneOuterWidth, Cfg.ZoneOuterHeight, 1f);

            // ★ 第一版截图里背景有一张**方格网**（格子约 1.68m，从中心往两边铺）。
            //   用一格细线画 —— 图层压在背景板之上、其它一切之下。
            var grid = new GameObject("ZoneGrid");
            grid.transform.SetParent(_root, false);
            float step = Mathf.Max(Cfg.ZoneGridStep, 0.1f);
            var line = new Color(0.22f, 0.22f, 0.24f);
            float hw = Cfg.ZoneWidth * 0.5f, hh = Cfg.ZoneHeight * 0.5f;
            int nx = Mathf.FloorToInt(hw / step), ny = Mathf.FloorToInt(hh / step);
            for (int i = -nx; i <= nx; i++)
            {
                if (i == 0) continue;
                var v = Primitives.MakeNode("V" + i, Primitives.Square, line, -9, grid.transform);
                v.transform.localPosition = new Vector3(i * step, 0f, 0f);
                v.transform.localScale = new Vector3(Cfg.ZoneGridLineWidth, Cfg.ZoneHeight, 1f);
            }
            for (int i = -ny; i <= ny; i++)
            {
                if (i == 0) continue;
                var h = Primitives.MakeNode("H" + i, Primitives.Square, line, -9, grid.transform);
                h.transform.localPosition = new Vector3(0f, i * step, 0f);
                h.transform.localScale = new Vector3(Cfg.ZoneWidth, Cfg.ZoneGridLineWidth, 1f);
            }
        }

        void BuildWalls()
        {
            // ★ 墙贴在**内腔外面**（别把墙画进内腔里，那会侵占乘倍区）
            float iw = Cfg.ZoneWidth * 0.5f, ih = Cfg.ZoneHeight * 0.5f, t = Cfg.ZoneWallThickness;
            float ow = Cfg.ZoneOuterWidth, oh = Cfg.ZoneOuterHeight;

            MakeWall("WallLeft", new Vector2(-iw - t * 0.5f, 0f), new Vector2(t, oh));
            MakeWall("WallRight", new Vector2(iw + t * 0.5f, 0f), new Vector2(t, oh));
            MakeWall("WallTop", new Vector2(0f, ih + t * 0.5f), new Vector2(ow, t));
            MakeWall("WallBottom", new Vector2(0f, -ih - t * 0.5f), new Vector2(ow, t));
        }

        void MakeWall(string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;

            var sr = Primitives.MakeNode("Body", Primitives.Square, Cfg.BlockerGrey, 3, go.transform);
            sr.transform.localScale = new Vector3(size.x, size.y, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            col.sharedMaterial = Physics2DConfig.ZoneWall;
        }

        /// <summary>顶部：帽子形**阻挡物** + 白框 x16 判定区 + 坐在帽子上的发射炮台。</summary>
        void BuildX16AndLauncher()
        {
            // ① 帽子：**纯阻挡**（用户：「我发你的那个不规则图形纯用来阻挡用的，可以碰撞的」）
            MultiplierGate.CreateX16Barrier(_root, new Vector2(0f, Cfg.X16BarrierY));

            // ② 白框判定区：压在帽子中间偏下，它的**中间**才是判定点
            X16 = MultiplierGate.CreateX16(_root,
                new Vector2(0f, Cfg.X16BarrierY + Cfg.X16PlateOffsetY));
            Gates.Add(X16);

            // ③ 炮台坐在帽子**最上面那个尖**上。
            //    ★ 规格（用户）：「炮座**一半在阻挡物里、一半在外面**」——
            //      所以圆心就放在**帽尖的高度**上（偏移 0），下半颗正好嵌进帽子的锥面。
            float launcherY = Cfg.X16BarrierY + Cfg.X16BarrierHeight * 0.5f + Cfg.LauncherSeatOffsetY;
            Launcher = Launcher.Create(_root, new Vector2(0f, launcherY));
        }

        /// <summary>
        /// 左右各 4 块乘法门：从**贴墙往里** x2 x4 x8 x2（第 91 轮按用户发的图改的），两组都贴着自己的墙；
        /// 每两块之间的缝里立一根灰色**阻挡柱**（用户第 79 轮要的「阻挡墙」）。
        /// </summary>
        void BuildGates()
        {
            float halfW = Cfg.ZoneWidth * 0.5f;
            int n = Cfg.GateMultipliers.Length;
            var posts = new GameObject("GatePosts");
            posts.transform.SetParent(_root, false);

            int gateIndex = 0;
            for (int sideIdx = 0; sideIdx < 2; sideIdx++)
            {
                float side = sideIdx == 0 ? -1f : 1f;      // -1 = 左组
                // 从贴墙那一侧往里码
                float cursor = side * (halfW - Cfg.GateSideMargin);

                for (int col = 0; col < n; col++)
                {
                    float w = Cfg.GateWidths[col];

                    // ★ 放这一块之前，先在它和上一块之间的缝里立一根柱子。
                    //   cursor 此刻 = 上一块的内边缘 + 一个缝宽，所以缝心在 cursor 往回半个缝处。
                    if (col > 0) MakeGatePost(posts.transform, cursor + side * Cfg.GateGap * 0.5f);

                    float centerX = cursor - side * w * 0.5f;
                    Gates.Add(MultiplierGate.Create(_root, new Vector2(centerX, Cfg.GatePlateY),
                                                    w, Cfg.GateMultipliers[col], gateIndex));
                    // ★★ 第 91/92 轮（用户 图）：「**x4 x8 有固定阻挡球**」——
                    //   顶在白牌**正上方**（横向对正牌心）；左右两组都会各长一颗。
                    if (System.Array.IndexOf(Cfg.GateTopBlockerMultipliers,
                                             Cfg.GateMultipliers[col]) >= 0)
                        MakeGateTopBlocker(_root, centerX);
                    gateIndex++;

                    cursor -= side * (w + Cfg.GateGap);
                }

                // ★ 规格（用户）：「**最里面**那两块 x2 也要有柱子」——
                //   码完一组之后，在**朝中间那道缝**的那一端立一根。
                //   注意：是**内侧**，不是贴墙那侧（用户第 80 轮专门更正过）。
                //
                // ★★ 第 91 轮修正（用户：「所有 x2 x4 x8 字样都得在白牌**正中间**」）：
                //   原来这里写的是 `gap + postWidth*0.5`，等于把柱子**往牌子里压了半个柱宽** ——
                //   最里面那块白牌的可见宽度被吃掉 0.20m，白牌可见部分就不再对称，
                //   字看着就偏了（实测：8 块门里只有这 2 块偏了 +6 / −7 px）。
                //   改成和**内缝**同一个公式：柱子落在缝的**正中**，一点不啃牌子。
                MakeGatePost(posts.transform, cursor + side * Cfg.GateGap * 0.5f);
            }
        }

        /// <summary>
        /// 门顶正中那颗**固定阻挡球**（用户第 91 轮 图一：「x8 是有一颗固定阻挡球的」）。
        /// 球心就放在**白牌顶边**上（图里量出来的），横向对正牌心。
        /// 颜色和别的阻挡物一样；**不挂 `BlockerBall`** —— 门这一排的阻挡物都不闪
        /// （用户第 79 轮：「碰到柱子…不需要闪烁，有点怪」）。
        /// </summary>
        void MakeGateTopBlocker(Transform parent, float x)
        {
            var go = new GameObject("GateTopBlocker");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(
                x, Cfg.GatePlateY + Cfg.GatePlateHeight * 0.5f, 0f);

            var sr = Primitives.MakeNode("Body", Primitives.Circle, Cfg.BlockerGrey, 7, go.transform);
            sr.transform.localScale = Vector3.one * (Cfg.GateTopBlockerRadius * 2f);

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = Cfg.GateTopBlockerRadius;
            col.sharedMaterial = Physics2DConfig.ZoneWall;
        }

        /// <summary>
        /// 两块门之间那根**灰色阻挡柱**（用户第 79 轮要的「阻挡墙」）。
        /// 纵向盖满整块门（白牌 + 下面的判定条），所以球在**任何高度**都别想从两块门中间钻过去。
        /// 图层**压在白牌之上**（6 &gt; 牌子 5）—— 不然它会被白牌盖住，只在下面的阻挡条那一截露出来，
        /// 看着就不像一根立在两块门之间的柱子了。
        /// </summary>
        void MakeGatePost(Transform parent, float x)
        {
            // ★★ 第 91 轮（用户）：「柱子的**长度略微缩小**，柱子上的阻挡球相应降低，
            //   使柱顶球的**顶部也只略微高于白框一点**」——
            //   底端不动，只把**顶端**往下让 `GatePostTopDrop`，柱顶球跟着一起降。
            float top = Cfg.GatePlateHeight * 0.5f - Cfg.GatePostTopDrop;   // 白牌顶 − 下移量
            float barTop = -Cfg.GatePlateHeight * 0.5f + 0.04f;
            float bottom = barTop - Cfg.GatePlateHeight * Cfg.GateTriggerHeightFactor;

            var go = new GameObject("GatePost");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, Cfg.GatePlateY + (top + bottom) * 0.5f, 0f);

            // ★ 规格（用户，第 82 轮）：柱子要**和阻挡球一个颜色**（浅灰）。
            //   以前是深灰，和背景（0.10）几乎一样 —— 用户的原话是「柱子还是没加进去吧」，
            //   其实一直在那儿，只是看不见。
            var sr = Primitives.MakeNode("Body", Primitives.Square, Cfg.BlockerGrey, 6, go.transform);
            sr.transform.localScale = new Vector3(Cfg.GatePostWidth, top - bottom, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(Cfg.GatePostWidth, top - bottom);
            col.sharedMaterial = Physics2DConfig.ZoneWall;
            // ★ 规格（用户）：「碰到柱子…不需要闪烁，有点怪」→ **不挂 BlockerBall**

            // ★ 柱顶那颗阻挡球
            AddPostCap(parent, x, Cfg.GatePlateY + top);
        }

        /// <summary>
        /// ★★ 规格（用户，第 82 轮）：**每根柱子的顶上都坐一颗阻挡球** ——
        /// 半径 = `Cfg.PostCapRadius`（柱子宽度就是 2×它），球的**中心与柱子顶面齐平**，
        /// 也就是半球露在柱子上面。门的柱子和武器区的柱子都用它，参数只有位置。
        /// </summary>
        public static void AddPostCap(Transform parent, float localX, float topLocalY)
        {
            float r = Cfg.PostCapRadius;      // ★ 柱子宽度就是 2×它 → 永远不会「不一致」

            var go = new GameObject("PostCap");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(localX, topLocalY, 0f);

            var sr = Primitives.MakeNode("Body", Primitives.Circle, Cfg.BlockerGrey, 7, go.transform);
            sr.transform.localScale = Vector3.one * (r * 2f);

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = r;
            col.sharedMaterial = Physics2DConfig.ZoneWall;
            // ★ 规格（用户）：柱顶球属于柱子 → **同样不闪**（闪起来怪）
        }

        /// <summary>四颗固定灰阻挡球：最上面两颗 + 贴着左右墙的两颗。</summary>
        void BuildFixedBlockers()
        {
            var root = new GameObject("Blockers");
            root.transform.SetParent(_root, false);

            for (int i = 0; i < Cfg.TopBlockerX.Length; i++)
                MakeBlocker(root.transform, "TopBlocker" + i,
                            new Vector2(Cfg.TopBlockerX[i], Cfg.TopBlockerY), Cfg.BlockerRadius);

            for (int i = 0; i < Cfg.WallBlockerX.Length; i++)
                MakeBlocker(root.transform, "WallBlocker" + i,
                            new Vector2(Cfg.WallBlockerX[i], Cfg.WallBlockerY), Cfg.BlockerRadius);
        }

        void MakeBlocker(Transform parent, string name, Vector2 localPos, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var sr = Primitives.MakeNode("Body", Primitives.Circle, Cfg.BlockerGrey, 7, go.transform);
            sr.transform.localScale = Vector3.one * (radius * 2f);

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = radius;
            col.sharedMaterial = Physics2DConfig.ZoneWall;
            // ★ 规格：阻挡球一律灰，被增倍球撞到时闪成那个阵营的颜色
            BlockerBall.Attach(go, sr);
        }

        /// <summary>一排等距固定阻挡球：13 颗，正好从 -9 铺到 +9。</summary>
        void BuildPegRow()
        {
            var row = new GameObject("PegRow");
            row.transform.SetParent(_root, false);

            int n = Cfg.PegRowCount;
            for (int i = 0; i < n; i++)
            {
                // 13 颗 → 12 段，首尾正好落在两边的墙上
                float x = -Cfg.PegRowSpacing * (n - 1) * 0.5f + Cfg.PegRowSpacing * i;

                var go = new GameObject("Peg" + i);
                go.transform.SetParent(row.transform, false);
                go.transform.localPosition = new Vector3(x, Cfg.PegRowY, 0f);
                go.transform.localScale = Vector3.one * (Cfg.PegRadius * 2f);

                var sr = Primitives.MakeNode("Body", Primitives.Circle, Cfg.BlockerGrey, 7, go.transform);
                sr.transform.localScale = Vector3.one;

                var col = go.AddComponent<CircleCollider2D>();
                col.radius = 0.5f;
                col.sharedMaterial = Physics2DConfig.ZoneWall;
                BlockerBall.Attach(go, sr);
            }
        }

        /// <summary>两颗移动阻挡球。规格：**只会水平移动，碰到墙了就折返**。</summary>
        void BuildMovingBlockers()
        {
            var root = new GameObject("MovingBlockers");
            root.transform.SetParent(_root, false);

            for (int i = 0; i < Cfg.MovingBlockerY.Length; i++)
            {
                // 起步位置交错开，几颗不会永远同步、也不会叠在一起
                float sign = (i % 2 == 0) ? -1f : 1f;
                float start = sign * Cfg.ZoneWidth * (0.13f + 0.09f * i);
                Movers.Add(MovingBlocker.Create(root.transform, Cfg.MovingBlockerY[i],
                                                Cfg.MovingBlockerRadius, start));
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 给增倍球用的接口
        // ─────────────────────────────────────────────────────────────

        /// <summary>投放一颗增倍球（开局、每 60 秒、复活都用它）—— 从炮口打出去。</summary>
        public MultiplierBall SpawnBall(Faction owner, double value)
        {
            var ball = MultiplierBall.Create(owner, this, LauncherLocalPosition(), value, _root);
            if (owner != null) owner.Balls.Add(ball);
            ball.LaunchFrom(LauncherLocalPosition(), RandomLaunchDirection() * Cfg.LaunchSpeed);
            return ball;
        }

        /// <summary>投放点 = **炮口**（没有炮台时退回图里那个重投点）。</summary>
        public Vector2 LauncherLocalPosition()
        {
            if (Launcher != null) return Launcher.MuzzleLocalPosition;
            return new Vector2(Cfg.RelaunchX, Cfg.RelaunchY);
        }

        /// <summary>发射方向 = 炮口的当前朝向（炮口只在 ±65° 之间扫，永远朝上）。</summary>
        public Vector2 RandomLaunchDirection()
        {
            if (Launcher != null) return Launcher.Direction;
            return Random.value < 0.5f
                ? new Vector2(Mathf.Cos(135f * Mathf.Deg2Rad), Mathf.Sin(135f * Mathf.Deg2Rad))
                : new Vector2(Mathf.Cos(45f * Mathf.Deg2Rad), Mathf.Sin(45f * Mathf.Deg2Rad));
        }

        public Vector2 LocalToWorld(Vector2 local) { return Center + local; }
        public Vector2 WorldToLocal(Vector2 world) { return world - Center; }
    }
}
