using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 领土上的**花纹**（用户第 93 轮：「图一同款领土花纹实装」；
    /// 第 100 轮又给了一张 8 色参考图，于是多了几种画法）。
    ///
    /// 参考图里每个阵营的领土都是**一整片连续的几何花纹**（不是零星的小图案）。
    /// 现有画法见 <see cref="Kind"/>：蜂窝 / 双层描边蜂窝 / 立方体 / 人字纹 /
    /// 回形纹 / 不规则 / 方形细格 / 伞骨蜂窝。
    /// **每个阵营用哪一种、多大、多深，全都写在阵营表的 `Deco` 里**
    /// （<see cref="FactionSet4"/> / <see cref="FactionSet8"/>），这个文件只管怎么画。
    /// 图案线 = 阵营色压暗一档，所以它天然跟着阵营色走，而且**0 个额外 GameObject**。
    ///
    /// ★ 做法：每个阵营**预烘一张「瓷砖」**（宽 W × 高 H 的明度系数表），
    ///   涂地时只做 `tile[(y % H) * W + (x % W)]` 一次查表。
    ///   瓷砖尺寸都是**整数**、而且瓷砖里的图形跨边界完全连续，所以铺出来没有接缝。
    /// ★ 不在热路径里算几何：一张 135×135 的瓷砖也只要 1.8 万次采样，开局烘一次就完了。
    /// </summary>
    public static class TerritoryDeco
    {
        /// <summary>花纹的画法。</summary>
        public enum Kind
        {
            /// <summary>正六边形蜂窝（尖朝上）。</summary>
            Hex = 0,
            /// <summary>抖动 Voronoi —— 不规则的五/六边形。</summary>
            Jittered = 1,
            /// <summary>六边形蜂窝 + 内缩一圈的第二条描边（细密马赛克 / 人字纹）。</summary>
            HexDouble = 2,
            /// <summary>方形细格（正交网格）。</summary>
            Grid = 3,
            /// <summary>六边形蜂窝 + 从中心到顶点的放射线（伞骨）。</summary>
            HexSpokes = 4,
            /// <summary>回形纹（希腊钥匙 / 迷宫）：横竖折线连成一片。</summary>
            Maze = 5,
            /// <summary>人字纹：一行行的「V」锯齿，隔行错半格 —— 像一片箭头马赛克。</summary>
            Chevron = 6,
            /// <summary>六边形 + 三根伞骨（把每个六边形分成三个菱形，像一堆立方体）。</summary>
            HexCube = 7,
        }

        public struct Style
        {
            public Kind Kind;
            /// <summary>特征尺寸（格）：Hex 是「中心到顶点」的半径 R，Jittered 是格距 S。</summary>
            public int R;
            /// <summary>花纹线的宽度（格）。</summary>
            public float Line;
            /// <summary>花纹线的明度系数（1 = 本色，越小越深）。</summary>
            public float Shade;
        }

        /// <summary>
        /// 各阵营的花纹 —— **直接从阵营表来**（`FactionSet.Defs[i].Deco`），
        /// 所以 4 家版和 8 家版各有一整套，切换版本时这里自动跟着换。
        /// ★ 做成**属性**而不是 static readonly 字段：阵营表里存的就是 `Style`，
        ///   两边都是静态初始化，写成字段会有初始化顺序的坑（谁先谁后不确定）。
        /// </summary>
        static Style[] _styles;
        static int _stylesCount = -1;
        public static Style[] Styles
        {
            get
            {
                if (_styles == null || _stylesCount != FactionSet.Count)
                {
                    var defs = FactionSet.Defs;
                    _styles = new Style[defs.Length];
                    for (int i = 0; i < defs.Length; i++) _styles[i] = defs[i].Deco;
                    _stylesCount = defs.Length;
                }
                return _styles;
            }
        }

        static float[][] _tile;      // [阵营][ty * W + tx]，值是明度系数
        static int[] _tw, _th;
        static bool _ready;

        /// <summary>瓷砖宽（格）。</summary>
        public static int TileWidth(int faction) { EnsureReady(); return _tw[Clamp(faction)]; }
        /// <summary>瓷砖高（格）。</summary>
        public static int TileHeight(int faction) { EnsureReady(); return _th[Clamp(faction)]; }

        /// <summary>
        /// 这一格要用哪个明度系数？1 = 正常阵营色，小于 1 = 花纹线。
        /// 热路径：两次取模 + 一次查表。
        /// </summary>
        public static float Shade(int x, int y, int faction)
        {
            EnsureReady();
            int f = Clamp(faction);
            int w = _tw[f], h = _th[f];
            int tx = x % w; if (tx < 0) tx += w;
            int ty = y % h; if (ty < 0) ty += h;
            return _tile[f][ty * w + tx];
        }

        static int Clamp(int f)
        {
            if (f < 0) return 0;
            if (f >= _tile.Length) return _tile.Length - 1;
            return f;
        }

        static void EnsureReady()
        {
            // ★ 阵营数运行时可改：瓷砖缓存按**张数**判断要不要重烘
            if (_ready && _tile != null && _tile.Length == Styles.Length) return;
            _ready = true;

            int n = Styles.Length;
            _tile = new float[n][];
            _tw = new int[n];
            _th = new int[n];
            for (int i = 0; i < n; i++)
            {
                var s = Styles[i];
                if (s.Kind == Kind.Jittered) BuildJittered(s, out _tile[i], out _tw[i], out _th[i]);
                else if (s.Kind == Kind.Grid) BuildGrid(s, out _tile[i], out _tw[i], out _th[i]);
                else if (s.Kind == Kind.Maze) BuildMaze(s, out _tile[i], out _tw[i], out _th[i]);
                else if (s.Kind == Kind.Chevron) BuildChevron(s, out _tile[i], out _tw[i], out _th[i]);
                else BuildHex(s, out _tile[i], out _tw[i], out _th[i]);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 六边形蜂窝
        // ─────────────────────────────────────────────────────────────

        const float SQRT3 = 1.7320508f;
        const float SIN60 = 0.8660254f;

        /// <summary>
        /// 尖朝上的正六边形：列距 = √3·R，行距 = 1.5·R，偶数行横向错开半列。
        /// 瓷砖高度取 **两行**（= 3R），所以「错半列」这件事在瓷砖里正好走完一个周期。
        /// 边界判定用六边形的「三条轴」：`d = max(|dx|, |0.5dx ± 0.866dy|)`，
        /// 六边形的内切半径 a = 0.866R，`d` 越接近 a 就越靠近边界。
        /// </summary>
        static void BuildHex(Style s, out float[] tile, out int W, out int H)
        {
            W = Mathf.Max(6, Mathf.RoundToInt(SQRT3 * s.R));
            H = Mathf.Max(6, 3 * s.R);
            float a = SIN60 * s.R;
            float innerR = a * 0.58f;                    // 内描边（只给 HexDouble 用）
            tile = new float[W * H];

            // ★★ 圆心要**连瓷砖上下邻居那一行一起算**（row = -1 .. 2）。
            //   只算瓷砖自己的两行的话，靠近上下边缘的格子会以为「最近的圆心」在很远的地方，
            //   于是整条边缘被误判成边界、画出一条**伪线** —— 表现出来就是每个行交界处
            //   一大块深色三角（用户第 93 轮：「有些花纹形状不对（大多变成深色了）」）。
            //   行距 1.5R、瓷砖高 3R，所以上一块瓷砖的第 1 行在 y = -0.5R、
            //   下一块的第 0 行在 y = 4R，两行都要算进来。横向同理要 -1..+1 三个副本。
            for (int py = 0; py < H; py++)
            {
                for (int px = 0; px < W; px++)
                {
                    float best = float.MaxValue;
                    // ★ 同时记住"赢的那个圆心"的三条轴距 —— 放射线（HexSpokes）要用：
                    //   某条轴距很小 = 这一点靠近穿过中心的那条线 = 一条伞骨。
                    float b1 = 0f, b2 = 0f, b3 = 0f;
                    float bcx = 0f, bcy = 0f;
                    for (int row = -1; row <= 2; row++)
                    {
                        float cy = s.R + row * 1.5f * s.R + 0.5f;
                        // 行的横向错位：偶数行（含 -2、0、2）错开半列
                        float ox = ((row & 1) == 0) ? 0.5f * W : 0f;
                        for (int j = -1; j <= 1; j++)
                        {
                            float cx = ox + j * W + 0.5f;
                            float dx = px + 0.5f - cx;
                            float dy = py + 0.5f - cy;
                            float a1 = Mathf.Abs(dx);
                            float a2 = Mathf.Abs(0.5f * dx + SIN60 * dy);
                            float a3 = Mathf.Abs(0.5f * dx - SIN60 * dy);
                            float d = Mathf.Max(a1, Mathf.Max(a2, a3));
                            if (d < best) { best = d; b1 = a1; b2 = a2; b3 = a3; bcx = cx; bcy = cy; }
                        }
                    }
                    // ★★ 伞骨要**画满整个半径**（用户 3.jpg 里的紫色那块就是一片连续的三角网：
                    //   每个六边形从中心连到 6 个顶点，相邻六边形的伞骨接在一起 → 整片三角形格子）。
                    //   ★ 历史：第一版只画 0.62a 那一段，出来是"每个六边形中间一个小星号"，
                    //     跟参考图对不上；当时怕"连成整竖线"，但参考图**本来就是连着的**。
                    bool spoke = s.Kind == Kind.HexSpokes
                                 && best < a * 0.95f
                                 && Mathf.Min(b1, Mathf.Min(b2, b3)) < s.Line * 0.75f;

                    // ★★ HexCube（用户 3.jpg 里的青/蓝）：从圆心往**三个顶点**（相差 120°）连线，
                    //   把每个六边形分成**三个菱形**，看上去像一堆立方体。
                    //   用**角度**判：偏离 90°/210°/330° 三条方向线很近就是伞骨。
                    bool cubeSpoke = false;
                    if (s.Kind == Kind.HexCube && best < a * 0.98f)
                    {
                        float ang = Mathf.Atan2(py + 0.5f - bcy, px + 0.5f - bcx) * Mathf.Rad2Deg;
                        float dd = Mathf.Repeat(ang - 90f, 120f);
                        dd = Mathf.Min(dd, 120f - dd);
                        cubeSpoke = dd < (s.Line / Mathf.Max(a, 0.001f)) * 57.2958f * 1.2f;
                    }

                    float v = 1f;
                    if (best > a - s.Line) v = s.Shade;                       // 外轮廓
                    else if (s.Kind == Kind.HexDouble
                             && Mathf.Abs(best - innerR) < s.Line * 0.55f)    // 内缩的第二圈
                        v = s.Shade;
                    else if (s.Kind == Kind.HexSpokes && spoke)              // 三条放射线
                        v = s.Shade;
                    else if (cubeSpoke)                                      // 立方体的三条棱
                        v = s.Shade;
                    tile[py * W + px] = v;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 方形细格（正交网格）
        // ─────────────────────────────────────────────────────────────

        /// <summary>正交网格：每 `R` 格一条线。瓷砖就是 R×R，天然无缝。</summary>
        static void BuildGrid(Style s, out float[] tile, out int W, out int H)
        {
            W = H = Mathf.Max(6, s.R);
            tile = new float[W * H];
            for (int py = 0; py < H; py++)
            {
                for (int px = 0; px < W; px++)
                {
                    // 到最近的竖线 / 横线的距离（点取格心）
                    float dx = Mathf.Min(px + 0.5f, W - (px + 0.5f));
                    float dy = Mathf.Min(py + 0.5f, H - (py + 0.5f));
                    tile[py * W + px] = (dx < s.Line || dy < s.Line) ? s.Shade : 1f;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 回形纹（每格套一个方框，隔行错半格）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 回形纹（希腊钥匙 / 迷宫，用户 3.jpg 里的橙色那块）：横竖折线连成一片，
        /// 每格（`R` 见方）里是一条「⊐」形折线 + 下面一条横线，横向再错半格 ——
        /// 拼起来就是连续的钥匙纹。
        /// 瓷砖取 <b>2R × R</b>（横向两格走完错位周期；纵向一格就重复）。
        /// </summary>
        static void BuildMaze(Style s, out float[] tile, out int W, out int H)
        {
            int R = Mathf.Max(6, s.R);
            W = 2 * R; H = R;
            tile = new float[W * H];
            float r = R;
            for (int py = 0; py < H; py++)
            {
                for (int px = 0; px < W; px++)
                {
                    float x = px + 0.5f, y = py + 0.5f;
                    float ph = Mathf.Repeat(x, 2f * r);        // 横向两格一个周期
                    float yy = Mathf.Repeat(y, r);             // 纵向一格一个周期
                    // 两条横线：上面那条只铺 1.4R 长（右头留口），下面那条铺满
                    bool hline = (Mathf.Abs(yy - 0.15f * r) < s.Line && ph < 1.4f * r)
                              || (Mathf.Abs(yy - 0.90f * r) < s.Line);
                    // 两条竖线：左边那根从下往上、右边那根从上往下
                    bool vline = (Mathf.Abs(ph - 0.15f * r) < s.Line && yy < 0.90f * r)
                              || (Mathf.Abs(ph - 1.35f * r) < s.Line && yy > 0.15f * r);
                    tile[py * W + px] = (hline || vline) ? s.Shade : 1f;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 人字纹（箭头马赛克）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 人字纹（用户 3.jpg 里的绿色那块）：一行行「V」形锯齿，
        /// **偶数行的斜线朝「\」、奇数行的朝「/」**，于是每一行都是一条折线，
        /// 上下两行拼在一起就是一片箭头 / 人字。
        /// 瓷砖取 <b>2R × 2R</b>（**两行** —— 一行高的瓷砖里全是同一奇偶，人字就没了）。
        /// </summary>
        static void BuildChevron(Style s, out float[] tile, out int W, out int H)
        {
            int R = Mathf.Max(4, s.R);
            W = H = 2 * R;
            tile = new float[W * H];
            float r = R;
            for (int py = 0; py < H; py++)
            {
                for (int px = 0; px < W; px++)
                {
                    float x = px + 0.5f, y = py + 0.5f;
                    int row = Mathf.FloorToInt(y / r);
                    // ★ 偶数行用 (y+x)、奇数行用 (y−x) —— 行与行方向相反才是"人字"。
                    //   取模 R 得到「到最近那条 45° 斜线的距离 × √2」。
                    float u = ((row & 1) == 0) ? (y + x) : (y - x);
                    float d = Mathf.Abs(Mathf.Repeat(u, r)) / 1.41421356f;
                    tile[py * W + px] = d < s.Line ? s.Shade : 1f;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 不规则（抖动 Voronoi）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 把 3×3 个种子点各在自己的格子里随机挪一点，再取 Voronoi —— 出来的就是
        /// 大小不一的**五边形 / 六边形**（正是参考图里绿色那块的样子）。
        /// 瓷砖正好装下 3×3 个格子，种子按 (i mod 3, j mod 3) 哈希，
        /// 所以跨瓷砖边界取到的是**同一个种子**，铺出来不会有接缝。
        /// </summary>
        static void BuildJittered(Style s, out float[] tile, out int W, out int H)
        {
            const int N = 3;
            W = H = Mathf.Max(9, N * s.R);
            tile = new float[W * H];

            var sx = new float[N * N];
            var sy = new float[N * N];
            for (int j = 0; j < N; j++)
            {
                for (int i = 0; i < N; i++)
                {
                    Hash2(i, j, out float hx, out float hy);
                    sx[j * N + i] = (i + 0.5f + (hx - 0.5f) * 0.62f) * s.R;
                    sy[j * N + i] = (j + 0.5f + (hy - 0.5f) * 0.62f) * s.R;
                }
            }

            for (int py = 0; py < H; py++)
            {
                for (int px = 0; px < W; px++)
                {
                    float x = px + 0.5f, y = py + 0.5f;
                    float d1 = float.MaxValue, d2 = float.MaxValue;
                    for (int j = -1; j <= N; j++)
                    {
                        for (int i = -1; i <= N; i++)
                        {
                            // ★ 这里必须是**向下取整**的除法：i = -1 时要挪到「左边那一块瓷砖」，
                            //   而 C# 的整数除法是向零取整（-1/3 == 0），直接用会把邻居错放到本瓷砖里。
                            int cell = Mod(j, N) * N + Mod(i, N);
                            float cx = sx[cell] + Mathf.Floor((float)i / N) * W;
                            float cy = sy[cell] + Mathf.Floor((float)j / N) * H;
                            float dx = x - cx, dy = y - cy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d < d1) { d2 = d1; d1 = d; }
                            else if (d < d2) { d2 = d; }
                        }
                    }
                    tile[py * W + px] = (d2 - d1) < s.Line ? s.Shade : 1f;
                }
            }
        }

        static int Mod(int v, int m) { int r = v % m; return r < 0 ? r + m : r; }

        static void Hash2(int a, int b, out float x, out float y)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
                h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
                x = (h & 0xFFFF) / 65535f;
                y = ((h >> 16) & 0xFFFF) / 65535f;
            }
        }
    }
}
