using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 画面装饰：战场里慢慢飘的**尘埃**（几十个很淡的小点）。
    ///
    /// 目的只是让静态画面有一点生气 —— 所以刻意做得很淡（alpha 0.05~0.12）、很小、很慢，
    /// 不抢主体。数量在 `Cfg.DustCount`，飘出边界就绕回来。
    /// </summary>
    public class ScreenDecor : MonoBehaviour
    {
        Transform[] _motes;
        Vector2[] _vels;
        float _extent;

        public static ScreenDecor Build(Transform parent, float worldSize)
        {
            var go = new GameObject("ScreenDecor");
            if (parent != null) go.transform.SetParent(parent, false);

            var d = go.AddComponent<ScreenDecor>();
            int n = Mathf.Max(Cfg.DustCount, 0);
            d._motes = new Transform[n];
            d._vels = new Vector2[n];
            d._extent = worldSize;

            for (int i = 0; i < n; i++)
            {
                var sr = Primitives.MakeNode("Mote" + i, Primitives.Circle,
                    new Color(1f, 1f, 1f, 0.05f + Random.value * 0.07f),
                    Cfg.DustSortingOrder, go.transform);
                float s = 0.02f + Random.value * 0.05f;
                sr.transform.localScale = new Vector3(s, s, 1f);
                sr.transform.localPosition = new Vector3(Random.value * worldSize,
                                                        Random.value * worldSize, 0f);
                d._motes[i] = sr.transform;
                d._vels[i] = new Vector2(Random.Range(-0.05f, 0.05f), Random.Range(0.02f, 0.12f));
            }
            return d;
        }

        void Update()
        {
            if (_motes == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _motes.Length; i++)
            {
                var t = _motes[i];
                if (t == null) continue;
                Vector3 p = t.localPosition + (Vector3)(_vels[i] * dt);
                if (p.x < -0.5f) p.x = _extent + 0.5f;
                else if (p.x > _extent + 0.5f) p.x = -0.5f;
                if (p.y < -0.5f) p.y = _extent + 0.5f;
                else if (p.y > _extent + 0.5f) p.y = -0.5f;
                t.localPosition = p;
            }
        }
    }
}
