#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MultiplyOrRelease.EditorTools
{
    /// <summary>
    /// 便利菜单。游戏本体是纯运行时构建的，其实随便哪个场景按 Play 都能跑，
    /// 这里只是帮你生成一个空的 Main 场景并加进 Build Settings，方便直接打包。
    /// </summary>
    public static class EditorMenu
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Multiply or Release/创建主场景并加入 Build Settings")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory("Assets/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool exists = false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].path == ScenePath) exists = true;
            if (!exists)
            {
                list.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }

            Debug.Log("[MultiplyOrRelease] 已创建 " + ScenePath + "，直接按 Play 即可运行。");
        }

        [MenuItem("Multiply or Release/打开参数配置文件 (Cfg.cs)")]
        public static void OpenConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/Core/Cfg.cs");
            if (asset != null) AssetDatabase.OpenAsset(asset);
            else Debug.LogWarning("[MultiplyOrRelease] 找不到 Cfg.cs");
        }

        /// <summary>
        /// 把设置面板改过的东西（阵营数 / 阵营颜色 / 文字黑白）全部清回阵营表默认值。
        /// 设置存在 `PlayerPrefs`（键名 `mor.set.*`），这里删掉那些键。
        /// ★ 暂停状态不持久化，这里只是顺手把它清成 false。
        /// </summary>
        [MenuItem("Multiply or Release/重置设置（阵营数 / 颜色）")]
        public static void ResetGameSettings()
        {
            // ★ 只在非 Play 状态下重置：Play 里换阵营数不会触发世界重建，
            //   会留下「设置说 16 家、场上还是 4 家」的错位。
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[MultiplyOrRelease] 请先退出 Play 模式再重置设置。");
                return;
            }
            GameSettings.ResetAll();
            Debug.Log("[MultiplyOrRelease] 设置已重置：阵营数 " + GameSettings.FactionCount
                      + "，颜色 / 文字黑白回阵营表原值。");
        }

        /// <summary>
        /// ★★ 打一个独立的 macOS 包出来。
        ///
        /// **为什么要有这个按钮**：用户报「帧率不稳定、总是跳帧」。
        /// Unity **编辑器**里的 Game 视图本身就带着一大堆开销（每次 repaint 都要过
        /// Editor 的渲染路径、还有 Inspector / Scene 视图在抢 CPU），
        /// 帧时间天然就毛刺；**同一个工程打成 Build 之后曲线会平得多**。
        /// 测试 / 录屏都应该用这个 Build，不要用编辑器。
        /// （F1 面板最后一行还会印 GPU 名和分辨率，截图给我就够了。）
        /// </summary>
        [MenuItem("Multiply or Release/打包 macOS 版（测帧率 / 录屏用）")]
        public static void BuildMac()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();

            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build");
            Directory.CreateDirectory(outDir);
            string app = Path.Combine(outDir, "MultiplyOrRelease.app");

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = app,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            if (sum.result == BuildResult.Succeeded)
                Debug.Log("[MultiplyOrRelease] 打包成功 → " + app
                          + "（" + (sum.totalSize / 1024 / 1024) + " MB，"
                          + sum.totalTime.TotalSeconds.ToString("F0") + " 秒）\n"
                          + "直接双击这个 .app 跑，再用录屏软件录 **它** —— 编辑器的帧率不作数。");
            else
                Debug.LogError("[MultiplyOrRelease] 打包失败：" + sum.result);
        }

        /// <summary>
        /// 把 Player Settings 设成**窗口化 1600×900、可拖拽缩放**。
        /// 打包 Windows 前会自动调一次；也可以手动点这个菜单重新应用。
        /// （Alt+Enter 仍然可以切全屏 —— `allowFullscreenSwitch` 没关。）
        /// </summary>
        [MenuItem("Multiply or Release/应用窗口化设置（1600×900 可拖拽）")]
        public static void ApplyWindowedSettings()
        {
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;   // 别用桌面原生分辨率
            PlayerSettings.resizableWindow = true;              // 边框可拖拽缩放
            AssetDatabase.SaveAssets();
            Debug.Log("[MultiplyOrRelease] 已设为窗口化 1600×900（可拖拽缩放；Alt+Enter 仍可全屏）");
        }

        /// <summary>
        /// ★ 把 Unity 的 **JDK / SDK / NDK** 三栏指到 `E:\AndroidTools\AndroidPlayer`。
        ///
        /// 这台机器的 Unity 是**手动安装**的（Hub 里 `manual:true`），Hub 装不上 Android 模块，
        /// 所以 SDK / NDK / OpenJDK 是我们从 Unity 官方 CDN 单独下下来解压的，不在编辑器目录里。
        /// 正常流程是在 `Edit → Preferences → External Tools` 里点三次 Browse，这里用反射直接写
        /// `UnityEditor.Android.AndroidExternalToolsSettings`（Unity 自己的这条 API 就是那三个输入框），
        /// 省掉手点。设完**必须重启 Unity** 才会重新检测。
        ///
        /// 反射而不是强引用：没装 Android 模块时 `UnityEditor.Android.Extensions.dll` 不存在，
        /// 强引用会让整个编辑器脚本编译失败。
        /// </summary>
        [MenuItem("Multiply or Release/修复 Android 工具链路径（指向 E:/AndroidTools）")]
        public static void FixAndroidToolchainPaths()
        {
            ApplyAndroidToolchain();

            Debug.Log("[MultiplyOrRelease] Android 工具链路径已写入（不用重启 Unity，直接打包）：\n"
                + "  SDK " + EditorPrefs.GetString("AndroidSDKRoot", "(空)") + "\n"
                + "  NDK " + EditorPrefs.GetString("AndroidNDKRoot", "(空)") + "\n"
                + "  JDK " + EditorPrefs.GetString("AndroidJavaRoot", "(空)"));
        }

        /// <summary>自己解压出来的 Android 工具链根目录（结构同 Unity 的 AndroidPlayer）。</summary>
        const string AndroidToolsRoot = @"E:\AndroidTools\AndroidPlayer";

        /// <summary>
        /// 把 JDK / SDK / NDK 指到 <see cref="AndroidToolsRoot"/>。打包前会自动调一次。
        ///
        /// 为什么需要：这台机器的 Unity 是**手动安装**的（Hub 里 `manual:true`），Hub 装不上
        /// Android 模块，SDK / NDK / OpenJDK 是从官方 CDN 单独下包解压出来的，不在编辑器目录里，
        /// 所以得告诉 Unity 去哪儿找。正常流程是在 Preferences → External Tools 手填三次。
        ///
        /// 两条路一起写，任一命中即可：
        ///   1. **EditorPrefs** —— Unity 的 Android 扩展真正读的是 `AndroidSDKRoot` /
        ///      `AndroidNDKRoot` / **`AndroidJavaRoot`**（注意 SDK / NDK 全大写，JDK 那条叫 Java，
        ///      拼错一个字母就静默读不到，之前 `JDK not found` 就是这个原因）；
        ///   2. **反射**写 `UnityEditor.Android.AndroidExternalToolsSettings` 的
        ///      `sdkRootPath` / `ndkRootPath` / `jdkRootPath`（= Preferences 里那三个输入框）。
        ///
        /// 用反射而不是强引用：没装 Android 模块时 `UnityEditor.Android.Extensions.dll` 不存在，
        /// 强引用会让整个编辑器脚本编译不过。
        /// </summary>
        static void ApplyAndroidToolchain()
        {
            string sdk = Path.Combine(AndroidToolsRoot, "SDK");
            string ndk = Path.Combine(AndroidToolsRoot, "NDK");
            string jdk = Path.Combine(AndroidToolsRoot, "OpenJDK");

            if (!Directory.Exists(sdk) || !Directory.Exists(ndk) || !Directory.Exists(jdk))
            {
                Debug.LogError("[MultiplyOrRelease] 找不到 " + AndroidToolsRoot
                    + " 下的 SDK / NDK / OpenJDK，Android 打包会失败。");
                return;
            }

            EditorPrefs.SetString("AndroidSDKRoot", sdk);
            EditorPrefs.SetString("AndroidNDKRoot", ndk);
            EditorPrefs.SetString("AndroidJavaRoot", jdk);

            // 旧键名（老版本 / 老文档用这套），一起写没有副作用
            EditorPrefs.SetString("AndroidSdkRoot", sdk);
            EditorPrefs.SetString("AndroidNdkRoot", ndk);
            EditorPrefs.SetString("JdkPath", jdk);

            // 「用 Unity 自带的那份」——只有 **JDK / SDK / NDK** 要关掉：
            // 手动安装的编辑器里那三份根本不存在，必须走我们解压出来的。
            EditorPrefs.SetInt("SdkUseEmbedded", 0);
            EditorPrefs.SetInt("NdkUseEmbedded", 0);
            EditorPrefs.SetInt("JdkUseEmbedded", 0);

            // ★★ **Gradle 反过来，必须用自带的那份**（`AndroidPlayer\Tools\gradle`，
            //    里面是完整的 Gradle 7.6）。之前这里被我一并关掉，Unity 就去读一个空的
            //    自定义 Gradle 路径，在 `AndroidGradleRoot.GetGradleLaunchJarPath` 里
            //    `Path.Combine(null, ...)` → `ArgumentNullException: path1`。
            EditorPrefs.SetInt("GradleUseEmbedded", 1);
            EditorPrefs.DeleteKey("AndroidGradleRoot");

            var t = System.Type.GetType(
                "UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            if (t == null) return;

            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public
                                                  | System.Reflection.BindingFlags.NonPublic
                                                  | System.Reflection.BindingFlags.Static;

            void SetStr(string prop, string value)
            {
                var p = t.GetProperty(prop, F);
                if (p != null && p.CanWrite) p.SetValue(null, value);
                else Debug.LogWarning("[MultiplyOrRelease] AndroidExternalToolsSettings." + prop
                                      + " 不可写，跳过（靠 EditorPrefs 那条路）");
            }
            SetStr("sdkRootPath", sdk);
            SetStr("ndkRootPath", ndk);
            SetStr("jdkRootPath", jdk);

            // 「Use Embedded」开关：JDK / SDK / NDK 关掉（我们那三份是外置的），
            // Gradle **改成开启**（只有 Unity 自带那一份）。不同版本是属性还是私有字段，都摸一遍。
            foreach (var p in t.GetProperties(F))
            {
                var n = p.Name.ToLowerInvariant();
                if (p.PropertyType != typeof(bool) || !p.CanWrite || !n.Contains("embedded")) continue;
                try { p.SetValue(null, n.Contains("gradle")); } catch { }
            }
            foreach (var f in t.GetFields(F))
            {
                var n = f.Name.ToLowerInvariant();
                if (f.FieldType != typeof(bool) || !n.Contains("embedded")) continue;
                try { f.SetValue(null, n.Contains("gradle")); } catch { }
            }
        }

        /// <summary>
        /// ★★ 打一个 Android 版（`.apk`）。
        ///
        /// 前置条件（缺一不可，Unity Hub → Installs → 2023.2.20f1 → ⚙ Add modules）：
        ///   · **Android Build Support**
        ///   · 它下面的 **Android SDK & NDK Tools** 和 **OpenJDK**
        /// 没装的话下面会先检查并直接报错，不会打出个半成品。
        ///
        /// 用的是 **IL2CPP + ARM64**（Mono 在 Android 上早就不支持了），
        /// 第一次编译会比较慢（几分钟），之后有增量缓存。
        /// 输出：`Build/Android/MultiplyOrRelease.apk`，默认用 debug keystore 签名，
        /// 自己装/发给别人测没问题；上架商店要换成自己的 keystore。
        /// </summary>
        [MenuItem("Multiply or Release/打包 Android 版（.apk）")]
        public static void BuildAndroid()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();

            // ★ 打包前先把 JDK / SDK / NDK 三栏指到 E:\AndroidTools\AndroidPlayer。
            //   手动安装的编辑器不会自动带这三样，不设就是 "UnityException: JDK not found"。
            ApplyAndroidToolchain();

            // ★ 应用图标（Assets/Art/AppIcon_1024.png）
            AppIconSetup.Apply();

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[MultiplyOrRelease] 没装 Android Build Support。\n"
                    + "Unity Hub → Installs → 2023.2.20f1 → ⚙ → Add modules → "
                    + "勾上 Android Build Support（含 Android SDK & NDK Tools、OpenJDK），装完重启 Unity。");
                return;
            }

            // ── Android 专用 Player Settings ──
            // 包名：**必须全小写、至少两段**（安卓/AAPT 的硬性规定），所以
            // `MultiplyOrRelease` 这种带大写的写法是非法包名，规范写法是 `com.multiplyorrelease`。
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,
                "com.multiplyorrelease");
            // 横屏、锁方向（游戏是 16:9 左栏 + 战场）
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            // IL2CPP + 只出 ARM64（Google Play 的硬要求；老 ARMv7 机器装不上）
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;      // 要 .apk，不是 .aab

            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "Android");
            Directory.CreateDirectory(outDir);
            string apk = Path.Combine(outDir, "MultiplyOrRelease.apk");

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apk,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            if (sum.result == BuildResult.Succeeded)
            {
                Debug.Log("[MultiplyOrRelease] Android 打包成功 → " + apk
                          + "（" + (sum.totalSize / 1024 / 1024) + " MB，"
                          + sum.totalTime.TotalSeconds.ToString("F0") + " 秒）\n"
                          + "拷到手机安装即可（首次要允许「安装未知来源应用」）。");
                EditorUtility.RevealInFinder(apk);
            }
            else
            {
                Debug.LogError("[MultiplyOrRelease] Android 打包失败：" + sum.result
                               + "（先确认 SDK / NDK / JDK 都装了，Console 里往上翻有具体报错）");
            }
        }

        /// <summary>
        /// ★★ 打一个独立的 Windows 包（`.exe`）。
        ///
        /// 输出到 `Build/Windows/MultiplyOrRelease.exe`。整个游戏是运行时搭出来的，
        /// 打包只需要 `Assets/Scenes/Main.unity` 这个**空场景**当载体
        /// （Bootstrap 用 `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` 建世界）。
        /// 没建过场景的话这里会自动建。
        ///
        /// 注意：**录屏菜单（Recorder）是编辑器扩展**，不会带进 exe；
        /// exe 里 F2 定步长模式照旧可用。
        /// </summary>
        [MenuItem("Multiply or Release/打包 Windows 版（.exe）")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();
            ApplyWindowedSettings();      // ★ 保证打出来的 exe 一定是窗口化的

            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "Windows");
            Directory.CreateDirectory(outDir);
            string exe = Path.Combine(outDir, "MultiplyOrRelease.exe");

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            if (sum.result == BuildResult.Succeeded)
            {
                Debug.Log("[MultiplyOrRelease] Windows 打包成功 → " + exe
                          + "（" + (sum.totalSize / 1024 / 1024) + " MB，"
                          + sum.totalTime.TotalSeconds.ToString("F0") + " 秒）\n"
                          + "整个 Build/Windows 文件夹一起拷走，双击 MultiplyOrRelease.exe 即可。");
                EditorUtility.RevealInFinder(exe);
            }
            else
            {
                Debug.LogError("[MultiplyOrRelease] 打包失败：" + sum.result
                               + "（Windows Build Support 模块没装的话，先在 Unity Hub 里勾上）");
            }
        }
    }
}
#endif
