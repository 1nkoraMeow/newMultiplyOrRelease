using System;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 右栏战场：**1024×1024 像素**的涂色地图，一个像素数值为 1。
    /// 整张地图就是一张 Texture2D + 一个 SpriteRenderer，染色只改 texel，
    /// 所以百万像素也只有 1 个 DrawCall。
    ///
    /// 性能要点：
    ///   · 每帧只把「这一帧真正改动到的矩形区域」上传到 GPU（脏矩形），
    ///     而不是整张 4MB 传一遍 —— 这是百万像素地图能跑起来的关键。
    ///   · 上传缓冲区复用，不在热路径里分配内存。
    ///   · 内部全用扁平数组和整数下标，不做 Vector2 装箱。
    /// </summary>
    public class Battlefield : MonoBehaviour
    {
        public int Texels { get; private set; }
        public float WorldSize { get; private set; }
        public float TexelsPerUnit { get { return Texels / WorldSize; } }
        public int TotalPixels { get { return Texels * Texels; } }

        // ★ 直接写进**贴图自己的原生缓冲**（GetRawTextureData），
        //   这样刷地不用再 SetPixels32 拷一遍 —— 一张 1024² 贴图的整份拷贝
        //   加 Apply 是之前掉帧的主要来源之一。
        Unity.Collections.NativeArray<Color32> _px;
        byte[] _owner;                 // 0 = 中立，1..N = 阵营 Index + 1
        int[] _pixelsPerFaction;

        Texture2D _tex;
        SpriteRenderer _renderer;

        // ── 脏块跟踪 ──
        // 整张图切成一堆 BlockSize×BlockSize 的块，染色时只标脏所在的块。
        // 上传时只推送脏块 —— 四个阵营分散在地图四角时，这能避免每帧
        // 因为「包围盒横跨整张图」而白推 4MB。
        bool _dirty;

        /// <summary>最近一帧真正推给 GPU 的像素数（给统计面板看）。</summary>
        public int LastUploadPixels { get; private set; }

        /// <summary>地图贴图（自检要看花纹用）。</summary>
        public Texture2D Texture { get { return _tex; } }

        public void Build(int texels, float worldSize, int factionCount)
        {
            Texels = texels;
            WorldSize = worldSize;

            _owner = new byte[texels * texels];
            _pixelsPerFaction = new int[factionCount];

            _tex = new Texture2D(texels, texels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                name = "battlefield_tex"
            };
            _px = _tex.GetRawTextureData<Color32>();

            Color32 empty = Palette.EmptyTile;
            Color32 grid = Palette.GridLine;
            int every = Mathf.Max(Cfg.MapGridEvery, 2);
            for (int y = 0; y < texels; y++)
            {
                int row = y * texels;
                bool gy = (y % every) == 0;
                for (int x = 0; x < texels; x++)
                    _px[row + x] = (gy || (x % every) == 0) ? grid : empty;
            }
            _tex.Apply(false, false);

            float ppu = texels / worldSize;
            var sprite = Sprite.Create(_tex, new Rect(0, 0, texels, texels), new Vector2(0.5f, 0.5f), ppu);
            sprite.name = "battlefield_sprite";

            _renderer = Primitives.MakeNode("Battlefield", sprite, Color.white, -100, transform);
            _renderer.transform.position = new Vector3(worldSize * 0.5f, worldSize * 0.5f, 0f);
            _renderer.transform.localScale = Vector3.one;

            BuildBounds();
            _dirty = true;
        }

        // ★ 世界会整棵重建（设置面板 → 换阵营数 / 换颜色）：
        //   贴图和 Sprite 都是运行时 new 出来的，GameObject 销毁不会带走它们。
        //   一张 1024² RGBA 贴图 = 4 MB，不手动 Destroy 的话每重开一局就漏一张。
        void OnDestroy()
        {
            if (_renderer != null && _renderer.sprite != null) Destroy(_renderer.sprite);
            if (_tex != null) Destroy(_tex);
        }

        void BuildBounds()
        {
            const float t = Cfg.BattleBoundThickness;            float w = WorldSize;
            MakeWall("BoundBottom", new Vector2(w * 0.5f, -t * 0.5f), new Vector2(w + t * 2f, t));
            MakeWall("BoundTop", new Vector2(w * 0.5f, w + t * 0.5f), new Vector2(w + t * 2f, t));
            MakeWall("BoundLeft", new Vector2(-t * 0.5f, w * 0.5f), new Vector2(t, w + t * 2f));
            MakeWall("BoundRight", new Vector2(w + t * 0.5f, w * 0.5f), new Vector2(t, w + t * 2f));
        }

        void MakeWall(string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            col.sharedMaterial = Physics2DConfig.Wall;

            // ★ 规格：墙不能只有判定、看不见 —— 补上可见的灰色墙体
            // ★ 规格：墙要在子弹拖尾上面 —— 图层收进 Cfg（见 BattleWallSortingOrder）
            var sr = Primitives.MakeNode("Body", Primitives.Square,
                new Color(0.62f, 0.62f, 0.65f), Cfg.BattleWallSortingOrder, go.transform);
            sr.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        /// <summary>边界墙的厚度（世界单位），相机取景要把它算进去。</summary>
        public const float BoundThickness = Cfg.BattleBoundThickness;

        // ─────────────────────────────────────────────────────────────
        // 查询
        // ─────────────────────────────────────────────────────────────

        public int PixelsOf(int factionIndex)
        {
            if (_pixelsPerFaction == null || factionIndex < 0 || factionIndex >= _pixelsPerFaction.Length) return 0;
            return _pixelsPerFaction[factionIndex];
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Texels && y < Texels;
        }

        public int OwnerAt(Vector2 world)
        {
            int x = Mathf.FloorToInt(world.x * TexelsPerUnit);
            int y = Mathf.FloorToInt(world.y * TexelsPerUnit);
            return OwnerAtTexel(x, y);
        }

        /// <summary>按**格子**查归属（-1 = 无主）。近防炮吸能要逐格扫，走这个口子省掉坐标换算。</summary>
        public int OwnerAtTexel(int x, int y)
        {
            if (!InBounds(x, y)) return -1;
            byte o = _owner[y * Texels + x];
            return o == 0 ? -1 : o - 1;
        }

        /// <summary>
        /// 这一格是不是**自己家**的（O(1) 查表）。
        /// 子弹靠它跳过「飞在自家领土上」的那些帧 —— 在自家地上刷本来也刷不出东西，
        /// 却照样要占刷地预算。不挡掉的话，一旦某家领土做大、大部分子弹都在自家地上飞，
        /// 真正该刷地的那几颗就会被稀释到几乎轮不上，看起来就是「子弹不涂地了」。
        /// </summary>
        public bool OwnedBy(Vector2 world, int factionIndex)
        {
            if (factionIndex < 0 || factionIndex >= Cfg.FactionCount) return false;
            int x = Mathf.FloorToInt(world.x * TexelsPerUnit);
            int y = Mathf.FloorToInt(world.y * TexelsPerUnit);
            if (!InBounds(x, y)) return false;
            return _owner[y * Texels + x] == (byte)(factionIndex + 1);
        }

        // ─────────────────────────────────────────────────────────────
        // 染色
        // ─────────────────────────────────────────────────────────────

        /// <summary>标脏。像素已经直接写进贴图缓冲了，这里只留一个「待上传」标志。</summary>
        void MarkBlock(int x0, int y0, int x1, int y1)
        {
            _dirty = true;
        }

        byte[] _factionBytes;

        /// <summary>阵营色的字节缓存（每帧上万次刷地，别每次都 Palette.ColorOf + 取整）。</summary>
        void BuildFactionBytes()
        {
            int n = Cfg.FactionCount;
            _factionBytes = new byte[n * 3];
            for (int i = 0; i < n; i++)
            {
                Color c = Palette.ColorOf(i);
                _factionBytes[i * 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                _factionBytes[i * 3 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                _factionBytes[i * 3 + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            }
        }

        /// <summary>
        /// 以 world 为圆心、radiusUnits 米为半径刷地。
        /// 返回「本次新染到的像素数」——调用方按「1 像素 = 1 数值」扣大球。
        /// </summary>
        public int Paint(Vector2 world, float radiusUnits, int factionIndex)
        {
            return Paint(world, radiusUnits, factionIndex, 1f, Vector2.right);
        }

        /// <param name="crossScale">笔刷**垂直于运动方向**再拉宽多少倍（1 = 正圆）。</param>
        /// <param name="dir">运动方向 —— 决定「横着」是哪一边。</param>
        public int Paint(Vector2 world, float radiusUnits, int factionIndex,
                         float crossScale, Vector2 dir)
        {
            return Paint(world, radiusUnits, factionIndex, crossScale, dir, 0);
        }

        /// <param name="maxNew">最多允许**新涂**几格（0 = 不限）。理由同 <see cref="PaintLine"/>：
        /// 先涂再扣的话，多涂的那些格子是白送的。大球也必须按「自己买得起几格」来涂。</param>
        public int Paint(Vector2 world, float radiusUnits, int factionIndex,
                         float crossScale, Vector2 dir, int maxNew)
        {
            if (maxNew == 0) maxNew = int.MaxValue;
            if (crossScale < 1f) crossScale = 1f;
            Vector2 u = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            Vector2 v = new Vector2(-u.y, u.x);          // 横向单位向量
            float cx = world.x * TexelsPerUnit;
            float cy = world.y * TexelsPerUnit;
            float rT = radiusUnits * TexelsPerUnit;
            if (rT < 0.5f) rT = 0.5f;

            int r = Mathf.CeilToInt(rT);
            int ix = Mathf.RoundToInt(cx);
            int iy = Mathf.RoundToInt(cy);

            int x0 = ix - r, x1 = ix + r;
            int y0 = iy - r, y1 = iy + r;
            if (x1 < 0 || y1 < 0 || x0 >= Texels || y0 >= Texels) return 0;
            int pad0 = Mathf.CeilToInt(rT * (crossScale - 1f)) + 1;
            if (x0 < -pad0) x0 = -pad0;
            if (y0 < -pad0) y0 = -pad0;
            if (x1 > Texels - 1 + pad0) x1 = Texels - 1 + pad0;
            if (y1 > Texels - 1 + pad0) y1 = Texels - 1 + pad0;
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 >= Texels) x1 = Texels - 1;
            if (y1 >= Texels) y1 = Texels - 1;

            byte want = (byte)(factionIndex + 1);
            int newly = 0;
            float rr = rT * rT;

            // 整个圆覆盖到的块一次性标脏（不是按像素来）
            MarkBlock(x0, y0, x1, y1);

            if (_factionBytes == null) BuildFactionBytes();
            byte cr = _factionBytes[factionIndex * 3];
            byte cg = _factionBytes[factionIndex * 3 + 1];
            byte cb = _factionBytes[factionIndex * 3 + 2];

            // 椭圆判定：把偏移分解成「顺着运动」和「横着」两个分量，横向再按 crossScale 缩回来
            float invCross2 = 1f / (crossScale * crossScale);
            int pad = Mathf.CeilToInt(rT * crossScale) + 1;      // 横向拉宽了，扫描范围也要跟着放宽
            for (int y = Mathf.Max(y0 - (pad - r), 0); y <= Mathf.Min(y1 + (pad - r), Texels - 1); y++)
            {
                float dy = y + 0.5f - cy;
                int rowBase = y * Texels;
                for (int x = Mathf.Max(x0 - (pad - r), 0); x <= Mathf.Min(x1 + (pad - r), Texels - 1); x++)
                {
                    float dx = x + 0.5f - cx;
                    float along = dx * u.x + dy * u.y;
                    float cross = dx * v.x + dy * v.y;
                    float d2 = along * along + cross * cross * invCross2;
                    if (Cfg.BrushEdgeNoise > 0f)
                    {
                        float edge = rr + (Hash01(x, y) - 0.5f) * 2f * Cfg.BrushEdgeNoise * rr;
                        if (d2 > edge) continue;
                    }
                    else if (d2 > rr) continue;

                    int i = rowBase + x;
                    byte have = _owner[i];
                    if (have == want) continue;
                    if (maxNew <= 0) return newly;          // ★ 数值买不起更多格了
                    maxNew--;

                    if (have != 0) _pixelsPerFaction[have - 1]--;
                    _owner[i] = want;
                    _pixelsPerFaction[factionIndex]++;
                    newly++;

                    // 颜色 = 阵营色 ×（网格线 / 花纹）
                    float k = 1f;
                    if (IsGrid(x, y)) k = 0.86f;
                    // ★ 花纹是**按阵营**的（用户第 93 轮：四个阵营的花纹不一样）
                    if (Cfg.TerritoryDecoEnable) k *= TerritoryDeco.Shade(x, y, factionIndex);

                    Color32 target = new Color32(
                        Clamp255(cr * k), Clamp255(cg * k), Clamp255(cb * k), 255);

                    // ★ 边缘抗锯齿：最外一圈按「覆盖率」和原来的颜色混一下 ——
                    //   归属判定仍然是硬边（一格要么是你的要么不是），但**画出来**的边界不再是锯齿。
                    //   （用户：「涂色感觉很不平滑，看着难受」。）
                    // 规格：最正常的涂色 —— 不混色、不留描边（TerritoryEdgeAA = 0 时整段跳过）
                    if (Cfg.TerritoryEdgeAA <= 0.0001f) { _px[i] = target; continue; }
                    float cov = Mathf.Clamp01((rT - Mathf.Sqrt(d2)) / Cfg.TerritoryEdgeAA + 0.5f);
                    if (cov < 0.995f)
                    {
                        Color32 prev = _px[i];     // 地图是不透明的，直接混 RGB
                        target = new Color32(
                            (byte)Mathf.Lerp(prev.r, target.r, cov),
                            (byte)Mathf.Lerp(prev.g, target.g, cov),
                            (byte)Mathf.Lerp(prev.b, target.b, cov),
                            255);
                    }
                    _px[i] = target;
                }
            }
            return newly;
        }

        /// <summary>
        /// ★ **格子检测**模型（规格：不是「子弹盖印章」，而是「领土格子自己判定」）：
        /// 把子弹这一帧走过的那条线段逐格标上阵营色。
        ///
        /// 和圆盘印章的区别很关键：
        ///   · 印章是「以子弹为圆心盖一个圆盘」—— 圆盘之间会留缝，缝就是用户看到的一片斑点；
        ///   · 这里是「子弹碰到的格子自己变色」—— 只要子弹真的划过，格子就一定被标上，
        ///     **连着走就是连着的一条**，不存在缝。
        /// 于是「范围」变成一个很小的整数 `margin`（往外再扩几格），可以砍得很小。
        /// </summary>
        /// <param name="margin">线上每一格往外再扩几格（0 = 只标子弹正压到的格子）。</param>
        public int PaintLine(Vector2 from, Vector2 to, int margin, int factionIndex)
        {
            float ignored;
            return PaintLine(from, to, margin, factionIndex, 0, out ignored);
        }

        /// <param name="maxNew">
        /// ★ 最多允许**新涂**几格（0 = 不限）。子弹/大球必须传「它自己还买得起几格」。
        ///
        /// 为什么必须有这个：以前只按「这一帧走过的那条线」一次涂完，再拿涂出来的格数去扣数值 ——
        /// 但格子在扣之前**已经写进地图了**。于是一颗只带 10 点数值的子弹，
        /// 这一帧可以涂掉五六十格（几百点数值），多出来的全是**白送**的。
        /// 这就是「60 万数值就把整张图刷满」（本该要 8388608）的原因。
        /// 有了上限，**涂出去的格数 × PixelCaptureCost ≤ 消耗的数值**，账就平了。
        /// </param>
        /// <param name="reachT">
        /// ★ 这一帧**实际涂到线段的哪个位置**（0..1）。额度用光时它会 &lt; 1 ——
        ///   调用方要把子弹的 `Pos` 收到这里，否则子弹（和拖尾的头）会停在
        ///   它**涂出来的领土前面**，看起来像「死了还往前冲一段」。
        /// </param>
        public int PaintLine(Vector2 from, Vector2 to, int margin, int factionIndex, int maxNew,
                             out float reachT)
        {
            reachT = 1f;
            if (_px == null) return 0;
            if (maxNew == 0) maxNew = int.MaxValue;
            if (_factionBytes == null) BuildFactionBytes();
            byte want = (byte)(factionIndex + 1);
            byte cr = _factionBytes[factionIndex * 3];
            byte cg = _factionBytes[factionIndex * 3 + 1];
            byte cb = _factionBytes[factionIndex * 3 + 2];

            float dx = to.x - from.x, dy = to.y - from.y;
            float lenTex = Mathf.Sqrt(dx * dx + dy * dy) * TexelsPerUnit;

            // ★ 采样间距：每格都采样是**没必要**的 —— 每个采样点标的是 (2m+1)² 的方块，
            //   只要相邻采样点的间距 ≤ 2m+1，这些方块就一定连得上、中间不会有缝。
            //   取 2m（比 2m+1 再保守一格，防整数取整留下缝）→ margin=1 时就是**每 2 格采一次**，
            //   采样数直接减半。
            int spacing = Mathf.Max(1, 2 * margin);
            int steps = Mathf.CeilToInt(lenTex / spacing);
            if (steps < 1) steps = 1;

            int newly = 0;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float wx = from.x + dx * t, wy = from.y + dy * t;
                int ix = Mathf.RoundToInt(wx * TexelsPerUnit);
                int iy = Mathf.RoundToInt(wy * TexelsPerUnit);
                newly += MarkBox(ix, iy, margin, want, factionIndex, cr, cg, cb, ref maxNew);
                if (maxNew <= 0)                        // 买不起了，剩下的不涂
                {
                    reachT = steps > 0 ? (float)i / steps : 0f;
                    break;
                }
            }
            return newly;
        }

        /// <summary>把 (ix,iy) 周围 (2*margin+1)² 个格子标成该阵营。返回新标上的格数。</summary>
        int MarkBox(int ix, int iy, int margin, byte want, int factionIndex,
                    byte cr, byte cg, byte cb, ref int budget)
        {
            int x0 = Mathf.Max(ix - margin, 0), x1 = Mathf.Min(ix + margin, Texels - 1);
            int y0 = Mathf.Max(iy - margin, 0), y1 = Mathf.Min(iy + margin, Texels - 1);
            int newly = 0;
            int every = Cfg.MapGridEvery;
            for (int y = y0; y <= y1; y++)
            {
                int rowBase = y * Texels;
                // ★ 网格线的 y 分量**在行外算一次** —— 原来每个格子都要取一次模
                bool gy = every >= 2 && (y % every) == 0;
                for (int x = x0; x <= x1; x++)
                {
                    int i = rowBase + x;
                    if (_owner[i] == want) continue;        // 这一格已经是我们的 —— 不用动
                    if (budget <= 0) return newly;          // ★ 数值买不起更多格了
                    budget--;
                    byte have = _owner[i];
                    if (have != 0) _pixelsPerFaction[have - 1]--;
                    _owner[i] = want;
                    _pixelsPerFaction[factionIndex]++;
                    newly++;

                    float k = 1f;
                    if (gy || (every >= 2 && (x % every) == 0)) k = 0.86f;   // 网格线
                    if (Cfg.TerritoryDecoEnable) k *= TerritoryDeco.Shade(x, y, factionIndex);   // 花纹
                    _px[i] = new Color32(Clamp255(cr * k), Clamp255(cg * k), Clamp255(cb * k), 255);
                }
            }
            if (newly > 0) MarkBlock(x0, y0, x1, y1);
            return newly;
        }

        /// <summary>把「底色 × 系数」夹成 0..255 的字节。三条涂色路径共用这一个口子。</summary>
        static byte Clamp255(float v)
        {
            int n = Mathf.RoundToInt(v);
            return (byte)(n < 0 ? 0 : (n > 255 ? 255 : n));
        }

        static bool IsGrid(int x, int y)
        {
            int every = Cfg.MapGridEvery;
            if (every < 2) return false;
            return (x % every) == 0 || (y % every) == 0;
        }

        static float Hash01(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        void SetPixel(int x, int y, int factionIndex)
        {
            int i = y * Texels + x;
            byte want = (byte)(factionIndex + 1);
            byte have = _owner[i];
            if (have == want) return;
            if (have != 0) _pixelsPerFaction[have - 1]--;
            _owner[i] = want;
            _pixelsPerFaction[factionIndex]++;

            // ★★ 第 93 轮（用户：「**初始领土上没有花纹**」）—— 根因就在这几行：
            //   阵营色的 HSV 明度是 **1.5**（超亮），黄/红/蓝这些色的某些通道本来就 &gt; 1.0。
            //   以前写的是 `c.r * k * 255` —— **先乘 k 再夹到 255**，于是
            //   1.5 × 0.84 × 255 = 321 又被夹回 255，花纹**一格都压不下去**。
            //   （另外两条涂色路径读的是已经夹好的 `_factionBytes`，所以它们是对的 ——
            //     症状正好就是「初始领土没花纹、后来长出来的有」。）
            //   现在**统一走 `_factionBytes`**：先把底色夹成显示色，再乘花纹系数。
            if (_factionBytes == null) BuildFactionBytes();
            byte baseR = _factionBytes[factionIndex * 3];
            byte baseG = _factionBytes[factionIndex * 3 + 1];
            byte baseB = _factionBytes[factionIndex * 3 + 2];
            float k = 1f;
            if (IsGrid(x, y)) k = 0.86f;
            if (Cfg.TerritoryDecoEnable) k *= TerritoryDeco.Shade(x, y, factionIndex);
            _px[i] = new Color32(Clamp255(baseR * k), Clamp255(baseG * k), Clamp255(baseB * k), 255);
            MarkBlock(x, y, x, y);
        }

        /// <summary>开局：在每个阵营出生点周围先涂一圈自己的领土。</summary>
        public void PaintStartingTerritory(Vector2 home, int factionIndex, float radiusUnits)
        {
            float cx = home.x * TexelsPerUnit;
            float cy = home.y * TexelsPerUnit;
            float rT = radiusUnits * TexelsPerUnit;

            int r = Mathf.CeilToInt(rT);
            int ix = Mathf.RoundToInt(cx);
            int iy = Mathf.RoundToInt(cy);
            float rr = rT * rT;

            for (int y = iy - r; y <= iy + r; y++)
            {
                if (y < 0 || y >= Texels) continue;
                float dy = y + 0.5f - cy;
                for (int x = ix - r; x <= ix + r; x++)
                {
                    if (x < 0 || x >= Texels) continue;
                    float dx = x + 0.5f - cx;
                    if (dx * dx + dy * dy > rr) continue;
                    SetPixel(x, y, factionIndex);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 上传（脏矩形）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 把这一帧脏掉的块推给 GPU。
        /// 脏块太多（超过 1/4 张图）时就退化成整张推一次，
        /// 免得几百次小 SetPixels32 反而比一次大拷贝慢。
        /// </summary>
        public void Flush()
        {
            long _t0 = Prof.Tick();
            if (!_dirty || _tex == null) return;
            // 像素已经直写进贴图缓冲，这里只需要一次上传（没有 CPU 侧的整张拷贝了）
            _tex.Apply(false, false);
            LastUploadPixels = _px.Length;
            _dirty = false;
            Prof.FlushMs = Prof.Since(_t0);
        }

        void ResetDirty()
        {
            _dirty = false;
        }
    }
}
