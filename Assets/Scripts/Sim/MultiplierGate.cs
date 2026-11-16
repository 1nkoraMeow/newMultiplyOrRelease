using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 乘法门（第一版）。一块**白色牌子**写 "x2 / x8 / x4 / x8 / x2"（黑字，Arimo Regular），
    /// 紧贴牌子下面一条**深灰判定条** —— 它才是真正的触发区，也是**阻挡物**：
    /// 增倍球要真的撞上去被弹开，而不是穿过去。
    ///
    /// 倍增在「第一次接触」时结算，离开后再碰到才会再乘一次。
    ///
    /// 顶上那扇 **x16** 是同一个类，只是形状换成了那块「帽子」（见 <see cref="CreateX16"/>）。
    /// </summary>
    public class MultiplierGate : MonoBehaviour
    {
        public int Multiplier = 2;
        public int GateIndex;
        /// <summary>牌子高度（世界单位）—— HUD 用它算计数器的高度。</summary>
        public float BlockHeight = 1f;
        public float BlockWidth = 1f;
        public bool IsSpecial;                 // true = 顶部 x16
        /// <summary>
        /// 「通过次数」相对门中心的偏移（世界单位）。
        /// ★ 做成门的属性而不是让 HUD 自己算：普通门的计数器在**上方**，
        ///   而 x16 上面坐着**发射炮台**，它的计数器必须在**下方**（截图里就是这样的）。
        /// </summary>
        public Vector2 CounterOffset = Vector2.zero;

        public long PassCount;

        /// <summary>过门时的闪烁强度（1 → 0）。</summary>
        float _flash;
        /// <summary>这一次闪的**目标色** —— 规格：用**过去那个增倍球的阵营色**。</summary>
        Color _flashColor = Cfg.GateFlashColor;
        SpriteRenderer _bodySr;
        /// <summary>这个门「不闪的时候」是什么颜色 —— 闪完要淡回它。建的时候一起定下来。</summary>
        Color _baseColor = Cfg.BlockerGrey;

        /// <summary>
        /// 球从这块门穿过去了：闪一下（零分配，比放一个爆炸特效便宜得多）。
        /// ★ 规格（用户）：**闪成那个增倍球的阵营色**，不是固定的一个颜色。
        /// </summary>
        public void Flash() { Flash(Cfg.GateFlashColor); }

        public void Flash(Color c) { _flashColor = c; _flash = 1f; }

        void Update()
        {
            if (_flash <= 0f || _bodySr == null) return;
            _flash = Mathf.Max(0f, _flash - Time.deltaTime / Mathf.Max(Cfg.GateFlashTime, 0.01f));
            _bodySr.color = Color.Lerp(_baseColor, _flashColor, _flash * _flash);   // 闪烁收得快一点
        }

        // ─────────────────────────────────────────────────────────────
        // 普通门（x2 / x4 / x8）
        // ─────────────────────────────────────────────────────────────

        public static MultiplierGate Create(Transform parent, Vector2 plateCenterLocalPos,
                                            float width, int multiplier, int gateIndex)
        {
            var go = new GameObject("Gate_x" + multiplier + "_" + gateIndex);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = plateCenterLocalPos;

            var gate = go.AddComponent<MultiplierGate>();
            gate.Multiplier = multiplier;
            gate.GateIndex = gateIndex;
            gate.BlockWidth = width;
            gate.BlockHeight = Cfg.GatePlateHeight;
            // 计数器在门**上方**（截图里计数器就压在牌子顶边上面一点）
            gate.CounterOffset = new Vector2(0f, Cfg.GatePlateHeight * 0.5f + Cfg.GateCounterGap);

            float tw = width * (1f + Cfg.GateTriggerWidthBonus);
            float plateTop = Cfg.GatePlateHeight * 0.5f;
            float plateBottom = -plateTop;

            // 白色牌子（"x2" 用黑字写在上面，由 HUD 画）
            var plate = Primitives.MakeNode("Plate", Primitives.Square, Color.white, 5, go.transform);
            plate.transform.localScale = new Vector3(width, Cfg.GatePlateHeight, 1f);

            // 白牌**下面**那条深灰**阻挡条**：它是实心的，球会撞上去
            float th = Cfg.GatePlateHeight * Cfg.GateTriggerHeightFactor;
            float barTop = plateBottom + 0.04f;
            float barBottom = barTop - th;

            var bar = Primitives.MakeNode("JudgeBar", Primitives.Square, Cfg.BlockerGrey, 4, go.transform);
            bar.transform.localPosition = new Vector3(0f, (barTop + barBottom) * 0.5f, 0f);
            bar.transform.localScale = new Vector3(tw, th, 1f);
            gate._bodySr = bar;                        // 闪烁闪的是这条阻挡条
            gate._baseColor = Cfg.BlockerGrey;              // 闪完淡回深灰

            // ★★ 规格（用户）：「判定调低一点，至少要球进了白框框将近一半再给他算乘倍成功」。
            //   判定体的**顶边**落在白牌从底边往上 GateTriggerTopFraction 的高度处
            //   （0.5 = 白牌正中间）→ **白牌上半截没有碰撞**，球要陷进牌子里将近一半才结算。
            float top = plateBottom + Cfg.GatePlateHeight * Cfg.GateTriggerTopFraction;
            float bottom = barBottom;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(tw, top - bottom);
            col.offset = new Vector2(0f, (top + bottom) * 0.5f);
            col.sharedMaterial = Physics2DConfig.ZoneWall;

            return gate;
        }

        /// <summary>
        /// 顶部那块**「帽子」形状的阻挡物**。规格（用户）：「我发你的那个不规则图形**纯用来阻挡用的**，
        /// 可以碰撞的」—— 它**不是**判定区，只是一块实心障碍。
        /// </summary>
        public static void CreateX16Barrier(Transform parent, Vector2 localPos)
        {
            var go = new GameObject("X16Barrier");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var body = Primitives.MakeNode("Body", Primitives.X16Shape, Cfg.BlockerGrey, 6, go.transform);
            body.transform.localScale = new Vector3(Cfg.X16BarrierWidth, Cfg.X16BarrierHeight, 1f);

            // 碰撞体用**同一条轮廓** —— 形状和贴图必须是同一个来源，各写一份迟早会对不上
            var col = go.AddComponent<PolygonCollider2D>();
            col.points = Primitives.X16ShapePoints(Cfg.X16BarrierWidth, Cfg.X16BarrierHeight);
            col.sharedMaterial = Physics2DConfig.ZoneWall;

            // ★ 规格（用户，第 82 轮）：「碰到…x16 上面的不规则阻挡物不需要闪烁，有点怪」
            //   → 帽子**不挂 BlockerBall**（它就是一块不动的障碍，闪起来反而怪）。
        }

        // ─────────────────────────────────────────────────────────────
        // 顶上的 x16
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 顶部的 **x16 判定区**。
        /// ★★ 规格（用户）：「看图二，**白色框的中间才是判定区**，
        /// 我发你的那个不规则图形**纯用来阻挡用的**」——
        /// 所以 x16 = **帽子（阻挡，另一个物体）** + **这块白底牌子**，
        /// 牌子压在帽子中间偏下，判定取**牌子的中间**。
        /// </summary>
        public static MultiplierGate CreateX16(Transform parent, Vector2 localPos)
        {
            var go = new GameObject("Gate_x16");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var gate = go.AddComponent<MultiplierGate>();
            gate.Multiplier = Cfg.X16Multiplier;
            gate.GateIndex = 1000;
            gate.IsSpecial = true;
            gate.BlockWidth = Cfg.X16PlateWidth;
            gate.BlockHeight = Cfg.X16PlateHeight;
            // ★ x16 的计数器在**下面**（上面是炮台，写上面会被压住）
            gate.CounterOffset = new Vector2(0f, Cfg.X16CounterOffsetY);

            // 白底牌子（黑字 "x16" 由 HUD 画在正中）
            var plate = Primitives.MakeNode("Plate", Primitives.Square, Color.white, 8, go.transform);
            plate.transform.localScale = new Vector3(Cfg.X16PlateWidth, Cfg.X16PlateHeight, 1f);
            gate._bodySr = plate;                      // 过门时这块白牌闪一下阵营色
            gate._baseColor = Color.white;             // 闪完淡回白色（不是灰色！）

            // 判定：**白框的中间**。做成实心（球要真的撞进去被弹开），
            // 顶边落在牌子正中 → 球得进到一半才结算（和其他门同一条规矩）。
            float top = Cfg.X16PlateHeight * (Cfg.GateTriggerTopFraction - 0.5f);
            float bottom = -Cfg.X16PlateHeight * 0.5f;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(Cfg.X16PlateWidth * (1f + Cfg.GateTriggerWidthBonus), top - bottom);
            col.offset = new Vector2(0f, (top + bottom) * 0.5f);
            col.sharedMaterial = Physics2DConfig.ZoneWall;

            return gate;
        }

        public double Apply(double value) { return value * Multiplier; }
    }
}
