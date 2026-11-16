using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 规格：**炮台的持续特效**（每个炮台一直有，不是一次性的）。
    ///
    /// 两层，都很轻：
    ///   ① **护盾边缘的「呼吸圈」** —— 两三圈软边环，从护盾里侧慢慢扩到护盾边上、边扩边淡，
    ///      相位错开，所以任意时刻总有一圈在动（和引力球那套涟漪是同一个手感，只是慢得多）；
    ///   ② **核心的微光** —— 本体上一圈很淡的软边环，跟着本体的呼吸微微缩放。
    ///
    /// 为什么不用「自转的环」：环是旋转对称的，转起来**看不出来**。
    ///
    /// 成本：每个炮台 3 个 SpriteRenderer，四个炮台一共 12 个 —— 可以忽略。
    /// </summary>
    public class TurretFx : MonoBehaviour
    {
        Turret _turret;
        SpriteRenderer[] _rings;
        SpriteRenderer _glow;
        Color _ringColor;
        Color _glowColor;

        public static TurretFx Attach(Turret turret, Color factionColor)
        {
            var go = new GameObject("TurretFx");
            if (turret != null) go.transform.SetParent(turret.transform, false);

            var fx = go.AddComponent<TurretFx>();
            fx._turret = turret;
            // 偏白一点：护盾本身已经是阵营色，"能量"再叠一层同色会糊成一块
            fx._ringColor = Color.Lerp(Color.white, factionColor, 0.45f);
            fx._glowColor = Color.Lerp(Color.white, factionColor, 0.25f);

            int n = Mathf.Max(Cfg.TurretFxRingCount, 1);
            fx._rings = new SpriteRenderer[n];
            for (int i = 0; i < n; i++)
            {
                fx._rings[i] = Primitives.MakeNode("Pulse" + i, Primitives.SoftRing,
                    new Color(1f, 1f, 1f, 0f), Cfg.TurretFxSortingOrder, go.transform);
                fx._rings[i].transform.localPosition = Vector3.zero;
            }

            fx._glow = Primitives.MakeNode("CoreGlow", Primitives.SoftRing,
                new Color(1f, 1f, 1f, 0f), Cfg.TurretFxSortingOrder, go.transform);
            fx._glow.transform.localPosition = Vector3.zero;
            return fx;
        }

        void Update()
        {
            if (_turret == null || _rings == null) return;

            // 炮台死了就把特效收掉（阵亡后只剩一个暗淡的本体）
            if (_turret.IsDead)
            {
                for (int i = 0; i < _rings.Length; i++)
                    if (_rings[i] != null) _rings[i].color = new Color(1f, 1f, 1f, 0f);
                if (_glow != null) _glow.color = new Color(1f, 1f, 1f, 0f);
                return;
            }

            float shieldR = Mathf.Max(_turret.ShieldVisualRadius, Cfg.RadiusMin);
            int n = _rings.Length;
            float period = Mathf.Max(Cfg.TurretFxPeriod, 0.05f);

            for (int i = 0; i < n; i++)
            {
                var sr = _rings[i];
                if (sr == null) continue;
                // 相位错开 → 总有圈在动，看起来是"一直有东西在动"而不是一次次闪
                float t = Mathf.Repeat(Time.time / period + (float)i / n, 1f);
                float s = Mathf.Lerp(Cfg.TurretFxMinScale, Cfg.TurretFxMaxScale, t);
                sr.transform.localScale = Vector3.one * (shieldR * 2f * s);
                // 两端都淡：冒出来 → 涨大 → 收掉
                float a = Cfg.TurretFxAlpha * Mathf.Sin(t * Mathf.PI);
                sr.color = Palette.Alpha(_ringColor, a);
            }

            if (_glow != null)
            {
                float coreR = _turret.CoreRadius;
                float t = Mathf.Repeat(Time.time / period, 1f);
                float s = 0.86f + 0.10f * Mathf.Sin(t * Mathf.PI * 2f);
                _glow.transform.localScale = Vector3.one * (coreR * 2f * s);
                _glow.color = Palette.Alpha(_glowColor, Cfg.TurretFxAlpha * 0.55f);
            }
        }
    }
}
