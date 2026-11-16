using System.Collections;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// ★★ 设置面板（第 105 轮，用户要求）：**阵营数选择 / 阵营颜色 / 暂停继续**。
    ///
    /// 为什么是 IMGUI（`OnGUI`）而不是 UGUI：
    ///   · 整个工程的 HUD 就是 IMGUI 画的，没有 Canvas / Prefab / 场景依赖；
    ///   · 手机和 PC 用同一套代码，不需要做两套布局；
    ///   · `Cfg.EnableSettingsPanel = false` 时**连这个物体都不建**（见 Bootstrap），
    ///     关掉就是彻底不存在，HUD 一点不受影响。
    ///
    /// ★ 注意：HUD 的 `OnGUI` 有「非 Repaint 直接 return」的优化，
    ///   所以设置面板**必须自己实现 OnGUI** —— 挂到 HudOverlay 上会收不到点击。
    ///
    /// ★ 改颜色 / 改阵营数不会立刻生效：颜色缓存在 `Faction.Color`、战场像素数组按阵营数分配，
    ///   所以统一走「应用并重开」→ `Bootstrap.RebuildRoutine()`（拆掉 `[Game]` 重搭）。
    ///   本物体挂在 `[Settings]` 下，重开时自己不会被拆（见 `Bootstrap.TearDown`）。
    ///
    /// ★ 中文字形：IMGUI 默认字体没有中文，安卓上也没有系统字体可挑，
    ///   所以中文走 `Assets/Resources/Fonts/SettingsCjk.ttf`（HarmonyOS Sans SC 子集，
    ///   只留本文件里用到的字，几十 KB）。找不到才退回 Arimo（中文会变方框）。
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        /// <summary>一套阵营表最多几家 —— 直接从 `GameSettings` 取，别在两处各写一个 16。</summary>
        const int MaxFactions = GameSettings.MaxFactions;
        /// <summary>一行的高度（虚拟像素）。手指点滑条要留够高度，16 家时靠滚动条 + ▲▼。</summary>
        const float RowH = 36f;

        /// <summary>面板开着没有。</summary>
        bool _open;
        /// <summary>正在拆/建世界（这期间屏蔽输入，画个提示）。</summary>
        bool _restarting;

        /// <summary>工作副本：面板上拖滑条只改这里，点「应用」才落盘 + 重开。</summary>
        readonly Vector3[] _hsv = new Vector3[MaxFactions];
        readonly bool[] _black = new bool[MaxFactions];
        Vector2 _scroll;

        /// <summary>点「阵营数」后**先记下来**，等世界拆完再写进 GameSettings（见 RebuildRoutine）。</summary>
        int _pendingCount = -1;

        GUIStyle _title, _label, _small, _button, _center;
        Texture2D _white;

        static readonly Color Accent = new Color(0.30f, 0.78f, 0.56f, 1f);
        static readonly Color Warn = new Color(0.95f, 0.62f, 0.28f, 1f);
        static readonly Color Card = new Color(0.060f, 0.065f, 0.082f, 0.97f);

        // ─────────────────────────────────────────────────────────────
        // 生命周期
        // ─────────────────────────────────────────────────────────────

        void Awake()
        {
            _white = Texture2D.whiteTexture;
            Reload();
        }

        /// <summary>把「当前生效的设置」读进工作副本。</summary>
        void Reload()
        {
            int n = Mathf.Clamp(FactionSet.Count, 0, MaxFactions);
            for (int i = 0; i < MaxFactions; i++)
            {
                if (i < n)
                {
                    _hsv[i] = GameSettings.HsvOf(i);
                    _black[i] = GameSettings.TextBlackOf(i);
                }
                else
                {
                    _hsv[i] = Vector3.zero;
                    _black[i] = false;
                }
            }
            _scroll = Vector2.zero;
        }

        // ─────────────────────────────────────────────────────────────
        // 输入
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 键盘：Tab 开关面板，空格暂停/继续。
        /// ★ 在 OnGUI 的 KeyDown 里处理并 `Use()` —— 放在 Update 里的话，
        ///   如果某个按钮刚好有焦点，空格会**既暂停又点一下那个按钮**。
        /// </summary>
        void HandleKeys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || _restarting) return;

            if (e.keyCode == Cfg.SettingsPanelKey)
            {
                _open = !_open;
                e.Use();
            }
            else if (e.keyCode == Cfg.PauseKey)
            {
                TogglePause();
                e.Use();
            }
        }

        void TogglePause()
        {
            Bootstrap.SetPaused(!GameSettings.Paused);
        }

        // ─────────────────────────────────────────────────────────────
        // 画
        // ─────────────────────────────────────────────────────────────

        void OnGUI()
        {
            // 注：`Cfg.EnableSettingsPanel = false` 时 `Bootstrap` **根本不会建这个物体**，
            //     所以这里不用再判一次（常量判断会被编译器整段删掉，还多一条 unreachable 警告）。
            HandleKeys();
            EnsureStyles();

            // ★ 手机屏比 PC 小，按高度整体缩放（GUI.matrix 会连鼠标/触摸坐标一起换算）
            float scale = Mathf.Clamp(Screen.height / 900f, 0.80f, 2.60f);
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));
            int oldDepth = GUI.depth;
            GUI.depth = -10;                       // 画在 HUD 上面

            float W = Screen.width / scale;
            float H = Screen.height / scale;

            if (_restarting)
            {
                Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.82f));
                GUI.Label(new Rect(0f, H * 0.5f - 22f, W, 44f), "正在重开一局…", _center);
            }
            else if (_open)
            {
                DrawPanel(W, H);
            }
            else
            {
                DrawLauncher(W);
            }

            GUI.depth = oldDepth;
            GUI.matrix = oldMatrix;
        }

        /// <summary>
        /// 面板没开时：右上角入口按钮 + （暂停时）右上角的「已暂停」小条。
        /// ★ 暂停条**不要放顶边中间**：那里是增倍区顶上的四条增益计数条，
        ///   会被挡住；右上角只有战场边框，最空。
        /// </summary>
        void DrawLauncher(float W)
        {
            // ★★ 第 109 轮（用户）：「右上角有一个设置键，在 cfg 里写个控制开关的变量」——
            //   `Cfg.ShowSettingsButton = false` 就只藏这个按钮；键盘 Tab 仍然能开面板。
            //   （整个功能关掉是 `Cfg.EnableSettingsPanel`，那个连本物体都不建。）
            if (Cfg.ShowSettingsButton)
            {
                if (GUI.Button(new Rect(W - 100f, 10f, 90f, 30f), "设置", _button))
                    _open = true;
            }

            if (!GameSettings.Paused) return;

            Rect banner = new Rect(W - 350f, 46f, 340f, 34f);
            Fill(banner, new Color(0f, 0f, 0f, 0.78f));
            Stroke(banner, new Color(1f, 1f, 1f, 0.18f));
            GUI.Label(new Rect(banner.x + 14f, banner.y, 200f, banner.height), "已暂停", _label);
            if (GUI.Button(new Rect(banner.xMax - 96f, banner.y + 3f, 86f, 28f), "继续", _button))
                TogglePause();
        }

        void DrawPanel(float W, float H)
        {
            // 压暗背景（暂停时世界本来就是静止的，这层只是让面板更清楚）
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.55f));

            float pw = Mathf.Min(W * 0.86f, 760f);
            float ph = Mathf.Min(H * 0.88f, 760f);
            Rect p = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);
            Fill(p, Card);
            Stroke(p, new Color(1f, 1f, 1f, 0.22f));

            // ── 标题栏 ──
            GUI.Label(new Rect(p.x + 22f, p.y + 10f, 240f, 34f), "设置", _title);
            if (GUI.Button(new Rect(p.xMax - 100f, p.y + 12f, 78f, 30f), "关闭", _button))
                _open = false;

            float y = p.y + 58f;

            // ── 阵营数 ──
            GUI.Label(new Rect(p.x + 22f, y, 72f, 30f), "阵营数", _label);
            float x = p.x + 96f;
            for (int i = 0; i < GameSettings.FactionCounts.Length; i++)
            {
                int n = GameSettings.FactionCounts[i];
                bool on = n == FactionSet.Count;
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = on ? Accent : Color.white;
                if (GUI.Button(new Rect(x, y + 1f, 54f, 28f), n.ToString(), _button))
                    OnCountClicked(n);
                GUI.backgroundColor = oldBg;
                x += 60f;
            }
            GUI.Label(new Rect(x + 10f, y, pw - (x - p.x) - 20f, 30f), "切换会重开一局", _small);

            // ── 暂停 / 继续 ──
            y += 42f;
            Color oldBg2 = GUI.backgroundColor;
            GUI.backgroundColor = GameSettings.Paused ? Accent : Warn;
            if (GUI.Button(new Rect(p.x + 22f, y, 108f, 30f),
                           GameSettings.Paused ? "继续" : "暂停", _button))
                TogglePause();
            GUI.backgroundColor = oldBg2;
            GUI.Label(new Rect(p.x + 144f, y, pw - 170f, 30f),
                      GameSettings.Paused ? "已暂停（空格 = 继续）" : "运行中（空格 = 暂停）",
                      _small);

            // ── 颜色标题 + 翻页按钮 ──
            y += 44f;
            GUI.Label(new Rect(p.x + 22f, y, 160f, 26f), "阵营颜色", _label);
            GUI.Label(new Rect(p.x + 112f, y + 2f, 320f, 24f), "改完点右下角「应用并重开」", _small);

            if (GUI.Button(new Rect(p.xMax - 118f, y - 1f, 42f, 26f), "▲", _button))
                _scroll.y = Mathf.Max(0f, _scroll.y - RowH * 4f);
            if (GUI.Button(new Rect(p.xMax - 72f, y - 1f, 42f, 26f), "▼", _button))
                _scroll.y += RowH * 4f;

            // ── 颜色列表（可滚） ──
            y += 30f;
            Rect list = new Rect(p.x + 16f, y, pw - 32f, Mathf.Max(80f, p.yMax - y - 66f));
            Fill(list, new Color(0f, 0f, 0f, 0.22f));

            int count = Mathf.Clamp(FactionSet.Count, 0, MaxFactions);
            float vh = Mathf.Max(list.height, count * RowH + 6f);
            // ★ 竖滚动条**强制显示**：安卓没有滚轮，只能拖条（再配合上面的 ▲▼）
            _scroll = GUI.BeginScrollView(list, _scroll,
                                         new Rect(0f, 0f, list.width - 22f, vh), false, true);
            for (int i = 0; i < count; i++)
                DrawRow(new Rect(0f, i * RowH + 3f, list.width - 26f, RowH - 4f), i);
            GUI.EndScrollView();

            // ── 底部按钮 ──
            float by = p.yMax - 54f;
            bool dirty = IsDirty();
            GUI.Label(new Rect(p.x + 22f, by + 8f, 240f, 26f),
                      dirty ? "有未应用的改动" : "颜色与当前一致", _small);

            if (GUI.Button(new Rect(p.xMax - 316f, by, 140f, 36f), "应用并重开", _button))
                ApplyAndRestart();
            if (GUI.Button(new Rect(p.xMax - 168f, by, 148f, 36f), "恢复默认颜色", _button))
                ResetColorsAndRestart();
        }

        /// <summary>一个阵营一行：色块 / 名字 / H S V / 黑字白字 / 还原。</summary>
        void DrawRow(Rect r, int i)
        {
            if ((i & 1) == 0) Fill(r, new Color(1f, 1f, 1f, 0.035f));

            Rect sw = new Rect(r.x + 6f, r.y + (r.height - 18f) * 0.5f, 26f, 18f);
            Fill(sw, Display(_hsv[i]));
            Stroke(sw, new Color(0f, 0f, 0f, 0.35f));

            float x = r.x + 40f;
            GUI.Label(new Rect(x, r.y, 58f, r.height), Palette.FactionNames[i], _small);
            x += 62f;

            // 右边留给两个按钮
            float avail = r.xMax - 8f - x;
            float tail = 104f;
            float triW = Mathf.Max(96f, (avail - tail) / 3f);

            Vector3 v = _hsv[i];
            v.x = Axis(r, ref x, triW, "H", v.x, 0f, Cfg.ColorHueMax, 0);
            v.y = Axis(r, ref x, triW, "S", v.y, 0f, Cfg.ColorSvMax, 2);
            v.z = Axis(r, ref x, triW, "V", v.z, 0f, Cfg.ColorSvMax, 2);
            _hsv[i] = v;

            x += 6f;
            bool black = _black[i];
            Color oldContent = GUI.contentColor;
            GUI.backgroundColor = black ? Color.white : new Color(0.16f, 0.16f, 0.20f);
            GUI.contentColor = black ? Color.black : Color.white;
            if (GUI.Button(new Rect(x, r.y + 3f, 46f, r.height - 6f), black ? "黑字" : "白字", _button))
                _black[i] = !black;
            GUI.contentColor = oldContent;
            GUI.backgroundColor = Color.white;

            x += 52f;
            if (GUI.Button(new Rect(x, r.y + 3f, 46f, r.height - 6f), "还原", _button))
            {
                _hsv[i] = GameSettings.BaseHsv(i);
                _black[i] = FactionSet.Get(i).TextBlack;
            }
        }

        /// <summary>H/S/V 三个滑条里的一条。返回滑条的新值。</summary>
        float Axis(Rect r, ref float x, float triW, string name,
                   float val, float min, float max, int decimals)
        {
            GUI.Label(new Rect(x, r.y, 14f, r.height), name, _small);
            x += 16f;

            float sw = Mathf.Max(24f, triW - 56f);
            val = GUI.HorizontalSlider(new Rect(x, r.y + (r.height - 20f) * 0.5f, sw, 20f),
                                       val, min, max);
            x += sw + 4f;

            GUI.Label(new Rect(x, r.y, 36f, r.height),
                      decimals == 0 ? Mathf.RoundToInt(val).ToString() : val.ToString("F2"),
                      _small);
            x += 36f;
            return val;
        }

        // ─────────────────────────────────────────────────────────────
        // 动作
        // ─────────────────────────────────────────────────────────────

        void OnCountClicked(int n)
        {
            if (_restarting || n == FactionSet.Count) return;
            ApplyColorEdits();                 // ★ 先按**旧**阵营数把颜色落盘
            _pendingCount = n;                 // 阵营数等拆完旧世界再换（见 Bootstrap.RebuildRoutine）
            StartRestart();
        }

        void ApplyAndRestart()
        {
            if (_restarting) return;
            ApplyColorEdits();
            StartRestart();
        }

        void ResetColorsAndRestart()
        {
            if (_restarting) return;
            GameSettings.ResetColors();        // 全部回阵营表原值（文字黑白也一起）
            Reload();
            StartRestart();
        }

        /// <summary>
        /// 把工作副本落盘。
        /// ★ 关键：工作副本如果**正好等于阵营表原值**（点了「还原」，或者本来就没动过），
        ///   就把覆盖**删掉**，而不是写一个「和原值一样的覆盖」——
        ///   后者会把阵营表**钉死**：以后再去 `FactionSetX.cs` 改颜色就不生效了。
        /// </summary>
        void ApplyColorEdits()
        {
            int n = Mathf.Clamp(FactionSet.Count, 0, MaxFactions);
            for (int i = 0; i < n; i++)
            {
                Vector3 baseHsv = GameSettings.BaseHsv(i);
                if (Approx(_hsv[i], baseHsv))
                {
                    if (GameSettings.HasHsv(i)) GameSettings.ClearHsv(i);
                }
                else if (!Approx(_hsv[i], GameSettings.HsvOf(i)))
                {
                    GameSettings.SetHsv(i, _hsv[i]);
                }

                bool baseBlack = FactionSet.Get(i).TextBlack;
                if (_black[i] == baseBlack)
                {
                    if (GameSettings.HasTextBlack(i)) GameSettings.ClearTextBlack(i);
                }
                else if (_black[i] != GameSettings.TextBlackOf(i))
                {
                    GameSettings.SetTextBlack(i, _black[i]);
                }
            }
        }

        bool IsDirty()
        {
            int n = Mathf.Clamp(FactionSet.Count, 0, MaxFactions);
            for (int i = 0; i < n; i++)
            {
                if (!Approx(_hsv[i], GameSettings.HsvOf(i))) return true;
                if (_black[i] != GameSettings.TextBlackOf(i)) return true;
            }
            return false;
        }

        void StartRestart()
        {
            if (_restarting) return;
            _restarting = true;
            StartCoroutine(RestartRoutine());
        }

        IEnumerator RestartRoutine()
        {
            int count = _pendingCount;
            _pendingCount = -1;
            yield return StartCoroutine(Bootstrap.RebuildRoutine(count));
            Reload();
            _restarting = false;
            _open = false;                     // 重开完把面板收起来，直接看新局
        }

        // ─────────────────────────────────────────────────────────────
        // 小工具
        // ─────────────────────────────────────────────────────────────

        void EnsureStyles()
        {
            if (_label != null) return;

            // 兜底：万一 Awake 没跑到（物体被建在 inactive 的父节点下），
            // 这里补一次，否则每帧 `GUI.DrawTexture(rect, null)` 刷错误。
            if (_white == null) _white = Texture2D.whiteTexture;

            Font font = Resources.Load<Font>("Fonts/SettingsCjk");
            if (font == null) font = Resources.Load<Font>("Fonts/Arimo");
            if (font == null) font = GUI.skin.font;

            _title = New(font, 26, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold, false);
            _label = New(font, 18, TextAnchor.MiddleLeft, new Color(0.92f, 0.94f, 0.98f), FontStyle.Normal, false);
            _small = New(font, 15, TextAnchor.MiddleLeft, new Color(0.78f, 0.80f, 0.86f), FontStyle.Normal, false);
            _button = New(font, 17, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal, true);
            _center = New(font, 22, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal, false);
        }

        static GUIStyle New(Font font, int size, TextAnchor anchor, Color color,
                            FontStyle fs, bool button)
        {
            var s = new GUIStyle(button ? GUI.skin.button : GUI.skin.label);
            s.font = font;
            s.fontSize = size;
            s.fontStyle = fs;
            s.alignment = anchor;
            s.clipping = TextClipping.Overflow;
            s.normal.textColor = color;
            s.hover.textColor = color;
            s.active.textColor = color;
            s.focused.textColor = color;
            if (button)
            {
                s.padding = new RectOffset(6, 6, 2, 2);
                s.margin = new RectOffset(0, 0, 0, 0);
            }
            return s;
        }

        static Color Display(Vector3 hsv)
        {
            Color c = Palette.HsvToRgb(hsv.x, hsv.y, hsv.z);
            c.r = Mathf.Clamp01(c.r);
            c.g = Mathf.Clamp01(c.g);
            c.b = Mathf.Clamp01(c.b);
            c.a = 1f;
            return c;
        }

        static bool Approx(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.01f
                && Mathf.Abs(a.y - b.y) < 0.001f
                && Mathf.Abs(a.z - b.z) < 0.001f;
        }

        void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        void Stroke(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }
    }
}
