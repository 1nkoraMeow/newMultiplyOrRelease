#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MultiplyOrRelease.EditorTools
{
    /// <summary>
    /// 把「当前这一帧画面上有什么、在屏幕的哪个位置、多大」导出成 JSON。
    /// 因为 batchmode 进不了 Play 模式、也截不了图，用这个来核对布局：
    /// 之后用一个 Python 脚本把 JSON 画成 PNG，就能看到真实的版面。
    ///
    /// 跑法：
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod MultiplyOrRelease.EditorTools.LayoutDump.Run -logFile -
    /// </summary>
    public static class LayoutDump
    {
        // 用一个固定的「假设屏幕大小」算坐标。-nographics 下 Screen.* 不可靠。
        const int SW = 1600, SH = 900;
        const string OutDir = "/tmp/mor-layout";

        static float _zoneVPW, _zoneVPH, _battleVPW, _battleVPH;
        static float _zoneSize, _battleSize;
        static Vector2 _zoneCenter, _battleCenter;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);

            var world = GameWorld.Build();

            _zoneVPW = Cfg.LeftPanelViewportWidth;
            _zoneVPH = 1f;
            _battleVPW = 1f - Cfg.LeftPanelViewportWidth;
            _battleVPH = 1f;

            // 跟 CameraRig.Build 保持一致，否则离线图和真实取景会不一样
            // ★ 高度**不留白**、宽度才乘 LeftPanelWidthMargin（和 CameraRig 一样）：
            //   这样左栏外墙和右栏战场墙都正好占满栏高，两块等高。
            _zoneSize = FitSize(Cfg.ZoneOuterWidth * Cfg.LeftPanelWidthMargin,
                                Cfg.ZoneOuterHeight, _zoneVPW, _zoneVPH);
            float bw = Cfg.MapWorldSize + Battlefield.BoundThickness * 2f;
            _battleSize = FitSize(bw, bw, _battleVPW, _battleVPH);
            _zoneCenter = Cfg.ZoneCenter;
            _battleCenter = new Vector2(Cfg.MapWorldSize * 0.5f, Cfg.MapWorldSize * 0.5f);

            var sb = new StringBuilder(1 << 18);
            sb.Append("{\n");
            sb.AppendFormat("\"screen\":[{0},{1}],\n", SW, SH);
            sb.AppendFormat("\"zoneScreenRect\":[{0:F1},{1:F1},{2:F1},{3:F1}],\n",
                0f, 0f, SW * _zoneVPW, SH * _zoneVPH);
            sb.AppendFormat("\"zonePxPerUnit\":{0:F3},\n", ZonePx());
            sb.AppendFormat("\"battlePxPerUnit\":{0:F3},\n", BattlePx());

            // ── 场景里的精灵 ──
            sb.Append("\"sprites\":[\n");
            var renderers = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            bool first = true;
            for (int i = 0; i < renderers.Length; i++)
            {
                var sr = renderers[i];
                if (sr == null || sr.sprite == null) continue;

                Bounds b = sr.bounds;
                float diameter = Mathf.Max(b.size.x, b.size.y);
                if (diameter <= 0.0005f) continue;

                Vector3 c = b.center;
                bool inZone = IsInZone(c);
                Vector2 sp = ToScreen(c, inZone);
                float px = diameter * (inZone ? ZonePx() : BattlePx());
                if (px < 0.7f) continue;

                if (!first) sb.Append(",\n");
                first = false;
                string shape = ShapeOf(sr);
                // ★ 把精灵名也带上 —— render.py 靠它区分「实心圆 / 圆环 / 方形」。
                //   不带的话圆环会被画成实心圆，离线图就会骗人（炮台的黑描边就是这样被吞掉的）。
                // ★ 旋转也要带上：炮台那块外形是**圆身子 + 小圆头**，不导出旋转的话
                //   离线图会把炮口画成朝右，跟游戏里对不上。
                // ★★ 最后这个值**必须先 ToString**：Mono 的 `AppendFormat` 碰到
                //   「带格式说明符的占位符紧跟结尾的 `}}`」会直接输出 "F1" 而不是数字
                //   （`{0:F1}}}` → `F1}`，实测过）。换成普通 `{12}` + 预格式化就没事。
                sb.AppendFormat(
                    "{{\"x\":{0:F1},\"y\":{1:F1},\"d\":{2:F1},\"w\":{3:F1},\"h\":{4:F1}," +
                    "\"r\":{5:F3},\"g\":{6:F3},\"b\":{7:F3},\"a\":{8:F2},\"z\":{9},\"shape\":\"{10}\"," +
                    "\"spr\":\"{11}\",\"rot\":{12}}}",
                    sp.x, sp.y, px,
                    b.size.x * (inZone ? ZonePx() : BattlePx()),
                    b.size.y * (inZone ? ZonePx() : BattlePx()),
                    sr.color.r, sr.color.g, sr.color.b, sr.color.a, sr.sortingOrder, shape,
                    sr.sprite.name, sr.transform.eulerAngles.z.ToString("F1"));
            }
            sb.Append("\n],\n");

            // ── HUD 文字（和 HudOverlay 用同一套公式）──
            sb.Append("\"texts\":[\n");
            first = true;
            AppendZoneTexts(world, sb, ref first);
            AppendBattleTexts(world, sb, ref first);
            sb.Append("\n]\n}");

            string path = Path.Combine(OutDir, "layout.json");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[LAYOUT] 已写出 " + path);

            // ── 领土贴图的抽样诊断：以每个阵营的出生点为圆心，
            //    量一圈像素的亮度标准差 —— 平的就是没花纹，起伏大就是有花纹。
            //    （用来验证「初始领土有没有花纹」，比肉眼看截图可靠）
            var diag = new System.Text.StringBuilder();
            var map = world.Field.Texture;
            var raw = map.GetRawTextureData<Color32>();
            float ppu = world.Field.TexelsPerUnit;
            for (int f = 0; f < world.Factions.Count; f++)
            {
                Vector2 home = world.Factions[f].HomePosition;
                float hx = home.x * ppu, hy = home.y * ppu;
                diag.Append("阵营 ").Append(f).Append(" 出生点 (").Append(home.x.ToString("F2"))
                    .Append(", ").Append(home.y.ToString("F2")).Append(")  每米 ")
                    .Append(ppu.ToString("F1")).Append(" 格\n");
                for (float r = 0.4f; r <= 3.0f; r += 0.2f)
                {
                    double sum = 0, sum2 = 0; int n = 0;
                    for (int a = 0; a < 240; a++)
                    {
                        float th = a * Mathf.PI * 2f / 240f;
                        int x = Mathf.RoundToInt(hx + Mathf.Cos(th) * r * ppu);
                        int y = Mathf.RoundToInt(hy + Mathf.Sin(th) * r * ppu);
                        if (x < 0 || y < 0 || x >= world.Field.Texels || y >= world.Field.Texels) continue;
                        var c = raw[y * world.Field.Texels + x];
                        double lum = c.r * 0.299 + c.g * 0.587 + c.b * 0.114;
                        sum += lum; sum2 += lum * lum; n++;
                    }
                    if (n < 60) continue;
                    double mean = sum / n;
                    double sd = System.Math.Sqrt(System.Math.Max(sum2 / n - mean * mean, 0));
                    diag.Append("   r=").Append(r.ToString("F1")).Append("m  亮度 ").Append(mean.ToString("F0"))
                        .Append("  标准差 ").Append(sd.ToString("F1")).Append('\n');
                }
                diag.Append('\n');
            }
            {
                int f0 = 0;
                Vector2 h0 = world.Factions[f0].HomePosition;
                int hy = Mathf.RoundToInt(h0.y * ppu);
                int hx = Mathf.RoundToInt(h0.x * ppu);
                diag.Append("── 出生点那一行的逐格对照（阵营 ").Append(f0)
                    .Append("，y=").Append(hy).Append("）──\n");
                diag.Append("x     Shade(x,y,0)  地图像素(亮度)  归属\n");
                for (int dx = -60; dx <= 60; dx += 3)
                {
                    int x = hx + dx;
                    var c = raw[hy * world.Field.Texels + x];
                    double lum = c.r * 0.299 + c.g * 0.587 + c.b * 0.114;
                    diag.Append(x).Append("   ")
                        .Append(TerritoryDeco.Shade(x, hy, f0).ToString("F2")).Append("   ")
                        .Append(lum.ToString("F0")).Append("   (")
                        .Append(c.r).Append(',').Append(c.g).Append(',').Append(c.b).Append(")\n");
                }
                diag.Append('\n');
            }

            string diagPath = Path.Combine(OutDir, "mapdiag.txt");
            File.WriteAllText(diagPath, diag.ToString());
            Debug.Log("[LAYOUT] 已写出 " + diagPath);

            // ── 领土花纹的 ASCII 预览（花纹是纯函数，离线看得见才好校对）──
            var art = new System.Text.StringBuilder();
            for (int f = 0; f < TerritoryDeco.Styles.Length; f++)
            {
                int w = TerritoryDeco.TileWidth(f), h = TerritoryDeco.TileHeight(f);
                int stride = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(w, h) / 48f));
                art.Append("=== 阵营 ").Append(f).Append("  ")
                   .Append(TerritoryDeco.Styles[f].Kind).Append("  瓷砖 ")
                   .Append(w).Append("x").Append(h)
                   .Append("  线宽 ").Append(TerritoryDeco.Styles[f].Line.ToString("F1"))
                   .Append("  深 ").Append(TerritoryDeco.Styles[f].Shade.ToString("F2"))
                   .Append("（1:·  线:#，缩小 ").Append(stride).Append(" 倍）\n");
                for (int y = 0; y < h; y += stride)
                {
                    for (int x = 0; x < w; x += stride)
                        art.Append(TerritoryDeco.Shade(x, y, f) < 1f ? '#' : '.');
                    art.Append('\n');
                }
                art.Append('\n');
            }
            string artPath = Path.Combine(OutDir, "territory.txt");
            File.WriteAllText(artPath, art.ToString());
            Debug.Log("[LAYOUT] 已写出 " + artPath);

            EditorApplication.Exit(0);
        }

        // ── 坐标换算 ──────────────────────────────────────────────

        /// <summary>把四个武器图标导出成 PNG（离线看形状，白底黑图）。</summary>
        public static void DumpIcons()
        {
            const string dir = "/tmp/mor-icons";
            Directory.CreateDirectory(dir);
            for (int k = 0; k <= (int)WeaponSlotKind.BigBall; k++)
            {
                var sp = Primitives.WeaponIcon((WeaponSlotKind)k);
                if (sp == null || sp.texture == null) { Debug.LogError("[ICON] 缺图标 " + k); continue; }
                var tex = sp.texture;
                Debug.Log("[ICON] " + k + " -> " + tex.width + "x" + tex.height);
                var src = tex.GetPixels32();
                var outp = new Color32[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    byte v = (byte)(255 - src[i].a);
                    outp[i] = new Color32(v, v, v, 255);
                }
                var t2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                t2.SetPixels32(outp);
                t2.Apply(false, false);
                System.IO.File.WriteAllBytes(dir + "/icon" + k + ".png", t2.EncodeToPNG());
                Object.DestroyImmediate(t2);
            }
            Debug.Log("[ICON] dumped to " + dir);
        }

        static float FitSize(float w, float h, float vpw, float vph)
        {
            float vw = SW * vpw, vh = SH * vph;
            float aspect = vw / Mathf.Max(vh, 1f);
            return Mathf.Max(h * 0.5f, (w * 0.5f) / aspect);
        }

        static float ZonePx() { return (SH * _zoneVPH) / Mathf.Max(2f * _zoneSize, 0.001f); }
        static float BattlePx() { return (SH * _battleVPH) / Mathf.Max(2f * _battleSize, 0.001f); }

        static bool IsInZone(Vector3 worldPos)
        {
            return Mathf.Abs(worldPos.x - Cfg.ZoneCenter.x) < Cfg.MapWorldSize;
        }

        /// <summary>世界坐标 → 屏幕像素（y 向下，和 OnGUI 一致）。</summary>
        static Vector2 ToScreen(Vector3 worldPos, bool inZone)
        {
            float px = inZone ? ZonePx() : BattlePx();
            Vector2 center = inZone ? _zoneCenter : _battleCenter;
            float rectX = inZone ? 0f : SW * _zoneVPW;
            float rectW = inZone ? SW * _zoneVPW : SW * _battleVPW;
            float rectH = inZone ? SH * _zoneVPH : SH * _battleVPH;

            float x = rectX + rectW * 0.5f + (worldPos.x - center.x) * px;
            float y = rectH * 0.5f - (worldPos.y - center.y) * px;
            return new Vector2(x, y);
        }

        /// <summary>方形 / 地图 / 拖尾都按矩形画，其余按圆画。</summary>
        static string ShapeOf(SpriteRenderer sr)
        {
            string n = sr.sprite != null ? sr.sprite.name : "";
            if (n.IndexOf("square", System.StringComparison.OrdinalIgnoreCase) >= 0) return "rect";
            if (n.IndexOf("battlefield", System.StringComparison.OrdinalIgnoreCase) >= 0) return "rect";
            if (n.IndexOf("streak", System.StringComparison.OrdinalIgnoreCase) >= 0) return "rect";
            if (n.IndexOf("wedge", System.StringComparison.OrdinalIgnoreCase) >= 0) return "rect";
            return "circle";
        }

        static void T(StringBuilder sb, ref bool first, float x, float y, int size,
                      string text, Color color, string note)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!first) sb.Append(",\n");
            first = false;
            sb.AppendFormat(
                "{{\"x\":{0:F1},\"y\":{1:F1},\"size\":{2},\"text\":\"{3}\",\"r\":{4:F2},\"g\":{5:F2},\"b\":{6:F2},\"note\":\"{7}\"}}",
                x, y, size, text.Replace("\\", "").Replace("\"", "'"),
                color.r, color.g, color.b, note);
        }

        // ── 增倍区文字 ────────────────────────────────────────────

        static void AppendZoneTexts(GameWorld world, StringBuilder sb, ref bool first)
        {
            float px = ZonePx();

            // 乘倍区倍率
            for (int i = 0; i < world.Zone.Gates.Count; i++)
            {
                var g = world.Zone.Gates[i];
                Vector2 sp = ToScreen(g.transform.position, true);
                // ★ 必须读 Cfg（以前写死 0.60，和 HudOverlay 脱钩了 —— 离线图会骗人）
                int size = Mathf.Clamp(Mathf.RoundToInt(Cfg.GateLabelHeight * 0.7f * px), 8, Cfg.HudFontSizeMax);
                T(sb, ref first, sp.x, sp.y - size * 0.61f, size, "x" + g.Multiplier, Color.black, "gate");

                // 通过次数（和 HudOverlay 同一套：位置由门自己给 CounterOffset、字号固定 GateCounterHeight）
                var cw = g.transform.position + (Vector3)g.CounterOffset;
                Vector2 cs = ToScreen(cw, true);
                int csize = Mathf.Clamp(Mathf.RoundToInt(Cfg.GateCounterHeight * px), 8, Cfg.HudFontSizeMax);
                T(sb, ref first, cs.x, cs.y - csize * 0.61f, csize,
                  BigNumber.Grouped(g.PassCount), Color.white, "gatecount");
            }

            // 增倍球
            for (int f = 0; f < world.Factions.Count; f++)
            {
                var fac = world.Factions[f];
                for (int i = 0; i < fac.Balls.Count; i++)
                {
                    var b = fac.Balls[i];
                    if (b == null || b.Consumed) continue;
                    Vector2 sp = ToScreen(b.transform.position, true);
                    int size = Mathf.Clamp(Mathf.RoundToInt(b.Radius * 1.7f * px), 10, 46);
                    T(sb, ref first, sp.x, sp.y - size * 0.61f, size,
                      BigNumber.SuffixedRounded(b.Value), fac.TextColor, "ball");
                }
            }


            // 底部武器区：白牌子 + 每格正上方的计数器。
            // ★ 位置必须和 HudOverlay.DrawWeaponBar / DrawWeaponCounters 用**同一套算法**
            //   （按增倍区投影 + DY 定位），否则离线图会骗人 ——
            //   这里以前写的是「屏幕底部往上留 6px」，和真实的 HUD 早就对不上了。
            var zone = world.Zone;
            int slotN = Cfg.WeaponSlotCount;
            var edges = WeaponZone.SlotEdgesWorld();
            float wzTop = Cfg.DY(Cfg.WeaponZoneTopPx);
            float wzBot = Cfg.DY(Cfg.WeaponZoneBottomPx);
            float plateH = Mathf.Max(Mathf.Abs(wzTop - wzBot) * px, 20f);
            float plateCy = (wzTop + wzBot) * 0.5f;
            int plateFont = Mathf.Clamp(Mathf.RoundToInt(plateH * 0.34f), 10, 24);
            int ctrFont = Mathf.Clamp(Mathf.RoundToInt(Cfg.WeaponCounterHeight * px), 8, Cfg.HudFontSizeMax);
            float ctrCy = Cfg.DY(Cfg.WeaponZoneTopPx - Cfg.WeaponCounterGapPx);
            string[] labels = Cfg.WeaponSlotNames;

            for (int i = 0; i < slotN && i + 1 < edges.Length; i++)
            {
                float cx = (edges[i] + edges[i + 1]) * 0.5f;

                Vector2 sp = ToScreen(zone.transform.TransformPoint(new Vector3(cx, plateCy, 0f)), true);
                T(sb, ref first, sp.x, sp.y - plateFont * 0.61f, plateFont, labels[i], Color.black, "slot");

                Vector2 sc = ToScreen(zone.transform.TransformPoint(new Vector3(cx, ctrCy, 0f)), true);
                long picks = zone.Weapons != null && i < zone.Weapons.PickCount.Length
                             ? zone.Weapons.PickCount[i] : 0L;
                T(sb, ref first, sc.x, sc.y - ctrFont * 0.61f, ctrFont,
                  BigNumber.Grouped(picks), Color.white, "wcount");
            }
        }

        static void AppendBattleTexts(GameWorld world, StringBuilder sb, ref bool first)
        {
            float px = BattlePx();
            for (int f = 0; f < world.Factions.Count; f++)
            {
                var fac = world.Factions[f];
                if (fac.Turret == null) continue;

                // ★★ 这里必须**逐字复刻 `HudOverlay.DrawTurretPlate`** ——
                //   之前这段是旧的（没有名字系统、数字字号也不用 Cfg.TurretTextHeight），
                //   结果离线图上根本没有名称那三行、数字也对不上，等于在骗人。
                Vector2 tp = ToScreen(fac.Turret.transform.position, false);
                float corePx = Mathf.Max(fac.Turret.CoreRadius * px, 8f);

                // 数字：固定世界高度（不跟炮台半径走）
                int resSize = Mathf.Clamp(Mathf.RoundToInt(Cfg.TurretTextHeight * px * 0.95f),
                                          11, Cfg.TurretTextSizeMax);

                // 子弹数量：三位分级 + 换行，贴着炮台（块底在炮台上方 corePx+2 处）
                string[] groups = BigNumber.Groups(fac.Bullets);
                float bulletLineH = resSize * Cfg.TurretBulletLineFactor;
                float bottomY = tp.y - corePx - 2f;
                for (int gi = 0; gi < groups.Length; gi++)
                {
                    float top = bottomY - (groups.Length - gi) * bulletLineH;
                    T(sb, ref first, tp.x, top, resSize, groups[gi], fac.TextColor, "bullets");
                }

                // 名称三行（头衔 / 名称 / 称号，空的那段整行不画；名称那行大一号）
                int nBaseSize = Mathf.Clamp(
                    Mathf.RoundToInt(Cfg.TurretNameTextHeight * px), 9, Cfg.TurretTextSizeMax);
                int nMidSize = Mathf.Clamp(
                    Mathf.RoundToInt(nBaseSize * Cfg.TurretNameMidScale), 10, Cfg.TurretTextSizeMax);
                float below = tp.y + corePx * (1f - Cfg.TurretLowerBlockLiftFactor) + 2f
                              + nBaseSize * Cfg.TurretNameDropEm;
                if (fac.Index >= 0 && fac.Index < Cfg.TurretNames.Length)
                {
                    var nd = Cfg.TurretNames[fac.Index];
                    below = NameLine(sb, ref first, tp.x, below, nd.Title, fac.TextColor, nBaseSize);
                    // 名字那行再单独往下让 0.10 em，算下一行时把偏移减回去（称号位置不变）
                    float nMidDrop = nMidSize * 0.10f;
                    below = NameLine(sb, ref first, tp.x, below + nMidDrop, nd.Name, fac.TextColor, nMidSize)
                            - nMidDrop;
                    below = NameLine(sb, ref first, tp.x, below, nd.Epithet, fac.TextColor, nBaseSize);
                }

                // 护盾量：名称下面 —— ★ 和 HudOverlay 一样，**固定按「两行名称」的高度**摆，
                // 不跟实际行数走（`Cfg.TurretShieldNameLines`），否则离线图和真实 HUD 会对不上。
                float shieldTop = tp.y + corePx * (1f - Cfg.TurretLowerBlockLiftFactor) + 2f
                                  + nBaseSize * Cfg.TurretNameDropEm;
                {
                    int lines = Mathf.Clamp(Cfg.TurretShieldNameLines, 0, 3);
                    for (int i = 0; i < lines; i++)
                        shieldTop += (i == 1 ? nMidSize : nBaseSize) * Cfg.TurretNameLineAdvance;
                }
                // 护盾数字再往上提一点点（和 HudOverlay 同一套）
                shieldTop -= nBaseSize * Cfg.TurretShieldLiftEm;
                T(sb, ref first, tp.x, shieldTop, resSize,
                  BigNumber.Suffixed(fac.Shield), fac.TextColor, "shield");
            }
        }

        /// <summary>和 `HudOverlay.DrawNameLine` 同一套：空行不占位置，行距 = 字号 × `TurretNameLineAdvance`。</summary>
        static float NameLine(StringBuilder sb, ref bool first, float x, float top,
                              string text, Color color, int size)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return top;
            T(sb, ref first, x, top, size, text, color, "name");
            return top + size * Cfg.TurretNameLineAdvance;
        }
    }
}
#endif
