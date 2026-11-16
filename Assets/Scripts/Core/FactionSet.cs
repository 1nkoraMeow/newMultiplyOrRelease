using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 一个阵营的**全部外观与出身**：颜色、名字、领土花纹、出生点。
    /// 换版本（4 家 / 8 家）时只需要换这一张表，别的地方不用动。
    /// </summary>
    public struct FactionDef
    {
        /// <summary>
        /// 颜色：HSV（H 度 / S 饱和 / V 明度）。★ `Palette.HsvToRgb` **不夹分量**。
        ///
        /// 这里填的是**出厂值**。设置面板调过的颜色存在 PlayerPrefs
        /// （`mor.set.hsv.*.<阵营数>.<下标>`）里，覆盖这张表。
        /// ★★ 但第 110 轮起覆盖会记下写入时的出厂值（`mor.set.base.*`）：
        ///   **改了这里的 Hsv，下次启动旧覆盖自动作废**，直接用新值 —— 改表一定生效。
        ///   `TextBlack` 同理（`mor.set.text.*` / `mor.set.base.text.*`）。
        ///   （想立刻生效就重开一次；面板里的「还原」「恢复默认颜色」仍然可用。）
        /// </summary>
        public Vector3 Hsv;
        /// <summary>
        /// 这个阵营的文字用**黑字**（false = 白字）。★ **手动填**，不再由颜色推算 ——
        /// 想换文字颜色直接改这一格，别再去调阈值。
        /// </summary>
        public bool TextBlack;
        /// <summary>调试面板(F1)和日志里的短标签。</summary>
        public string Label;
        /// <summary>头衔（可空 —— 空了整行不画）。</summary>
        public string Title;
        /// <summary>名称（必填，「名称」那行永远大一号）。</summary>
        public string Name;
        /// <summary>称号（可空）。</summary>
        public string Epithet;
        /// <summary>领土花纹（见 `TerritoryDeco.Styles`）。</summary>
        public TerritoryDeco.Style Deco;
        /// <summary>出生点（世界坐标，0..`Cfg.MapWorldSize`）。</summary>
        public Vector2 Home;
    }

    /// <summary>
    /// ★★ 第 100 轮（用户）：「把 4 色版和 8 色版**单独分开成两个文件**」。
    ///
    /// 于是阵营表分成四份、各自完整、互不覆盖：
    ///   · <see cref="FactionSet2"/> —— 2 家（1v1）
    ///   · <see cref="FactionSet4"/> —— 4 家（四角布局）
    ///   · <see cref="FactionSet8"/> —— 8 家（八边形环布局，第一批就是照用户给的图做的）
    ///   · <see cref="FactionSet16"/> —— 16 家（均匀网格）
    /// 这个文件只做三件事：**选哪一套** + 给全工程一个统一的取数口子。
    /// 将来真要拆成独立工程，也就是复制一份文件夹、把 <see cref="DefaultActive"/> 改一下。
    /// </summary>
    public static class FactionSet
    {
        /// <summary>
        /// ★ 工程**默认**用哪一套：**2**（1v1）/ **4** / **8** / **16**。
        ///
        /// ★★ 从第 105 轮起这个值只是「默认档」—— 真正生效的阵营数在
        ///   <see cref="GameSettings.FactionCount"/>（设置面板里可改，存 PlayerPrefs）。
        ///   改这里的值 = 改「第一次启动时用几家」。
        /// </summary>
        public const int DefaultActive = 4;

        /// <summary>当前生效的阵营数（2 / 4 / 8 / 16）。</summary>
        public static int Count { get { return GameSettings.FactionCount; } }

        public static FactionDef[] Defs
        {
            get { return TableOf(GameSettings.FactionCount); }
        }

        /// <summary>
        /// 阵营数 → 阵营表。四张表各自完整、互不覆盖（见本文件头部说明）。
        /// ★ public 是给 `GameSettings` 用的：加载存档时要拿**每一档**的出厂值
        ///   去比对旧覆盖（`mor.set.base.*`），不能只看当前这一档。
        /// </summary>
        public static FactionDef[] TableOf(int n)
        {
            if (n == 2) return FactionSet2.Defs;
            if (n == 16) return FactionSet16.Defs;
            if (n == 8) return FactionSet8.Defs;
            return FactionSet4.Defs;
        }

        public static FactionDef Get(int i)
        {
            var d = Defs;
            return d[Mathf.Clamp(i, 0, d.Length - 1)];
        }

        // ── 几套布局共用的算法（各自的表在 FactionSet2/4/8/16 里调）──

        /// <summary>
        /// **均匀网格**布局（第 103 轮 16 家版用）：`cols × rows`，离四周各留 `margin` 比例。
        /// ★ **行 0 在最上面**（和用户给的「(1,1) 左上角」一致），
        ///   index 按行走：`i = row * cols + col`。
        /// </summary>
        public static Vector2 Grid(int i, int cols, int rows, float margin)
        {
            float s = Cfg.MapWorldSize;
            int col = cols > 0 ? ((i % cols) + cols) % cols : 0;
            int row = cols > 0 ? (((i / cols) % rows) + rows) % rows : 0;
            float fx = cols > 1 ? margin + (1f - 2f * margin) * col / (cols - 1) : 0.5f;
            float fy = rows > 1 ? margin + (1f - 2f * margin) * row / (rows - 1) : 0.5f;
            // 行 0 在最上面 ⇒ y 用 1 - fy
            return new Vector2(s * fx, s * (1f - fy));
        }

        /// <summary>四角布局：离最近两条边各留 `margin` 的比例。</summary>
        public static Vector2 Corner(int i, float margin)
        {
            float s = Cfg.MapWorldSize;
            float a = s * margin;
            float b = s * (1f - margin);
            switch (i & 3)
            {
                case 0: return new Vector2(a, b);   // 左上
                case 1: return new Vector2(b, b);   // 右上
                case 2: return new Vector2(a, a);   // 左下
                default: return new Vector2(b, a);  // 右下
            }
        }

        /// <summary>
        /// 【1v1】对角布局：`i = 0` 是**左下角**、`i = 1` 是**右上角**（离最近两条边各留 margin）。
        /// ★ 用的是和四角布局一样的 margin（1.82m / 14m ≈ 0.13）。
        /// </summary>
        public static Vector2 Diagonal(int i, float margin)
        {
            float s = Cfg.MapWorldSize;
            float a = s * margin;
            float b = s * (1f - margin);
            return (i & 1) == 0 ? new Vector2(a, a) : new Vector2(b, b);
        }

        /// <summary>
        /// 八边形环布局：以地图中心为圆心、半径 `radius`，第 i 个在
        /// `startDeg + i × 45°` 上（**从正上方顺时针量**）。
        /// ★ 量参考图量出来的：8 家正好在 337.5° 起、每 45° 一个（也就是 startDeg = −22.5）。
        /// </summary>
        public static Vector2 Ring(int i, float radius, float startDeg)
        {
            float s = Cfg.MapWorldSize;
            float th = Mathf.Deg2Rad * (startDeg + i * 45f);
            // θ 从正上方顺时针：x 用 sin、y 用 cos
            return new Vector2(s * 0.5f + Mathf.Sin(th) * radius,
                               s * 0.5f + Mathf.Cos(th) * radius);
        }
    }
}
