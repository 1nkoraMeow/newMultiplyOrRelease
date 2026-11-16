using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 全部 HUD 与文字都在 OnGUI 里画（不需要 Canvas / TMP / 字体资源）。
    ///
    /// 版面（和视频对齐）：
    ///   左栏增倍区：顶部 x16 门 + 它下面的计数器 → 中部 10 个门的白牌和上方计数器
    ///               → 底部 8 格武器栏
    ///   右栏战场：四角炮台（上方子弹数量、下方护盾量）、大球数值
    ///
    /// 所有世界文字的字号都跟着**物体在屏幕上的半径**走，缩放时不会和物体脱节。
    /// 没有称号 / 名称系统（按你的要求去掉了）。
    /// </summary>
    public class HudOverlay : MonoBehaviour
    {
        GameWorld _world;
        CameraRig _rig;
        int _focus;

        GUIStyle _label, _labelPlain, _labelCjk, _plate, _counter, _panel, _panelTitle, _nameStyle;
        /// <summary>总值计数条上的白字（Arimo，居中）—— 每帧只改 fontSize。</summary>
        GUIStyle _gainStyle;
        /// <summary>增倍球右上角的存活时间（左对齐，往球外延伸）。</summary>
        GUIStyle _lifeStyle;
        /// <summary>
        /// 总值计数条**内条**（累计增益占比）的缓动状态（每个阵营一份，按阵营下标存）。
        /// 对应 Algodoo 原版里那两个 poststep 的 [_size, _pos]：
        ///   宽度和位置**各自**朝目标缓动 0.2/步，所以条是「从左边长出去」而不是居中缩放。
        /// `_gainFillInit` 是「还没初始化过」的标记：第一次**直接落到目标值**，
        /// 不从 0 滑进来（否则开局会看到四条从无到有地长一遍）。
        /// </summary>
        float[] _gainFillW = new float[0];
        bool[] _gainFillInit = new bool[0];

        /// <summary>
        /// 这一帧「炮台那片 HUD 文字」的屏幕矩形（每个阵营一块）。
        /// ★ 用途：大球在 IMGUI 里重画的那份副本**只有在和这些文字重叠时才需要**
        ///   （它的存在意义就是「大球盖住文字」）。绝大多数大球都在战场中间、
        ///   根本不压着文字，那份副本就是白画 —— 每颗球每帧 2 次 `GUI.DrawTexture`
        ///   加两张覆盖面积不小的半透明大图，球一多就是一笔很实在的开销。
        /// </summary>
        readonly List<Rect> _hudTextRects = new List<Rect>(4);
        /// <summary>武器区 x2 格的那个字号（每帧复用一个 style，别每帧 new）。</summary>
        GUIStyle _plateGate;
        // ★ 中文兜底字体：做成 **static** —— HUD 会跟着世界一起重建（设置面板换阵营数），
        //   每次重建都 `CreateDynamicFontFromOSFont` 一份会一路漏字体图集。
        static Font _cjk;
        static bool _cjkTried;
        bool _hasCjk;

        // ── ★★ 每帧文字缓存（第 94 轮，专治「帧率不稳定 / 总是跳帧」）──
        //   HUD 里同一处的文字，**只在显示值真的变了的时候**才重新拼字符串。
        //   原来的写法是每帧对每块牌子、每颗球、每个计数器都重新格式化一遍 ——
        //   一帧上百次小对象分配（字符串 / StringBuilder / 数组 / GUIContent），
        //   于是隔一会儿就来一次 GC，画面就是「一顿一顿」的。
        //   ★ `NumStr` 的缓存是**按值**命中的：值没变就直接复用上次那个字符串。
        float[] _weaponXs;
        double[] _numV;
        string[] _numS;
        string[] _gateLabels;      // 乘法门上「x2 / x8」这种永远不变的标签
        string[] _slotLabels;      // 武器区 x2 格子上的标签
        double[] _groupsV;         // 炮台「三位一组」那三行
        string[][] _groupsS;
        GUIContent _content;       // 测量文字宽度用，复用同一个（GUIContent 是类，会分配）

        public void Bind(GameWorld world, CameraRig rig)
        {
            _world = world;
            _rig = rig;
        }

        // ─────────────────────────────────────────────────────────────
        // 样式
        // ─────────────────────────────────────────────────────────────

        void EnsureStyles()
        {
            if (_label != null) return;

            var installed = Font.GetOSInstalledFontNames();

            Font Pick(params string[] names)
            {
                var found = new List<string>();
                for (int i = 0; i < names.Length; i++)
                    for (int k = 0; k < installed.Length; k++)
                        if (installed[k] == names[i]) { found.Add(names[i]); break; }
                return found.Count > 0 ? Font.CreateDynamicFontFromOSFont(found.ToArray(), 32) : null;
            }

            // ★★ 主字体：Arimo **打进工程**（`Assets/Resources/Fonts/Arimo.ttf`）。
            //   以前走 `Font.CreateDynamicFontFromOSFont`，只在**装了 Arimo 的系统**上才是 Arimo，
            //   换台机器 / 打包给别人就跑成 Arial 甚至 Helvetica。打进 Resources 之后
            //   跟系统装没装无关，exe 拷到哪都是同一套字形。
            //   真找不到资源才退回系统字体（兜底，正常不会走到）。
            Font arimo = Resources.Load<Font>("Fonts/Arimo");
            if (arimo == null)
            {
                Debug.LogWarning("[FONT] 找不到 Resources/Fonts/Arimo.ttf，退回系统 Arimo / Arial / Helvetica");
                arimo = Pick("Arimo", "Arial", "Helvetica", "Arial Unicode MS");
            }
            // 中文兜底（Arimo 没有中文字形）—— 只建一次，世界重建时复用
            if (!_cjkTried)
            {
                _cjkTried = true;
                // ★★ 系统字体名**两边都要列**：Windows 上根本没有 PingFang / Heiti，
                //   只写 macOS 那串的话 `Pick` 一个都找不到 -> `_hasCjk = false` -> 中文全变方块。
                //   `Pick` 是逐个对 `Font.GetOSInstalledFontNames()` 比的，找不到的自动跳过。
                //   （Windows 中文系统的字体名有时是中文的「微软雅黑」，所以中英两套都写。）
                _cjk = Pick("Microsoft YaHei", "Microsoft YaHei UI", "微软雅黑",
                            "SimHei", "黑体", "SimSun", "宋体",
                            "Noto Sans CJK SC", "Source Han Sans SC", "HarmonyOS Sans SC",
                            "PingFang SC", "Heiti SC", "Hiragino Sans GB", "STHeiti",
                            "Arial Unicode MS");
            }
            _hasCjk = _cjk != null;

            // ★ 规格：**只有炮台牌子上那两块数字**（子弹数 / 护盾）和原版一样是粗体。
            //   大球 / 增倍球 / 乘法门上的文字**不加粗**（用户明确说过）。
            _label = New(arimo, 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _label.clipping = TextClipping.Overflow;   // 别把 g / y 的下缘裁掉
            _labelPlain = New(arimo, 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal);
            _labelPlain.clipping = TextClipping.Overflow;
            // 通用「可能含中文」的样式：Arimo / Rajdhani 都没有汉字，画中文会是一排方块。
            // 名字（阵营表里手填的）和 F1 面板的文字都走它，用系统 CJK 字体。
            // ★ 字号 / 颜色在 `DrawTextAt` 里按每次调用改，这个 20 只是占位。
            _labelCjk = New(_hasCjk ? _cjk : arimo, 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal);
            _labelCjk.clipping = TextClipping.Overflow;   // 和名称样式一致（别裁掉 g / y 的下缘）
            _plate = New(arimo, 16, TextAnchor.MiddleCenter, Color.black, FontStyle.Normal);
            // ★ 规格：炮台下面那三行**名称**用 **Rajdhani Medium**。
            // ★★ 为什么把 ttf **打进工程**、而不是挑系统字体：
            //   Rajdhani 是「仅当前用户」安装的（在 `%LOCALAPPDATA%\...\Fonts`），
            //   而 Unity 的 `Font.GetOSInstalledFontNames()` **只枚举 `C:\Windows\Fonts`**
            //   （系统级）—— 所以 `Pick` 永远看不到它，只能静默退回 Arimo
            //   （表现就是用户报的「改了字体没效果」）。打进 Resources 后
            //   跟系统装没装无关，打包到 macOS 也一致。
            //   字体文件：`Assets/Resources/Fonts/Rajdhani-Medium.ttf`（= 类名 Resources.Load 路径去掉扩展名）。
            // ★ 字重：IMGUI 的 `FontStyle` 只有 Normal / Bold、**没有 Medium**；
            //   但字体文件本身就是 Medium，所以用 `Normal` 画出来就是 Medium 字形。
            Font nameFont = Resources.Load<Font>("Fonts/Rajdhani-Medium");
            if (nameFont == null)
            {
                Debug.LogWarning("[FONT] 找不到 Resources/Fonts/Rajdhani-Medium.ttf，退回系统字体（多半会退到 Arimo）");
                nameFont = Pick("Rajdhani Medium", "Rajdhani") ?? arimo;
            }
            // （排查字体那阵子这里打过一行 `Debug.Log`，字体已经确认是 Rajdhani-Medium，去掉了。）
            _nameStyle = New(nameFont, 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal);
            _nameStyle.clipping = TextClipping.Overflow;
            _counter = New(arimo, 18, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal);
            // ★ F1 面板里是**中文说明 + 英文数字**混排，所以整块都用 CJK 字体
            //   （系统找不到 CJK 时才退回 Arimo —— 那种机器上中文本来就是方块，没别的办法）。
            Font panelFont = _hasCjk ? _cjk : arimo;
            _panel = New(panelFont, 16, TextAnchor.UpperLeft, Color.white, FontStyle.Normal);
            _panelTitle = New(panelFont, 17, TextAnchor.UpperLeft, Color.white, FontStyle.Normal);
        }

        static GUIStyle New(Font font, int size, TextAnchor anchor, Color color, FontStyle fs)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = size,
                alignment = anchor,
                fontStyle = fs,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip
            };
            s.normal.textColor = color;
            return s;
        }

        void OnGUI()
        {
            if (_world == null || _rig == null || _rig.ZoneCam == null) return;
            // ★ 第 131 轮（用户截图）：设置面板开着时**整块 HUD 不画**。
            //   之前想靠 `SettingsPanel` 里的 `GUI.depth = -10` 把面板压在 HUD 上面，
            //   但实测压不住 —— 表现就是炮台的「名称 / 子弹数 / 护盾值」压在
            //   「切换会重开一局」上面。面板本身有一层 0.55 的黑遮罩，HUD 消失不突兀。
            if (SettingsPanel.BlocksHud) return;
            // OnGUI 每帧会被调用多次（Layout / Repaint / 各种输入事件）。
            // 我们全部用绝对坐标，不需要 Layout 阶段 —— 只画 Repaint 一次，HUD 开销直接砍一半以上。
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            long _t0 = Prof.Tick();

            // ★ 画面装饰：全屏暗角（压在世界之上、文字之下 —— 别把四角的文字也压暗了）
            if (Cfg.VignetteStrength > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, Cfg.VignetteStrength);   // 调低过，见 Cfg
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                                Primitives.Vignette.texture);
                GUI.color = Color.white;
            }

            // `hud` 一栏偏高时，这三段告诉你钱花在哪
            long _tz = Prof.Tick();
            DrawZoneLayer(_rig.ZoneCam);
            DrawGainBar();
            Prof.HudZoneMs = Prof.Since(_tz);

            long _tb = Prof.Tick();
            DrawBattleLayer(_rig.BattleCam);
            Prof.HudBattleMs = Prof.Since(_tb);

            // 再拆两块：底部武器栏 / F1 面板（面板是调试用的，压着 F1 量出来的读数要能扣掉它）
            long _tr = Prof.Tick();
            DrawWeaponBar();
            DrawHint();
            Prof.HudWeaponMs = Prof.Since(_tr);

            long _tp = Prof.Tick();
            if (Cfg.ShowStats) DrawStatsPanel();
            Prof.HudPanelMs = Prof.Since(_tp);
            Prof.HudRestMs = Prof.HudWeaponMs + Prof.HudPanelMs;

            Prof.HudMs = Prof.Since(_t0);
        }

        void Update()
        {
            Prof.PushFrame(Time.unscaledDeltaTime);
            if (_world == null) return;

            for (int i = 0; i < 8 && i < _world.Factions.Count; i++)   // ★ 4 家/8 家都够用
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _focus = i;

            if (Input.GetKeyDown(KeyCode.K) && _focus < _world.Factions.Count)
            {
                var t = _world.Factions[_focus].Turret;
                if (t != null && !t.IsDead) t.Die();
            }

            // ★ 调试：给当前聚焦阵营在战场中央刷一颗 **999M 的大球**（引力球但还不是中子星）。
            //   稍微抖一点位置，连按几次不会叠在同一个点上。
            if (Input.GetKeyDown(KeyCode.B) && _focus < _world.Factions.Count)
            {
                float half = Cfg.MapWorldSize * 0.5f;
                Vector2 jitter = Random.insideUnitCircle * 0.6f;
                _world.SpawnBigBall(_world.Factions[_focus], Cfg.DebugBallValue,
                                    new Vector2(half, half) + jitter);
            }

            // ★★ 第 113 轮：调试键 **V** —— 给当前聚焦阵营刷一颗 **99M 吸血球**。
            //   之前这里是一段 `TEMP-DEBUG-VAMPIRE`（1M、固定位置），现在正式化：
            //   数值走 `Cfg.DebugVampireBallValue`；99M < 100M 引力门槛，所以它只是
            //   「普通大球 + 吸血标记」，不会叠上引力球/中子星特效，方便单独看
            //   第 112 轮的螺旋笔迹和吸血碰撞。和 B 键一样抖一点位置，连按不叠在一起。
            if (Input.GetKeyDown(KeyCode.V) && _focus < _world.Factions.Count)
            {
                float half2 = Cfg.MapWorldSize * 0.5f;
                Vector2 jitter2 = Random.insideUnitCircle * 0.6f;
                _world.SpawnBigBall(_world.Factions[_focus], Cfg.DebugVampireBallValue,
                                    new Vector2(half2, half2) + jitter2, vampire: true);
            }

            // ★★ 第 125 轮：调试键 **L**（Left）—— 给**每个阵营**在左栏各补一颗**增倍球**。
            //   和「每 80 秒补一颗」同一个入口（`GameWorld.GrantBallToAllFactions` →
            //   `MultiplierZone.SpawnBall`），所以是从炮口按当前朝向打出去的，
            //   数值 = `Cfg.NewBallStartValue`（1）。用来观察多球同时撞门 / 挤在武器栏的样子。
            if (Input.GetKeyDown(KeyCode.L)) _world.GrantBallToAllFactions();

            if (Input.GetKeyDown(KeyCode.F1)) Cfg.ShowStats = !Cfg.ShowStats;
            // ★★ F2：定步长（录屏）模式 —— 一帧 = 一个模拟步，卡了就变慢动作而不是跳帧。
            //   状态显示在 F1 面板里（不放世界里的常驻标记，免得录进视频）。
            if (Input.GetKeyDown(KeyCode.F2)) Bootstrap.SetCaptureMode(!Bootstrap.CaptureMode);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                Time.timeScale = Mathf.Max(Time.timeScale * 0.5f, 0.125f);
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                Time.timeScale = Mathf.Min(Time.timeScale * 2f, 8f);
        }

        // ── ① 增倍区（左栏）────────────────────────────────────────

        void DrawZoneLayer(Camera cam)
        {
            var zone = _world.Zone;
            float pxPerUnit = PxPerUnit(cam);

            // ★ 投影背景里的大球**要补上它的数值**。
            //   投影是一张 RenderTexture，而 HUD 文字全是 IMGUI —— IMGUI **不会**被相机渲进贴图，
            //   所以背景里的大球只有一团颜色、没字。这里反过来：
            //   用「游荡投影相机」把大球的战场坐标映射到左栏那块 quad 的屏幕位置，在 IMGUI 里补一遍。
            DrawProjectedBigBallText(cam, pxPerUnit);

            // 8 块乘法门：倍率（黑字）写在深灰方块正中，通过次数写在方块上方
            for (int i = 0; i < zone.Gates.Count; i++)
            {
                var g = zone.Gates[i];
                Vector3 p = g.transform.position;
                // 规格：门上的字和计数器的字都是**固定大小**，不随方块高低变化
                int size = Mathf.Clamp(Mathf.RoundToInt(Cfg.GateLabelHeight * pxPerUnit), 8, Cfg.HudFontSizeMax);
                DrawWorldText(cam, p, LabelAt(ref _gateLabels, i, g.Multiplier),
                              Color.black, size, false, _labelPlain);
                // ★ 计数器的位置**由门自己给**（普通门在上方、x16 在下方 —— 上面坐着炮台）
                DrawWorldText(cam, p + (Vector3)g.CounterOffset,
                    NumStr(10 + i, g.PassCount, NumGrouped), Color.white,
                    Mathf.Clamp(Mathf.RoundToInt(Cfg.GateCounterHeight * pxPerUnit), 8, Cfg.HudFontSizeMax), false,
                    _labelPlain);
            }

            // 增倍球数值
            for (int f = 0; f < _world.Factions.Count; f++)
            {
                var faction = _world.Factions[f];
                for (int i = 0; i < faction.Balls.Count; i++)
                {
                    var b = faction.Balls[i];
                    if (b == null || b.Consumed) continue;
                    // 字号只看球的屏幕直径（见 `Cfg.BallTextScale`），不再"撑满文本宽度"
                    float diameterPx = b.Radius * 2f * pxPerUnit;
                    string txt = NumStr(200 + i, b.Value, NumRounded);
                    int size = BallTextSize(diameterPx);
                    DrawWorldText(cam, b.transform.position, txt, faction.TextColor, size, false,
                                  _labelPlain);

                    // ★ 存活时间（用户）：球的**右上方**，m:ss；不足 20 秒不显示。
                    //   「碰到判定框 → 回到炮台」时归零，见 MultiplierBall.LaunchFrom。
                    if (b.LifeTime >= LifeClockMinSeconds)
                    {
                        // 用户：字号再大一点（0.6 → 0.85），颜色固定**白色**，位置往下一点
                        int lifeSize = Mathf.Clamp(Mathf.RoundToInt(size * 0.85f), 10, 56);
                        DrawBallLifeClock(cam, b, Color.white, lifeSize, pxPerUnit);
                    }
                }
            }
        }

        /// <summary>存活时间的最短显示秒数（用户规格：**20 秒以下不显示**）。</summary>
        public const float LifeClockMinSeconds = 20f;

        /// <summary>
        /// 存活时间文本的小缓存：**只跟整秒有关**，所以按「秒数」命中即可，
        /// 不用给每颗球分配 slot（64 格环形，够覆盖场上所有球的当前秒数）。
        /// 这样每帧不会为每颗球都拼一个新字符串。
        /// </summary>
        readonly int[] _clockSecs = new int[64];
        readonly string[] _clockText = new string[64];

        string LifeClockStr(float seconds)
        {
            int secs = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int k = secs & 63;
            if (_clockText[k] == null || _clockSecs[k] != secs)
            {
                _clockSecs[k] = secs;
                _clockText[k] = ClockText(seconds);
            }
            return _clockText[k];
        }

        /// <summary>
        /// 存活时间的文本格式：**m:ss**（分钟不补零、秒补两位）。
        /// 20 → "0:20"、65 → "1:05"、3600 → "60:00"。向下取整（19.9s 还是 0:19）。
        /// </summary>
        public static string ClockText(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        /// <summary>
        /// 增倍球**右上方**的存活时间。锚在球右上 45° 的边缘上、文字往右延伸，
        /// 这样不会盖住球正中间那个数值。
        /// </summary>
        void DrawBallLifeClock(Camera cam, MultiplierBall b, Color color, int size, float pxPerUnit)
        {
            if (cam == null || b == null) return;

            Vector3 sp = cam.WorldToScreenPoint(b.transform.position);
            if (sp.z < 0f) return;
            float x = sp.x;
            float y = Screen.height - sp.y;

            if (_lifeStyle == null)
            {
                _lifeStyle = new GUIStyle(_labelPlain);
                _lifeStyle.alignment = TextAnchor.MiddleLeft;
                _lifeStyle.clipping = TextClipping.Clip;
            }
            size = QuantSize(size);
            _lifeStyle.fontSize = size;
            _lifeStyle.normal.textColor = color;

            float rPx = Mathf.Max(b.Radius * pxPerUnit, 1f);
            float h = size * Cfg.HudLineHeightFactor;
            // ★ 从球心往右上 45° 的**外侧**落笔（> 0.707 才在圆外），文字再往右延伸，
            //   所以不会和球正中间那串数值抢位置。
            //   用户：位置再**往下**一点（纵向 0.95 → 0.55，横向不动）。
            float lx = x + rPx * 0.85f;
            float top = y - rPx * 0.55f - h * 0.5f;
            GUI.Label(new Rect(lx, top, rPx * 3f + 80f, h), LifeClockStr(b.LifeTime), _lifeStyle);
        }

        /// <summary>
        /// 给**左栏投影背景**里的大球补数值。
        ///
        /// 做法：投影相机是正交的、而且投影是 1:1 的，所以
        ///   ① 大球在投影里的**屏幕大小**就是 `2 × Radius × 增倍区相机的 pxPerUnit`；
        ///   ② 位置用 `WorldToViewportPoint` 得到 (0..1)，再按那块 quad 的屏幕矩形铺开。
        /// 文字和投影本身一样淡（alpha = WanderCamAlpha）—— 它是背景的一部分，不该抢戏。
        /// </summary>
        /// <summary>
        /// ★★ 左栏那块**战场投影**的放大倍数：`quad 的世界高 / WanderCamSize`。
        /// 投影相机只取 `WanderCamSize`（7m）那么高的一片战场，却把它**铺满整个左栏**，
        /// 所以贴图里的每个东西都被放大了这个倍数 —— 投影里补画的文字必须乘它。
        /// ★ 做成公共口子，是为了「画」和「自检」用的是**同一个数**。
        /// </summary>
        public static float ProjectionZoom(Transform quad)
        {
            if (quad == null) return 1f;
            return quad.lossyScale.y / Mathf.Max(Cfg.WanderCamSize, 0.001f);
        }

        void DrawProjectedBigBallText(Camera cam, float pxPerUnit)
        {
            var proj = _rig != null ? _rig.Projection : null;
            if (proj == null || proj.Quad == null) return;
            Transform quad = proj.Quad;

            Vector3 qs = cam.WorldToScreenPoint(quad.position);
            if (qs.z < 0f) return;
            float hw = quad.lossyScale.x * 0.5f * pxPerUnit;
            float hh = quad.lossyScale.y * 0.5f * pxPerUnit;
            if (hw < 2f || hh < 2f) return;

            float cx = qs.x;
            float cy = Screen.height - qs.y;    // IMGUI 的 y 从上往下
            Color faint = new Color(1f, 1f, 1f, Cfg.WanderCamAlpha);

            for (int f = 0; f < _world.Factions.Count; f++)
            {
                var fac = _world.Factions[f];
                for (int i = 0; i < fac.BigBalls.Count; i++)
                {
                    var b = fac.BigBalls[i];
                    if (b == null || b.Consumed) continue;

                    Vector2 uv;
                    if (!proj.TryViewportUV(b.transform.position, out uv)) continue;
                    if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) continue;

                    float sx = cx - hw + uv.x * 2f * hw;
                    float sy = cy - hh + (1f - uv.y) * 2f * hh;    // 贴图的 v 是自下而上

                    string txt = NumStr(600 + i, b.DisplayValue, NumSuffixed);
                    // ★★★ 直径要**乘投影的放大倍数**（字号 = 直径 × 系数）。
                    //   这块 quad 是把「`WanderCamSize`（7m）那么高的一片战场」铺满整个左栏
                    //   （左栏视野 ~27m 高），所以贴图里的东西被放大了 `zoneH / 7` 倍 ——
                    //   球看着很大，字却还按 1:1 算，就是用户第 102 轮报的
                    //   「摄像机里的文字大小与右边实际的文字大小不匹配」。
                    int size = BallTextSize(2f * b.Radius * pxPerUnit * ProjectionZoom(quad));
                    Color c = fac.TextColor;
                    c.a = faint.a;
                    float h = size * Cfg.HudLineHeightFactor;
                    DrawTextAt(sx, sy - h * 0.5f, 380f, txt, c, size, false, 0f, _labelPlain);
                }
            }
        }

        // ── ② 战场（右栏）：炮台 + 大球 ────────────────────────────

        void DrawBattleLayer(Camera cam)
        {
            if (cam == null) return;

            // ① 炮台那一圈文字（子弹数 / 名称三行 / 护盾）
            _hudTextRects.Clear();
            for (int f = 0; f < _world.Factions.Count; f++) DrawTurretPlate(cam, _world.Factions[f]);

            // ② ★ 再把大球在 IMGUI 里画一遍，压在这批文字之上。
            //    规格：大球从炮台文字上经过时，应该是**大球盖住文字**，不是文字盖住大球。
            //    HUD 文字全部走 OnGUI，而 IMGUI 永远画在所有相机之上 ——
            //    所以只能反过来：把大球也画进 IMGUI，垫在文字上面、在大球自己的数值之下。
            //    最终自下而上是：炮台文字 → 大球本体 → 大球自己的数值。
            DrawBigBallOverlay(cam);

            // ③ 大球自己的数值（必须在大球本体之上）
            for (int f = 0; f < _world.Factions.Count; f++)
            {
                var fac = _world.Factions[f];
                for (int i = 0; i < fac.BigBalls.Count; i++)
                {
                    var b = fac.BigBalls[i];
                    if (b == null || b.Consumed) continue;
                    // 规格：字号只看**大球本体的屏幕直径**（`Cfg.BallTextScale`）
                    float dia = b.Radius * 2f * PxPerUnit(cam);
                    string txt = NumStr(400 + i, b.DisplayValue, NumSuffixed);
                    int size = BallTextSize(dia);
                    DrawWorldText(cam, b.transform.position, txt, fac.TextColor, size, false,
                                  _labelPlain);
                }
            }

            // ④ ★★★ 近防炮的文字（用户：「护盾…需要相应的文字显示」）：
            //    两行 —— 上面是**盾**、下面是**散弹**。白字 + 黑描边，
            //    因为它站在敌方领土上，底色什么颜色都有可能。
            DrawCiwsText(cam);
        }

        /// <summary>近防炮：盾值 / 散弹值的文字（白字黑边）。</summary>
        void DrawCiwsText(Camera cam)
        {
            if (_world == null || _world.CiwsUnits == null) return;
            var list = _world.CiwsUnits;
            float ppu = PxPerUnit(cam);
            int size = Mathf.Clamp(Mathf.RoundToInt(Cfg.CiwsTextHeight * ppu),
                                   9, Cfg.TurretTextSizeMax);
            float lineH = size * 1.2f / Mathf.Max(ppu, 0.001f);
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c.Consumed || c.Flying) continue;
                Vector3 top = (Vector3)c.Pos + new Vector3(0f, Cfg.CiwsShieldRadius + Cfg.CiwsTextGap, 0f);
                DrawWorldText(cam, top, NumStr(700 + i * 2, c.Shield, NumSuffixed),
                              Color.white, size, true, _labelPlain);
                DrawWorldText(cam, top - new Vector3(0f, lineH, 0f),
                              NumStr(701 + i * 2, c.Ammo, NumSuffixed),
                              Color.white, size, true, _labelPlain);
            }
        }

        /// <summary>
        /// 大球的 IMGUI 副本：黑描边 + 阵营色本体，
        /// 尺寸/位置和世界里的那个 SpriteRenderer（order 60/61）完全一致
        /// （`BigBall.ApplyRadius`：本体 localScale = 半径×2，描边再乘 1 + BigBallOutlineFactor）。
        /// </summary>
        void DrawBigBallOverlay(Camera cam)
        {
            float pxPerUnit = PxPerUnit(cam);
            float rim = 1f + Cfg.BigBallOutlineFactor;
            Color prev = GUI.color;

            for (int f = 0; f < _world.Factions.Count; f++)
            {
                var fac = _world.Factions[f];
                for (int i = 0; i < fac.BigBalls.Count; i++)
                {
                    var b = fac.BigBalls[i];
                    if (b == null || b.Consumed) continue;

                    Vector3 sp = cam.WorldToScreenPoint(b.transform.position);
                    if (sp.z < 0f) continue;
                    float x = sp.x, y = Screen.height - sp.y;
                    float r = b.Radius * pxPerUnit;
                    if (r < 1f) continue;
                    // 只在战场取景框内画（别糊到左栏上去）
                    Rect view = new Rect(cam.rect.x * Screen.width, cam.rect.y * Screen.height,
                                         cam.rect.width * Screen.width, cam.rect.height * Screen.height);
                    if (x + r < view.xMin || x - r > view.xMax) continue;

                    // ★★ 只在**压着 HUD 文字**时才重画这一份副本（它的意义就是「球盖住字」）。
                    //   不压着文字时，世界里的那份 SpriteRenderer 看着完全一样 ——
                    //   省下每颗球每帧 2 次 `GUI.DrawTexture` + 两张半透明大图。
                    // ★★ 第 119 轮：**吸血球不画这层副本**。
                    //   吸血球的持续特效是 SpriteRenderer / TrailRenderer（order 62 / 63），
                    //   而 IMGUI 永远画在所有 Sprite 之上 —— 一旦这里把球面重画一遍，
                    //   球内所有特效就被整块盖掉，只剩球外那圈露着
                    //   （用户：「拖尾跑到了球的下方」）。
                    //   代价：吸血球压到炮台文字时，文字会盖住球（普通大球行为不变）。
                    if (!b.IsVampire && OverlapsHudText(x, y, r))
                    {
                        GUI.color = Color.black;
                        GUI.DrawTexture(new Rect(x - r * rim, y - r * rim, r * 2f * rim, r * 2f * rim),
                                        Primitives.Circle.texture);
                        // ★ 颜色必须和世界里那份 `BigBall._body` 走**同一条规则**
                        //   （`Palette.ShiftHsv(阵营色, 饱和度×0.7, V+0.3)`）——
                        //   这里原来直接用的 `fac.Color`（原色），于是球压到文字上的一瞬间
                        //   颜色会比世界里的那份更艳/更灰，看着像"跳"了一下。
                        GUI.color = Palette.ShiftHsv(fac.Color, Cfg.BigBallSaturation, Cfg.BigBallValueAdd);
                        GUI.DrawTexture(new Rect(x - r, y - r, r * 2f, r * 2f),
                                        Primitives.Circle.texture);
                    }

                    // ★ 规格（用户）：大球加点**光效**，不然一块平涂的颜色太单一。
                    //   两层软边环：贴着球体的一圈亮边（会呼吸）+ 外面一圈更淡的光晕。
                    //   （画在 IMGUI 里 —— 因为大球本体本来就被 IMGUI 的副本盖住了，
                    //     画在世界里是看不见的。）
                    //
                    // ★★ 中子星**不画**这层光晕 —— 用户报的「中子星上有一层白色的圆圈」
                    //    就是它：光晕是 `Lerp(阵营色, 白, 0.55)`，半径按 `r × 1.18 / 1.02`
                    //    跟着球走；大球半径大，光晕贴着边看不出来，可中子星半径只有 0.3m 出头，
                    //    这一圈就**悬在球外面**，看着就是一个白圈。
                    // ★ 屏幕上一颗几像素的小球，那两圈软边环根本看不见，却是两张半透明大图。
                    if (!b.IsNeutronStar && r >= Cfg.BigBallGlowMinScreenPx)
                    {
                        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Cfg.BigBallGlowPulseSpeed
                                                              + f * 1.7f);
                        Color glow = Color.Lerp(fac.Color, Color.white, 0.55f);

                        float hs = r * Cfg.BigBallHaloScale;
                        GUI.color = new Color(glow.r, glow.g, glow.b, Cfg.BigBallHaloAlpha);
                        GUI.DrawTexture(new Rect(x - hs, y - hs, hs * 2f, hs * 2f),
                                        Primitives.SoftRing.texture);

                        float gs = r * Cfg.BigBallGlowScale;
                        GUI.color = new Color(glow.r, glow.g, glow.b,
                                              Cfg.BigBallGlowAlpha * (0.6f + 0.4f * pulse));
                        GUI.DrawTexture(new Rect(x - gs, y - gs, gs * 2f, gs * 2f),
                                        Primitives.SoftRing.texture);
                    }
                }
            }
            GUI.color = prev;
        }

        /// <summary>
        /// 名称三段里的一段。★ 空的（null 或全空白）**整行不画**，也不占行高 ——
        /// 所以「头衔/称号留空」的阵营就只有一行名字，不会留出空档。
        /// </summary>
        float DrawNameLine(float x, float topY, float w, string text, Color color, int size)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return topY;
            // ★ 名称是从阵营表（FactionSetX.cs）手填的，**可能被改成中文** ——
            //   Rajdhani / Arimo 里没有汉字，照原样画就是一排方块，所以含中文时换 CJK 字体。
            //   全英文时仍走 Rajdhani Medium（规格：名称字体 = Rajdhani Medium）。
            GUIStyle style = (_hasCjk && HasCjk(text)) ? _labelCjk : _nameStyle;
            return DrawTextAt(x, topY, w, text, color, size, false,
                              size * Cfg.TurretNameLineAdvance, style);
        }

        /// <summary>
        /// 这段文字里有没有中 / 日 / 韩字（或全角标点）。有的话就不能用 Arimo / Rajdhani 画。
        /// 范围取 CJK 部首 … 统一汉字 + 全角标点 —— 够覆盖中文名字了，不必上 Unicode 全表。
        /// </summary>
        static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u2E80' && c <= '\u9FFF') return true;
                if (c >= '\uFF00' && c <= '\uFFEF') return true;
            }
            return false;
        }

        /// <summary>大球（屏幕圆心 x,y、半径 r）有没有压到这一帧的炮台文字？</summary>
        bool OverlapsHudText(float x, float y, float r)
        {
            for (int i = 0; i < _hudTextRects.Count; i++)
            {
                var t = _hudTextRects[i];
                if (x + r < t.xMin || x - r > t.xMax) continue;
                if (y + r < t.yMin || y - r > t.yMax) continue;
                return true;
            }
            return false;
        }

        void DrawTurretPlate(Camera cam, Faction f)
        {
            if (f.Turret == null) return;
            Vector3 sp = cam.WorldToScreenPoint(f.Turret.transform.position);
            if (sp.z < 0f) return;

            float pxPerUnit = PxPerUnit(cam);
            float x = sp.x;
            float y = Screen.height - sp.y;

            float corePx = Mathf.Max(f.Turret.CoreRadius * pxPerUnit, 8f);
            float shieldPx = Mathf.Max(f.Turret.Owner.ShieldRadius, Cfg.RadiusMin) * pxPerUnit;

            // 字号由**恒定的炮台半径**算出，所以不会随护盾量变化
            // 固定世界高度，不跟着炮台半径一起变小
            int resSize = Mathf.Clamp(Mathf.RoundToInt(Cfg.TurretTextHeight * pxPerUnit * Cfg.TurretTextSizeFactor),
                                      11, Cfg.TurretTextSizeMax);
            float w = 400f;

            // ★ 记下这片文字的屏幕矩形 —— 大球副本只在和它重叠时才重画（见 `_hudTextRects`）
            {
                float lineH = resSize * Cfg.HudLineHeightFactor;
                _hudTextRects.Add(new Rect(x - w * 0.5f, y - corePx - 7f * lineH,
                                           w, corePx * 2f + 14f * lineH));
            }

            // 上：子弹数量 —— 三位分级 + 换行，**贴着炮台**（不是贴着护盾圈）
            // ★ 规格：**子弹数为 0 时这块文字直接不显示**（不是画一个「0」出来）。
            if (ShowStat(f.Bullets))
                DrawStackedBlock(x, y - corePx - 2f, w, GroupsStr(f.Index, f.Bullets),
                                 f.TextColor, resSize, true);

            // 下：名称（头衔 / 名称 / 称号），居中、**紧贴着炮台**
            // ★ 第 94 轮：头衔和称号**可以留空**，留空的那一段整行不画；
            //   名称那一段永远用大一号的字（原来靠「固定三行、中间那行大」的写法）。
            // ★★ 名称字号用**固定世界高度**算，不跟炮台半径走 ——
            //   炮台半径缩了三轮，再乘它就一路缩到 9px 的钳位上了（用户第 100 轮：「文字变得很小」）。
            int baseSize = Mathf.Clamp(
                Mathf.RoundToInt(Cfg.TurretNameTextHeight * pxPerUnit), 9, Cfg.TurretTextSizeMax);
            int midSize = Mathf.Clamp(
                Mathf.RoundToInt(baseSize * Cfg.TurretNameMidScale), 10, Cfg.TurretTextSizeMax);

            // ★ 规格：名称三行 + 下面的护盾整体**往上拉一点**。
            //   单位取「炮台屏幕半径的比例」（第 6 条教训：别写死像素，否则改分辨率就错位）。
            // ★ 再往下让 `TurretNameDropEm`：头衔留空时最大那行紧贴炮台，用户要求往下挪一点。
            float belowTop = y + corePx * (1f - Cfg.TurretLowerBlockLiftFactor) + 2f
                             + baseSize * Cfg.TurretNameDropEm;

            if (f.Turret.IsDead)
            {
                float cd = Mathf.Max(0f, Cfg.RespawnDelay - (Time.time - f.DeathTime));
                // ★ 倒计时那行字**只在秒数变了**才重拼（复活窗口整整 60 秒，每帧都在画）
                string txt = CountdownStr(f.Index, Mathf.CeilToInt(cd));
                DrawTextAt(x, belowTop, w, txt, f.TextColor, resSize, true);
                return;
            }

            float below = belowTop;
            int idx = f.Index;
            if (idx >= 0 && idx < Cfg.TurretNames.Length)
            {
                var nd = Cfg.TurretNames[idx];
                below = DrawNameLine(x, below, w, nd.Title, f.TextColor, baseSize);
                // ★ 名字那行（最大的那行）再单独往下让 0.10 em。算下一行时把这点的偏移
                //   **减回去**，所以称号那行位置不动 —— 效果是「名字和称号之间的空隙缩小」。
                float midDrop = midSize * 0.10f;
                below = DrawNameLine(x, below + midDrop, w, nd.Name, f.TextColor, midSize) - midDrop;
                below = DrawNameLine(x, below, w, nd.Epithet, f.TextColor, baseSize);
            }

            // 再下：护盾量（规格：名称和护盾的位置换过来了）
            // ★★ 护盾的 y **不跟名称的实际行数走**：固定按「名称占了两行」的高度算
            //   （`Cfg.TurretShieldNameLines`；行序 = 头衔(base) / 名字(mid) / 称号(base)）。
            //   于是「只填了名字」和「头衔+名字+称号」的阵营，护盾都落在**同一条水平线**上。
            float shieldTop = belowTop;
            {
                int lines = Mathf.Clamp(Cfg.TurretShieldNameLines, 0, 3);
                for (int i = 0; i < lines; i++)
                    shieldTop += (i == 1 ? midSize : baseSize) * Cfg.TurretNameLineAdvance;
            }
            // ★ 用户：护盾数字再往上提一点点（名称块被 TurretNameDropEm 带下去过，这里单独收回来）
            shieldTop -= baseSize * Cfg.TurretShieldLiftEm;
            // ★ 用户：护盾数值**一直显示** —— 护盾为 0 时也写个「0」，不隐藏
            //   （子弹数那条「为 0 就不画」还留着，见 `ShowStat`）。
            DrawTextAt(x, shieldTop, w, NumStr(60 + f.Index, f.Shield, NumSuffixed), f.TextColor, resSize, true);
        }

        // ── ③ 左栏底部 8 格武器栏 ────────────────────────────────

        /// <summary>
        /// 武器栏白牌子相对格子边缘往里缩多少（**世界单位**）—— 自检和 <see cref="DrawWeaponBar"/>
        /// 共用这一个来源。★ 必须 ≥ 柱子半宽，否则柱子会被牌子盖住（用户报的「柱子不够粗」）。
        /// </summary>
        public static float WeaponPlatePostInsetWorld(float zonePx)
        {
            return Mathf.Max(Cfg.WeaponPostWidth * 0.5f, 2f / Mathf.Max(zonePx, 1e-4f));
        }

        void DrawWeaponBar()
        {
            float panelW = Screen.width * Cfg.LeftPanelViewportWidth;
            int n = Cfg.WeaponSlotCount;
            // ★ 白牌子要**正好压在增倍区里那 5 格的白色方块上**。
            //   以前是按「屏幕底部往上留 6px」放的，结果和场地里的白块错位，
            //   看起来就像「武器区下半部分伸到墙外面去了」。
            float zonePx = ZonePxPerUnit();
            float topY = Cfg.DY(Cfg.WeaponZoneTopPx);
            float botY = Cfg.DY(Cfg.WeaponZoneBottomPx);
            float plateH = Mathf.Max(Mathf.Abs(topY - botY) * zonePx, 20f);
            float y = ZoneLocalToPanelY((topY + botY) * 0.5f) - plateH * 0.5f;

            // ★ 规格：武器区必须在**墙里面**。以前按整个面板宽度等分，
            //   结果比增倍区的内腔还宽，压到墙上去了。现在把 5 格的边界投影到屏幕上。
            var edges = WeaponZone.SlotEdgesWorld();
            // ★ 复用同一个数组（每帧 new float[] 就是每帧往 GC 里扔垃圾）
            if (_weaponXs == null || _weaponXs.Length < n + 1) _weaponXs = new float[n + 1];
            var xs = _weaponXs;
            for (int e = 0; e <= n; e++) xs[e] = ZoneLocalToPanelX(edges[e]);

            _plate.fontSize = Mathf.Clamp(Mathf.RoundToInt(plateH * 0.34f), 10, 24);

            // ★★ 规格（用户第 90 轮）：武器区的柱子「不够粗」。
            //   柱子本身够粗（0.24m），但**白牌子画在 IMGUI 里、盖在一切之上**，
            //   以前只在格子之间留 4px 缝 → 柱子被遮得只剩中间一小条，看着比阻挡球细。
            //   现在按**柱子的半宽**往里缩，整根柱子露出来。
            float postInset = WeaponPlatePostInsetWorld(zonePx) * zonePx;

            for (int i = 0; i < n; i++)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                float slotW = x1 - x0;
                Rect plate = new Rect(x0 + postInset, y,
                                      Mathf.Max(slotW - postInset * 2f, 1f), plateH);
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.94f);
                GUI.DrawTexture(plate, Texture2D.whiteTexture);
                GUI.color = prev;

                // ★★ 规格（用户 图二）：这一格是 **x2 门**的话，格子里写的是 **x2 文字**
                //   （黑色，和乘法门上那套一样），不是图标。
                var def = Cfg.WeaponSlot(i);
                if (def.IsGate)
                {
                    if (_plateGate == null) _plateGate = new GUIStyle(_plate);
                    _plateGate.fontSize = Mathf.Clamp(Mathf.RoundToInt(plateH * 0.62f), 10, 40);
                    var prevC = GUI.color;
                    GUI.color = Color.black;
                    GUI.Label(plate, LabelAt(ref _slotLabels, i, def.Gate), _plateGate);
                    GUI.color = prevC;
                    continue;
                }

                // ★ 规格：格子里放**图标**（参考图上就是图标、没有文字）
                var icon = Primitives.WeaponIcon(def.Kind);
                if (icon != null && icon.texture != null)
                {
                    // 图标不是方的（散射是扁的、直射更扁）—— 等比塞进格子，别拉变形。
                    // ★★ 宽和高**各自卡上限**：以前两边都用 `min(格宽,格高)`，
                    //   于是「又扁又宽」的直射图标被**高度**卡住，只画到格子一半宽。
                    //   （方的图标（散射/齐射/护盾/大球）在宽格里本来就被高度卡住，结果不变。）
                    float maxW = plate.width * 0.88f, maxH = plate.height * 0.86f;
                    float iw = icon.texture.width, ih = icon.texture.height;
                    float k = Mathf.Min(maxW / iw, maxH / ih);
                    float w2 = iw * k, h2 = ih * k;
                    GUI.color = Color.black;
                    GUI.DrawTexture(new Rect(plate.center.x - w2 * 0.5f, plate.center.y - h2 * 0.5f,
                                             w2, h2), icon.texture);
                    GUI.color = Color.white;
                }
            }

            DrawWeaponCounters();
        }

        /// <summary>
        /// 武器区的计数器。★ 规格：**和乘法门的计数器同一个机制** ——
        ///   计数器挂在区上（四家共享）、数的是「这一格被选中过多少次」，
        ///   画法也照抄乘法门那套：每格正上方、白字、三位分级、**固定世界高度**。
        ///
        /// 所以这里走 `DrawWorldText`（世界坐标 + 增倍区相机），
        /// 而不是像以前那样在面板像素里摆 —— 以前那个还是**按当前选中的阵营**显示数值
        /// （护盾/球值），既不是计数器、也跟着阵营跑，和乘倍区完全不是一回事。
        /// </summary>
        void DrawWeaponCounters()
        {
            var zone = _world.Zone;
            if (zone == null || zone.Weapons == null || _rig.ZoneCam == null) return;

            int n = Cfg.WeaponSlotCount;
            var picks = zone.Weapons.PickCount;
            var edges = WeaponZone.SlotEdgesWorld();

            // 固定世界高度 → 和乘法门的计数器同一个大小规则
            float pxPerUnit = PxPerUnit(_rig.ZoneCam);
            int size = Mathf.Clamp(Mathf.RoundToInt(Cfg.WeaponCounterHeight * pxPerUnit), 8, Cfg.HudFontSizeMax);
            float cy = Cfg.DY(Cfg.WeaponZoneTopPx - Cfg.WeaponCounterGapPx);

            for (int i = 0; i < n && i < picks.Length && i + 1 < edges.Length; i++)
            {
                float cx = (edges[i] + edges[i + 1]) * 0.5f;
                Vector3 w = zone.transform.TransformPoint(new Vector3(cx, cy, 0f));
                DrawWorldText(_rig.ZoneCam, w, NumStr(40 + i, picks[i], NumGrouped), Color.white, size, false,
                              _labelPlain);
            }
        }

        /// <summary>
        /// **总值计数条**：增倍区最顶部那一排空心方框，一家一格，边框 = 阵营色。
        /// 数字 = 这颗阵营增倍球的**累计增益**（过门时「翻倍后 − 翻倍前」的和）。
        /// 4 家一行 4 格；8 / 16 家**往下铺**（每行仍 4 格）。
        /// </summary>
        void DrawGainBar()
        {
            if (_world == null || _rig == null || _rig.ZoneCam == null) return;
            int n = _world.Factions.Count;
            if (n <= 0) return;

            int cols = Mathf.Clamp(Mathf.Min(n, Cfg.GainBarColumns), 1, 4);
            float cellW = (Cfg.ZoneWidth - Cfg.GainBarGap * (cols + 1)) / cols;
            float topLocal = Cfg.ZoneHeight * 0.5f - Cfg.GainBarTopMargin;
            float leftLocal = -Cfg.ZoneWidth * 0.5f + Cfg.GainBarGap;

            // ★ 内条状态按阵营数扩容（阵营数由 FactionSet 决定，2/4/8/16 都可能）
            if (_gainFillW.Length != n)
            {
                _gainFillW = new float[n];
                _gainFillInit = new bool[n];
            }

            // ★ 分母 = 各家的**累计增益**之和
            //   这样内条和框里的数字用的是**同一个量**（GainTotal），
            //   四家的条加起来正好填满 100%；分母为 0（刚开局谁都没过门）时整个内条跳过。
            double totalGain = 0d;
            for (int i = 0; i < n; i++) totalGain += _world.Factions[i].GainTotal;

            if (_gainStyle == null)
            {
                _gainStyle = new GUIStyle(_labelPlain);
                _gainStyle.alignment = TextAnchor.MiddleLeft;   // 规格：文字居左
                _gainStyle.clipping = TextClipping.Clip;
                // ★ 字重：**Rajdhani SemiBold（600）** —— Medium(500) 和 Bold(700) 的正中间。
                //   IMGUI 的 FontStyle 只有 Normal/Bold 两档、且 Bold 是伪粗体（横向拉伸叠画，
                //   小字号发糊），所以「调字重」只能靠**换字体文件**，这一处就是。
                //   fontStyle 必须 Normal：SemiBold 本身就是真字重，再叠伪粗体只会更糊。
                //   资源名是**文件名**（注意是连字符，不是下划线）：
                //     Assets/Resources/Fonts/rb-semibold.ttf → "Fonts/rb-semibold"
                //   两级回退：SemiBold → Medium（和炮台名称同一套），缺文件也不会白屏。
                Font gainFont = Resources.Load<Font>("Fonts/rb-semibold");
                if (gainFont == null && _nameStyle != null) gainFont = _nameStyle.font;   // 退回 Medium
                if (gainFont != null) _gainStyle.font = gainFont;
                _gainStyle.fontStyle = FontStyle.Normal;
            }

            Color prev = GUI.color;
            for (int i = 0; i < n; i++)
            {
                var f = _world.Factions[i];
                int col = i % cols, row = i / cols;
                float lx = leftLocal + col * (cellW + Cfg.GainBarGap);
                float ly = topLocal - row * (Cfg.GainBarHeight + Cfg.GainBarGap);

                float x0 = ZoneLocalToPanelX(lx), x1 = ZoneLocalToPanelX(lx + cellW);
                float y0 = ZoneLocalToPanelY(ly), y1 = ZoneLocalToPanelY(ly - Cfg.GainBarHeight);
                Rect r = new Rect(x0, y0, x1 - x0, y1 - y0);
                if (r.width < 2f || r.height < 2f) continue;

                // ★ 只有一圈**细边框**（阵营色），内部**透明**（不填底色，能看见后面的场景）
                GUI.color = f.Color;
                float b = Mathf.Clamp(Mathf.Round(r.height * 0.04f), 1f, 3f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, b), Texture2D.whiteTexture);              // 上
                GUI.DrawTexture(new Rect(r.x, r.yMax - b, r.width, b), Texture2D.whiteTexture);       // 下
                GUI.DrawTexture(new Rect(r.x, r.y, b, r.height), Texture2D.whiteTexture);             // 左
                GUI.DrawTexture(new Rect(r.xMax - b, r.y, b, r.height), Texture2D.whiteTexture);      // 右

                // ── 内条：该阵营**累计增益的占比** ──
                //   这里同样：宽度 = 占比 × 框内宽，左端固定，所以只缓动宽度就够了（位置由左端推出）。
                if (i < _gainFillW.Length && totalGain > 0d)
                {
                    float targetW = (float)(f.GainTotal / totalGain) * r.width;
                    if (!_gainFillInit[i]) { _gainFillW[i] = targetW; _gainFillInit[i] = true; }
                    else _gainFillW[i] += (targetW - _gainFillW[i]) * Cfg.GainFillEase;

                    // 颜色：阵营色提到亮度 +0.2、alpha 0.2（原版 colorhsva(2) + 0.2, 0.2）
                    Color fc = f.Color;
                    float mx = Mathf.Max(fc.r, Mathf.Max(fc.g, fc.b));
                    if (mx > 0.0001f)
                    {
                        float k = Mathf.Min(1f / mx, (mx + 0.2f) / mx);   // 明度 +0.2（夹在 1 以内）
                        fc = new Color(fc.r * k, fc.g * k, fc.b * k, Cfg.GainFillAlpha);
                    }
                    else fc.a = Cfg.GainFillAlpha;

                    GUI.color = fc;
                    GUI.DrawTexture(new Rect(r.x, r.y, Mathf.Clamp(_gainFillW[i], 0f, r.width), r.height),
                                    Texture2D.whiteTexture);
                }

                // ★ GUI.color 会**乘到文字上**，所以画字之前必须还原成白色
                GUI.color = Color.white;
                _gainStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.72f), 8, 48);
                _gainStyle.normal.textColor = Color.white;      // 规格：白字
                // ★ 文字**居左**（离左边框留一点点空）
                float pad = Mathf.Max(4f, Mathf.Round(r.height * 0.22f));
                GUI.Label(new Rect(r.x + pad, r.y, Mathf.Max(r.width - pad * 2f, 1f), r.height),
                          GainText(f.GainTotal), _gainStyle);
            }
            GUI.color = prev;
        }

        /// <summary>
        /// 总值计数条的显示格式（用户规格）：**1e9 以下写整数**，以上写「xxxxM」且**不要小数点**。
        /// </summary>
        public static string GainText(double v)
        {
            if (double.IsNaN(v) || v <= 0d) return "0";
            if (v < 1e9) return v.ToString("0");
            return (v / 1e6).ToString("0") + "M";
        }

        /// <summary>左栏里 1 个世界单位 = 多少屏幕像素（纵向）。</summary>
        float ZonePxPerUnit()
        {
            float panelW = Screen.width * Cfg.LeftPanelViewportWidth;
            float panelH = Mathf.Max(Screen.height, 1f);
            float ortho = _rig.ZoneCam.orthographicSize;
            float halfW = Mathf.Max(ortho * (panelW / panelH), 0.001f);
            return panelW / (2f * halfW);
        }

        /// <summary>增倍区里某个局部 y 对应到屏幕上的 y（左上角为 0）。</summary>
        float ZoneLocalToPanelY(float localY)
        {
            float panelH = Mathf.Max(Screen.height, 1f);
            float ortho = Mathf.Max(_rig.ZoneCam.orthographicSize, 0.001f);
            return panelH * 0.5f - (localY / (2f * ortho)) * panelH;
        }

        /// <summary>增倍区里某个局部 x 对应到屏幕上的 x（左栏面板坐标系）。</summary>
        float ZoneLocalToPanelX(float localX)
        {
            float panelW = Screen.width * Cfg.LeftPanelViewportWidth;
            float panelH = Mathf.Max(Screen.height, 1f);
            float ortho = _rig.ZoneCam.orthographicSize;
            float halfW = Mathf.Max(ortho * (panelW / panelH), 0.001f);
            return panelW * 0.5f + (localX / (2f * halfW)) * panelW;
        }

        // ── ④ 调试统计（F1，默认关；放右上角不挡左栏）────────────

        void DrawStatsPanel()
        {
            float w = Mathf.Min(Screen.width * 0.34f, 620f);
            float line = _panel.fontSize * 1.4f;
            float h = line * (_world.Factions.Count + 3) + 10f;
            float x = Screen.width - w - 8f;

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x, 8f, w, h), Texture2D.whiteTexture);
            GUI.color = prev;

            _panelTitle.normal.textColor = Color.white;
            GUI.Label(new Rect(x + 8f, 10f, w - 16f, line),
                "F1  stats   |   " + Cfg.MapTexels + "x" + Cfg.MapTexels + " px map", _panelTitle);

            float total = Mathf.Max(_world.Field.TotalPixels, 1);
            float y = 10f + line;
            for (int i = 0; i < _world.Factions.Count; i++)
            {
                var f = _world.Factions[i];
                _panel.normal.textColor = f.Color;
                GUI.Label(new Rect(x + 8f, y, w - 16f, line), string.Format(
                    "{0}{1,-7} {2,5:0.0}%  px {3,-9}  bullets {4,-11}  shield {5,-9}  balls {6}{7}",
                    i == _focus ? ">" : " ", Palette.FactionNames[i],
                    f.TilesOwned * 100f / total, f.TilesOwned,
                    BigNumber.Grouped(f.Bullets), BigNumber.Suffixed(f.Shield),
                    f.LiveBallCount, f.Alive ? "" : "  [DOWN]"), _panel);
                y += line;
            }

            _panel.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            var bullets = _world.Bullets;
            int bigs = 0;
            for (int i = 0; i < _world.Factions.Count; i++) bigs += _world.Factions[i].BigBalls.Count;

            GUI.Label(new Rect(x + 8f, y, w - 16f, line), string.Format(
                // ★ FPS 用 Prof 里的**真实**帧时间算 —— 定步长模式下 Time.deltaTime 是钉死的，
                //   拿它算 FPS 会永远显示 60，正好骗了自己。
                "FPS {0,5:0.0}   bullets {1}/{2} (pool {3})   bigballs {4}   texels/frame {5}",
                1000f / Mathf.Max(Prof.FrameAvgMs, 1e-3f),
                bullets != null ? bullets.ActiveCount : 0, Cfg.BulletRenderCapacity,
                bullets != null ? bullets.CreatedCount : 0, bigs,
                _world.Field.LastUploadPixels), _panel);
            y += line;

            // ★ 「偶尔跳帧」定位用：帧时间的平均/**最大**（最大值才看得见尖峰），
            //   以及三个自己写的阶段各花了多少毫秒。哪一列跟着尖峰一起跳，就是它。
            GUI.Label(new Rect(x + 8f, y, w - 16f, line), string.Format(
                "frame ms  avg {0,5:0.0}  max {1,5:0.0}   |   pool {2,5:0.0}  view {3,5:0.0}  flush {4,5:0.0}  hud {5,5:0.0}   |   screen {6}x{7}",
                Prof.FrameAvgMs, Prof.FrameMaxMs, Prof.PoolMs, Prof.ViewMs, Prof.FlushMs, Prof.HudMs,
                Screen.width, Screen.height), _panel);
            y += line;
            // ★★ 「终极优化」专用：把**上面四项之外**的时间单独列出来。
            //   如果 `其他` 很大（比 pool/view/hud 都大），说明瓶颈在**我们自己的代码之外**
            //   —— Unity 的渲染 / 填充率 / IMGUI 后端 / 物理。那就该往**减少过绘制**那边想，
            //   而不是再去抠 pool 里的循环。
            GUI.Label(new Rect(x + 8f, y, w - 16f, line), string.Format(
                "其他  {0,5:0.0} ms（frame 减掉 pool/view/flush/hud，含 Unity 渲染+填充率+物理）",
                Prof.OtherMs), _panel);
            y += line;
            // ★★ 定步长（录屏）模式的状态。**只在 F1 面板里显示** ——
            //   不放世界里的常驻标记，免得录进视频里。
            GUI.Label(new Rect(x + 8f, y, w - 16f, line), Bootstrap.CaptureMode
                ? string.Format("录制  定步长 ON（F2 关）",
                                Cfg.CaptureFramerate)
                : "录制  定步长 OFF（F2 开）", _panel);
            y += line;
            GUI.Label(new Rect(x + 8f, y, w - 16f, line), string.Format(
                "pool split  step {0,5:0.0}  resolve {1,5:0.0}",
                Prof.StepMs, Prof.ResolveMs), _panel);
            y += line;
            GUI.Label(new Rect(x + 8f, y, w - 16f, line), string.Format(
                "hud split  zone {0,5:0.0}  battle {1,5:0.0}  weapon {2,5:0.0}  panel {3,5:0.0}",
                Prof.HudZoneMs, Prof.HudBattleMs, Prof.HudWeaponMs, Prof.HudPanelMs), _panel);
            y += line;
            // ★ 用户问「要不要提供设备信息」——直接印在面板上，他截图就行，不用去翻系统信息。
            //   `device` 是真正的 GPU；`full` 是显示器全分辨率。填充率瓶颈一眼就能看出来
            //   （screen 越大、其他那一栏越大，就越是填充率）。
            GUI.Label(new Rect(x + 8f, y, w - 16f, line), string.Format(
                "设备  {0}   |   显示器 {1}x{2}   |   Unity {3}",
                Prof.GpuName, Screen.currentResolution.width, Screen.currentResolution.height,
                Application.unityVersion), _panel);
            y += line;

            if (_world.WinnerIndex >= 0)
            {
                _panelTitle.normal.textColor = Palette.ColorOf(_world.WinnerIndex);
                GUI.Label(new Rect(x + 8f, y, w - 16f, line),
                    "WINNER: " + Palette.FactionNames[_world.WinnerIndex], _panelTitle);
            }
        }

        void DrawHint()
        {
            int size = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.017f), 9, 18);
            _counter.fontSize = size;
            _counter.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(10f, Screen.height - size * 1.5f, 1200f, size * 1.4f),
                "1-4 focus   K kill turret   B spawn 999M ball   V spawn 99M vampire   L +1 ball each   F1 stats   -/+ speed",
                _counter);
        }

        // ─────────────────────────────────────────────────────────────
        // 世界坐标 → 屏幕文字
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 求一个能让 text 塞进 maxWidthPx 的字号（先给上限，再按实测宽度收）。
        /// 用来让增倍球的数字永远不超出球体。
        /// </summary>
        /// <summary>
        /// 规格：文字总宽度**正好等于**目标宽度（大球 / 增倍球都这么算）。
        /// 以 **128px** 为基准量一次宽度，再按比例缩放字号。
        /// </summary>
        /// <summary>数出文字里「单位之前的数字位数」（逗号/小数点不算）。</summary>
        /// <summary>
        /// **子弹数**这块文字：为 0 时直接不显示（不是画一个孤零零的「0」占位置）。
        /// ★ 护盾**不用**这个 —— 用户要求护盾一直显示（0 也写出来）。
        /// ★ 判的是**显示出来会不会是 0**，不是「原始值 &gt; 0」：
        ///   子弹数会被涂地按小数扣（`Bullet.cs` 里 `Bullets -= painted × PixelCaptureCost`），
        ///   所以常常剩下 0.3 这种；`BigNumber.Grouped` 会四舍五入成 "0"。
        ///   这里用**同一个取整**来判断。
        /// </summary>
        public static bool ShowStat(double v)
        {
            if (double.IsNaN(v)) return false;
            if (v >= 9.0e18) return true;                  // 走后缀兜底，肯定不是 0
            return (long)System.Math.Round(v) > 0;         // 和 Grouped 的取整保持一致
        }

        /// <summary>把字号量化到 <see cref="Cfg.HudFontSizeStep"/> 的倍数（见那里的说明）。</summary>
        const int NumGrouped = 0;    // BigNumber.Grouped（子弹数那种三位分级）
        const int NumSuffixed = 1;   // BigNumber.Suffixed（大球 / 护盾）
        const int NumRounded = 2;    // BigNumber.SuffixedRounded（增倍球）

        /// <summary>
        /// 把一个数变成显示字符串，**带缓存**：同一个 slot 上值没变就直接返回上次那个
        /// （字符串只跟数值有关，所以按值命中是安全的）。
        /// ★ slot 由调用点自己分配，**同一个 slot 只能用同一种 kind**。
        /// </summary>
        string NumStr(int slot, double v, int kind)
        {
            if (_numV == null || slot >= _numV.Length)
            {
                int n = Mathf.Max(96, slot + 16);
                var nv = new double[n];
                var ns = new string[n];
                if (_numV != null)
                {
                    System.Array.Copy(_numV, nv, _numV.Length);
                    System.Array.Copy(_numS, ns, _numS.Length);
                }
                _numV = nv; _numS = ns;
            }
            if (_numS[slot] != null && _numV[slot] == v) return _numS[slot];
            _numV[slot] = v;
            _numS[slot] = kind == NumGrouped ? BigNumber.Grouped(v)
                       : kind == NumRounded ? BigNumber.SuffixedRounded(v)
                                            : BigNumber.Suffixed(v);
            return _numS[slot];
        }

        /// <summary>炮台上那份「三位一组」的多行数字（返回 string[]，同样按值缓存）。</summary>
        string[] GroupsStr(int slot, double v)
        {
            if (_groupsV == null)
            {
                _groupsV = new double[8];
                _groupsS = new string[8][];
            }
            if (slot >= _groupsV.Length)
            {
                int n = slot + 4;
                var nv = new double[n]; var ns = new string[n][];
                System.Array.Copy(_groupsV, nv, _groupsV.Length);
                System.Array.Copy(_groupsS, ns, _groupsS.Length);
                _groupsV = nv; _groupsS = ns;
            }
            if (_groupsS[slot] != null && _groupsV[slot] == v) return _groupsS[slot];
            _groupsV[slot] = v;   // ★ 存 double：子弹数会涨到千万级，float 的整数精度会不够
            _groupsS[slot] = BigNumber.Groups(v);
            return _groupsS[slot];
        }

        int[] _cdSecs;
        string[] _cdText;

        /// <summary>复活倒计时（「复活 12s」/「DOWN 12s」）——只在**秒数**变了才重拼。</summary>
        string CountdownStr(int slot, int secs)
        {
            if (_cdSecs == null) { _cdSecs = new int[8]; _cdText = new string[8]; }
            if (slot >= _cdSecs.Length)
            {
                System.Array.Resize(ref _cdSecs, slot + 4);
                System.Array.Resize(ref _cdText, slot + 4);
            }
            if (_cdText[slot] != null && _cdSecs[slot] == secs) return _cdText[slot];
            _cdSecs[slot] = secs;
            _cdText[slot] = _hasCjk ? "" + secs + "s" : "" + secs + "s";
            return _cdText[slot];
        }

        /// <summary>「x2 / x8 / x16」——**永远不变**，只拼一次。</summary>
        static string LabelAt(ref string[] cache, int slot, int mult)
        {
            if (cache == null) cache = new string[16];
            if (slot >= cache.Length) System.Array.Resize(ref cache, slot + 8);
            if (cache[slot] == null) cache[slot] = "x" + mult;
            return cache[slot];
        }

        static int QuantSize(int size)
        {
            int step = Mathf.Max(Cfg.HudFontSizeStep, 1);
            if (step == 1) return size;
            return Mathf.Max(step, Mathf.RoundToInt(size / (float)step) * step);
        }

        /// <summary>
        /// 大球 / 增倍球上那个数值的字号：**只看球的屏幕直径** ——
        /// `字号 = 直径(px) × Cfg.BallTextScale`，再夹到 `BallTextSizeMin..Max`。
        /// ★ 不再按数字位数分档，也不再"把文字撑满球宽"（见 <see cref="Cfg.BallTextScale"/>）。
        /// </summary>
        static int BallTextSize(float diameterPx)
        {
            return Mathf.Clamp(Mathf.RoundToInt(diameterPx * Cfg.BallTextScale),
                               Cfg.BallTextSizeMin, Cfg.BallTextSizeMax);
        }

        /// <summary>测量宽度用的复用对象（`GUIContent` 是 class，别每帧 new）。</summary>
        static readonly GUIContent _measureContent = new GUIContent();

        const int BASE_FONT = 128;

        static float PxPerUnit(Camera cam)
        {
            return cam.pixelHeight / Mathf.Max(2f * cam.orthographicSize, 0.001f);
        }

        /// <summary>
        /// 把几行数字**贴底 + 居中**叠起来（录像里子弹数量就是这样：2 / 531 / 742）。
        /// </summary>
        /// <summary>
        /// 炮台上**子弹数**那几行（三位一组、贴底居中叠起来）。
        /// ★★ 第 98 轮（用户）：「子弹文字显示的字间距和行间距稍微调小一点」
        ///   —— 行距由 `Cfg.TurretBulletLineFactor` 控制（原来写死 1.06），
        ///   字距由 `Cfg.TurretBulletCharFactor` 控制（IMGUI 没有 letter-spacing，
        ///   所以是**逐字符**按手动步进画的，见 `DrawTightText`）。
        /// </summary>
        void DrawStackedBlock(float cx, float bottomY, float w, string[] lines,
                              Color color, int size, bool outline)
        {
            if (lines == null || lines.Length == 0) return;
            float lineH = size * Cfg.TurretBulletLineFactor;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                float top = bottomY - (lines.Length - i) * lineH;
                DrawTightText(cx, top, w, lines[i], color, size, outline);
            }
        }

        /// <summary>单字符字符串的缓存（逐字符画的时候用，别每帧 `ToString` 造垃圾）。</summary>
        static readonly string[] _charStr = BuildCharStrs();
        static string[] BuildCharStrs()
        {
            var a = new string[128];
            for (int i = 0; i < a.Length; i++) a[i] = ((char)i).ToString();
            return a;
        }

        /// <summary>每个字符在 <see cref="BASE_FONT"/> 字号下的宽度（缓存的，只有十来个字符）。</summary>
        static readonly Dictionary<char, float> _charW = new Dictionary<char, float>();

        static float CharWidthAtBase(GUIStyle style, char c)
        {
            float w;
            if (_charW.TryGetValue(c, out w)) return w;
            style.fontSize = BASE_FONT;
            _measureContent.text = c < 128 ? _charStr[c] : c.ToString();
            w = Mathf.Max(style.CalcSize(_measureContent).x, 1f);
            _charW[c] = w;
            return w;
        }

        /// <summary>
        /// ★★ 逐字符画一行数字，字符间距 = 字体默认 × `Cfg.TurretBulletCharFactor`。
        ///   IMGUI 的 `GUIStyle` 没有 letter-spacing，只能自己按步进摆字符。
        ///   · 用**每个字符各自的宽度**（`CharWidthAtBase`，缓存）而不是均分 ——
        ///     这样逗号那种窄字符不会被撑开；
        ///   · `_label` 的锚点是 MiddleCenter + Overflow 裁剪，所以每个字符在一个
        ///     宽度 = 自己步进的矩形里居中画出来、而且**不会被裁掉**（收紧时允许压边）；
        ///   · 单字符字符串是缓存的，热路径零分配。
        /// </summary>
        void DrawTightText(float cx, float topY, float w, string text, Color color, int size, bool outline)
        {
            if (string.IsNullOrEmpty(text)) return;
            float factor = Cfg.TurretBulletCharFactor;
            if (factor >= 0.999f || text.Length <= 1)
            {
                DrawTextAt(cx, topY, w, text, color, size, outline);
                return;
            }

            var style = _label;
            int qs = QuantSize(size);
            float scale = qs / (float)BASE_FONT * factor;
            float total = 0f;
            for (int i = 0; i < text.Length; i++) total += CharWidthAtBase(style, text[i]);
            float blockW = total * scale;

            style.fontSize = qs;
            style.normal.textColor = color;
            float h = qs * Cfg.HudLineHeightFactor;
            float x = cx - blockW * 0.5f;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                float cw = CharWidthAtBase(style, c) * scale;
                GUI.Label(new Rect(x, topY, cw, h), c < 128 ? _charStr[c] : c.ToString(), style);
                x += cw;
            }
        }

        float DrawTextAt(float cx, float topY, float w, string text, Color color, int size, bool outline)
        {
            return DrawTextAt(cx, topY, w, text, color, size, outline, 0f);
        }

        float DrawTextAt(float cx, float topY, float w, string text, Color color, int size,
                         bool outline, float advance)
        {
            return DrawTextAt(cx, topY, w, text, color, size, outline, advance, null);
        }

        float DrawTextAt(float cx, float topY, float w, string text, Color color, int size,
                         bool outline, float advance, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return topY;
            if (style == null) style = _label;
            // ★ 所有 HUD 文字都从这一个口子出去 —— 字号在这里**统一量化**，
            //   动态字体图集就不会每帧为「差了 1 像素的新字号」重建一遍（见 Cfg.HudFontSizeStep）。
            size = QuantSize(size);
            style.fontSize = size;
            style.normal.textColor = color;
            float h = size * Cfg.HudLineHeightFactor;
            var r = new Rect(cx - w * 0.5f, topY, w, h);

            // 规格：文字不要白色描边。（顺带省掉每个文字 4 次 GUI.Label）
            if (false && outline && size >= 13)
            {
                style.normal.textColor = color == Color.black ? Color.white : Color.black;
                GUI.Label(new Rect(r.x - 1f, r.y, r.width, r.height), text, style);
                GUI.Label(new Rect(r.x + 1f, r.y, r.width, r.height), text, style);
                GUI.Label(new Rect(r.x, r.y - 1f, r.width, r.height), text, style);
                GUI.Label(new Rect(r.x, r.y + 1f, r.width, r.height), text, style);
                style.normal.textColor = color;
            }
            GUI.Label(r, text, style);
            return topY + (advance > 0f ? advance : h);
        }

        void DrawWorldText(Camera cam, Vector3 world, string text, Color color, int size, bool outline)
        {
            DrawWorldText(cam, world, text, color, size, outline, null);
        }

        void DrawWorldText(Camera cam, Vector3 world, string text, Color color, int size, bool outline,
                           GUIStyle style)
        {
            if (cam == null || string.IsNullOrEmpty(text)) return;

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f) return;

            float x = sp.x;
            float y = Screen.height - sp.y;

            Rect view = new Rect(cam.rect.x * Screen.width, cam.rect.y * Screen.height,
                                 cam.rect.width * Screen.width, cam.rect.height * Screen.height);
            if (x < view.xMin - 80f || x > view.xMax + 80f) return;
            if (y < view.yMin - 40f || y > view.yMax + 40f) return;

            float h = size * Cfg.HudLineHeightFactor;
            DrawTextAt(x, y - h * 0.5f, 380f, text, color, size, outline, 0f, style);
        }
    }
}
