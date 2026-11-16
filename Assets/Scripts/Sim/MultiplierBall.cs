using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 增倍球。按你的说明，它由**三样东西**组成：
    ///   ① 本体 —— 一个**圆**（不做彗星形状）
    ///   ② 文字 —— 画在本体**上层**，不超出本体范围，宽度动态调整并始终居中（由 HUD 负责）
    ///   ③ 拖尾 —— 用 **Unity 自带的 TrailRenderer**，长度随速度变化（不是定长）
    ///
    /// 行为：
    ///   · 撞到乘倍区判定框 → 数值乘倍（离开后再碰到才会再乘，不会站在上面狂刷）
    ///   · 掉进底部武器区 → 「选武器」，数值归 1，然后从顶部**发射炮台**带初速度打出去
    /// </summary>
    public class MultiplierBall : MonoBehaviour
    {
        public Faction Owner;
        public MultiplierZone Zone;
        public double Value = 1.0;
        public bool Consumed;
        public float Radius = 0.17f;

        /// <summary>
        /// 存活时间（秒）：从**上次从发射炮台打出去**那一刻算起。
        /// 碰到判定框 / 选完武器**回到炮台**时归零（统一在 <see cref="LaunchFrom"/> 里清）。
        /// HUD 在球的右上方画成 m:ss，不足 20 秒不显示。
        /// </summary>
        public float LifeTime;

        /// <summary>体积倍率：随时间 ×0.9995/tick 缩小的是**显示半径**，数值不受影响。</summary>
        float _sizeMul = 1f;
        bool _pickedWeapon;
        TrailRenderer _trail;
        /// <summary>100M 及以上的**白色粒子拖尾** —— 它会**替换**掉上面的丝带拖尾。</summary>
        ParticleTrail _particles;

        Rigidbody2D _rb;
        SpriteRenderer _body;
        Transform _visual;

        readonly HashSet<int> _touchingGates = new HashSet<int>();

        public Rigidbody2D Body { get { return _rb; } }

        public static MultiplierBall Create(Faction owner, MultiplierZone zone,
                                            Vector2 localPos, double value, Transform parent)
        {
            var go = new GameObject("MultiplierBall");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var ball = go.AddComponent<MultiplierBall>();
            ball.Owner = owner;
            ball.Zone = zone;
            ball.Value = value;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            ball._visual = visual.transform;

            // ① 本体：一个圆
            ball._body = Primitives.MakeNode("Body", Primitives.Circle, owner.Color, 20, visual.transform);
            ball._body.transform.localScale = Vector3.one;

            // ③ 拖尾：Unity 自带的 TrailRenderer，长度随速度变化
            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(go.transform, false);
            ball._trail = trailGo.AddComponent<TrailRenderer>();
            ball._trail.time = 0.22f;
            ball._trail.startWidth = 0.5f;                  // 真正的宽度每帧按半径重设（世界单位）
            ball._trail.endWidth = 0f;
            ball._trail.numCapVertices = 4;
            ball._trail.numCornerVertices = 2;
            ball._trail.minVertexDistance = 0.06f;
            ball._trail.autodestruct = false;
            ball._trail.emitting = true;
            ball._trail.textureMode = LineTextureMode.Stretch;
            ball._trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ball._trail.receiveShadows = false;
            ball._trail.sharedMaterial = Primitives.SpriteMaterial;
            ball._trail.startColor = Palette.Alpha(owner.Color, 0.85f);
            ball._trail.endColor = Palette.Alpha(owner.Color, 0f);
            ball._trail.sortingOrder = 19;
            // ★ 规格（用户）：原来套在 1B 以上的那条「B 级额外拖尾」**删掉**了。
            //   现在 100M 及以上是把整条丝带拖尾**换成**白色粒子拖尾（见 RefreshRadius / Update）。

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = Cfg.ZoneGravityScale;
            // ★ 用户：「蹦得老高老远」—— 弹性之外，**阻尼**是另一个直接杠杆：
            //   0.04 几乎等于没有阻尼，球飞出去基本不掉速。提到 0.20 之后落点明显收得住。
            rb.drag = 0.20f;
            rb.angularDrag = 0.6f;
            rb.mass = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.freezeRotation = true;
            rb.sharedMaterial = Physics2DConfig.Bouncy;
            ball._rb = rb;

            ball.RefreshRadius();
            return ball;
        }

        public void RefreshRadius()
        {
            float target = Cfg.LogV((float)Value) / Cfg.MultiplierBallRadiusLogDivisor
                           + Cfg.MultiplierBallRadiusBias;
            // 显示半径 = 公式半径 × 随时间缩小的体积倍率（数值本身不受影响）
            Radius = Mathf.Clamp(target * _sizeMul, Cfg.MultiplierBallRadiusMin, Cfg.RadiusMax);
            transform.localScale = Vector3.one * (Radius * 2f);
            // ★ TrailRenderer 的宽度是**世界单位**，不受 transform 缩放影响。
            //   以前写死 1.0 —— 等于拖尾有 1 米宽，比球本体宽一倍多，整颗球看着就是个大气球。
            if (_trail != null) _trail.startWidth = Radius * 2f;

            // ★ 规格：100M 及以上 → 丝带拖尾**关掉**，改用白色粒子拖尾（是替换，不是叠加）。
            bool wantParticles = Value >= Cfg.MultiplierParticleThreshold;
            if (_trail != null) _trail.emitting = !wantParticles;
            if (wantParticles) EnsureParticles();
            if (_particles != null)
            {
                _particles.SetOn(wantParticles);
                _particles.SetRadius(Radius);
            }
            // 质量随数值（也就是随半径）增长：越大的球越沉，不容易被子弹推开
            // ★ 质量按**公式半径**算，不按带显示下限的 Radius ——
            //   否则小球也会被撑到下限那么大，浮力永远托不起来它（浮力区等于没加）。
            // ★ 规格（用户给的公式）：`density = ln(数值) × 0.8 + 0.05`（自然对数）。
            //   2D 刚体没有 density 属性，所以直接算质量 = 密度 × 面积。
            //   ★ 半径用**公式半径**（不带显示下限）—— 带下限的话小球会被抬到下限那么大，
            //     质量凭空变大，浮力区就托不动它了（这是之前踩过的坑）。
            if (_rb != null) _rb.mass = Cfg.MultiplierBallMass(Value);
        }

        /// <summary>惰性建粒子拖尾 —— 只有真到 100M 的球才需要它，别给小球白建一堆 sprite。</summary>
        void EnsureParticles()
        {
            if (_particles != null || Zone == null || Zone.Root == null) return;
            _particles = ParticleTrail.Attach(Zone.Root, Cfg.MultiplierParticleSortingOrder);
        }

        /// <summary>从发射炮台打出去。</summary>
        public void LaunchFrom(Vector2 localPos, Vector2 velocity)
        {
            if (_rb == null) return;
            _sizeMul = 1f;              // 重新投放 → 体积恢复
            _pickedWeapon = false;
            LifeTime = 0f;              // ★ 回到炮台 → 存活时间归零
            transform.localPosition = localPos;
            _rb.position = transform.position;
            _rb.velocity = velocity;
            if (_trail != null) _trail.Clear();
            if (_particles != null) _particles.Clear();
        }

        void FixedUpdate()
        {
            if (Consumed || _rb == null) return;

            // 规格：增倍球每个 tick 缩小的是**体积**（×0.9995），**数值不变**
            _sizeMul *= Cfg.MultiplierBallShrinkPerTick;
            RefreshRadius();

            // ★ 第 79 轮用户：「给予向上的力那个机制不需要了」——
            //   「静止 5 秒就踢一脚」那套已经整个删掉，这里只剩这么一点随机扰动，
            //   免得球在平地上永远一动不动。
            if (_rb.velocity.sqrMagnitude < 0.5f)
                _rb.AddForce(new Vector2(Random.Range(-1f, 1f), Random.Range(0.2f, 1f)) * 0.7f, ForceMode2D.Force);
        }

        void Update()
        {
            if (Consumed) return;
            LifeTime += Time.deltaTime;
            RefreshRadius();

            // ★ 白色粒子拖尾：按时间间隔在球经过的位置盖章。
            //   放在渲染帧（和球的插值位置同步），粒子间距看着才均匀。
            if (_particles != null)
            {
                bool moving = _rb != null && _rb.velocity.sqrMagnitude > 0.04f;
                _particles.Tick(Time.deltaTime, transform.localPosition, moving);
            }
        }

        // ── 和乘倍区的物理交互（实心碰撞，不是穿过去）────────────────

        /// <summary>
        /// ★★ 规格：**只有从上面撞下来**才算过门。
        ///   以前任何方向的接触都算 —— 球被弹上去、从下面顶到门底，也会「倒着吃一次乘倍」
        ///   （用户报的就是这个）。这里用接触法线判方向：法线朝上（从门指向球）才是从上面来的。
        /// </summary>
        static bool HitFromAbove(Collision2D c)
        {
            if (c == null || c.contactCount <= 0) return false;
            // normal 是**从被撞的物体指向自己**，所以从上方撞上来时 normal.y ≈ +1
            return c.GetContact(0).normal.y > Cfg.GateTopHitNormalMin;
        }

        /// <summary>
        /// ★★ 第 99 轮（用户）：「增倍球有时**在传送回发射炮台时会被压进 x16**」——
        ///   传送回炮台的那一下，球可能和帽子中间那块 x16 判定区重叠，
        ///   以前 x16 是**不判方向的**（任何接触都算），于是那一压就白吃一次 ×16。
        ///   现在 x16 **只认从下面吃**：浮力把球从下面顶上来（法线朝下）才算数。
        ///   判定阈值和「从上面」共用同一个数（只是符号相反），别各写一个。
        /// </summary>
        static bool HitFromBelow(Collision2D c)
        {
            if (c == null || c.contactCount <= 0) return false;
            return c.GetContact(0).normal.y < -Cfg.GateTopHitNormalMin;
        }

        void OnCollisionEnter2D(Collision2D c)
        {
            if (Consumed) return;
            var gate = c.collider != null ? c.collider.GetComponentInParent<MultiplierGate>() : null;
            if (gate == null) return;
            // ★ 普通门：**只认从上面撞下来**。
            // ★★ x16（特殊门）：判定区挂在帽子中间那个缺口里，球是从**浮力通道里往上飘**撞上它的
            //   —— 所以是**只认从下面顶上来**（第 99 轮：传送回炮台时被压进 x16 白吃一次 ×16 的 bug）。
            if (gate.IsSpecial)
            {
                if (!HitFromBelow(c)) return;
            }
            else if (!HitFromAbove(c)) return;

            if (_touchingGates.Contains(gate.GateIndex)) return;   // 还贴着，不重复结算
            _touchingGates.Add(gate.GateIndex);

            double before = Value;
            Value = gate.Apply(Value);
            // ★ 总值计数条：累计「翻倍后 − 翻倍前」（1→4 记 +3、256→512 记 +256）
            if (Owner != null) Owner.GainTotal += Value - before;
            gate.PassCount++;
            gate.Flash(Owner != null ? Owner.Color : Cfg.GateFlashColor);   // ★ 闪成这个球的阵营色
            RefreshRadius();

            // 规格：**乘倍之后立刻传送回发射炮台**，并重新给初速度方向
            if (Zone != null)
            {
                LaunchFrom(Zone.LauncherLocalPosition(), Zone.RandomLaunchDirection() * Cfg.LaunchSpeed);
                _touchingGates.Clear();
            }
        }

        void OnCollisionExit2D(Collision2D c)
        {
            if (Consumed) return;
            var gate = c.collider != null ? c.collider.GetComponentInParent<MultiplierGate>() : null;
            if (gate != null) _touchingGates.Remove(gate.GateIndex);
        }

        // ── 掉进武器区 ──────────────────────────────────────────────

        void OnTriggerEnter2D(Collider2D other)
        {
            if (Consumed) return;
            var zone = other.GetComponent<WeaponZone>();
            if (zone != null) zone.Accept(this);
        }

        /// <summary>
        /// `OnTriggerEnter` 只在**跨进触发框那一瞬间**来一次，
        ///   而 x2 / 齐射那些格的判定线比整排的触发框顶边更低（见 `WeaponZone.TriggerTopLocalY`）。
        ///   球刚跨进来时还在判定线上面、会被拒绝，所以还得靠 **Stay** 在它沉下去之后再判一次。
        ///   （不用额外降的格子在 Enter 那一下就已经接受了，Stay 这边会被 `_pickedWeapon` 挡住。）
        /// </summary>
        void OnTriggerStay2D(Collider2D other)
        {
            if (Consumed) return;
            var zone = other.GetComponent<WeaponZone>();
            if (zone != null) zone.Accept(this);
        }

        /// <summary>武器区接受了这个球：选武器 → 数值归 1 → 从发射炮台重新打出去。</summary>
        public void OnPickedWeapon(int slotIndex)
        {
            if (Consumed) return;

            var world = GameWorld.Instance;

            // ★ 计数器挂在**区**上（和乘法门的 PassCount 一个机制）。
            //   放在这里而不是 WeaponZone.Accept 里，是为了「只要选了武器就一定计数」——
            //   不管走的是哪条路径都绕不过去。
            var wz = world != null && world.Zone != null ? world.Zone.Weapons : null;

            // ★★ 规格（用户第 87 轮 图二）：武器区里有 **x2 格**，而且
            //    「x2 正常还原就行了」—— 那就是**乘法门**：球落进来**不消耗**，
            //    数值 ×2 之后原样重新投出去。（所以它不算「选了武器」，可以反复吃。）
            var def = Cfg.WeaponSlot(slotIndex);
            if (def.IsGate)
            {
                if (wz != null) wz.NotePick(slotIndex);
                double before = Value;
                Value *= def.Gate;
                // ★ 总值计数条：武器区里的 x2 格也算一次乘法
                if (Owner != null) Owner.GainTotal += Value - before;
                RefreshRadius();
                RelaunchFromLauncher();
                return;
            }

            if (_pickedWeapon) return;               // 一颗球只选一次武器
            _pickedWeapon = true;

            // ★ 规格：球落进武器区 = **用这颗球的数值换一件武器**。
            //   之前重写增倍区的时候我把这块逻辑弄丢了 —— 选了武器只有数值归 1 + 重投，
            //   所以「选了不加子弹、不加护盾、不出大球、齐射打了个寂寞」。
            double payload = Value;
            if (wz != null) wz.NotePick(slotIndex);

            if (Owner != null)
            {
                switch (def.Kind)
                {
                    case WeaponSlotKind.Direct:
                    case WeaponSlotKind.Scatter:
                        // 直射 / 散射都是**炮台的射击模式**：选中就切过去，并把数值变成弹药
                        Owner.TurretMode = def.Kind;
                        Owner.Bullets += payload;
                        break;
                    case WeaponSlotKind.Shield:
                        Owner.Shield += payload;
                        break;
                    case WeaponSlotKind.BigBall:
                        if (world != null) world.SpawnBigBall(Owner, payload);
                        break;
                    case WeaponSlotKind.Vampire:
                        // ★ 第 111 轮：吸血球 —— 和「大球」同一条生成路径，只多一个标记。
                        if (world != null) world.SpawnBigBall(Owner, payload, true);
                        break;
                    case WeaponSlotKind.Ciws:
                        // ★ 第 103 轮：从炮台打出一颗不参与物理的小球，
                        //   碰到敌方领土才展开成近防炮。
                        if (world != null) world.SpawnCiws(Owner, payload);
                        break;
                    case WeaponSlotKind.Volley:
                        if (world != null) world.FireVolley(Owner, payload);
                        break;
                }
            }

            Value = 1.0;
            RefreshRadius();
            RelaunchFromLauncher();
        }

        /// <summary>从增倍区顶上的发射炮台重新投出去。</summary>
        void RelaunchFromLauncher()
        {
            if (Zone == null) return;
            Vector2 dir = Zone.RandomLaunchDirection();
            LaunchFrom(Zone.LauncherLocalPosition(), dir * Cfg.LaunchSpeed);
            _touchingGates.Clear();
        }

        // ── 被子弹 / 别的球作用 ─────────────────────────────────────

        public void Push(Vector2 impulse)
        {
            if (_rb != null) _rb.AddForce(impulse, ForceMode2D.Impulse);
        }

        public bool Drain(double amount)
        {
            Value -= amount;
            if (Value <= 0.0001d) { Consume(); return true; }
            RefreshRadius();
            return false;
        }

        public void Consume()
        {
            if (Consumed) return;
            Consumed = true;

            // ★★ 粒子拖尾必须**跟着一起销毁**。
            //   它是**挂在增倍区 root 上**的独立物体（不是挂在球上），而且**全靠这个球每帧
            //   调 `Tick` 才淡出、才回收** —— 球一 `Destroy` 就再没人推它了：那串白点会
            //   **永远定格在屏幕上**（用户：「有的百M增倍球在炮台死亡后消失了，粒子拖尾没消失」），
            //   顺带每死一颗 ≥100M 的球就漏一个 GameObject（+28 个 sprite）。
            ReleaseTrail();
            Destroy(gameObject);
        }

        /// <summary>把粒子拖尾一起带走（**立刻不显示**，Destroy 帧末才生效）。</summary>
        void ReleaseTrail()
        {
            if (_particles == null) return;
            _particles.Clear();
            // ★ 先 SetActive(false)：Destroy 要到帧末才真的执行，
            //   中间这一帧那串白点还会画出来（用户看到的就是「拖尾没消失」）。
            if (_particles.gameObject != null) _particles.gameObject.SetActive(false);
            Destroy(_particles.gameObject);
            _particles = null;
        }

        /// <summary>兜底：任何别的销毁路径（场景拆掉、编辑器停播放）也要把拖尾带走。</summary>
        void OnDestroy() { ReleaseTrail(); }
    }
}
