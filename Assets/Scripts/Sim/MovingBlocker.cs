using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// **移动阻挡球**（用户圈出来的那两颗灰球）。
    ///
    /// 规格（用户）：**只会水平移动，碰到墙了就折返**。
    /// 所以它不是「在某个小范围里晃」，而是**横穿整幅**：一路走到左/右墙内壁就掉头。
    /// 高度固定（一颗在乘法门那一排，一颗在固定阻挡球那一排）。
    ///
    /// 用 Kinematic 刚体 + `MovePosition`：它自己不会被重力 / 碰撞推开，
    /// 但**增倍球撞上来会被它挡住 / 弹开**（动态的球和 Kinematic 是有碰撞响应的）。
    /// </summary>
    public class MovingBlocker : MonoBehaviour
    {
        /// <summary>当前横向位置（相对增倍区中心，米）。</summary>
        public float X { get; private set; }
        /// <summary>活动范围的半宽（= 半墙距 - 半径），撞到这里就折返。</summary>
        public float Limit { get; private set; }

        Rigidbody2D _rb;
        int _dir = 1;
        Vector2 _home;

        public static MovingBlocker Create(Transform parent, float y, float radius, float startX)
        {
            var go = new GameObject("MovingBlocker");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(startX, y, 0f);
            go.transform.localScale = Vector3.one * (radius * 2f);

            var mb = go.AddComponent<MovingBlocker>();
            mb._home = new Vector2(startX, y);
            mb.X = startX;
            mb._dir = startX >= 0f ? -1 : 1;             // 一开始朝区中心走，别贴着墙起步
            mb.Limit = Mathf.Max(Cfg.ZoneWidth * 0.5f - radius, 0.05f);

            var sr = Primitives.MakeNode("Body", Primitives.Circle, Cfg.BlockerGrey, 7, go.transform);
            sr.transform.localScale = Vector3.one;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;                            // 外面那层 scale 才是真半径
            // ★ 规格（用户）：「移动阻挡球的弹性需要提升不少」——
            //   它和别的阻挡物不一样：**它是主动撞球的**，所以单独给它一份高弹材质
            //   （Unity 取两个材质的平均，配球的 0.35 → 有效 (0.35+1.0)/2 = 0.675，
            //    比区里的 0.50 高出一大截）。
            col.sharedMaterial = Physics2DConfig.Perfect;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            mb._rb = rb;

            // ★ 规格：灰色，被增倍球撞到时闪成那个阵营色再淡回灰（和固定阻挡球同一个机制）
            BlockerBall.Attach(go, sr);

            return mb;
        }

        void FixedUpdate()
        {
            if (_rb == null) return;

            X += _dir * Cfg.MovingBlockerSpeed * Time.fixedDeltaTime;
            // 规格：**碰到墙就折返**
            if (X >= Limit) { X = Limit; _dir = -1; }
            else if (X <= -Limit) { X = -Limit; _dir = 1; }

            _rb.MovePosition((Vector2)transform.parent.TransformPoint(new Vector2(X, _home.y)));
        }
    }
}
