using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 底部武器区。增倍球掉进来 = 「选了武器」：数值归 1，然后从顶部发射炮台重新打出去。
    /// 宽度比增倍区收一点，免得插进两侧的墙里。
    /// </summary>
    public class WeaponZone : MonoBehaviour
    {
        public MultiplierZone Zone;

        /// <summary>
        /// 每一格被增倍球「选中」过多少次。
        /// ★ 和乘法门的 `PassCount` **完全同一个机制**：计数器挂在**区**上（不挂在阵营上）——
        /// 增倍区是四家共用的一间，所以「这一格被选中过几次」本来就是全局的，
        /// 分阵营统计没有意义。显示也照乘法门那套来（见 HudOverlay.DrawWeaponBar）。
        /// </summary>
        public readonly long[] PickCount = new long[Cfg.WeaponSlotCount];

        public void NotePick(int slot)
        {
            if (slot >= 0 && slot < PickCount.Length) PickCount[slot]++;
        }

        /// <summary>
        /// 8 格的 x 分界（世界坐标，相对增倍区中心）。
        /// ★★ 这份边界是**常量**，而且 HUD **每帧要问两次**（画牌子、画计数器）——
        ///   以前每次都 `new float[]`，等于每帧白扔两个数组去喂 GC。
        ///   现在只算一次、返回同一个数组。
        /// ★ **调用方只许读，不许改这个数组**（它是共享的）。
        /// </summary>
        public static float[] SlotEdgesWorld()
        {
            if (_edges == null)
            {
                _edges = new float[Cfg.WeaponSlotXPx.Length];
                for (int i = 0; i < _edges.Length; i++) _edges[i] = Cfg.DX(Cfg.WeaponSlotXPx[i]);
            }
            return _edges;
        }

        static float[] _edges;

        public static WeaponZone Create(Transform parent, MultiplierZone zone)
        {
            float top = Cfg.DY(Cfg.WeaponZoneTopPx);
            float bottom = Cfg.DY(Cfg.WeaponZoneBottomPx);
            float h = Mathf.Abs(top - bottom);
            var edges = SlotEdgesWorld();
            float x0 = edges[0], x1 = edges[edges.Length - 1];

            var go = new GameObject("WeaponZone");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, (top + bottom) * 0.5f, 0f);

            var wz = go.AddComponent<WeaponZone>();
            wz.Zone = zone;

            // ★ 只保留**一个**总触发框：以前每一格各挂一个，
            //   大数值的球半径很大，会同时压到好几格 → 一次落点触发好几种武器
            //   （你看到的「大数值齐射会变成大球」就是这个）。
            //   落在第几格由落点的 x 现算。

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            // ★★ 规格（用户第 87 轮）：「**判定区在阻挡球底部下方一点的位置**」。
            //   阻挡球的中心就卡在白框顶边上、半径 = PostCapRadius →
            //   它的底面在「白框顶边 − PostCapRadius」；判定框的顶边再往下让一个间隙。
            //   （以前是「只盖下半截」，那个位置是拍的，不是量出来的。）
            float pegBottom = top - Cfg.PostCapRadius;
            float trigTop = Mathf.Min(pegBottom - Cfg.WeaponTriggerTopGap, top);
            float th = Mathf.Max(trigTop - bottom, 0.02f);
            float midY = (top + bottom) * 0.5f;
            // ★ 每格自己的判定线都从这条顶边往下让：x2 门 / 齐射格让得比整排更多
            //   （见 `Cfg.WeaponSlotTriggerDrop`，`Accept` 里按格判高度）。
            wz.TriggerTopLocalY = trigTop - midY;
            col.size = new Vector2(x1 - x0, th);
            col.offset = new Vector2(0f, trigTop - th * 0.5f - midY);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            // 视觉：浅灰衬底（只衬白格那一截）—— 以前它盖满整排，
            // 结果把下面那条**深灰底板**整个遮掉了（离线渲染图里一眼看出来）。
            float slabH0 = h - Mathf.Clamp(Cfg.WeaponBaseBarHeight, 0.05f, h * 0.6f);
            var baseSlab = Primitives.MakeNode("WeaponBase", Primitives.Square,
                new Color(0.78f, 0.78f, 0.80f), 4, go.transform);
            baseSlab.transform.localPosition = new Vector3(0f, (h - slabH0) * 0.5f, 0f);
            baseSlab.transform.localScale = new Vector3(x1 - x0 + 0.04f, slabH0 + 0.04f, 1f);

            // ★ 规格（用户）：「武器区也要**底板**」—— 白格下面留出一条深灰的地板，
            //   和乘法门下面那条阻挡条**同一个颜色**。**不带碰撞**（下面那段是选武器的触发区，
            //   加实体挡板球就掉不进来了）。
            float barH = Mathf.Clamp(Cfg.WeaponBaseBarHeight, 0.05f, h * 0.6f);
            var baseBar = Primitives.MakeNode("BaseBar", Primitives.Square,
                Cfg.BlockerGrey, 4, go.transform);
            baseBar.transform.localPosition = new Vector3(0f, -h * 0.5f + barH * 0.5f, 0f);
            baseBar.transform.localScale = new Vector3(x1 - x0, barH, 1f);
            // ★ 它是**实心**的（用户：球从上面掉进来，底板在最底下，挡不住「选武器」）
            var bbCol = baseBar.gameObject.AddComponent<BoxCollider2D>();
            bbCol.size = new Vector2(x1 - x0, barH);
            bbCol.sharedMaterial = Physics2DConfig.ZoneWall;

            // 白格：高度 = 整排高度 - 底板
            float plateH = h - barH - 0.06f;
            for (int i = 0; i < Cfg.WeaponSlotCount; i++)
            {
                float cx = (edges[i] + edges[i + 1]) * 0.5f;
                float w = (edges[i + 1] - edges[i]) - 0.14f;
                var plate = Primitives.MakeNode("Plate" + i, Primitives.Square, Color.white, 5, go.transform);
                plate.transform.localPosition = new Vector3(cx, (h - plateH) * 0.5f, 0f);
                plate.transform.localScale = new Vector3(w, plateH, 1f);

                var dot = Primitives.MakeNode("Dot" + i, Primitives.Hex,
                    new Color(0.86f, 0.86f, 0.88f), 6, go.transform);
                dot.transform.localPosition = new Vector3(cx, (h - plateH) * 0.5f + plateH * 0.10f, 0f);
                dot.transform.localScale = Vector3.one * (h * 0.34f);
            }

            // ★ 规格（用户）：「武器区也要**柱子**」—— 格子之间立一根，
            //   和乘法门之间那根**同一个样式/颜色**（深灰 + 撞到闪阵营色）。
            var posts = new GameObject("WeaponPosts");
            posts.transform.SetParent(go.transform, false);
            float postH = h;                     // 盖满整排（白格 + 底板）
            for (int i = 1; i < Cfg.WeaponSlotCount; i++)
            {
                var p = new GameObject("Post" + i);
                p.transform.SetParent(posts.transform, false);
                p.transform.localPosition = new Vector3(edges[i], 0f, 0f);

                // ★ 和门的柱子同一个颜色（阻挡球灰）
                var sr = Primitives.MakeNode("Body", Primitives.Square,
                    Cfg.BlockerGrey, 6, p.transform);
                sr.transform.localScale = new Vector3(Cfg.WeaponPostWidth, postH, 1f);

                var pc = p.AddComponent<BoxCollider2D>();
                pc.size = new Vector2(Cfg.WeaponPostWidth, postH);
                pc.sharedMaterial = Physics2DConfig.ZoneWall;
                // ★ 规格（用户）：碰到柱子不闪 → 不挂 BlockerBall

                // ★ 柱顶那颗阻挡球（和乘法门那些柱子同一个做法）
                MultiplierZone.AddPostCap(posts.transform, edges[i], postH * 0.5f);
            }
            // ★ 图二里**最右端**还有一颗球（贴着右墙），但左端没有 —— 照着量到的来。
            MultiplierZone.AddPostCap(posts.transform, edges[edges.Length - 1], postH * 0.5f);

            // ★★ 规格（用户第 87 轮 图二）：**顶上加一排阻挡球**，球就卡在白框顶边上。
            //    12 颗小球里，7 颗是柱顶球（上面已经加过了），剩下 5 颗是**平衡球**；
            //    外加护盾那格里的**一颗大阻挡球**（半径 0.257m，明显比别的大）。
            //    位置全部是**从图二量出来的**（见 Cfg.WeaponBalancePegXPx 的换算说明），
            //    不是把格子平分算出来的 —— 大球那格被三颗挡住，所以它最少见。
            {
                var bar = new GameObject("BalancePegs");
                bar.transform.SetParent(go.transform, false);
                float midY2 = (top + bottom) * 0.5f;
                float ly = Cfg.DY(Cfg.WeaponBalanceYPx) - midY2;          // 卡在白框顶边

                if (Cfg.WeaponBalancePegXPx != null)
                {
                    for (int i = 0; i < Cfg.WeaponBalancePegXPx.Length; i++)
                    {
                        var p = new GameObject("BalancePeg" + i);
                        p.transform.SetParent(bar.transform, false);
                        p.transform.localPosition =
                            new Vector3(Cfg.DX(Cfg.WeaponBalancePegXPx[i]), ly, 0f);
                        MakeBall(p, Cfg.WeaponBalancePegRadius, true);
                    }
                }
            }

            return wz;
        }

        /// <summary>一颗灰色阻挡球（半径是**世界半径**，自己带碰撞和闪烁）。</summary>
        static void MakeBall(GameObject go, float radius, bool flash)
        {
            var sr = Primitives.MakeNode("Body", Primitives.Circle, Cfg.BlockerGrey, 7, go.transform);
            sr.transform.localScale = Vector3.one * (radius * 2f);
            var pc = go.AddComponent<CircleCollider2D>();
            pc.radius = radius;
            pc.sharedMaterial = Physics2DConfig.ZoneWall;
            if (flash) BlockerBall.Attach(go, sr);
        }

        /// <summary>
        /// 落点在**第几格**？
        ///
        /// ★★ 这里原来有个**真 bug**：老写法拿「格心」去比，然后 `slot = i - 1`，
        ///   于是 x 落在 [edges[1], 中点) 这一段时算出来的是**上一格** ——
        ///   用户看到的就是「球明明进的是大球那格，加的却是护盾」。
        ///   现在老老实实找「edges[i] ≤ x &lt; edges[i+1]」的那一格。
        /// </summary>
        public static int SlotAt(float localX)
        {
            var edges = SlotEdgesWorld();
            int n = Mathf.Min(Cfg.WeaponSlotCount, edges.Length - 1);
            int slot = 0;
            for (int i = 0; i < n; i++)
                if (localX >= edges[i]) slot = i;      // 命中哪一格的**下界**就往哪一格走
            return Mathf.Clamp(slot, 0, Mathf.Max(n - 1, 0));
        }

        /// <summary>
        /// 整排触发框的**顶边**（本区局部坐标的 y）。
        /// 每格真正的判定线 = 这条线再往下让 `Cfg.WeaponSlotTriggerDrop(格)`：
        ///   整排只有一个总触发框（见 `Create` 里的说明），所以「某一格低一点」
        ///   是靠**在 Accept 里再判一次高度**做的 —— 球落在那一格上但还没沉到
        ///   它的判定线以下就先不接受（`OnTriggerStay2D` 会一直重试）。
        /// ★ 比的是**球的底边**（`y - ball.Radius`），不是圆心 —— 见 `Accept` 里的说明。
        /// </summary>
        public float TriggerTopLocalY;

        /// <summary>球掉进来了：落在第几格由落点的 x 决定。</summary>
        public void Accept(MultiplierBall ball)
        {
            if (ball == null || ball.Consumed) return;
            Vector3 lp = transform.InverseTransformPoint(ball.transform.position);
            int slot = SlotAt(lp.x);
            // ★ 有些格（x2 门 / 齐射）要**沉得比整排更低**才算落进去
            float drop = Cfg.WeaponSlotTriggerDrop(slot);
            if (drop > 0f)
            {
                // ★★ 比的是球的**底边**，不是圆心。
                //   `drop` 的语义是「球再往下沉这么多米」；拿圆心比的话，
                //   整颗球的半径会被白算成下落量 —— 半径 1m 的球设 0.01 也会沉 1.01m，
                //   看着就是「明明只调了 1 厘米，却下去了很多」。
                float ballBottom = lp.y - ball.Radius;
                if (ballBottom > TriggerTopLocalY - drop) return;
            }
            ball.OnPickedWeapon(slot);
        }
    }
}
