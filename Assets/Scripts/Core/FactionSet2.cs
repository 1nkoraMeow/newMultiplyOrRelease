using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 【1v1 版】的阵营表 —— **只有两个炮台**，占**对角**：左下角 + 右上角。
    /// （用户第 101 轮：「只保留左下角和右上角两个炮台」。）
    ///
    /// ★ 顺序 = 阵营 Index：**0 左下 / 1 右上**。
    /// ★★★ 第 103 轮（用户）：「改成 1v1 版。**左下亮红色 Queen, Molave VI, the Elected**；
    ///    **右上红色 King, Haiyan XIX, the Ruinous**。**两个红色需要看出颜色区别**」。
    ///
    /// 两个都是"红"，所以**靠明度拉开**（色相也各偏一点，双重区分）：
    ///   · 左下**亮红** H4 S0.96 V1.00 —— 偏一点橙的鲜红（`TextBlack = true` ⇒ **黑字**）
    ///   · 右上**红** H352 S0.88 V0.66 —— 偏一点洋红的暗红（`TextBlack = false` ⇒ **白字**；
    ///     ★ 亮度 0.26 又刻意压在 `Cfg.BulletTrailMinLuminance` 0.25 **之上**，
    ///     免得被"太暗就换白"那条规则把拖尾整个变成白色）
    ///   ⇒ 一明一暗、一黑字一白字，**扫一眼就分得开**。
    /// ★ 花纹沿用上一版这两家：左下**人字纹**、右上**立方体**（用户这次只说了颜色和名字）。
    /// </summary>
    public static class FactionSet2
    {
        /// <summary>离最近两条边留多少比例（和 4 家版一样：1.82m / 14m ≈ 0.13）。</summary>
        const float Margin = 0.13f;

        public static readonly FactionDef[] Defs =
        {
            // 0 —— 亮红（左下）：人字纹
            new FactionDef
            {
                Hsv = new Vector3(4f, 0.96f, 1.00f),
                TextBlack = true,
                Label = "BrightRed",
                Title = "Queen", Name = "Molave VI", Epithet = "the Elected",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Chevron, R = 19, Line = 2.0f, Shade = 0.72f },
                Home = FactionSet.Diagonal(0, Margin),
            },
            // 1 —— 红（右上）：立方体
            new FactionDef
            {
                Hsv = new Vector3(352f, 0.88f, 0.66f),
                TextBlack = false,
                Label = "Red",
                Title = "King", Name = "Haiyan XIX", Epithet = "the Ruinous",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexCube, R = 23, Line = 1.6f, Shade = 0.82f },
                Home = FactionSet.Diagonal(1, Margin),
            },
        };
    }
}
