using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>共享的 2D 物理材质，避免每个物体各建一份。</summary>
    public static class Physics2DConfig
    {
        static PhysicsMaterial2D _bouncy, _slippery, _wall, _perfect, _zoneWall;

        /// <summary>球与球之间：弹性中等，摩擦很低。</summary>
        public static PhysicsMaterial2D Bouncy
        {
            get
            {
                if (_bouncy == null)
                {
                    _bouncy = new PhysicsMaterial2D("MorBouncy")
                    {
                        // ★★ 用户：「改了弹性还是蹦得老高老远，还得大削」。
                        //   根因：**Unity 取的是两个材质的平均值**，而区墙那边是 0.65 ——
                        //   只降球这一侧，有效弹性被墙拖着走（球 0.35 时仍有 0.50）。
                        //   两边一起动才有用：现在球 **0.45** + 墙 **0.65** ⇒ 有效 **0.55**
                        //   （第 90 轮用户：「增倍球弹性再提升」，见下）。
                        //   历史：0.35 → 0.08 → 0.30 → 0.50 → 0.75 → 0.65 → 0.55 → 0.35 → 0.15 → 0.25 → 0.35 → **0.45**
                        //   ★ Unity 相撞是**取平均**，所以光调球没用 —— 增倍区的墙另有 ZoneWall（见下，两处要一起改）。
                        bounciness = 0.45f,
                        friction = 0.05f,
                        hideFlags = HideFlags.DontSave
                    };
                }
                return _bouncy;
            }
        }

        /// <summary>气泡那种轻飘飘的东西。</summary>
        public static PhysicsMaterial2D Slippery
        {
            get
            {
                if (_slippery == null)
                {
                    _slippery = new PhysicsMaterial2D("MorSlippery")
                    {
                        // ★ 泡泡用的材质，和增倍球的弹力无关 —— 别跟着一起改
                        bounciness = 0.25f,
                        friction = 0.0f,
                        hideFlags = HideFlags.DontSave
                    };
                }
                return _slippery;
            }
        }

        /// <summary>
        /// **完全弹性**（bounciness = 1.0）。大球和墙壁都用它，
        /// 规格：大球碰墙要反弹而且**不会越弹越慢**。
        /// （两个材质加起来时 Unity 默认取平均，所以两边都得是 1.0 才行）
        /// </summary>
        public static PhysicsMaterial2D Perfect
        {
            get
            {
                if (_perfect == null)
                {
                    _perfect = new PhysicsMaterial2D("MorPerfect")
                    {
                        bounciness = 1.0f,
                        friction = 0f,
                        hideFlags = HideFlags.DontSave
                    };
                }
                return _perfect;
            }
        }

        /// <summary>
        /// **增倍区**里所有碰撞体用的材质（门、立柱、底板、阻挡球、平衡球……）。
        /// 弹性和球那边配成一对：有效弹性 = (0.45 + 0.65) / 2 = **0.55**（两边一起改才有用，见上）。
        /// （战场那边的 `Wall` 保持 1.0 —— 大球碰墙要**不损失能量地弹**，那是另一条规格。）
        /// </summary>
        public static PhysicsMaterial2D ZoneWall
        {
            get
            {
                if (_zoneWall == null)
                {
                    _zoneWall = new PhysicsMaterial2D("MorZoneWall")
                    {
                        bounciness = 0.65f,
                        friction = 0.18f,      // 稍微有点摩擦，球不会一直打转
                        hideFlags = HideFlags.DontSave
                    };
                }
                return _zoneWall;
            }
        }

        /// <summary>墙壁：不损失能量。</summary>
        public static PhysicsMaterial2D Wall
        {
            get
            {
                if (_wall == null)
                {
                    _wall = new PhysicsMaterial2D("MorWall")
                    {
                        bounciness = 1.0f,
                        // ★★ 第 92 轮：摩擦 0.02 → **0**。用户报「大球的反弹有时不严格遵循
                        //   反射角等于入射角」—— 摩擦会啃掉切向速度，把出射角掰弯，
                        //   摩擦系数要是非零，**每一次**擦着墙面弹都会偏一点点。
                        friction = 0f,
                        hideFlags = HideFlags.DontSave
                    };
                }
                return _wall;
            }
        }
    }
}
