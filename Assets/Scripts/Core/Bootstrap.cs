using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 引导器。整个游戏是运行时「代码搭出来」的，不依赖任何场景 / 预制体 / 美术资源，
    /// 所以随便打开哪个场景直接按 Play 就能跑。
    ///
    /// ★★ 第 105 轮：为了设置面板能**换阵营数 / 换颜色**，世界改成「一整棵树」：
    ///   所有运行时造出来的东西（GameWorld、相机、HUD）都挂在 <see cref="WorldRoot"/>（`[Game]`）下，
    ///   换设置 = 把 `[Game]` 整个 `Destroy` 掉、下一帧 `BuildWorld()` 重搭。
    ///   这样不用给每个 `Build()` 都加「清理」代码，也不会有旧炮台/旧纹理漏在场上。
    ///   **设置面板自己不在 `[Game]` 里**（它挂 `[Settings]`），否则一重开自己就没了。
    /// </summary>
    public static class Bootstrap
    {
        public static bool Booted { get; private set; }

        /// <summary>现在是不是「定步长（录屏）」模式。</summary>
        public static bool CaptureMode { get; private set; }

        /// <summary>整个运行时世界的根（`[Game]`）。没建世界时为 null。</summary>
        public static GameObject WorldRoot { get; private set; }

        /// <summary>
        /// 启动时就存在的场景物体（默认场景的 Main Camera 之类）——
        /// 重建世界时**绝对不许动**它们（它们不是我们造的）。
        /// </summary>
        static readonly HashSet<GameObject> _preserved = new HashSet<GameObject>();

        /// <summary>
        /// ★★ 定步长模式 —— 就是 Algodoo 卡顿时那种「把一步的时间拉长」的做法。
        ///
        /// **两种模式的差别**：
        ///   · 关（默认，实时优先）：`Time.deltaTime` = 真实过去了多久。卡一下（比如 40ms），
        ///     下一帧 Unity 会**连跑 2~3 个物理步把时间追回来** —— 追的过程本身就是新的卡顿，
        ///     而且**丢掉的那一帧是真的没了**（录屏里就是「跳帧」）。
        ///   · 开（模拟步优先）：`Time.deltaTime` 被**钉死**成 `1/CaptureFramerate`，
        ///     不管这一帧画了 8ms 还是 40ms。于是：**一帧 = 一个模拟步**，
        ///     机器慢就**整体变慢动作**，一步都不会跳。录出来每一帧都是完整的一步，
        ///     后期直接抽帧 / 加速就行。
        ///
        /// **为什么还要动 `targetFrameRate`**：机器**跑得比 60 快**的时候，
        ///   如果不管，一帧还是 1/60 秒，那游戏就变成 2 倍速快放了。
        ///   所以定步长模式下把目标帧率也设成 60：快不过 60（实时）、慢下去就是慢动作。
        /// </summary>
        public static void SetCaptureMode(bool on)
        {
            CaptureMode = on;
            if (on)
            {
                Time.captureFramerate = Mathf.Max(Cfg.CaptureFramerate, 1);
                Application.targetFrameRate = Mathf.Max(Cfg.CaptureFramerate, 1);
            }
            else
            {
                Time.captureFramerate = 0;                  // 0 = 关掉定步长，回到真实时间
                Application.targetFrameRate = Cfg.TargetFrameRate;
            }
        }

        /// <summary>
        /// 暂停 / 继续。★ 走 `Time.timeScale`（物理、动画、`Time.deltaTime` 一起停），
        /// 但 **IMGUI 不受 timeScale 影响** —— 所以暂停时设置面板照样能点。
        /// 状态同时写进 <see cref="GameSettings.Paused"/>，重开一局时能保住。
        /// </summary>
        public static void SetPaused(bool on)
        {
            GameSettings.Paused = on;
            Time.timeScale = on ? 0f : Cfg.TimeScale;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Booted || GameWorld.Instance != null) return;
            Booted = true;

            // 手机 / 平板：别让屏幕自己睡过去（这是个会一直跑下去的自动模拟）
            if (Application.isMobilePlatform) Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Physics2D.gravity = new Vector2(0f, -9.81f);
            // 增倍区里东西小、速度快，步长太大会穿模
            Time.fixedDeltaTime = Cfg.FixedTimestep;   // 物理开销直接减半；射速已经和步长解耦了
            Physics2D.velocityIterations = 6;
            Physics2D.positionIterations = 2;
            // 画质相关的全部关掉：这是个 2D 自动模拟，不需要阴影/抗锯齿/多光源
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.antiAliasing = 0;
            QualitySettings.pixelLightCount = 0;
            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.skinWeights = SkinWeights.OneBone;
            // ★ 上万颗子弹两两检测是最大的开销，直接关掉
            Physics2D.IgnoreLayerCollision(Cfg.BulletLayer, Cfg.BulletLayer, true);
            // ★★ 第 92 轮：350 颗泡泡**互相之间**不碰撞（用户报「帧率很低」）。
            //   它们和球、和墙照常碰 —— 只省掉那几十万对泡沫自己撞自己的检测。
            Physics2D.IgnoreLayerCollision(Cfg.BubbleLayer, Cfg.BubbleLayer, true);
            // 低速碰撞也按弹性处理，不然大球走慢了就「贴」在墙上不动了
            // ★★ 第 92 轮（用户：「大球撞墙**直接不反弹了**」+「反弹不严格遵循反射角」）：
            //   0.2 太高了 —— 大球被尾流推着**稳定巡航在 0.25 m/s**（第 88 轮改的），
            //   碰撞又会把速度吃掉一点，于是**贴墙那一下经常落在 0.2 以下 → 引擎干脆不弹**，
            //   表现就是「撞上去就不动了」。压到 0.05：慢球也弹，又不到 0
            //   （0 会让慢速贴墙的物体一直微抖）。
            Physics2D.bounceThreshold = Cfg.BounceThreshold;
            QualitySettings.vSyncCount = 0;   // vSync 一旦错过 16.6ms 就会直接掉到 30 帧，很突兀
            Application.targetFrameRate = Cfg.TargetFrameRate;
            SetCaptureMode(Cfg.CaptureModeAtStart);   // ★★ 录屏用的定步长模式（F2 开关）
            // ★ 关键：帧一慢，Unity 会在一帧里补跑十几个物理步，越补越慢形成雪崩。
            //   卡住上限之后宁可慢动作，也不要帧率崩到 1 fps。
            Time.maximumDeltaTime = 0.05f;

            // 先把场景里**已有的**根物体记下来 —— 重建世界时这些一律不碰
            RememberSceneRoots();

            GameSettings.Paused = false;
            BuildWorld();

            // ★ 设置面板：Cfg.EnableSettingsPanel 关掉时**连这个物体都不建**
            if (Cfg.EnableSettingsPanel)
            {
                var shell = new GameObject("[Settings]");
                shell.AddComponent<SettingsPanel>();
                _preserved.Add(shell);
            }

            SetPaused(GameSettings.Paused);
        }

        static void RememberSceneRoots()
        {
            _preserved.Clear();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] != null) _preserved.Add(roots[i]);
        }

        /// <summary>
        /// 搭一整个新世界。已经有一个在跑就直接返回（重开请先 <see cref="TearDown"/>）。
        /// 所有运行时物体都收进 `[Game]` —— 见类头注释。
        /// </summary>
        public static GameWorld BuildWorld()
        {
            if (WorldRoot != null) return GameWorld.Instance;

            var root = new GameObject("[Game]");
            WorldRoot = root;

            var world = GameWorld.Build();
            BulletRenderer.Build(world.BattleRoot);
            // 合批拖尾（TrailRenderer 画法下不会用到，但建着没开销：没东西画时 MeshRenderer 是关的）
            if (!Cfg.BulletTrailUsesTrailRenderer) BulletTrailRenderer.Build(world.BattleRoot);
            var rig = CameraRig.Build(world);

            var hud = new GameObject("HudOverlay").AddComponent<HudOverlay>();
            hud.Bind(world, rig);

            // Build() 造出来的东西都是场景根物体 —— 这里一次性收进 `[Game]`
            AdoptNewRoots(root);

            SetPaused(GameSettings.Paused);
            return world;
        }

        /// <summary>把刚才新造出来的场景根物体挂到 `[Game]` 下面（世界坐标不变）。</summary>
        static void AdoptNewRoots(GameObject root)
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var go = roots[i];
                if (go == null || go == root || _preserved.Contains(go)) continue;
                if (go.GetComponent<SettingsPanel>() != null) continue;
                go.transform.SetParent(root.transform, true);
            }
        }

        /// <summary>
        /// 把整个世界拆掉（下一帧才真正 Destroy，所以调用方要 `yield return null` 之后再 Build）。
        /// ★ 场景里**不是我们造的**物体（`_preserved`）和设置面板都留着。
        /// </summary>
        public static void TearDown()
        {
            GameWorld.ClearInstance();

            if (WorldRoot != null)
            {
                Object.Destroy(WorldRoot);
                WorldRoot = null;
            }

            // 兜底：万一有谁漏在场景根上（没挂进 [Game]），一并清掉
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var go = roots[i];
                if (go == null || _preserved.Contains(go)) continue;
                if (go.GetComponent<SettingsPanel>() != null) continue;
                Object.Destroy(go);
            }
        }

        /// <summary>
        /// 拆掉旧世界、下一帧搭新的。设置面板用（协程里调用）。
        /// ★ `newFactionCount` 在**旧世界已经拆掉之后**才写进 `GameSettings` ——
        ///   否则阵营数一变，`Palette` 的缓存立刻缩到新长度，而旧 HUD 还在用旧下标画，
        ///   同一帧就会下标越界。顺序：冻结 → 拆 → 换数 → 搭 → 恢复暂停态。
        /// </summary>
        public static System.Collections.IEnumerator RebuildRoutine(int newFactionCount = -1)
        {
            bool wasPaused = GameSettings.Paused;
            SetPaused(true);          // 拆的时候别让旧世界还在跑物理
            yield return null;        // 等 Destroy 真正生效
            TearDown();
            yield return null;        // 再等一帧，确保旧物体都清了
            if (newFactionCount > 0) GameSettings.SetFactionCount(newFactionCount);
            BuildWorld();
            SetPaused(wasPaused);
        }
    }
}
