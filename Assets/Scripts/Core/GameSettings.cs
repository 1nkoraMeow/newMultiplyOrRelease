using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// ★★ 运行时可变的对局设置 —— 设置面板改的就是这里。
    ///
    /// 为什么不放在 <see cref="Cfg"/> 里：`Cfg` 绝大部分是**编译期常量**（`const`），
    /// 运行期改不了，而且阵营数一改就得重建整个世界。所以这里单独放一组
    /// 「可以改、改了要重开」的设置，`Cfg` 那边只保留只读的派生值
    /// （`Cfg.FactionCount` / `ShieldVisualScale` 这些已经改成读这里的属性）。
    ///
    /// 为什么是静态的：换阵营数 = 销毁 `[Game]` 整棵树再重搭，
    /// 挂在世界里的东西活不过这一次重搭，设置必须活在静态字段上。
    ///
    /// 顺手存 `PlayerPrefs`：下次启动还是上次那套（`Persist` 可以关掉）。
    /// ★ 键名带 `mor.set.` 前缀，和别的工程不会撞。
    ///
    /// ★★ 颜色 / 文字黑白的覆盖**按阵营数分开存**（`mor.set.hsv.h.<阵营数>.<下标>`）：
    ///   四张阵营表本来就各自完整、互不覆盖，所以「4 家版调过的颜色」不该串到 8 家版去 ——
    ///   那两家除了下标一样以外没有任何关系（名字、出生角、全套配色都不一样）。
    ///   下标见 <see cref="SlotOf"/>，键名见 <see cref="HsvKey"/>。
    ///
    /// ★★ 第 110 轮：每条覆盖还记着**写入时阵营表里的原值**（`mor.set.base.*`）。
    ///   启动时拿它和现在表里的值比，**对不上就说明表被改过 → 旧覆盖自动作废**，
    ///   直接用表里的新值。这样改 `FactionSetX.cs` 一定生效，不会被以前在面板里
    ///   调过的颜色「钉死」。（旧版存档没有这组键，按「无法确认」处理，同样作废。）
    /// </summary>
    public static class GameSettings
    {
        /// <summary>可选阵营数 —— 每一项对应一套现成的阵营表（FactionSet2/4/8/16）。</summary>
        public static readonly int[] FactionCounts = { 2, 4, 8, 16 };

        /// <summary>一套阵营表最多几家（表里最大就是 16）。</summary>
        public const int MaxFactions = 16;

        /// <summary>
        /// 要不要记到 `PlayerPrefs`（关掉 = 每次启动都回默认）。
        /// ★ `static readonly` 而不是 `const`：写成 `const true` 的话，
        ///   下面 `if (!Persist) return;` 会被编译器判成「永远不成立」，
        ///   整个方法体变成 unreachable，白多两条警告。
        /// </summary>
        public static readonly bool Persist = true;

        const string KeyCount = "mor.set.count";
        /// <summary>★ 后面还要拼「阵营数.下标」，见 <see cref="HsvKey"/>。</summary>
        const string KeyH = "mor.set.hsv.h.";
        const string KeyS = "mor.set.hsv.s.";
        const string KeyV = "mor.set.hsv.v.";
        const string KeyText = "mor.set.text.";
        /// <summary>
        /// 覆盖写入时**当时的出厂值**（阵营表里的原值）。
        /// ★ 加载时拿它和现在表里的值比：**对不上就说明表被改过 → 旧覆盖作废**。
        ///   这样「改 FactionSetX.cs 一定生效」，不会被以前在面板里调过的颜色钉死。
        /// </summary>
        const string KeyBaseH = "mor.set.base.h.";
        const string KeyBaseS = "mor.set.base.s.";
        const string KeyBaseV = "mor.set.base.v.";
        const string KeyBaseText = "mor.set.base.text.";

        /// <summary>当前阵营数（永远是 <see cref="FactionCounts"/> 里的一个）。</summary>
        public static int FactionCount { get; private set; }

        /// <summary>暂停中。真正的暂停由 <see cref="Bootstrap.SetPaused"/> 落到 `Time.timeScale`。</summary>
        public static bool Paused;

        /// <summary>
        /// 设置**版本号**：任何一项改了就 +1。`Palette` 用这个判断缓存要不要重建
        /// （颜色数组是按阵营数 + 版本号缓存的，别每帧重算）。
        /// </summary>
        public static int Version { get; private set; }

        // ── 覆盖表：槽位 × 阵营下标 ──────────────────────────────
        // 槽位 = `FactionCounts` 里的下标（2家 / 4家 / 8家 / 16家各一槽），
        // 扁平成 `slot * MaxFactions + 阵营下标`。
        static readonly int Slots = FactionCounts.Length;
        static readonly bool[] _hasHsv = new bool[Slots * MaxFactions];
        static readonly Vector3[] _hsv = new Vector3[Slots * MaxFactions];
        /// <summary>覆盖写入时阵营表里的原值（见 <see cref="KeyBaseH"/>）。</summary>
        static readonly Vector3[] _baseHsv = new Vector3[Slots * MaxFactions];
        /// <summary>-1 = 没改过（用阵营表原值），0 = 白字，1 = 黑字。</summary>
        static readonly sbyte[] _textBlack = new sbyte[Slots * MaxFactions];
        /// <summary>文字黑白覆盖写入时阵营表里的原值（-1 = 没记）。</summary>
        static readonly sbyte[] _baseTextBlack = new sbyte[Slots * MaxFactions];

        static GameSettings()
        {
            for (int i = 0; i < _textBlack.Length; i++)
            {
                _textBlack[i] = -1;
                _baseTextBlack[i] = -1;
            }
            FactionCount = FactionSet.DefaultActive;
            Load();
        }

        // ── 槽位 / 键名 ───────────────────────────────────────────

        /// <summary>
        /// 阵营数 → 覆盖槽位（0..3，对应 <see cref="FactionCounts"/> 的下标）。
        /// 传非法值先按 <see cref="ClampCount"/> 归一（3 → 4 那一槽）。
        /// </summary>
        public static int SlotOf(int count)
        {
            for (int i = 0; i < FactionCounts.Length; i++)
                if (FactionCounts[i] == count) return i;
            return SlotOf(ClampCount(count));
        }

        /// <summary>当前阵营数对应的槽位。</summary>
        public static int Slot { get { return SlotOf(FactionCount); } }

        /// <summary>「当前槽位 + 阵营下标」→ 扁平数组下标。</summary>
        static int Idx(int i) { return Slot * MaxFactions + i; }

        static bool Ok(int i) { return i >= 0 && i < MaxFactions; }

        /// <summary>
        /// 存档键：`mor.set.hsv.h.<阵营数>.<下标>`。
        /// ★ 段里带阵营数是**故意的** —— 四种模式各存各的，改 4 家版不影响 8 家版。
        /// （`prefix` 用 <see cref="KeyH"/> 那组常量。）
        /// </summary>
        public static string HsvKey(string prefix, int count, int i)
        {
            return prefix + count + "." + i;
        }

        // ── 阵营数 ────────────────────────────────────────────────

        public static bool IsValidCount(int n)
        {
            for (int i = 0; i < FactionCounts.Length; i++)
                if (FactionCounts[i] == n) return true;
            return false;
        }

        /// <summary>
        /// 夹到**最近的合法档位**（传 3 会得到 4，传 12 会得到 16）。
        /// ★ 正好卡在中间时取**大一档**（`d &lt;= bestD`，不是 `d &lt; bestD`）——
        ///   这样 2/4 之间的 3 → 4、8/16 之间的 12 → 16，
        ///   和「默认 4 家」「加人优先」的直觉一致；写 `<` 的话 3 会掉回 2。
        /// </summary>
        public static int ClampCount(int n)
        {
            int best = FactionCounts[0];
            int bestD = int.MaxValue;
            for (int i = 0; i < FactionCounts.Length; i++)
            {
                int d = Mathf.Abs(FactionCounts[i] - n);
                if (d <= bestD) { bestD = d; best = FactionCounts[i]; }
            }
            return best;
        }

        /// <summary>切阵营数。返回 true 表示**确实变了**（调用方该重开一局）。</summary>
        public static bool SetFactionCount(int n)
        {
            n = ClampCount(n);
            if (n == FactionCount) return false;
            FactionCount = n;
            Version++;
            Save();
            return true;
        }

        // ── 颜色 ──────────────────────────────────────────────────

        /// <summary>**当前阵营数**下，这个阵营的颜色被用户改过没有。</summary>
        public static bool HasHsv(int i)
        {
            return Ok(i) && _hasHsv[Idx(i)];
        }

        /// <summary>阵营表里的原色（HSV，H 度 / S / V；V 允许 &gt; 1）。</summary>
        public static Vector3 BaseHsv(int i)
        {
            return FactionSet.Get(i).Hsv;
        }

        /// <summary>
        /// 当前生效的颜色：改过用改过的，没改过用阵营表原值。
        /// ★ 表改过之后（`Load` 里发现出厂值对不上），覆盖会被丢掉，这里自然回落到表里的新值。
        /// </summary>
        public static Vector3 HsvOf(int i)
        {
            if (HasHsv(i)) return _hsv[Idx(i)];
            return FactionSet.Get(i).Hsv;
        }

        public static void SetHsv(int i, Vector3 v)
        {
            if (!Ok(i)) return;
            int k = Idx(i);
            _hsv[k] = v;
            _hasHsv[k] = true;
            // ★ 记下**写覆盖时**的出厂值：以后阵营表被改动，这条覆盖就作废（见 Load）。
            _baseHsv[k] = BaseHsv(i);
            Version++;
            Save();
        }

        public static void ClearHsv(int i)
        {
            if (!Ok(i)) return;
            int k = Idx(i);
            _hasHsv[k] = false;
            _baseHsv[k] = default(Vector3);
            Version++;
            Save();
        }

        /// <summary>这个阵营的文字是不是黑字（改过用改过的，没改过用阵营表原值）。</summary>
        public static bool TextBlackOf(int i)
        {
            if (Ok(i) && _textBlack[Idx(i)] >= 0) return _textBlack[Idx(i)] == 1;
            return FactionSet.Get(i).TextBlack;
        }

        public static bool HasTextBlack(int i)
        {
            return Ok(i) && _textBlack[Idx(i)] >= 0;
        }

        public static void SetTextBlack(int i, bool black)
        {
            if (!Ok(i)) return;
            int k = Idx(i);
            _textBlack[k] = (sbyte)(black ? 1 : 0);
            _baseTextBlack[k] = (sbyte)(FactionSet.Get(i).TextBlack ? 1 : 0);
            Version++;
            Save();
        }

        /// <summary>文字黑白还原成阵营表原值（去掉覆盖）。</summary>
        public static void ClearTextBlack(int i)
        {
            if (!Ok(i)) return;
            int k = Idx(i);
            _textBlack[k] = -1;
            _baseTextBlack[k] = -1;
            Version++;
            Save();
        }

        /// <summary>
        /// 全部恢复成阵营表里的原色（文字黑白也一起）。
        /// ★ 清的是**所有四套模式**的覆盖 —— 按钮叫「恢复默认颜色」，
        ///   那就该是全部回出厂，而不是只清当前这套。
        /// </summary>
        public static void ResetColors()
        {
            ClearAllOverrides();
            Version++;
            Save();
        }

        static void ClearAllOverrides()
        {
            for (int i = 0; i < _hsv.Length; i++)
            {
                _hasHsv[i] = false;
                _baseHsv[i] = default(Vector3);
                _textBlack[i] = -1;
                _baseTextBlack[i] = -1;
            }
        }

        // ── 存取 ──────────────────────────────────────────────────

        public static void Save()
        {
            if (!Persist) return;
            PlayerPrefs.SetInt(KeyCount, FactionCount);

            for (int s = 0; s < Slots; s++)
            {
                int count = FactionCounts[s];
                int b = s * MaxFactions;
                for (int i = 0; i < MaxFactions; i++)
                {
                    int k = b + i;
                    string kh = HsvKey(KeyH, count, i);
                    string ks = HsvKey(KeyS, count, i);
                    string kv = HsvKey(KeyV, count, i);
                    string kt = HsvKey(KeyText, count, i);

                    if (_hasHsv[k])
                    {
                        PlayerPrefs.SetFloat(kh, _hsv[k].x);
                        PlayerPrefs.SetFloat(ks, _hsv[k].y);
                        PlayerPrefs.SetFloat(kv, _hsv[k].z);
                        // ★ 一起存「写覆盖时的出厂值」，加载时用它判断表有没有被改过
                        PlayerPrefs.SetFloat(HsvKey(KeyBaseH, count, i), _baseHsv[k].x);
                        PlayerPrefs.SetFloat(HsvKey(KeyBaseS, count, i), _baseHsv[k].y);
                        PlayerPrefs.SetFloat(HsvKey(KeyBaseV, count, i), _baseHsv[k].z);
                    }
                    else
                    {
                        PlayerPrefs.DeleteKey(kh);
                        PlayerPrefs.DeleteKey(ks);
                        PlayerPrefs.DeleteKey(kv);
                        PlayerPrefs.DeleteKey(HsvKey(KeyBaseH, count, i));
                        PlayerPrefs.DeleteKey(HsvKey(KeyBaseS, count, i));
                        PlayerPrefs.DeleteKey(HsvKey(KeyBaseV, count, i));
                    }

                    string kbt = HsvKey(KeyBaseText, count, i);
                    if (_textBlack[k] >= 0)
                    {
                        PlayerPrefs.SetInt(kt, _textBlack[k]);
                        if (_baseTextBlack[k] >= 0) PlayerPrefs.SetInt(kbt, _baseTextBlack[k]);
                        else PlayerPrefs.DeleteKey(kbt);
                    }
                    else
                    {
                        PlayerPrefs.DeleteKey(kt);
                        PlayerPrefs.DeleteKey(kbt);
                    }
                }
            }
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            if (!Persist) return;

            FactionCount = ClampCount(PlayerPrefs.GetInt(KeyCount, FactionCount));

            bool dirty = false;
            for (int s = 0; s < Slots; s++)
            {
                int count = FactionCounts[s];
                var table = FactionSet.TableOf(count);
                int b = s * MaxFactions;
                for (int i = 0; i < MaxFactions; i++)
                {
                    int k = b + i;
                    bool valid = i < table.Length;
                    FactionDef def = valid ? table[i] : default(FactionDef);

                    // ── 颜色覆盖 ──
                    // ★★ 第 110 轮（用户）：「表里的出厂值一变，旧覆盖自动作废」——
                    //   每条覆盖都记着**写入时**的出厂值；和现在表里的值对不上，
                    //   说明阵营表被改过 → 这条覆盖作废，直接用表里的新值。
                    //   没记出厂值的是旧版存档，同样按「无法确认」处理 → 作废。
                    string kh = HsvKey(KeyH, count, i);
                    if (PlayerPrefs.HasKey(kh))
                    {
                        string kbh = HsvKey(KeyBaseH, count, i);
                        bool keep = valid && PlayerPrefs.HasKey(kbh);
                        if (keep)
                        {
                            var was = new Vector3(PlayerPrefs.GetFloat(kbh),
                                                  PlayerPrefs.GetFloat(HsvKey(KeyBaseS, count, i), 1f),
                                                  PlayerPrefs.GetFloat(HsvKey(KeyBaseV, count, i), 1f));
                            keep = Approx(was, def.Hsv);
                            if (keep) _baseHsv[k] = was;
                        }

                        if (keep)
                        {
                            _hsv[k] = new Vector3(PlayerPrefs.GetFloat(kh),
                                                  PlayerPrefs.GetFloat(HsvKey(KeyS, count, i), 1f),
                                                  PlayerPrefs.GetFloat(HsvKey(KeyV, count, i), 1f));
                            _hasHsv[k] = true;
                        }
                        else
                        {
                            dirty = true;      // 作废：下面的 Save() 会把这些键删掉
                        }
                    }

                    // ── 文字黑白覆盖（同样的规则）──
                    string kt = HsvKey(KeyText, count, i);
                    if (PlayerPrefs.HasKey(kt))
                    {
                        string kbt = HsvKey(KeyBaseText, count, i);
                        bool keep = valid && PlayerPrefs.HasKey(kbt)
                                    && (PlayerPrefs.GetInt(kbt) != 0) == def.TextBlack;
                        if (keep)
                        {
                            _textBlack[k] = (sbyte)PlayerPrefs.GetInt(kt);
                            _baseTextBlack[k] = (sbyte)PlayerPrefs.GetInt(kbt);
                        }
                        else dirty = true;
                    }
                }
            }

            // 旧格式（按全局下标存、不区分数制）的键：搬进**当前**这一槽，然后删掉。
            // 只是为了让上一版调过的颜色别白丢；搬完就再也不会走这条路了。
            if (MigrateLegacyKeys()) dirty = true;
            if (dirty) Save();
            Version++;
        }

        /// <summary>
        /// 把上一版的 `mor.set.hsv.h.3`（不区分数制）搬进当前阵营数那一槽。
        /// 返回 true = 确实搬过东西（调用方该落盘）。
        /// </summary>
        static bool MigrateLegacyKeys()
        {
            bool any = false;
            int b = Slot * MaxFactions;
            for (int i = 0; i < MaxFactions; i++)
            {
                string lh = KeyH + i;
                string ls = KeyS + i;
                string lv = KeyV + i;
                string lt = KeyText + i;

                int k = b + i;
                if (PlayerPrefs.HasKey(lh))
                {
                    if (!_hasHsv[k])
                    {
                        _hsv[k] = new Vector3(PlayerPrefs.GetFloat(lh),
                                              PlayerPrefs.GetFloat(ls, 1f),
                                              PlayerPrefs.GetFloat(lv, 1f));
                        _hasHsv[k] = true;
                        // ★ 旧键没有出厂值记录，这里按**当前**表值补一条，
                        //   否则下一次启动会被「出厂值对不上」的规则当成旧覆盖丢掉。
                        _baseHsv[k] = FactionSet.Get(i).Hsv;
                    }
                    any = true;
                }
                if (PlayerPrefs.HasKey(lt))
                {
                    if (_textBlack[k] < 0)
                    {
                        _textBlack[k] = (sbyte)PlayerPrefs.GetInt(lt);
                        _baseTextBlack[k] = (sbyte)(FactionSet.Get(i).TextBlack ? 1 : 0);
                    }
                    any = true;
                }

                PlayerPrefs.DeleteKey(lh);
                PlayerPrefs.DeleteKey(ls);
                PlayerPrefs.DeleteKey(lv);
                PlayerPrefs.DeleteKey(lt);
            }
            return any;
        }

        /// <summary>全部回默认（自检 / 换机器时用）。</summary>
        public static void ResetAll()
        {
            FactionCount = FactionSet.DefaultActive;
            Paused = false;
            ClearAllOverrides();
            Version++;
            if (Persist)
            {
                PlayerPrefs.DeleteKey(KeyCount);
                DeleteAllKeys();
                PlayerPrefs.Save();
            }
        }

        static void DeleteAllKeys()
        {
            for (int s = 0; s < Slots; s++)
            {
                int count = FactionCounts[s];
                for (int i = 0; i < MaxFactions; i++)
                {
                    PlayerPrefs.DeleteKey(HsvKey(KeyH, count, i));
                    PlayerPrefs.DeleteKey(HsvKey(KeyS, count, i));
                    PlayerPrefs.DeleteKey(HsvKey(KeyV, count, i));
                    PlayerPrefs.DeleteKey(HsvKey(KeyText, count, i));
                    PlayerPrefs.DeleteKey(HsvKey(KeyBaseH, count, i));
                    PlayerPrefs.DeleteKey(HsvKey(KeyBaseS, count, i));
                    PlayerPrefs.DeleteKey(HsvKey(KeyBaseV, count, i));
                    PlayerPrefs.DeleteKey(HsvKey(KeyBaseText, count, i));
                }
            }
            // 旧格式（按全局下标）的键也一并扫掉
            for (int i = 0; i < MaxFactions; i++)
            {
                PlayerPrefs.DeleteKey(KeyH + i);
                PlayerPrefs.DeleteKey(KeyS + i);
                PlayerPrefs.DeleteKey(KeyV + i);
                PlayerPrefs.DeleteKey(KeyText + i);
            }
        }

        /// <summary>两个 HSV 是否「一样」（判定出厂值有没有被改过用，1e-4 足够）。</summary>
        static bool Approx(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 1e-4f
                && Mathf.Abs(a.y - b.y) < 1e-4f
                && Mathf.Abs(a.z - b.z) < 1e-4f;
        }
    }
}
