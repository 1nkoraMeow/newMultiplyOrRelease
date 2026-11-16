using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 引力球的**持续特效**：一圈一圈往外扩、边扩边淡的涟漪。
    ///
    /// 为什么用涟漪：引力球最重要的一点是「它和普通大球不一样」——
    /// 视频里的引力球一直在往外「发功」，所以这里做成**永远有东西在动**的循环，
    /// 而不是一次性的爆闪（一次性的话，安静下来就又和普通球长一样了）。
    ///
    /// 做法很省：`GravityFxRingCount` 个软边环（`Primitives.SoftRing`），
    /// 相位错开 1/N，各自按 `Time.time / GravityFxPeriod` 循环缩放 + 淡出。
    /// 挂在大球**下面**当子物体，所以大球本体的缩放/移动它自动跟着。
    /// 图层在大球本体（60/61）之下，于是只看得见冒出球外的那一段。
    ///
    /// ★★★ 本轮（用户：「引力需要优化，**数值越大的球引力应该是更大的**」）：
    /// 涟漪的上限倍率再乘一个**随数值增长**的系数（`Cfg.GravityFxMaxScaleGain`，取 ln）
    /// —— 这是那条规格的**视觉**一半。参考点 100M 是 ×1（普通引力球观感不变），
    /// 1B ≈ ×1.50、100B ≈ ×2.13，所以「数值越大 → 涟漪铺得越远」能直接被看见，
    /// 而不是只有子弹漩涡收紧（那个要摆一堆子弹才看得出来）。
    /// </summary>
    public class GravityFx : MonoBehaviour
    {
        SpriteRenderer[] _rings;
        Color _base = Color.white;
        /// <summary>挂在谁下面 —— 用来读它的**数值**：引力越大，涟漪扩得越远。</summary>
        BigBall _ball;

        public static GravityFx Attach(Transform parent, Color color)
        {
            var go = new GameObject("GravityFx");
            if (parent != null) go.transform.SetParent(parent, false);

            var fx = go.AddComponent<GravityFx>();
            fx._base = color;
            // ★ 父物体就是大球本体（`BigBall.Create` 传的是 `go.transform`），这里拿它读数值。
            fx._ball = parent != null ? parent.GetComponent<BigBall>() : null;
            int n = Mathf.Max(Cfg.GravityFxRingCount, 1);
            fx._rings = new SpriteRenderer[n];
            for (int i = 0; i < n; i++)
            {
                fx._rings[i] = Primitives.MakeNode("Ring" + i, Primitives.SoftRing,
                    Palette.Alpha(color, 0f), Cfg.GravityFxSortingOrder + i, go.transform);
                fx._rings[i].transform.localPosition = Vector3.zero;
            }
            return fx;
        }

        void Update()
        {
            if (_rings == null) return;

            int n = _rings.Length;

            // ★★★ 「数值越大的球引力越大」的视觉那一半：数值越大 → `GravityValueFactor` 越大
            //   → 涟漪扩得越远。参考点（100M）= ×1，所以普通引力球的观感不变，
            //   只有几百 M 往上的球才明显往外铺。取 ln 是为了别让几百 B 的球糊满整屏。
            float grow = 1f;
            if (_ball != null)
                grow = Mathf.Min(
                    1f + Cfg.GravityFxMaxScaleGain
                         * Mathf.Log(Mathf.Max(GameWorld.GravityValueFactor(_ball.Value), 1f)),
                    Cfg.GravityFxMaxScaleGainCap);

            for (int i = 0; i < n; i++)
            {
                var sr = _rings[i];
                if (sr == null) continue;

                // 相位错开：任意时刻总有一圈在往外扩，所以是「持续」的
                float t = Mathf.Repeat(Time.time / Mathf.Max(Cfg.GravityFxPeriod, 0.01f)
                                       + (float)i / n, 1f);
                float s = Mathf.Lerp(1f, Cfg.GravityFxMaxScale * grow, t);
                sr.transform.localScale = new Vector3(s, s, 1f);

                // 越往外越淡（平方衰减，收尾干脆一点）
                float k = 1f - t;
                sr.color = Palette.Alpha(_base, Cfg.GravityFxAlpha * k * k);
            }
        }

        /// <summary>变回普通大球 / 被消耗时立刻停掉，别让残影留着。</summary>
        public void Silence()
        {
            if (_rings == null) return;
            for (int i = 0; i < _rings.Length; i++)
                if (_rings[i] != null) _rings[i].color = Palette.Alpha(_base, 0f);
        }
    }
}
