using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 【4 家版】的阵营表 —— 四角布局。
    ///
    /// ★ 这张表最初是从第 94~99 轮那套原样搬过来的；第 110 轮把面板里调好的颜色
    ///   同步了回来（见下面 <see cref="Defs"/> 的注释）。
    ///   以后用户改 4 家版的颜色/名字，改这里；**不要**去动 <see cref="FactionSet8"/>。
    /// 顺序 = 阵营 Index：0 左上 / 1 右上 / 2 左下 / 3 右下。
    ///
    /// ★★ 第 110 轮起：改这张表**一定生效**。面板调过的颜色是临时覆盖
    ///   （`mor.set.hsv.*.4.<下标>`），但每条都记着写入时的出厂值；下次启动发现
    ///   表里的值变了，旧覆盖自动作废并删除，直接用这张表的新值。
    /// </summary>
    public static class FactionSet4
    {
        /// <summary>离最近两条边留多少比例（1.82m / 14m ≈ 0.13）。</summary>
        const float Margin = 0.13f;

        /// <summary>
        /// ★★ 第 131 轮：随机换了一套**高饱和高亮度**的四色（蓝 / 黄 / 品红 / 绿）——
        ///   四家的 S ≥ 0.80、V ≥ 0.95，色相间隔 ≥ 80°，涂地时一眼能分家。
        ///   （第 110 轮那套「蓝 / 绿 / 紫 / 橙红」已被这版取代。）
        /// ★ 名字那几行按「头衔 / 名字 / 称号」三段填，**缺的那段留 null，整行不画**
        /// ★ 文字黑/白是**每家手填**的（`TextBlack`），不再按颜色推算：
        /// </summary>
        public static readonly FactionDef[] Defs =
        {
            // 0 —— 左上
            new FactionDef
            {
                Hsv = new Vector3(225f, 0.80f, 0.99f),
                TextBlack = false,
                Label = "Color1",
                Title = null, Name = "Instantine V", Epithet = "the Enlightened",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 47, Line = 2.6f, Shade = 0.80f },
                Home = FactionSet.Corner(0, Margin),
            },
            // 1 —— 右上
            new FactionDef
            {
                Hsv = new Vector3(55f, 0.84f, 0.95f),
                TextBlack = true,
                Label = "Color2",
                Title = null, Name = "Queen Rita VI", Epithet = "",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.HexDouble, R = 20, Line = 1.3f, Shade = 0.78f },
                Home = FactionSet.Corner(1, Margin),
            },
            // 2 —— 左下
            new FactionDef
            {
                Hsv = new Vector3(306f, 0.99f, 0.96f),
                TextBlack = true,
                Label = "Color3",
                Title = null, Name = "Sylvia", Epithet = "the Supernova",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Hex, R = 28, Line = 2.0f, Shade = 0.80f },
                Home = FactionSet.Corner(2, Margin),
            },
            // 3 —— 右下
            new FactionDef
            {
                Hsv = new Vector3(140f, 0.88f, 1.00f),
                TextBlack = true,
                Label = "Color4",
                Title = null, Name = "Yolanda VIII", Epithet = "",
                Deco = new TerritoryDeco.Style { Kind = TerritoryDeco.Kind.Jittered, R = 45, Line = 2.2f, Shade = 0.80f },
                Home = FactionSet.Corner(3, Margin),
            },
        };
    }
}
