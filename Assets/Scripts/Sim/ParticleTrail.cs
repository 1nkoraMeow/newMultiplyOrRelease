using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// **白色粒子拖尾**（规格：100M 及以上的增倍球，把原本那条丝带拖尾换成这个）。
    ///
    /// 做法是一圈固定大小的小白点，按时间间隔在球经过的位置上「盖章」，
    /// 每个点自己淡出缩小、到期就藏起来，下一轮循环复用 ——
    /// **零分配、零 GameObject 增长**，比 Unity 的 ParticleSystem 轻得多
    /// （这个工程也不需要为它引一套粒子材质）。
    ///
    /// 挂在**增倍区 root** 上（不是挂在球上）—— 这样点的坐标和球的 localPosition 同一空间，
    /// 不用每帧做世界坐标换算。
    /// </summary>
    public class ParticleTrail : MonoBehaviour
    {
        SpriteRenderer[] _dots;
        float[] _life;
        int _next;
        float _emitAccum;
        float _radius = 0.1f;
        bool _on;

        public static ParticleTrail Attach(Transform parent, int sortingOrder)
        {
            var go = new GameObject("ParticleTrail");
            if (parent != null) go.transform.SetParent(parent, false);
            var pt = go.AddComponent<ParticleTrail>();

            int n = Mathf.Max(Cfg.MultiplierParticleCount, 4);
            pt._dots = new SpriteRenderer[n];
            pt._life = new float[n];
            for (int i = 0; i < n; i++)
            {
                pt._dots[i] = Primitives.MakeNode("P" + i, Primitives.Circle,
                    new Color(1f, 1f, 1f, 0f), sortingOrder, go.transform);
                pt._dots[i].transform.localPosition = Vector3.zero;
                pt._dots[i].transform.localScale = Vector3.zero;
            }
            return pt;
        }

        /// <summary>粒子大小跟着球半径走（规格：是从球半径推出来的，不是写死的）。</summary>
        public void SetRadius(float r) { _radius = Mathf.Max(r, 0.001f); }

        /// <summary>开关：关掉之后现有的点会自然淡掉（不会啪一下消失）。</summary>
        public void SetOn(bool on)
        {
            _on = on;
            if (!on) _emitAccum = 0f;
        }

        /// <summary>由球每帧调一次。<paramref name="localPos"/> 是球在**增倍区**里的位置。</summary>
        public void Tick(float dt, Vector2 localPos, bool moving)
        {
            float life = Mathf.Max(Cfg.MultiplierParticleLife, 0.02f);

            // ① 淡出 / 回收
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_life[i] <= 0f) continue;
                _life[i] -= dt;
                float k = Mathf.Clamp01(_life[i] / life);
                SetAlpha(_dots[i], Cfg.MultiplierParticleAlpha * k);
                float s = _radius * 2f * Cfg.MultiplierParticleScale * (0.45f + 0.55f * k);
                _dots[i].transform.localScale = new Vector3(s, s, 1f);
                if (_life[i] <= 0f) SetAlpha(_dots[i], 0f);
            }

            // ② 按间隔盖章
            if (!_on || !moving) return;
            _emitAccum += dt;
            float interval = Mathf.Max(Cfg.MultiplierParticleInterval, 0.002f);
            while (_emitAccum >= interval)
            {
                _emitAccum -= interval;
                int i = _next;
                _next = (_next + 1) % _dots.Length;
                var d = _dots[i];
                d.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
                float s = _radius * 2f * Cfg.MultiplierParticleScale;
                d.transform.localScale = new Vector3(s, s, 1f);
                SetAlpha(d, Cfg.MultiplierParticleAlpha);
                _life[i] = life;                       // 刚盖的那一颗（索引别搞混）
            }
        }

        /// <summary>全清（球被重新投放时用）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                _life[i] = 0f;
                if (_dots[i] != null) SetAlpha(_dots[i], 0f);
            }
            _emitAccum = 0f;
        }

        static void SetAlpha(SpriteRenderer sr, float a)
        {
            if (sr != null) sr.color = new Color(1f, 1f, 1f, a);
        }
    }
}
