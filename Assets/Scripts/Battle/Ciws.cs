using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// ★★★ 第 103 轮（用户）新增的**近防炮（CIWS）**。
    ///
    /// 生命周期：
    ///   ① **飞行形态**：增倍球在武器区选了近防炮 → 从炮台打出一颗*不参与物理*的小球
    ///      （半径 = <see cref="Cfg.CiwsBodyRadius"/>，大小相当于中子星），直线飞；
    ///   ② 碰到**非己方领土** → **展开**：数值 50% 变盾、50% 变散弹
    ///      （<see cref="Cfg.CiwsShieldFraction"/>）;
    ///   ③ 展开后原地不动：
    ///        · 索敌范围内有敌方 → 开火（见 `GameWorld.StepCiws`）；
    ///        · 没有 → 吸收范围内**己方领土**能量到散弹（每格每帧 +0.5）；
    ///   ④ 盾被打光之后，本体再被敌人子弹击中 → **死亡，无亡语**。
    ///
    /// ★ 这个类只负责**状态 + 视觉**；索敌/开火/吸能/护盾判定都在
    ///   `GameWorld.StepCiws` 里（那里才拿得到地图、子弹池和其他大球）。
    /// </summary>
    public class Ciws
    {
        public Faction Owner;
        /// <summary>世界坐标。</summary>
        public Vector2 Pos;
        public Vector2 Vel;
        /// <summary>还在飞行形态（没展开）。</summary>
        public bool Flying = true;
        public bool Consumed;

        /// <summary>展开时的**原始数值**（= 增倍球给的那一份）。</summary>
        public double Payload;
        /// <summary>护盾剩余。</summary>
        public double Shield;
        /// <summary>散弹剩余。</summary>
        public double Ammo;

        /// <summary>开火冷却计时。</summary>
        public float FireTimer;

        /// <summary>刚吸收/开火时的闪光计时（视觉反馈）。</summary>
        public float Pulse;

        GameObject _go;
        SpriteRenderer _body;
        SpriteRenderer _shield;
        Transform _root;

        public GameObject GameObject { get { return _go; } }

        public static Ciws Create(Faction owner, Vector2 pos, Vector2 dir, Transform parent)
        {
            var c = new Ciws();
            c.Owner = owner;
            c.Pos = pos;
            c.Vel = dir.sqrMagnitude > 1e-6f
                ? dir.normalized * Cfg.CiwsLaunchSpeed
                : Vector2.right * Cfg.CiwsLaunchSpeed;

            var go = new GameObject("Ciws");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            c._go = go;
            c._root = go.transform;

            // 本体：用武器区那个炮形图标（缩放到「中子星大小」）。
            c._body = Primitives.MakeNode("CiwsBody", Primitives.CiwsIcon,
                owner != null ? owner.Color : Color.white, Cfg.CiwsSortingOrder, go.transform);
            c._body.transform.localScale = Vector3.one * (Cfg.CiwsBodyRadius * 2.6f);

            // 护盾圈：展开后才显示。
            c._shield = Primitives.MakeNode("CiwsShield", Primitives.Ring,
                owner != null ? Palette.Alpha(owner.Color, 0.9f) : Color.white,
                Cfg.CiwsShieldSortingOrder, go.transform);
            c._shield.transform.localScale = Vector3.one * (Cfg.CiwsShieldRadius * 2f);
            c._shield.enabled = false;

            return c;
        }

        /// <summary>展开：数值对半分成盾 / 散弹。</summary>
        public void Deploy()
        {
            if (!Flying) return;
            Flying = false;
            Vel = Vector2.zero;
            Shield = Payload * Cfg.CiwsShieldFraction;
            Ammo = Payload * (1d - Cfg.CiwsShieldFraction);
            _shield.enabled = Shield > 0d;
        }

        /// <summary>展开后每帧同步一次视觉（位置、护盾可见性、闪光）。</summary>
        public void SyncVisual(float dt)
        {
            if (_go == null) return;
            _root.position = Pos;

            // 飞行形态：炮身朝飞行方向（视觉上像一颗有朝向的弹丸）。
            if (Flying)
            {
                float a = Mathf.Atan2(Vel.y, Vel.x) * Mathf.Rad2Deg;
                _root.rotation = Quaternion.Euler(0f, 0f, a);
            }
            else
            {
                _root.rotation = Quaternion.identity;
                if (_shield != null)
                {
                    bool on = Shield > 0d && !Consumed;
                    if (_shield.enabled != on && _shield.gameObject.activeSelf) _shield.enabled = on;
                    if (on)
                    {
                        float f = Mathf.Clamp01((float)(Shield / System.Math.Max(Payload * Cfg.CiwsShieldFraction, 1d)));
                        _shield.color = Palette.Alpha(Owner != null ? Owner.Color : Color.white,
                                                      Mathf.Lerp(0.25f, 0.9f, f));
                    }
                }
            }

            if (Pulse > 0f) Pulse = Mathf.Max(0f, Pulse - dt);
            if (_body != null)
            {
                float s = Cfg.CiwsBodyRadius * 2.6f * (1f + Pulse * 0.35f);
                _body.transform.localScale = Vector3.one * s;
            }
        }

        /// <summary>死亡：**无亡语**，直接销毁视觉、从世界列表里去掉。</summary>
        public void Kill()
        {
            if (Consumed) return;
            Consumed = true;
            if (_go != null) Object.Destroy(_go);
            _go = null;
            _body = null;
            _shield = null;
        }
    }
}
