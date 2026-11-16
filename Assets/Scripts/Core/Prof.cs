using System.Diagnostics;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 极轻量的分阶段计时（只在 F1 面板里显示）。
    ///
    /// 不是为了做引擎级 profiler，而是为了回答一个很具体的问题：
    /// **「偶尔跳帧」到底是哪一段造成的** —— 子弹物理、地图上传，还是 HUD？
    /// 每段只取两次 `Stopwatch` 时间戳，开销可以忽略；
    /// 帧时间统计是「最近 N 帧的最大值」，所以偶发的尖峰不会被平均值糊掉。
    /// </summary>
    public static class Prof
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        /// <summary>最近一帧各阶段花的毫秒数。</summary>
        public static float PoolMs, FlushMs, HudMs;
        /// <summary>`BulletPool.LateUpdate`：圆点/拖尾两张网格的装配（在 `pool` 计时之外）。</summary>
        public static float ViewMs;
        /// <summary>`pool` 内部再拆两段：step（推进+回收+网格）与 resolve（碰撞）。</summary>
        public static float StepMs, ResolveMs;
        /// <summary>HUD 内部再拆三块（`hud` 一栏偏高时用来看是哪一块，见 F1 第三行）。</summary>
        public static float HudZoneMs, HudBattleMs, HudRestMs;
        /// <summary>底部武器栏 / F1 面板（`hud` 里除 zone、battle 之外的两块）。</summary>
        public static float HudWeaponMs, HudPanelMs;
        /// <summary>最近 N 帧的平均 / 最大帧时间（毫秒）。</summary>
        public static float FrameAvgMs, FrameMaxMs;
        /// <summary>
        /// `frame avg` **减掉**我们能计时的四项（pool / view / flush / hud）之后剩下的。
        /// ★ 这一栏才是「终极优化」的眼睛：它很大就说明瓶颈不在我们的循环里，
        ///   而在 Unity 的渲染 / 填充率 / IMGUI 后端 / 物理。
        /// </summary>
        public static float OtherMs;

        /// <summary>显卡名（缓存一次 —— `SystemInfo` 每次都问驱动，不该每帧调）。</summary>
        public static string GpuName
        {
            get
            {
                if (_gpu == null)
                {
                    try { _gpu = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType; }
                    catch (System.Exception) { _gpu = "未知"; }
                }
                return _gpu;
            }
        }
        static string _gpu;

        const int Window = 120;

        static float _acc, _max;
        static int _n;

        public static long Tick() { return Clock.ElapsedTicks; }

        public static float Since(long t0)
        {
            return (float)((Clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency);
        }

        /// <summary>每帧调一次，喂进这一帧的总时长（秒）。</summary>
        /// <summary>
        /// ★★ 这里量的是**真实**过去了多久，不是 `Time.deltaTime`。
        ///   定步长模式（F2）下 `Time.deltaTime` / `unscaledDeltaTime` 都被钉死成 1/60，
        ///   拿它当帧时间的话 F1 面板会永远显示「16.7ms / 60fps」——正好在**最需要看帧率**的时候
        ///   把读数变成假的。所以用 `realtimeSinceStartup` 自己差分。
        /// </summary>
        static float _lastReal;

        public static void PushFrame(float ignored)
        {
            float now = Time.realtimeSinceStartup;
            float dt = _lastReal > 0f ? Mathf.Max(now - _lastReal, 1e-5f) : ignored;
            _lastReal = now;
            float ms = dt * 1000f;
            _acc += ms;
            if (ms > _max) _max = ms;
            if (++_n < Window) return;

            FrameAvgMs = _acc / _n;
            OtherMs = Mathf.Max(0f, FrameAvgMs - (PoolMs + ViewMs + FlushMs + HudMs));
            FrameMaxMs = _max;
            _acc = 0f; _max = 0f; _n = 0;
        }

        public static void Reset()
        {
            PoolMs = FlushMs = HudMs = 0f;
            FrameAvgMs = FrameMaxMs = 0f;
            _acc = _max = 0f; _n = 0;
        }
    }
}
