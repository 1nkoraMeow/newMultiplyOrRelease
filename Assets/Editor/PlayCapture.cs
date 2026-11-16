#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MultiplyOrRelease.EditorTools
{
    /// <summary>
    /// 批处理跑起来并截图，用来在没有人的情况下检查画面布局。
    ///
    /// 跑法：
    ///   Unity -batchmode -projectPath . \
    ///         -executeMethod MultiplyOrRelease.EditorTools.PlayCapture.Run -logFile -
    ///
    /// 注意：**不要加 -nographics**，否则没有图形设备、截不出图。
    /// 输出到 /tmp/mor-shot/shot_XXs.png
    /// </summary>
    public static class PlayCapture
    {
        public static string OutDir = "/tmp/mor-shot";
        static readonly float[] ShotTimes = { 3f, 12f, 40f };

        static float _startTime;
        static bool _started;
        static int _next;

        public static void Run()
        {
            Debug.Log("[SHOT] Run() 进入，准备进 Play 模式");
            Directory.CreateDirectory(OutDir);
            _next = 0;
            _started = false;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;

            if (!_started)
            {
                _started = true;
                _startTime = (float)EditorApplication.timeSinceStartup;
            }

            float t = (float)EditorApplication.timeSinceStartup - _startTime;

            if (_next < ShotTimes.Length && t >= ShotTimes[_next])
            {
                Shoot(ShotTimes[_next]);
                _next++;
            }

            if (_next >= ShotTimes.Length)
            {
                EditorApplication.update -= Tick;
                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
        }

        static void Shoot(float t)
        {
            var rig = Object.FindAnyObjectByType<CameraRig>();
            if (rig == null || rig.ZoneCam == null || rig.BattleCam == null)
            {
                Debug.LogError("[SHOT] 找不到 CameraRig");
                return;
            }

            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);

            var zc = rig.ZoneCam;
            var bc = rig.BattleCam;

            var zClear = zc.clearFlags;
            var bClear = bc.clearFlags;
            var zTarget = zc.targetTexture;
            var bTarget = bc.targetTexture;

            // 先让战场相机整张清黑，再让增倍区相机只补自己那块（不清色），
            // 这样两块 viewport 能拼进同一张 RT。
            bc.clearFlags = CameraClearFlags.SolidColor;
            zc.clearFlags = CameraClearFlags.Depth;
            bc.targetTexture = rt;
            zc.targetTexture = rt;

            bc.Render();
            zc.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string path = Path.Combine(OutDir, string.Format("shot_{0:00}s.png", Mathf.RoundToInt(t)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[SHOT] saved " + path);

            bc.targetTexture = bTarget;
            zc.targetTexture = zTarget;
            bc.clearFlags = bClear;
            zc.clearFlags = zClear;

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
#endif
