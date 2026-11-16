using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 规格：**左栏背景的「游荡投影相机」**。
    ///
    /// 一个小方形取景框在战场里**匀速游荡**、碰到地图边界**原速反弹**，
    /// 框里看到的画面被投影到左边增倍区的背景上，**透明度 25%**。
    ///
    /// 做法：一台 `targetTexture` = RenderTexture 的相机（正方形的取景框），
    /// 再在增倍区里放一个好大的 SpriteRenderer 把这张贴图铺满左栏 ——
    /// 于是 25% 的透明度、图层关系都完全可控（相机之间的合成做不到"半透明叠加"）。
    ///
    /// 性能：投影相机**不看子弹层**（`Cfg.BulletLayer`）—— 战场里有上千颗子弹和拖尾，
    /// 那是画面上最贵的一块，而投影只是 25% 的底纹，不差它们。
    /// </summary>
    public class ZoneProjection : MonoBehaviour
    {
        Camera _cam;
        RenderTexture _rt;
        Transform _quad;
        Vector2 _pos;
        Vector2 _vel;

        public static ZoneProjection Build(Transform parent, Transform zoneRoot,
                                           Vector2 zoneCenter, float zoneViewW)
        {
            var go = new GameObject("ZoneProjection");
            if (parent != null) go.transform.SetParent(parent, false);
            var zp = go.AddComponent<ZoneProjection>();

            int res = Mathf.Max(Cfg.WanderCamResolution, 64);

            // ① 取景框相机
            var camGo = new GameObject("WanderCam");
            camGo.transform.SetParent(go.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Cfg.WanderCamSize * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f);
            cam.depth = -10f;                                 // 只负责往贴图里画，不参与主画面
            cam.cullingMask = ~(1 << Cfg.BulletLayer);        // 子弹/拖尾不参与投影（省一大笔）
            zp._cam = cam;
            // ★ 渲染贴图的**长宽比必须等于那块 quad 的长宽比**，否则球会被拉成椭圆
            //   （左栏是竖的、贴图是方的 → 圆球变竖椭圆。用户就是看到这个）。
            //   这里先按 1:1 建，FitToZone 拿到真实面板尺寸后会重建。
            zp.RebuildTarget(1f);
            zp._aspect = 1f;

            // 起点：地图中心，方向随机
            float half = Mathf.Max(Cfg.MapWorldSize * 0.5f - Cfg.WanderCamSize * 0.5f, 0.1f);
            zp._pos = new Vector2(Cfg.MapWorldSize * 0.5f, Cfg.MapWorldSize * 0.5f);
            float a = Random.Range(0f, Mathf.PI * 2f);
            zp._vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Cfg.WanderCamSpeed;
            zp._half = half;

            // ② 增倍区背景上的那层投影（一块 1×1 的 quad，铺满左栏）
            //    注意：SpriteRenderer 只吃 Texture2D，装不下 RenderTexture —— 所以用 MeshRenderer + 材质。
            var quadGo = new GameObject("ProjectionQuad");
            quadGo.transform.SetParent(zoneRoot != null ? zoneRoot : go.transform, false);
            zp._quad = quadGo.transform;

            var mf = quadGo.AddComponent<MeshFilter>();
            mf.sharedMesh = MakeQuad();
            zp._mr = quadGo.AddComponent<MeshRenderer>();
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            var mat = new Material(sh) { hideFlags = HideFlags.DontSave, name = "proj_mat" };
            mat.mainTexture = zp._rt;
            mat.color = new Color(1f, 1f, 1f, Cfg.WanderCamAlpha);   // ★ 25% 透明
            zp._mr.sharedMaterial = mat;
            zp._mr.sortingOrder = Cfg.WanderCamSortingOrder;
            zp._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            zp._mr.receiveShadows = false;
            return zp;
        }

        // ★ 世界会整棵重建（设置面板换阵营数 / 颜色），运行时 new 的
        //   RenderTexture / 材质 / quad 网格都得自己收，不然每重开一局漏一份。
        void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            if (_mr != null && _mr.sharedMaterial != null) Destroy(_mr.sharedMaterial);
            if (_quad != null)
            {
                var mf = _quad.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            }
        }

        MeshRenderer _mr;
        /// <summary>贴图当前的长宽比（跟着左栏面板走）。</summary>
        float _aspect = 1f;

        /// <summary>
        /// （重）建渲染贴图，长宽比 = <paramref name="aspect"/>。
        /// ★ 这个必须和那块 quad 的长宽比一致，不然投影里的圆就是椭圆。
        ///   只在面板比例变化时调用（分辨率变化 / 布局变化），不是每帧。
        /// </summary>
        void RebuildTarget(float aspect)
        {
            if (_cam == null) return;
            int w = Mathf.Max(Cfg.WanderCamResolution, 64);
            int h = Mathf.Clamp(Mathf.RoundToInt(w / Mathf.Max(aspect, 0.05f)), 64, 4096);

            if (_rt != null)
            {
                _cam.targetTexture = null;
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            { name = "wander_cam_rt", filterMode = FilterMode.Bilinear };
            _cam.targetTexture = _rt;
            // 贴图换了 → quad 的材质要指向新的那张
            if (_mr != null && _mr.sharedMaterial != null) _mr.sharedMaterial.mainTexture = _rt;
            // 相机自己是按贴图尺寸自动算 aspect 的，所以不用再手设 —— 只要两张尺寸对得上
            _cam.orthographicSize = Cfg.WanderCamSize * 0.5f;
            // 取景框的宽半幅（跟着 aspect 变，反弹边界要用）
            _halfW = Cfg.WanderCamSize * 0.5f * aspect;
        }

        float _halfW = Cfg.WanderCamSize * 0.5f;

        /// <summary>自检/UI 用：把战场上的一个点映射成投影取景框里的归一化坐标 (0..1)。</summary>
        public bool TryViewportUV(Vector3 world, out Vector2 uv)
        {
            uv = Vector2.zero;
            if (_cam == null) return false;
            Vector3 v = _cam.WorldToViewportPoint(world);
            if (v.z < 0f) return false;
            uv = new Vector2(v.x, v.y);
            return true;
        }

        /// <summary>投影方片（在增倍区里那块 quad）。</summary>
        public Transform Quad { get { return _quad; } }
        /// <summary>取景框相机（自检要看它的 targetTexture 尺寸）。</summary>
        public Camera Cam { get { return _cam; } }

        /// <summary>1×1 的方片（中心在原点），用 transform.localScale 定尺寸。</summary>
        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "proj_quad", hideFlags = HideFlags.DontSave };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),   new Vector3(0.5f, -0.5f, 0f)
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f)
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        float _half;

        void LateUpdate()
        {
            if (_cam == null) return;

            // ★ 优化：投影相机**每 WanderCamInterval 帧才渲一次**。
            //   它等于把整个战场再画一遍（只是不看子弹层），却是纯粹为了左边那层
            //   25% 透明的底纹 —— 每帧渲一遍等于白送一趟场景渲染。20Hz 的底纹看不出来。
            _cam.enabled = Cfg.WanderCamInterval <= 1
                           || (Time.frameCount % Mathf.Max(Cfg.WanderCamInterval, 1)) == 0;

            // 匀速直线 + 碰地图边界原速反弹（取景框整体留在地图内）
            float dt = Time.deltaTime;
            _pos += _vel * dt;
            float halfH = Mathf.Min(_half, Cfg.MapWorldSize * 0.5f);
            float halfW = Mathf.Min(_halfW, Cfg.MapWorldSize * 0.5f);
            if (_pos.x < halfW) { _pos.x = halfW; _vel.x = Mathf.Abs(_vel.x); }
            else if (_pos.x > Cfg.MapWorldSize - halfW) { _pos.x = Cfg.MapWorldSize - halfW; _vel.x = -Mathf.Abs(_vel.x); }
            if (_pos.y < halfH) { _pos.y = halfH; _vel.y = Mathf.Abs(_vel.y); }
            else if (_pos.y > Cfg.MapWorldSize - halfH) { _pos.y = Cfg.MapWorldSize - halfH; _vel.y = -Mathf.Abs(_vel.y); }

            _cam.transform.position = new Vector3(_pos.x, _pos.y, -40f);
        }

        /// <summary>把投影铺满左栏（由 CameraRig 在算完取景之后调）。</summary>
        public void FitToZone(float centerX, float centerY, float viewW, float viewH)
        {
            if (_quad == null) return;

            // ★ 贴图长宽比跟着面板走 —— 这一步是「球被拉长」的解药。
            float aspect = (viewH > 1e-4f) ? Mathf.Clamp(viewW / viewH, 0.15f, 8f) : 1f;
            if (Mathf.Abs(aspect - _aspect) > 0.002f)
            {
                _aspect = aspect;
                RebuildTarget(aspect);
            }
            // ★ 这块 quad 是挂在**增倍区 root** 下面的，而 root 本身就坐在 ZoneCenter 上 ——
            //   所以局部坐标必须是 0。之前写成 ZoneCenter 等于又偏了一次，
            //   整块投影跑到取景范围之外，难怪「背景上看不到东西」。
            _quad.localPosition = Vector3.zero;
            _quad.localScale = new Vector3(viewW, viewH, 1f);
        }

        /// <summary>自检用：投影方框现在在战场的哪里。</summary>
        public Vector2 WanderPos { get { return _pos; } }
        public bool HasTarget { get { return _cam != null && _cam.targetTexture != null; } }
        /// <summary>投影方块的世界坐标（自检用：必须正好落在增倍区中心）。</summary>
        public Vector3 QuadWorldPos { get { return _quad != null ? _quad.position : Vector3.zero; } }
        public float QuadAlpha
        {
            get { return _mr != null && _mr.sharedMaterial != null ? _mr.sharedMaterial.color.a : 0f; }
        }
    }
}
