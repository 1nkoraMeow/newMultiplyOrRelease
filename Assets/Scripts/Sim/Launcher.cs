using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// **发射炮台**（第一版）。坐在 x16 阻块的正上方，有一根长方形炮管。
    ///
    /// 规格（用户）：炮口**只在 -65° ~ +65° 之间来回转**（0° = 正上方），
    /// 而且**按 sin 摆**（中间快、两端慢，端点自然停一下再折回，见 `Cfg.LauncherSweepOmega`）；
    /// 增倍球乘完倍 / 选完武器之后，就从**炮口**带着初速度打出去。
    /// </summary>
    public class Launcher : MonoBehaviour
    {
        /// <summary>当前偏角（度）。0 = 正上，正数 = 向右偏。</summary>
        public float AngleDeg { get; private set; }

        Transform _pivot;
        Transform _root;
        Vector3 _home;          // 该待的位置（x16 阻块上面）—— 后坐必须相对它做偏移
        /// <summary>sin 扫动的相位（弧度）：`AngleDeg = LauncherSweepAngle × sin(相位)`。</summary>
        float _sweepPhase;
        float _kick;

        public static Launcher Create(Transform parent, Vector2 localPos)
        {
            var go = new GameObject("Launcher");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var launcher = go.AddComponent<Launcher>();
            launcher._home = localPos;
            launcher._root = go.transform;

            // 本体：一个圆。★ 规格（用户）：**炮台颜色要和阻挡物一致**（不要自己一个浅灰）。
            var body = Primitives.MakeNode("Body", Primitives.Circle, Cfg.BlockerGrey, 12, go.transform);
            body.transform.localScale = Vector3.one * (Cfg.LauncherRadius * 2f);

            // 炮管：挂在 pivot 上，pivot 只负责转
            var pivot = new GameObject("BarrelPivot");
            pivot.transform.SetParent(go.transform, false);
            launcher._pivot = pivot.transform;

            var bar = Primitives.MakeNode("Barrel", Primitives.Square, Cfg.BlockerGrey, 13, pivot.transform);
            // ★★ 炮管是**竖着**的一根（长度沿 Y）—— 这个方块的宽度参数是 X、高度是 Y，
            //   以前这里把「长度」写到了 X 上，于是炮管永远是一根**横躺着的杠**
            //   （不管 pivot 怎么转）。用户连着两轮说「炮管还是横着的」就是它。
            bar.transform.localScale = new Vector3(Cfg.LauncherBarrelWidth, Cfg.LauncherBarrelLength, 1f);
            // 炮管从本体中心往外伸，中心在长度的一半处
            bar.transform.localPosition = new Vector3(0f, Cfg.LauncherBarrelLength * 0.5f, 0f);

            launcher.AngleDeg = 0f;
            launcher.Apply();
            return launcher;
        }

        void Update()
        {
            // ★ 用户：扫动改成 **sin 的非线性**（角度范围不变，仍是 ±LauncherSweepAngle）。
            //   角速度 = 幅值 × ω × cos(相位) —— 正上方最快、越靠两端越慢，端点处自然折回。
            //   相位一直往前推，不做「碰到边界就反向」的判断。
            _sweepPhase += Cfg.LauncherSweepOmega * Time.deltaTime;
            AngleDeg = Cfg.LauncherSweepAngle * Mathf.Sin(_sweepPhase);

            Apply();
        }

        /// <summary>发射时弹一下（视觉反馈）。</summary>
        public void Recoil() { _kick = 0.12f; }

        void Apply()
        {
            if (_pivot != null) _pivot.localRotation = Quaternion.Euler(0f, 0f, -AngleDeg);

            if (_root == null) return;
            if (_kick > 0f)
            {
                _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime * 0.9f);
                // ★ 后坐是相对**家**的偏移。以前这里直接写 Vector3.zero，
                //   等于每帧把炮台拽回增倍区正中心 —— 就是「炮台跑到中间去了」那个 bug。
                _root.localPosition = _home - (Vector3)(Direction * _kick);
            }
            else
            {
                _root.localPosition = _home;
            }
        }

        /// <summary>当前的发射方向（就是炮口朝向）。0° 朝上，正数向右偏。</summary>
        public Vector2 Direction
        {
            get
            {
                float r = AngleDeg * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            }
        }

        /// <summary>炮口的本地坐标（球就是从这里打出去的）。</summary>
        public Vector2 MuzzleLocalPosition
        {
            get { return (Vector2)transform.localPosition + Direction * Cfg.LauncherBarrelLength; }
        }
    }
}
