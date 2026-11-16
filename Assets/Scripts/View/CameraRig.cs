using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 双相机：左栏看增倍区，右栏看战场。
    /// 右栏支持滚轮缩放 / WASD 平移，方便调规则的时候盯局面。
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera ZoneCam;
        public Camera BattleCam;
        /// <summary>左栏背景的游荡投影（可为 null）。</summary>
        public ZoneProjection Projection;

        Vector3 _battleHome;
        float _battleHomeSize;

        public static CameraRig Build(GameWorld world)
        {
            var go = new GameObject("CameraRig");
            var rig = go.AddComponent<CameraRig>();

            float leftW = Cfg.LeftPanelViewportWidth;

            // ── 左栏：增倍区 ──
            var zc = new GameObject("ZoneCamera").AddComponent<Camera>();
            zc.transform.SetParent(go.transform, false);
            zc.orthographic = true;
            zc.clearFlags = CameraClearFlags.SolidColor;
            zc.backgroundColor = Color.black;
            zc.rect = new Rect(0f, 0f, leftW, 1f);
            zc.depth = 1;
            zc.transform.position = new Vector3(Cfg.ZoneCenter.x, Cfg.ZoneCenter.y, -20f);
            // ★★ 规格（用户）：「让左边和右边两块**高度等高**，只缩放左边的，右半边不要动」。
            //
            //   右栏的战场是**正好顶到画面上下边**的：`FitSize(bw, bw, ...)` 里的 `bw`
            //   把 `BattleBoundThickness` 算进去了，而栏的宽高比 ≥ 1 时**高度那一脉胜出**
            //   → 可视高度 == bw → 边界墙的外沿刚好压在屏幕上下沿（占满 100% 栏高）。
            //
            //   左栏原来**高度也乘了 1.03 的留白** → 增倍区外框只占 97.1% 栏高，上下各空十几个像素，
            //   和右栏并排看就是「左矮右高」。
            //   所以现在**高度不留白、宽度仍留**：
            //     · 高度那一脉 = `ZoneOuterHeight / 2` → 外墙外沿正好压到屏幕上下沿 = 和右栏等高
            //     · 宽度那一脉 = `ZoneOuterWidth × 留白 / 2 ÷ 栏宽高比` → 栏特别窄时兜底，保证区不被裁
            //   （`FitSize` 取两者的 max：正常情况下高度胜出 → 等高；
            //     只有栏窄到宽高比 < 约 0.73 时宽度才会胜出，那时宁可矮一点也不裁边。）
            //
            //   ★ 右栏**一个字都没动** —— 合乎「右半边不要动」。
            zc.orthographicSize = FitSize(Cfg.ZoneOuterWidth * Cfg.LeftPanelWidthMargin,
                                          Cfg.ZoneOuterHeight, zc.rect);
            // ★ 纯 2D + 不用后处理：HDR / MSAA / 深度贴图全部关掉。
            //   这里的瓶颈是**填充率**（用户窗口 3402×1932 ≈ 660 万像素），
            //   任何一项开着都是白给 GPU 加一遍全屏的工作量。
            zc.allowHDR = false;
            zc.allowMSAA = false;
            zc.depthTextureMode = DepthTextureMode.None;
            zc.useOcclusionCulling = false;
            rig.ZoneCam = zc;

            // ── 右栏：战场 ──
            var bc = new GameObject("BattleCamera").AddComponent<Camera>();
            bc.transform.SetParent(go.transform, false);
            bc.orthographic = true;
            bc.clearFlags = CameraClearFlags.SolidColor;
            bc.backgroundColor = Color.black;
            bc.rect = new Rect(leftW, 0f, 1f - leftW, 1f);
            bc.depth = 0;
            bc.transform.position = new Vector3(Cfg.MapWorldSize * 0.5f, Cfg.MapWorldSize * 0.5f, -20f);
            // 把边界墙一起框进来（不然墙在视野外，看着就像「没有墙」）
            float bw = Cfg.MapWorldSize + Battlefield.BoundThickness * 2f;
            bc.orthographicSize = FitSize(bw, bw, bc.rect);
            bc.allowHDR = false;
            bc.allowMSAA = false;
            bc.depthTextureMode = DepthTextureMode.None;
            bc.useOcclusionCulling = false;
            rig.BattleCam = bc;

            rig._battleHome = bc.transform.position;
            rig._battleHomeSize = bc.orthographicSize;

            // ── 左栏背景：游荡的投影相机（规格：小方框在战场匀速游荡、碰墙反弹，
            //    框里的画面投影到增倍区背景上，透明度 25%）──
            var proj = ZoneProjection.Build(go.transform, world.Zone.transform,
                                            Cfg.ZoneCenter, 1f);
            // 铺满左栏：增倍区相机取景的世界宽/高各是多少
            float zoneH = zc.orthographicSize * 2f;
            float zoneW = zoneH * (Screen.width * leftW) / Mathf.Max(Screen.height, 1f);
            proj.FitToZone(Cfg.ZoneCenter.x, Cfg.ZoneCenter.y, zoneW, zoneH);
            rig.Projection = proj;
            return rig;
        }

        /// <summary>按 viewport 的宽高比算出「刚好装下 w×h」的正交尺寸。</summary>
        public static float FitSize(float w, float h, Rect viewport)
        {
            float vw = Mathf.Max(Screen.width * viewport.width, 1f);
            float vh = Mathf.Max(Screen.height * viewport.height, 1f);
            float aspect = vw / vh;
            return Mathf.Max(h * 0.5f, (w * 0.5f) / aspect);
        }

        void Update()
        {
            if (BattleCam == null) return;

            // 滚轮缩放（只在右栏范围内响应）
            Vector3 m = Input.mousePosition;
            bool overBattle = m.x > Screen.width * Cfg.LeftPanelViewportWidth;

            if (overBattle)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.0001f)
                {
                    BattleCam.orthographicSize = Mathf.Clamp(
                        BattleCam.orthographicSize * (1f - scroll * 0.8f), 8f, 400f);
                }
            }

            // WASD 平移
            float dx = 0f, dy = 0f;
            if (Input.GetKey(KeyCode.A)) dx -= 1f;
            if (Input.GetKey(KeyCode.D)) dx += 1f;
            if (Input.GetKey(KeyCode.S)) dy -= 1f;
            if (Input.GetKey(KeyCode.W)) dy += 1f;
            if (dx != 0f || dy != 0f)
            {
                float speed = BattleCam.orthographicSize * 1.6f * Time.deltaTime;
                BattleCam.transform.position += new Vector3(dx * speed, dy * speed, 0f);
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                BattleCam.transform.position = _battleHome;
                BattleCam.orthographicSize = _battleHomeSize;
            }
        }
    }
}
