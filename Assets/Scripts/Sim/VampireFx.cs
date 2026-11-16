using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 吸血球的持续特效 —— **第 117 轮按用户给的 Algodoo poststep 原文重写**。
    ///
    /// 原文整段放在大球的 poststep 里，所以**每个物理步都执行一次**：
    /// <code>
    /// tx = scene.addcircle({ radius := 0.01; colorhsva := [0,0,0.2,1];
    ///                       _angle := rand.uniform01*2*pi; _distance := radius+0.2;
    ///                       timetolive := 1.7;
    ///        poststep := (e)=>{ pos = _bball.pos + [sin(_angle),cos(_angle)]*_distance;
    ///                           _angle *= 1.02; _distance *= 0.98 } });
    /// scene.addpen({ pos := tx.pos; size := tx.radius*2; fadetime := 0.2;
    ///                colorhsva := tx.colorhsva });
    /// </code>
    ///
    /// 所以一个「特效」= **一个会自己沿螺旋运动的小圆 + 一支跟着它盖章的笔**：
    ///   · 圆每步 `angle *= 1.02`、`distance *= 0.98`，活 1.7 秒；
    ///   · 笔每步在圆的当前位置盖一个点，点 0.2 秒淡出 ⇒ 尾巴是**运动轨迹**；
    ///   · 每帧新建一套、各自随机起始角 ⇒ 铺满球面，且每套的尾巴都在动。
    ///
    /// ★★ 前几轮全错在：把「拖尾」当成预先画好的静态线段，而且所有特效
    ///    **共用同一条螺旋**（第 114 轮挤成两簇）→ 后来又干脆删掉螺旋（115/116 轮）。
    ///    正确的是**每个特效有自己的螺旋状态**，尾巴由它自己走出来。
    ///
    /// 实现：Unity 的 `TrailRenderer` 就是 addpen（按时间采样 + 渐隐），和子弹拖尾同一套；
    ///   `SpriteRenderer` 圆 = addcircle。圆和笔迹同色 `[0,0,0.2,1]` 深灰，没有白圆头。
    ///
    /// ★ 挂在大球本体下。大球 `localScale = 半径 × 2`，这里每帧反向缩放一次，
    ///   子物体就能直接用**世界单位**摆；整套按对象池复用，不每帧 Instantiate。
    /// </summary>
    public class VampireFx : MonoBehaviour
    {
        /// <summary>一套「圆 + 笔」，带自己独立的螺旋状态。</summary>
        class Pen
        {
            public GameObject Go;
            public Transform Tr;
            public SpriteRenderer Head;
            public TrailRenderer Trail;
            public float Age;
            public float Angle;
            public float Distance;
        }

        readonly List<Pen> _active = new List<Pen>();
        readonly Stack<Pen> _free = new Stack<Pen>();
        int _created;
        float _spawnCarry;

        BigBall _ball;

        // addpen 的材质 / 渐变全特效共享一份（每套各一份 Material 会多出上百次 SetPass）
        static Material _trailMat;
        static Gradient _trailGradient;

        /// <summary>自检 / 调试用：最近一次生成的起始极角（弧度）。</summary>
        public float AngleForSmoke { get; private set; }
        /// <summary>自检 / 调试用：最近一次生成的起始距离。</summary>
        public float OrbitDistance { get; private set; }
        /// <summary>自检 / 调试用：最早那套当前的距离（应逐帧变小，证明粒子真的在走螺旋）。</summary>
        public float ActiveDistance { get { return _active.Count > 0 ? _active[0].Distance : 0f; } }
        /// <summary>自检用：一共建了几套（含池里躺着的）。</summary>
        public int PenCount { get { return _created; } }
        /// <summary>自检用：此刻活着的套数。</summary>
        public int LivePenCount { get { return _active.Count; } }
        /// <summary>自检用：活着的套里真带 addpen 笔迹的有几套（应等于 LivePenCount）。</summary>
        public int TrailCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _active.Count; i++)
                    if (_active[i].Trail != null) n++;
                return n;
            }
        }
        /// <summary>自检用：累计生成过多少套。</summary>
        public int SpawnedTotal { get; private set; }

        public static VampireFx Attach(Transform parent)
        {
            var go = new GameObject("VampireFx");
            if (parent != null) go.transform.SetParent(parent, false);
            var fx = go.AddComponent<VampireFx>();
            fx._ball = parent != null ? parent.GetComponent<BigBall>() : null;
            EnsureTrailAssets();
            return fx;
        }

        static void EnsureTrailAssets()
        {
            if (_trailMat != null) return;
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            _trailMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            // 不贴纹理：Algodoo 的 addpen 是等粗的圆点序列，靠 alpha 沿时间渐隐。
            _trailGradient = new Gradient();
            var c = Cfg.VampireFxColor;
            _trailGradient.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        }

        void FixedUpdate()
        {
            // 原文：`_pdsz <= 0 ? { vel = 0; timetolive = 0 } : {}` —— 本体没了，整套立刻收掉
            if (_ball == null || _ball.Consumed || _ball.Value <= 0d)
            {
                HideAll();
                return;
            }

            float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);

            // 抵消大球本体的缩放（= 半径 × 2），让子物体直接用世界单位摆。
            float ps = Mathf.Max(_ball.Radius * 2f, 1e-4f);
            transform.localScale = Vector3.one / ps;

            // 原文的两个 1.02 / 0.98 是「每 Algodoo 步」的倍率；
            // 换算成与帧率无关的写法（30fps 时和原式完全一致）。
            float step = dt * Cfg.VampireFxAlgodooFps;
            float angGain = Mathf.Pow(Cfg.VampireFxAngleGainPerStep, step);
            float distGain = Mathf.Pow(Cfg.VampireFxDistanceGainPerStep, step);

            // ① 每套自己的 poststep：沿自己的螺旋走一步，笔（TrailRenderer）自动跟着盖章
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var p = _active[i];
                p.Age += dt;
                if (p.Age >= Cfg.VampireFxLife) { Despawn(i); continue; }

                p.Angle *= angGain;
                p.Distance *= distGain;
                p.Tr.localPosition =
                    new Vector3(Mathf.Sin(p.Angle), Mathf.Cos(p.Angle), 0f) * p.Distance;
            }

            // ② poststep 里那次 addcircle + addpen：每步再新建一套
            SpawnDue();
        }

        /// <summary>每物理帧按 `VampireFxSpawnPerFrame` 新建（支持小数：攒够一套才建）。</summary>
        void SpawnDue()
        {
            float rate = Cfg.VampireFxSpawnPerFrame;
            if (rate <= 0f) return;

            _spawnCarry += rate;
            int n = Mathf.FloorToInt(_spawnCarry);
            if (n <= 0) return;
            _spawnCarry -= n;

            for (int k = 0; k < n; k++)
            {
                if (_active.Count >= Cfg.VampireFxMaxPens) break;
                SpawnOne();
            }
        }

        void SpawnOne()
        {
            var p = _free.Count > 0 ? _free.Pop() : NewPen();
            if (p == null) return;

            float ballR = _ball != null
                ? Mathf.Max(_ball.Radius, _ball.TargetRadiusWorld)
                : 1f;
            ballR = Mathf.Max(ballR, 0.05f);
            // 圆头：**固定尺寸**（和子弹差不多粗）。原文 `radius := 0.01` 本来就是绝对单位，
            // 不随球大小变；只有球特别小时按球半径 12% 夹一下，免得特效比球还大。
            float size = Mathf.Min(Cfg.VampireFxPenRadius * 2f, ballR * 0.24f);

            p.Go.SetActive(true);
            p.Age = 0f;
            // `_angle := rand.uniform01 * 2 * pi`，但给个下限：乘法增长下起始角趋近 0
            // 会让整套几乎不动、笔迹堆成一个点（看着就是「特效消失」）。
            p.Angle = Random.Range(Cfg.VampireFxAngleMin, Mathf.PI * 2f);
            p.Distance = ballR * Cfg.VampireFxOrbitStartFrac;  // `_distance := radius + 0.2`
            p.Tr.localScale = Vector3.one;
            p.Tr.localRotation = Quaternion.identity;
            p.Tr.localPosition =
                new Vector3(Mathf.Sin(p.Angle), Mathf.Cos(p.Angle), 0f) * p.Distance;

            p.Head.transform.localScale = new Vector3(size, size, 1f);
            p.Head.color = Cfg.VampireFxColor;            // 圆和笔同色，没有白圆头
            // 原文 `size := tx.radius * 2` 是等宽；用户要拖尾比本体再细一点。
            // 固定 0.02m，圆头被夹小（小球）时再按它的 50% 收一档，靠 alpha 渐隐。
            float trailW = Mathf.Min(Cfg.VampireFxTrailWidth, size * 0.5f);
            p.Trail.startWidth = trailW;
            p.Trail.endWidth = trailW;
            p.Trail.Clear();                              // 池复用：别把上一套的尾巴连过来
            p.Trail.emitting = true;

            AngleForSmoke = p.Angle;
            OrbitDistance = p.Distance;

            _active.Add(p);
            SpawnedTotal++;
        }

        /// <summary>建一套池对象（不激活）：addcircle 的圆 + addpen 的 TrailRenderer。</summary>
        Pen NewPen()
        {
            var go = new GameObject("Pen" + _created++);
            go.transform.SetParent(transform, false);

            var p = new Pen { Go = go, Tr = go.transform };
            p.Head = Primitives.MakeNode("Head", Primitives.Circle,
                Cfg.VampireFxColor, Cfg.VampireFxSortingOrder, go.transform);

            var tgo = new GameObject("PenTrail");
            tgo.transform.SetParent(go.transform, false);
            var tr = tgo.AddComponent<TrailRenderer>();
            tr.sharedMaterial = _trailMat;
            tr.colorGradient = _trailGradient;
            tr.time = Cfg.VampireFxTrailTime;             // `fadetime := 0.2`
            tr.minVertexDistance = Cfg.VampireFxTrailMinVertex;
            tr.startWidth = 0.02f;
            tr.endWidth = 0.02f;                          // SpawnOne 里按 size 重设
            tr.numCapVertices = 0;
            tr.numCornerVertices = 0;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.autodestruct = false;
            tr.emitting = false;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.sortingOrder = Cfg.VampireFxTrailSortingOrder;  // `tx.zdepth + 1`
            p.Trail = tr;

            go.SetActive(false);
            return p;
        }

        /// <summary>到 timetolive 了：整套关掉还池（笔也要清，否则复用时会连一条旧线）。</summary>
        void Despawn(int i)
        {
            var p = _active[i];
            p.Trail.emitting = false;
            p.Trail.Clear();
            p.Go.SetActive(false);

            int last = _active.Count - 1;
            _active[i] = _active[last];
            _active.RemoveAt(last);

            _free.Push(p);
        }

        void HideAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--) Despawn(i);
            _spawnCarry = 0f;
        }
    }
}
