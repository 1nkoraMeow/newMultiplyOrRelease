using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 固定阻挡球（门上方那两排小圆球、两排六边形、门顶上的那两颗、武器区顶上那排平衡球）。
    ///
    /// 规格（用户）：
    ///   · **一律灰色**；
    ///   · 被**增倍球**撞到时，闪成**那个阵营的颜色**，然后淡回灰色。
    ///
    /// 实现：本组件挂在**有碰撞体的那个物体**上（`OnCollisionEnter2D` 才能收到），
    /// 颜色改在**子物体**的 SpriteRenderer 上。零分配，比放粒子特效便宜得多。
    /// </summary>
    public class BlockerBall : MonoBehaviour
    {
        SpriteRenderer _sr;
        float _flash;
        /// <summary>不闪的时候是什么颜色 —— 闪完要淡回它。默认是阻挡球那个灰。</summary>
        Color _base = Cfg.BlockerGrey;

        /// <summary>挂一个默认灰色的阻挡球（阻挡球排、帽子、移动阻挡球都用它）。</summary>
        public static void Attach(GameObject host, SpriteRenderer body)
        {
            Attach(host, body, Cfg.BlockerGrey);
        }

        /// <summary>
        /// <paramref name="baseColor"/> = 它「不闪的时候」的颜色。
        /// ★ 规格（用户）：门与门之间那根**柱子**要和白牌下面的阻挡条**同一个颜色**，
        /// 所以它不能套用固定的灰 —— 基色必须能传进来。
        /// </summary>
        public static void Attach(GameObject host, SpriteRenderer body, Color baseColor)
        {
            if (host == null || body == null) return;
            var b = host.AddComponent<BlockerBall>();
            b._sr = body;
            b._base = baseColor;
            body.color = baseColor;
        }

        /// <summary>被谁撞了：闪成它的颜色。</summary>
        public void Flash(Color c)
        {
            _sr.color = c;
            _flash = 1f;
        }

        void Update()
        {
            if (_flash <= 0f || _sr == null) return;
            _flash = Mathf.Max(0f, _flash - Time.deltaTime / Mathf.Max(Cfg.BlockerFlashTime, 0.01f));
            // 1 → 0：阵营色 → 基色
            _sr.color = Color.Lerp(_base, _sr.color, _flash);
            if (_flash <= 0f) _sr.color = _base;
        }

        void OnCollisionEnter2D(Collision2D c)
        {
            if (c == null || c.collider == null) return;
            // 只有**增倍球**能点亮它（子弹不打这里；大球也不该让挡球变色）
            var mb = c.collider.GetComponentInParent<MultiplierBall>();
            if (mb == null || mb.Consumed) return;
            Flash(mb.Owner != null ? mb.Owner.Color : Color.white);
        }
    }
}
