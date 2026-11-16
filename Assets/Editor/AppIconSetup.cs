#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MultiplyOrRelease.EditorTools
{
    /// <summary>
    /// 把 `Assets/Art/AppIcon_1024.png` 设成 **Android 应用图标**。
    ///
    /// 为什么用反射：Unity 换过几轮图标 API ——
    ///   · 老接口 `PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup, Texture2D[])`
    ///   · 新接口 `PlayerSettings.SetIcons(NamedBuildTarget, Texture2D[], IconKind)`
    /// 2023.2 里两个都还在，但哪个真正写进「Platform Settings → Android → Icon」跟平台设置有关，
    /// 而且 `IconKind` 的枚举值各版本不一样（Any / Legacy / Round / AdaptiveForeground / AdaptiveBackground…）。
    /// 所以这里不写死：把能摸到的重载和枚举值全调一遍，最后打日志告诉你写了什么。
    ///
    /// 名字里带 `background` 的那一层（自适应图标的底色）用纯色图 `AppIconBackground.png`，
    /// 其余层用主图 —— 否则前景背景都塞同一张，桌面上会糊成重影。
    ///
    /// 打包菜单 `BuildAndroid()` 会自动调一次，也可以手动点菜单。
    /// </summary>
    public static class AppIconSetup
    {
        public const string IconPath = "Assets/Art/AppIcon_1024.png";
        public const string BackgroundPath = "Assets/Art/AppIconBackground.png";

        [MenuItem("Multiply or Release/应用应用图标（AppIcon_1024.png）")]
        public static void ApplyFromMenu()
        {
            Apply();
            EditorUtility.DisplayDialog("应用图标",
                "已把 " + IconPath + " 设为 Android 应用图标。\n重新打包后手机上就是这张图。", "好");
        }

        public static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            if (icon == null)
            {
                Debug.LogError("[Icon] 找不到 " + IconPath + "，跳过设置图标。");
                return;
            }
            if (bg == null) bg = icon;

            PrepareImporter(IconPath);
            PrepareImporter(BackgroundPath);

            var iconArray = Fill(icon);
            var bgArray = Fill(bg);

            bool any = false;

            foreach (var m in typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var ps = m.GetParameters();
                try
                {
                    // ── 老接口：SetIconsForTargetGroup(BuildTargetGroup, Texture2D[]) ──
                    if (m.Name == "SetIconsForTargetGroup" && ps.Length == 2
                        && ps[0].ParameterType == typeof(BuildTargetGroup)
                        && ps[1].ParameterType == typeof(Texture2D[]))
                    {
                        m.Invoke(null, new object[] { BuildTargetGroup.Android, iconArray });
                        any = true;
                        Debug.Log("[Icon] SetIconsForTargetGroup(Android) 已设置");
                    }
                    // ── 新接口：SetIcons(NamedBuildTarget, Texture2D[], IconKind) ──
                    else if (m.Name == "SetIcons" && ps.Length == 3
                             && ps[1].ParameterType == typeof(Texture2D[]))
                    {
                        object target = StaticMember(ps[0].ParameterType, "Android");
                        if (target == null) continue;
                        Type kindType = ps[2].ParameterType;
                        if (!kindType.IsEnum) continue;

                        foreach (string kindName in Enum.GetNames(kindType))
                        {
                            object kind = Enum.Parse(kindType, kindName);
                            bool isBackground = kindName.IndexOf("background",
                                StringComparison.OrdinalIgnoreCase) >= 0;
                            m.Invoke(null, new object[] { target, isBackground ? bgArray : iconArray, kind });
                            any = true;
                            Debug.Log("[Icon] SetIcons(Android, " + kindName + ") ← "
                                      + (isBackground ? "纯色底图" : "主图"));
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Icon] " + m.Name + " 调用失败：" + e.GetBaseException().Message);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (any)
                Debug.Log("[Icon] 图标已写入 PlayerSettings（" + IconPath + "）。重新打包即可生效。");
            else
                Debug.LogError("[Icon] 没找到任何可用的设图标 API —— 请手动在 "
                               + "Project Settings → Player → Icon 里拖入 " + IconPath);
        }

        /// <summary>纹理导入设置：不压 mipmap、不加 NPOT 缩放，图标要清清楚楚。</summary>
        static void PrepareImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            bool dirty = false;
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
            if (ti.maxTextureSize < 1024) { ti.maxTextureSize = 1024; dirty = true; }
            if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; dirty = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed)
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true;
            }
            if (dirty) ti.SaveAndReimport();
        }

        static Texture2D[] Fill(Texture2D tex)
        {
            // Android 一档要 432/324/216/162/108/81…，Unity 会自己缩，这里塞同一个引用即可
            var arr = new Texture2D[6];
            for (int i = 0; i < arr.Length; i++) arr[i] = tex;
            return arr;
        }

        static object StaticMember(Type t, string name)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (p != null) return p.GetValue(null);
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
            return f != null ? f.GetValue(null) : null;
        }
    }
}
#endif
