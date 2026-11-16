using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 【8 家版】的阵营表 —— **八边形环**布局，照用户第 100 轮给的那张图做的。
    ///
    /// ★ 顺序 = 用户给的**顺时针**顺序，从绿色开始：
    ///   绿 → 青 → 蓝 → 黄 → 橙 → 红 → 粉 → 紫
    /// ★ 颜色是**从参考图上采样**出来的（取每家领土一小块的中位数 RGB，再换算成 HSV）——
    ///   用户只给了「绿青蓝黄橙红粉紫」这几个字和「花纹如图一」，具体色值就照图走。
    ///   ★ 采样值是**显示色**（S/V ≤ 1）；之后用户要求「颜色更艳一点」，
    ///     就在采样值的**色调不变**的前提下把 S 顶到 0.85~1.0、V 提到 1.0~1.05。
    /// ★ 位置是从参考图上**量出来的**：整张图 1170px ≈ 14m → 83.6 px/m，
    ///   八家的中心连起来是个正八边形，环半径 ≈ 460px = **5.5m**，
    ///   角度落在 **337.5° / 22.5° / 67.5° …**（从正上方顺时针量）——
    ///   也就是「从 −22.5° 起、每 45° 一个」。量出来的角度和用户给的顺序完全对得上。
    ///   （换算下来每家离最近的两条边各约 1.8m，和 4 家版的 1.82m 一致。）
    /// </summary>
    public static class FactionSet8
    {
        /// <summary>环半径（米）—— 图上量出来 460px / 83.6。</summary>
        const float RingRadius = 5.5f;
        /// <summary>第一个（绿色）的角度：从正上方顺时针量是 −22.5°。</summary>
        const float RingStartDeg = -22.5f;

        static Vector2 Home(int i) { return FactionSet.Ring(i, RingRadius, RingStartDeg); }

        public static readonly FactionDef[] Defs =
        {
            // 0 —— 绿（左上）：人字/箭头马赛克
            new FactionDef
            {
                Hsv = new Vector3(106f, 0.95f, 1.00f),
                TextBlack = true,
                Label = "Green",
                Title = "King", Name = "illuminates VI", Epithet = "the Brave",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Chevron, R = 19, Line = 2.0f, Shade = 0.72f },
                Home = Home(0),
            },
            // 1 —— 青（右上）：六边形 + 放射线
            new FactionDef
            {
                Hsv = new Vector3(182f, 0.85f, 1.05f),
                TextBlack = true,
                Label = "Cyan",
                Title = "King", Name = "Constantine XII", Epithet = "the Ruinous",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexCube, R = 23, Line = 1.6f, Shade = 0.82f },
                Home = Home(1),
            },
            // 2 —— 蓝（右）：细方格
            new FactionDef
            {
                Hsv = new Vector3(241f, 1.00f, 1.00f),
                TextBlack = false,
                Label = "Blue",
                Title = "Queen", Name = "Melissa VIII", Epithet = "the Ruinous",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexCube, R = 45, Line = 2.0f, Shade = 0.82f },
                Home = Home(2),
            },
            // 3 —— 黄（右下）：蜂窝
            new FactionDef
            {
                Hsv = new Vector3(59f, 1.00f, 1.05f),
                TextBlack = true,
                Label = "Yellow",
                Title = "Queen", Name = "Judy XIX", Epithet = "the Best",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 24, Line = 2.0f, Shade = 0.86f },
                Home = Home(3),
            },
            // 4 —— 橙（下）：回形纹
            new FactionDef
            {
                Hsv = new Vector3(27f, 1.00f, 1.00f),
                TextBlack = true,
                Label = "Orange",
                Title = "King", Name = "Diu III", Epithet = "the Brave",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Maze, R = 36, Line = 1.6f, Shade = 0.82f },
                Home = Home(4),
            },
            // 5 —— 红（下左）：小蜂窝
            new FactionDef
            {
                Hsv = new Vector3(4f, 1.00f, 1.00f),
                TextBlack = true,
                Label = "Red",
                Title = "Queen", Name = "Molave V", Epithet = "the Supernova",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Jittered, R = 36, Line = 2.0f, Shade = 0.80f },
                Home = Home(5),
            },
            // 6 —— 粉（左）：大蜂窝
            new FactionDef
            {
                Hsv = new Vector3(296f, 0.90f, 1.05f),
                TextBlack = true,
                Label = "Pink",
                Title = "King", Name = "echowenemy trestica", Epithet = "the Last",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 30, Line = 2.4f, Shade = 0.90f },
                Home = Home(6),
            },
            // 7 —— 紫（左上偏上）：大六边形 + 放射线
            new FactionDef
            {
                Hsv = new Vector3(267f, 1.00f, 1.05f),
                TextBlack = false,
                Label = "Purple",
                Title = "Queen", Name = "Eliauk VI", Epithet = "the Ruinous",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexSpokes, R = 40, Line = 2.4f, Shade = 0.84f },
                Home = Home(7),
            },
        };
    }
}
