using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 增倍球死亡时的爆炸特效（炮台阵亡、亡语触发时每个球放一个）。
    /// 一圈扩散的环 + 一团收缩的光球，纯运行时生成，不用粒子系统资源。
    /// </summary>
    public class Explosion : MonoBehaviour
    {
        float _t;
        float _life;
        float _maxR;
        Color _color;
        Color _ringColor = Color.white;
        SpriteRenderer _ring;
        SpriteRenderer _core;

        public static void Spawn(Vector2 worldPos, float radius, Color color, Transform parent)
        {
            Spawn(worldPos, radius, color, parent, 0.70f, Color.white, 7.0f);
        }

        /// <param name="life">整个动画多长（秒）。</param>
        /// <param name="ringColor">扩散环的颜色（白 = 冲击波）。</param>
        /// <param name="radiusScale">最大半径 = radius × 这个倍数。</param>
        /// <summary>可调参数的版本 —— 复活特效就是「更大的环 + 更长的时间 + 阵营色的闪光」。</summary>
        public static void Spawn(Vector2 worldPos, float radius, Color color, Transform parent,
                                 float life, Color ringColor, float radiusScale)
        {
            var go = new GameObject("Explosion");
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = worldPos;

            var e = go.AddComponent<Explosion>();
            e._life = Mathf.Max(life, 0.05f);
            e._maxR = Mathf.Max(radius * radiusScale, 0.6f);
            e._color = color;
            e._ringColor = ringColor;

            e._core = Primitives.MakeNode("Core", Primitives.Circle, Palette.Alpha(color, 0.9f), 60, go.transform);
            e._ring = Primitives.MakeNode("Ring", Primitives.Ring, Palette.Alpha(ringColor, 0.9f), 61, go.transform);
            return;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _life);
            if (k >= 1f) { Destroy(gameObject); return; }

            float r = _maxR * k;
            if (_ring != null)
            {
                _ring.transform.localScale = Vector3.one * (r * 2f);
                _ring.color = Palette.Alpha(_ringColor, (1f - k) * 0.9f);
            }
            if (_core != null)
            {
                _core.transform.localScale = Vector3.one * (_maxR * 1.1f * (1f - k) * (1f - k) * 2f);
                _core.color = Palette.Alpha(_color, (1f - k) * 0.9f);
            }
        }
    }
}
