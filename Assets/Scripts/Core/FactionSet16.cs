using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 【16 家版】的阵营表 —— **4×4 均匀分布**在战场区（用户第 103 轮给的图和名单）。
    ///
    /// ★ 顺序 = 用户给的顺序，**按行**：
    ///   第 1 行（最上面）从左到右 4 家，然后第 2 行……最后第 4 行。
    ///   也就是 `i = (row-1)*4 + (col-1)`，行 0 在**最上面**。
    /// ★ 颜色是从用户给的参考图上**采样**的（取每家扇形最饱和的那一块，再换算成 HSV）；
    ///   之后按之前定下的习惯把 S/V 拉到"更艳"的区间（S 0.55~0.95、V 0.6~1.0）。
    /// ★ 名字按「头衔 / 名字 / 称号」三段填，**缺的那段留 null，整行不画**。
    /// </summary>
    public static class FactionSet16
    {
        /// <summary>离最近两条边留多少比例（和四角版一致：1.82m / 14m ≈ 0.13）。</summary>
        const float Margin = 0.13f;

        static Vector2 Home(int i) { return FactionSet.Grid(i, 4, 4, Margin); }

        public static readonly FactionDef[] Defs =
        {
            // ── 第 1 行（最上面）──
            // 0 —— 深青（左上）
            new FactionDef
            {
                Hsv = new Vector3(176f, 0.90f, 0.80f),
                TextBlack = true,
                Label = "DeepTeal",
                Title = null, Name = "Indian Street Food", Epithet = null,
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 40, Line = 2.4f, Shade = 0.80f },
                Home = Home(0),
            },
            // 1 —— 品红
            new FactionDef
            {
                Hsv = new Vector3(300f, 0.90f, 1.00f),
                TextBlack = true,
                Label = "Magenta",
                Title = "Captain", Name = "Nari V", Epithet = "the Kingslayer",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexDouble, R = 22, Line = 1.4f, Shade = 0.78f },
                Home = Home(1),
            },
            // 2 —— 红
            new FactionDef
            {
                Hsv = new Vector3(2f, 0.95f, 1.00f),
                TextBlack = true,
                Label = "Red",
                Title = "Captain", Name = "red ciast win II", Epithet = "the Ruinous",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Chevron, R = 19, Line = 2.0f, Shade = 0.72f },
                Home = Home(2),
            },
            // 3 —— 粉
            new FactionDef
            {
                Hsv = new Vector3(325f, 0.60f, 1.00f),
                TextBlack = true,
                Label = "Pink",
                Title = "Queen", Name = "Sophia XVIII", Epithet = "the Kingslayer",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 30, Line = 2.0f, Shade = 0.80f },
                Home = Home(3),
            },

            // ── 第 2 行 ──
            // 4 —— 深渊蓝（第 1 列）
            new FactionDef
            {
                Hsv = new Vector3(229f, 0.92f, 0.85f),
                TextBlack = false,
                Label = "AbyssBlue",
                Title = "King", Name = "ra XIX", Epithet = "the Brave",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Maze, R = 24, Line = 1.8f, Shade = 0.80f },
                Home = Home(4),
            },
            // 5 —— 墨绿
            new FactionDef
            {
                Hsv = new Vector3(135f, 0.90f, 0.65f),
                TextBlack = true,
                Label = "InkGreen",
                Title = "Queen", Name = "Rita V", Epithet = "the Ruinous",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexCube, R = 21, Line = 1.6f, Shade = 0.82f },
                Home = Home(5),
            },
            // 6 —— 藏绿（只有名字）
            new FactionDef
            {
                Hsv = new Vector3(70f, 0.55f, 0.80f),
                TextBlack = true,
                Label = "TibetGreen",
                Title = null, Name = "Brepordis II", Epithet = null,
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Jittered, R = 40, Line = 2.2f, Shade = 0.80f },
                Home = Home(6),
            },
            // 7 —— 紫
            new FactionDef
            {
                Hsv = new Vector3(270f, 0.90f, 1.00f),
                TextBlack = false,
                Label = "Purple",
                Title = "Captain", Name = "10210425 III", Epithet = "the Undefeated",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexSpokes, R = 26, Line = 1.8f, Shade = 0.80f },
                Home = Home(7),
            },

            // ── 第 3 行 ──
            // 8 —— 橙
            new FactionDef
            {
                Hsv = new Vector3(28f, 0.90f, 1.00f),
                TextBlack = true,
                Label = "Orange",
                Title = "King", Name = "Felix VII", Epithet = "the Lucky",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 34, Line = 2.2f, Shade = 0.80f },
                Home = Home(8),
            },
            // 9 —— 白
            new FactionDef
            {
                Hsv = new Vector3(0f, 0.00f, 1.00f),
                TextBlack = true,
                Label = "White",
                Title = "Queen", Name = "Celeste V", Epithet = null,
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexDouble, R = 18, Line = 1.3f, Shade = 0.86f },
                Home = Home(9),
            },
            // 10 —— 深紫蓝（只有头衔 + 名字）
            new FactionDef
            {
                Hsv = new Vector3(235f, 0.92f, 0.75f),
                TextBlack = false,
                Label = "VioletBlue",
                Title = "Captain", Name = "pt XX", Epithet = null,
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Chevron, R = 21, Line = 1.9f, Shade = 0.74f },
                Home = Home(10),
            },
            // 11 —— 亮绿
            new FactionDef
            {
                Hsv = new Vector3(125f, 0.90f, 1.00f),
                TextBlack = true,
                Label = "BrightGreen",
                Title = "Captain", Name = "Chlorine II", Epithet = "the Ruinous",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexCube, R = 19, Line = 1.5f, Shade = 0.80f },
                Home = Home(11),
            },

            // ── 第 4 行（最下面）──
            // 12 —— 棕
            new FactionDef
            {
                Hsv = new Vector3(22f, 0.75f, 0.60f),
                TextBlack = false,
                Label = "Brown",
                Title = "King", Name = "Lupit III", Epithet = "the Supernova",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Jittered, R = 45, Line = 2.2f, Shade = 0.80f },
                Home = Home(12),
            },
            // 13 —— 黄（只有名字）
            new FactionDef
            {
                Hsv = new Vector3(58f, 0.90f, 1.00f),
                TextBlack = true,
                Label = "Yellow",
                Title = null, Name = "Jebi II", Epithet = null,
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 28, Line = 2.0f, Shade = 0.80f },
                Home = Home(13),
            },
            // 14 —— 青
            new FactionDef
            {
                Hsv = new Vector3(182f, 0.85f, 1.00f),
                TextBlack = true,
                Label = "Cyan",
                Title = "Captain", Name = "Polaris III", Epithet = "the Thurdered",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Maze, R = 22, Line = 1.7f, Shade = 0.80f },
                Home = Home(14),
            },
            // 15 —— 棕红（只有名字）
            new FactionDef
            {
                Hsv = new Vector3(15f, 0.85f, 0.62f),
                TextBlack = false,
                Label = "BrownRed",
                Title = null, Name = "Kenshi", Epithet = null,
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexSpokes, R = 24, Line = 1.8f, Shade = 0.78f },
                Home = Home(15),
            },
        };
    }
}
