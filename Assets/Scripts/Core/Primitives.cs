using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 运行时生成所有用到的图形，不依赖任何美术资源：
    /// 方块 / 实心圆 / 圆环 / 楔形（炮台朝向指示）/ 三角形。
    /// 全部按 1×1 世界单位生成，用 transform.localScale 缩放。
    /// </summary>
    public static class Primitives
    {
        static Sprite _square, _circle, _ring, _wedge, _streak, _comet, _rock, _hat, _hex, _arrow, _shieldDisc, _softRing;
        static Material _spriteMat;

        public static Material SpriteMaterial
        {
            get
            {
                if (_spriteMat == null)
                {
                    Shader sh = Shader.Find("Sprites/Default");
                    if (sh == null) sh = Shader.Find("Unlit/Transparent");
                    if (sh == null) sh = Shader.Find("Unlit/Color");
                    if (sh != null) _spriteMat = new Material(sh) { hideFlags = HideFlags.DontSave };
                }
                return _spriteMat;
            }
        }

        public static Sprite Square
        {
            get
            {
                if (_square == null)
                {
                    var tex = SolidTexture(8, Color.white);
                    _square = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
                    _square.name = "prims_square";
                }
                return _square;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    var tex = CircleTexture(192, 1f);
                    _circle = Sprite.Create(tex, new Rect(0, 0, 192, 192), new Vector2(0.5f, 0.5f), 192f);
                    _circle.name = "prims_circle";
                }
                return _circle;
            }
        }

        public static Sprite Ring
        {
            get
            {
                if (_ring == null)
                {
                    var tex = CircleTexture(192, 0.90f);
                    _ring = Sprite.Create(tex, new Rect(0, 0, 192, 192), new Vector2(0.5f, 0.5f), 192f);
                    _ring.name = "prims_ring";
                }
                return _ring;
            }
        }

        /// <summary>右指的细楔形，枢轴在左侧尖端 —— 用于炮台朝向指示（旋转即可）。</summary>
        public static Sprite Wedge
        {
            get
            {
                if (_wedge == null)
                {
                    const int N = 192;
                    var px = new Color32[N * N];
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            float fx = (x + 0.5f) / N;
                            float fy = (y + 0.5f) / N;
                            float half = 0.17f * fx;              // 从尖端往右逐渐变宽
                            bool inside = fx >= 0f && fx <= 1f && Mathf.Abs(fy - 0.5f) <= half;
                            px[y * N + x] = inside ? new Color32(255, 255, 255, 255)
                                                   : new Color32(255, 255, 255, 0);
                        }
                    }
                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "prims_wedge_tex"
                    };
                    tex.SetPixels32(px);
                    tex.Apply(false, false);

                    _wedge = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0f, 0.5f), (float)N);
                    _wedge.name = "prims_wedge";
                }
                return _wedge;
            }
        }

        /// <summary>
        /// 彗星：**圆头 + 三角形长尾**（视频里增倍球就是这个样子）。
        /// 枢轴放在**头部圆心**，所以把它放在球心、绕 Z 转到速度方向，
        /// 尾巴就自然甩在身后。
        ///
        /// 精灵原始尺寸：长 2.0 × 高 1.0（pixelsPerUnit = 128），
        /// 头部直径 ≈ 0.984，尾尖到头部圆心 ≈ 1.51。
        /// </summary>
        public static Sprite Comet
        {
            get
            {
                if (_comet == null)
                {
                    const int W = 256, H = 128;
                    const float headR = 63f;                       // 头部半径（像素）
                    float hx = W - headR;                          // 头部圆心 x
                    float cy = H * 0.5f;

                    var px = new Color32[W * H];
                    for (int y = 0; y < H; y++)
                    {
                        for (int x = 0; x < W; x++)
                        {
                            float fx = x + 0.5f, fy = y + 0.5f;
                            float a = 0f;

                            // 圆头
                            float dx = fx - hx, dy = fy - cy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            a = Mathf.Clamp01(headR - d + 0.5f);

                            // 三角尾：从头部的上下两个切点收到 (0, cy)
                            if (fx <= hx)
                            {
                                float t = fx / hx;                 // 0 在尾尖，1 在头部
                                float half = headR * t;
                                if (Mathf.Abs(fy - cy) <= half) a = 1f;
                            }

                            px[y * W + x] = new Color32(255, 255, 255,
                                (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                        }
                    }

                    var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "prims_comet_tex"
                    };
                    tex.SetPixels32(px);
                    tex.Apply(false, false);

                    _comet = Sprite.Create(tex, new Rect(0, 0, W, H),
                        new Vector2(hx / W, 0.5f), 128f);
                    _comet.name = "prims_comet";
                }
                return _comet;
            }
        }

        /// <summary>
        /// 设置彗星缩放。rootScale 是父物体的缩放（球体用「直径」当缩放），
        /// headDiameter / tailLength 是**世界单位**。之前这里多乘了一次 4，
        /// 结果拖尾比球还粗、看起来像三角形 —— 已修。
        /// </summary>
        public static void SetComet(Transform t, float rootScale, float headDiameter, float tailLength)
        {
            if (t == null) return;
            float s = Mathf.Max(rootScale, 0.0001f);
            t.localScale = new Vector3(
                Mathf.Max(tailLength, 0.001f) / (1.51f * s),
                Mathf.Max(headDiameter, 0.001f) / (0.984f * s),
                1f);
        }

        /// <summary>
        /// **不规则多边形**（录像里 x16 上方那块阻挡不是规整矩形）。
        /// 半径按角度随机起伏，再用射线法光栅化。确定性种子，每次跑都一样。
        /// </summary>
        public static Sprite Rock
        {
            get
            {
                if (_rock == null)
                {
                    const int N = 256;
                    const int V = 14;                 // 顶点数
                    var rnd = new System.Random(20260103);
                    var verts = new Vector2[V];
                    for (int i = 0; i < V; i++)
                    {
                        float a = Mathf.PI * 2f * i / V + (float)rnd.NextDouble() * 0.16f;
                        float r = 0.72f + (float)rnd.NextDouble() * 0.28f;
                        verts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.38f);
                    }

                    var px = new Color32[N * N];
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            var p = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f);
                            bool inside = false;
                            for (int i = 0, j = V - 1; i < V; j = i++)
                            {
                                if (((verts[i].y > p.y) != (verts[j].y > p.y)) &&
                                    (p.x < (verts[j].x - verts[i].x) * (p.y - verts[i].y) /
                                           (verts[j].y - verts[i].y) + verts[i].x))
                                    inside = !inside;
                            }
                            px[y * N + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                        }
                    }

                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "prims_rock_tex"
                    };
                    tex.SetPixels32(px);
                    tex.Apply(false, false);
                    _rock = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
                    _rock.name = "prims_rock";
                }
                return _rock;
            }
        }

        /// <summary>
        /// **x16 的本体**：录像里它是一块「灰楔形 / 帽子」——
        /// 顶部收成一段窄颈，往下朝两边张开，底边最宽且近乎平。
        /// 尺寸 1×1，底边在下、窄颈在上。
        /// </summary>
        public static Sprite Hat
        {
            get
            {
                if (_hat == null)
                {
                    const int N = 192;
                    var px = new Color32[N * N];
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            float fx = (x + 0.5f) / N;
                            float fy = (y + 0.5f) / N;          // 0 = 底边，1 = 顶
                            // 顶部只占 22% 宽，底部满宽，侧面带一点外凸的弧度
                            float half = 0.5f * (0.22f + 0.78f * Mathf.Pow(1f - fy, 0.5f));
                            // 底部两角收圆一点
                            if (fy < 0.10f)
                                half *= Mathf.Sqrt(Mathf.Clamp01(fy / 0.10f)) * 0.35f + 0.65f;
                            bool inside = Mathf.Abs(fx - 0.5f) <= half;
                            px[y * N + x] = inside ? new Color32(255, 255, 255, 255)
                                                   : new Color32(255, 255, 255, 0);
                        }
                    }
                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "prims_hat_tex"
                    };
                    tex.SetPixels32(px);
                    tex.Apply(false, false);
                    _hat = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
                    _hat.name = "prims_hat";
                }
                return _hat;
            }
        }

        // ── ★ 第一版：x16 阻块的形状（用户给的第二张图，1:1 描出来的）────────────
        //   归一化坐标：u 从左到右 0..1，v 从**上**到下 0..1（和看图的方向一致，
        //   换算到 Unity 的时候再翻 y）。
        //
        //   轮廓要点（都是对着图量的）：上尖（u=0.5）→ v=0.45 处展到满宽 →
        //   两边几乎垂直往下收到 v≈0.70 → 底边正中挖掉一个**梯形缺口**（u 0.32~0.68），
        //   所以底边收成左右两条尖腿。
        public static readonly Vector2[] X16ShapeOutline =
        {
            new Vector2(0.500f, 0.000f),   // 顶点
            new Vector2(1.000f, 0.450f),   // 右肩（最宽）
            new Vector2(0.982f, 0.700f),
            new Vector2(0.683f, 1.000f),   // 右腿尖
            new Vector2(0.671f, 0.780f),   // 缺口右壁顶
            new Vector2(0.328f, 0.780f),   // 缺口左壁顶
            new Vector2(0.317f, 1.000f),   // 左腿尖
            new Vector2(0.018f, 0.700f),
            new Vector2(0.000f, 0.450f),   // 左肩（最宽）
        };

        /// <summary>把归一化轮廓换算成**本地米制**顶点（中心在原点，y 向上）。</summary>
        public static Vector2[] X16ShapePoints(float width, float height)
        {
            var pts = new Vector2[X16ShapeOutline.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 p = X16ShapeOutline[i];
                pts[i] = new Vector2((p.x - 0.5f) * width, (0.5f - p.y) * height);
            }
            return pts;
        }

        static Sprite _x16;

        /// <summary>
        /// x16 阻块那张图（1×1，贴图里已经是上面那个轮廓）。
        /// 用法：`localScale = new Vector3(width, height, 1)` —— 比例就对了。
        /// 零资源：运行时把轮廓**点内填充**烤成贴图。
        /// </summary>
        public static Sprite X16Shape
        {
            get
            {
                if (_x16 == null)
                {
                    const int N = 256;
                    var px = new Color32[N * N];
                    var poly = X16ShapeOutline;
                    // 2×2 超采样做抗锯齿（这些图只在启动时烤一次，多花点 CPU 无所谓）
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            int hit = 0;
                            for (int sy = 0; sy < 2; sy++)
                            {
                                for (int sx = 0; sx < 2; sx++)
                                {
                                    float u = (x + 0.25f + sx * 0.5f) / N;
                                    float v = (y + 0.25f + sy * 0.5f) / N;   // 0 = 底边
                                    if (InsidePolygon(poly, u, v)) hit++;
                                }
                            }
                            byte a = (byte)(hit * 255 / 4);
                            px[y * N + x] = new Color32(255, 255, 255, a);
                        }
                    }
                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "prims_x16_tex"
                    };
                    tex.SetPixels32(px);
                    tex.Apply(false, false);
                    _x16 = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
                    _x16.name = "prims_x16";
                }
                return _x16;
            }
        }

        /// <summary>射线法判点是否在多边形内。<paramref name="v"/> 是**从下往上**的 0..1。</summary>
        static bool InsidePolygon(Vector2[] poly, float u, float v)
        {
            // 轮廓存的是「从上到下」，这里翻成「从下到上」再判，图才不会倒过来
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                float xi = poly[i].x, yi = 1f - poly[i].y;
                float xj = poly[j].x, yj = 1f - poly[j].y;
                if ((yi > v) != (yj > v) && u < (xj - xi) * (v - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>**六边形**（示意图里那两排固定阻挡球是六边形，不是圆）。1×1。</summary>
        public static Sprite Hex
        {
            get
            {
                if (_hex == null)
                {
                    const int N = 192;
                    var px = new Color32[N * N];
                    // 平顶六边形：上下两条水平边，左右两个尖角
                    float sin60 = 0.8660254f;
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            float px_ = (x + 0.5f) / N - 0.5f;
                            float py_ = (y + 0.5f) / N - 0.5f;
                            float ax = Mathf.Abs(px_), ay = Mathf.Abs(py_);
                            bool inside = ay <= 0.5f * sin60 && ax <= 0.5f
                                          && (ax + ay * 0.5774f) <= 0.5f + 0.0001f;
                            px[y * N + x] = inside ? new Color32(255, 255, 255, 255)
                                                   : new Color32(255, 255, 255, 0);
                        }
                    }
                    // ★ 用 Point：六边形缩到屏幕上只有 ~20px，双线性插值会把六个角糊成圆
                    //   （你会看成「圆的而不是六角形」）。钉子是静止的，Point 不会有抖动。
                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "prims_hex_tex" };
                    tex.SetPixels32(px); tex.Apply(false, false);
                    _hex = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
                    _hex.name = "prims_hex";
                }
                return _hex;
            }
        }

        /// <summary>**向上的箭头**（浮力区那一排）。1×1，枢轴居中。</summary>
        public static Sprite Arrow
        {
            get
            {
                if (_arrow == null)
                {
                    const int W = 48, H = 96;
                    var px = new Color32[W * H];
                    for (int y = 0; y < H; y++)
                    {
                        for (int x = 0; x < W; x++)
                        {
                            float fx = (x + 0.5f) / W - 0.5f;
                            float fy = (y + 0.5f) / H;            // 0 = 底，1 = 顶
                            float shaftW = 0.10f;
                            bool inside;
                            if (fy >= 0.45f)
                            {
                                // 箭头头部：越靠上越窄
                                float t = (1f - fy) / 0.55f;      // 0 在顶尖，1 在头底
                                inside = Mathf.Abs(fx) <= 0.50f * t;
                            }
                            else
                            {
                                inside = Mathf.Abs(fx) <= shaftW;
                            }
                            px[y * W + x] = inside ? new Color32(255, 255, 255, 255)
                                                   : new Color32(255, 255, 255, 0);
                        }
                    }
                    var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "prims_arrow_tex" };
                    tex.SetPixels32(px); tex.Apply(false, false);
                    _arrow = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), (float)H);
                    _arrow.name = "prims_arrow";
                }
                return _arrow;
            }
        }

        /// <summary>
        /// **护盾的纹理**：中心纯白、往外渐暗，最外圈暗到 `Cfg.ShieldEdgeBrightness`。
        /// 用阵营色一染，就是「亮心 + 深边」的护盾球。
        /// </summary>
        public static Sprite ShieldDisc
        {
            get
            {
                if (_shieldDisc == null)
                {
                    const int N = 256;
                    var px = new Color32[N * N];
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            float dx = (x + 0.5f) / N - 0.5f;
                            float dy = (y + 0.5f) / N - 0.5f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;   // 0 中心，1 边缘
                            // 中心 0.34 以内纯白 → 到 0.92 渐变成「深边」→ 0.92~1 保持深边
                            // ★ 深边的**亮度**由 Cfg.ShieldEdgeBrightness 决定（不写死成纯黑，
                            //   否则护盾外面就像套了一圈黑框）。
                            float edgeK = Mathf.Clamp01(Cfg.ShieldEdgeBrightness);
                            float k;
                            if (d <= 0.28f) k = 1f;
                            else if (d >= 0.95f) k = edgeK;
                            else k = 1f - (1f - edgeK) * Mathf.SmoothStep(0f, 1f, (d - 0.28f) / 0.67f);

                            // 羽化：最外 22% 用 alpha 渐隐，别做成一个硬边圆。
                            // ★ 注意 Mathf.SmoothStep(from,to,t) 是**在 from~to 之间插值**，
                            //   不是「把 t 映射到 0~1」，所以这里必须先 InverseLerp
                            //   —— 之前写错，alpha 恒等于 ~0.1，护盾整个看不见了。
                            // 外围羽化：只在最外圈淡出。
                            // ★ 以前是从 **0.55** 就开始淡出，alpha 到 0.775 只剩一半、
                            //   到边缘已经是 0 —— 于是「护盾纹理半径明显小于护盾实际半径」
                            //   （判定用的是 ShieldRadius，画出来只有它的七八成）。
                            //   现在 0.90 之前都是实心，最后 10% 才羽化，纹理半径 = 实际半径。
                            // ★ 带宽和**判定那边共用** Cfg.ShieldFeatherBand：
                            //   纹理羽化到哪儿，真护盾就软到哪儿，两边不会各说各话。
                            float a = 1f - Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(1f - Cfg.ShieldFeatherBand, 1f, d));
                            byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(k) * 255f);
                            px[y * N + x] = new Color32(v, v, v,
                                (byte)Mathf.RoundToInt(a * 255f));
                        }
                    }
                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "prims_shield_tex" };
                    tex.SetPixels32(px); tex.Apply(false, false);
                    _shieldDisc = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
                    _shieldDisc.name = "prims_shield";
                }
                return _shieldDisc;
            }
        }

        /// <summary>**软边圆环**（护盾外圈用）：内外两侧都是羽化的，不是硬边环。</summary>
        public static Sprite SoftRing
        {
            get
            {
                if (_softRing == null)
                {
                    const int N = 256;
                    var px = new Color32[N * N];
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            float dx = (x + 0.5f) / N - 0.5f;
                            float dy = (y + 0.5f) / N - 0.5f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                            // 峰值在 0.96（≈ 真实半径），内外各羽化 0.06 ——
                            // 以前峰值 0.90、羽化 0.16，亮边整个都缩在真实半径里面。
                            float a = 1f - Mathf.Clamp01(Mathf.Abs(d - 0.96f) / 0.06f);
                            a = a * a * (3f - 2f * a);
                            byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                            px[y * N + x] = new Color32(255, 255, 255, v);
                        }
                    }
                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "prims_softring_tex" };
                    tex.SetPixels32(px); tex.Apply(false, false);
                    _softRing = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
                    _softRing.name = "prims_softring";
                }
                return _softRing;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 炮台外形（第 101 轮）
        // ─────────────────────────────────────────────────────────────
        //
        // 图一里的炮台是**一条闭合的轮廓线**：一个圆身子 + 一个贴在边上的小圆疙瘩，
        // 中间是**收进去的一个"脖子"**，不是两个圈叠在一起。
        // ★ 第一版我用「本体圆环 + 炮管圆环」两层叠，用户当场就看出来了：
        //   「炮台怎么变成俩圆了」—— 两个环相交处会留下两条内弧，看着就是两个圆。
        // 正确做法：把「大圆 ∪ 小圆」这个**并集**当成一个形状，
        //   场函数 `f = max(大圆内距, 小圆内距)`，`f = 0` 就是并集的边界；
        //   `|f|` 小的地方是轮廓线，`f` 大的地方是内部。

        /// <summary>机身圆半径（单位 = 炮台半径）。</summary>
        const float TurretBodyR = 1f;
        /// <summary>炮管圆头中心离炮台中心的距离（单位 = 炮台半径）。</summary>
        const float TurretMuzzleDist = 0.95f;
        /// <summary>炮管圆头半径（单位 = 炮台半径）。</summary>
        const float TurretMuzzleR = 0.55f;
        /// <summary>轮廓线宽（单位 = 炮台半径）。</summary>
        const float TurretOutlineW = 0.16f;
        /// <summary>贴图覆盖 [-TurretSpan, TurretSpan]²（单位 = 炮台半径）。</summary>
        const float TurretSpan = 1.5f;

        /// <summary>并集形状的场函数：内部为「到边界的正距离」，外部为负。</summary>
        static float TurretShapeField(float u, float v)
        {
            float inner = TurretBodyR - Mathf.Sqrt(u * u + v * v);
            float du = u - TurretMuzzleDist;
            float outer = TurretMuzzleR - Mathf.Sqrt(du * du + v * v);
            return Mathf.Max(inner, outer);
        }

        static Sprite _turretOutline, _turretFill;

        /// <summary>
        /// 炮台的**轮廓线**（一整条闭合曲线，用黑色染）。★ 缩放 = 炮台半径
        /// （贴图正好横跨 2×`TurretSpan` 个炮台半径）。
        /// </summary>
        public static Sprite TurretOutline
        {
            get
            {
                if (_turretOutline == null) _turretOutline = BuildTurretSprite(true, "prims_turretshape");
                return _turretOutline;
            }
        }

        /// <summary>炮台的**填充**（并集内部，用阵营色的低透明度染）。缩放同 <see cref="TurretOutline"/>。</summary>
        public static Sprite TurretFill
        {
            get
            {
                if (_turretFill == null) _turretFill = BuildTurretSprite(false, "prims_turretfill");
                return _turretFill;
            }
        }

        static Sprite BuildTurretSprite(bool outline, string name)
        {
            const int N = 256;
            var px = new Color32[N * N];
            // 1 像素对应的"场"宽度（场是以炮台半径为单位的距离）
            float ppu = N / (2f * TurretSpan);
            float aa = 1.5f / ppu;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float u = ((x + 0.5f) / N - 0.5f) * 2f * TurretSpan;
                    float v = ((y + 0.5f) / N - 0.5f) * 2f * TurretSpan;
                    float f = TurretShapeField(u, v);
                    // 轮廓：|f| < 线宽/2 的地方（边界两侧各一半）；填充：f > 0 的全部
                    float a = outline
                        ? Mathf.Clamp01((TurretOutlineW * 0.5f - Mathf.Abs(f)) / aa)
                        : Mathf.Clamp01(f / aa);
                    px[y * N + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name + "_tex" };
            tex.SetPixels32(px); tex.Apply(false, false);
            var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), ppu);
            sp.name = name;
            return sp;
        }

        /// <summary>纯白细条纹，给子弹做「白色竖条纹拖尾」。
        public static Sprite Streak
        {
            get
            {
                if (_streak == null)
                {
                    const int W = 256, H = 32;
                    var px = new Color32[W * H];
                    for (int y = 0; y < H; y++)
                    {
                        for (int x = 0; x < W; x++)
                        {
                            // ★ TrailRenderer 的贴图 u（和颜色渐变一样）**从「头」开始算**，
                            //   所以 x=0 必须是头：头满宽、往尾尖收成 10% ⇒ 画出来才是「头粗尾细」。
                            float fx = 1f - (x + 0.5f) / W;            // 0 = 头，1 = 尾尖
                            float fy = (y + 0.5f) / H - 0.5f;
                            float s = fx * fx * (3f - 2f * fx);
                            float half = 0.5f * (0.10f + 0.90f * s);
                            float a = Mathf.Clamp01((half - Mathf.Abs(fy)) * H + 0.5f);
                            px[y * W + x] = new Color32(255, 255, 255,
                                (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                        }
                    }
                    var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "prims_streak_tex"
                    };
                    tex.SetPixels32(px);
                    tex.Apply(false, false);
                    // 原始尺寸 2.0 长 × 0.25 高；x=0 那头是**头**（TrailRenderer 的 u 从头部开始）
                    _streak = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), 128f);
                    _streak.name = "prims_streak";
                }
                return _streak;
            }
        }

        /// <summary>设置条纹拖尾缩放（世界单位）。</summary>
        public static void SetStreak(Transform t, float rootScale, float worldLength, float worldThickness)
        {
            if (t == null) return;
            float s = Mathf.Max(rootScale, 0.0001f);
            // 原始：长 2.0、高 0.25
            t.localScale = new Vector3(
                Mathf.Max(worldLength, 0.001f) / (2f * s),
                Mathf.Max(worldThickness, 0.001f) / (0.25f * s),
                1f);
        }

        /// <summary>把拖尾转到速度方向（精灵 +X 是头）。</summary>
        public static void FaceVelocity(Transform t, Vector2 velocity)
        {
            if (t == null) return;
            if (velocity.sqrMagnitude < 0.0001f) return;
            t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);
        }

        // ─────────────────────────────────────────────────────────────
        // 武器栏的四个图标（规格：照着参考图做的剪影）
        // 全部是**白 alpha 剪影**，颜色由调用方给（武器栏里就是黑色）。
        // 坐标系是 0..1 归一化、y 向上。
        // ─────────────────────────────────────────────────────────────

        static Sprite _iconDirect, _iconScatter, _iconVolley, _iconShield, _iconBigBall;
        static Sprite _vignette;

        /// <summary>全屏暗角：中间透明、四周变暗。画面装饰用。</summary>
        public static Sprite Vignette
        {
            get
            {
                if (_vignette == null)
                {
                    const int N = 256;
                    var px = new Color32[N * N];
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            float dx = (x + 0.5f) / N - 0.5f;
                            float dy = (y + 0.5f) / N - 0.5f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;   // 1 = 角
                            float a = Mathf.Clamp01(Mathf.InverseLerp(0.35f, 1.05f, d));
                            a = a * a;
                            px[y * N + x] = new Color32(0, 0, 0,
                                (byte)Mathf.RoundToInt(a * 255f));
                        }
                    }
                    var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "prims_vignette_tex" };
                    tex.SetPixels32(px); tex.Apply(false, false);
                    _vignette = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
                    _vignette.name = "prims_vignette";
                }
                return _vignette;
            }
        }

        /// <summary>
        /// 从内嵌的 base64 PNG 解出一个图标 Sprite（白色 + alpha 剪影）。
        /// 规格：图标用**用户给的参考图**，转成剪影后内嵌在 `WeaponIcons` 里 ——
        /// 工程因此保持零外部资源（没有 png / meta / GUID）。解不出来返回 null，调用方退回程序生成的形状。
        /// </summary>
        static Sprite IconFromBase64(string b64, string name)
        {
            if (string.IsNullOrEmpty(b64))
            {
                Debug.LogWarning("[ICON] " + name + " 没有内嵌位图数据（退回程序生成的形状）");
                return null;
            }
            try
            {
                byte[] bytes = System.Convert.FromBase64String(b64);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name + "_tex" };
                if (!tex.LoadImage(bytes)) return null;
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                       new Vector2(0.5f, 0.5f), tex.width);
                sp.name = name;
                // 成功解码不必打日志：世界重建（设置面板换阵营数）会再走一遍，一开就是一串。
                return sp;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ICON] " + name + " 解码失败（退回程序生成的形状）：" + e.Message);
                return null;
            }
        }

        /// <summary>按武器槽种类取图标。</summary>
        public static Sprite WeaponIcon(WeaponSlotKind kind)
        {
            switch (kind)
            {
                case WeaponSlotKind.Direct: return DirectIcon;
                case WeaponSlotKind.Scatter: return ScatterIcon;
                case WeaponSlotKind.Volley: return VolleyIcon;
                case WeaponSlotKind.Shield: return ShieldIcon;
                case WeaponSlotKind.Ciws: return CiwsIcon;
                case WeaponSlotKind.Vampire: return VampireIcon;
                default: return BigBallIcon;
            }
        }

        /// <summary>圆角/实心矩形遮罩（用于拼图标）。</summary>
        static float Box(float x, float y, float x0, float y0, float x1, float y1, float soft)
        {
            float a = Mathf.Clamp01(Mathf.Min(x - x0, x1 - x) / soft + 0.5f);
            float b = Mathf.Clamp01(Mathf.Min(y - y0, y1 - y) / soft + 0.5f);
            return Mathf.Min(a, b);
        }

        /// <summary>
        /// ★★★ 第 103 轮（用户给的参考图）：**近防炮**图标 —— 侧视的炮：
        /// 底座 + 机身 + 伸向左边的粗炮管，炮口中间是**镂空**的（和参考图一样）。
        /// ★ 参考图是等轴测的，但武器区只有 ~14px 高，等轴测细节全糊成一团；
        ///   这里用**侧视剪影**，小尺寸下辨识度最高。
        /// </summary>
        public static Sprite CiwsIcon
        {
            get
            {
                if (_iconCiws == null)
                {
                    // ★★★ 第 103 轮：**优先用用户给的那张参考图**（内嵌位图）。
                    //   程序生成的侧视形状只在位图缺失时兜底。
                    var embedded = IconFromBase64(WeaponIcons.Ciws, "prims_icon_ciws");
                    if (embedded != null) { _iconCiws = embedded; return embedded; }

                    _iconCiws = BuildIcon(delegate (float x, float y)
                    {
                        float soft = 0.012f;
                        float baseBox = Box(x, y, 0.16f, 0.72f, 0.84f, 0.88f, soft);   // 底座
                        float body = Box(x, y, 0.36f, 0.30f, 0.80f, 0.73f, soft);       // 机身
                        float barrel = Taper(x, y, 0.40f, 0.46f, 0.06f, 0.53f, 0.105f, 0.070f, soft);
                        float solid = Mathf.Max(baseBox, Mathf.Max(body, barrel));
                        float muzzle = Disc(x, y, 0.105f, 0.525f, 0.052f, soft);        // 炮口镂空
                        return Mathf.Clamp01(solid - muzzle * 0.9f);
                    }, "prims_icon_ciws");
                }
                return _iconCiws;
            }
        }
        static Sprite _iconCiws;

        /// <summary>圆盘遮罩：返回 0~1 的覆盖度（边缘 1px 抗锯齿）。</summary>
        static float Disc(float x, float y, float cx, float cy, float r, float soft)
        {
            float dx = x - cx, dy = y - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01((r - d) / soft + 0.5f);
        }

        /// <summary>锥形线段遮罩：从 (ax,ay) 半径 ra 收到 (bx,by) 半径 rb。</summary>
        static float Taper(float x, float y, float ax, float ay, float bx, float by,
                           float ra, float rb, float soft)
        {
            float vx = bx - ax, vy = by - ay;
            float wx = x - ax, wy = y - ay;
            float l2 = vx * vx + vy * vy;
            float t = l2 > 1e-9f ? Mathf.Clamp01((wx * vx + wy * vy) / l2) : 0f;
            float dx = wx - vx * t, dy = wy - vy * t;
            float r = Mathf.Lerp(ra, rb, t);
            return Mathf.Clamp01((r - Mathf.Sqrt(dx * dx + dy * dy)) / soft + 0.5f);
        }

        static Sprite BuildIcon(System.Func<float, float, float> mask, string name)
        {
            const int N = 160;
            float soft = 1.6f / N;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float fx = (x + 0.5f) / N, fy = (y + 0.5f) / N;
                    float a = Mathf.Clamp01(mask(fx, fy));
                    px[y * N + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name + "_tex" };
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), (float)N);
        }

        /// <summary>
        /// **直射**（用户 图一）：一个实心圆点 + 右边一小截粗炮口，再往右三节**虚线**
        /// —— 就是「朝正前方打出去的一串直弹」。
        /// </summary>
        public static Sprite DirectIcon
        {
            get
            {
                if (_iconDirect == null)
                {
                    // ★★ 规格（用户第 89 轮）：「直射图标怪怪的，**直接把图一放上去就行了**」——
                    //   所以优先用内嵌的位图（就是我按图一生成的剪影），
                    //   下面那套程序生成只当兜底（解码失败时用）。
                    var embeddedD = IconFromBase64(WeaponIcons.Direct, "prims_icon_direct");
                    if (embeddedD != null) { _iconDirect = embeddedD; return embeddedD; }

                    const float soft = 1.6f / 160f;
                    _iconDirect = BuildIcon((x, y) =>
                    {
                        float a = Disc(x, y, 0.19f, 0.50f, 0.175f, soft);                  // 弹头圆点
                        a = Mathf.Max(a, Taper(x, y, 0.28f, 0.50f, 0.44f, 0.50f,
                                               0.115f, 0.105f, soft));                     // 炮口那一小截
                        // 三节虚线（每节 = 一短横，右端略粗，看着像在往外飞）
                        a = Mathf.Max(a, Taper(x, y, 0.56f, 0.50f, 0.68f, 0.50f, 0.075f, 0.105f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.75f, 0.50f, 0.87f, 0.50f, 0.075f, 0.105f, soft));
                        return a;
                    }, "prims_icon_direct");
                }
                return _iconDirect;
            }
        }

        /// <summary>散射：一支刷子（圆头 + 短柄）外加三道笔触。</summary>
        public static Sprite ScatterIcon
        {
            get
            {
                if (_iconScatter == null)
                {
                    var embedded = IconFromBase64(WeaponIcons.Scatter, "prims_icon_scatter");
                    // ★★ 必须**存进静态字段**再返回！原来直接 `return embedded` 没缓存 ——
                    //    于是**每一帧每一格**都重新 base64 解码 + 新建一张 Texture2D，
                    //    武器栏 5 格就是每帧 5 次。实测这一条吃掉 **8.7ms**（F1 的 weapon 一栏）。
                    if (embedded != null) { _iconScatter = embedded; return embedded; }

                    const float soft = 1.6f / 160f;
                    _iconScatter = BuildIcon((x, y) =>
                    {
                        float a = Disc(x, y, 0.21f, 0.70f, 0.185f, soft);              // 刷头
                        a = Mathf.Max(a, Taper(x, y, 0.30f, 0.60f, 0.47f, 0.42f, 0.10f, 0.035f, soft)); // 柄
                        a = Mathf.Max(a, Taper(x, y, 0.55f, 0.45f, 0.66f, 0.38f, 0.045f, 0.02f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.69f, 0.47f, 0.79f, 0.41f, 0.035f, 0.015f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.82f, 0.44f, 0.90f, 0.40f, 0.025f, 0.01f, soft));
                        return a;
                    }, "prims_icon_scatter");
                }
                return _iconScatter;
            }
        }

        /// <summary>齐射：三枚并排的尖头弹丸。</summary>
        public static Sprite VolleyIcon
        {
            get
            {
                if (_iconVolley == null)
                {
                    var embedded = IconFromBase64(WeaponIcons.Volley, "prims_icon_volley");
                    // ★★ 必须**存进静态字段**再返回！原来直接 `return embedded` 没缓存 ——
                    //    于是**每一帧每一格**都重新 base64 解码 + 新建一张 Texture2D，
                    //    武器栏 5 格就是每帧 5 次。实测这一条吃掉 **8.7ms**（F1 的 weapon 一栏）。
                    if (embedded != null) { _iconVolley = embedded; return embedded; }

                    const float soft = 1.6f / 160f;
                    _iconVolley = BuildIcon((x, y) =>
                    {
                        float a = 0f;
                        // 每枚 = 圆头 + 往后收成尖的长身（朝左上 45°）
                        a = Mathf.Max(a, Disc(x, y, 0.72f, 0.26f, 0.115f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.72f, 0.26f, 0.20f, 0.78f, 0.105f, 0.012f, soft));
                        a = Mathf.Max(a, Disc(x, y, 0.52f, 0.14f, 0.105f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.52f, 0.14f, 0.06f, 0.60f, 0.095f, 0.012f, soft));
                        a = Mathf.Max(a, Disc(x, y, 0.90f, 0.24f, 0.085f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.90f, 0.24f, 0.44f, 0.72f, 0.078f, 0.010f, soft));
                        return a;
                    }, "prims_icon_volley");
                }
                return _iconVolley;
            }
        }

        /// <summary>护盾：经典盾牌剪影（上宽、下面收成尖）。</summary>
        public static Sprite ShieldIcon
        {
            get
            {
                if (_iconShield == null)
                {
                    var embedded = IconFromBase64(WeaponIcons.Shield, "prims_icon_shield");
                    // ★★ 必须**存进静态字段**再返回！原来直接 `return embedded` 没缓存 ——
                    //    于是**每一帧每一格**都重新 base64 解码 + 新建一张 Texture2D，
                    //    武器栏 5 格就是每帧 5 次。实测这一条吃掉 **8.7ms**（F1 的 weapon 一栏）。
                    if (embedded != null) { _iconShield = embedded; return embedded; }

                    const float soft = 1.6f / 160f;
                    _iconShield = BuildIcon((x, y) =>
                    {
                        const float top = 0.88f, bottom = 0.10f;
                        if (y > top || y < bottom) return 0f;
                        float t = (y - bottom) / (top - bottom);           // 0 尖，1 顶
                        // 上面平、下面收成尖，中段带一点弧度
                        float half = 0.32f * Mathf.Pow(t, 0.42f);
                        // 顶部两个小肩角
                        if (y > top - 0.10f) half = 0.32f;
                        float d = Mathf.Abs(x - 0.5f) - half;
                        return Mathf.Clamp01(-d / soft + 0.5f);
                    }, "prims_icon_shield");
                }
                return _iconShield;
            }
        }

        /// <summary>大球：彗星（本体 + 月牙 + 扇形尾迹）。</summary>
        public static Sprite BigBallIcon
        {
            get
            {
                if (_iconBigBall == null)
                {
                    var embedded = IconFromBase64(WeaponIcons.BigBall, "prims_icon_bigball");
                    // ★★ 必须**存进静态字段**再返回！原来直接 `return embedded` 没缓存 ——
                    //    于是**每一帧每一格**都重新 base64 解码 + 新建一张 Texture2D，
                    //    武器栏 5 格就是每帧 5 次。实测这一条吃掉 **8.7ms**（F1 的 weapon 一栏）。
                    if (embedded != null) { _iconBigBall = embedded; return embedded; }

                    const float soft = 1.6f / 160f;
                    _iconBigBall = BuildIcon((x, y) =>
                    {
                        // 尾迹：三束从本体往左上散开的尖条
                        float a = Taper(x, y, 0.52f, 0.42f, 0.04f, 0.96f, 0.085f, 0.006f, soft);
                        a = Mathf.Max(a, Taper(x, y, 0.50f, 0.36f, 0.03f, 0.70f, 0.095f, 0.006f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.50f, 0.32f, 0.06f, 0.44f, 0.085f, 0.006f, soft));
                        a = Mathf.Max(a, Taper(x, y, 0.56f, 0.50f, 0.18f, 0.92f, 0.06f, 0.006f, soft));
                        // 本体
                        a = Mathf.Max(a, Disc(x, y, 0.62f, 0.38f, 0.30f, soft));
                        // 月牙：从本体里挖掉一个偏心圆
                        a = Mathf.Min(a, 1f - Disc(x, y, 0.72f, 0.47f, 0.235f, soft));
                        return a;
                    }, "prims_icon_bigball");
                }
                return _iconBigBall;
            }
        }

        /// <summary>
        /// **吸血球**（第 111 轮，用户给的参考图）：左下、右上两个实心圆，
        /// 中间一条**蛇形粗线**把它们连起来，两个圆里各挖一个字母（X / Y）——
        /// 小尺寸下就是「两颗球被吸在一起」的剪影。
        /// ★ 字母是**镂空**的（参考图上字母是白的）：从剪影里减掉，白牌子上就露出白字。
        /// </summary>
        public static Sprite VampireIcon
        {
            get
            {
                if (_iconVampire == null)
                {
                    const float soft = 1.6f / 160f;
                    _iconVampire = BuildIcon((x, y) =>
                    {
                        // 两个实心圆
                        float a = Disc(x, y, 0.270f, 0.260f, 0.170f, soft);
                        a = Mathf.Max(a, Disc(x, y, 0.735f, 0.755f, 0.155f, soft));
                        // 中间那条 S 形粗线（三次贝塞尔）
                        a = Mathf.Max(a, BezierStroke(x, y,
                            0.360f, 0.360f, 0.620f, 0.440f, 0.400f, 0.580f, 0.645f, 0.655f,
                            0.055f, soft));
                        // 字母挖空
                        float letters = LetterX(x, y, 0.270f, 0.260f, 0.085f, 0.016f, soft);
                        letters = Mathf.Max(letters, LetterY(x, y, 0.735f, 0.755f, 0.080f, 0.015f, soft));
                        return Mathf.Clamp01(a - letters);
                    }, "prims_icon_vampire");
                }
                return _iconVampire;
            }
        }
        static Sprite _iconVampire;

        /// <summary>沿三次贝塞尔曲线描一条等粗的线（24 段 `Taper` 拼起来）。</summary>
        static float BezierStroke(float x, float y,
            float ax, float ay, float bx, float by,
            float cx, float cy, float dx, float dy,
            float r, float soft)
        {
            const int N = 24;
            float best = 0f;
            float px = ax, py = ay;
            for (int i = 1; i <= N; i++)
            {
                float t = (float)i / N, u = 1f - t;
                float qx = u * u * u * ax + 3f * u * u * t * bx + 3f * u * t * t * cx + t * t * t * dx;
                float qy = u * u * u * ay + 3f * u * u * t * by + 3f * u * t * t * cy + t * t * t * dy;
                best = Mathf.Max(best, Taper(x, y, px, py, qx, qy, r, r, soft));
                px = qx; py = qy;
            }
            return best;
        }

        /// <summary>字母 X 的笔画遮罩（给吸血球图标镂空用）。</summary>
        static float LetterX(float x, float y, float cx, float cy, float s, float r, float soft)
        {
            float a = Taper(x, y, cx - s, cy - s, cx + s, cy + s, r, r, soft);
            a = Mathf.Max(a, Taper(x, y, cx + s, cy - s, cx - s, cy + s, r, r, soft));
            return a;
        }

        /// <summary>字母 Y 的笔画遮罩（给吸血球图标镂空用）。</summary>
        static float LetterY(float x, float y, float cx, float cy, float s, float r, float soft)
        {
            float a = Taper(x, y, cx - s, cy + s * 0.9f, cx, cy, r, r, soft);
            a = Mathf.Max(a, Taper(x, y, cx + s, cy + s * 0.9f, cx, cy, r, r, soft));
            a = Mathf.Max(a, Taper(x, y, cx, cy, cx, cy - s * 1.1f, r, r, soft));
            return a;
        }

        static Texture2D SolidTexture(int size, Color c)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "prims_solid"
            };
            var px = new Color32[size * size];
            Color32 c32 = c;
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>实心圆或圆环（innerRatio &lt; 1 时是环）。带一像素抗锯齿。</summary>
        /// <remarks>
        /// ★★★ **必须带 mipmap**。这张贴图同时给两个差了几百倍的尺度的东西用：
        ///   · 炮台机体 / 大球轮廓 —— 屏幕上几百像素，用 mip 0，不受影响；
        ///   · **子弹圆点** —— 半径 `Cfg.BulletDotRadius` = 0.018m，在 1024px 的地图上
        ///     直径只有 ≈ **2.6 个像素**。
        /// 没有 mipmap 时，2.6 像素的输出要从 192×192 里采样 —— 双线性只看得到 2×2 个纹素，
        /// 正好落在圆的边界附近随机取到 0 或 255，于是圆点画出来是**方块 / 菱形 / 锯齿块**，
        /// 也就是用户报的「**子弹好像不是完全圆形的**」。
        /// 有 mipmap 之后，这一档会自动落到第 6 级那几张（≈3×3 纹素、就是圆被平均出来的样子），
        /// 圆点才是一颗**圆的、边缘柔和的小点**。
        /// </remarks>
        static Texture2D CircleTexture(int n, float innerRatio)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Bilinear,   // 带 mipmap 时 = 三线性
                wrapMode = TextureWrapMode.Clamp,
                name = "prims_circle_tex"
            };
            var px = new Color32[n * n];
            float outer = 0.5f;
            float inner = 0.5f * innerRatio;
            float aa = 1.5f / n;

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f;
                    float dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float a = innerRatio >= 0.999f
                        ? Mathf.Clamp01((outer - d) / aa)
                        : Mathf.Min(Mathf.Clamp01((outer - d) / aa), Mathf.Clamp01((d - inner) / aa));

                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            // ★ 第一个参数必须是 true：建的时候开了 mip 链，不在这里生成的话
            //   那几级 mip 是**未初始化**的（花屏/黑块），缩小时反而更糟。
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>建一个用 SpriteRenderer 表现的对象。</summary>
        public static SpriteRenderer MakeNode(string name, Sprite sprite, Color color, int order, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            if (SpriteMaterial != null) sr.sharedMaterial = SpriteMaterial;
            return sr;
        }
    }
}
