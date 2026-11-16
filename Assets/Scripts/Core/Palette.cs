using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 阵营配色 + 文字黑白的判定。
    /// 规格：增倍球文字、大球文字、子弹数量、护盾数量的文字颜色只有黑与白，
    ///       孰黑孰白由炮台（阵营）颜色决定。
    /// </summary>
    public static class Palette
    {
        /// ★ 规格（用户）：阵营色用 **HSV** 定义 —— 用户习惯这种写法，
        ///   以后调颜色直接改阵营表里的三元组（H 度, S 饱和, V 明度）就行。
        ///
        /// ★★ 第 100 轮：这里改成**从阵营表读**（`FactionSet.Defs[i].Hsv`）——
        ///   4 家版和 8 家版各有一整套颜色，切换版本时这里自动跟着换，
        ///   不用再在本文件里维护一份。**改颜色请去 FactionSet4.cs / FactionSet8.cs。**
        ///
        /// ★★★ 第 105 轮：阵营数 / 颜色都变成**运行时可变**（设置面板），
        ///   所以这三个数组改成**惰性重建的属性**，缓存键 = (阵营数, GameSettings.Version)。
        ///   改颜色时 Version 会 +1，下一次读就重建 —— 调用方照旧按下标用，不用管缓存。
        ///
        /// ★★★ 三条不变的规则（比具体色值重要得多）：
        ///   ① `HsvToRgb` **不夹分量** —— `max(RGB) = V`、`min(RGB) = V×(1−S)`。
        ///      **V > 1 时最大分量溢出；S > 1 时最小分量是负数**（绿/红那两家就是 S = 1.1）。
        ///      显示端会夹到 0/1，所以画出来是纯色；但**任何拿颜色做算术的地方都不能漏掉这个**。
        ///   ② 文字黑白**手填**在阵营表里（`FactionDef.TextBlack`），设置面板块里可覆盖
        ///      （`GameSettings.TextBlackOf`）；这里不再有按亮度推算的逻辑。
        ///   ③ 涂地那条路走 `_factionBytes`（**先把底色夹成显示色，再乘花纹系数**）。
        static Vector3[] _factionHsv;
        static Color[] _factionColors;
        static string[] _factionNames;
        static int _cacheCount = -1;
        static int _cacheVersion = -1;

        public static Vector3[] FactionHsv
        {
            get { Refresh(); return _factionHsv; }
        }

        /// <summary>各阵营的颜色（由 <see cref="FactionHsv"/> 换算），顺序 = 出生角顺序。</summary>
        public static Color[] FactionColors
        {
            get { Refresh(); return _factionColors; }
        }

        /// <summary>调试面板(F1)用的短标签（跟着阵营表走，别和用户的"名字"混了）。</summary>
        public static string[] FactionNames
        {
            get { Refresh(); return _factionNames; }
        }

        static void Refresh()
        {
            int n = FactionSet.Count;
            int v = GameSettings.Version;
            if (_factionHsv != null && n == _cacheCount && v == _cacheVersion) return;

            _cacheCount = n;
            _cacheVersion = v;

            var defs = FactionSet.Defs;
            var hsv = new Vector3[n];
            var cols = new Color[n];
            var names = new string[n];
            for (int i = 0; i < n; i++)
            {
                // ★ 走 GameSettings：用户在设置面板改过的颜色优先，没改过就是阵营表原值
                hsv[i] = GameSettings.HsvOf(i);
                cols[i] = HsvToRgb(hsv[i].x, hsv[i].y, hsv[i].z);
                names[i] = defs[i].Label;
            }

            _factionHsv = hsv;
            _factionColors = cols;
            _factionNames = names;
        }

        /// <summary>
        /// HSV → RGB。**不夹 V** —— 允许分量 &gt; 1（用户要的 V = 1.5 就是靠这个「更亮」）。
        /// （Unity 自带的 `Color.HSVToRGB` 会把结果夹到 [0,1]，就丢掉这个语义了。）
        /// </summary>
        public static Color HsvToRgb(float h, float s, float v)
        {
            h = Mathf.Repeat(h, 360f) / 60f;
            float c = v * s;
            float x = c * (1f - Mathf.Abs(Mathf.Repeat(h, 2f) - 1f));
            float m = v - c;
            float r, g, b;
            switch (Mathf.FloorToInt(h) % 6)
            {
                case 0: r = c; g = x; b = 0f; break;
                case 1: r = x; g = c; b = 0f; break;
                case 2: r = 0f; g = c; b = x; break;
                case 3: r = 0f; g = x; b = c; break;
                case 4: r = x; g = 0f; b = c; break;
                default: r = c; g = 0f; b = x; break;
            }
            return new Color(r + m, g + m, b + m, 1f);
        }

        public static Color ColorOf(int factionIndex)
        {
            return FactionColors[Mathf.Abs(factionIndex) % FactionColors.Length];
        }

        /// <summary>感知亮度（Rec.601）。</summary>
        public static float Luminance(Color c)
        {
            return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        }

        /// <summary>
        /// 按 **HSV** 调色：**饱和度 × satScale**、**明度 V + valueAdd**（色相一点不动）。
        /// 战斗区大球就是这条：`S × Cfg.BigBallSaturation(0.7)`、`V + Cfg.BigBallValueAdd(0.3)`。
        /// ★ V **夹到 1**：再往上 RGB 分量就溢出了（本工程的 `HsvToRgb` 故意不夹），
        ///   而渲染是按**通道**截断的 —— 那会把色相也带偏，就不是「同色更亮」了。
        /// </summary>
        public static Color ShiftHsv(Color c, float satScale, float valueAdd)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float d = max - min;
            float s = max > 1e-6f ? d / max : 0f;

            float h;
            if (d < 1e-6f) h = 0f;
            else if (max == c.r) h = 60f * Mathf.Repeat((c.g - c.b) / d, 6f);
            else if (max == c.g) h = 60f * ((c.b - c.r) / d + 2f);
            else h = 60f * ((c.r - c.g) / d + 4f);

            Color o = HsvToRgb(h, Mathf.Clamp01(s * satScale), Mathf.Clamp01(max + valueAdd));
            o.a = c.a;
            return o;
        }

        /// <summary>把颜色压暗/提亮，用于画护盾环、网格线、背景等。</summary>
        public static Color Shade(Color c, float factor)
        {
            return new Color(c.r * factor, c.g * factor, c.b * factor, c.a);
        }

        /// <summary>带透明度的版本。</summary>
        public static Color Alpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }

        /// <summary>
        /// ★★ 第 102 轮（用户）：「大球的饱和度**比阵营颜色高一点**」「子弹拖尾改成阵营颜色
        /// （但饱和度**提升**）」。（★ 他先说的是"低一点"，随即更正成"提升"。）
        ///
        /// 把颜色沿着「它自己的灰度」往外拉：`k = 1` 一点不动，`k &gt; 1` **更艳**，
        /// `k &lt; 1` 更灰（`k = 0` 完全变灰）。
        /// ★ 先夹到 [0,1] —— 阵营色常常 V &gt; 1（分量溢出），不夹的话灰度会算歪。
        /// ★ 结果也夹回 [0,1]（外推可能把分量推成负数）。alpha 原样带过去。
        ///
        /// ★★★ **不能用 `Mathf.Lerp` / `Color.Lerp`！** 它们的 `t` 是**夹到 [0,1]** 的，
        ///   于是 `k &gt; 1`（这条路的**全部意义**就是"更艳"）会被夹成 `k = 1` → **原样返回输入**。
        ///   这个洞让「大球饱和度比阵营色高一点」从第 102 轮起一直是**静默失效**的
        ///   （自检当时也没抓出来，因为断言只比对"两处算的是同一个值"）。
        ///   所以这里自己写外推：`grey + (分量 − grey) × k`。
        /// </summary>
        public static Color Saturate(Color c, float k)
        {
            float r = Mathf.Clamp01(c.r), g = Mathf.Clamp01(c.g), b = Mathf.Clamp01(c.b);
            float grey = 0.299f * r + 0.587f * g + 0.114f * b;
            return new Color(Mathf.Clamp01(grey + (r - grey) * k),
                             Mathf.Clamp01(grey + (g - grey) * k),
                             Mathf.Clamp01(grey + (b - grey) * k), c.a);
        }

        /// <summary>
        /// ★★★ 第 103 轮（用户给参考图）：拖尾是**发白的浅色**，不是更艳的深色。
        ///
        /// 参考图（蓝家）量出来的：领土 ≈ (35,81,219)、拖尾核心 ≈ (148,186,255) ——
        /// 同一色相、饱和掉一大半、明度拉满。所以规则就一句：**阵营色往白色提**
        /// （比例见 `Cfg.BulletTrailWhitening`）。
        ///
        /// ★★ 例外仍然是第 102 轮那条：「**黑色的**子弹拖尾还是用白色（**以后也是这个设定**）」
        /// —— 判据放在**提亮之前**、判的是**阵营色本身**：黑家（亮度 0.16）直接给纯白。
        ///   （提亮之后再判就没意义了 —— 黑提 55% 已经变成灰的，永远过不了阈值。）
        /// ★ 画和自检都走这一个口子，别在别处再写一遍这个判断。
        /// </summary>
        public static Color BulletTrail(Color factionColor)
        {
            if (Luminance(factionColor) < Cfg.BulletTrailMinLuminance) return Color.white;
            float r = Mathf.Clamp01(factionColor.r);
            float g = Mathf.Clamp01(factionColor.g);
            float b = Mathf.Clamp01(factionColor.b);
            return Color.Lerp(new Color(r, g, b, 1f), Color.white, Cfg.BulletTrailWhitening);
        }

        public static readonly Color EmptyTile = new Color(0.055f, 0.055f, 0.070f, 1f);
        public static readonly Color GridLine = new Color(0.16f, 0.16f, 0.19f, 1f);
        public static readonly Color ZoneBackdrop = new Color(0.03f, 0.03f, 0.045f, 1f);
        public static readonly Color BubbleGrey = new Color(0.72f, 0.72f, 0.78f, 1f);
    }
}
