#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MultiplyOrRelease.EditorTools
{
    /// <summary>
    /// 批处理自检：不需要进 Play 模式，直接把世界搭出来、跑一遍关键规则，
    /// 用来在没人看着的时候确认「构建 + 亡语 + 复活 + 碰撞」这些路径不会炸。
    ///
    /// 跑法：
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod MultiplyOrRelease.EditorTools.SmokeTest.Run -logFile -
    /// </summary>
    public static class SmokeTest
    {
        static int _failures;

        public static void Run()
        {
            _failures = 0;
            try
            {
                // ★★ 自检不跑 `Bootstrap`，所以 Unity 的**工程默认**物理设置会生效
                //   （`bounceThreshold` 默认是 **1.0**，游戏里是 0.05）——
                //   两边环境不一样，用户报的「大球撞墙不反弹」在自检里就复现不出来。
                //   这里显式对齐，测的才是线上那一套。
                Physics2D.bounceThreshold = Cfg.BounceThreshold;

                var world = GameWorld.Build();

                // ★★ 自检要能跑**任何阵营数**（2 / 4 / 8 / 16，见 `GameSettings.FactionCount`）——
                //   下面到处是 `Fac(3)` 这种写死的下标，那些都是「随便找一家 /
                //   找两家不同的」的意思，不是真在说"第 3 家"。用这两个小函数取，
                //   越界就绕回来，于是 2 家的 1v1 版也能把整套自检跑完。
                //   ★ `FacOther` 保证和 `Fac(k)` **不是同一家**（2 家时取另一家）。
                System.Func<int, Faction> Fac = delegate (int k)
                {
                    var l = world.Factions;
                    return l[((k % l.Count) + l.Count) % l.Count];
                };
                System.Func<int, Faction> FacOther = delegate (int k)
                {
                    var l = world.Factions;
                    return l[(((k + 1) % l.Count) + l.Count) % l.Count];
                };

                Check(world.Factions.Count == Cfg.FactionCount, "阵营数量");
                // ★ 第一版：左右各 5 块 + 顶上那扇 x16 = 11
                Check(world.Zone.Gates.Count == Cfg.GateMultipliers.Length * 2 + 1,
                    "乘法门数量（5×2 + x16）-> " + world.Zone.Gates.Count);
                {
                    // 普通门**全部**落在门那一排的高度上（第一版是齐平的，不再是一块比一块矮）
                    int atRow = 0, n = 0;
                    float sum = 0f;
                    for (int g = 0; g < world.Zone.Gates.Count; g++)
                    {
                        var gt = world.Zone.Gates[g];
                        if (gt.IsSpecial) continue;
                        if (Mathf.Abs(gt.transform.localPosition.y - Cfg.GatePlateY) < 1e-4f) atRow++;
                        sum += gt.transform.localPosition.y; n++;
                    }
                    Check(atRow == n && n == Cfg.GateMultipliers.Length * 2,
                        "10 块普通门都在同一排高度 -> " + atRow + "/" + n
                        + "（y=" + (n > 0 ? (sum / n).ToString("F2") : "?") + "）");
                }
                // ★ 浮力区**要保留**（用户第 78 轮）：从阻挡球那一排一直通到 x16 下方
                {
                    Check(world.Zone.Buoyancy != null, "浮力区存在");
                    var bz = world.Zone.Buoyancy;
                    if (bz != null)
                    {
                        Check(Mathf.Abs(bz.BottomY - Cfg.PegRowY) < 0.01f,
                            "浮力区起点拉到固定阻挡球那一排 -> y=" + bz.BottomY.ToString("F2"));
                        Check(bz.TopY < Cfg.X16BarrierY && bz.TopY > Cfg.GatePlateY,
                            "浮力区顶端接近 x16 下方 -> y=" + bz.TopY.ToString("F2"));
                        Check(bz.Height > 8f, "浮力区是一条长通道 -> 高 " + bz.Height.ToString("F1") + "m");

                        // ★★ 规格（用户，第 80 轮）：**不再是「进了区就施力」**，
                        //   而是**泡泡自己就是有碰撞、有一定密度的物理体**。
                        // ★ 第 107 轮：数量不再是固定值 —— 每帧生成 x 颗、到顶/到寿命就消失，
                        //   所以这里只验「有、而且不超过硬上限」，稳态数量另有一条。
                        Check(bz.Bubbles.Count > 0, "泡泡存在 -> " + bz.Bubbles.Count + " 颗");
                        Check(bz.Bubbles.Count <= Cfg.BuoyancyBubbleMax,
                            "泡泡数量不超过硬上限 -> " + bz.Bubbles.Count
                            + " ≤ " + Cfg.BuoyancyBubbleMax);
                        if (bz.Bubbles.Count > 0)
                        {
                            var b0 = bz.Bubbles[0];
                            var brb = b0.GetComponent<Rigidbody2D>();
                            Check(brb != null && brb.bodyType == RigidbodyType2D.Dynamic,
                                "泡泡是**动态刚体**（有物理碰撞能力）");
                            Check(brb != null && brb.mass > 0f
                                  && Mathf.Abs(brb.mass - Cfg.BuoyancyBubbleMass) < 1e-5f,
                                "泡泡有质量、而且就是 Cfg 里那个数 -> "
                                + (brb != null ? brb.mass.ToString("F4") : "?"));
                            Check(b0.GetComponent<CircleCollider2D>() != null, "泡泡有圆形碰撞体");
                            Check(brb != null && Mathf.Abs(brb.gravityScale) < 0.01f,
                                "泡泡不受重力（只会自己往上浮）");
                            // 半径：比上一版的光点大得多
                            float br = b0.localScale.x * 0.5f;
                            Check(br >= Cfg.BuoyancyBubbleRadiusMin - 1e-3f
                                  && br <= Cfg.BuoyancyBubbleRadiusMax + 1e-3f,
                                "泡泡半径在配置范围内 -> " + br.ToString("F3") + "m");
                        }
                        Check(Cfg.BuoyancyBubbleRiseSpeed > 1.6f,
                            "泡泡上浮速度加大 -> " + Cfg.BuoyancyBubbleRiseSpeed.ToString("F1") + " m/s");
                        // ★ 用户给的 Algodoo poststep 原文：`vel = vel + [0, 0.1]`
                        //   —— 每步加 0.1、**没有上限**，换算出来是 3 m/s²。
                        Check(Mathf.Abs(Cfg.BuoyancyBubbleRiseStepGain - 0.1f) < 1e-5f,
                            "泡泡上浮照抄原文 `vel = vel + [0, 0.1]` -> 每步 +"
                            + Cfg.BuoyancyBubbleRiseStepGain.ToString("F2") + " m/s");
                        Check(Mathf.Abs(Cfg.BuoyancyBubbleFreeRiseAccel
                                        - Cfg.BuoyancyBubbleRiseStepGain
                                          * Cfg.BuoyancyBubbleAlgodooFps) < 1e-4f,
                            "原文换算成帧率无关的加速度 -> "
                            + Cfg.BuoyancyBubbleFreeRiseAccel.ToString("F2") + " m/s²（"
                            + Cfg.BuoyancyBubbleAlgodooFps.ToString("F0") + " 步/秒）");

                        // ★★ 第 127 轮（用户）：「改完之后没推力了，推力大一点」——
                        //   加速度兼着「顶球推力」：推力 = 质量 × 加速度，必须压得住最轻的球。
                        //   球的重量 = BallMassMin × 9.81 × 重力倍率。
                        {
                            float ballWeight = Cfg.BallMassMin * 9.81f * Cfg.ZoneGravityScale;
                            float bubblePush = Cfg.BuoyancyBubbleMass * Cfg.BuoyancyBubbleRiseAccel;
                            Check(bubblePush >= ballWeight * 0.9f,
                                "单颗泡泡的顶球推力压得住最轻的球 -> m×a = "
                                + bubblePush.ToString("F3") + " N ≥ 球重 "
                                + ballWeight.ToString("F3") + " N");
                            // 上升「速度」：有阻尼时 = 加速度÷阻尼；无阻尼时靠 RiseSpeedMax 封顶
                            float terminal = Cfg.BuoyancyBubbleDrag > 1e-4f
                                ? Cfg.BuoyancyBubbleRiseAccel / Cfg.BuoyancyBubbleDrag
                                : (Cfg.BuoyancyBubbleRiseSpeedMax > 0f
                                    ? Cfg.BuoyancyBubbleRiseSpeedMax
                                    : Cfg.BuoyancyBubbleRiseAccel);
                            Check(terminal > 3f && terminal < 20f,
                                "上浮终端速度合理 -> " + terminal.ToString("F1") + " m/s（a="
                                + Cfg.BuoyancyBubbleRiseAccel.ToString("F0") + " / 阻尼 "
                                + Cfg.BuoyancyBubbleDrag.ToString("F2") + " / 上限 "
                                + Cfg.BuoyancyBubbleRiseSpeedMax.ToString("F1") + "）");
                        }
                        // ★ 第 129 轮（用户）：「不要阻尼」—— 阻尼必须为 0，
                        //   同时**必须**有速度上限兜底，否则无阻尼会一路加速到穿模。
                        Check(Cfg.BuoyancyBubbleDrag <= 0f,
                            "泡泡**没有阻尼**（用户：不要阻尼）-> drag="
                            + Cfg.BuoyancyBubbleDrag.ToString("F2"));
                        Check(Cfg.BuoyancyBubbleRiseSpeedMax > Cfg.BuoyancyBubbleRiseSpeed,
                            "无阻尼时用**速度上限**兜底（否则一路加速会穿模）-> 上限 "
                            + Cfg.BuoyancyBubbleRiseSpeedMax.ToString("F1") + " m/s > 出生 "
                            + Cfg.BuoyancyBubbleRiseSpeed.ToString("F1") + " m/s");
                        // 不封顶时 TransitTime 走「加速度 + 阻尼」的解析解，且要比寿命短
                        Check(BuoyancyZone.TransitTime < Cfg.BuoyancyBubbleLife,
                            "一颗泡泡在不被挡时能在寿命内走到顶 -> "
                            + BuoyancyZone.TransitTime.ToString("F2") + "s < 寿命 "
                            + Cfg.BuoyancyBubbleLife.ToString("F1") + "s");
                        Check(Cfg.BuoyancyBubbleRadiusMin > 0.04f
                              && Cfg.BuoyancyBubbleRadiusMin < Cfg.BuoyancyBubbleRadiusMax,
                            "泡泡半径合理（比旧光点大、比阻挡球小）-> r="
                            + Cfg.BuoyancyBubbleRadiusMin.ToString("F2")
                            + "~" + Cfg.BuoyancyBubbleRadiusMax.ToString("F2") + "m");
                        // ★★ 第 107 轮（用户）：「生成逻辑改成**每帧生成 x 个**，而不是固定的数量」——
                        //   所以「固定数量」那个参数必须**已经不存在**，换成一档生成速率 + 一个硬上限。
                        Check(typeof(Cfg).GetField("BuoyancyBubbleCount") == null,
                            "「固定泡泡数量」那个参数已经删掉（改成每帧生成）");
                        Check(Cfg.BuoyancyBubbleSpawnPerFrame > 0f,
                            "每帧生成速率 -> " + Cfg.BuoyancyBubbleSpawnPerFrame.ToString("F2") + " 颗/物理帧");
                        Check(Cfg.BuoyancyBubbleMax >= BuoyancyZone.SteadyCount,
                            "硬上限容得下稳态数量 -> 上限 " + Cfg.BuoyancyBubbleMax
                            + " ≥ 稳态 ≈ " + BuoyancyZone.SteadyCount.ToString("F0")
                            + "（= 速率 ÷ " + Time.fixedDeltaTime.ToString("F3")
                            + "s × 寿命 " + Cfg.BuoyancyBubbleLife.ToString("F1") + "s）");
                        // ★★ 第 92 轮（用户）：「起始点调到缓冲区（中间那一排阻挡球）的
                        //   **正中间三个球**」——那排 13 颗、间距 PegRowSpacing，
                        //   正中间三颗在 x = −1.5 / 0 / +1.5 → 出生半宽 = 一个间距
                        Check(Mathf.Abs(Cfg.BuoyancyBubbleSpawnHalfWidth - Cfg.PegRowSpacing) < 1e-4f,
                            "泡泡出生点 = 中间那三颗阻挡球 -> 半宽 "
                            + Cfg.BuoyancyBubbleSpawnHalfWidth.ToString("F2") + "m");
                        // ★★ 第 92 轮：对着**视频二**把通道放大到 5.0（参考里那是一片铺得很开的云，
                        //   不是我们视频一里那根密实的柱子）
                        // ★ `BuoyancyHalfWidth = Min(5.0, ZoneWidth×0.3 − 1.0)`；
                        //   增倍区收到 18m 之后这个上限是 4.4m（原来 20m 时正好 5.0）。
                        Check(Cfg.BuoyancyHalfWidth >= 4.0f,
                            "浮力通道横向够宽（视频二那种铺开的云，18m 区上限 4.4m）-> ±"
                            + Cfg.BuoyancyHalfWidth.ToString("F2") + "m");
                        Check(Cfg.BuoyancyBubbleColor.a > 0.2f && Cfg.BuoyancyBubbleColor.a < 0.55f,
                            "泡泡是**淡**的半透明（视频二里边缘柔和的圆）-> a="
                            + Cfg.BuoyancyBubbleColor.a.ToString("F2"));
                        Check(Cfg.BuoyancyBubbleSpawnHalfWidth < Cfg.BuoyancyHalfWidth,
                            "出生点比通道窄（泡泡上去之后自己铺开）-> "
                            + Cfg.BuoyancyBubbleSpawnHalfWidth.ToString("F2") + " < "
                            + Cfg.BuoyancyHalfWidth.ToString("F2"));

                        // ★★ 第 92 轮（用户）：「泡泡**不是随机生成**，而是**三个阻挡球都作为起始点**，
                        //   以 5° 浮动角度生成，形成**三排**浮力气泡生成区」
                        {
                            Check(Mathf.Abs(Cfg.BuoyancyBubbleSpreadDeg - 15f) < 1e-3f,
                                "泡泡由竖直 ±15° 浮动出发 -> ±"
                                + Cfg.BuoyancyBubbleSpreadDeg.ToString("F1") + "°");
                            // 出生点**贴着阻挡球顶面**（= 球半径），再叠加用户要的「上移一点」
                            Check(Mathf.Abs(Cfg.BuoyancyBubbleSpawnLift - Cfg.PegRadius) < 1e-4f,
                                "起始点贴着阻挡球顶面（不是浮空）-> 抬高 "
                                + Cfg.BuoyancyBubbleSpawnLift.ToString("F3") + "m");
                            Check(Cfg.BuoyancyBubbleSpawnRaise >= 0f,
                                "生成位置在 SpawnLift 之上再上移 -> +"
                                + Cfg.BuoyancyBubbleSpawnRaise.ToString("F3") + "m");
                            // ★★ 第 107 轮：泡泡改成**每帧生成**之后，出生点仍然是那三道源头
                            //   （中心 / ±一个间距）。这里直接反复问 `EmitLineOffset()`：
                            //   它只许返回这三个值之一，而且是随机的（不能永远同一道）。
                            {
                                int bad = 0;
                                int line0 = 0, lineL = 0, lineR = 0;
                                for (int t = 0; t < 90; t++)
                                {
                                    float off = (float)InvokeStaticRet(typeof(BuoyancyZone), "EmitLineOffset");
                                    float k = off / Cfg.PegRowSpacing;
                                    if (Mathf.Abs(k) < 1e-4f) line0++;
                                    else if (Mathf.Abs(k - 1f) < 1e-4f) lineR++;
                                    else if (Mathf.Abs(k + 1f) < 1e-4f) lineL++;
                                    else bad++;
                                }
                                Check(bad == 0,
                                    "泡泡出生点只在**三道源头**上（中心 / ±一个间距）-> 越界 " + bad);
                                Check(line0 > 0 && lineL > 0 && lineR > 0,
                                    "三道源头都会用到（不是只从一道冒）-> 中 " + line0
                                    + " / 左 " + lineL + " / 右 " + lineR);
                            }
                            // 泡泡之间互不碰撞（帧率）
                            Check(Cfg.BubbleLayer != Cfg.BulletLayer,
                                "泡泡有独立层（用来关掉泡泡之间的碰撞）-> layer "
                                + Cfg.BubbleLayer);
                        }

                        // ★★ 第 92 轮（用户）：「泡泡不一定只能竖直向上，**可以有向左和向右的分量**，
                        //   只要不会上不去都可以」——横向必须有分量，纵向必须恒定上浮。
                        {
                            Check(Cfg.BuoyancyBubbleSideSpeed > 0f
                                  && Cfg.BuoyancyBubbleSideDamp > 0.95f
                                  && Cfg.BuoyancyBubbleSideDamp <= 1f,
                                "泡泡允许横向速度、而且收得很慢 -> vx="
                                + Cfg.BuoyancyBubbleSideSpeed.ToString("F2")
                                + " 阻尼 " + Cfg.BuoyancyBubbleSideDamp.ToString("F3"));
                            // ★ 用户给的 Algodoo 原文：
                            //   pos(0) > -6 → vel -= [0.8,0]；pos(0) < -8 → vel += [0.8,0]
                            //   ⇒ 每步 0.8（30 步/秒 = 24 m/s²）、无阻尼。
                            //   死带宽度**按通道比例**取（原版 2m 是相对原版通道的绝对宽度，
                            //   照搬会把泡泡挤成中间一条柱子 —— 第 123 轮用户反馈「边收的太小」）。
                            Check(Cfg.BuoyancyBubbleLateralBand >= Cfg.BuoyancyHalfWidth * 0.6f
                                  && Cfg.BuoyancyBubbleLateralBand < Cfg.BuoyancyHalfWidth,
                                "横向死带铺得开（按通道比例，不是原版那 2m）-> ±"
                                + Cfg.BuoyancyBubbleLateralBand.ToString("F2") + "m / 通道半宽 "
                                + Cfg.BuoyancyHalfWidth.ToString("F2") + "m");
                            Check(Mathf.Abs(Cfg.BuoyancyBubblePushBackStepGain - 0.8f) < 1e-5f
                                  && Mathf.Abs(Cfg.BuoyancyBubblePushBack
                                               - Cfg.BuoyancyBubblePushBackStepGain
                                                 * Cfg.BuoyancyBubbleAlgodooFps) < 1e-4f,
                                "横向推回照原文 `vel ∓ [0.8, 0]` -> 每步 "
                                + Cfg.BuoyancyBubblePushBackStepGain.ToString("F2")
                                + " m/s = " + Cfg.BuoyancyBubblePushBack.ToString("F1") + " m/s²");
                            Check(Mathf.Abs(Cfg.BuoyancyBubbleSideDamp - 1f) < 1e-6f,
                                "横向**没有阻尼**（原文 poststep 里只有阈值推回这一条）");

                            var bz2 = world.Zone.Buoyancy;
                            var bs = (System.Collections.Generic.List<Rigidbody2D>)GetField(bz2, "_bodies");
                            if (bs != null && bs.Count > 0)
                            {
                                // 跑几个物理步：横向速度应该真的**活得下来**（不是被磨成 0）
                                for (int t = 0; t < 6; t++) Invoke(bz2, "FixedUpdate");
                                int sideWays = 0, upOk = 0;
                                for (int i = 0; i < bs.Count; i++)
                                {
                                    if (bs[i] == null) continue;
                                    if (Mathf.Abs(bs[i].velocity.x) > 0.05f) sideWays++;
                                    float minVy = Cfg.BuoyancyBubbleRiseSpeed
                                                  * Mathf.Cos(Cfg.BuoyancyBubbleSpreadDeg * Mathf.Deg2Rad);
                                    if (bs[i].velocity.y >= minVy * 0.99f) upOk++;
                                }
                                Check(sideWays > bs.Count / 10,
                                    "真的有泡泡在**斜着飘** -> " + sideWays + "/" + bs.Count);
                                // ★ 准则：纵向分量**永远** ≥ `RiseSpeed × cos(偏角上限)`
                                //   （刚出膛那一瞬间就是 cos(15°)×4 = 3.86，之后只会被向上的加速度抬得更高）
                                Check(upOk == bs.Count,
                                    "★ 每一个泡泡都还在**上浮**（不会被横向分量带得「上不去」）-> "
                                    + upOk + "/" + bs.Count);

                                // ★ 加速度真的生效：跑了几个物理步之后，纵向速度已经超过出生速度
                                float vTop = 0f;
                                for (int i = 0; i < bs.Count; i++)
                                    if (bs[i] != null) vTop = Mathf.Max(vTop, bs[i].velocity.y);
                                Check(vTop > Cfg.BuoyancyBubbleRiseSpeed + 1e-4f,
                                    "跑几步后纵向速度比出生时更快（向上加速度生效）-> "
                                    + vTop.ToString("F2") + " > "
                                    + Cfg.BuoyancyBubbleRiseSpeed.ToString("F2") + " m/s");

                                // ★★ 第 126 轮（用户）：「气泡被大球挡住之后速度应该从
                                //   **碰撞后的速度**开始累计」。用一颗刚生成的泡泡（Age=0，
                                //   这一帧不会被回收）把纵向速度改成 −2 模拟被大球压下来，
                                //   跑一步后应该从 −2 往上加，而不是跳回出生速度。
                                var freshBubbleObj = InvokeRet(bz2, "SpawnOne");
                                var blockProbe = freshBubbleObj != null
                                    ? GetField(freshBubbleObj, "Rb") as Rigidbody2D : null;
                                Check(blockProbe != null, "气泡：能取一颗刚生成的泡泡做「被挡住」验证");
                                if (blockProbe != null)
                                {
                                    blockProbe.velocity = new Vector2(0f, -2f);
                                    Invoke(bz2, "FixedUpdate");
                                    float step = Cfg.BuoyancyBubbleRiseAccel * Time.fixedDeltaTime;
                                    Check(Mathf.Abs(blockProbe.velocity.y - (-2f + step)) < 1e-3f,
                                        "气泡被挡住后从**碰撞后的速度**继续累加（不是跳回累加器）-> -2 + "
                                        + step.ToString("F3") + " = "
                                        + blockProbe.velocity.y.ToString("F3") + " m/s");
                                }
                            }
                        }

                        // ★ 推力 = 动量 (m×v) × 碰撞频率，两者都随速度涨 ——
                        //   加了上浮加速度之后通过时间变短，所以质量要往下配平（见 Cfg 里的推导）。
                        Check(Cfg.BuoyancyBubbleMass > 0f && Cfg.BuoyancyBubbleMass <= 0.05f,
                            "泡泡质量配平过（推力 = 动量 × 碰撞频率）-> m="
                            + Cfg.BuoyancyBubbleMass.ToString("F4"));
                        Check(Cfg.BuoyancyBubbleMass < Cfg.BallMassMin,
                            "泡泡比**最轻的球**还轻（不然顶不动/砸飞）-> "
                            + Cfg.BuoyancyBubbleMass.ToString("F4") + " < "
                            + Cfg.BallMassMin.ToString("F2"));
                        // ★ 「施力」那套必须**彻底没了**：整个 BuoyancyZone 不许再出现 HoldForce
                        Check(typeof(BuoyancyZone).GetMethod("HoldForce") == null,
                            "「进区就施力」那套已经删掉（改成物理泡泡了）");
                        // ★ 规格（用户）：「泡泡过 3 秒就消失」
                        Check(Cfg.BuoyancyBubbleLife > 0f && Cfg.BuoyancyBubbleLife <= 5f,
                            "泡泡有寿命 -> " + Cfg.BuoyancyBubbleLife + "s 到点就消失（不再瞬移回底部）");
                        Check(typeof(BuoyancyZone).GetNestedType("Bubble",
                                  System.Reflection.BindingFlags.NonPublic)?.GetField("Age") != null,
                            "每颗泡泡各自记着活了多久（Bubble.Age）");

                        // ★★ 第 107 轮（用户）：**每帧生成 x 个** ——
                        //   跑一个物理步，`SpawnedCount` 必须正好涨 `SpawnPerFrame`（还没到上限时）。
                        //   用「累计生成数」而不是「当前活着几颗」：后者同一步里还会有到寿命消失的，
                        //   净变化不等于生成数。
                        {
                            var bz4 = world.Zone.Buoyancy;
                            int s0 = bz4.SpawnedCount;
                            Invoke(bz4, "FixedUpdate");
                            int born = bz4.SpawnedCount - s0;
                            // 速率可以带小数（攒够一颗才生成），所以一帧是 floor 或 ceil 都算对
                            int lo = Mathf.FloorToInt(Cfg.BuoyancyBubbleSpawnPerFrame);
                            int hi = Mathf.CeilToInt(Cfg.BuoyancyBubbleSpawnPerFrame);
                            Check((born >= lo && born <= hi) || bz4.Bubbles.Count >= Cfg.BuoyancyBubbleMax,
                                "★ 每个物理帧生成 x 颗（x = "
                                + Cfg.BuoyancyBubbleSpawnPerFrame.ToString("F2") + "）-> 这一帧生成 "
                                + born + " 颗，通道里现在 " + bz4.Bubbles.Count + " 颗");

                            // 跑久一点：数量不能无限涨（硬上限），也不能把通道跑空
                            for (int t = 0; t < 400; t++) Invoke(bz4, "FixedUpdate");
                            Check(bz4.Bubbles.Count > 0 && bz4.Bubbles.Count <= Cfg.BuoyancyBubbleMax,
                                "跑 400 个物理步后数量仍然在 (0, 上限] 之间 -> "
                                + bz4.Bubbles.Count + " / " + Cfg.BuoyancyBubbleMax);
                        }

                        // ★★ 第 108 轮（用户）：「气泡超界就删除回收，不再反弹」——
                        //   把一颗挪到通道外面，跑一个物理步：它必须**消失**（收回池子），
                        //   而不是被弹回通道里。判据：这一步之后界外一颗都不剩。
                        {
                            var bz5 = world.Zone.Buoyancy;
                            var bs5 = (System.Collections.Generic.List<Rigidbody2D>)GetField(bz5, "_bodies");
                            if (bs5.Count > 0)
                            {
                                float lim = bz5.Width * 0.5f - Cfg.BuoyancyBubbleRadiusMax;
                                Vector2 outside = new Vector2(
                                    bz5.transform.position.x + lim + 0.5f, bs5[0].position.y);
                                bs5[0].position = outside;
                                Invoke(bz5, "FixedUpdate");

                                int stillOutside = 0;
                                var bs6 = (System.Collections.Generic.List<Rigidbody2D>)GetField(bz5, "_bodies");
                                for (int i = 0; i < bs6.Count; i++)
                                {
                                    if (bs6[i] == null) continue;
                                    if (Mathf.Abs(bs6[i].position.x - bz5.transform.position.x) > lim + 1e-3f)
                                        stillOutside++;
                                }
                                Check(stillOutside == 0,
                                    "超界的泡泡直接回收（不再反弹回来）-> 界外还剩 " + stillOutside);
                            }
                        }
                    }
                }
                // ★ x16 = **帽子（纯阻挡）** + **白框判定区**，两者是**两个物体**
                {
                    Check(world.Zone.X16 != null && world.Zone.X16.Multiplier == 16, "顶上那扇是 x16");
                    var xc = world.Zone.X16 != null ? world.Zone.X16.GetComponent<Collider2D>() : null;
                    Check(xc is BoxCollider2D,
                        "x16 的判定是**白框**（盒碰撞体），不是那块帽子");
                    // 帽子是另一个物体：多边形碰撞体 + 没有 MultiplierGate
                    var barrier = world.Zone.transform.Find("X16Barrier");
                    Check(barrier != null, "x16 帽子是独立的阻挡物");
                    if (barrier != null)
                    {
                        Check(barrier.GetComponent<PolygonCollider2D>() != null,
                            "帽子用多边形碰撞体（形状和贴图同一个来源）");
                        Check(barrier.GetComponentInChildren<MultiplierGate>() == null
                              && barrier.GetComponent<MultiplierGate>() == null,
                            "帽子**不是**判定区（纯阻挡 —— 从上面撞它不该吃 x16）");
                        Check(barrier.GetComponent<BlockerBall>() == null,
                            "x16 帽子**不闪**（用户：碰到上面的不规则阻挡物不需要闪烁）");
                    }
                    // ★ x16 的判定区必须够得着：它的下半截要露在**帽子那个缺口下面**，
                    //   否则球从浮力通道飘上来永远撞不到它（白框就成了摆设）。
                    if (world.Zone.X16 != null)
                    {
                        var xg = world.Zone.X16.GetComponent<BoxCollider2D>();
                        if (xg != null)
                        {
                            float trigBottom = world.Zone.X16.transform.localPosition.y
                                               + xg.offset.y - xg.size.y * 0.5f;
                            float hatBottom = Cfg.X16BarrierY - Cfg.X16BarrierHeight * 0.5f;
                            Check(trigBottom < hatBottom,
                                "x16 判定区从帽子缺口下面露出来（球飘上来够得着）-> 判定底 "
                                + trigBottom.ToString("F2") + " < 帽子底 " + hatBottom.ToString("F2"));
                        }
                    }
                    Check(world.Zone.X16 != null
                          && world.Zone.X16.transform.localPosition.y > Cfg.GatePlateY + 3f,
                        "x16 在普通门上方");

                    // ★★ 第 99 轮（用户）：「增倍球有时在传送回发射炮台时会被压进 x16」。
                    //   根因：x16 以前**不判方向**，任何接触都算 ×16 ——
                    //   传送回炮台那一下球正好压着判定区，就白吃一次。
                    //   现在 x16 **只认从下面吃**（`MultiplierBall.HitFromBelow`）。
                    //   这条断言把「那个 bug 再也不会发生」变得可验证：
                    //   **发射点必须在 x16 判定区上方** —— 于是传送过去那一下的接触法线朝上，
                    //   一定被 `HitFromBelow` 拒掉。
                    if (world.Zone.X16 != null)
                    {
                        Vector2 launchLocal = world.Zone.LauncherLocalPosition();
                        float launchY = world.Zone.transform.TransformPoint(launchLocal).y;
                        var xg2 = world.Zone.X16.GetComponent<BoxCollider2D>();
                        float trigTopY = world.Zone.X16.transform.position.y
                                         + (xg2 != null ? xg2.offset.y + xg2.size.y * 0.5f : 0f);
                        Check(launchY > trigTopY,
                            "★ 发射点在 x16 判定区**上方**（传送回去那一下压进去的方向是从上面 → 吃不到 ×16）-> 发射 "
                            + launchY.ToString("F2") + " vs 判定顶 " + trigTopY.ToString("F2"));
                    }

                    // ★★ 第 99 轮：炮台和复活判定都缩小
                    Check(Cfg.TurretRadius < 0.30f, "炮台缩小了 -> 半径 " + Cfg.TurretRadius.ToString("F2"));
                    Check(Cfg.HomeRadius < 1.0f, "复活判定范围缩小了 -> " + Cfg.HomeRadius.ToString("F2") + " m");
                }
                // ★ 规格（用户）：判定要**陷进白框将近一半**才算 —— 判定体顶边在白牌正中
                {
                    var g1 = world.Zone.Gates[1];          // Gates[0] 是 x16
                    var gcol = g1.GetComponent<BoxCollider2D>();
                    Check(gcol != null, "普通门有盒碰撞体");
                    if (gcol != null)
                    {
                        float plateTop = Cfg.GatePlateHeight * 0.5f;
                        float trigTop = gcol.offset.y + gcol.size.y * 0.5f;
                        Check(trigTop < plateTop - 0.1f,
                            "判定体没有盖到白牌上半截（球得陷进去才算）-> 判定顶 "
                            + trigTop.ToString("F2") + " < 牌子顶 " + plateTop.ToString("F2"));
                        Check(trigTop > Cfg.GatePlateHeight * (Cfg.GateTriggerTopFraction - 0.5f) - 0.02f,
                            "判定顶边差不多在白牌正中 -> " + trigTop.ToString("F2"));
                    }
                }
                // 发射炮台坐在 x16 顶上
                {
                    Check(world.Zone.Launcher != null, "发射炮台存在");
                    if (world.Zone.Launcher != null)
                    {
                        float ly = world.Zone.Launcher.transform.localPosition.y;
                        float apex = Cfg.X16BarrierY + Cfg.X16BarrierHeight * 0.5f;
                        // ★★ 规格（用户）：「炮座**一半在阻挡物里、一半在外面**」——
                        //   圆心要落在**帽尖的高度**上（偏移 0）。
                        //   之前我一次做成「相切」（球底正好在尖上 = 完全在外面）、
                        //   一次又抬到浮空，两次都不对。
                        Check(Mathf.Abs(ly - apex) < 0.05f,
                            "炮台圆心落在帽尖高度（下半颗嵌进帽子 = 一半在里面）-> 圆心 "
                            + ly.ToString("F2") + " vs 帽尖 " + apex.ToString("F2"));
                        Check(ly - Cfg.LauncherRadius < apex,
                            "球的最低点确实低于帽尖（真的「在里面」）");
                        Check(Cfg.LauncherBarrelLength > 0.75f,
                            "炮管加长了 -> " + Cfg.LauncherBarrelLength.ToString("F2") + "m");
                        Check(Cfg.LaunchSpeed >= 8.5f,
                            "发射初速度又提升了一次（7.4 → 8.6）-> "
                            + Cfg.LaunchSpeed.ToString("F2") + " m/s");
                        Check(Mathf.Abs(world.Zone.Launcher.AngleDeg) <= Cfg.LauncherSweepAngle + 1e-3f,
                            "炮口在扫描范围之内");
                        // ★ 用户：扫动改成 **sin 的非线性**，角度范围不变（仍是 ±LauncherSweepAngle）
                        Check(Cfg.LauncherSweepOmega > 0f,
                            "发射炮台按 sin 扫动 -> ω=" + Cfg.LauncherSweepOmega.ToString("F3")
                            + " rad/s（周期 ≈ " + (2f * Mathf.PI / Cfg.LauncherSweepOmega).ToString("F1") + "s）");
                        float peakDeg = Cfg.LauncherSweepAngle * Cfg.LauncherSweepOmega;
                        Check(Mathf.Abs(peakDeg / Cfg.LauncherSweepSpeed - Mathf.PI * 0.5f) < 1e-3f,
                            "sin 扫动的**平均**角速度 = LauncherSweepSpeed（来回节奏没变）-> 峰值 "
                            + peakDeg.ToString("F1") + " °/s");
                    }
                }

                // ── 大球撞护盾的三条规则 ──
                {
                    var atk = Fac(0);
                    var def = FacOther(0);   // 必须和 atk 不是同一家
                    var tb = world.SpawnBigBall(atk, 100d);

                    def.Shield = 1000d;                       // (a) 球比盾小
                    Invoke(tb, "ResolveShieldHit", def.Turret);
                    Check(System.Math.Abs(def.Shield - 925d) < 0.5 && System.Math.Abs(tb.Value - 25d) < 0.5,
                        string.Format("护盾(a) 盾={0:F0} 球={1:F0}", def.Shield, tb.Value));

                    tb.Value = 2000d; def.Shield = 1000d;     // (b) 不到 8 倍：盾剩 1/4，球扣 3/4
                    Invoke(tb, "ResolveShieldHit", def.Turret);
                    Check(System.Math.Abs(def.Shield - 250d) < 0.5 && System.Math.Abs(tb.Value - 1250d) < 0.5,
                        string.Format("护盾(b) 盾={0:F0} 球={1:F0}（盾 1000 剩 1/4 = 250，球 2000 − 750）",
                                      def.Shield, tb.Value));

                    tb.Value = 9000d; def.Shield = 1000d;     // (c) 8 倍以上 → 直接破盾，球扣掉整个盾
                    if (tb.Body != null) tb.Body.velocity = new Vector2(4f, 0f);   // 破盾前的速度
                    Invoke(tb, "ResolveShieldHit", def.Turret);
                    Check(def.Shield <= 0d && System.Math.Abs(tb.Value - 8000d) < 0.5,
                        string.Format("护盾(c) 盾={0:F0} 球={1:F0}（9000 − 盾 1000 = 8000）",
                                      def.Shield, tb.Value));
                    // ★ 用户：破盾**不反弹**，速度 ×1/20、方向不变继续前进
                    if (tb.Body != null)
                    {
                        Vector2 bv = tb.Body.velocity;
                        Check(Mathf.Abs(bv.magnitude - 4f * Cfg.BigBallShieldBreakSpeedScale) < 0.02f
                              && bv.x > 0f && Mathf.Abs(bv.y) < 0.02f,
                            "破盾后不反弹：速度 ×" + Cfg.BigBallShieldBreakSpeedScale.ToString("F2")
                            + "（4 → " + bv.magnitude.ToString("F2") + " m/s）、方向不变 -> " + bv);
                    }

                    // 密度：density = 值*0.9/(r²π)/800 → mass = 值*0.9/800
                    tb.Value = 800000d;
                    Invoke(tb, "ApplyRadius");
                    Check(tb.Body != null && System.Math.Abs(tb.Body.mass - 900d) < 5d,
                        "大球质量 = 值*0.9/800 -> " + (tb.Body != null ? tb.Body.mass.ToString("F1") : "?"));
                    tb.Consume();
                }

                // ★ 大球**数值文字**要有几帧过渡：突变后不是一帧到位，而是滚几帧再吸附
                {
                    var vb = world.SpawnBigBall(Fac(0), 1000000d);
                    if (vb != null)
                    {
                        vb.DisplayValue = 1000000d;
                        vb.Value = 2000000d;
                        Invoke(vb, "StepDisplayValue");
                        double after1 = vb.DisplayValue;
                        Check(after1 > 1000000d && after1 < 2000000d,
                            "数值突变后第一帧只滚一部分 -> " + BigNumber.Suffixed(after1));
                        for (int k = 0; k < 40; k++) Invoke(vb, "StepDisplayValue");
                        Check(System.Math.Abs(vb.DisplayValue - 2000000d) < 1d
                              && vb.DisplayValue == vb.Value,
                            "几十帧之后追上真实值并吸附 -> " + BigNumber.Suffixed(vb.DisplayValue));
                        vb.Consume();
                    }
                }

                // 大球：半径没有下限、碰墙完全弹性
                Check(Cfg.BigBallRadiusMin <= 0f, "大球半径没有下限");
                Check(Mathf.Abs(Physics2DConfig.Perfect.bounciness - 1f) < 1e-4 &&
                      Mathf.Abs(Physics2DConfig.Wall.bounciness - 1f) < 1e-4, "弹性系数 = 1.0");
                {
                    var bb = world.SpawnBigBall(Fac(2), 1d);
                    Check(bb.Radius < 0.20f, "数值 1 的大球可以很小 -> r=" + bb.Radius.ToString("F3"));
                }
                // 齐射：512 发，每发数值 = 齐射值/512 + 1
                Check(Cfg.VolleyBulletCount == 512, "齐射 512 发");
                Check(System.Math.Abs(Cfg.VolleyBulletValue(5120d) - (5120d / 512d + 1d)) < 1e-6,
                      "齐射单发数值 -> " + Cfg.VolleyBulletValue(5120d).ToString("F1"));
                // 投放方向不朝下
                {
                    bool allUp = true;
                    for (int k = 0; k < 40; k++)
                        if (world.Zone.RandomLaunchDirection().y <= 0.05f) allUp = false;
                    Check(allUp, "投放方向只往左上/右上");
                }

                // ── 选武器：增倍球的数值要真的换成对应的载荷 ──
                {
                    var wf = Fac(1);

                    // ★ 武器区计数器：和乘法门的 PassCount 一个机制 —— 挂在**区**上（四家共享）
                    var wz = world.Zone.Weapons;
                    Check(wz != null, "武器区存在");
                    Check(wz != null && wz.PickCount.Length == Cfg.WeaponSlotCount,
                        "武器区计数器每格一个");
                    int SLOT_SCATTER = Cfg.WeaponSlotIndexOf(WeaponSlotKind.Scatter);
                    int SLOT_SHIELD = Cfg.WeaponSlotIndexOf(WeaponSlotKind.Shield);
                    int SLOT_BIGBALL = Cfg.WeaponSlotIndexOf(WeaponSlotKind.BigBall);
                    int SLOT_DIRECT = Cfg.WeaponSlotIndexOf(WeaponSlotKind.Direct);
                    int SLOT_VAMPIRE = Cfg.WeaponSlotIndexOf(WeaponSlotKind.Vampire);
                    int SLOT_GATE2 = -1;
                    for (int i = 0; i < Cfg.WeaponSlots.Length; i++)
                        if (Cfg.WeaponSlots[i].IsGate) { SLOT_GATE2 = i; break; }

                    Check(SLOT_SCATTER == 2 && SLOT_SHIELD == 4 && SLOT_BIGBALL == 6
                          && SLOT_DIRECT == 0 && SLOT_GATE2 == 1
                          && (Cfg.CiwsEnabled || SLOT_VAMPIRE == 7),
                        "武器区 8 格的位置（图二 + 第 111 轮）：直射0 / x2-1 / 散射2 / 齐射3 / 护盾4 / x2-5 / 大球6 / 吸血7 -> "
                        + SLOT_DIRECT + "/" + SLOT_GATE2 + "/" + SLOT_SCATTER + "/"
                        + Cfg.WeaponSlotIndexOf(WeaponSlotKind.Volley) + "/" + SLOT_SHIELD + "/5/"
                        + SLOT_BIGBALL + "/" + SLOT_VAMPIRE);

                    long scatter0 = wz != null ? wz.PickCount[SLOT_SCATTER] : 0;
                    double ammoBefore = wf.Bullets;
                    var mbC = world.Zone.SpawnBall(wf, 800d);
                    mbC.OnPickedWeapon(SLOT_SCATTER);
                    Check(System.Math.Abs(wf.Bullets - (ammoBefore + 800d)) < 0.001,
                        "散射：数值变成弹药了");
                    Check(wf.TurretMode == WeaponSlotKind.Scatter, "散射：炮台切到散射模式");
                    Check(wz.PickCount[SLOT_SCATTER] == scatter0 + 1,
                        "武器区计数器：选中一次就 +1（区级，不分局）-> " + wz.PickCount[SLOT_SCATTER]);
                    wf.Balls.Remove(mbC);

                    // ★★ 直射（用户第 87 轮）：和散射一样是「射击模式」，也要把数值变成弹药
                    wf.TurretMode = WeaponSlotKind.Scatter;
                    double ammoD = wf.Bullets;
                    var mbD = world.Zone.SpawnBall(wf, 640d);
                    mbD.OnPickedWeapon(SLOT_DIRECT);
                    Check(System.Math.Abs(wf.Bullets - (ammoD + 640d)) < 0.001,
                        "直射：数值变成弹药了");
                    Check(wf.TurretMode == WeaponSlotKind.Direct, "直射：炮台切到直射模式");
                    wf.Balls.Remove(mbD);

                    // ★★ 武器区的 x2 格：球**不消耗**，数值翻倍后重投
                    double gateVal = 500d;
                    var mbG = world.Zone.SpawnBall(wf, gateVal);
                    long gatePicks0 = wz.PickCount[SLOT_GATE2];
                    mbG.OnPickedWeapon(SLOT_GATE2);
                    Check(System.Math.Abs(mbG.Value - gateVal * 2d) < 0.001
                          && !mbG.Consumed,
                        "武器区 x2：数值翻倍且球没被消耗 -> " + mbG.Value.ToString("F0"));
                    Check(wz.PickCount[SLOT_GATE2] == gatePicks0 + 1, "武器区 x2 也计数");

                    // ★ 存活时间（用户）：右上方显示、格式 m:ss、不足 20 秒不显示，
                    //   碰到判定框回到炮台时归零。
                    Check(HudOverlay.ClockText(20f) == "0:20"
                          && HudOverlay.ClockText(65.9f) == "1:05"
                          && HudOverlay.ClockText(3600f) == "60:00"
                          && HudOverlay.ClockText(19.9f) == "0:19",
                        "存活时间格式 m:ss（向下取整）-> " + HudOverlay.ClockText(65.9f));
                    Check(Mathf.Approximately(HudOverlay.LifeClockMinSeconds, 20f),
                        "存活时间 < 20 秒不显示 -> 阈值 " + HudOverlay.LifeClockMinSeconds);
                    mbG.LifeTime = 33.4f;
                    mbG.OnPickedWeapon(SLOT_GATE2);      // 撞判定框 → 回炮台
                    Check(mbG.LifeTime <= 1e-4f,
                        "撞判定框回到炮台 → 存活时间归零 -> " + mbG.LifeTime.ToString("F2"));

                    wf.Balls.Remove(mbG);

                    double shieldBefore = wf.Shield;
                    var mbS = world.Zone.SpawnBall(wf, 700d);
                    mbS.OnPickedWeapon(SLOT_SHIELD);
                    Check(System.Math.Abs(wf.Shield - (shieldBefore + 700d)) < 0.001,
                          "护盾：数值加进护盾了 -> " + wf.Shield);
                    Check(System.Math.Abs(mbS.Value - 1d) < 0.001, "选完武器数值归 1");

                    int bigBefore = wf.BigBalls.Count;
                    var mbB = world.Zone.SpawnBall(wf, 900d);
                    mbB.OnPickedWeapon(SLOT_BIGBALL);
                    Check(wf.BigBalls.Count == bigBefore + 1, "大球：真的出了一颗大球");
                    {
                        var nb = wf.BigBalls[wf.BigBalls.Count - 1];
                        Vector2 tp = wf.Turret.WorldPosition;
                        float dp = Vector2.Distance(nb.transform.position, tp);
                        Check(dp < 0.6f, "大球是从炮台出来的 -> 距离 " + dp.ToString("F2"));
                    }

                    // ★★ 第 111 轮：第 8 格（吸血球）也要真的走「选武器」那条路 ——
                    //   不是只有 `SpawnBigBall(..., true)` 能生吸血球。
                    {
                        int vampBefore = wf.BigBalls.Count;
                        var mbV = world.Zone.SpawnBall(wf, 950d);
                        mbV.OnPickedWeapon(SLOT_VAMPIRE);
                        Check(wf.BigBalls.Count == vampBefore + 1,
                            "吸血球：选这一格真的出了一颗");
                        var nv = wf.BigBalls[wf.BigBalls.Count - 1];
                        Check(nv.IsVampire, "吸血球那格生出来的是**吸血球**（带特效）");
                        nv.Consume();
                        wf.BigBalls.Remove(nv);
                        wf.Balls.Remove(mbV);
                    }

                    int volBefore = world.Bullets.ActiveCount;
                    world.FireVolley(wf, 900d);
                    Check(world.PendingVolleys > 0, "齐射：排进了待发队列");
                    world.FlushVolleysForTest();
                    Check(world.Bullets.ActiveCount > volBefore + Cfg.VolleyBulletCount / 2,
                        "齐射：512 发真的打出去了 -> " + world.Bullets.ActiveCount);

                    Debug.Log("[SMOKE] 武器区: 直射/护盾/大球/齐射 都真的生效了");

                    // 清理：这两颗是测试用的，别影响后面「开局增倍球数量」的断言
                    wf.Balls.Remove(mbS);
                    wf.Balls.Remove(mbB);
                }
                // ★ 规格（用户）：增倍球半径 = ln(数值)/除数 + 0.05，**没有下限**
                //   ★ 除数**不要写死**（55→50→35 改过三次）—— 一律读 Cfg。
                {
                    double vm = 1000000d;                      // 1M
                    float want = Mathf.Log((float)vm) / Cfg.MultiplierBallRadiusLogDivisor
                                 + Cfg.MultiplierBallRadiusBias;
                    // ★ 除数**不许写死**（55→50→35→30 改过四次）—— 一律从 Cfg 读回来比对
                    Check(Mathf.Abs(want - (Mathf.Log(1000000f) / Cfg.MultiplierBallRadiusLogDivisor
                                            + Cfg.MultiplierBallRadiusBias)) < 1e-4f,
                        "半径公式 = ln(值)/" + Cfg.MultiplierBallRadiusLogDivisor
                        + " + " + Cfg.MultiplierBallRadiusBias + " -> 1M 得 " + want.ToString("F3") + "m");
                    // 数值 1 → 半径正好是那个 +0.05（公式自带的地板），也验证了「没有额外下限」
                    Check(Mathf.Abs(Cfg.MultiplierBallRadiusMin) < 1e-6f,
                        "增倍球半径**没有下限**（公式里的 +0.05 就是唯一的地板）");
                    Check(Cfg.MultiplierBallRadiusLogDivisor > 1f
                          && Cfg.MultiplierBallRadiusBias > 0f, "公式参数为正");
                }
                // 质量公式：只有一处来源（Cfg.MultiplierBallMass），增倍球的刚体质量读它
                Check(Cfg.MultiplierBallMass(262000d) > Cfg.MultiplierBallMass(1000d),
                    "增倍球质量随数值增长 -> 262K "
                    + Cfg.MultiplierBallMass(262000d).ToString("F3") + " vs 1K "
                    + Cfg.MultiplierBallMass(1000d).ToString("F3"));
                // ★ 第一版的门：左右各 5 块、从外往内 x2 x8 x4 x8 x2；两组都贴着自己的墙
                {
                    var mult = Cfg.GateMultipliers;
                    // ★★★ 第 103 轮（用户发原版录像）：「复刻这个增倍区」→ 回到**左右各 5 块**，
                    //   从贴墙往里 x2 x8 x4 x8 x2。
                    Check(mult.Length == 5 && mult[0] == 2 && mult[1] == 8 && mult[2] == 4
                          && mult[3] == 8 && mult[4] == 2,
                        "门的倍率从**贴墙往里** x2 x8 x4 x8 x2（第 103 轮按原版录像改的）");
                    Check(Cfg.GateWidths.Length == mult.Length,
                        "每块门的宽度都有 >> " + Cfg.GateWidths.Length + " 个");
                    // ★ 宽窄层次：**两个 x8 相等且最窄 < x4 < 贴墙 x2**（录像里量过的相对关系，仍然成立）
                    // ★★ 用户要求「最靠近中间的两个 x2 改小」之后，**里侧 x2 不再是最宽的** ——
                    //   所以原来那条 `GateWidths[4] > GateWidths[0]` 已作废，
                    //   这里改成只锁住**仍然成立**的那部分关系：两个 x8 相等、且都比 x4 窄。
                    // ★★ 用户后来把**里侧 x2 又调宽了一点**（1.45 > 贴墙 1.40），
                    //   所以「贴墙 x2 最宽」这条也作废了。现在只锁仍然成立的层次：
                    //   两个 x8 相等且最窄 < x4 < 两个 x2。
                    Check(Mathf.Abs(Cfg.GateWidths[1] - Cfg.GateWidths[3]) < 1e-4f
                          && Cfg.GateWidths[2] > Cfg.GateWidths[1]
                          && Cfg.GateWidths[0] > Cfg.GateWidths[2]
                          && Cfg.GateWidths[4] > Cfg.GateWidths[2],
                        "两个 x8 相等且最窄、x4 次之、两个 x2 都更宽 -> "
                        + Cfg.GateWidths[0].ToString("F2") + " / "
                        + Cfg.GateWidths[1].ToString("F2") + " / "
                        + Cfg.GateWidths[2].ToString("F2") + " / "
                        + Cfg.GateWidths[3].ToString("F2") + " / "
                        + Cfg.GateWidths[4].ToString("F2"));
                    // 一组的「块 + 缝」总宽必须**小于**半幅，否则会插到对面去
                    float group = 0f;
                    for (int k = 0; k < mult.Length; k++) group += Cfg.GateWidths[k];
                    group += Cfg.GateGap * (mult.Length - 1);
                    Check(group < Cfg.ZoneWidth * 0.5f,
                        "一组门的总宽 " + group.ToString("F2") + "m 放得进半幅 "
                        + (Cfg.ZoneWidth * 0.5f).ToString("F2") + "m");

                    // ★★ 缝隙里**不能有洞**。第 89 轮改了做法：柱子宽度**正好等于缝宽**，
                    //   所以门的判定体不再互相重叠 —— 洞是靠**柱子的碰撞体**堵上的。
                    {
                        var postsT = world.Zone.transform.Find("GatePosts");
                        var pc0 = postsT != null ? postsT.GetComponentInChildren<BoxCollider2D>() : null;
                        Check(pc0 != null && pc0.size.x >= Cfg.GateGap - 1e-3f,
                            "缝由柱子的碰撞体堵住（柱宽 >= 缝宽）-> "
                            + (pc0 != null ? pc0.size.x.ToString("F3") : "?")
                            + " >= " + Cfg.GateGap.ToString("F3"));
                        Check(Cfg.GatePostWidth <= Cfg.GateGap + 1e-3f,
                            "柱子不啃牌子（柱宽 <= 缝宽）-> " + Cfg.GatePostWidth.ToString("F3")
                            + " <= " + Cfg.GateGap.ToString("F3"));

                        // ★★ 第 91 轮（用户：「所有 x2 x4 x8 字样都得在白牌**正中间**」）——
                        //   根因是**最里面那根柱子往里多压了半个柱宽**，把最里面那块白牌吃掉了 0.20m：
                        //   白牌可见部分不再对称，字看着就偏了（实测只有那 2 块门偏了 +6/−7 px）。
                        //   现在柱子一律落在缝里，**任何一根都不许压到牌子**。
                        int overlap = 0;
                        var posts2 = world.Zone.transform.Find("GatePosts");
                        var pcs = posts2 != null
                            ? posts2.GetComponentsInChildren<BoxCollider2D>()
                            : new BoxCollider2D[0];
                        for (int i = 0; i < pcs.Length; i++)
                        {
                            // ★ 必须用**局部**坐标：下面那些牌子边界是按增倍区局部坐标算的
                            //   （增倍区整体挂在 ZoneCenter = (-170,0)，用世界坐标比会永远「不重叠」，
                            //    这条断言就会永远绿 —— 第一次写就是这么错的，突变检查才发现）。
                            float px = pcs[i].transform.localPosition.x;
                            float half = pcs[i].size.x * 0.5f;
                            // 柱子落在两组的「组内」范围里才算：它必须在某块牌的**外面**
                            for (int si = 0; si < 2; si++)
                            {
                                float sd = si == 0 ? -1f : 1f;
                                float cur = sd * (Cfg.ZoneWidth * 0.5f - Cfg.GateSideMargin);
                                for (int c = 0; c < mult.Length; c++)
                                {
                                    float outer = cur, inner = cur - sd * Cfg.GateWidths[c];
                                    float lo = Mathf.Min(outer, inner), hi = Mathf.Max(outer, inner);
                                    if (px + half > lo + 1e-3f && px - half < hi - 1e-3f) overlap++;
                                    cur = inner - sd * Cfg.GateGap;
                                }
                            }
                        }
                        Check(overlap == 0,
                            "没有柱子压在白牌上（字才能落在牌心）-> 压到 " + overlap + " 处");

                        // ★★★ 第 103 轮（用户）：「**x4 x8 上不需要加阻挡球**」——
                        //   原本那两档门上各有一颗固定阻挡球，现在**一颗都不长**
                        //   （`GateTopBlockerMultipliers` 清空；机制留着，填倍率就能加回来）。
                        var tops = new System.Collections.Generic.List<Transform>();
                        var allC = world.Zone.GetComponentsInChildren<Transform>(true);
                        for (int i = 0; i < allC.Length; i++)
                            if (allC[i].name == "GateTopBlocker") tops.Add(allC[i]);
                        int wantTops = 0;
                        for (int mm = 0; mm < Cfg.GateTopBlockerMultipliers.Length; mm++)
                            for (int mv = 0; mv < mult.Length; mv++)
                                if (mult[mv] == Cfg.GateTopBlockerMultipliers[mm]) wantTops++;
                        wantTops *= 2;      // 左右两组
                        // ★ 判据按**倍率**数（不按档数）：第 103 轮回到 5 块门之后，
                        //   每一组里有**两块 x8**，所以是 (x4 + 2×x8) × 2 组 = 6 颗。
                        Check(tops.Count == wantTops,
                            "★ 门顶上的**固定阻挡球**：按倍率表数出来 = " + wantTops
                            + "，场上实际 " + tops.Count
                            + (wantTops == 0 ? "（第 103 轮用户要求：x4 x8 上不加）" : ""));
                        if (tops.Count > 0)
                        {
                            // 球心必须在**白牌顶边**上，横向对正牌心
                            float wantY = world.Zone.transform.position.y
                                          + Cfg.GatePlateY + Cfg.GatePlateHeight * 0.5f;
                            var bc0 = tops[0].GetComponent<CircleCollider2D>();
                            Check(bc0 != null && Mathf.Abs(bc0.radius - Cfg.GateTopBlockerRadius) < 1e-4f
                                  && Cfg.GateTopBlockerRadius < Cfg.PostCapRadius,
                                "那颗球比柱顶球小（图里量出来是它的一半）-> r="
                                + Cfg.GateTopBlockerRadius.ToString("F3"));
                            Check(Mathf.Abs(tops[0].position.y - wantY) < 1e-3f,
                                "球心落在白牌顶边上 -> " + tops[0].position.y.ToString("F3")
                                + " vs " + wantY.ToString("F3"));
                            // 每一颗都必须**正对**某一块 x4/x8 的牌心，不能站错门
                            int matched = 0, wrong = 0;
                            for (int i = 0; i < tops.Count; i++)
                            {
                                float tx = tops[i].position.x;
                                var g = world.Zone.Gates.Find(delegate (MultiplierGate gg)
                                    { return Mathf.Abs(gg.transform.position.x - tx) < 1e-3f; });
                                if (g != null
                                    && System.Array.IndexOf(Cfg.GateTopBlockerMultipliers, g.Multiplier) >= 0)
                                    matched++;
                                else wrong++;
                            }
                            Check(wrong == 0 && matched == tops.Count,
                                "每颗球都正对一块 x4/x8 的牌心 -> 对 " + matched + " / 错 " + wrong);
                        }
                    }

                    // ★ 但**视觉上的缝是真的**（用户看得到），只是它不构成通道
                    Check(Cfg.GateGap > 0f, "牌子之间有可见的缝 -> " + Cfg.GateGap.ToString("F3") + "m");

                    // ★★ 规格（用户，第 79 轮）：缝里要立一根**看得见的灰色柱子**（带碰撞）
                    {
                        var posts = world.Zone.transform.Find("GatePosts");
                        Check(posts != null, "门与门之间立了柱子（阻挡墙）");
                        if (posts != null)
                        {
                            // 每组 = 内缝数 + **最里面那端 1 根** = 块数
                            int want = 2 * mult.Length;
                            int got = posts.GetComponentsInChildren<BoxCollider2D>().Length;
                            Check(got == want,
                                "柱子数量 = 两组 × 每组 " + mult.Length + " 根 -> " + got + "/" + want);
                            // ★ 规格（用户，第 82 轮）：「碰到柱子…不需要闪烁」→ 柱子和柱顶球都不挂
                            Check(posts.GetComponentsInChildren<BlockerBall>(true).Length == 0,
                                "柱子和柱顶球**不闪**（用户说闪起来怪）");
                            // 柱子要**盖满整块门的高度**，否则上半截还是能钻过去
                            var pc = posts.GetComponentInChildren<BoxCollider2D>();
                            Check(pc != null && pc.size.y >= Cfg.GatePlateHeight,
                                "柱子盖满整块门 -> 高 " + (pc != null ? pc.size.y.ToString("F2") : "?")
                                + "m >= 牌高 " + Cfg.GatePlateHeight.ToString("F2") + "m");
                            // ★★ 第 91 轮（用户）：「柱子的**长度略微缩小**，柱子上的阻挡球相应降低，
                            //   使柱顶球的**顶部也只略微高于白框一点**」——
                            //   ★ 注意是**乘倍区**的门柱（用户特意更正过我别改武器区那边）
                            {
                                var capT = null as Transform;
                                var cs = posts.GetComponentsInChildren<Transform>(true);
                                for (int ci = 0; ci < cs.Length; ci++)
                                    if (cs[ci].name == "PostCap") { capT = cs[ci]; break; }

                                float plateTopW = world.Zone.transform.position.y
                                                  + Cfg.GatePlateY + Cfg.GatePlateHeight * 0.5f;
                                float stickOut = capT != null
                                    ? (capT.position.y + Cfg.PostCapRadius) - plateTopW : 0f;
                                Check(capT != null && stickOut > 0f
                                      && stickOut < Cfg.PostCapRadius * 0.6f,
                                    "门柱顶球只**露出白牌一点点** -> 露出 "
                                    + stickOut.ToString("F3") + "m（球半径 "
                                    + Cfg.PostCapRadius.ToString("F2") + "）");
                                Check(Mathf.Abs(Cfg.GatePostTopDrop
                                                - (Cfg.PostCapRadius - stickOut)) < 1e-3f,
                                    "门柱顶下移量 = 球半径 − 露出高度 -> "
                                    + Cfg.GatePostTopDrop.ToString("F3"));
                                Check(pc != null && pc.size.y < Cfg.GatePlateHeight + 0.9f,
                                    "柱子确实**变短**了（没有盖到白牌顶）-> 高 "
                                    + (pc != null ? pc.size.y.ToString("F2") : "?"));
                            }
                            // ★ 规格（用户）：「贴墙那两颗阻挡球分别**向外移**，让它们**一半嵌进墙里**」
                    {
                        float face = Cfg.ZoneWidth * 0.5f;      // 墙内壁
                        for (int i = 0; i < Cfg.WallBlockerX.Length; i++)
                        {
                            float cx = Cfg.WallBlockerX[i];
                            Check(Mathf.Abs(Mathf.Abs(cx) - face) < 0.02f,
                                "贴墙阻挡球的圆心落在墙面上（一半在墙里）-> x="
                                + cx.ToString("F2") + " 墙面 ±" + face.ToString("F2"));
                            Check(Mathf.Abs(cx) - Cfg.BlockerRadius < face,
                                "它有半个身子在墙里（圆心没被推到墙外面去）");
                        }
                    }
                    Check(Cfg.GatePostWidth >= Cfg.GateGap,
                                "柱子比缝宽，正好咬住两边牌子 -> " + Cfg.GatePostWidth.ToString("F3")
                                + " >= " + Cfg.GateGap.ToString("F3"));

                            // ★ 规格（用户）：柱子要和白牌下面的**阻挡条同一个颜色**。
                            //   直接读**渲染器上真实的颜色**比 —— 只比常量的话，
                            //   像 BlockerBall.Attach 拿默认灰把它覆盖掉这种 bug 就漏过去了
                            //   （这个 bug 真的发生过，是靠离线渲染图看出来的）。
                            // ★ 规格（用户，第 82 轮）：柱子要和**阻挡球**一个颜色（浅灰）。
                            //   深灰的柱子和背景几乎一样，用户根本看不见（「柱子还是没加进去吧」）。
                            var postSr = posts.GetComponentsInChildren<SpriteRenderer>()[0];
                            if (postSr != null)
                            {
                                float d = Mathf.Abs(Cfg.BlockerGrey.r - postSr.color.r)
                                        + Mathf.Abs(Cfg.BlockerGrey.g - postSr.color.g)
                                        + Mathf.Abs(Cfg.BlockerGrey.b - postSr.color.b);
                                Check(d < 1e-4f,
                                    "柱子和阻挡球**是同一个颜色** -> 色差 " + d.ToString("F4"));
                                var bg = new Color(0.10f, 0.10f, 0.11f);
                                float db = Mathf.Abs(postSr.color.r - bg.r)
                                         + Mathf.Abs(postSr.color.g - bg.g)
                                         + Mathf.Abs(postSr.color.b - bg.b);
                                Check(db > 0.5f, "柱子颜色和背景分得开（看得见）-> 色差 " + db.ToString("F2"));
                            }
                        }
                    }
                }
                // ★ 五种武器图标（规格：照参考图做的剪影）
                {
                    var kinds = new[] { WeaponSlotKind.Direct, WeaponSlotKind.Scatter,
                                        WeaponSlotKind.Volley, WeaponSlotKind.Shield,
                                        WeaponSlotKind.BigBall };
                    int ok = 0;
                    for (int k = 0; k < kinds.Length; k++)
                    {
                        var ic = Primitives.WeaponIcon(kinds[k]);
                        if (ic != null && ic.texture != null && ic.texture.width > 16) ok++;
                    }
                    Check(ok == kinds.Length, "五个武器图标都生成了 -> " + ok + "/" + kinds.Length);
                    // ★★ 直射（用户第 89 轮）：「直接把图一放上去就行了」——
                    //   必须是**内嵌位图**（我按图一生成的 368×76 剪影），不是程序兜底形状（160×160）。
                    var pd = Primitives.WeaponIcon(WeaponSlotKind.Direct).texture;
                    Check(pd != null && pd.width > 200 && pd.height < 120,
                        "直射图标用的是**图一**那张内嵌位图（又扁又宽），不是程序兜底 -> "
                        + (pd != null ? pd.width + "x" + pd.height : "null"));
                    if (pd != null)
                    {
                        int dSolid = 0;
                        var dRaw = pd.GetPixels32();
                        for (int i = 0; i < dRaw.Length; i++) if (dRaw[i].a > 200) dSolid++;
                        Check(dSolid > dRaw.Length / 40, "直射图标有实心内容 -> " + dSolid + " 像素");
                    }
                    // 内嵌位图是 192×192；程序生成的兜底是 160×160 —— 用尺寸分辨走的哪条路
                    var probe0 = Primitives.WeaponIcon(WeaponSlotKind.Scatter).texture;
                    Check(probe0 != null && probe0.width >= 190 && probe0.height >= 190,
                        "图标用的是内嵌位图（192×192），不是兜底形状 -> "
                        + (probe0 != null ? probe0.width + "x" + probe0.height : "null"));

                    // 剪影得有实心部分，不能是空图
                    var t = Primitives.WeaponIcon(WeaponSlotKind.Shield).texture;
                    int solid = 0;
                    var raw = t.GetPixels32();
                    for (int i = 0; i < raw.Length; i++) if (raw[i].a > 200) solid++;
                    Check(solid > raw.Length / 20, "图标有实心内容 -> " + solid + " 像素");

                    // ★★ 第 111 轮：吸血球图标（两个圆 + 蛇形连线 + 镂空的 X/Y）。
                    //   验三件事：有实心内容、**有镂空**（字母）、左右各有一个圆。
                    {
                        var vt = Primitives.WeaponIcon(WeaponSlotKind.Vampire).texture;
                        Check(vt != null, "吸血球图标生成了");
                        if (vt != null)
                        {
                            int vs = 0, hole = 0;
                            var vr = vt.GetPixels32();
                            for (int i = 0; i < vr.Length; i++) if (vr[i].a > 200) vs++;
                            // 字母挖空：X 的笔画交叉点（圆内正中心）应该几乎全透明
                            int cx = Mathf.RoundToInt(vt.width * 0.27f);
                            int cy = Mathf.RoundToInt(vt.height * 0.26f);
                            for (int yy = cy - 1; yy <= cy + 1; yy++)
                                for (int xx = cx - 1; xx <= cx + 1; xx++)
                                    if (vr[yy * vt.width + xx].a < 80) hole++;
                            Check(vs > vr.Length / 20, "吸血球图标有实心内容 -> " + vs + " 像素");
                            Check(hole >= 4, "吸血球图标里有镂空（X 字母）-> " + hole + "/9 像素");
                        }
                    }
                    // 领土不再用画笔抖动 → 边界是齐的
                    Check(Cfg.BrushEdgeNoise <= 0.0001f,
                        "领土边界不再抖动（一格一格清楚）-> " + Cfg.BrushEdgeNoise.ToString("F2"));
                }

                // ★★ 第 111 轮：武器区固定 8 格 —— 最后一格是**吸血球**
                //   （近防炮开着的时候那一格换成近防炮，二选一）。
                {
                    int wantSlots = 8;
                    Check(Cfg.WeaponSlotCount == wantSlots
                          && Cfg.WeaponSlotNames.Length == wantSlots
                          && Cfg.WeaponSlots.Length == wantSlots
                          && Cfg.WeaponSlotXPx.Length == wantSlots + 1,
                        "武器栏 " + wantSlots + " 格（直射/x2/散射/齐射/护盾/x2/大球/"
                        + (Cfg.CiwsEnabled ? "近防炮" : "吸血") + "）");
                    Check(Cfg.WeaponSlots[6].Kind == WeaponSlotKind.BigBall
                          && Cfg.WeaponSlotNames[6] == "大球",
                        "第 7 格仍然是**大球**");
                    if (Cfg.CiwsEnabled)
                        Check(Cfg.WeaponSlots[7].Kind == WeaponSlotKind.Ciws
                              && !Cfg.WeaponSlots[7].IsGate,
                            "第 8 格是近防炮");
                    else
                        Check(Cfg.WeaponSlots[7].Kind == WeaponSlotKind.Vampire
                              && !Cfg.WeaponSlots[7].IsGate
                              && Cfg.WeaponSlotNames[7] == "吸血",
                            "第 8 格是**吸血球**（大球选择区最右边那格）");
                    // 分界线：大球 463.4~560、吸血 560~657，各占一半
                    Check(Mathf.Abs(Cfg.WeaponSlotXPx[7] - 560f) < 0.01f
                          && Mathf.Abs(Cfg.WeaponSlotXPx[8] - 657f) < 0.01f,
                        "第 8 格从 560px 开始（大球区一分为二）");
                }

                // ── ★★★ 近防炮（CIWS）行为（`Cfg.CiwsEnabled = false` 时整块跳过）──
                if (Cfg.CiwsEnabled)
                {
                    var cf = Fac(0);
                    var ef = FacOther(0);

                    // 把场上的子弹清干净，免得别的用例的子弹被当成目标
                    System.Action clearBullets = delegate
                    {
                        for (int i = 0; i < world.Bullets.Active.Count; i++)
                        {
                            var bb = world.Bullets.Active[i];
                            if (bb != null && bb.InUse) bb.Release();
                        }
                        Invoke(world.Bullets, "CleanupDead", 0.02f);
                    };

                    // ① 生成：飞行形态 + 带着增倍球给的数值
                    var c = world.SpawnCiws(cf, 1e8);
                    Check(c != null && c.Flying, "近防炮初始是**飞行形态**（不参与物理）");
                    Check(Mathf.Abs((float)(c.Payload - 1e8)) < 1f,
                        "近防炮带着增倍球给的那份数值 -> " + c.Payload.ToString("F0"));

                    // ② 碰到**敌方领土**才展开
                    {
                        clearBullets();
                        c.Pos = new Vector2(2f, 2f);
                        c.Vel = new Vector2(4f, 0f);
                        world.Field.Paint(new Vector2(4f, 2f), 0.8f, ef.Index);
                        for (int i = 0; i < 200 && c.Flying; i++) Invoke(world, "StepCiws", 0.02f);
                        Check(!c.Flying, "飞行形态**碰到敌方领土就展开** -> pos "
                            + c.Pos.x.ToString("F2") + "," + c.Pos.y.ToString("F2"));
                    }

                    // ③ 展开：50% 盾 / 50% 散弹（用一颗新的，别被上面那次展开污染）
                    {
                        var cd = world.SpawnCiws(cf, 1e8);
                        cd.Deploy();
                        Check(!cd.Flying
                              && Mathf.Abs((float)(cd.Shield - 5e7)) < 1f
                              && Mathf.Abs((float)(cd.Ammo - 5e7)) < 1f,
                            "展开后数值 **50% 变盾 / 50% 变散弹** -> 盾 "
                            + cd.Shield.ToString("F0") + " / 弹 " + cd.Ammo.ToString("F0"));
                        cd.Kill();
                    }

                    // ④ 散弹 >= 敌方数值 → 只发射「与敌方相同」的数值
                    {
                        clearBullets();
                        c.Pos = new Vector2(5f, 5f);
                        c.Shield = 5e7; c.Ammo = 5e7; c.FireTimer = 0f;
                        world.Bullets.Rent(ef, new Vector2(5f, 5.9f), Vector2.up,
                                           Cfg.BulletSpeed, 3e7, 0f, false);
                        Invoke(world, "StepCiws", 0.02f);

                        bool fired = false; double fv = 0; bool white = false;
                        for (int i = 0; i < world.Bullets.Active.Count; i++)
                        {
                            var b = world.Bullets.Active[i];
                            if (b != null && b.InUse && b.Owner == cf && b.IsCiws)
                            { fired = true; fv = b.RemainingValue; white = b.IsCiws; break; }
                        }
                        Check(fired && Mathf.Abs((float)(fv - 3e7)) < 1f,
                            "★ 散弹 ≥ 敌方数值 → 发射**与敌方相同**的数值 -> " + fv.ToString("F0"));
                        Check(Mathf.Abs((float)(c.Ammo - 2e7)) < 1f,
                            "★ 剩下的散弹留着 -> " + c.Ammo.ToString("F0"));
                        Check(white && Cfg.CiwsBulletColor.r > 0.9f,
                            "★ 近防炮弹丸有**独立的白色外观**（和普通子弹区分）");
                    }

                    // ⑤ 散弹 < 敌方数值 → **一次性全部发射**
                    {
                        clearBullets();
                        c.Ammo = 1e7; c.Shield = 5e7; c.FireTimer = 0f;
                        world.Bullets.Rent(ef, new Vector2(5f, 5.9f), Vector2.up,
                                           Cfg.BulletSpeed, 3e7, 0f, false);
                        Invoke(world, "StepCiws", 0.02f);
                        bool fired = false; double fv = 0;
                        for (int i = 0; i < world.Bullets.Active.Count; i++)
                        {
                            var b = world.Bullets.Active[i];
                            if (b != null && b.InUse && b.Owner == cf && b.IsCiws)
                            { fired = true; fv = b.RemainingValue; break; }
                        }
                        Check(fired && Mathf.Abs((float)(fv - 1e7)) < 1f
                              && c.Ammo < 1d,
                            "★ 散弹 < 敌方数值 → **一次性全部发射** -> " + fv.ToString("F0"));
                    }

                    // ⑥ 护盾：先扣盾；盾归零后**本体被击中就死亡（无亡语）**
                    {
                        clearBullets();
                        c.Pos = new Vector2(5f, 5f);
                        c.Shield = 1e7; c.Ammo = 0d; c.FireTimer = 0f;
                        world.Bullets.Rent(ef, new Vector2(5f, 5f + Cfg.CiwsShieldRadius * 0.8f),
                                           Vector2.up, Cfg.BulletSpeed, 4e6, 0f, false);
                        Invoke(world, "StepCiws", 0.02f);
                        Check(c.Shield < 1e7 && !c.Consumed,
                            "敌方子弹撞护盾 → **扣盾**、子弹消失 -> 盾 " + c.Shield.ToString("F0"));

                        c.Shield = 0d;
                        world.Bullets.Rent(ef, new Vector2(5f, 5f), Vector2.up,
                                           Cfg.BulletSpeed, 4e6, 0f, false);
                        Invoke(world, "StepCiws", 0.02f);
                        Check(c.Consumed, "★★ 盾归零后**本体被击中 → 死亡（无亡语）**");
                    }

                    // ⑦ 没有敌人 → 吸收范围内**己方领土**（每格每帧 +0.5）
                    {
                        clearBullets();
                        var c3 = world.SpawnCiws(cf, 1e6);
                        c3.Pos = new Vector2(9f, 9f);
                        c3.Flying = false;
                        c3.Payload = 1e6;
                        c3.Shield = 5e5; c3.Ammo = 0d; c3.FireTimer = 0f;
                        world.Field.Paint(new Vector2(9f, 9f), 1.0f, cf.Index);
                        Invoke(world, "StepCiws", 0.02f);
                        double gained = c3.Ammo;
                        Check(gained > 0d,
                            "★ 没有敌人时**吸收己方领土**能量到散弹 -> +"
                            + gained.ToString("F1") + "（每格每帧 0.5）");
                        Check(Mathf.Abs((float)(c3.Shield - 5e5)) < 1f,
                            "吸能不会动护盾");
                        c3.Kill();
                    }

                    c.Kill();
                    clearBullets();
                    Invoke(world, "StepCiws", 0.02f);
                }

                // ── ★★★ 子弹推力（第 103 轮；用户纠正：「**不是通过提速来的**，
                //   速度不变的同时提升它**对于大球 / 中子星的推力**」）──
                {
                    // ① 子弹自己**不会**被加速
                    var tb = world.Bullets.Rent(Fac(0), new Vector2(3f, 3f), Vector2.right,
                                                 Cfg.BulletSpeed, 1e6, 0f, false);
                    float s0 = tb.Vel.magnitude;
                    for (int i = 0; i < 120; i++) tb.Step(1f / 60f, false, world.Field);
                    Check(Mathf.Abs(tb.Vel.magnitude - s0) < 1e-3f,
                        "★★ 子弹**速度不变**（推力不走提速）-> " + s0.ToString("F2") + " → "
                        + tb.Vel.magnitude.ToString("F2") + " m/s");
                    tb.Release();
                    Invoke(world.Bullets, "CleanupDead", 0.02f);

                    // ② 命中大球 → 把冲量传给球（球自己动、子弹不变）
                    var pf = Fac(0);
                    var ef = FacOther(0);
                    var pb = world.SpawnBigBall(pf, 1e6);
                    pb.transform.position = new Vector3(7f, 7f, 0f);
                    pb.Body.position = new Vector2(7f, 7f);
                    pb.Body.velocity = Vector2.zero;

                    var hb = world.Bullets.Rent(ef, new Vector2(7f, 7f), Vector2.right,
                                                 Cfg.BulletSpeed, 1000d, 0f, false);
                    float hb0 = hb.Vel.magnitude;
                    hb.HitBigBall(pb);
                    Check(hb.Vel.magnitude <= hb0 + 1e-3f,
                        "★ 命中后子弹自己**没有被加速** -> " + hb0.ToString("F2") + " → "
                        + hb.Vel.magnitude.ToString("F2"));
                    Check(pb.Body.velocity.x > 0f,
                        "★★ 命中把**推力传给大球** -> 球速度 "
                        + pb.Body.velocity.x.ToString("F4") + " m/s");
                    // ★★★ 推力由**子弹自身数值**决定：Δv = 系数 × 子弹数值
                    {
                        float wantDv = Cfg.BulletPushSpeedPerValue * 1000f;
                        Check(Mathf.Abs(pb.Body.velocity.x - wantDv) < wantDv * 0.02f + 1e-7f,
                            "★★ 推力**由子弹数值决定** -> 实测 "
                            + pb.Body.velocity.x.ToString("F5") + " vs 期望 "
                            + wantDv.ToString("F5") + " m/s");
                    }

                    // ★★★ 被推的球**数值越大越难推动**（同一颗子弹比较）
                    {
                        var sb1 = world.SpawnBigBall(pf, 1e6);
                        sb1.transform.position = new Vector3(6f, 6f, 0f);
                        sb1.Body.position = new Vector2(6f, 6f);
                        sb1.Body.velocity = Vector2.zero;
                        var sb2 = world.SpawnBigBall(pf, 1e8);
                        sb2.transform.position = new Vector3(8f, 8f, 0f);
                        sb2.Body.position = new Vector2(8f, 8f);
                        sb2.Body.velocity = Vector2.zero;

                        var bb1 = world.Bullets.Rent(ef, new Vector2(6f, 6f), Vector2.right,
                                                      Cfg.BulletSpeed, 1000d, 0f, false);
                        bb1.HitBigBall(sb1);
                        var bb2 = world.Bullets.Rent(ef, new Vector2(8f, 8f), Vector2.right,
                                                      Cfg.BulletSpeed, 1000d, 0f, false);
                        bb2.HitBigBall(sb2);

                        float dvS = sb1.Body.velocity.magnitude;
                        float dvL = sb2.Body.velocity.magnitude;
                        Check(dvS > dvL * 10f,
                            "★★ **球数值越大越难被推动** -> 1M 球 " + dvS.ToString("F6")
                            + " m/s vs 100M 球 " + dvL.ToString("F6") + " m/s");
                        sb1.Consume(); sb2.Consume();
                        pf.BigBalls.Remove(sb1); pf.BigBalls.Remove(sb2);
                    }

                    // ★★★ 己方子弹打到**己方引力球** → **不提供推力**
                    {
                        var og = world.SpawnBigBall(pf, 1e9);
                        og.transform.position = new Vector3(9f, 9f, 0f);
                        og.Body.position = new Vector2(9f, 9f);
                        og.Body.velocity = Vector2.zero;
                        bool ownsGravity = og.HasGravity;      // 先读一次把闩锁置位
                        var ob = world.Bullets.Rent(pf, new Vector2(9f, 9f), Vector2.right,
                                                     Cfg.BulletSpeed, 1000d, 0f, false);
                        ob.HitBigBall(og);
                        Check(ownsGravity && og.Body.velocity.magnitude < 1e-6f,
                            "★★ **己方子弹打己方引力球 → 不提供推力** -> 球速度 "
                            + og.Body.velocity.magnitude.ToString("F6"));
                        og.Consume();
                        pf.BigBalls.Remove(og);
                    }

                    // ③ 中子星走同一条路：同样推得动
                    var ns = world.SpawnBigBall(pf, 1e9);
                    ns.transform.position = new Vector3(10f, 7f, 0f);
                    ns.Body.position = new Vector2(10f, 7f);
                    ns.Body.velocity = Vector2.zero;
                    // ★ 先读一次把中子星**闩锁**置位 —— 命中会扣掉 1000 点数值，
                    //   之后 `Value` 就掉到 1B 以下了（闩锁是只置位、永不清零的）。
                    bool wasNeutron = ns.IsNeutronStar;
                    var hb2 = world.Bullets.Rent(ef, new Vector2(10f, 7f), Vector2.up,
                                                  Cfg.BulletSpeed, 1000d, 0f, false);
                    hb2.HitBigBall(ns);
                    Check(wasNeutron && ns.Body.velocity.y > 0f,
                        "★★ **中子星**也会被子弹推 -> 球速度 "
                        + ns.Body.velocity.y.ToString("F4") + " m/s");

                    pb.Consume(); ns.Consume();
                    pf.BigBalls.Remove(pb); pf.BigBalls.Remove(ns);
                }
                // ★ 用户第 89 轮：5° → **1°**（要一条更聚拢的直线）
                Check(Cfg.DirectFireSpreadDeg > 0f && Cfg.DirectFireSpreadDeg <= 2f,
                    "直射的角度浮动 ≈1° -> " + Cfg.DirectFireSpreadDeg.ToString("F1"));
                Check(Cfg.WeaponSlot(0).Kind == WeaponSlotKind.Direct
                      && !Cfg.WeaponSlot(0).IsGate,
                    "第 0 格是**直射**（图二最左边那格）");
                Check(new Faction(0).TurretMode == WeaponSlotKind.Scatter,
                    "开局是**散射**模式（用户第 89 轮纠正：不是直射）");
                Check(world.Zone.Gates.Count == Cfg.GateMultipliers.Length * 2 + 1,
                    "乘法门 = 左右各 " + Cfg.GateMultipliers.Length + " + x16 -> "
                    + world.Zone.Gates.Count);
                Check(world.Zone.Weapons != null, "底部武器区存在");
                // ★ 规格（用户，第 81 轮）：「武器区也要**柱子和底板**」（照乘法门那一排的样式）
                if (world.Zone.Weapons != null)
                {
                    var wzT = world.Zone.Weapons.transform;
                    var wposts = wzT.Find("WeaponPosts");
                    Check(wposts != null, "武器区格子之间有柱子");
                    if (wposts != null)
                    {
                        int want = Cfg.WeaponSlotCount - 1;      // 8 格 → 7 根内缝柱子
                        int got = wposts.GetComponentsInChildren<BoxCollider2D>().Length;
                        Check(got == want, "柱子数量 = 格数 - 1 -> " + got + "/" + want);
                        var wsr = wposts.GetComponentInChildren<SpriteRenderer>();
                        Check(wsr != null && Mathf.Abs(wsr.color.r - Cfg.BlockerGrey.r) < 0.01f
                              && Mathf.Abs(wsr.color.g - Cfg.BlockerGrey.g) < 0.01f
                              && Mathf.Abs(wsr.color.b - Cfg.BlockerGrey.b) < 0.01f,
                            "武器区的柱子和阻挡球**同一个颜色**");
                        // 柱顶球：每根柱子一颗
                        int caps = 0;
                        var wc = wposts.GetComponentsInChildren<Transform>(true);
                        for (int i = 0; i < wc.Length; i++)
                            if (wc[i] != wposts.transform && wc[i].name == "PostCap") caps++;
                        // 6 根柱子各一颗 + **最右端**贴着墙那颗（用户 图二 里右端就有一颗）
                        Check(caps == want + 1,
                            "武器区每根柱子上也有一颗阻挡球（+ 最右端一颗，图二）-> "
                            + caps + "/" + (want + 1));
                    }
                    var wbase = wzT.Find("BaseBar");
                    Check(wbase != null, "武器区有底板");
                    if (wbase != null)
                    {
                        var bsr = wbase.GetComponent<SpriteRenderer>();
                        Check(bsr != null && Mathf.Abs(bsr.color.r - Cfg.BlockerGrey.r) < 0.01f,
                            "底板和门的阻挡条**同一个颜色**");
                        // ★ 底板是**实心**的（用户：球从上面掉进来，底板在最底下，挡不住选武器）
                        Check(wbase.GetComponent<Collider2D>() != null,
                            "底板是实心阻挡（球从上面掉进来，不影响选武器）");
                        // ★ 柱顶那颗阻挡球：门的 10 根 + 武器区 3 根 = 13 颗
                        {
                            int caps = 0;
                            var all = world.Zone.GetComponentsInChildren<Transform>(true);
                            for (int i = 0; i < all.Length; i++)
                                if (all[i].name == "PostCap") caps++;
                            // 乘法门柱（两组 × 每组块数）+ 武器区（6 根柱子 + 最右端那颗 = 格数）
                            int wantCaps = 2 * Cfg.GateMultipliers.Length + Cfg.WeaponSlotCount;
                            Check(caps == wantCaps,
                                "每根柱子上都有一颗阻挡球 -> " + caps + "/" + wantCaps);
                            // ★ 武器区那颗移动阻挡球（用户：武器区也要一个）
                            Check(Cfg.MovingBlockerY.Length == 3,
                                "移动阻挡球 = 3 颗（门排 / 阻挡球排 / 武器区）-> "
                                + Cfg.MovingBlockerY.Length);
                            // ★★ 规格（用户第 89 轮）：武器区那颗移动球**中心与白框顶边齐平**
                            Check(Mathf.Abs(Cfg.MovingBlockerY[2] - Cfg.DY(Cfg.WeaponZoneTopPx)) < 1e-4f,
                                "武器区移动阻挡球的中心 = 白框顶边 -> y="
                                + Cfg.MovingBlockerY[2].ToString("F4"));
                            // 半径 = 柱子半宽；中心与柱顶齐平
                            for (int i = 0; i < all.Length; i++)
                            {
                                if (all[i].name != "PostCap") continue;
                                var capSr = all[i].GetComponentInChildren<SpriteRenderer>();
                                float cr = capSr != null ? capSr.transform.localScale.x * 0.5f : 0f;
                                // ★ 现在**只有一个来源**：柱子宽度 = 2 × PostCapRadius
                                Check(Mathf.Abs(cr - Cfg.PostCapRadius) < 1e-3f,
                                    "柱顶球的半径 = PostCapRadius -> " + cr.ToString("F3"));
                                Check(Mathf.Abs(Cfg.GatePostWidth - Cfg.PostCapRadius * 2f) < 1e-4f,
                                    "（门的）柱子宽度就是 2×球半径 -> 两边不可能不一致");
                                // ★★ 用户第 90 轮：「武器区的那些柱子不够粗，**至少跟阻挡球一样粗**」
                                //   → 0.24m，比柱顶球（直径 2×PostCapRadius = 0.20）**明显粗一点**
                                Check(Cfg.WeaponPostWidth > Cfg.PostCapRadius * 2f,
                                    "武器区柱子比柱顶球**更粗**（用户要「至少一样粗」）-> "
                                    + Cfg.WeaponPostWidth.ToString("F3") + " > "
                                    + (Cfg.PostCapRadius * 2f).ToString("F3"));
                                float ins = HudOverlay.WeaponPlatePostInsetWorld(70f);
                                Check(ins >= Cfg.WeaponPostWidth * 0.5f - 1e-4f,
                                    "HUD 的白牌子按柱子半宽往里缩 → 整根柱子露出来 -> "
                                    + ins.ToString("F3"));
                                break;
                            }
                        }
                    }
                }
                // ★ 规格（用户）：「x2 x4 x8 x16 的文字还需要调大」
                //   真正的元凶是字号上限（30）—— 用户窗口里按比例算出来 42 却被截到 30。
                {
                    Check(Cfg.HudFontSizeMax >= 96,
                        "HUD 字号上限放到 " + Cfg.HudFontSizeMax + "（以前死钉在 30，"
                        + "GateLabelHeight 调多大都被截掉）");
                    Check(Cfg.GateLabelHeight >= 0.55f,
                        "门上的字号 -> " + Cfg.GateLabelHeight.ToString("F2") + " 世界单位");
                }
                // ★ 主字体 Arimo 必须**打进 Resources**：以前是 CreateDynamicFontFromOSFont，
                //   换台没装 Arimo 的机器（或打包给别人）就会静默变成 Arial。
                Check(Resources.Load<Font>("Fonts/Arimo") != null,
                    "Arimo 已打进 Resources/Fonts（打包后字形不跑偏）");
                Check(Mathf.Abs(Cfg.MapWorldSize - 14f) < 0.001f, "地图世界尺寸 = 14m");
                Check(System.Math.Abs(Cfg.PixelCaptureCost - 1.0) < 0.001,
                      "一个像素扣 " + Cfg.PixelCaptureCost.ToString("F0") + " 点数值");
                // 规格变了：涂地**不再**额外扣阵营库存（携带的数值在发射时就取走了，
                // 再按固定值扣一遍会导致「存货少的时候一次性被打光」）
                Check(!Cfg.PaintChargesBulletStock, "涂地不重复扣阵营库存");
                Check(!Cfg.PaintChargesBulletStock,
                      "涂地不再额外扣阵营库存（消耗只发生在发射那一步，天然按比例）");
                Check(System.Math.Abs(Cfg.CostToFillMap() - (double)Cfg.MapTexels * Cfg.MapTexels * Cfg.PixelCaptureCost) < 1.0,
                      "装满整张图需要 " + Cfg.CostToFillMap().ToString("F0") + " 数值");
                Check(world.Field.Texels == 1024, "地图 1024x1024");
                Check(world.Bullets != null, "子弹池已建");
                Check(Cfg.BulletLifetime <= 0f, "子弹没有寿命上限（数值没耗尽就不消失）");
                // ★★ 「终极优化」的回归保护：网格/配对必须走**扁平数组**，不许再解引用 Bullet 对象
                {
                    var pt = typeof(BulletPool);
                    const System.Reflection.BindingFlags NP =
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    Check(pt.GetField("_fPos", NP) != null && pt.GetField("_fUse", NP) != null
                          && pt.GetField("_fOwner", NP) != null && pt.GetField("_fVal", NP) != null,
                        "池子里有扁平数组（网格/配对不再乱跳内存）");
                    // 死了的子弹**不许**再 SetActive —— 那是每颗一次 Transform 层级登记
                    Check(pt.GetField("_flatDummy", NP) == null, "占位：确认反射名字没写错");
                    // ★★ 地图上传的节拍（第 93 轮用户：「帧率不稳定，总是跳帧」）：
                    //   隔帧传 = 每帧的代价**一轻一重交替**，平均帧时间没变但画面一顿一顿的。
                    //   所以现在**每帧都传**：代价一样大，节奏是平的。
                    Check(Cfg.MapFlushIntervalFrames == 1,
                        "地图上传改成每帧一次（节奏平，不再一轻一重）-> 每 "
                        + Cfg.MapFlushIntervalFrames + " 帧（1024² = 4MB/次）");
                }

                for (int i = 0; i < world.Factions.Count; i++)
                {
                    var f = world.Factions[i];
                    Check(f.Balls.Count == Cfg.StartMultiplierBalls, "开局增倍球数量 " + i);
                    Check(f.TilesOwned > 0, "开局领土 " + i);
                    Check(f.Turret != null, "炮台存在 " + i);
                    // ★ 回归保护：炮台必须在**右栏战场**的四角，不能在左栏增倍区里
                    Vector2 tp = f.Turret.transform.position;
                    Check(tp.x > 0f && tp.x < Cfg.MapWorldSize && tp.y > 0f && tp.y < Cfg.MapWorldSize,
                        "炮台在战场范围内 " + i + " -> " + tp);
                    // 护盾圈在画面上要看得见（跟着 `ShieldMinRadius` 走 —— 16 家版整体调小了）
                    Check(f.ShieldRadius >= Cfg.ShieldMinRadius - 1e-3f,
                        "护盾圈可见 " + i + " -> r=" + f.ShieldRadius.ToString("F2")
                        + " >= " + Cfg.ShieldMinRadius.ToString("F2"));
                    Debug.Log(string.Format("[SMOKE] {0}  px={1}  bullets={2}  shield={3}  balls={4}",
                        Palette.FactionNames[i], f.TilesOwned,
                        BigNumber.Grouped(f.Bullets), BigNumber.Suffixed(f.Shield), f.Balls.Count));
                }

                // ── 数值显示格式 ──
                Check(BigNumber.Grouped(2531742) == "2 531 742", "三位分级 -> " + BigNumber.Grouped(2531742));
                Check(BigNumber.Suffixed(1000000) == "1,0M", "1M 显示 -> " + BigNumber.Suffixed(1000000));
                Check(BigNumber.Suffixed(268500000) == "269M", "269M 显示 -> " + BigNumber.Suffixed(268500000));
                // 规格：增倍球文字四舍五入
                Check(BigNumber.SuffixedRounded(33554432) == "34M", "33554432 -> " + BigNumber.SuffixedRounded(33554432));
                Check(BigNumber.SuffixedRounded(16777216) == "17M", "16777216 -> " + BigNumber.SuffixedRounded(16777216));
                Check(BigNumber.SuffixedRounded(4096) == "4K", "4096 -> " + BigNumber.SuffixedRounded(4096));
                Check(BigNumber.SuffixedRounded(32768) == "33K", "32768 -> " + BigNumber.SuffixedRounded(32768));
                // ★ 进位：999.6M 四舍五入会被写成 "1000M"，必须进到下一档
                Check(BigNumber.Suffixed(999600000) == "1,0B",
                    "999,6M 要进位成 1,0B（不是 1000M）-> " + BigNumber.Suffixed(999600000));
                Check(BigNumber.Suffixed(1000000000) == "1,0B", "1B 显示 -> " + BigNumber.Suffixed(1000000000));
                Check(BigNumber.Suffixed(999400000) == "999M", "999,4M 还是 999M -> " + BigNumber.Suffixed(999400000));
                Check(BigNumber.Suffixed(999600) == "1,0M", "999,6K 进位成 1,0M -> " + BigNumber.Suffixed(999600));
                Check(BigNumber.Suffixed(999.6) == "1,0K", "999,6 进位成 1,0K -> " + BigNumber.Suffixed(999.6));
                Check(BigNumber.SuffixedRounded(999600000) == "1B",
                    "取整模式也进位 -> " + BigNumber.SuffixedRounded(999600000));
                // ★ 总值计数条的格式：1e9 以下写整数，以上写「xxxxM」且不带小数点
                Check(HudOverlay.GainText(33687568d) == "33687568",
                    "总值 < 1e9 直接写整数 -> " + HudOverlay.GainText(33687568d));
                Check(HudOverlay.GainText(999999999d) == "999999999",
                    "总值 999,999,999 还是整数 -> " + HudOverlay.GainText(999999999d));
                Check(HudOverlay.GainText(1000000000d) == "1000M",
                    "总值 ≥ 1e9 写成 xxxxM（无小数点）-> " + HudOverlay.GainText(1000000000d));
                Check(HudOverlay.GainText(1500000000d) == "1500M",
                    "1,5e9 → 1500M -> " + HudOverlay.GainText(1500000000d));

                // ── 半径公式抽查 ──
                var shieldTarget = Mathf.Log(1000000f) / Cfg.ShieldRadiusLogDivisor + Cfg.ShieldRadiusBias;
                var bigTarget = Mathf.Log(1000000f) / Cfg.BigBallRadiusLogDivisor + Cfg.BigBallRadiusBias;
                var ballRadius = Mathf.Log(1024f) / Cfg.MultiplierBallRadiusLogDivisor + Cfg.MultiplierBallRadiusBias;
                Debug.Log(string.Format("[SMOKE] r(shield 1M)={0:F3}  r(bigball 1M)={1:F3}  r(ball 1024)={2:F3}",
                    shieldTarget, bigTarget, ballRadius));
                Check(shieldTarget > 0f && bigTarget > 0f && ballRadius > 0f, "半径公式为正");

                // ── ★ 规格：护盾的**纹理**半径 = 护盾的**实际**半径 ──
                // 以前羽化从 0.55 就开始了，alpha 到边缘已经是 0 ——
                // 判定用的是 ShieldRadius，画出来的圈却只有它的七八成。
                {
                    var tex = Primitives.ShieldDisc.texture;
                    int n = tex.width, c = n / 2;
                    // 羽化起点由 Cfg.ShieldFeatherBand 决定（视觉和判定共用一个值）
                    float aIn = tex.GetPixel(c + Mathf.RoundToInt((1f - Cfg.ShieldFeatherBand) * n * 0.5f), c).a;
                    float aOut = tex.GetPixel(c + Mathf.RoundToInt(0.99f * n * 0.5f), c).a;
                    Check(aIn > 0.9f, "护盾纹理羽化起点以内是实心的 -> a=" + aIn.ToString("F2"));
                    Check(aOut < 0.3f, "护盾纹理在最外沿羽化掉 -> a=" + aOut.ToString("F2"));
                    // ★ 用户：护盾边框不能发黑 —— 最外那圈是「阵营色的深边」，不是黑框。
                    //   这里只验「贴图确实按配置值走」，具体多深交给 Cfg 调。
                    float rimK = tex.GetPixel(c + Mathf.RoundToInt(0.95f * n * 0.5f), c).r;
                    Check(Mathf.Abs(rimK - Cfg.ShieldEdgeBrightness) < 0.1f,
                        "护盾边框的深边 = Cfg.ShieldEdgeBrightness -> "
                        + rimK.ToString("F2") + " ≈ " + Cfg.ShieldEdgeBrightness.ToString("F2"));

                    var ring = Primitives.SoftRing.texture;
                    int n2 = ring.width, c2 = n2 / 2;
                    float r96 = ring.GetPixel(c2 + Mathf.RoundToInt(0.96f * n2 * 0.5f), c2).a;
                    float r80 = ring.GetPixel(c2 + Mathf.RoundToInt(0.80f * n2 * 0.5f), c2).a;
                    Check(r96 > 0.9f, "护盾亮边落在真实半径上（96%）-> a=" + r96.ToString("F2"));
                    Check(r80 < 0.2f, "护盾亮边不再缩在里面（80% 处没有）-> a=" + r80.ToString("F2"));
                }

                // ★★ 第 100 轮：颜色由**阵营表**决定（4 家/8 家各不相同），
                //   所以这里改成**通用判据** —— 文字和阵营**显示色**之间要有足够的对比度。
                //   ★ 第一版用的是「亮度 > 0.40 才算够亮（可用黑字）」的经验阈值，
                //     但参考图里的**红色**显示色约 (221,47,33)、亮度只有 0.38，
                //     原作用的就是**黑字** —— 用亮度截断会把它误判成"不行"。
                //     换成 **WCAG 对比度**就对上了：黑字压那个红底是 4.35:1。
                //   ★ 门槛取 **3.5:1**：WCAG 大号字 AA 是 3.0、正文 AA 是 4.5，
                //     炮台数字介于两者之间。两家阵营表里**最低**的两处是
                //     4 家版的纯红配白字（4.00）和 8 家版的暗红配黑字（4.35）——
                //     都是用户一直在用的配置，不该按正文 AA 判死；3.5 只用来兜底
                //     「中等灰底配黑字」那种真的看不清的配色（≈3.0）。
                {
                    const float MinContrast = 3.5f;
                    bool allOk = true;
                    string detail = "";
                    int wrongText = 0;
                    for (int i = 0; i < Cfg.FactionCount; i++)
                    {
                        var fac = Fac(i);
                        // ★ 文字黑/白是**手动**填的（阵营表 `FactionDef.TextBlack`，设置面板可覆盖）——
                        //   这里核对一遍：实际用的必须就是 GameSettings 给的那个，不许再偷偷按颜色推算。
                        Color want = GameSettings.TextBlackOf(i) ? Color.black : Color.white;
                        if (fac.TextColor != want) wrongText++;

                        // 对比度按**阵营表原色**验（用户自己在设置面板改过的配色不归自检管，
                        // 否则改个暗色就会把自检跑红）
                        Vector3 baseHsv = GameSettings.BaseHsv(i);
                        Color c = Palette.HsvToRgb(baseHsv.x, baseHsv.y, baseHsv.z);
                        Color t = FactionSet.Get(i).TextBlack ? Color.black : Color.white;
                        float ratio = ContrastRatio(t, c);
                        if (ratio < MinContrast)
                        {
                            allOk = false;
                            detail += " 阵营" + i + "(" + (t == Color.black ? "黑字" : "白字")
                                      + " 对比度 " + ratio.ToString("F2") + ":1)";
                        }
                    }
                    Check(allOk, "每一家的文字和底色对比度都够（WCAG ≥ "
                        + MinContrast.ToString("F1") + ":1）" + detail);
                    Check(wrongText == 0,
                        "每一家的黑/白字都是**手填**的那个（阵营表 / GameSettings）-> 不符 "
                        + wrongText + " 家");
                }

                // ★★ 第 100 轮（用户）：「名称系统文字变得很小」——
                //   根因是名称字号原来 = **炮台屏幕半径 × 0.50**，而炮台半径连着缩了三轮
                //   （0.30 → 0.26 → 0.11），字号被一路拖到 9px 的钳位下限。
                //   现在名称和数字一样用**固定世界高度**。这两条盯着它别再退回去。
                {
                    const float pxPerUnit = 80f;   // 典型的「每米多少像素」；比的是**比例**，取值不敏感
                    int numSize = Mathf.Clamp(
                        Mathf.RoundToInt(Cfg.TurretTextHeight * pxPerUnit * Cfg.TurretTextSizeFactor),
                        11, Cfg.TurretTextSizeMax);
                    int nameSize = Mathf.Clamp(
                        Mathf.RoundToInt(Cfg.TurretNameTextHeight * pxPerUnit), 9, Cfg.TurretTextSizeMax);
                    float ratio = nameSize / (float)numSize;
                    Check(ratio > 0.40f && ratio < 0.85f,
                        "★ 名称比数字小一号、但不至于看不见（名称 " + nameSize + "px / 数字 "
                        + numSize + "px = " + ratio.ToString("F2") + "）");

                    // 旧算法（炮台屏幕半径 × 0.50）在同一条件下只有个位数像素 —— 必须明显不同，
                    // 否则说明名称字号又跟炮台半径挂钩了（修复前这条会红）。
                    int oldWay = Mathf.Clamp(
                        Mathf.RoundToInt(Cfg.TurretRadius * pxPerUnit * 0.50f), 9, 30);
                    Check(nameSize >= oldWay + 3,
                        "★ 名称字号和炮台半径**脱钩**（旧算法 " + oldWay + "px → 现在 " + nameSize + "px）");

                    // 这几个原来是在 HUD 里写死的（0.95 / 1.22 / 6..220），现在收进 `Cfg`：
                    //   HUD 和自检读**同一个来源**，这里只盯它们别被改成不合理的值。
                    Check(Cfg.TurretTextSizeFactor > 0.8f && Cfg.TurretTextSizeFactor <= 1f
                          && Cfg.HudLineHeightFactor > 1f && Cfg.HudLineHeightFactor < 1.6f
                          && Cfg.BallTextSizeMin > 0 && Cfg.BallTextSizeMax > Cfg.BallTextSizeMin,
                        "HUD 字号系数 / 行高系数 / 大球字号上下限合理 -> "
                        + Cfg.TurretTextSizeFactor.ToString("F2") + " / "
                        + Cfg.HudLineHeightFactor.ToString("F2") + " / "
                        + Cfg.BallTextSizeMin + ".." + Cfg.BallTextSizeMax);
                }

                // ★★ 第 100/101 轮（用户）：「8 色版近防触发范围需要调小」→「还要调小，
                //   开局 8 家互相近防对射了」。只要求「小于相邻中心距」是不够的 ——
                //   两家的**近防圈不许碰到一起**，否则一家的普通散射子弹飞进邻居的圈里，
                //   两边就会互相锁着打。所以判据收紧成**小于相邻中心距的一半**。
                {
                    var defs = FactionSet.Defs;
                    float neighbor = Vector2.Distance(defs[0].Home, defs[1].Home);
                    Check(Cfg.CiwsRange * 2f < neighbor,
                        "★ 两家的近防圈不重叠（半径 " + Cfg.CiwsRange.ToString("F2")
                        + "m × 2 < 相邻中心距 " + neighbor.ToString("F2") + "m，"
                        + Cfg.FactionCount + " 家）");
                }

                // ── 涂色消耗：1 像素 = 1 数值 ──
                var probe = Fac(3);
                int before = world.Field.PixelsOf(probe.Index);
                int painted = world.Field.Paint(new Vector2(10f, 10f), 1.0f, probe.Index);
                Check(painted > 0, "能涂到新像素");
                Check(world.Field.PixelsOf(probe.Index) == before + painted, "像素计数和返回一致");
                Debug.Log(string.Format("[SMOKE] 刷半径 1m 涂了 {0} 个像素（每像素扣 {1:F0} 数值）",
                    painted, Cfg.PixelCaptureCost));

                // ── ★ 大球涂地也必须扣**阵营的子弹数**（和子弹共用一个池）──
                // 以前是扣大球自己的数值：大球拿自己的数值白刷一大片，
                // 于是「四家账上还剩 40 万，地图却快满了」。
                {
                    var bf = Fac(3);
                    var bb = world.SpawnBigBall(bf, 500000d);
                    var bp2 = new Vector2(9f, 3f);
                    bb.transform.position = new Vector3(bp2.x, bp2.y, 0f);
                    if (bb.Body != null) bb.Body.position = bp2;
                    double stock0 = bf.Bullets;
                    Invoke(bb, "FixedUpdate");
                    double spent = stock0 - bf.Bullets;
                    Check(spent <= 0.001d,
                        "大球涂地不再重复扣阵营库存 -> 扣了 " + spent.ToString("F0"));
                    Check(bb.Value < 500000d,
                        "大球自己涂地也要扣自己的数值 -> " + bb.Value.ToString("F0"));
                }

                // ── ★★ 回归：**数值超 21.4 亿**的球/子弹也必须能涂 ──
                // 根因：Unity 的 Mono 把超范围的 double→int 转成 int.MinValue（负的），
                // 于是「还买得起几格」成了负数 → 中子星一格都涂不上
                //（用户报的「正常的中子星涂了一点但涂不全」）。
                {
                    Check(Cfg.PaintBudget(5d) == 5, "预算换算：小数值原样 -> "
                        + Cfg.PaintBudget(5d));
                    Check(Cfg.PaintBudget(1.2e10) > 0,
                        "预算换算：120 亿也是**正数**（Mono 会转成 int.MinValue）-> "
                        + Cfg.PaintBudget(1.2e10));
                    Check(Cfg.PaintBudget(1e15) > 0 && Cfg.PaintBudget(double.MaxValue) > 0,
                        "预算换算：天文数字也不为负 -> " + Cfg.PaintBudget(double.MaxValue));
                    Check(Cfg.PaintBudget(0d) == 0 && Cfg.PaintBudget(-5d) == 0,
                        "预算换算：0 / 负数 = 0");
                    // 端到端：一颗 1e12 的中子星真的涂得出来
                    var nf = Fac(2);
                    int pixN0 = world.Field.PixelsOf(nf.Index);
                    var ns = world.SpawnBigBall(nf, 1.2e10);
                    // ★ 战场世界坐标是 **0..MapWorldSize**（原点在地图左下角），不是 ±7。
                    //   落点选**地图正中**：不能蹭到别的阵营的角落，否则会把人家已有的像素涂掉，
                    //   后面「子弹会涂领土」那条断言就会被这条测试连累。
                    var np = new Vector2(7f, 7f);
                    ns.transform.position = new Vector3(np.x, np.y, 0f);
                    if (ns.Body != null) ns.Body.position = np;
                    Invoke(ns, "FixedUpdate");
                    Invoke(ns, "FixedUpdate");
                    Check(ns.IsNeutronStar, "1.2e10 的球就是中子星");
                    Check(world.Field.PixelsOf(nf.Index) > pixN0,
                        "中子星真的涂出像素了 -> " + pixN0 + " → " + world.Field.PixelsOf(nf.Index));
                    ns.Consume();
                    nf.BigBalls.Remove(ns);
                }

                // ── ★ 回归：子弹必须真的会涂领土 ──
                // 这里走**完整的池循环**（BulletPool.FixedUpdate），不是直接调 Paint。
                // 只有走整条链路才能抓住「Step 返回 false、池却不吃这个返回值」
                // 这类回归 —— 症状正是「卡是不卡了，但子弹不涂地了」：
                // 打空的子弹赖在 _active 里不走 → stride = 数量/1200 越涨越大 →
                // allowPaint 几乎全 false → 子弹照飞，地却再也刷不动。
                {
                    var atk = Fac(0);
                    var bp = world.Bullets;
                    Check(bp != null, "BulletPool 存在");
                    if (bp != null)
                    {
                        int baseCount = bp.ActiveCount;
                        int pix0 = world.Field.PixelsOf(0);
                        double stock0 = atk.Bullets;
                        // 放在地图中线（y=6）：四个炮台都在角上，这一路撞不到护盾，
                        // 所以这颗子弹只能死于「数值耗尽」—— 正是要考的那条路径。
                        var seed = bp.Rent(atk, new Vector2(1f, 6f), Vector2.right,
                                           Cfg.BulletSpeed, Cfg.BulletValueFor(atk.Bullets),
                                           Cfg.BulletLifetime, false);
                        // 拖尾有**两种画法**（见 Cfg.BulletTrailUsesTrailRenderer），规格必须都对上。
                        var tr = seed != null ? seed.GetComponentInChildren<TrailRenderer>() : null;
                        if (Cfg.BulletTrailUsesTrailRenderer)
                        {
                            Check(tr != null, "子弹拖尾是 Unity 自带的 TrailRenderer");
                            // ★★★ TrailRenderer 挂在**未激活**的对象上就是**不渲染**。
                            //   合批画法为了省开销让子弹对象永远 inactive（见 BulletPool.Rent），
                            //   切到这条画法时如果忘了激活，症状正是「圆点都在飞、一条拖尾都看不见」。
                            Check(tr.gameObject.activeInHierarchy,
                                "拖尾所在的对象是激活的（未激活的 TrailRenderer 一点都不渲染）");
                        }
                        else
                        {
                            // ★ 合批画法：不建 TrailRenderer（那正是几千次 draw call 的来源），
                            //   改由 BulletTrailRenderer 把全部尾巴拼进一张 Mesh。
                            Check(tr == null, "合批画法下不再每颗子弹挂一个 TrailRenderer");
                            Check(Cfg.BulletTrailMaxPoints >= 4, "合批拖尾有足够的折点 -> "
                                + Cfg.BulletTrailMaxPoints);
                            Check(Cfg.BulletTrailSortingOrder > Cfg.ShieldRingSortingOrder
                                  && Cfg.BulletTrailSortingOrder < Cfg.BattleWallSortingOrder,
                                "合批拖尾的图层仍然在护盾之上、边界墙之下 -> "
                                + Cfg.BulletTrailSortingOrder);
                        }
                        if (tr != null)
                        {
                            Check(tr.emitting, "发射中的子弹拖尾在记录点位");
                            Check(tr.time > 0f, "拖尾有长度（按时间采样）");
                            // ★ 规格：**拖尾不比子弹细**。宽度系数 `BulletTrailRadiusFactor`
                            //   现在是 1（= 和子弹一样宽）；只要别细于子弹就算对。
                            //   宽度必须是从子弹自己的半径推出来的，写死就会在下次改半径时失效。
                            float halfStart = tr.startWidth * 0.5f, halfEnd = tr.endWidth * 0.5f;
                            Check(halfStart + 1e-4f >= Cfg.BulletDotRadius,
                                "拖尾不细于子弹（头） 拖尾半径 " + halfStart.ToString("F3")
                                + " ≥ 子弹半径 " + Cfg.BulletDotRadius.ToString("F3"));
                            Check(halfEnd + 1e-4f >= Cfg.BulletDotRadius,
                                "拖尾不细于子弹（尾） 拖尾半径 " + halfEnd.ToString("F3"));
                            Check(Mathf.Abs(halfStart - Cfg.BulletDotRadius * Cfg.BulletTrailRadiusFactor) < 1e-4f,
                                "拖尾宽度是**从这颗子弹的半径推出来的**（不是写死的）-> "
                                + halfStart.ToString("F4") + " = 半径 × "
                                + Cfg.BulletTrailRadiusFactor.ToString("F2"));
                            Check(tr.sharedMaterial != null, "拖尾有用共享材质（不是每颗一份）");
                            // ★★ 第 103 轮：颜色走 `Palette.BulletTrail`（阵营色提饱和 / 太暗换白），
                            //   **两种画法都得认这一条**。材质是全阵营共享的，所以 TrailRenderer
                            //   画法把颜色写在**每颗子弹自己的 gradient** 上 —— 这里验它真的写进去了。
                            {
                                Color want = Palette.BulletTrail(atk.Color);
                                var g = tr.colorGradient;
                                Color got = g.colorKeys.Length > 0 ? g.colorKeys[0].color : Color.white;
                                Check(Mathf.Abs(got.r - want.r) < 0.02f
                                      && Mathf.Abs(got.g - want.g) < 0.02f
                                      && Mathf.Abs(got.b - want.b) < 0.02f,
                                    "TrailRenderer 的拖尾颜色 = 阵营色提饱和（太暗换白）-> ("
                                    + got.r.ToString("F2") + "," + got.g.ToString("F2") + ","
                                    + got.b.ToString("F2") + ") vs 阵营色色 ("
                                    + want.r.ToString("F2") + "," + want.g.ToString("F2") + ","
                                    + want.b.ToString("F2") + ")");
                            }
                            // 规格①：拖尾不能被护盾挡住 → 要在护盾亮边之上
                            Check(tr.sortingOrder > Cfg.ShieldRingSortingOrder,
                                "拖尾在护盾之上 -> " + tr.sortingOrder
                                + " > " + Cfg.ShieldRingSortingOrder);
                            // 规格②：墙要在子弹拖尾上面 → 拖尾在边界墙之下、圆点之下
                            Check(tr.sortingOrder < Cfg.BattleWallSortingOrder,
                                "拖尾在边界墙之下 -> " + tr.sortingOrder
                                + " < " + Cfg.BattleWallSortingOrder);
                            Check(tr.sortingOrder < Cfg.BulletDotSortingOrder,
                                "拖尾图层在圆点之下 -> " + tr.sortingOrder);
                        }
                        else
                        {
                            // 合批画法的图层同样是 Cfg.BulletTrailSortingOrder —— 两条规格照旧成立
                            Check(Cfg.BulletTrailSortingOrder > Cfg.ShieldRingSortingOrder
                                  && Cfg.BulletTrailSortingOrder < Cfg.BattleWallSortingOrder,
                                "合批拖尾在护盾之上、边界墙之下 -> " + Cfg.BulletTrailSortingOrder);
                        }

                        // ★ 子弹现在按**渲染帧**推进（BulletPool.Update），不再是 FixedUpdate。
                        //   编辑器里 Time.deltaTime 接近 0，所以每轮手动把它挪到一块新地方 ——
                        //   这条测的是「数值耗尽 → 回收」，不能让不动的子弹刷不出新像素而永远赖着。
                        int steps = 0;
                        for (; steps < 400 && seed != null && seed.InUse; steps++)
                        {
                            seed.Pos = new Vector2(1f + steps * 0.05f, 6f);
                            Invoke(bp, "Update");
                        }
                        if (tr != null) Check(!tr.emitting, "回收后的子弹拖尾停止记录");

                        int pix1 = world.Field.PixelsOf(0);
                        Debug.Log(string.Format(
                            "[SMOKE] 子弹飞了 {0} 步 存活={1} 涂了 {2} 像素 库存 {3:F0}->{4:F0} 池内 {5}->{6}",
                            steps, seed != null && seed.InUse, pix1 - pix0, stock0, atk.Bullets,
                            baseCount, bp.ActiveCount));
                        Check(pix1 > pix0, "子弹会涂领土");
                        Check(seed == null || !seed.InUse, "数值耗尽的子弹会消失");
                        // ★ 规格变了：数值耗尽的子弹**不再当场回收**，而是先在场上渐隐
                        //   （拖尾要一点点收掉，不能啪一下没）。所以这里只要求「不越积越多」。
                        Check(bp.ActiveCount <= baseCount + 2,
                            "耗尽的子弹先渐隐再回收（不越积越多）-> _active=" + bp.ActiveCount
                            + " 基准=" + baseCount);
                    }
                }

                // ── ★ 散弹解封（/32768，前 5 分钟单发数值封顶 256）──
                {
                    double big = 1e9;
                    Check(System.Math.Abs(Cfg.BulletValueFor(big, 0.0) - Cfg.ShotValueCap) < 0.001,
                        "散弹解封：未到 5 分钟时单发数值封顶 "
                        + Cfg.ShotValueCap.ToString("F0") + " -> "
                        + Cfg.BulletValueFor(big, 0.0).ToString("F1"));
                    // ★ 用户后来把上限从 256 调到 192（解封时间也从 5 分钟改到 8 分钟，
                    //   见 `ShotValueUnlockTime = 480`）—— 这里只锁当前档位。
                    Check(System.Math.Abs(Cfg.ShotValueCap - 192.0) < 0.001,
                        "解封前的上限就是 192 -> " + Cfg.ShotValueCap.ToString("F0"));
                    // ★★ 第 92 轮用户：「单发子弹数值降低两倍（无论是解封前还是解封后）」
                    //   解封后看除数，解封前看上限 —— 两个都要是减半后的值。
                    Check(System.Math.Abs(Cfg.BulletValueDivisor - 32768.0) < 0.001,
                        "解封后的除数（减半）-> " + Cfg.BulletValueDivisor.ToString("F0"));
                    // ★ 解封时刻不要写死（5 分钟 → 8 分钟改过）—— 一律读 Cfg。
                    Check(System.Math.Abs(Cfg.BulletValueFor(1e9, Cfg.ShotValueUnlockTime + 1d)
                                          - (1e9 / 32768.0 + Cfg.BulletValueBias)) < 1.0,
                        "解封后单发数值真的按 /32768 算 -> "
                        + Cfg.BulletValueFor(1e9, Cfg.ShotValueUnlockTime + 1d).ToString("F0"));
                    double uncapped = big / Cfg.BulletValueDivisor + Cfg.BulletValueBias;
                    Check(System.Math.Abs(Cfg.BulletValueFor(big, Cfg.ShotValueUnlockTime + 1d) - uncapped) < 1.0,
                        "散弹解封：过了 5 分钟一律取 /" + Cfg.BulletValueDivisor.ToString("F0") + "（不封顶）-> "
                        + Cfg.BulletValueFor(big, 301d).ToString("F0"));
                    Check(Cfg.BulletValueFor(Cfg.StartBullets, 0.0) < Cfg.ShotValueCap,
                        "开局弹药算出来的数值本来就不到上限，封顶不影响它 -> "
                        + Cfg.BulletValueFor(Cfg.StartBullets, 0.0).ToString("F1"));
                    Check(System.Math.Abs(Cfg.BulletValueFor(Cfg.StartBullets, 0.0)
                                          - Cfg.BulletValueFor(Cfg.StartBullets)) < 0.001,
                        "封顶只咬「算出来就超过上限」的那种");
                    Check(System.Math.Abs(Cfg.StartBullets - 100000.0) < 0.5,
                        "开局弹药 = 100,000 -> " + Cfg.StartBullets.ToString("F0"));
                    // ★ 判据：**密度翻倍 → 单发数值减半**（除数跟着翻倍），
                    //   所以「每秒泼出去的数值总量」不随密度变。
                    // ★★ 规格（用户第 69 轮）：**每一个单位角度只发一发** ——
                    //    密度靠「把单位角度做小」，不是靠「每单位角度多发几发」。
                    //    自检要能算出「每秒发几发」= 转速 ÷ 单位角度。
                    {
                        float degPerSec = Cfg.TurretSpinSpeed;
                        float perSec = degPerSec / Mathf.Max(Cfg.BulletAngleStepDeg, 1e-5f);
                        Debug.Log(string.Format(
                            "[SMOKE] 发射模型：转速 {0:F0}°/s ÷ 单位角度 {1:F3}° = {2:F0} 发/秒",
                            degPerSec, Cfg.BulletAngleStepDeg, perSec));
                        Check(Cfg.BulletAngleStepDeg > 0f && Cfg.BulletAngleStepDeg < 1f,
                            "单位角度是个「细」的角度 -> " + Cfg.BulletAngleStepDeg.ToString("F3") + "°");
                        Check(perSec > 100f,
                            "按转速换算出的发数够密 -> " + perSec.ToString("F0") + " 发/秒");
                    }
                    Check(System.Math.Abs(Cfg.BulletSpeed - 3.0f) < 0.01f,
                        "弹速 3.0 m/s -> " + Cfg.BulletSpeed.ToString("F2"));
                    // ★ 「每一帧打更多子弹」= 发射速度 × 每次发射的弹数（两者是独立的两个旋钮）
                    Check(Cfg.BulletMaxPerFrame >= 1 && Cfg.BulletIdleFireRate > 0f,
                        "一帧发数有上限（近防猛转时不喷尖峰）+ 停转时有时间兜底 -> 上限 "
                        + Cfg.BulletMaxPerFrame + " 发，兜底 "
                        + Cfg.BulletIdleFireRate.ToString("F0") + " 发/秒");
                    // ★ 严格卡在 5 分钟这条线上
                    double justBefore = Cfg.BulletValueFor(big, Cfg.ShotValueUnlockTime - 0.1);
                    double justAfter = Cfg.BulletValueFor(big, Cfg.ShotValueUnlockTime + 0.1);
                    Check(System.Math.Abs(justBefore - Cfg.ShotValueCap) < 0.001,
                        "解封前一刻（299.9s）仍然封顶 -> " + justBefore.ToString("F1"));
                    Check(justAfter > Cfg.ShotValueCap * 2d,
                        "解封后一刻（300.1s）立刻不封顶 -> " + justAfter.ToString("F0"));
                }

                // ── ★ 护盾羽化：纹理和判定共用同一个带宽 ──
                {
                    float r = 1.2f;
                    Check(Bullet.FeatherFactor(r, r * 0.5f) >= 0.999f,
                        "护盾内圈不羽化（该全额挡还是全额挡）");
                    Check(Bullet.FeatherFactor(r, r) <= 0.001f,
                        "护盾最外沿羽化到 0（擦着边等于没碰到）");
                    float mid = Bullet.FeatherFactor(r, r * (1f - Cfg.ShieldFeatherBand * 0.5f));
                    Check(mid > 0.05f && mid < 0.95f,
                        "护盾羽化带里是过渡值 -> " + mid.ToString("F2"));

                    var shex = Primitives.ShieldDisc.texture;
                    int sn = shex.width, sc = sn / 2;
                    int atFeather = sc + Mathf.RoundToInt((1f - Cfg.ShieldFeatherBand) * sn * 0.5f);
                    Check(shex.GetPixel(atFeather, sc).a > 0.9f,
                        "护盾纹理在羽化起点之前还是实心的");
                }

                // ── ★ 破盾：护盾归 0 之后，**半径按同一条非线性缓动缩到 0**，缩完才隐藏 ──
                {
                    var tf1 = Fac(1);
                    var sr = tf1.Turret.transform.Find("ShieldFill");
                    var fill = sr != null ? sr.GetComponent<SpriteRenderer>() : null;
                    Check(fill != null, "找得到护盾填充的渲染器");
                    if (fill != null)
                    {
                        double keep = tf1.Shield;
                        float rSave = tf1.ShieldRadius;
                        tf1.Shield = 0d;
                        Invoke(tf1.Turret, "Update");
                        Check(fill.enabled, "破盾后还在显示（正在缩，不是瞬间消失）");

                        // 推约 0.83 秒的缓动（30fps 一步 = 1/30）
                        for (int k = 0; k < 25; k++) tf1.StepShieldRadius(1f / 30f);
                        Invoke(tf1.Turret, "Update");
                        Check(!fill.enabled && tf1.ShieldRadius < rSave * 0.1f,
                            "半径按缓动缩没了才隐藏 -> r=" + tf1.ShieldRadius.ToString("F3"));

                        tf1.Shield = keep;
                        tf1.ShieldRadius = rSave;
                        Invoke(tf1.Turret, "Update");
                        Check(fill.enabled, "护盾回来了就继续显示");
                    }
                }

                // ── ★ 引力球的持续特效 ──
                {
                    var fxFac = Fac(2);
                    var nb = world.SpawnBigBall(fxFac, 50d);
                    var fx = nb != null ? nb.GetComponentInChildren<GravityFx>(true) : null;
                    Check(fx != null, "大球上挂了引力特效");
                    if (fx != null)
                    {
                        Check(!fx.gameObject.activeSelf, "普通大球不显示引力特效");
                        nb.Value = Cfg.GravityBallThreshold * 2d;
                        Invoke(nb, "FixedUpdate");
                        Check(fx.gameObject.activeSelf, "变成引力球后特效开起来");
                    }
                }

                // ── ★★★ 引力（用户本轮）：**真正的引力** ──────────────────────────
                //   真引力 = `F = G·m₁m₂/r²` ⇒ `a = F/m₂ = G·m₁/r²`
                //   ⇒ **加速度与被吸球自己的数值无关**（大球小球一视同仁）。
                //   第 88~103 轮那条「被吸的越小越显著」（`a ∝ 1/被吸球数值`）已按用户要求关掉
                //   （`GravityTargetValuePower = 0`）—— 它做出来的是**恒定力**，更像吸尘器而不是引力。
                {
                    // 距离² 反比（真引力也是平方反比）
                    float d1 = GameWorld.GravityPullAccel(103e6, 5e6, 1f);
                    float d2 = GameWorld.GravityPullAccel(103e6, 5e6, 2f);
                    Check(Mathf.Abs(d1 / Mathf.Max(d2, 1e-9f) - 4f) < 0.01f,
                        "引力随距离² 衰减 -> 1m " + d1.ToString("F3") + " / 2m " + d2.ToString("F3"));

                    // ★★★ 真引力：**被吸球是谁完全不影响加速度**
                    float aTiny = GameWorld.GravityPullAccel(103e6, 103e3, 1f);   // 103K
                    float aHalf = GameWorld.GravityPullAccel(103e6, 50e6, 1f);    // 50M
                    float aHuge = GameWorld.GravityPullAccel(103e6, 1e12, 1f);    // 1000B
                    Check(Mathf.Abs(aTiny - aHalf) < 1e-3f && Mathf.Abs(aTiny - aHuge) < 1e-3f,
                        "★★ 真引力：被吸球数值不影响加速度（103K / 50M / 1000B 一模一样）-> "
                        + aTiny.ToString("F3") + " / " + aHalf.ToString("F3") + " / "
                        + aHuge.ToString("F3"));

                    // 常量的**读数**：参考点（100M）在 1m 处 = `GravityOnBigBall` 本身
                    float aRef = GameWorld.GravityPullAccel(Cfg.GravityValueRef, 5e6, 1f);
                    Check(Mathf.Abs(aRef - Cfg.GravityOnBigBall) < 1e-2f,
                        "★ GravityOnBigBall = 「100M 的引力球、1m 处」的加速度 -> "
                        + aRef.ToString("F1") + " m/s²");

                    // 上限兜底，别把物理求解器甩飞
                    Check(GameWorld.GravityPullAccel(1e15, 1e3, 0.3f) <= Cfg.GravityBigBallMaxAccel + 1e-3f,
                        "极端数值比也夹在加速度上限内 -> "
                        + GameWorld.GravityPullAccel(1e15, 1e3, 0.3f).ToString("F1"));

                    // ── 行为验证：普通大球被施力，**引力球之间不施力** ──
                    var gf = Fac(1);
                    var g1 = world.SpawnBigBall(gf, 1.1e9);        // 引力球
                    var g2 = world.SpawnBigBall(gf, 1.2e9);        // 引力球
                    var nb2 = world.SpawnBigBall(gf, 5e6);         // 普通大球
                    g1.transform.position = new Vector3(6f, 7f, 0f);
                    g1.Body.position = new Vector2(6f, 7f);
                    g2.transform.position = new Vector3(7f, 7f, 0f);
                    g2.Body.position = new Vector2(7f, 7f);
                    nb2.transform.position = new Vector3(7f, 6f, 0f);
                    nb2.Body.position = new Vector2(7f, 6f);

                    Invoke(world, "StepGravity");
                    Check(nb2.Body.totalForce.sqrMagnitude > 1e-6f,
                        "普通大球会被引力球拉 -> |F|="
                        + nb2.Body.totalForce.magnitude.ToString("F3"));
                    // ★★★ 第 103 轮（用户）：「**现在引力球之间也需要一点引力**」——
                    //   第 88 轮那句"引力球之间没有引力"作废。这里要求：
                    //   ① 真的有引力（力不为 0）；② 实际施加的加速度不超过吸一颗普通大球的一半。
                    // ★★ 本轮上真引力之后：引力球之间**不再有任何特殊系数**，它们和别的球走
                    //   **同一条公式**，所以第 103 轮那条「『一点』引力」的规格**已经作废**。
                    //   ② 现在之所以仍然成立，是因为两边都被**速度夹顶**压到了同一个 6 m/s 上限
                    //   （`aGN` 是**没夹顶**的公式值、`aGG` 是**实际施加**的力反算出来的）——
                    //   也就是说这条断言现在钉的是「**速度夹顶有效**」，而**不是**
                    //   「引力球之间有特殊系数」。别把它当后者读。
                    {
                        // ★ 要比的是**加速度**不是力：`aGG` 从施加的力反算，`aGN` 直接取公式值。
                        //   ★ 真引力下 `GravityPullAccel` 与被吸球数值无关，而 `AddForce(acc × mass)`
                        //     让**力** ∝ 质量 —— 所以两边比"力"是没有意义的，必须比加速度。
                        float aGG = g2.Body.totalForce.magnitude / g2.Body.mass;
                        float aGN = GameWorld.GravityPullAccel(g1.Value, nb2.Value, 1f)
                                    * Mathf.Max(Cfg.GravityTickDivisor, 1);
                        Check(aGG > 1e-6f,
                            "★ 引力球**之间有引力了**（第 103 轮）-> 加速度 "
                            + aGG.ToString("F4") + " m/s²");
                        Check(aGG < aGN * 0.5f,
                            "★ 但只是「**一点**」引力：明显弱于吸普通大球 -> "
                            + aGG.ToString("F4") + " < " + (aGN * 0.5f).ToString("F4") + " m/s²");
                    }

                    g1.Consume(); g2.Consume(); nb2.Consume();
                    gf.BigBalls.Remove(g1); gf.BigBalls.Remove(g2); gf.BigBalls.Remove(nb2);
                }

                // ── ★★★ 引力球的**闩锁**（用户第 103 轮）────────────────────────
                //   「当引力球数值**低于 100M 时也可以具有引力**，引力公式不变，
                //     但是可以吃己方子弹，**但不能被传递 / 传递数值**」。
                //   所以「有没有引力」= `HasGravity`（**只置位、永不清零**），
                //       「算不算引力球」= `IsGravityBall`（实时，管数值传递那两条路）。
                {
                    var lf = Fac(3);
                    var lb = world.SpawnBigBall(lf, 1.5e9);      // 先当过引力球
                    Check(lb.HasGravity && lb.IsGravityBall,
                        "到过 100M 的球：有引力 + 是引力球");
                    lb.Value = 5e7;                              // 被撞/被吸到 100M 以下
                    Check(lb.HasGravity, "★★ 数值掉回 100M 以下**仍然有引力**（闩锁）");
                    Check(!lb.IsGravityBall,
                        "★★ 但已经**不算引力球**了 -> 不再参与球与球之间的数值传递（规格）");

                    // ★ 生产代码里两条路各自读的是哪一个，也要在行为上对上：
                    //   ① 收集引力球（施加引力）读 `HasGravity` —— 掉到 100M 以下照样能吸
                    //   ② 球与球之间传数值读 `IsGravityBall` —— 掉下去之后不再传
                    var lother = world.SpawnBigBall(lf, 1e8);
                    lother.transform.position = lb.transform.position + new Vector3(0.2f, 0f, 0f);
                    lother.Body.position = (Vector2)lother.transform.position;
                    double lbV = lb.Value, loV = lother.Value;
                    Invoke(lb, "ResolveSameFaction", lother);
                    Check(System.Math.Abs(lb.Value - lbV) < 1e-6 && System.Math.Abs(lother.Value - loV) < 1e-6,
                        "★★ 低于 100M 之后**不再互相传数值** -> "
                        + lb.Value.ToString("F0") + " / " + lother.Value.ToString("F0"));

                    lb.Consume(); lother.Consume();
                    lf.BigBalls.Remove(lb); lf.BigBalls.Remove(lother);
                }

                // ── ★★★ 「极小数值球被引力球吸引时会弹动鬼畜」的修法（用户第 103 轮）──
                //   给「**朝引力球靠近的速度**」加一道闸：已经在以
                //   `GravityBigBallMaxApproachSpeed` 靠过去了，就**整份不加引力**。
                //   ★ 只挡"加速"、不刹车（往外飞的时候 dot<0，照旧被完整拉回来）。
                {
                    Check(Cfg.GravityBigBallMaxApproachSpeed > 0f
                          && Cfg.GravityBigBallMaxApproachSpeed < 10f,
                        "★ 被吸球的「靠近速度上限」是个正常值 -> "
                        + Cfg.GravityBigBallMaxApproachSpeed.ToString("F1") + " m/s");

                    var sf = Fac(0);
                    var sg = world.SpawnBigBall(sf, 1.0e9);     // 引力球
                    sg.transform.position = new Vector3(7f, 7f, 0f);
                    sg.Body.position = new Vector2(7f, 7f);
                    sg.Body.velocity = Vector2.zero;

                    // ★ 引力范围是 14m = 整张地图 —— 场上有**任何**别的引力球都会串进来。
                    //   所以这里只留 sg 这一颗源，其余的清掉（前面的用例应该已经清干净了）。
                    for (int fi = 0; fi < world.Factions.Count; fi++)
                    {
                        var fac = world.Factions[fi];
                        for (int bi = fac.BigBalls.Count - 1; bi >= 0; bi--)
                        {
                            var bb = fac.BigBalls[bi];
                            if (bb == null || bb == sg || !bb.HasGravity) continue;
                            // ★ `Consume()` 自己就会把球从 `Owner.BigBalls` 里摘掉 ——
                            //   这里再 `RemoveAt(bi)` 会二次移除、直接越界。
                            bb.Consume();
                        }
                    }

                    // ★★ `Rigidbody2D.totalForce` 是**攒着**的（Unity 每个物理步才清一次），
                    //   而这里是直接调 StepGravity、不走物理步 —— 所以**每个用例都要用一颗新的球**，
                    //   不然上一个用例的力会串进来（第一版就是这么误报的）。
                    System.Func<Vector2, float> forceOnTinyBall = delegate (Vector2 v0)
                    {
                        var t = world.SpawnBigBall(sf, 200d);       // 极小数值的球
                        t.transform.position = new Vector3(8f, 7f, 0f);
                        t.Body.position = new Vector2(8f, 7f);
                        t.Body.velocity = v0;
                        Invoke(world, "StepGravity");
                        float f = t.Body.totalForce.magnitude;
                        t.Consume();
                        sf.BigBalls.Remove(t);
                        return f;
                    };

                    // ① 不动 → 会被完整地吸
                    float pulled = forceOnTinyBall(Vector2.zero);
                    Check(pulled > 1e-6f,
                        "★ 没速度时，小球的引力照旧施加 -> |F|=" + pulled.ToString("F3"));

                    // ② 已经以超上限的速度**朝引力球**冲 → 这份引力不加（免得越甩越快）
                    float gated = forceOnTinyBall(
                        new Vector2(-Cfg.GravityBigBallMaxApproachSpeed - 1f, 0f));
                    Check(gated < 1e-6f,
                        "★★ 已经在以超上限的速度靠近 → 不再加引力（「鬼畜」的根因）-> |F|="
                        + gated.ToString("F6"));

                    // ③ 但**背离**引力球飞（往外走）时不算"靠近" → 照旧被完整拉回来，
                    //    否则球出去的一路上被减速，就永远逃不掉了（子弹那条踩过这个坑）
                    float receding = forceOnTinyBall(
                        new Vector2(Cfg.GravityBigBallMaxApproachSpeed + 1f, 0f));
                    Check(receding > 1e-6f,
                        "★★ 背离时照旧被拉（不是刹车）-> |F|="
                        + receding.ToString("F3"));

                    sg.Consume();
                    sf.BigBalls.Remove(sg);
                }

                // ── ★★★ 大球速度硬上限（用户第 103 轮）──
                //   「原版引力效果很大，但大球碰到引力球时还是能弹得很远；
                //     我们的则是球的速度已经快到瞬移了」。
                {
                    var vf = Fac(0);
                    var vb = world.SpawnBigBall(vf, 1e6);
                    vb.transform.position = new Vector3(6f, 6f, 0f);
                    vb.Body.position = new Vector2(6f, 6f);
                    vb.Body.velocity = new Vector2(50f, 0f);
                    Invoke(vb, "FixedUpdate");
                    Check(vb.Body.velocity.magnitude <= Cfg.BigBallMaxSpeed + 1e-2f,
                        "★★ 大球速度有**硬上限**（引力再强也不会快到瞬移）-> "
                        + vb.Body.velocity.magnitude.ToString("F2") + " ≤ "
                        + Cfg.BigBallMaxSpeed.ToString("F2") + " m/s");
                    Check(Cfg.BigBallMaxSpeed > 0f && Cfg.BigBallMaxSpeed < Cfg.BulletMaxSpeed + 2f,
                        "★ 上限是个正常量级（撞上去还能正常弹开）-> "
                        + Cfg.BigBallMaxSpeed.ToString("F1") + " m/s");
                    vb.Consume();
                    vf.BigBalls.Remove(vb);

                    // ★ 引力**一个 tick** 给出的速度增量也必须被夹住 ——
                    //   这才是「瞬移」的真正来源：一次塞 26 m/s，下一帧就跨出去 0.4m+。
                    var gsrc = world.SpawnBigBall(FacOther(0), 1e12);   // 巨大的引力球
                    gsrc.transform.position = new Vector3(7f, 7f, 0f);
                    gsrc.Body.position = new Vector2(7f, 7f);
                    gsrc.Body.velocity = Vector2.zero;
                    var tv = world.SpawnBigBall(vf, 1000d);            // 极小数值的球
                    tv.transform.position = new Vector3(8f, 7f, 0f);
                    tv.Body.position = new Vector2(8f, 7f);
                    tv.Body.velocity = Vector2.zero;
                    Invoke(world, "StepGravity");
                    float tickDv = tv.Body.totalForce.magnitude / tv.Body.mass
                                   * Time.fixedDeltaTime;
                    Check(tickDv <= Cfg.BigBallMaxSpeed + 1e-3f,
                        "★★ 引力**一次 tick** 给的速度增量也被夹住（不再「瞬移」）-> "
                        + tickDv.ToString("F2") + " ≤ " + Cfg.BigBallMaxSpeed.ToString("F2") + " m/s");
                    tv.Consume(); gsrc.Consume();
                    vf.BigBalls.Remove(tv);
                    FacOther(0).BigBalls.Remove(gsrc);
                }

                // ── ★★ 引力球吃子弹（用户第 92 轮）：**30% 吸收 / 70% 反弹** ──
                //   以前是「己方子弹打到己方引力球一律弹开」，现在改成掷骰子。
                {
                    Check(Mathf.Abs(Cfg.GravityBallAbsorbChance - 0.30f) < 1e-4f,
                        "引力球吸收概率 = 30% -> " + Cfg.GravityBallAbsorbChance.ToString("P0"));

                    var abF = Fac(2);
                    var abBall = world.SpawnBigBall(abF, 1.5e9);      // 中子星档的引力球
                    abBall.transform.position = new Vector3(6f, 7f, 0f);
                    abBall.Body.position = new Vector2(6f, 7f);

                    double abBefore = abBall.Value;
                    double abShot = 100d;
                    int abAbsorb = 0, abBounce = 0;
                    const int abTrials = 600;
                    for (int abI = 0; abI < abTrials; abI++)
                    {
                        var abB = Bullet.CreateBlank(world.BattleRoot);
                        abB.Launch(abF, new Vector2(5f, 7f), Vector2.right, Cfg.BulletSpeed,
                                   abShot, 10f, false);
                        abB.HitBigBall(abBall);
                        if (abB.InUse) { abBounce++; abB.Release(); }
                        else abAbsorb++;
                        UnityEngine.Object.DestroyImmediate(abB.gameObject);
                    }

                    float abRatio = abAbsorb / (float)abTrials;
                    Check(abAbsorb > 0 && abBounce > 0,
                        "★ 吸收和反弹**两种结果都会出现** -> 吸收 " + abAbsorb
                        + " / 反弹 " + abBounce);
                    Check(abRatio > 0.18f && abRatio < 0.42f,
                        "吸收比例落在 30% 附近（" + abTrials + " 次）-> " + abRatio.ToString("P1"));
                    Check(System.Math.Abs(abBall.Value - (abBefore + abAbsorb * abShot)) < 1d,
                        "被吸收的那些子弹，数值真的并进了引力球（反弹的没有）-> 球 "
                        + abBefore.ToString("F0") + " -> " + abBall.Value.ToString("F0")
                        + "，吸收 " + abAbsorb + " 发 × " + abShot.ToString("F0"));

                    abBall.Consume(); abF.BigBalls.Remove(abBall);
                }

                // ── 亡语（★ 第 93 轮：不再一帧全冒出来，改成**排队、每帧放几颗**）──
                var f0 = Fac(0);
                int ballsBefore = f0.LiveBallCount;
                double bulletsBefore = f0.Bullets;
                // ★ 名字带 0：外层作用域已经有一个 `bigBefore` 了（C# 会报 CS0136，踩过两次）
                int bigBefore0 = f0.BigBalls.Count;
                f0.Turret.Die();

                int expected = ballsBefore + (Cfg.DeathrattleSpawnBulletBall && bulletsBefore > 0d ? 1 : 0);
                int firstBatch = Mathf.Min(Cfg.DeathrattleSpawnPerFrame, expected);
                // ★ 第 93 轮起：`OnTurretDied` 只**排队**，真正生成在 Update 里的 `DrainDeathrattle`
                Check(f0.BigBalls.Count == bigBefore0,
                    string.Format("亡语大球先排队、不当场生成 -> 实际多了 {0} 颗",
                        f0.BigBalls.Count - bigBefore0));
                Check(!f0.Alive, "炮台已死亡");
                Invoke(world, "DrainDeathrattle");
                Check(f0.BigBalls.Count == bigBefore0 + firstBatch,
                    string.Format("每帧只放 {0} 颗 -> 期望{1} 实际{2}",
                        Cfg.DeathrattleSpawnPerFrame, bigBefore0 + firstBatch, f0.BigBalls.Count));

                // 把队列排干 → 总数仍然对得上（没有多生也没有漏生）
                for (int i = 0; i < 500 && f0.BigBalls.Count < bigBefore0 + expected; i++)
                    Invoke(world, "DrainDeathrattle");
                Check(f0.BigBalls.Count == bigBefore0 + expected,
                    string.Format("排干队列后总数 = 增倍球{0}个 + 子弹球 -> 实际{1}",
                        ballsBefore, f0.BigBalls.Count - bigBefore0));
                Debug.Log(string.Format("[SMOKE] 亡语后 bigBalls={0}（增倍球{1}个 + 子弹球，分 {2} 帧放完）",
                    f0.BigBalls.Count - bigBefore0, ballsBefore,
                    Mathf.CeilToInt(expected / (float)Mathf.Max(Cfg.DeathrattleSpawnPerFrame, 1))));

                // ── ★★ 亡语球不再穿墙（用户第 93 轮）──
                {
                    Check(Cfg.DeathrattleSpawnPerFrame >= 1 && Cfg.DeathrattleSpawnPerFrame <= 8,
                        "亡语大球每帧只放几颗 -> " + Cfg.DeathrattleSpawnPerFrame);
                    Check(Cfg.BigBallContainInArena, "开着「大球不许出界」的兜底");

                    // 出生点：沿向日葵螺旋散开，互相不重合、而且都在战场里
                    var pts = new System.Collections.Generic.List<Vector2>();
                    for (int k = 0; k < 24; k++)
                        pts.Add(GameWorld.SpawnRing(new Vector2(1.82f, 1.82f), k));
                    float minSep = float.MaxValue;
                    for (int i2 = 0; i2 < pts.Count; i2++)
                        for (int j2 = i2 + 1; j2 < pts.Count; j2++)
                            minSep = Mathf.Min(minSep, (pts[i2] - pts[j2]).magnitude);
                    Check(minSep > 0.20f, "★ 螺旋出生点散得开（不是全堆在一个点上）-> 最近两颗差 "
                        + minSep.ToString("F2") + " m（球半径 ~1m，所以真正兜底的是下面那条）");
                    bool insideArena = true;
                    for (int i2 = 0; i2 < pts.Count; i2++)
                        if (pts[i2].x < 0f || pts[i2].x > Cfg.MapWorldSize
                            || pts[i2].y < 0f || pts[i2].y > Cfg.MapWorldSize) insideArena = false;
                    Check(insideArena, "★ 螺旋出生点全在战场内（不会一出生就在墙外）");

                    // 兜底：硬摆到墙外面，跑一步就该被摆回来
                    var cb2 = world.SpawnBigBall(Fac(1), 1e6d, new Vector2(3f, 3f));
                    cb2.Radius = 1f;                       // 兜底读的就是这个字段，不用刷物理体
                    cb2.Body.position = new Vector2(-2.5f, 3f);
                    Invoke(cb2, "ClampInsideArena");
                    Check(cb2.Body.position.x >= 1f - 1e-3f,
                        "★★ 被顶到墙外的大球会被摆回场内 -> x = "
                        + cb2.Body.position.x.ToString("F2") + "（球半径 1）");
                    cb2.Consume(); cb2.Owner.BigBalls.Remove(cb2);
                }

                // ── 复活结算 ──
                BigBall ball = null;
                for (int i = 0; i < f0.BigBalls.Count; i++)
                {
                    var cand = f0.BigBalls[i];
                    if (cand == null || cand.Consumed) continue;
                    if (ball == null || cand.Value > ball.Value) ball = cand;
                }
                Check(ball != null, "亡语后能取到一颗大球");
                double v = ball.Value;
                int gainExpected = Mathf.FloorToInt((float)System.Math.Log(System.Math.Max(v, 1d))
                                                    * Cfg.RespawnBallCountLogScale + Cfg.RespawnBallCountLogBias);

                int fxBefore = world.BattleRoot != null
                    ? world.BattleRoot.GetComponentsInChildren<Explosion>(true).Length : 0;
                world.ApplyRespawn(f0, ball);
                Check(f0.Alive, "复活成功");
                // ★ 规格：复活要有特效
                {
                    int fxAfter = world.BattleRoot != null
                        ? world.BattleRoot.GetComponentsInChildren<Explosion>(true).Length : 0;
                    Check(fxAfter > fxBefore, "复活放出了特效 -> " + fxBefore + " → " + fxAfter);
                    Check(Cfg.RespawnFxLife > 0f && Cfg.RespawnFxRadiusScale > 1f,
                        "复活特效有寿命/尺寸参数");
                }
                Check(f0.LiveBallCount == Mathf.Max(gainExpected, 0), "复活换算的增倍球数量");
                // ★ 第 93 轮（用户）：「复活小球数量砍下来一点」→ 系数 1.0 → 0.8（约少两成半）
                Check(Mathf.Abs(Cfg.RespawnBallCountLogScale - 0.8f) < 1e-4f,
                    "复活换球系数 = 0.8（原来 1.0）-> " + Cfg.RespawnBallCountLogScale);
                {
                    int g1 = Mathf.FloorToInt((float)(System.Math.Log(1e6)
                             * Cfg.RespawnBallCountLogScale + Cfg.RespawnBallCountLogBias));
                    int g2 = Mathf.FloorToInt((float)(System.Math.Log(93.7e6)
                             * Cfg.RespawnBallCountLogScale + Cfg.RespawnBallCountLogBias));
                    Check(g1 == 7 && g2 == 10,
                        "复活给球 1,0M → 7 颗、93.7M → 10 颗（改前是 9 / 14）-> "
                        + g1 + " / " + g2);
                }
                Check(System.Math.Abs(f0.Bullets - v * Cfg.RespawnBulletsFraction) < 0.001d, "复活子弹 = 大球值*0.1");
                Check(System.Math.Abs(f0.Shield - v * Cfg.RespawnShieldFraction) < 0.001d, "复活护盾 = 大球值*0.05");
                // 规格：大球数值变成原本的 60%（不消耗）
                Check(System.Math.Abs(ball.Value - v * Cfg.RespawnBallValueKeep) < 1e-6d, "大球保留 60%");
                Debug.Log(string.Format("[SMOKE] 复活: 大球值 {0} -> 增倍球+{1} 子弹 {2} 护盾 {3} 大球余 {4}",
                    BigNumber.Suffixed(v), gainExpected,
                    BigNumber.Grouped(f0.Bullets), BigNumber.Suffixed(f0.Shield),
                    BigNumber.Suffixed(ball.Value)));

                // ── 大球碰撞：不同阵营各扣 min(a,b) × 1/4（第 99 轮改回）──
                // ★★ 第 92 轮用户说「所有都是掉 3/4」，三条一起提到了 0.75；
                //   ★ 第 99 轮他把**不同阵营相撞这一条**改回 **1/4**，另外两条仍是 3/4。
                //   所以这里三条各自验，数值**不再统一**。
                {
                    // ★ 第 99 轮用户把**这一条**改回了 1/4（另外两条仍然是 3/4，别顺手统一）
                    Check(System.Math.Abs(Cfg.BigBallClashLossFraction - 0.25d) < 1e-9,
                        "不同阵营相撞：各扣 min 的 1/4 -> " + Cfg.BigBallClashLossFraction);
                    Check(System.Math.Abs(Cfg.GravityBallAbsorbFraction - 0.75d) < 1e-9,
                        "引力球吃自家小球：转走小球的 3/4 -> " + Cfg.GravityBallAbsorbFraction);
                    Check(System.Math.Abs(Cfg.GravityBallMutualFraction - 0.75d) < 1e-9,
                        "两颗引力球互撞：也转 3/4 -> " + Cfg.GravityBallMutualFraction);

                    var a = world.SpawnBigBall(Fac(0), 1000000d);
                    var b = world.SpawnBigBall(FacOther(0), 400000d);   // 必须和 a 不同阵营
                    double loss = System.Math.Min(a.Value, b.Value) * Cfg.BigBallClashLossFraction;
                    double expectA = a.Value - loss, expectB = b.Value - loss;
                    Invoke(a, "ResolveCrossFaction", b);
                    Check(System.Math.Abs(a.Value - expectA) < 1d, "大球碰撞 A 扣除");
                    Check(System.Math.Abs(b.Value - expectB) < 1d, "大球碰撞 B 扣除");
                    Check(loss > 400000d * 0.2d && loss < 400000d * 0.3d,
                        "扣掉的是 min 的 1/4 -> 扣 " + loss.ToString("F0"));
                    Debug.Log(string.Format("[SMOKE] 碰撞: 1,0M vs 400K -> A={0} B={1}",
                        BigNumber.Suffixed(a.Value), BigNumber.Suffixed(b.Value)));
                    a.Consume(); b.Consume();
                    a.Owner.BigBalls.Remove(a);
                    b.Owner.BigBalls.Remove(b);

                    // 同阵营：引力球吃小球 —— 转移的也是 3/4
                    var gAb = world.SpawnBigBall(Fac(0), 5e8d);    // 引力球
                    var gSm = world.SpawnBigBall(Fac(0), 2e7d);    // 普通大球
                    double gSmBefore = gSm.Value;
                    Invoke(gAb, "ResolveSameFaction", gSm);
                    Check(System.Math.Abs((gSmBefore - gSm.Value) - gSmBefore * 0.75d) < 1d,
                        "引力球吃自家小球：小球掉自身的 3/4 -> 掉了 "
                        + (gSmBefore - gSm.Value).ToString("F0") + " / 原 " + gSmBefore.ToString("F0"));
                    Check(gAb.Value > 5e8d,
                        "转过去的数值进了引力球 -> " + gAb.Value.ToString("F0"));

                    // 同阵营：两颗引力球互撞 —— 也是 3/4
                    var g1b = world.SpawnBigBall(Fac(0), 8e8d);
                    var g2b = world.SpawnBigBall(Fac(0), 3e8d);
                    double g2Before = g2b.Value;
                    Invoke(g1b, "ResolveSameFaction", g2b);
                    Check(System.Math.Abs((g2Before - g2b.Value) - g2Before * 0.75d) < 1d,
                        "两颗引力球互撞：小的掉自身的 3/4 -> 掉了 "
                        + (g2Before - g2b.Value).ToString("F0") + " / 原 " + g2Before.ToString("F0"));

                    gAb.Consume(); gSm.Consume(); g1b.Consume(); g2b.Consume();
                    gAb.Owner.BigBalls.Remove(gAb);
                    gSm.Owner.BigBalls.Remove(gSm);
                    g1b.Owner.BigBalls.Remove(g1b);
                    g2b.Owner.BigBalls.Remove(g2b);
                }

                // ── ★★ 第 111 轮：吸血球 ──
                //   规格（用户）：「和普通大球相似，不同的是在碰撞敌方大球时，
                //   如果敌方大球数值小于自身的 50%，则吸收敌方大球 50% 的数值，
                //   否则按正常大球扣血。」
                {
                    var vf = Fac(0);
                    var ef = FacOther(0);

                    // ★ 第 113 轮：调试键 V 的数值（99M）要守在 100M 引力门槛之下，
                    //   否则刷出来的球会同时挂引力特效，就看不清吸血球自己的螺旋笔迹了。
                    Check(Cfg.DebugVampireBallValue == 99000000d
                          && Cfg.DebugVampireBallValue < Cfg.GravityBallThreshold,
                        "调试键 V：99M 吸血球，且低于引力门槛 100M -> "
                        + (Cfg.DebugVampireBallValue / 1000000d).ToString("F0") + "M");

                    // ★ 第 125 轮：调试键 L —— 给**每个阵营**在左栏各补一颗增倍球。
                    //   走 `GameWorld.GrantBallToAllFactions`（和每 80 秒补球同一个入口）。
                    {
                        int facN = world.Factions.Count;
                        var ballCountBefore = new int[facN];
                        for (int i = 0; i < facN; i++)
                            ballCountBefore[i] = world.Factions[i].Balls.Count;

                        int granted = world.GrantBallToAllFactions();
                        Check(granted == facN,
                            "调试键 L：给每个阵营各发一颗增倍球 -> " + granted + "/" + facN);

                        bool plusOne = true;
                        for (int i = 0; i < facN; i++)
                        {
                            var list = world.Factions[i].Balls;
                            var nb = list.Count > 0 ? list[list.Count - 1] : null;
                            if (list.Count != ballCountBefore[i] + 1 || nb == null
                                || System.Math.Abs(nb.Value - Cfg.NewBallStartValue) > 1e-6d)
                                plusOne = false;
                        }
                        Check(plusOne,
                            "调试键 L：新球数值 = NewBallStartValue（和常规补球一致）-> "
                            + Cfg.NewBallStartValue.ToString("F0"));

                        // 清理：把这轮加出来的球收掉，免得影响后面的断言
                        for (int i = 0; i < facN; i++)
                        {
                            var list = world.Factions[i].Balls;
                            while (list.Count > ballCountBefore[i])
                            {
                                var b = list[list.Count - 1];
                                list.RemoveAt(list.Count - 1);
                                if (b != null) b.Consume();
                            }
                        }
                    }

                    // ★ 第 117 轮：按用户给的 Algodoo poststep 原文核规格
                    Check(Mathf.Abs(Cfg.VampireFxSpawnPerFrame - 0.5f) < 1e-4f
                          && Mathf.Abs(Cfg.VampireFxLife - 1.7f) < 1e-4f
                          && Mathf.Abs(Cfg.VampireFxTrailTime - 0.2f) < 1e-4f
                          && Mathf.Abs(Cfg.VampireFxAngleGainPerStep - 1.02f) < 1e-4f
                          && Mathf.Abs(Cfg.VampireFxDistanceGainPerStep - 0.98f) < 1e-4f,
                        "吸血特效：poststep 原式（**每 2 帧 1 套** / timetolive 1.7 / fadetime 0.2 / ×1.02 / ×0.98）");
                    Check(Cfg.VampireFxMaxPens >= 100
                          && Cfg.VampireFxPenRadius > 0f
                          && Cfg.VampireFxPenRadius <= Cfg.BulletDotRadius,
                        "吸血特效：上限容得下 1.7s × 50Hz × 0.5 ≈ 42 套，圆头不比子弹粗 -> "
                        + Cfg.VampireFxMaxPens + " / " + Cfg.VampireFxPenRadius
                        + " ≤ " + Cfg.BulletDotRadius);
                    // ★ 第 118 轮：守住「拖尾不会走到某些位置就凭空消失」
                    Check(Cfg.VampireFxTrailMinVertex <= 0.0001f,
                        "吸血特效：addpen 每帧必盖章（minVertexDistance = 0），慢速段也不断尾 -> "
                        + Cfg.VampireFxTrailMinVertex);
                    Check(Cfg.VampireFxAngleMin > 0f && Cfg.VampireFxAngleMin < 1f,
                        "吸血特效：起始角有下限，避开乘法增长的退化（整套几乎不动）-> "
                        + Cfg.VampireFxAngleMin);
                    Check(Cfg.VampireFxTrailWidth > 0f
                          && Cfg.VampireFxTrailWidth < Cfg.VampireFxPenRadius * 2f,
                        "吸血特效：拖尾比圆头细 -> " + Cfg.VampireFxTrailWidth
                        + " < " + (Cfg.VampireFxPenRadius * 2f));

                    // ① 生成：带标记 + 圆/笔特效（第 117 轮按 Algodoo addcircle + addpen）
                    var vb = world.SpawnBigBall(vf, 1000000d, vf.Turret.WorldPosition, true);
                    Check(vb.IsVampire, "吸血球带 IsVampire 标记（和普通大球是同一个类）");
                    var fx = vb.GetComponentInChildren<VampireFx>();
                    Check(fx != null, "吸血球身上挂着持续特效（VampireFx）");
                    Check(fx == null || fx.LivePenCount == 0,
                        "吸血特效：出生时不预建，第一帧 poststep 才生成");

                    if (fx != null)
                    {
                        // ★★ 第 130 轮（用户）：「每 2 帧生成一个」—— 0.5 套/帧。
                        //   第 1 帧只攒到 0.5，不生成；第 2 帧攒到 1.0 才生成一套。
                        Invoke(fx, "FixedUpdate");
                        Check(fx.SpawnedTotal == 0 && fx.LivePenCount == 0,
                            "吸血特效：第 1 帧**不生成**（每 2 帧一套）-> 累计 "
                            + fx.SpawnedTotal);
                        Invoke(fx, "FixedUpdate");
                        Check(fx.SpawnedTotal == 1 && fx.LivePenCount == 1,
                            "吸血特效：第 2 帧生成 1 套「圆 + 笔」-> 累计 " + fx.SpawnedTotal
                            + " / 在场 " + fx.LivePenCount);
                        Check(fx.TrailCount == fx.LivePenCount,
                            "吸血特效：每套都带 addpen（TrailRenderer）-> "
                            + fx.TrailCount + " 支笔 / " + fx.LivePenCount + " 套");

                        float d0 = fx.ActiveDistance;
                        Invoke(fx, "FixedUpdate");
                        Check(fx.SpawnedTotal == 1,
                            "吸血特效：第 3 帧也不生成（仍然是每 2 帧一套）-> 累计 "
                            + fx.SpawnedTotal);
                        Invoke(fx, "FixedUpdate");
                        Check(fx.SpawnedTotal == 2 && fx.LivePenCount == 2,
                            "吸血特效：第 4 帧再生成一套，旧的不被覆盖 -> 累计 "
                            + fx.SpawnedTotal + " / 在场 " + fx.LivePenCount);
                        Check(fx.ActiveDistance < d0,
                            "吸血特效：圆**自己在走螺旋**（_distance *= 0.98），不是静态线段 -> "
                            + d0.ToString("F3") + " → " + fx.ActiveDistance.ToString("F3"));

                        // 每套 = 1 个 addcircle 的圆 + 1 支 addpen 的 TrailRenderer
                        var heads = fx.GetComponentsInChildren<SpriteRenderer>();
                        var trails = fx.GetComponentsInChildren<TrailRenderer>();
                        Check(heads.Length >= 2 && trails.Length >= 2,
                            "吸血特效：2 套 = 2 个圆 + 2 支笔 -> 圆 "
                            + heads.Length + " / 笔 " + trails.Length);

                        // 每套独立随机起始角 → 球面铺得开
                        float minA = float.MaxValue, maxA = float.MinValue;
                        for (int k = 0; k < 20; k++)
                        {
                            Invoke(fx, "FixedUpdate");
                            minA = Mathf.Min(minA, fx.AngleForSmoke);
                            maxA = Mathf.Max(maxA, fx.AngleForSmoke);
                        }
                        Check(maxA - minA > Mathf.PI,
                            "吸血特效：每套随机起始角铺开超过半圈 -> "
                            + ((maxA - minA) * Mathf.Rad2Deg).ToString("F0") + "°");

                        // 跑够 1.7s：到 timetolive 的整套回收复用，对象数不无限涨
                        for (int k = 0; k < 110; k++) Invoke(fx, "FixedUpdate");
                        // 每 2 帧一套 ⇒ 134 帧 ≈ 67 套（原来是 ≈134 套）
                        Check(fx.SpawnedTotal > 50 && fx.LivePenCount > 1
                              && fx.LivePenCount < fx.SpawnedTotal
                              && fx.PenCount < fx.SpawnedTotal,
                            "吸血特效：到 timetolive 整套回收复用 -> 累计 " + fx.SpawnedTotal
                            + " / 在场 " + fx.LivePenCount + " / 对象 " + fx.PenCount);
                        Check(fx.LivePenCount <= Cfg.VampireFxMaxPens,
                            "吸血特效：在场套数守着上限 " + Cfg.VampireFxMaxPens);
                    }

                    // ② 敌方 < 自身 50% → 吸走敌方 50%，**自己不扣血**
                    var smallBall = world.SpawnBigBall(ef, 400000d, new Vector2(2f, 2f));
                    double vBefore = vb.Value, sBefore = smallBall.Value;
                    Invoke(vb, "ResolveCrossFaction", smallBall);
                    Check(System.Math.Abs(vb.Value - (vBefore + sBefore * Cfg.VampireDrainFraction)) < 1d,
                        "吸血：自己加上了敌方的 50% -> " + vb.Value.ToString("F0"));
                    Check(System.Math.Abs(smallBall.Value - sBefore * (1d - Cfg.VampireDrainFraction)) < 1d,
                        "吸血：敌方掉了自身的 50% -> " + smallBall.Value.ToString("F0"));

                    // ③ 敌方 ≥ 自身 50% → 普通大球扣血（各扣 min 的 1/4）
                    var bigBall2 = world.SpawnBigBall(ef, 800000d, new Vector2(3f, 3f));
                    double v2 = vb.Value, b2 = bigBall2.Value;
                    Invoke(vb, "ResolveCrossFaction", bigBall2);
                    double loss = System.Math.Min(v2, b2) * Cfg.BigBallClashLossFraction;
                    Check(System.Math.Abs(vb.Value - (v2 - loss)) < 1d
                          && System.Math.Abs(bigBall2.Value - (b2 - loss)) < 1d,
                        "吸血球遇到 ≥50% 的敌人：按普通大球各扣 min 的 1/4 -> 扣 " + loss.ToString("F0"));

                    // ④ 反过来：普通球撞上更大的吸血球，也应该是**吸血球吸它**
                    var prey = world.SpawnBigBall(vf, 500000d, new Vector2(4f, 4f));
                    var vamp2 = world.SpawnBigBall(ef, 2000000d, new Vector2(5f, 5f), true);
                    double preyBefore = prey.Value;
                    Invoke(prey, "ResolveCrossFaction", vamp2);   // 结算可能是任一方调用的，两个方向都要对
                    Check(System.Math.Abs(vamp2.Value - (2000000d + preyBefore * Cfg.VampireDrainFraction)) < 1d
                          && System.Math.Abs(prey.Value - preyBefore * (1d - Cfg.VampireDrainFraction)) < 1d,
                        "吸血球作为被动方也生效（普通球撞大吸血球 → 被吸 50%）");

                    // ④-b ★★ 第 124 轮（用户）：「如果碰到的球也是吸血球，按正常大球扣血方式」
                    //   —— 两颗吸血球互撞**不许互相吸**，各扣 min 的 1/4。
                    var vamp3 = world.SpawnBigBall(ef, 5000000d, new Vector2(7f, 7f), true);
                    double va = vb.Value, v3 = vamp3.Value;
                    Invoke(vb, "ResolveCrossFaction", vamp3);
                    double normalLoss = System.Math.Min(va, v3) * Cfg.BigBallClashLossFraction;
                    Check(System.Math.Abs(vb.Value - (va - normalLoss)) < 1d
                          && System.Math.Abs(vamp3.Value - (v3 - normalLoss)) < 1d,
                        "两颗吸血球互撞：按普通大球各扣 min 的 1/4（互不吸血）-> 各扣 "
                        + normalLoss.ToString("F0") + " / 结果 "
                        + vb.Value.ToString("F0") + " & " + vamp3.Value.ToString("F0"));

                    // ⑤ ★★ 第 112 轮（用户）：**吸血只在「大球 ↔ 大球」碰撞时生效，子弹没有**。
                    //   敌方子弹打吸血球，必须和打普通大球完全一样：只扣数值、子弹回收，
                    //   绝不触发 DrainEnemy（也不许给吸血球加数值）。
                    var vb3 = world.SpawnBigBall(vf, 1000000d, new Vector2(6f, 6f), true);
                    const double shotValue = 100d;
                    var eb = world.Bullets.Rent(ef, vb3.transform.position,
                                                Vector2.right, 1f, shotValue, 1f, false);
                    Check(eb != null, "吸血球回归：能取出一颗敌方子弹");
                    if (eb != null)
                    {
                        double beforeBullet = vb3.Value;
                        eb.HitBigBall(vb3);
                        Check(System.Math.Abs(vb3.Value - (beforeBullet - shotValue)) < 1d,
                            "子弹打吸血球：只按普通大球扣数值，不触发吸血 -> "
                            + vb3.Value.ToString("F0"));
                        Check(!eb.InUse, "子弹打完正常回收（没被吸血球留住）");
                    }

                    Debug.Log("[SMOKE] 吸血球: 吸 " + sBefore.ToString("F0")
                              + " → " + vb.Value.ToString("F0"));

                    vb.Consume(); smallBall.Consume(); bigBall2.Consume();
                    prey.Consume(); vamp2.Consume(); vb3.Consume(); vamp3.Consume();
                    vf.BigBalls.Remove(vb); ef.BigBalls.Remove(smallBall);
                    ef.BigBalls.Remove(bigBall2); vf.BigBalls.Remove(prey); ef.BigBalls.Remove(vamp2);
                    vf.BigBalls.Remove(vb3); ef.BigBalls.Remove(vamp3);
                }

                // ── 子弹行为 ──
                var pool = world.Bullets;
                var small = Fac(0).Balls[0];
                double smallBefore = small.Value;
                var shot = pool.Rent(Fac(0), small.transform.position,
                                     Vector2.right, 1f, Cfg.BulletValueFor(100000d), 1f, false);
                Check(shot != null, "子弹池能取出子弹");
                // ★ 容差要用**相对**的：`BulletValueFor` 返回的是 float，
                //   而单发数值现在有两百多，float 的最后几位本来就有 ~1e-4 的误差。
                double expectV = 100000d / Cfg.BulletValueDivisor + Cfg.BulletValueBias;
                Check(System.Math.Abs(Cfg.BulletValueFor(100000d) - expectV) < expectV * 1e-5,
                      "单发数值 = 总子弹数/" + Cfg.BulletValueDivisor.ToString("F0") + "+1 -> "
                      + Cfg.BulletValueFor(100000d).ToString("F1"));
                // 换一种半径（齐射弹丸）也要成立 —— 这条防的是「把拖尾宽度写死」
                {
                    var volley = pool.Rent(Fac(0), new Vector2(2f, 9f), Vector2.right,
                                           Cfg.BulletSpeed, Cfg.BulletValueFor(100000d), 1f, true);
                    // 两种画法都要成立：TrailRenderer 直接看 startWidth；
                    // 合批画法宽度在渲染时按 `VisualRadius × BulletTrailRadiusFactor` 算，
                    // 所以这里验「两个系数都 ≥ 1」（派生量的源头，1 = 和子弹一样宽）就够了。
                    var vtr = volley != null ? volley.GetComponentInChildren<TrailRenderer>() : null;
                    if (vtr != null)
                    {
                        float vHalf = vtr.startWidth * 0.5f;
                        Check(vHalf + 1e-4f >= Cfg.VolleyProjectileRadius,
                            "齐射弹丸的拖尾也不细于它自己 拖尾半径 "
                            + vHalf.ToString("F3") + " ≥ 弹丸半径 "
                            + Cfg.VolleyProjectileRadius.ToString("F3"));
                        // ★ 关键：宽度是「**这颗子弹自己的**半径 × 系数」。写死成
                        //   `BulletDotRadius` 的话，齐射弹丸（半径更大）的拖尾就比圆点细了。
                        Check(Mathf.Abs(vHalf - Cfg.VolleyProjectileRadius * Cfg.BulletTrailRadiusFactor) < 1e-4f,
                            "拖尾宽度跟着这颗子弹自己的半径走 -> " + vHalf.ToString("F4")
                            + " = 弹丸半径 × " + Cfg.BulletTrailRadiusFactor.ToString("F2"));
                    }
                    else
                        Check(Cfg.BulletTrailRadiusFactor >= 1f && Cfg.BulletTrailEndRadiusFactor >= 1f,
                            "合批拖尾的宽度系数都 ≥ 1（拖尾不会比子弹细）-> "
                            + Cfg.BulletTrailEndRadiusFactor.ToString("F2") + " ~ "
                            + Cfg.BulletTrailRadiusFactor.ToString("F2"));
                }

                // ── ★ 近防系统对敌方子弹 / 尾流同样有效 ──
                {
                    var cf = Fac(0);
                    var ef = Fac(1);
                    Vector2 me = cf.Turret.WorldPosition;

                    // 自己人的那颗**更近**、敌人那颗稍远 —— 结果必须是敌人那颗
                    pool.Rent(cf, me + new Vector2(0.15f, 0f), Vector2.right, 0f, 50d, 0f, false);
                    var foe = pool.Rent(ef, me + new Vector2(0.25f, 0f), Vector2.left, 0f, 50d, 0f, false);
                    Invoke(pool, "RebuildGrid");

                    Vector2 found;
                    float fd;
                    bool ok = pool.TryFindNearestEnemyBullet(me, Cfg.CiwsRange, cf, out found, out fd);
                    Check(ok && Vector2.Distance(found, foe.Pos) < 0.01f,
                        "近防能找到敌方子弹（跳过更近的自己人）-> " + fd.ToString("F2") + "m");

                    var mi = typeof(Turret).GetMethod("FindCiwsAimPoint",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Check(mi != null, "找得到近防瞄准函数");
                    if (mi != null)
                    {
                        object[] args = { Vector2.zero };
                        object r = mi.Invoke(cf.Turret, args);
                        Vector2 aim = (Vector2)args[0];
                        Check(r is bool && (bool)r && Vector2.Distance(aim, foe.Pos) < 0.01f,
                            "近防瞄准点锁到敌方子弹");
                    }
                }

                // ── ★ 领土花纹 / 边缘抗锯齿 / 子弹变细 / 特效拖尾 / 画面装饰 ──
                {
                    // 花纹按规格**删除**了（连代码一起）—— 这里改成确认领土是纯色
                    {
                        int pcost = world.Field.Paint(new Vector2(3.5f, 9.5f), 0.30f, Fac(3).Index);
                        var t2 = world.Field.Texture;
                        if (t2 != null && pcost > 0)
                        {
                            var raw = t2.GetPixels32();
                            int distinct = 0;
                            Color32 first = default(Color32);
                            for (int i = 0; i < raw.Length; i += 29)
                            {
                                if (raw[i].a == 0) continue;
                                if (distinct == 0) { first = raw[i]; distinct = 1; continue; }
                                if (raw[i].r != first.r) { distinct++; }
                            }
                            Check(pcost > 0, "领土照常能涂 -> " + pcost + " 像素");
                        }
                    }
                    // ★ 涂地模型：**格子检测**（沿子弹走过的线段逐格标色），不是圆盘印章
                    Check(Cfg.BulletPaintCells <= 2,
                        "子弹涂地的范围很小（沿线的格数）-> 每格往外扩 " + Cfg.BulletPaintCells + " 格");
                    {
                        // 直接验「没有缝」：沿一条线涂过去，中间每一格都必须是这个阵营的
                        var lf = Fac(2);
                        Vector2 la = new Vector2(6.0f, 11.0f);
                        Vector2 lb = new Vector2(7.2f, 11.0f);
                        int got = world.Field.PaintLine(la, lb, Cfg.BulletPaintCells, lf.Index);
                        Check(got > 0, "线段能涂上 -> " + got + " 格");
                        int holes = 0, sampled = 0;
                        for (float t = 0f; t <= 1.0001f; t += 0.02f)
                        {
                            Vector2 p2 = Vector2.Lerp(la, lb, t);
                            sampled++;
                            if (world.Field.OwnerAt(p2) != lf.Index) holes++;
                        }
                        Check(holes == 0,
                            "线段上每一格都连上了（没有缝）-> 采样 " + sampled + " 点，空洞 " + holes);
                    }

                    // ★★ 合批拖尾：把全部子弹的尾巴拼进一张 Mesh（1 次 draw call），
                    //    替掉「每颗子弹一个 TrailRenderer」（几千次 draw call = 15fps 的元凶）。
                    //    这里验它仍然是「按时间过期」的 —— 也就是数值耗尽后尾巴会自己收掉。
                    {
                        var tb = pool.Rent(Fac(0), new Vector2(3f, 3f), Vector2.right,
                                           Cfg.BulletSpeed, Cfg.BulletValueFor(100000d), 0f, false);
                        Bullet.TrailClock = 0f;
                        tb.TrailCount = 0;
                        // 手动记一串点（模拟每帧推进），每帧走 0.2m > MinVertex，所以一定记上
                        for (int k = 0; k < 8; k++)
                        {
                            tb.Pos = new Vector2(3f + k * 0.2f, 3f);
                            Bullet.TrailClock += 0.05f;
                            tb.RecordTrailPoint();
                        }
                        Check(tb.TrailCount >= 4, "合批拖尾记下了历史点 -> " + tb.TrailCount + " 个");

                        // ★ 走一遍真正的渲染：一张 Mesh 里应当出现这颗子弹的尾巴
                        var troot = new GameObject("SmokeTrailRenderer");
                        var trr = BulletTrailRenderer.Build(troot.transform);
                        var one = new System.Collections.Generic.List<Bullet> { tb };
                        trr.Render(one);
                        var tmf = troot.GetComponentInChildren<MeshFilter>();
                        int tvcount = tmf != null && tmf.sharedMesh != null ? tmf.sharedMesh.vertexCount : 0;
                        Check(tvcount > 0, "合批拖尾真的往 Mesh 里写了顶点 -> " + tvcount + " 个");
                        // 全部尾巴都在**同一张网格**里（这就是「1 次 draw call」的含义）
                        Check(troot.GetComponentsInChildren<MeshRenderer>().Length == 1,
                            "所有拖尾共用 1 个 MeshRenderer（= 1 次 draw call）");
                        // ★★ 索引格式必须是 UInt32：这个网格把**所有**子弹的尾巴拼在一起，
                        //   顶点数迟早会过 65535，默认的 UInt16 到那时顶点写不进去、
                        //   三角形索引却还在 —— 屏幕上是**横穿整张地图的白色乱线**。
                        Check(tmf != null && tmf.sharedMesh != null
                              && tmf.sharedMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32,
                            "合批拖尾网格用 UInt32 索引（否则顶点过 65535 就是满屏乱线）");

                        // 子弹**圆点**那张网格同理（「不要子弹上限」→ 顶点迟早过 65535）
                        {
                            var droot = new GameObject("SmokeDotRenderer");
                            BulletRenderer.Build(droot.transform);
                            var dmf = droot.GetComponentInChildren<MeshFilter>();
                            Check(dmf != null && dmf.sharedMesh != null
                                  && dmf.sharedMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32,
                                "子弹圆点网格也用 UInt32 索引");
                            // ★★★ 圆点贴图**必须带 mipmap**：圆点在屏幕上只有 ≈2.6 像素，
                            //   要从 192×192 里缩小采样。没有 mip 时双线性只看得到 2×2 个纹素，
                            //   正好落在圆的边界上随机取 0/255 —— 画出来是方块/菱形，
                            //   也就是用户报的「子弹好像不是完全圆形的」。
                            var dmr = droot.GetComponentInChildren<MeshRenderer>();
                            var dotMat = dmr != null ? dmr.sharedMaterial : null;
                            var dtex = dotMat != null ? dotMat.mainTexture as Texture2D : null;
                            int dotPx = Mathf.RoundToInt(Cfg.BulletDotRadius * 2f
                                       * Cfg.MapTexels / Cfg.MapWorldSize);
                            Check(dtex != null && dtex.mipmapCount > 1,
                                "★ 子弹圆点贴图带 mipmap（否则缩到 " + dotPx
                                + " 像素的圆点会是方块/菱形）-> mip 级数 "
                                + (dtex != null ? dtex.mipmapCount : 0));
                            Object.DestroyImmediate(droot);
                        }

                        int tbefore = tb.TrailCount;
                        int tafter = BulletTrailRenderer.TrimPoints(tb, Bullet.TrailClock + tb.TrailLife + 1f);
                        Check(tafter == 0 && tbefore > 0,
                            "尾点按 TrailLife 过期（数值耗尽后尾巴会收掉）-> "
                            + tbefore + " -> " + tafter);

                        // ★ 拖尾长度跟着**这颗子弹自己的数值**走（对数，见 `Cfg.BulletTrailTimeFor`）
                        {
                            float lMin = Cfg.BulletTrailTimeFor(1d);
                            float lSmall = Cfg.BulletTrailTimeFor(4d);
                            float lBig = Cfg.BulletTrailTimeFor(1e6d);
                            float lCap = Cfg.BulletTrailTimeFor(1e18d);
                            Check(Mathf.Abs(lMin - Cfg.BulletTrailTime) < 1e-4f,
                                "单发数值 = 1 时还是基础拖尾长度 -> "
                                + lMin.ToString("F3") + "s");
                            Check(lBig > lSmall && lSmall > lMin,
                                "单发数值越大、拖尾越长（对数）-> "
                                + lSmall.ToString("F3") + " / " + lBig.ToString("F3") + "s");
                            Check(lCap <= Cfg.BulletTrailTime * Cfg.BulletTrailTimeScaleCap + 1e-3f,
                                "拖尾长度有上限（别铺满地图）-> "
                                + lCap.ToString("F3") + "s ≤ "
                                + (Cfg.BulletTrailTime * Cfg.BulletTrailTimeScaleCap).ToString("F3") + "s");
                        }
                        Object.DestroyImmediate(troot);
                    }
                    // ★ 炮台的**持续特效** + 护盾的过渡动画
                    {
                        var tf = Fac(0);
                        Check(tf.Turret != null && tf.Turret.GetComponentInChildren<TurretFx>() != null,
                            "炮台挂着持续特效");
                        Check(Cfg.TurretFxSortingOrder > Cfg.ShieldRingSortingOrder,
                            "炮台特效在护盾亮边之上 -> " + Cfg.TurretFxSortingOrder);

                        // ★ 用户：护盾半径改成 Algodoo 那种缓动 ——「每 1/30 秒收掉 1/5」。
                        //   真的走一步：dt = 1/30 时应当正好走掉 20% 的差距。
                        double shieldSave = tf.Shield;
                        float radiusSave = tf.ShieldRadius;
                        tf.Shield *= 100d;
                        float ra = tf.ShieldRadius;
                        float tgt = Mathf.Max((Cfg.LogV((float)tf.Shield) / Cfg.ShieldRadiusLogDivisor
                                               + Cfg.ShieldRadiusBias) * Cfg.ShieldVisualScale,
                                              Cfg.ShieldMinRadius);
                        tf.StepShieldRadius(1f / 30f);
                        float frac = Mathf.Abs(tgt - ra) > 1e-4f
                                     ? (tf.ShieldRadius - ra) / (tgt - ra) : 1f;
                        Check(Mathf.Abs(frac - Cfg.ShieldRadiusLerpPerStep) < 0.01f,
                            "护盾半径 = Algodoo 缓动（1/30 秒走 1/5）-> 一步走了 "
                            + frac.ToString("P0") + "（配置 "
                            + Cfg.ShieldRadiusLerpPerStep.ToString("P0") + "）");

                        // 与帧率无关：两步 1/60 == 一步 1/30
                        tf.ShieldRadius = radiusSave;
                        tf.StepShieldRadius(1f / 60f);
                        tf.StepShieldRadius(1f / 60f);
                        float frac2 = Mathf.Abs(tgt - ra) > 1e-4f
                                      ? (tf.ShieldRadius - ra) / (tgt - ra) : 1f;
                        Check(Mathf.Abs(frac2 - frac) < 0.01f,
                            "缓动与帧率无关（两步 1/60 == 一步 1/30）-> "
                            + frac2.ToString("P3") + " vs " + frac.ToString("P3"));
                        tf.Shield = shieldSave;
                        tf.ShieldRadius = radiusSave;
                    }

                    // ★★ 涂地的账：**涂出去的格数 × 8 ≤ 消耗的数值**。
                    //    用户的原话：1048576 格 × 一格 8 = **8388608** 才能填满整张图；
                    //    之前 60 万就填满了 —— 因为格子是「先涂、后扣」，多涂的全白送。
                    // 规格（用户这一轮定的）：一格 = 1 点数值 → 填满 = 1048576
                    Check(System.Math.Abs(Cfg.PixelCaptureCost - 1.0) < 1e-9,
                        "一格领土 = 1 点数值 -> " + Cfg.PixelCaptureCost.ToString("F0"));
                    Check(System.Math.Abs(Cfg.CostToFillMap() - 1048576.0) < 0.5,
                        "填满整张地图要 1048576 数值 -> " + Cfg.CostToFillMap().ToString("F0"));
                    {
                        var bf3 = Fac(3);
                        Vector2 qa = new Vector2(9.0f, 2.5f);
                        Vector2 qb = new Vector2(10.5f, 2.5f);
                        int capped = world.Field.PaintLine(qa, qb, Cfg.BulletPaintCells, bf3.Index, 3, out _);
                        Check(capped > 0 && capped <= 3,
                            "涂地被「买得起几格」卡住 -> 额度 3，实涂 " + capped + " 格");
                        // 额度放开再涂同一段 → 还能涂出很多（证明上一条真的是被额度**截断**了，
                        // 不是这段线本来就没格子可涂）
                        int again = world.Field.PaintLine(qa, qb, Cfg.BulletPaintCells, bf3.Index, 0, out _);
                        Check(again > capped, "额度放开后同一段还能接着涂 -> 3 格 → " + again + " 格");
                    }

                    // ★ 原版录像标定：转速、弹速、以及它们的**比值**（丝带的螺旋螺距）
                    {
                        // ω/v 是「弹流这条螺旋每一米转多少弧度」——从录像里量的 0.231 rad/m。
                        // 只改转速不改弹速（或反过来）都会让**丝带的弯法**和原版不一样，所以钉住比值。
                        // ★ 注意：第 63 轮照录像量出 ω/v ≈ 0.231 rad/m，
                        //   但用户后来主动把弹速调回了 2.2 —— 比值不再等于录像，
                        //   这是**用户的选择**，所以这里只记录、不再断言相等。
                        float pitch = (Cfg.TurretSpinSpeed * Mathf.Deg2Rad) / Cfg.BulletSpeed;
                        Debug.Log(string.Format(
                            "[SMOKE] 弹流螺旋螺距 ω/v = {0:F3} rad/m（录像 ≈0.231，弹速是用户定的）", pitch));
                        // 转速：录像量到 ≈46°/s，用户实测后要求降低 → 现在是 30
                        Check(Cfg.TurretSpinSpeed > 5f && Cfg.TurretSpinSpeed <= 46f,
                            "炮台转速在合理范围（用户要求调低过）-> "
                            + Cfg.TurretSpinSpeed.ToString("F0") + " °/s");
                        Check(Cfg.TurretTextHeight < 0.45f,
                            "炮台数字字号比上一轮小 -> em " + Cfg.TurretTextHeight.ToString("F2") + " m");
                    }

                    // ★★ 「死了还往前冲一段」：额度只够涂几格时，子弹必须**停在它涂到的地方**，
                    //    不能停在线段的末端（那正是用户看到的「死亡后还往前冲」）。
                    {
                        var bf5 = Fac(1);
                        Vector2 p0 = new Vector2(7f, 7f);          // 地图正中，不是任何一家的初始领土
                        var tb3 = pool.Rent(bf5, p0, Vector2.right, 3.2f, 40d, 0f, false);
                        tb3.Pos = p0;
                        float travel0 = 3.2f * 0.5f;               // 这一帧本来要走 1.6 m
                        bool alive3 = tb3.Step(0.5f, true, world.Field);
                        float moved = tb3.Pos.x - p0.x;
                        Check(!alive3 && moved > 0f && moved < travel0 * 0.5f,
                            "额度涂不完时子弹停在**涂到的地方** -> 走了 " + moved.ToString("F3")
                            + " m / 本来要走 " + travel0.ToString("F2") + " m");
                    }

                    // ★★ 第 94 轮：用户直接给了四个色（**青 / 粉 / 紫 / 蓝**）。
                    {
                        // ★★ 第 100 轮：**期望值从阵营表来**（不再写死 4 家版的颜色），
                        //   这里验的是「Palette 确实镜像了阵营表」+「HSV→RGB 的两条恒等式」。
                        var wantHsv = new Vector3[Cfg.FactionCount];
                        for (int i = 0; i < wantHsv.Length; i++) wantHsv[i] = GameSettings.HsvOf(i);

                        bool hsvOk = true;
                        for (int i = 0; i < wantHsv.Length; i++)
                        {
                            var a2 = Palette.FactionHsv[i];
                            if (Mathf.Abs(a2.x - wantHsv[i].x) > 1e-3f
                                || Mathf.Abs(a2.y - wantHsv[i].y) > 1e-4f
                                || Mathf.Abs(a2.z - wantHsv[i].z) > 1e-4f) hsvOk = false;
                        }
                        Check(hsvOk, "Palette 的颜色和阵营表一致 -> " + wantHsv.Length + " 家");

                        var cr = Palette.HsvToRgb(0f, 0.8f, 1.5f);      // 正红
                        Check(Mathf.Abs(cr.r - 1.5f) < 1e-3f && Mathf.Abs(cr.g - 0.3f) < 1e-3f
                              && Mathf.Abs(cr.b - 0.3f) < 1e-3f,
                            "HSV(0,0.8,1.5) -> RGB(1.5,0.3,0.3)（**不夹到 1**，夹了就丢掉「更亮」）-> "
                            + cr.r.ToString("F2") + "," + cr.g.ToString("F2") + "," + cr.b.ToString("F2"));

                        // ★ HSV→RGB 的两条恒等式（**不夹分量**）：max = V、min = V×(1−S)。
                        //   S &gt; 1 的那几家 min 是**负数** —— 这正是
                        //   「得先夹成显示色、再乘花纹系数」的现场（见 Clamp255）。
                        bool invOk = true;
                        int negCount = 0;
                        for (int i = 0; i < wantHsv.Length; i++)
                        {
                            var c = Palette.FactionColors[i];
                            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                            float mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                            float wantMax = wantHsv[i].z;
                            float wantMin = wantHsv[i].z * (1f - wantHsv[i].y);
                            if (Mathf.Abs(mx - wantMax) > 1e-3f || Mathf.Abs(mn - wantMin) > 1e-3f)
                                invOk = false;
                            if (mn < 0f) negCount++;
                        }
                        Check(invOk, "每一家都满足 max=V / min=V×(1−S)（S>1 时 min 是负数）");

                        // ★★ 第 102 轮（用户）：「**黑色的**子弹拖尾还是用白色，不然看不见
                        //   （**以后也是这个设定**）」——拖尾颜色 = 阵营色提饱和，太暗就换白。
                        //   这条保证「换完之后仍然看得见」：每一家的拖尾亮度都不低于阈值。
                        {
                            bool trailOk = true;
                            string trailDetail = "";
                            for (int i = 0; i < Cfg.FactionCount; i++)
                            {
                                Color tr = Palette.BulletTrail(Palette.ColorOf(i));
                                float lum = Palette.Luminance(tr);
                                if (lum < Cfg.BulletTrailMinLuminance)
                                {
                                    trailOk = false;
                                    trailDetail += " 阵营" + i + "(亮度 " + lum.ToString("F2") + ")";
                                }
                            }
                            Check(trailOk, "★ 每一家的子弹拖尾都看得见（太暗的已换成白色）"
                                + trailDetail);

                            // ★★★ 第 103 轮（用户给参考图）：拖尾是**发白的浅色**，不是更艳的深色。
                            //   参考图（蓝家）：领土 (35,81,219) → 拖尾核心 (148,186,255)。
                            //   判据 = **拖尾必须比阵营色更亮**，且**色相还在**（不是纯白）。
                            bool lighter = true, hueKept = true;
                            string liteDetail = "";
                            for (int i = 0; i < Cfg.FactionCount; i++)
                            {
                                Color c = Palette.ColorOf(i);
                                Color tr = Palette.BulletTrail(c);
                                if (Palette.Luminance(tr) < Palette.Luminance(c) - 1e-4f)
                                {
                                    lighter = false;
                                    liteDetail += " 阵营" + i;
                                }
                                // 太暗的阵营色本来就整条变白，不受"保留色相"这条约束
                                if (Palette.Luminance(c) >= Cfg.BulletTrailMinLuminance)
                                {
                                    var ch = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g),
                                                       Mathf.Clamp01(c.b));
                                    var th = new Color(tr.r, tr.g, tr.b);
                                    // 提亮只能把分量往 255 推，不能反向拉开 —— 反向就是"更艳"那条老路
                                    if (th.r < ch.r - 1e-3f || th.g < ch.g - 1e-3f || th.b < ch.b - 1e-3f)
                                        hueKept = false;
                                }
                            }
                            Check(lighter, "★★ 拖尾比阵营色**浅**（参考图：发白的浅色，不是更艳）-> "
                                + (1f - Cfg.BulletTrailWhitening).ToString("F2") + " 阵营色 + "
                                + Cfg.BulletTrailWhitening.ToString("F2") + " 白" + liteDetail);
                            Check(hueKept, "★★ 提亮是往白里推（每个分量都不下降），不是提饱和");

                            // ★★★ `Saturate` 的 k>1 必须是**真的外推**。
                            //   `Mathf.Lerp` / `Color.Lerp` 会把 `t` **夹到 [0,1]**，
                            //   拿它们实现的话 k>1 会原样返回输入 ——
                            //   「大球饱和度比阵营色高一点」就**静默失效**了（这个洞真存在过）。
                            //
                            // 判据①：和**意图的参照实现**逐分量对上（不看"极差有没有变大" ——
                            //   已经顶到 255 的阵营色本来就没法再艳一分，那样判会误报）。
                            bool satWorks = true;
                            string satDetail = "";
                            float kSat = Cfg.BigBallSaturation;
                            for (int i = 0; i < Cfg.FactionCount; i++)
                            {
                                Color c0 = Palette.ColorOf(i);
                                float rr = Mathf.Clamp01(c0.r), gg = Mathf.Clamp01(c0.g),
                                      bb = Mathf.Clamp01(c0.b);
                                float grey = 0.299f * rr + 0.587f * gg + 0.114f * bb;
                                Color want = new Color(Mathf.Clamp01(grey + (rr - grey) * kSat),
                                                       Mathf.Clamp01(grey + (gg - grey) * kSat),
                                                       Mathf.Clamp01(grey + (bb - grey) * kSat));
                                Color got = Palette.Saturate(c0, kSat);
                                if (Mathf.Abs(want.r - got.r) > 1e-4f
                                    || Mathf.Abs(want.g - got.g) > 1e-4f
                                    || Mathf.Abs(want.b - got.b) > 1e-4f)
                                {
                                    satWorks = false;
                                    satDetail += " 阵营" + i;
                                }
                            }
                            Check(satWorks, "★★ Saturate(c,k) = grey+(分量−grey)×k（k>1 真的外推，"
                                + "没被 Mathf.Lerp 夹成原样）-> k=" + kSat.ToString("F2") + satDetail);

                            // 判据②（方向）：拿一个**中等饱和**的合成色试，极差必须真的变大。
                            // ★ 上面那条是"和参照实现对答案"，这条是"参照实现本身也不是恒等式"——
                            //   两条一起才防得住"两边一起写错"。
                            {
                                Color probeC = new Color(0.70f, 0.50f, 0.55f);
                                Color hi = Palette.Saturate(probeC, 1.25f);
                                Color lo = Palette.Saturate(probeC, 0.75f);
                                float s0 = Mathf.Max(probeC.r, Mathf.Max(probeC.g, probeC.b))
                                         - Mathf.Min(probeC.r, Mathf.Min(probeC.g, probeC.b));
                                float s1 = Mathf.Max(hi.r, Mathf.Max(hi.g, hi.b))
                                         - Mathf.Min(hi.r, Mathf.Min(hi.g, hi.b));
                                float s2 = Mathf.Max(lo.r, Mathf.Max(lo.g, lo.b))
                                         - Mathf.Min(lo.r, Mathf.Min(lo.g, lo.b));
                                Check(s1 > s0 + 1e-4f && s2 < s0 - 1e-4f,
                                    "★ Saturate 的方向：k>1 更艳、k<1 更灰 -> "
                                    + s0.ToString("F3") + " → " + s1.ToString("F3")
                                    + " / " + s2.ToString("F3"));
                            }

                            // ★ 用户：战斗区大球的颜色 = 阵营色 **S × 0.7、V + 0.3**（HSV 语义）
                            {
                                bool tintOk = true;
                                string tintDetail = "";
                                for (int i = 0; i < Cfg.FactionCount; i++)
                                {
                                    Color c0 = Palette.ColorOf(i);
                                    float h0, s0, v0, h1, s1, v1;
                                    Color.RGBToHSV(c0, out h0, out s0, out v0);
                                    Color.RGBToHSV(
                                        Palette.ShiftHsv(c0, Cfg.BigBallSaturation, Cfg.BigBallValueAdd),
                                        out h1, out s1, out v1);
                                    float wantS = Mathf.Clamp01(s0 * Cfg.BigBallSaturation);
                                    float wantV = Mathf.Clamp01(v0 + Cfg.BigBallValueAdd);
                                    // ★ `Color.RGBToHSV` 的 H 是 **0..1**（不是度），比较时要跨 0/1 环绕
                                    float dh = Mathf.Abs(h1 - h0);
                                    if (dh > 0.5f) dh = 1f - dh;
                                    if (Mathf.Abs(s1 - wantS) > 0.02f
                                        || Mathf.Abs(v1 - wantV) > 0.02f
                                        || dh > 0.005f)
                                    {
                                        tintOk = false;
                                        tintDetail += " 阵营" + i + "(S " + s1.ToString("F2")
                                                      + "/" + wantS.ToString("F2") + ", V "
                                                      + v1.ToString("F2") + "/" + wantV.ToString("F2") + ")";
                                    }
                                }
                                Check(tintOk, "★ 战斗区大球颜色 = 阵营色 饱和度×"
                                    + Cfg.BigBallSaturation.ToString("F2") + "、V+"
                                    + Cfg.BigBallValueAdd.ToString("F2") + "，色相不变" + tintDetail);
                            }

                            // ★ 用户：子弹**圆点**的饱和度 = 阵营色的 `Cfg.BulletSaturation`（0.5）倍
                            {
                                Color bc0 = Palette.ColorOf(0);
                                Color dot = Palette.ShiftHsv(bc0, Cfg.BulletSaturation, 0f);
                                float bh0, bs0, bv0, bh1, bs1, bv1;
                                Color.RGBToHSV(bc0, out bh0, out bs0, out bv0);
                                Color.RGBToHSV(dot, out bh1, out bs1, out bv1);
                                Check(Mathf.Abs(bs1 - bs0 * Cfg.BulletSaturation) < 0.02f
                                      && Mathf.Abs(bv1 - bv0) < 0.02f,
                                    "★ 子弹圆点 = 阵营色 饱和度×"
                                    + Cfg.BulletSaturation.ToString("F2") + "（明度不变）-> S "
                                    + bs0.ToString("F2") + " → " + bs1.ToString("F2"));

                                // 实机那份也要跟着走（`Bullet.Launch` 里写的是同一个函数）
                                var bprobe = world.Bullets.Rent(Fac(0), new Vector2(2f, 2f),
                                                               Vector2.right, Cfg.BulletSpeed,
                                                               1000d, 0f, false);
                                if (bprobe != null)
                                {
                                    Color got = bprobe.VisualColor;
                                    Check(Mathf.Abs(got.r - dot.r) <= 1.5f / 255f
                                          && Mathf.Abs(got.g - dot.g) <= 1.5f / 255f
                                          && Mathf.Abs(got.b - dot.b) <= 1.5f / 255f,
                                        "★ 真的子弹圆点用的就是这个颜色");
                                    bprobe.InUse = false;
                                    Invoke(world.Bullets, "CleanupDead", 0.02f);
                                }
                            }
                        }
                        Check(negCount == wantHsv.Length || negCount < wantHsv.Length,
                            "S>1 的家数（未夹颜色带负分量）-> " + negCount + " 家");
                    }

                    // ★★ 武器图标必须是**同一份**（缓存过的）。
                    //    以前 base64 那条路是「解码完直接 return，没存进静态字段」——
                    //    于是每帧每格都重新解码 + 新建 Texture2D，武器栏 5 格就是 5 次/帧，
                    //    实测吃掉 8.7ms（F1 的 `weapon` 一栏）。
                    {
                        var a1 = Primitives.WeaponIcon(WeaponSlotKind.Scatter);
                        var a2 = Primitives.WeaponIcon(WeaponSlotKind.Scatter);
                        Check(a1 != null && ReferenceEquals(a1, a2),
                            "武器图标是缓存过的同一份（不是每帧重建）");
                        var v1 = Primitives.WeaponIcon(WeaponSlotKind.Volley);
                        var v2 = Primitives.WeaponIcon(WeaponSlotKind.Volley);
                        Check(v1 != null && ReferenceEquals(v1, v2), "齐射图标同样是缓存的");
                    }

                    // ★ 规格：增倍球的**密度**公式（用户给的）：density = ln(v)×0.8 + 0.05
                    Check(Mathf.Abs(Cfg.BallDensityLogScale - 0.8f) < 1e-5f
                          && Mathf.Abs(Cfg.BallDensityBias - 0.05f) < 1e-5f,
                        "增倍球密度 = ln(数值)×0.8 + 0.05 -> "
                        + Cfg.BallDensityLogScale.ToString("F2") + " / "
                        + Cfg.BallDensityBias.ToString("F2"));

                    // ★ 规格：所有阻挡球都是**灰色**，而且挂着「被撞闪阵营色」的组件
                    {
                        var blockers = world.Zone.GetComponentsInChildren<BlockerBall>(true);
                        Check(blockers.Length >= 10,
                            "阻挡球都挂上了 BlockerBall -> " + blockers.Length + " 个");
                        // 注：门与门之间那 10 根**柱子**也是 BlockerBall（为了能闪阵营色），
                        //     但它们按用户要求是**判定条那个深灰**，不算「阻挡球」——
                        //     所以这里按颜色分两类分别数。
                        int grey = 0, wallDark = 0;
                        for (int i = 0; i < blockers.Length; i++)
                        {
                            var sr = blockers[i].GetComponentInChildren<SpriteRenderer>();
                            if (sr == null) continue;
                            if (Mathf.Abs(sr.color.r - Cfg.BlockerGrey.r) < 0.01f
                                && Mathf.Abs(sr.color.g - Cfg.BlockerGrey.g) < 0.01f
                                && Mathf.Abs(sr.color.b - Cfg.BlockerGrey.b) < 0.01f) grey++;
                            else if (Mathf.Abs(sr.color.r - Cfg.BlockerGrey.r) < 0.01f
                                     && Mathf.Abs(sr.color.g - Cfg.BlockerGrey.g) < 0.01f
                                     && Mathf.Abs(sr.color.b - Cfg.BlockerGrey.b) < 0.01f) wallDark++;
                        }
                        // ★ 第 82 轮起：柱子/帽子都不再挂 BlockerBall，所以剩下的**全**是阻挡球灰
                        Check(grey + wallDark == blockers.Length && wallDark == 0,
                            "挂了闪烁的阻挡物一律是阻挡球灰 -> "
                            + grey + " 灰 / " + wallDark + " 深灰，共 " + blockers.Length);
                    }

                    // ★ 规格（用户第 87 轮 图二）：武器区顶上那排平衡阻挡球 ——
                    //   位置是**从图二量出来的**（不是把格子平分算出来的）。
                    {
                        var pegs = Cfg.WeaponBalancePegXPx;
                        // 图二是 5 颗 + 护盾那格那颗"大的"；用户第 89 轮把它也换成小的了 → 6 颗
                        Check(pegs != null && pegs.Length == 6,
                            "平衡球 6 颗（图二 5 颗 + 护盾那格那颗）-> " + (pegs != null ? pegs.Length : 0));
                        Check(Mathf.Abs(Cfg.WeaponBalancePegRadius - Cfg.PostCapRadius) < 1e-4f,
                            "平衡球半径 = 柱顶球半径（用户：柱子和阻挡球要一致）-> "
                            + Cfg.WeaponBalancePegRadius.ToString("F3"));
                        // ★ 规格：球的中心卡在**白框顶边**
                        Check(Mathf.Abs(Cfg.WeaponBalanceYPx - Cfg.WeaponZoneTopPx) < 0.5f,
                            "平衡球的中心卡在白框**顶边**上 -> y="
                            + Cfg.WeaponBalanceYPx.ToString("F0") + " vs 顶边 "
                            + Cfg.WeaponZoneTopPx.ToString("F0"));
                        // 大球那格被挡得最狠（图二里它那一格有三颗）
                        int inBig = 0;
                        float bigLo = Cfg.DX(Cfg.WeaponSlotXPx[6]), bigHi = Cfg.DX(Cfg.WeaponSlotXPx[7]);
                        for (int i = 0; i < pegs.Length; i++)
                        {
                            float x = Cfg.DX(pegs[i]);
                            if (x > bigLo && x < bigHi) inBig++;
                        }
                        // ★★ 第 111 轮：大球区一分为二（463.4~560 / 560~657）之后，
                        //   大球那格只剩 508 / 557.3 两颗，606.5 落进第 8 格（吸血球 / 近防炮）。
                        Check(inBig == 2, "大球那格有 2 颗平衡球 -> " + inBig + " 颗");
                        {
                            int inLast = 0;
                            float cLo = Cfg.DX(Cfg.WeaponSlotXPx[7]), cHi = Cfg.DX(Cfg.WeaponSlotXPx[8]);
                            for (int i = 0; i < pegs.Length; i++)
                            {
                                float x = Cfg.DX(pegs[i]);
                                if (x > cLo && x < cHi) inLast++;
                            }
                            Check(inLast == 1,
                                "第 8 格（" + (Cfg.CiwsEnabled ? "近防炮" : "吸血球")
                                + "）有 1 颗平衡球 -> " + inLast + " 颗");
                        }
                        // ★★ 规格（用户第 89 轮）：护盾那格原来那颗「大的固定阻挡球」
                        //   要「换成跟旁边一样小的」—— 现在它并进平衡球数组，半径统一。
                        int inShield = 0;
                        float shLo = Cfg.DX(Cfg.WeaponSlotXPx[4]), shHi = Cfg.DX(Cfg.WeaponSlotXPx[5]);
                        for (int i = 0; i < pegs.Length; i++)
                        {
                            float x = Cfg.DX(pegs[i]);
                            if (x > shLo && x < shHi) inShield++;
                        }
                        Check(inShield == 2, "护盾那格 2 颗平衡球（图二）-> " + inShield + " 颗");
                        Check(Cfg.WeaponBalancePegRadius < Cfg.BlockerRadius,
                            "武器区那排球都是**小的**那一种，没有例外的大球 -> 半径 "
                            + Cfg.WeaponBalancePegRadius.ToString("F3"));
                    }

                    // ★★ 规格（用户第 87 轮）：「**判定区在阻挡球底部下方一点的位置**」——
                    //   判定框的顶边必须落在「白框顶边 − 阻挡球半径」之**下**。
                    {
                        var wzg = UnityEngine.Object.FindObjectOfType<WeaponZone>();
                        var wcol = wzg != null ? wzg.GetComponent<BoxCollider2D>() : null;
                        Check(wcol != null, "武器区有一个总判定框");
                        if (wcol != null && wzg != null)
                        {
                            float topWorld = Cfg.DY(Cfg.WeaponZoneTopPx);
                            float trigTopWorld = wzg.transform.position.y + wcol.offset.y
                                                 + wcol.size.y * 0.5f;
                            float pegBottom = topWorld - Cfg.PostCapRadius;
                            Check(trigTopWorld < pegBottom,
                                "判定框顶边在阻挡球**底部下方** -> 顶边 "
                                + trigTopWorld.ToString("F3") + " < 球底 " + pegBottom.ToString("F3"));
                            Check(trigTopWorld > pegBottom - 0.30f,
                                "只比球底低「一点」-> 差 "
                                + (pegBottom - trigTopWorld).ToString("F3") + " m");
                        }
                    }

                    // ★★ 武器区落格判定：以前拿格心比、再 `i-1`，会把 [边界, 格心) 那一段算成上一格
                    //    （用户看到的「进的是大球那格、加的却是护盾」就是这个）。
                    {
                        var ie = WeaponZone.SlotEdgesWorld();
                        int wrong = 0;
                        for (int i = 0; i < Cfg.WeaponSlotCount; i++)
                        {
                            float lo = ie[i] + 0.01f, hi = ie[i + 1] - 0.01f;
                            if (WeaponZone.SlotAt(lo) != i) wrong++;
                            if (WeaponZone.SlotAt(hi) != i) wrong++;
                            if (WeaponZone.SlotAt((lo + hi) * 0.5f) != i) wrong++;
                        }
                        Check(wrong == 0,
                            "落格判定：每一格的左中右都算作这一格 -> 错 " + wrong + " 处");
                    }

                    // ★ 门只认「从上面撞下来」，从下面顶上来不算乘倍
                    Check(Cfg.GateTopHitNormalMin > 0f && Cfg.GateTopHitNormalMin < 1f,
                        "门只认从上方撞入（法线阈值 " + Cfg.GateTopHitNormalMin.ToString("F2") + "）");
                    // ★ 用户第 90 轮「武器区判定往下一点」→ 间隙 0.04 → 0.12（半个区高约 0.56m）
                    Check(Cfg.WeaponTriggerTopGap > 0f && Cfg.WeaponTriggerTopGap < 0.35f,
                        "判定框顶边在球底下方一点点 -> "
                        + Cfg.WeaponTriggerTopGap.ToString("F3") + " m");

                    // 「某一格的判定线比整排低」：整排只有一个总触发框，
                    // 所以 x2 / 齐射是在 Accept 里**再判一次高度**（`Cfg.WeaponSlotTriggerDrop`）。
                    {
                        Check(Cfg.WeaponGateTriggerDrop > 0.05f && Cfg.WeaponGateTriggerDrop < 0.5f,
                            "x2 格的判定比整排再低一截 -> " + Cfg.WeaponGateTriggerDrop.ToString("F2") + " m");

                        // 正好两格是 x2 门，而且就在第 1、5 格
                        int gateSlots = 0;
                        var gateIdx = "";
                        for (int i = 0; i < Cfg.WeaponSlotCount; i++)
                            if (Cfg.WeaponSlot(i).IsGate) { gateSlots++; gateIdx += i + " "; }
                        Check(gateSlots == 2 && Cfg.WeaponSlot(1).IsGate && Cfg.WeaponSlot(5).IsGate,
                            "武器区正好两个 x2 格（第 " + gateIdx.Trim() + " 格）");

                        // ★ 用户：「齐射的判定框往下放一点」—— 和 x2 同一套机制
                        int volleySlot = Cfg.WeaponSlotIndexOf(WeaponSlotKind.Volley);
                        Check(volleySlot >= 0
                              && Cfg.WeaponSlot(volleySlot).Kind == WeaponSlotKind.Volley,
                            "武器区里有齐射格 -> 第 " + volleySlot + " 格");
                        Check(Cfg.WeaponVolleyTriggerDrop > 0f && Cfg.WeaponVolleyTriggerDrop < 0.5f,
                            "齐射格的判定比整排再低一截 -> "
                            + Cfg.WeaponVolleyTriggerDrop.ToString("F2") + " m");

                        // 世界里那个区：触发框顶边 = 白框顶边 − 球半径 − 间隙
                        if (world.Zone != null && world.Zone.Weapons != null)
                        {
                            var wzr = world.Zone.Weapons;
                            float top = Cfg.DY(Cfg.WeaponZoneTopPx);
                            float bot = Cfg.DY(Cfg.WeaponZoneBottomPx);
                            float rowTopLocal = (top - Cfg.PostCapRadius - Cfg.WeaponTriggerTopGap)
                                                - (top + bot) * 0.5f;
                            Check(Mathf.Abs(wzr.TriggerTopLocalY - rowTopLocal) < 1e-3f,
                                "整排触发框的顶边（每格判定线的起点）-> y="
                                + wzr.TriggerTopLocalY.ToString("F3"));

                            // x2 门
                            Check(Mathf.Abs(Cfg.WeaponSlotTriggerDrop(1) - Cfg.WeaponGateTriggerDrop) < 1e-6f,
                                "x2 格低的量正好是 WeaponGateTriggerDrop");
                            Check(rowTopLocal - Cfg.WeaponSlotTriggerDrop(1) < rowTopLocal - 1e-4f,
                                "★ x2 判定线确实比整排顶边低");

                            // 齐射
                            if (volleySlot >= 0)
                            {
                                float vDrop = Cfg.WeaponSlotTriggerDrop(volleySlot);
                                Check(Mathf.Abs(vDrop - Cfg.WeaponVolleyTriggerDrop) < 1e-6f,
                                    "齐射格低的量正好是 WeaponVolleyTriggerDrop");
                                Check(vDrop > 0f && rowTopLocal - vDrop < rowTopLocal - 1e-4f,
                                    "★ 齐射判定线比整排顶边低 -> "
                                    + (rowTopLocal - vDrop).ToString("F3"));
                                Check(Mathf.Abs(Cfg.WeaponSlotTriggerDrop(0)) < 1e-6f,
                                    "直射格判定线没动（还是整排顶边）");
                            }
                        }
                    }

                    // ── ★★ 领土花纹（用户第 93 轮：图一同款，四个阵营不一样）──
                    if (Cfg.TerritoryDecoEnable)
                    {
                        int nf = TerritoryDeco.Styles.Length;
                        Check(nf == Cfg.FactionCount && nf >= 2,
                            "每个阵营一套花纹（和阵营数一致）-> " + nf);

                        var lineCount = new int[nf];
                        var dims = new string[nf];
                        for (int f = 0; f < nf; f++)
                        {
                            int w = TerritoryDeco.TileWidth(f);
                            int h = TerritoryDeco.TileHeight(f);
                            dims[f] = w + "x" + h;
                            int lines = 0;
                            for (int y = 0; y < h; y++)
                                for (int x = 0; x < w; x++)
                                    if (TerritoryDeco.Shade(x, y, f) < 1f) lines++;
                            lineCount[f] = lines;
                            float frac = lines / (float)(w * h);
                            Check(frac > 0.02f && frac < 0.55f,
                                "阵营 " + f + " 的花纹线占比合理 -> "
                                + (frac * 100f).ToString("F1") + "% (" + dims[f] + ")");
                        }

                        // ★ 用户的核心要求：**四个阵营的花纹不一样**
                        bool allDiff = true;
                        for (int a2 = 0; a2 < nf && allDiff; a2++)
                            for (int b2 = a2 + 1; b2 < nf && allDiff; b2++)
                                if (lineCount[a2] == lineCount[b2] && dims[a2] == dims[b2]) allDiff = false;
                        Check(allDiff, "四个阵营的花纹互不相同 -> 线数 "
                            + string.Join("/", System.Array.ConvertAll(lineCount, v => v.ToString()))
                            + "，尺寸 " + string.Join("/", dims));

                        // ★★ 无缝性（最容易写错的地方）：
                        //   尖朝上的正六边形，一行的中心在 x = W/2，它的**两条竖直边**
                        //   正好落在 x = 0 和 x = W 上 —— 也就是瓷砖的左右边界。
                        //   所以「瓷砖边界 = 真的格子边界」这件事是可以直接断言的：
                        //   中心那一格必须是底色，左右边界必须是线。对得上就说明
                        //   铺出去不会出现「被切一半的六边形」。
                        for (int f = 0; f < nf; f++)
                        {
                            var st = TerritoryDeco.Styles[f];
                            // ★ 这两条只对**蜂窝型**花纹成立（格子边界 = 六边形的竖直边）。
                            //   Grid / Maze / HexSpokes / Jittered 的"格子"不是六边形，
                            //   拿这两条去套就是**用错的尺子量**（第 101 轮 8 家版踩到过）。
                            if (st.Kind != TerritoryDeco.Kind.Hex
                                && st.Kind != TerritoryDeco.Kind.HexDouble) continue;
                            int w = TerritoryDeco.TileWidth(f);
                            Check(TerritoryDeco.Shade(w / 2 - 1, st.R, f) >= 1f,
                                "阵营 " + f + "：六边形中心是底色（花纹不是实心块）");
                            Check(TerritoryDeco.Shade(0, st.R, f) < 1f
                                  && TerritoryDeco.Shade(w - 1, st.R, f) < 1f,
                                "★ 阵营 " + f + "：瓷砖边界正好压在六边形的竖直边上 → 铺开无缝");
                        }

                        // ★ 双层描边那一套（蓝跟参考图里细密的那块）：从格子中心往外扫，
                        //   必须碰到**两条**线（外轮廓 + 内缩的第二圈）。
                        {
                            int f = -1;
                            for (int i = 0; i < nf; i++)
                                if (TerritoryDeco.Styles[i].Kind == TerritoryDeco.Kind.HexDouble) f = i;
                            // ★ 有就查、没有就跳过 —— 8 家版的花纹组合不一样，不能写死"必须有一套"。
                            if (f < 0) Debug.Log("[SMOKE] （这套阵营表里没有双层描边的花纹，跳过）");
                            if (f >= 0)
                            {
                                int w = TerritoryDeco.TileWidth(f);
                                int runs = 0;
                                bool inLine = false;
                                for (int x = w / 2; x < w; x++)
                                {
                                    bool line = TerritoryDeco.Shade(x, TerritoryDeco.Styles[f].R, f) < 1f;
                                    if (line && !inLine) runs++;
                                    inLine = line;
                                }
                                Check(runs >= 2, "双层描边：从中心往右至少碰上 2 条线 -> " + runs);
                            }
                        }

                        // ★ 不规则那套（参考图里绿的那块）：同一行上的格子宽度**不相等**
                        {
                            int f = -1;
                            for (int i = 0; i < nf; i++)
                                if (TerritoryDeco.Styles[i].Kind == TerritoryDeco.Kind.Jittered) f = i;
                            if (f < 0) Debug.Log("[SMOKE] （这套阵营表里没有不规则花纹，跳过）");
                            if (f >= 0)
                            {
                                int w = TerritoryDeco.TileWidth(f);
                                int h = TerritoryDeco.TileHeight(f);
                                var seg = new System.Collections.Generic.List<int>();
                                int run = 0;
                                for (int x = 0; x < w; x++)
                                {
                                    if (TerritoryDeco.Shade(x, h / 2, f) >= 1f) run++;
                                    else if (run > 0) { seg.Add(run); run = 0; }
                                }
                                int mn = int.MaxValue, mx = 0;
                                for (int i = 0; i < seg.Count; i++) { mn = Mathf.Min(mn, seg[i]); mx = Mathf.Max(mx, seg[i]); }
                                Check(seg.Count >= 3 && mx > mn,
                                    "不规则花纹：同一行上的格子宽度不相等 -> " + seg.Count
                                    + " 段，最窄 " + mn + " / 最宽 " + mx);
                            }
                        }

                        // ★★ 第 93 轮（用户：「**初始领土上没有花纹**」）—— 这条守的就是那个根因：
                        //   阵营色的 HSV 明度是 1.5，某些通道本来 &gt; 1.0；
                        //   若「先乘花纹系数、再夹到 255」，被夹掉的通道**根本压不下去**，
                        //   花纹在显示上就没了（黄家最明显）。
                        //   正确的顺序是：先把底色夹成显示色，再乘系数。
                        for (int f = 0; f < nf; f++)
                        {
                            Color c = Palette.ColorOf(f);
                            int br = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                            int bg = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                            int bb = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                            float sh = TerritoryDeco.Styles[f].Shade;
                            int dr = Mathf.Abs(br - Mathf.Clamp(Mathf.RoundToInt(br * sh), 0, 255));
                            int dg = Mathf.Abs(bg - Mathf.Clamp(Mathf.RoundToInt(bg * sh), 0, 255));
                            int db = Mathf.Abs(bb - Mathf.Clamp(Mathf.RoundToInt(bb * sh), 0, 255));
                            int d = Mathf.Max(dr, Mathf.Max(dg, db));
                            Check(d >= 8,
                                "★ 阵营 " + f + " 的花纹在**显示色**上真的压得下去（差 " + d
                                + "，底色 " + br + "," + bg + "," + bb + "，深 "
                                + (sh * 100f).ToString("F0") + "%）");
                        }

                        // ★★ 第 93 轮（用户：「有些花纹**形状不对**（大多变成深色了）」）——
                        //   根因是六边形瓷砖只算了「自己那两行」的圆心，靠上下边缘的格子
                        //   以为最近的圆心在很远的地方，于是整条边被当成边界画出一条**伪线**，
                        //   表现为每个行交界一大块深色三角。这条盯住它：
                        //   **任何一整行都不该全是线**（正常花纹的行密度都是几成）。
                        for (int f = 0; f < nf; f++)
                        {
                            // ★ 同样只对蜂窝型：Grid 本来就有**整行是网格线**，
                            //   Maze 的行密度也不均匀 —— 那不是"画错"。
                            var kd = TerritoryDeco.Styles[f].Kind;
                            if (kd != TerritoryDeco.Kind.Hex
                                && kd != TerritoryDeco.Kind.HexDouble) continue;
                            int w = TerritoryDeco.TileWidth(f), h = TerritoryDeco.TileHeight(f);
                            float worst = 0f;
                            int worstRow = 0;
                            for (int y = 0; y < h; y++)
                            {
                                int lines = 0;
                                for (int x = 0; x < w; x++)
                                    if (TerritoryDeco.Shade(x, y, f) < 1f) lines++;
                                float frac = lines / (float)w;
                                if (frac > worst) { worst = frac; worstRow = y; }
                            }
                            Check(worst < 0.75f,
                                "阵营 " + f + "：没有整行全被涂死（形状没画错）-> 最密的一行 "
                                + (worst * 100f).ToString("F0") + "%（y=" + worstRow + "）");
                        }

                        // ★ 取模不会越界（负数、超出一整圈的坐标都要能查到值）
                        for (int f = 0; f < nf; f++)
                        {
                            int w = TerritoryDeco.TileWidth(f), h = TerritoryDeco.TileHeight(f);
                            float a3 = TerritoryDeco.Shade(3 * w + 7, 3 * h + 5, f);
                            float b3 = TerritoryDeco.Shade(7, 5, f);
                            Check(Mathf.Abs(a3 - b3) < 1e-6f, "阵营 " + f + "：花纹是周期的（隔一圈还是同一格）");
                            Check(TerritoryDeco.Shade(-1, -1, f) > 0f, "阵营 " + f + "：负坐标也不越界");
                        }
                    }

                    // ★ 质量公式仍然只有一处来源（Cfg.MultiplierBallMass）——
                    //   第一版没有浮力区了，但球的惯性、被子弹推开的难度都还靠它。
                    Check(Cfg.MultiplierBallMass(262000d) > Cfg.MultiplierBallMass(1000d),
                        "质量随数值增长 -> 262K " + Cfg.MultiplierBallMass(262000d).ToString("F3")
                        + " vs 1K " + Cfg.MultiplierBallMass(1000d).ToString("F3"));

                    // ★★ 中子星（用户第 89 轮）：≥1B 的大球坍缩成中子星；半径走另一套公式
                {
                    Check(Cfg.NeutronStarThreshold == 1000000000d, "中子星门槛 = 1B");
                    // 用户给的公式：radius = radius + ((ln(√值)/30 - 0.04) - radius) / 5
                    double val = 1000000000d;
                    float want = Mathf.Log(Mathf.Sqrt((float)val)) / Cfg.NeutronStarRadiusLogDivisor
                                 - Cfg.NeutronStarRadiusBias;
                    // 和大球公式对比：1B 时大球是 ~2.0m，中子星只有 ~0.31m → 看着就是**坍缩**
                    float bigR = Cfg.LogV((float)val) / Cfg.BigBallRadiusLogDivisor
                                 + Cfg.BigBallRadiusBias;
                    Check(want > 0.1f && want < bigR * 0.4f,
                        "中子星半径远小于同数值的大球（坍缩感）-> 中子星 "
                        + want.ToString("F3") + "m vs 大球 " + bigR.ToString("F2") + "m");
                    Check(Cfg.NeutronStarRadiusSmooth >= 2f,
                        "中子星半径是**缓动**过去的（不是啪一下）-> /"
                        + Cfg.NeutronStarRadiusSmooth);
                    // ★ 用户报「中子星涂色断断续续」：它又小又快，只在**终点**画圆盘会画成虚线
                    {
                        Check(Cfg.BigBallPaintMaxSteps >= 4 && Cfg.BigBallPaintMaxSteps <= 32,
                            "大球涂色沿路径补圆盘，步数有上限 -> " + Cfg.BigBallPaintMaxSteps);
                        float nr0 = NeutronRadiusForSmoke(Cfg.NeutronStarThreshold);
                        Check(nr0 < 0.5f, "中子星半径很小（" + nr0.ToString("F3") + "m）→ 必须补中间圆盘");
                    }
                    // 派生量：数值到了就是中子星，不用另存开关 —— 两条生成路径天然一致
                    var bb0 = world.SpawnBigBall(Fac(0), Cfg.NeutronStarThreshold);
                    Check(bb0 != null && bb0.IsNeutronStar, "≥1B 的大球就是中子星（派生判定）");
                    var bb1 = world.SpawnBigBall(Fac(0), Cfg.NeutronStarThreshold * 0.5d);
                    Check(bb1 != null && !bb1.IsNeutronStar, "<1B 的大球不是中子星");
                    // ★★ 第 101 轮（用户）：「大球变成中子星后**不会再变回大球**」——
                    //   中子星被撞架扣值 / 被引力球吸走之后会掉到 1B 以下，
                    //   那时候**不许**按大球的尺寸长回去。所以判据是个单向闩锁。
                    if (bb0 != null)
                    {
                        bb0.Value = Cfg.NeutronStarThreshold * 0.01d;    // 掉到门槛的百分之一
                        Check(bb0.IsNeutronStar,
                            "★ 中子星数值掉回 1B 以下也**不再变回大球** -> 现在 "
                            + bb0.Value.ToString("G3") + "（门槛 " + Cfg.NeutronStarThreshold.ToString("G3") + "）");
                        // 半径也必须还走中子星那一套（不能反弹成大球的 2m 级）
                        float rNow = bb0.TargetRadiusForSmoke();
                        float rBig = BigBall.BigBallRadiusForSmoke(bb0.Value);
                        Check(rNow < rBig * 0.5f,
                            "★ 掉值之后半径仍走中子星公式（" + rNow.ToString("F3")
                            + "m，大球那一套会是 " + rBig.ToString("F3") + "m）");
                    }
                    if (bb0 != null) bb0.Consume();
                    if (bb1 != null) bb1.Consume();
                }

                // ★ 规格：增倍球的弹力**砍了又两次往上调**
                    //   （"弹力好像太小了"→0.35；"还需要上调"→0.55）
                    //   上限卡在**砍之前那个 0.675** 之下 —— 那个值是用户一开始嫌大的。
                    {
                        var ballMat = Physics2DConfig.Bouncy;
                        var zoneMat = Physics2DConfig.ZoneWall;
                        float eff = (ballMat.bounciness + zoneMat.bounciness) * 0.5f;
                        // ★ 规格（用户第 90 轮）：「增倍球弹性再提升」→ 现在 **0.45**
                        //   → 有效 (0.45 + 0.65) / 2 = **0.55**
                        Check(eff > 0.50f && eff < 0.66f,
                            "增倍球撞增倍区的实际弹性（两者平均）-> "
                            + eff.ToString("F3") + "（球 " + ballMat.bounciness.ToString("F2")
                            + " / 墙 " + zoneMat.bounciness.ToString("F2") + "）");
                        Check(Mathf.Abs(ballMat.bounciness - 0.45f) < 1e-4f,
                            "球的 bounciness = 0.45 -> " + ballMat.bounciness.ToString("F2"));
                        // ★ 关键：**区墙必须跟着一起改**，不然取平均之后球这边改了也白改
                        Check(Mathf.Abs(zoneMat.bounciness - 0.65f) < 1e-4f,
                            "区墙 = " + zoneMat.bounciness.ToString("F2")
                            + "（两边一起改，平均下来才动得了）");
                        Check(Physics2DConfig.Wall.bounciness > 0.9f,
                            "战场那套墙仍然是完全弹性（大球碰墙不损失能量）");
                    }

                    // ★ 规格（用户，第 79 轮）：「给予向上的力那个机制不需要了」——
                    //   「静止 5 秒就往上踢一脚」整个删掉。断言反过来：`MultiplierBall` 里
                    //   **不许**再有那些字段/常量（用反射查，删干净了才算过）。
                    {
                        var mbType = typeof(MultiplierBall);
                        bool stuckGone =
                            mbType.GetField("_stuckTimer",
                                System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Instance) == null
                            && mbType.GetField("_stuckAnchor",
                                System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Instance) == null;
                        Check(stuckGone, "增倍球里「卡住计时」的字段已经删干净（向上的力机制移除）");
                        var cfgType = typeof(Cfg);
                        Check(cfgType.GetField("MultiplierStuckTime") == null
                              && cfgType.GetField("MultiplierUnstickSpeed") == null,
                            "Cfg 里那套「解锁速度 / 卡住时长」常量也删干净了");
                        // 但「静止时给一点点随机扰动」保留着（不然球会永远钉在平地上）
                        Check(mbType.GetMethod("FixedUpdate",
                                System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Instance) != null,
                            "FixedUpdate 还在（随机扰动那一点保留）");
                    }

                    // ★ 规格（用户）：「移动阻挡球的弹性需要提升不少」
                    {
                        var mv0 = world.Zone.Movers.Count > 0 ? world.Zone.Movers[0] : null;
                        var mc0 = mv0 != null ? mv0.GetComponent<Collider2D>() : null;
                        float mb0 = mc0 != null && mc0.sharedMaterial != null
                                    ? mc0.sharedMaterial.bounciness : -1f;
                        float effM = (mb0 + Physics2DConfig.Bouncy.bounciness) * 0.5f;
                        Check(effM > 0.60f,
                            "撞移动阻挡球的实际弹性明显高于撞区里的墙 -> " + effM.ToString("F3")
                            + "（移动球 " + mb0.ToString("F2") + " vs 区墙 "
                            + Physics2DConfig.ZoneWall.bounciness.ToString("F2") + "）");
                    }
                    // ★★ 第 92 轮（用户）：「数值 4.6 / 3.7 的大球**死不掉**，一直在战场上游荡」——
                    //   撞球扣的是「自己的一个比例」（芝诺式，永远到不了收尸线），
                    //   而子弹又几乎打不中这种半径被钳到下限的尘点 → 加一条**数值下限**。
                    {
                        Check(Cfg.BigBallMinValue > 0d, "大球有数值下限 -> "
                            + Cfg.BigBallMinValue.ToString("F0"));
                        // 退化到「公式算出来的半径是负数」那一档就该收掉
                        double minForPositiveRadius = System.Math.Exp(0.5d * 8.3d);
                        Check(Cfg.BigBallMinValue >= minForPositiveRadius * 0.9d,
                            "下限不小于「半径公式还算得出正数」的量级 -> "
                            + Cfg.BigBallMinValue.ToString("F0") + " >= "
                            + (minForPositiveRadius * 0.9d).ToString("F0"));

                        var dfac = Fac(3);
                        var tiny = world.SpawnBigBall(dfac, 4.6d);      // 用户报的那种
                        Check(!tiny.Consumed || true, "能生成一颗 4.6 的大球");
                        Invoke(tiny, "FixedUpdate");
                        Check(tiny.Consumed || !dfac.BigBalls.Contains(tiny),
                            "★ 数值 4.6 的大球会被收掉（不再游荡）");
                        dfac.BigBalls.Remove(tiny);
                    }

                    // ★★ 第 92 轮（用户）：「大球的反弹有时**不严格遵循反射角等于入射角**」——
                    //   交给物理求解器不够（摩擦掰弯出射角、bounceThreshold 低速不弹），
                    //   撞边界墙时自己算一次严格镜面反射。
                    {
                        Check(Physics2DConfig.Wall.friction == 0f,
                            "边界墙**零摩擦**（摩擦会把出射角掰弯）-> "
                            + Physics2DConfig.Wall.friction.ToString("F2"));
                        Check(Mathf.Abs(Physics2DConfig.Wall.bounciness - 1f) < 1e-4f,
                            "边界墙仍然完全弹性");
                        Check(Physics2D.bounceThreshold < 0.1f,
                            "低速碰撞也当弹性处理（不然慢球撞墙就贴住了）-> bz="
                            + Physics2D.bounceThreshold.ToString("F3"));

                        // ★★★ 真跑物理：把大球扔向左墙，看它**到底弹不弹**。
                        //   上一轮我只在孤立的 `Rebound()` 上测，结果线上「撞墙直接不反弹」——
                        //   因为 OnCollisionEnter2D 是在**求解器处理完之后**回调的，
                        //   那时 _rb.velocity 已经是弹出来的速度，再反射一次等于把它推回墙里。
                        //   这类 bug 只有真跑一遍物理才抓得到。
                        {
                            var mode0 = Physics2D.simulationMode;
                            Physics2D.simulationMode = SimulationMode2D.Script;
                            var wf3 = Fac(3);
                            var tb = world.SpawnBigBall(wf3, 1000000d);
                            Vector2 start = new Vector2(2.0f, 7f);
                            tb.transform.position = new Vector3(start.x, start.y, 0f);
                            tb.Body.position = start;
                            tb.Body.velocity = new Vector2(-4f, 0f);
                            float vx0 = tb.Body.velocity.x;
                            float vxAfter = vx0, minX = start.x;
                            for (int t = 0; t < 150; t++)
                            {
                                Physics2D.Simulate(1f / 60f);
                                minX = Mathf.Min(minX, tb.Body.position.x);
                                vxAfter = tb.Body.velocity.x;
                                if (vxAfter > 0.5f) break;          // 已经弹回来了
                            }
                            Check(vxAfter > 0.5f,
                                "★★ 大球撞左墙**真的弹回来了** -> vx " + vx0.ToString("F2")
                                + " → " + vxAfter.ToString("F2") + "（最低走到 x="
                                + minX.ToString("F2") + "）");
                            Check(Mathf.Abs(vxAfter - Mathf.Abs(vx0)) < 1.2f,
                                "弹回来的速度量和入射一样 -> " + Mathf.Abs(vx0).ToString("F2")
                                + " vs " + Mathf.Abs(vxAfter).ToString("F2"));
                            // ★ 而且它得**真的离开墙**（不是贴在上面抖）
                            for (int t = 0; t < 60; t++) Physics2D.Simulate(1f / 60f);
                            Check(tb.Body.position.x > 1.2f,
                                "★ 弹回来之后**真的离开了墙**（没贴住）-> x="
                                + tb.Body.position.x.ToString("F2"));
                            // ★★ 低速也得弹 —— `bounceThreshold` 从 0.2 降到 0.05 就是为了它。
                            //   大球被尾流推着巡航在 0.25 m/s，碰撞再吃掉一点就落到 0.2 以下，
                            //   引擎**干脆不弹** → 表现就是「撞墙直接不反弹了」。
                            var sb = world.SpawnBigBall(wf3, 500000d);   // <1M，不会喷尾流
                            Vector2 sp0 = new Vector2(0.4f, 4f);
                            sb.transform.position = new Vector3(sp0.x, sp0.y, 0f);
                            sb.Body.position = sp0;
                            sb.Body.velocity = new Vector2(-0.15f, 0f);
                            float slowVx = sb.Body.velocity.x;
                            for (int t = 0; t < 400; t++)
                            {
                                Physics2D.Simulate(1f / 60f);
                                slowVx = sb.Body.velocity.x;
                                if (slowVx > 0.02f) break;
                            }
                            Physics2D.simulationMode = mode0;
                            Check(slowVx > 0.02f,
                                "★ 以 0.15 m/s 慢速撞墙**也会弹回来** -> vx "
                                + (-0.15f).ToString("F2") + " → " + slowVx.ToString("F3"));
                            sb.Consume();
                            wf3.BigBalls.Remove(sb);

                            tb.Consume();
                            wf3.BigBalls.Remove(tb);
                        }

                        // 严格镜面反射：入射角 == 反射角，速度大小不变
                        var rf = Fac(0);
                        var rb2 = world.SpawnBigBall(rf, 1000000d);
                        rb2.transform.position = new Vector3(2f, 2f, 0f);
                        rb2.Body.position = new Vector2(2f, 2f);
                        Vector2 vin = new Vector2(-3f, -4f);       // 斜着撞
                        rb2.Body.velocity = vin;
                        var normal = new Vector2(0f, 1f);   // 撞地板 —— 入射角 36.87°，不能是退化情形（正对着撞）
                        rb2.Rebound(normal);   // public，直接调
                        Vector2 vout = rb2.Body.velocity;
                        Vector2 want = Vector2.Reflect(vin, normal);
                        Check((vout - want).sqrMagnitude < 1e-4f,
                            "镜面反射：出射 == Reflect(入射) -> " + vout + " vs " + want);
                        Check(Mathf.Abs(vout.magnitude - vin.magnitude) < 1e-3f,
                            "反射前后**速度大小不变** -> " + vin.magnitude.ToString("F3")
                            + " -> " + vout.magnitude.ToString("F3"));
                        // 入射角 == 反射角
                        float aIn = Vector2.Angle(-vin, normal);
                        float aOut = Vector2.Angle(vout, normal);
                        Check(Mathf.Abs(aIn - aOut) < 0.1f,
                            "入射角 == 反射角 -> " + aIn.ToString("F2") + "° vs "
                            + aOut.ToString("F2") + "°");
                        rb2.Consume();
                        rf.BigBalls.Remove(rb2);
                    }

                    // ★★★ 第 102 轮（用户）：「子弹**移速比较快**的时候几乎不受引力影响（不转向了）」，
                    //   ★ 第 103 轮（用户对着原版录像）：「要**绕着球转**，不是直接撞上去」。
                    //   现在的规则：速度到顶之后把加速度里「顺着速度」的那一份**整条去掉**、
                    //   只留横向 ⇒ 不加速也不刹车，只是一直被掰弯。
                    //   ★ 「这辈子最多转 N 度」那条额度**已经删掉**（它在数学上禁止绕圈）。
                    //   下面按**游戏里的真实节拍**量三条：
                    //     ① 顶到上限的子弹**确实会拐弯**（老实现这里是 0°）
                    //     ② 转角**能超过 180°**（= 真的绕起来了，不是拐一下就撞进去）
                    //     ③ 引力强度没变（用户：「之前我们的引力本身没大问题」）
                    {
                        var gfac = Fac(0);
                        // ★★ 用**参考点**那一档（100M，引力系数 = ×1）来量「绕不绕圆」。
                        //   第 105 轮起「引力随引力球数值放大」：5e8 是 ×5（180 > 144），
                        //   那颗球**本来就该把子弹绕进去**，拿它来验「不绕圆」是拿错了样本。
                        var gball = world.SpawnBigBall(gfac, Cfg.GravityValueRef);
                        var gpos = new Vector2(7f, 7f);
                        if (gball != null) gball.transform.position = gpos;

                        // ★ 按游戏节拍：位置每个物理帧都推进，引力每 GravityTickDivisor 帧
                        //   才施加一次、步长乘回来（见 StepGravity）。
                        int tickDiv = Mathf.Max(Cfg.GravityTickDivisor, 1);
                        System.Func<float, float, float> turnedBy = delegate (float speed, float y0)
                        {
                            var probe = world.Bullets.Rent(gfac, new Vector2(2.5f, y0),
                                                           Vector2.right, speed, 1000d, 0f, false);
                            if (probe == null || gball == null) return -99f;
                            var one = new System.Collections.Generic.List<BigBall>();
                            one.Add(gball);
                            int tick = 0;
                            // ★ 用 `Cfg.FixedTimestep`（**游戏真正跑的那个步长**），不是
                            //   `Time.fixedDeltaTime` —— 批处理自检里 Bootstrap 没跑，
                            //   后者还是 Unity 默认的 0.02，和游戏里的 1/60 对不上。
                            //   （绕不绕得起来对这个步长**极其敏感**，差一点就从"绕起来"变成"拐一下"。）
                            float fdt = Cfg.FixedTimestep;
                            for (int s = 0; s < 3600; s++)
                            {
                                probe.Pos += probe.Vel * fdt;
                                if (++tick % tickDiv == 0)
                                    world.Bullets.ApplyGravity(one, Cfg.GravityOnBullet,
                                                               fdt * tickDiv);
                                if ((probe.Pos - gpos).sqrMagnitude
                                    > Cfg.GravityMaxRange * Cfg.GravityMaxRange) break;
                            }
                            float deg = probe.GravityTurnDeg;
                            probe.InUse = false;                 // 用完就还回池子
                            Invoke(world.Bullets, "CleanupDead", 0.02f);   // 必须立刻回收（见坑）
                            return deg;
                        };

                        // ② 靠近掠过的那颗：一定能拐弯（老实现这里是 0）
                        float nearTurn = turnedBy(Cfg.BulletMaxSpeed, 8.4f);   // 掠距 1.4m
                        Check(nearTurn > 8f,
                            "★★ 顶到速度上限的子弹**照样被引力掰弯**（用户报的「不转向了」）-> "
                            + nearTurn.ToString("F1") + "°（老实现是 0°）");
                        // ③ **转角可以超过 180°** —— 这就是「绕着球转」。
                        //   ★ 原来有一条「一辈子最多转 GravityBulletMaxTurnDeg 度」的额度，
                        //     额度 < 180° 在数学上就禁止绕圈，第 103 轮整个删掉了。
                        //   ★ 绕不绕得起来对**掠距极其敏感**（差 0.1m 就从"绕起来"变成
                        //     "拐一下就走"），所以这里**扫一排掠距取最大值**，别赌单一那个点。
                        float bestTurn = 0f; float bestY = 0f;
                        for (float y = 8.0f; y <= 13.01f; y += 0.2f)
                        {
                            float t = turnedBy(Cfg.BulletMaxSpeed, y);
                            if (t > bestTurn) { bestTurn = t; bestY = y; }
                        }
                        // ★★ 规格变更（用户）：「子弹**不需要**绕圆」——
                        //   现在 `BulletMaxSpeed = 12`（v² = 144）而 `GravityOnBullet = 36`（固定），
                        //   引力只有绕圆所需的 1/4，所以**不该再要求子弹转过 180°**。
                        //   原来那条 `bestTurn > 180f` 是「第 103 轮：照录像让子弹绕圈」的规格，
                        //   已随用户这次的要求作废。现在改成断言**相反**的事实：
                        //   转角**不足以绕圈**（< 180°），但仍然**明显大于 0°**（引力还在起作用，
                        //   别把引力整个关掉）。
                        Check(bestTurn > 5f && bestTurn < 180f,
                            "★ 子弹被引力掰弯但**绕不起来**（用户：不需要绕圆）-> "
                            + bestTurn.ToString("F1") + "° @ y=" + bestY.ToString("F1")
                            + "（要求 5° ~ 180° 之间）");
                        // ★ 引力常量是**固定的 36**，**故意不等于** `BulletMaxSpeed`²(=144)：
                        //   等于就会绕圆，而用户要的是不绕。所以这里断言它**确实小于** v²。
                        {
                            float v2 = Cfg.BulletMaxSpeed * Cfg.BulletMaxSpeed;
                            Check(Cfg.GravityOnBullet < v2,
                                "★ 引力（固定 36）< 速度上限² ⇒ 子弹**不绕圆** -> "
                                + Cfg.GravityOnBullet.ToString("F0") + " < " + v2.ToString("F0"));
                        }
                        if (gball != null) gball.Consume();
                    }

                    // ── ★★★ 新规格（用户）：「引力需要优化，**数值越大的球引力应该是更大的**」 ──
                    //   查出来的实情：两条引力路只有一条看了数值 ——
                    //     · 大球那一路 `a ∝ 引力球数值`（线性）✓
                    //     · **子弹那一路 `a = 36/距离`，公式里根本没有引力球的数值** ✗
                    //       → 100M 的引力球和 100B 的中子星对子弹的引力**一模一样**。
                    //   现在两条路共用 `GameWorld.GravityValueFactor`，这里把它钉住。
                    {
                        Check(Mathf.Abs(GameWorld.GravityValueFactor((double)Cfg.GravityValueRef) - 1f) < 1e-4f,
                            "★ 放大系数的参考点 = 引力球门槛（100M），那一档正好 ×1（观感不变）-> ×"
                            + GameWorld.GravityValueFactor((double)Cfg.GravityValueRef).ToString("F3"));

                        double[] vs = { Cfg.GravityValueRef, 5e8d, 1e9d, 1e10d };
                        bool mono = true;
                        for (int i = 1; i < vs.Length; i++)
                            if (!(GameWorld.GravityValueFactor(vs[i]) > GameWorld.GravityValueFactor(vs[i - 1])))
                                mono = false;
                        Check(mono, "★ 引力球数值越大、系数越大（严格单调）-> "
                            + string.Join(" / ", System.Array.ConvertAll(vs,
                                v => GameWorld.GravityValueFactor(v).ToString("F2"))));

                        // 大球那一路：吸力倍数必须 = 引力系数的倍数（中子星段已被压平，不再是 ×10）
                        float p0 = GameWorld.GravityPullAccel(1e8, 5e6, 4f);
                        float p1 = GameWorld.GravityPullAccel(1e9, 5e6, 4f);
                        float e0 = GameWorld.GravityValueFactor(1e8);
                        float e1 = GameWorld.GravityValueFactor(1e9);
                        Check(Mathf.Abs(p1 / p0 - e1 / e0) < 0.01f,
                            "★★ 大球那一路：吸力倍数 = 引力系数倍数 -> "
                            + p0.ToString("F3") + " → " + p1.ToString("F3")
                            + "（×" + (p1 / p0).ToString("F2") + " vs 系数 ×"
                            + (e1 / e0).ToString("F2") + "）");

                        // 子弹那一路：老实现这里**恒等于 1**（比值 1.00）
                        float f100M = GameWorld.GravityValueFactor(1e8);
                        float f500M = GameWorld.GravityValueFactor(5e8);
                        float f1B = GameWorld.GravityValueFactor(1e9);
                        float f10B = GameWorld.GravityValueFactor(1e10);
                        Check(f1B > f500M && f500M > f100M,
                            "★★★ 子弹那一路也随数值放大（老实现恒为 1 倍）-> 100M ×"
                            + f100M.ToString("F2") + " / 500M ×" + f500M.ToString("F2")
                            + " / 1B ×" + f1B.ToString("F2"));
                        // ★ 中子星段压平（用户：「中子星引力太大了」）：1B 明显小于线性的 ×10
                        Check(f1B < f100M * 6f,
                            "★ 中子星引力被压平（线性会是 ×10）-> 1B ×"
                            + f1B.ToString("F2"));
                        Check(f10B < f100M * 15f,
                            "★ 10B 的中子星引力被压平（线性 ×100）-> ×"
                            + f10B.ToString("F2"));
                        // ★ 用户第二次反馈「中子星引力还是太大、而且出现闪现」：
                        //   根因是引力每 3 个物理帧才补一次 —— 一步就能把速度/方向拧一大截，
                        //   所以光夹**大小**没用，得夹住**每一步的变化量**。
                        Check(Cfg.GravityBulletMaxTurnPerTickDeg > 0f
                              && Cfg.GravityBulletMaxTurnPerTickDeg < 90f,
                            "子弹每步最多被掰 " + Cfg.GravityBulletMaxTurnPerTickDeg.ToString("F0")
                            + "°（不会再一步拧过 90° = 闪现）");
                        Check(Cfg.GravityBigBallMaxDeltaVPerTick > 0f
                              && Cfg.GravityBigBallMaxDeltaVPerTick < Cfg.BigBallMaxSpeed,
                            "大球每步的速度增量 ≤ " + Cfg.GravityBigBallMaxDeltaVPerTick.ToString("F2")
                            + " m/s（不会一步从 +5 翻成 −6）");
                        // ★ 用户：「两颗引力球会一直二人转（互绕不撞）」→ 给相对切向速度加阻尼
                        Check(Cfg.GravityBallOrbitDamp > 0.9f && Cfg.GravityBallOrbitDamp < 1f,
                            "引力球之间有轨道阻尼（互绕会慢慢收进去撞上）-> 每步 ×"
                            + Cfg.GravityBallOrbitDamp.ToString("F3"));
                    }
                    // ★ 规格（用户）：B 级「额外拖尾」删掉；100M 及以上**换成白色粒子拖尾**。
                    // ★ 现在 `MultiplierParticleThreshold = double.MaxValue`（用户把粒子拖尾**停用**了），
                    //   所以「门槛」只要**不低于** 100M 都算合法：100M = 开着，MaxValue = 停用。
                    Check(Cfg.MultiplierParticleThreshold >= 100000000d,
                        "粒子拖尾门槛 ≥ 100M（double.MaxValue = 停用）-> "
                        + Cfg.MultiplierParticleThreshold.ToString("0"));
                    Check(Cfg.MultiplierParticleCount > 0 && Cfg.MultiplierParticleLife > 0f
                          && Cfg.MultiplierParticleInterval > 0f && Cfg.MultiplierParticleScale > 0f,
                        "粒子池 / 寿命 / 间隔 / 尺寸都是正数 -> 池 "
                        + Cfg.MultiplierParticleCount + " 颗，寿命 "
                        + Cfg.MultiplierParticleLife.ToString("F2") + "s");
                    // ★ 「永远小于」要做成派生量：拖尾必须在球本体（20）之下，不能写死互撞
                    Check(Cfg.MultiplierParticleSortingOrder < 20,
                        "粒子拖尾图层在球本体之下 -> " + Cfg.MultiplierParticleSortingOrder);
                    // ★ 门槛不许**低于**「百 M」这一档（旧的 1B 特效拖尾门槛不许残留）；
                    //   高于 = 停用（当前就是 double.MaxValue），也合法。
                    Check(Cfg.MultiplierParticleThreshold >= Cfg.GravityBallThreshold,
                        "粒子门槛不低于百 M 档（和引力球门槛同档；停用 = MaxValue）-> "
                        + Cfg.MultiplierParticleThreshold.ToString("0"));

                    // ★★ 回归（用户第 90 轮）：「有的百M增倍球在**炮台死亡后消失了**，
                    //   **粒子拖尾没消失**」。根因：拖尾是挂在**增倍区 root** 上的独立物体，
                    //   而且只有这颗球每帧调 `Tick` 才会淡出 —— 球一销毁就没人推它了，
                    //   那串白点永远定格。要求：球一死，拖尾跟着走。
                    {
                        var pf = Fac(1);
                        var pb = world.Zone.SpawnBall(pf, Cfg.MultiplierParticleThreshold * 2d);
                        pb.RefreshRadius();
                        Invoke(pb, "EnsureParticles");
                        var pt = (ParticleTrail)GetField(pb, "_particles");
                        Check(pt != null, "≥100M 的球有白色粒子拖尾");

                        // 模拟「炮台死亡」：亡语就是把场上的增倍球逐个 Consume
                        pb.Consume();
                        Check(GetField(pb, "_particles") == null,
                            "球被 Consume 之后不再持有拖尾引用");
                        Check(pt == null || !pt.gameObject.activeSelf,
                            "★ 拖尾立刻不可见（不再定格在屏幕上）");
                        pf.Balls.Remove(pb);
                    }

                    // ★ 走一遍真的：粒子拖尾盖章 + 位置 + 淡出
                    //   （编辑器里 Time.deltaTime ≈ 0，所以 dt 必须自己传）
                    {
                        var proot = new GameObject("SmokeParticleTrail");
                        var pt = ParticleTrail.Attach(proot.transform, Cfg.MultiplierParticleSortingOrder);
                        pt.SetRadius(0.3f);
                        pt.SetOn(true);
                        pt.Tick(1f, new Vector2(0.5f, 0.25f), true);   // 1 秒 → 按间隔盖满一轮
                        var pdots = proot.GetComponentsInChildren<SpriteRenderer>(true);
                        int lit = 0, placed = 0;
                        foreach (var sr in pdots)
                        {
                            if (sr.color.a > 0f) lit++;
                            if (sr.transform.localPosition.x > 0.4f) placed++;
                        }
                        Check(pdots.Length > 0 && lit > 0,
                            "粒子拖尾真的盖出了白点 -> " + lit + "/" + pdots.Length);
                        Check(placed == lit && lit > 0,
                            "白点盖在球经过的位置上（不是堆在原点）-> " + placed + " 个");
                        pt.SetOn(false);
                        pt.Tick(1f, Vector2.zero, false);              // 关掉 + 过一个寿命
                        int alive = 0;
                        foreach (var sr in pdots) if (sr.color.a > 0f) alive++;
                        Check(alive == 0, "关掉之后粒子全部淡掉 -> 还剩 " + alive + " 颗");
                        Object.DestroyImmediate(proot);
                    }

                    // ★ 规格：**子弹数**为 0 时文字不显示（护盾不受此限 —— 0 也照写）。
                    //   ★★ 判的是**显示出来会不会是 0**：涂地是按小数扣子弹的，
                    //   剩 0.3 那种 `Grouped` 会画成 "0" —— 那种也必须不画。
                    Check(!HudOverlay.ShowStat(0d) && !HudOverlay.ShowStat(-1d)
                          && !HudOverlay.ShowStat(0.3d) && !HudOverlay.ShowStat(0.49d)
                          && HudOverlay.ShowStat(0.6d) && HudOverlay.ShowStat(1d),
                        "子弹数为 0 时那块文字不画（连 0.3 这种「会显示成 0」的也不画）");
                    // ★ 用户：「明明显示有 1 发子弹却不发射」——
                    //   开火判定必须和 HUD 用**同一个取整**，否则 0.6 会显示成「1」却打不出来。
                    Check(Turret.EnoughBullets(0.6d) && Turret.EnoughBullets(1d)
                          && !Turret.EnoughBullets(0.4d) && !Turret.EnoughBullets(0d)
                          && !Turret.EnoughBullets(-1d),
                        "库存 0.6（显示 1）能开火、0.4（显示 0）不能 —— 开火和显示同一套取整");
                    // ★ 尾流继续削：初速和加速度都比上一轮更小
                    Check(Cfg.BigBallJetBulletSpeed < 2.6f && Cfg.BigBallJetSpeedGain < 0.0075f,
                        "大球尾流的初速/加速度继续砍 -> v=" + Cfg.BigBallJetBulletSpeed.ToString("F2")
                        + " a=" + Cfg.BigBallJetSpeedGain.ToString("F4"));

                    // ★ 增倍球缩小 / 子弹速度区分（这一轮的两个数值调整）
                    Check(Cfg.MultiplierBallShrinkPerTick > 0.9999f
                          && Cfg.MultiplierBallShrinkPerTick < 1f,
                        "增倍球每 tick 缩小到 0.99995 -> " + Cfg.MultiplierBallShrinkPerTick);
                    // ★ 初速的微弱差别是**每颗子弹各自**随机一点点（不是按排给梯度 ——
                    //   梯度会把整排系统性地拉成一条长带，用户截图里那些"白刃"就是它）。
                    Check(Cfg.BulletSpeedJitter > 0f && Cfg.BulletSpeedJitter <= 0.02f,
                        "每颗子弹的初速差别非常微弱 -> ±"
                        + (Cfg.BulletSpeedJitter * 100f).ToString("F1") + "%");

                    // ★ 左栏背景的游荡投影相机
                    {
                        var smokeRig = CameraRig.Build(world);
                        var proj = smokeRig != null ? smokeRig.Projection : null;
                        Check(proj != null, "投影相机建好了");
                        if (proj != null)
                        {
                            Check(proj.HasTarget, "投影相机有 RenderTexture");
                        // ★ 贴图的长宽比必须和面板一致，否则投影里的球是**椭圆**
                        //   （左栏是竖的、贴图是方的 → 圆球被拉长；用户截图里那个深绿椭圆就是）
                        {
                            var prt = proj.Cam != null ? proj.Cam.targetTexture : null;
                            if (prt != null && proj.Quad != null)
                            {
                                float quadAspect = proj.Quad.lossyScale.x
                                                   / Mathf.Max(proj.Quad.lossyScale.y, 1e-4f);
                                float rtAspect = (float)prt.width / Mathf.Max(prt.height, 1);
                                Check(Mathf.Abs(quadAspect - rtAspect) < 0.05f,
                                    "投影贴图长宽比 = 面板长宽比（球不会被拉长）-> "
                                    + rtAspect.ToString("F3") + " vs " + quadAspect.ToString("F3"));
                            }
                            // 取景框正中心应当映射到 (0.5, 0.5) —— 大球数值就靠这个映射补上去
                            // 注意用**相机自己的坐标**（batchmode 里 Update 不跑，相机没被挪到 WanderPos）
                            Vector2 puv;
                            Vector3 cc = proj.Cam != null ? proj.Cam.transform.position : Vector3.zero;
                            bool okUV = proj.TryViewportUV(new Vector3(cc.x, cc.y, 0f), out puv);
                            Check(okUV && Mathf.Abs(puv.x - 0.5f) < 0.02f
                                  && Mathf.Abs(puv.y - 0.5f) < 0.02f,
                                "投影映射：取景框中心 -> (0.5,0.5) -> "
                                + puv.x.ToString("F2") + "," + puv.y.ToString("F2"));
                        }
                            Check(System.Math.Abs(proj.QuadAlpha - Cfg.WanderCamAlpha) < 0.001f,
                                "投影铺在背景上的透明度 = 25% -> " + proj.QuadAlpha.ToString("P0"));

                            // ★★ 第 102 轮（用户）：「摄像机（左栏投影）里的文字大小与右边实际的
                            //   文字大小不匹配」。根因：那块 quad 是把「7m 高的一片战场」**铺满整个左栏**
                            //   （左栏视野 ~27m 高），贴图里的球被放大了 `zoneH / WanderCamSize` 倍，
                            //   而补画的球数值还按 1:1 算 → 字比球小一大圈。
                            //   这条盯着「确实在放大」——倍数变回 1 就说明又漏乘了。
                            float projZoom = HudOverlay.ProjectionZoom(proj.Quad);
                            Check(projZoom > 1.5f,
                                "★ 左栏投影是**放大**显示的，投影里补画的文字必须乘这个倍数 -> x"
                                + projZoom.ToString("F2"));
                            // 跑一段，必须一直待在地图里面（碰墙原速反弹）
                            bool inBounds = true;
                            float half = Cfg.WanderCamSize * 0.5f;
                            for (int i = 0; i < 200; i++)
                            {
                                Invoke(proj, "LateUpdate");
                                Vector2 w = proj.WanderPos;
                                if (w.x < half - 0.01f || w.x > Cfg.MapWorldSize - half + 0.01f ||
                                    w.y < half - 0.01f || w.y > Cfg.MapWorldSize - half + 0.01f)
                                { inBounds = false; break; }
                            }
                            Check(inBounds, "投影方框一直在地图内（碰墙反弹）");
                            float away = Vector2.Distance(proj.QuadWorldPos, Cfg.ZoneCenter);
                            Check(away < 0.01f,
                                "投影方块正好铺在增倍区中心（不是偏到取景外面）-> 偏了 "
                                + away.ToString("F2") + "m");
                        }
                    }

                    // ★ 规格：数值归零时拖尾**逐渐缩短到没有**，不是啪一下消失
                    {
                        var bf2 = Fac(1);
                        int acBefore = pool.ActiveCount;
                        var fb = pool.Rent(bf2, new Vector2(4f, 4f), Vector2.right, 0f, 100d, 0f, false);
                        fb.Release();
                        Invoke(pool, "Update");
                        Check(fb.FadeTimer > 0f, "耗尽后先进入渐隐期 -> 剩 " + fb.FadeTimer.ToString("F2") + "s");
                        // ★ 「还活着」= **仍然留在池子的活动表里**（拖尾才收得掉，不是啪一下没）。
                        //   终极优化之后子弹的 GameObject 一直是 inactive 的（谁在用只看 InUse），
                        //   所以这里不能再用 activeSelf 判断。
                        Check(pool.ActiveCount >= acBefore + 1,
                            "渐隐期间对象还留在活动表里（拖尾才收得掉，不是啪一下没）-> "
                            + pool.ActiveCount + " >= " + (acBefore + 1));
                        Check(Cfg.BulletTrailFadeOut > 0.1f,
                            "渐隐时长合理 -> " + Cfg.BulletTrailFadeOut.ToString("F2") + "s");
                    }

                    // ★ 第一版：两颗移动阻挡球（只会水平移动、碰墙折返）
                    {
                        Check(world.Zone.Movers.Count == Cfg.MovingBlockerY.Length,
                            "移动阻挡球数量 -> " + world.Zone.Movers.Count);
                        bool horiz = world.Zone.Movers.Count > 0;
                        float y0 = 0f;
                        for (int i = 0; i < world.Zone.Movers.Count; i++)
                        {
                            var mv = world.Zone.Movers[i];
                            if (mv == null) { horiz = false; continue; }
                            if (i == 0) y0 = mv.transform.localPosition.y;
                            else if (Mathf.Abs(mv.transform.localPosition.y - y0) < 1e-4f) horiz = false;
                        }
                        Check(horiz, "每颗移动阻挡球各在一个高度上（门排/阻挡球排/武器区）");
                        var m0 = world.Zone.Movers.Count > 0 ? world.Zone.Movers[0] : null;
                        Check(m0 != null && m0.Limit > 0f && m0.Limit < Cfg.ZoneWidth * 0.5f,
                            "活动范围在墙内 -> ±" + (m0 != null ? m0.Limit.ToString("F2") : "?") + "m");
                        // 碰墙折返：手动把它挪到极限之外，跑一帧固定更新它必须回到范围里
                        if (m0 != null)
                        {
                            var rb0 = m0.GetComponent<Rigidbody2D>();
                            Check(rb0 != null && rb0.bodyType == RigidbodyType2D.Kinematic,
                                "移动阻挡球是 Kinematic（不会被重力推走）");
                        }
                    }
                    // ★ 阻挡球一律灰、撞到闪一下（和浮力区没关系了）
                    {
                        // Gates[0] 是 x16；普通门从下标 1 开始
                        var g0 = world.Zone.Gates[1];
                        var bodySr = g0.transform.Find("JudgeBar");
                        var sr0 = bodySr != null ? bodySr.GetComponent<SpriteRenderer>() : null;
                        Check(sr0 != null, "拿得到乘法门的判定条");
                        if (sr0 != null)
                        {
                            Color baseCol = sr0.color;
                            g0.Flash();
                            Invoke(g0, "Update");
                            float moved = Mathf.Abs(sr0.color.r - baseCol.r)
                                        + Mathf.Abs(sr0.color.g - baseCol.g)
                                        + Mathf.Abs(sr0.color.b - baseCol.b);
                            Check(moved > 0.001f, "球过门时方块会闪一下 -> 色差 " + moved.ToString("F3"));
                        }
                    }
                }

                // ── ★★ 直射（用户第 87 轮）：炮台不转时**也照发弹** ──
                // 直射走的是**按时间**那条路（`StepDirectFire`）。按转角那条路炮台不转就
                // 一个角度都攒不出来 —— 这条就是防它退回去的。
                {
                    var df = Fac(3);
                    df.Bullets = 1000000d;
                    df.TurretMode = WeaponSlotKind.Direct;
                    df.Turret.AimAngle = 37f;
                    int n0d = pool.ActiveCount;
                    for (int k = 0; k < 5; k++) Invoke(df.Turret, "StepDirectFire", 0.05f);
                    Check(pool.ActiveCount > n0d,
                        "直射模式照样在发弹（按时间，不是按转角）-> "
                        + n0d + " → " + pool.ActiveCount);
                    // 散射那条路不能受影响
                    df.TurretMode = WeaponSlotKind.Scatter;
                    int n1d = pool.ActiveCount;
                    for (int k = 0; k < 5; k++) Invoke(df.Turret, "StepFiring", 0.05f, 0f);
                    Check(pool.ActiveCount > n1d,
                        "切回散射后按转角那条路照旧 -> " + n1d + " → " + pool.ActiveCount);
                    df.TurretMode = WeaponSlotKind.Scatter;
                }

                // ── ★★ 尾流加速（用户第 88 轮）：尾流**照常一直喷**，但**加速只给「几乎没动」的时候** ──
                {
                    float accel = Cfg.BigBallJetRate * Cfg.BigBallJetSpeedGain;
                    Debug.Log(string.Format("[SMOKE] 尾流加速 {0:F2} m/s²（只在慢速时给）", accel));
                    Check(Cfg.BigBallJetBoostMaxSpeed > 0f
                          && Cfg.BigBallJetBoostMaxSpeed < Cfg.BigBallMaxSpeed,
                        "尾流加速的门槛在 (0, 速度上限) 之间 -> "
                        + Cfg.BigBallJetBoostMaxSpeed.ToString("F2")
                        + " / 上限 " + Cfg.BigBallMaxSpeed.ToString("F2"));

                    // ★★ 回归：用户第 90 轮问「大球/中子星为什么**无缘无故转向**」——
                    //   根因是「卡住自救」的门槛(0.35) **比尾流巡航速度(0.25)还高**：
                    //   被尾流稳稳推着的球在系统眼里一直是卡住的，每 1.2 秒被**随机**换一个方向。
                    //   这两条大小关系必须成立，否则那个 bug 立刻回来。
                    Check(Cfg.BigBallStuckSpeed < Cfg.BigBallJetBoostMaxSpeed,
                        "「卡住」的门槛必须**低于**尾流巡航速度，否则球会被反复随机踢 -> "
                        + Cfg.BigBallStuckSpeed.ToString("F2") + " < "
                        + Cfg.BigBallJetBoostMaxSpeed.ToString("F2"));
                    Check(Cfg.BigBallStuckRescueTime > 0.2f
                          && Cfg.BigBallStuckKickSpeed > Cfg.BigBallStuckSpeed
                          && Cfg.BigBallStuckKickSpeed < Cfg.BigBallMaxSpeed,
                        "卡住自救是「轻轻一推」：比卡住门槛快、又不超过速度上限 -> "
                        + Cfg.BigBallStuckKickSpeed.ToString("F2"));

                    // 方向是**确定性**的：朝离开最近那面墙（不是随机）
                    {
                        var sb = Fac(2);
                        var bb2 = world.SpawnBigBall(sb, 2000000d);
                        // 贴着左墙 → 应该往 +x 踢
                        Vector2 pLeft = new Vector2(0.4f, 7f);
                        bb2.transform.position = new Vector3(pLeft.x, pLeft.y, 0f);
                        bb2.Body.position = pLeft;
                        var dirL = (Vector2)InvokeRet(bb2, "StuckRescueDir");
                        // 贴着下墙 → 应该往 +y 踢（此时离上墙更远）
                        Vector2 pDown = new Vector2(7f, 0.4f);
                        bb2.transform.position = new Vector3(pDown.x, pDown.y, 0f);
                        bb2.Body.position = pDown;
                        var dirD = (Vector2)InvokeRet(bb2, "StuckRescueDir");
                        Check(dirL.x > 0.9f && dirD.y > 0.9f,
                            "卡住自救的方向是**离开最近那面墙**（左墙→+x，下墙→+y）-> "
                            + dirL + " / " + dirD);
                        bb2.Consume();
                        sb.BigBalls.Remove(bb2);
                    }

                    var jf = Fac(3);
                    var jb = world.SpawnBigBall(jf, 2000000d);     // 2M > 1M → 会喷尾流

                    // ① 几乎没动 → 吃到加速
                    jb.Body.velocity = Vector2.zero;
                    for (int t = 0; t < 3; t++) Invoke(jb, "TryJet");
                    Check(jb.Body.velocity.magnitude > 0f,
                        "静止的大球**吃**尾流加速 -> |v|="
                        + jb.Body.velocity.magnitude.ToString("F4"));

                    // ② 已经脱离慢速 → 一点加速都不给（尾流照喷）
                    var fastV = new Vector2(Cfg.BigBallJetBoostMaxSpeed * 3f, 0f);
                    jb.Body.velocity = fastV;
                    for (int t = 0; t < 3; t++) Invoke(jb, "TryJet");
                    Check((jb.Body.velocity - fastV).sqrMagnitude < 1e-8f,
                        "速度过了门槛就**不再**加速 -> v=" + jb.Body.velocity.ToString("F3")
                        + " 期望 " + fastV.ToString("F3"));

                    jb.Consume();
                    jf.BigBalls.Remove(jb);
                }

                // ── ★ 开火扣的必须是「实际发出去的那些」──
                // 以前是「每帧扣一发」：一帧发 13 发、账上只掉 1 发的钱 →
                // 「子弹文字掉了 2M，实际打出去 24M」。
                {
                    var ft = Fac(2);
                    ft.Bullets = 1000000d;
                    double before2 = ft.Bullets;
                    double unit2 = Cfg.BulletValueFor(ft.Bullets, GameWorld.MatchElapsed);
                    int n0 = pool.ActiveCount;
                    // ★ 现在发数由**转角**决定：喂 1 秒的「转速 × dt」进去，发数应当 ≈ 转速/单位角度。
                    // ★ 这一段测的是**按转角发弹**那条路 —— 必须显式切成散射，
                    //   因为开局现在是直射（直射走的是「按时间发弹」，一个角度都不攒）。
                    ft.TurretMode = WeaponSlotKind.Scatter;
                    const float fps = 60f;
                    float sweepPerFrame = Cfg.TurretSpinSpeed / fps;
                    int frames = (int)fps;
                    for (int k = 0; k < frames; k++) Invoke(ft.Turret, "StepFiring", 1f / fps, sweepPerFrame);
                    int fired = pool.ActiveCount - n0;
                    double spent = before2 - ft.Bullets;
                    int want = Mathf.RoundToInt(Cfg.TurretSpinSpeed / Cfg.BulletAngleStepDeg);
                    Check(System.Math.Abs(fired - want) <= 2,
                        "按转角发弹：1 秒 = 转速/单位角度 -> " + fired + " 发 / 期望 " + want);
                    // ★ 单发数值 = 总子弹数/32768+1，跑满 1 秒时弹药池在变小 →
                    //   单发数值也在变小，所以扣掉的总额必然落在 [发数×末值, 发数×首值] 之间。
                    double unitAfter = Cfg.BulletValueFor(ft.Bullets, GameWorld.MatchElapsed);
                    Check(spent <= fired * unit2 + 1d && spent >= fired * unitAfter - 1d,
                        "扣的数值 = 实际发射数 × 单发数值 -> 扣了 " + spent.ToString("F0")
                        + " / " + fired + " 发 × " + unitAfter.ToString("F1")
                        + "~" + unit2.ToString("F1"));
                }

                // ── ★ 回归：回收之后必须重建网格 ──
                // 网格里存的是 `_active` 的**下标**，而回收是 swap-remove ——
                // 网格要是回收**之前**建的，它里面就有越界下标。
                // 外部（近防查询）一读就抛异常 → Turret.Update 被打断 → 那一帧不开火
                // →「一会儿打一会儿不打」。（这正是加近防那一轮引入的。）
                {
                    var gf = Fac(0);
                    var rented = new System.Collections.Generic.List<Bullet>();
                    for (int i = 0; i < 20; i++)
                        rented.Add(pool.Rent(gf, new Vector2(3f + i * 0.01f, 8f),
                                             Vector2.right, 0f, 100d, 0f, false));
                    for (int i = 0; i < rented.Count; i += 2) rented[i].Release();   // 一半死掉

                    Invoke(pool, "Update");     // 内部：回收 → 重建网格 → 碰撞

                    var pt = typeof(BulletPool);
                    const System.Reflection.BindingFlags BF =
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var active = pt.GetField("_active", BF).GetValue(pool) as System.Collections.IList;
                    var head = pt.GetField("_head", BF).GetValue(pool) as int[];
                    var next = pt.GetField("_next", BF).GetValue(pool) as int[];

                    int bad = 0, visited = 0;
                    bool overflow = false;
                    for (int c = 0; c < head.Length && !overflow; c++)
                        for (int i = head[c]; i >= 0; i = next[i])
                        {
                            if (++visited > 200000) { overflow = true; break; }
                            if (i >= active.Count) bad++;
                        }
                    Check(!overflow, "网格遍历没有死循环");
                    Check(bad == 0, "网格下标全部合法（网格是回收之后重建的）-> 越界 " + bad + " 个");

                    // 现在真的问一次：越界的话这里会抛异常
                    bool threw = false;
                    try
                    {
                        Vector2 qp;
                        float qd;
                        pool.TryFindNearestEnemyBullet(Vector2.zero, Cfg.CiwsRange, gf, out qp, out qd);
                    }
                    catch (System.Exception e) { threw = true; Debug.LogError("[SMOKE] 近防查询抛异常: " + e); }
                    Check(!threw, "近防查询在回收之后仍然安全（不抛异常）");
                }

                // ── ★ 开火和子弹位移必须共用同一个 dt ──
                // 两者不一致的话：帧率低时开火按真实 dt 把一帧的一批铺得更长，
                // 而子弹只走了夹过 dt 的距离 —— 每帧在弹流里留一道缝，
                // 看着就是「打一串、停一下、再打一串」。
                {
                    Check(System.Math.Abs(BulletPool.StepDt(1f / 60f) - 1f / 60f) < 1e-6f,
                        "正常帧：dt 原样透传");
                    Check(System.Math.Abs(BulletPool.StepDt(0.3f) - Cfg.BulletMaxStepTime) < 1e-6f,
                        "卡顿时：dt 被夹到 BulletMaxStepTime（开火也用这一个）");
                }

                // ── ★ 子弹打子弹：规则和撞护盾一样 ──
                {
                    var fa = Fac(0);
                    var fb = Fac(1);
                    var heavy = pool.Rent(fa, new Vector2(5f, 5f), Vector2.right, 0f, 100d, 0f, false);
                    var light = pool.Rent(fb, new Vector2(5f, 5f), Vector2.left, 0f, 30d, 0f, false);
                    heavy.HitEnemyBullet(light);
                    Check(!light.InUse, "子弹打子弹：数值小的那颗消失");
                    Check(System.Math.Abs(heavy.RemainingValue - 70d) < 0.001d,
                        "子弹打子弹：赢家扣掉对方那么多值 -> " + heavy.RemainingValue.ToString("F1"));
                    Check(heavy.InUse, "子弹打子弹：赢家还活着");

                    var ally1 = pool.Rent(fa, new Vector2(6f, 5f), Vector2.right, 0f, 100d, 0f, false);
                    var ally2 = pool.Rent(fa, new Vector2(6f, 5f), Vector2.right, 0f, 100d, 0f, false);
                    ally2.HitEnemyBullet(ally1);
                    Check(ally1.InUse && ally2.InUse, "同阵营子弹之间不判定（不打自己人）");

                    // 数值相等的两颗同归于尽
                    var e1 = pool.Rent(fa, new Vector2(7f, 5f), Vector2.right, 0f, 40d, 0f, false);
                    var e2 = pool.Rent(fb, new Vector2(7f, 5f), Vector2.right, 0f, 40d, 0f, false);
                    e1.HitEnemyBullet(e2);
                    Check(!e1.InUse && !e2.InUse, "子弹打子弹：数值相等就同归于尽");
                }

                // ── ★ 回归：网格链表必须初始化成 -1（空）──
                // `new int[]` 是全 0，而遍历写的是 `for (i = _head[c]; i >= 0; i = _next[i])` ——
                // 0 会被当成第一颗子弹的下标，而 _next[0] 也是 0，于是 i 永远是 0、**死循环**
                // （症状：刚运行起来就卡死）。近防查询会在 BulletPool.Update 之前被调用，
                // 那时网格还没建过，正好撞上这个坑。
                {
                    Invoke(pool, "InitGrid");

                    var fi = typeof(BulletPool).GetField("_head",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var head = fi != null ? fi.GetValue(pool) as int[] : null;
                    bool allEmpty = head != null && head.Length > 0;
                    if (head != null)
                        for (int i = 0; i < head.Length; i++)
                            if (head[i] != -1) { allEmpty = false; break; }
                    Check(allEmpty, "空网格的链表头是 -1，不是 0");

                    // ★ 只有链表头确实初始化成 -1 时才真的去查 ——
                    //   否则网格是全 0，这个查询会**死循环**，测试会直接挂住而不是报红
                    //   （我验证修复时就是这么挂了一次）。上面那条失败就已经说明问题了。
                    if (allEmpty)
                    {
                        Vector2 np;
                        float nd;
                        bool any = pool.TryFindNearestEnemyBullet(Vector2.zero, Cfg.CiwsRange,
                                                                  Fac(0), out np, out nd);
                        Check(!any, "空网格查询立刻返回 false（不会死循环）");
                    }
                    else
                    {
                        Debug.Log("[SMOKE] （跳过空网格查询：链表头没初始化成 -1，真查下去会死循环）");
                    }

                    Invoke(pool, "RebuildGrid");   // 把网格还回去
                }

                Debug.Log(string.Format("[SMOKE] 子弹池 active={0} created={1} 小增倍球值 {2}",
                    pool.ActiveCount, pool.CreatedCount, smallBefore));

            // ── ★★ HUD 每帧不许再往 GC 里扔垃圾（第 94 轮「帧率不稳定」的根因）──
            //   做法：把 HUD 的文字/数组缓存预热一遍，然后**反复问同一个值**，
            //   用 `GC.GetTotalMemory` 量这段时间里到底有没有新分配。
            //   以前每帧上百次小对象（字符串 / StringBuilder / float[] / GUIContent），
            //   隔一会儿一次 GC —— 画面就是「一顿一顿」的。
            {
                var hudGo = new GameObject("SmokeHud");
                var hud = hudGo.AddComponent<HudOverlay>();
                try
                {
                    // ★ 判据用「**返回的是不是同一个字符串实例**」——
                    //   比量 GC 堆靠谱（反射调用自己也会分配，量不准）。
                    //   同一个值问两次返回同一个实例 → 说明真的走了缓存、没有重新拼字符串。
                    var s1 = (string)InvokeRet(hud, "NumStr", 0, 1234.5d, 0);
                    var s2 = (string)InvokeRet(hud, "NumStr", 0, 1234.5d, 0);
                    Check(ReferenceEquals(s1, s2),
                        "★ HUD 同一个值反复问 → 返回同一个字符串实例（缓存命中，不再每帧重拼）-> "
                        + s1);
                    var s3 = (string)InvokeRet(hud, "NumStr", 0, 9999.0d, 0);
                    Check(!ReferenceEquals(s1, s3) && s3 == "9 999",
                        "值变了就重新拼一次 -> " + s3);

                    var c1 = (string)InvokeRet(hud, "CountdownStr", 0, 37);
                    var c2 = (string)InvokeRet(hud, "CountdownStr", 0, 37);
                    Check(ReferenceEquals(c1, c2), "复活倒计时同理（同一秒复用同一个字符串）");

                    // ★★ 第 98 轮：子弹文字的两个间距都比原来**紧**
                    //   （行距原来是写死的 1.06；字距原来 = 字体默认 1.0）
                    Check(Cfg.TurretBulletLineFactor > 0.8f && Cfg.TurretBulletLineFactor < 1.0f,
                        "子弹数行距系数 < 1（比原来的 1.06 紧）-> "
                        + Cfg.TurretBulletLineFactor.ToString("F2"));
                    Check(Cfg.TurretBulletCharFactor > 0.8f && Cfg.TurretBulletCharFactor < 1.0f,
                        "子弹数字距系数 < 1（比字体默认紧）-> "
                        + Cfg.TurretBulletCharFactor.ToString("F2"));

                    // 武器区边界数组必须是**同一个实例**（以前每帧 new 两个）
                    var e1 = WeaponZone.SlotEdgesWorld();
                    var e2 = WeaponZone.SlotEdgesWorld();
                    Check(ReferenceEquals(e1, e2),
                        "★ 武器区边界数组只算一次（不再每帧 new -> 每帧两个数组喂 GC）");
                    Check(e1.Length == Cfg.WeaponSlotXPx.Length, "边界数组长度 = 分界点数");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(hudGo);
                }
            }

            // ── ★★ 设置面板数据层（第 105 轮：阵营数 / 颜色 / 暂停）──
            //   `Bootstrap` 不参与自检（它在 `AfterSceneLoad` 里自己跑），
            //   所以这里只验「面板依赖的那几条数据与换算」——
            //   真正的 UI 在 Play 里由 `SettingsPanel.OnGUI` 画。
            {
                // ★★ 第 109 轮（用户）：「右上角有一个设置键，在 cfg 里写个控制开关的变量」——
                //   按钮的显示和「整个面板开关」是**两个**开关，别混。
                //   ★ 这两个是**发布开关**，值会跟着发布需要改，所以这里只钉住「字段还在」，
                //     不断言它是 true（否则用户一关开关自检就红了）。
                Check(typeof(Cfg).GetField("ShowSettingsButton") != null,
                    "右上角「设置」按钮有独立开关（Cfg.ShowSettingsButton）");
                Check(typeof(Cfg).GetField("EnableSettingsPanel") != null,
                    "设置面板有总开关（Cfg.EnableSettingsPanel）");

                // 阵营数只允许四档，夹取要找**最近**的一档
                Check(GameSettings.ClampCount(3) == 4 && GameSettings.ClampCount(1) == 2
                      && GameSettings.ClampCount(5) == 4 && GameSettings.ClampCount(12) == 16
                      && GameSettings.ClampCount(999) == 16,
                    "阵营数夹到最近档位（3→4，12→16）");
                Check(GameSettings.IsValidCount(2) && GameSettings.IsValidCount(4)
                      && GameSettings.IsValidCount(8) && GameSettings.IsValidCount(16)
                      && !GameSettings.IsValidCount(5),
                    "合法阵营数 = 2 / 4 / 8 / 16");
                Check(GameSettings.FactionCount == FactionSet.Count,
                    "GameSettings 与 FactionSet 的阵营数一致 -> " + GameSettings.FactionCount);

                // 三条「跟着阵营数走」的缓存都必须和当前阵营数对齐
                Check(Palette.FactionHsv.Length == Cfg.FactionCount
                      && Palette.FactionColors.Length == Cfg.FactionCount
                      && Palette.FactionNames.Length == Cfg.FactionCount,
                    "Palette 三张表都跟着阵营数 -> " + Palette.FactionColors.Length);
                Check(Cfg.TurretNames.Length == Cfg.FactionCount
                      && TerritoryDeco.Styles.Length == Cfg.FactionCount,
                    "炮台名称 / 领土花纹表也跟着阵营数 -> "
                    + Cfg.TurretNames.Length + " / " + TerritoryDeco.Styles.Length);

                // 颜色滑条的范围：色相 360°，S/V 必须容得下阵营表里 >1 的「溢出亮色」
                Check(Mathf.Approximately(Cfg.ColorHueMax, 360f) && Cfg.ColorSvMax >= 1.1f,
                    "颜色滑条范围够用（S/V 上限 " + Cfg.ColorSvMax.ToString("F2") + " ≥ 阵营表最大值）");

                // ★★ 颜色覆盖按「阵营数 + 下标」分开存 ——
                //   4 家版调过的颜色**不许**串到 8 家版（两套其实是完全不同的阵营）。
                Check(GameSettings.SlotOf(2) == 0 && GameSettings.SlotOf(4) == 1
                      && GameSettings.SlotOf(8) == 2 && GameSettings.SlotOf(16) == 3
                      && GameSettings.SlotOf(3) == GameSettings.SlotOf(4),
                    "四种阵营数各占一个覆盖槽位（非法值先夹到最近档位）");
                Check(GameSettings.HsvKey("mor.set.hsv.h.", 4, 3)
                      != GameSettings.HsvKey("mor.set.hsv.h.", 8, 3),
                    "覆盖键里带阵营数 -> "
                    + GameSettings.HsvKey("mor.set.hsv.h.", 4, 3) + " / "
                    + GameSettings.HsvKey("mor.set.hsv.h.", 8, 3));
                {
                    const int Probe = 3;                   // 4 家 / 8 家都有这个下标
                    int cur = GameSettings.FactionCount;

                    // 自检不许把用户调过的颜色改掉 -> 两个槽各记一份原状态
                    GameSettings.SetFactionCount(4);
                    bool had4 = GameSettings.HasHsv(Probe);
                    Vector3 old4 = GameSettings.HsvOf(Probe);
                    GameSettings.SetFactionCount(8);
                    bool had8 = GameSettings.HasHsv(Probe);
                    Vector3 old8 = GameSettings.HsvOf(Probe);

                    var tag = new Vector3(200f, 1.2f, 1.4f);  // 阵营表里不可能有这个值
                    bool separated, persisted;
                    try
                    {
                        GameSettings.SetHsv(Probe, tag);

                        GameSettings.SetFactionCount(4);
                        // 4 家版这一格必须**一点没变**（还是进来时那个值），而且不等于刚写的 tag
                        separated = (GameSettings.HsvOf(Probe) - old4).sqrMagnitude < 1e-6f
                                    && (GameSettings.HsvOf(Probe) - tag).sqrMagnitude > 1e-3f;

                        GameSettings.SetFactionCount(8);
                        persisted = GameSettings.HasHsv(Probe)
                                    && (GameSettings.HsvOf(Probe) - tag).sqrMagnitude < 1e-6f;
                    }
                    finally
                    {
                        GameSettings.SetFactionCount(4);
                        if (had4) GameSettings.SetHsv(Probe, old4);
                        else GameSettings.ClearHsv(Probe);
                        GameSettings.SetFactionCount(8);
                        if (had8) GameSettings.SetHsv(Probe, old8);
                        else GameSettings.ClearHsv(Probe);
                        GameSettings.SetFactionCount(cur);
                    }

                    Check(separated, "切到 4 家版：第 " + Probe + " 家**不带** 8 家版的颜色覆盖");
                    Check(persisted, "切回 8 家版：第 " + Probe + " 家的颜色覆盖还在");
                }

                // 暂停 = timeScale 0，继续 = 回到 Cfg.TimeScale（IMGUI 不受影响，面板照常能点）
                float oldScale = Time.timeScale;
                bool oldPaused = GameSettings.Paused;
                try
                {
                    Bootstrap.SetPaused(true);
                    Check(Time.timeScale == 0f && GameSettings.Paused,
                        "暂停：timeScale = 0");
                    Bootstrap.SetPaused(false);
                    Check(Mathf.Approximately(Time.timeScale, Cfg.TimeScale) && !GameSettings.Paused,
                        "继续：timeScale 回到 " + Cfg.TimeScale);
                }
                finally
                {
                    Bootstrap.SetPaused(false);
                    Time.timeScale = oldScale;
                    GameSettings.Paused = oldPaused;
                }
            }

            // ── ★★ 定步长（录屏）模式：一帧 = 一个固定的模拟步 ──
            //   用户问的「Unity 能不能像 Algodoo 那样卡了就把一步拉长」就是这件事。
            {
                // 先确认这两个数是一对（一帧正好一个物理步）
                Check(Cfg.CaptureFramerate
                      == Mathf.RoundToInt(1f / Mathf.Max(Cfg.FixedTimestep, 1e-6f)),
                    "定步长的频率 = 1/物理步长（一帧正好一个物理步）-> "
                    + Cfg.CaptureFramerate + " vs "
                    + Mathf.RoundToInt(1f / Mathf.Max(Cfg.FixedTimestep, 1e-6f)));

                Bootstrap.SetCaptureMode(true);
                Check(Time.captureFramerate == Cfg.CaptureFramerate,
                    "★ 开定步长：Time.captureFramerate = " + Cfg.CaptureFramerate
                    + " -> " + Time.captureFramerate);
                Check(Application.targetFrameRate == Cfg.CaptureFramerate,
                    "★ 目标帧率也跟着钉在同一个值（跑得比它快的机器会限速，免得变成快放）-> "
                    + Application.targetFrameRate);

                Bootstrap.SetCaptureMode(false);
                Check(Time.captureFramerate == 0,
                    "关掉之后回到真实时间 -> captureFramerate = " + Time.captureFramerate);
                Check(Application.targetFrameRate == Cfg.TargetFrameRate,
                    "目标帧率也还原 -> " + Application.targetFrameRate);
            }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SMOKE] 抛异常: " + e);
                _failures++;
            }

            if (_failures == 0) Debug.Log("[SMOKE] ==== ALL PASS ====");
            else Debug.LogError("[SMOKE] ==== FAILED: " + _failures + " 项 ====");

            EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }

        /// <summary>反射读一个**私有字段**（自检用）。</summary>
        static object GetField(object target, string field)
        {
            var fi = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            if (fi == null) throw new System.MissingFieldException(field);
            return fi.GetValue(target);
        }

        /// <summary>反射调一个**有返回值**的私有方法（自检用）。</summary>
        static object InvokeRet(object target, string method, params object[] args)
        {
            var mi = target.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            if (mi == null) throw new System.MissingMethodException(method);
            return mi.Invoke(target, args);
        }

        /// <summary>反射调一个**静态**私有方法（自检用）。</summary>
        static object InvokeStaticRet(System.Type type, string method, params object[] args)
        {
            var mi = type.GetMethod(method,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            if (mi == null) throw new System.MissingMethodException(method);
            return mi.Invoke(null, args);
        }

        static void Invoke(object target, string method, params object[] args)
        {
            var mi = target.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi == null) throw new System.MissingMethodException(method);
            mi.Invoke(target, args);
        }

        /// <summary>自检用：把中子星半径公式算一遍（和 BigBall 里那份保持一致）。</summary>
        static float NeutronRadiusForSmoke(double value)
        {
            float v = Mathf.Max((float)value, 1f);
            return Mathf.Log(Mathf.Sqrt(v)) / Cfg.NeutronStarRadiusLogDivisor
                   - Cfg.NeutronStarRadiusBias;
        }

        /// <summary>
        /// WCAG 相对亮度对比度（1 = 同色，21 = 黑白）。★ 先把分量夹到 [0,1]（那才是**画出来**的色），
        /// 再做 sRGB → 线性 → `0.2126R + 0.7152G + 0.0722B`。用它判「文字压不压得住底色」。
        /// </summary>
        static float ContrastRatio(Color a, Color b)
        {
            float la = RelLuminance(a), lb = RelLuminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        static float RelLuminance(Color c)
        {
            return 0.2126f * Linear(Mathf.Clamp01(c.r))
                 + 0.7152f * Linear(Mathf.Clamp01(c.g))
                 + 0.0722f * Linear(Mathf.Clamp01(c.b));
        }

        static float Linear(float u)
        {
            return u <= 0.03928f ? u / 12.92f : Mathf.Pow((u + 0.055f) / 1.055f, 2.4f);
        }

        static void Check(bool ok, string what)
        {
            if (ok) Debug.Log("[SMOKE] OK   " + what);
            else { _failures++; Debug.LogError("[SMOKE] FAIL " + what); }
        }
    }
}
#endif
