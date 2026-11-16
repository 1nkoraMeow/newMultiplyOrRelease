using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// **所有子弹的「小圆点」用一个 Mesh 画**。
    ///
    /// 为什么要这样：一颗子弹的圆点如果各挂一个 SpriteRenderer，
    /// 两千颗就是两千个 Renderer 的剔除/排序/合批 —— 齐射一次立刻掉帧。
    /// 现在这里只出一张网格：每颗子弹 4 个顶点（一个正方形，贴圆形贴图），
    /// 顶点数组预分配、零 GC，包围盒给常量。
    ///
    /// ★ 拖尾**不在这里**了 —— 规格要求用 Unity 自带的 `TrailRenderer`，
    ///   见 `Bullet.CreateBlank`。这里只管圆点。
    /// </summary>
    public class BulletRenderer : MonoBehaviour
    {
        public static BulletRenderer Instance { get; private set; }

        MeshFilter _mf;
        MeshRenderer _mr;
        Mesh _mesh;

        Vector3[] _verts;
        Vector2[] _uvs;
        Color32[] _cols;
        int[] _tris;
        int _cap;

        public static BulletRenderer Build(Transform parent)
        {
            var go = new GameObject("BulletRenderer");
            if (parent != null) go.transform.SetParent(parent, false);
            // 和拖尾一样放子弹层：左栏的「游荡投影」不看这一层
            go.layer = Cfg.BulletLayer;

            var r = go.AddComponent<BulletRenderer>();
            r._mf = go.AddComponent<MeshFilter>();
            r._mr = go.AddComponent<MeshRenderer>();

            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            var matDot = new Material(sh) { hideFlags = HideFlags.DontSave };
            matDot.mainTexture = Primitives.Circle.texture;
            r._mr.sharedMaterial = matDot;

            r._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r._mr.receiveShadows = false;
            // 圆点在墙之上、在大球（60）之下；拖尾另行排在墙之下（见 Cfg.BulletTrailSortingOrder）
            r._mr.sortingOrder = Cfg.BulletDotSortingOrder;

            r.Alloc(Cfg.BulletRenderCapacity);
            Instance = r;
            return r;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // ★ 设置面板「换阵营数 / 换颜色」会整棵树拆掉重搭 —— 运行时 new 出来的
            //   网格和材质不跟着 GameObject 走，必须自己 Destroy，否则每重开一局漏一份。
            if (_mesh != null) Destroy(_mesh);
            if (_mr != null && _mr.sharedMaterial != null) Destroy(_mr.sharedMaterial);
        }

        void Alloc(int cap)
        {
            _cap = Mathf.Max(cap, 64);
            int v = _cap * 4;              // 每颗子弹 4 个圆点顶点
            _verts = new Vector3[v];
            _uvs = new Vector2[v];
            _cols = new Color32[v];
            _tris = new int[_cap * 6];
            for (int i = 0; i < _cap; i++)
            {
                int v0 = i * 4, t = i * 6;
                _tris[t + 0] = v0 + 0; _tris[t + 1] = v0 + 1; _tris[t + 2] = v0 + 2;
                _tris[t + 3] = v0 + 0; _tris[t + 4] = v0 + 2; _tris[t + 5] = v0 + 3;
            }

            _mesh = new Mesh { name = "bullets", hideFlags = HideFlags.DontSave };
            // ★ 同上：**不用子弹上限**，所以顶点数迟早会过 65535 ——
            //   默认 UInt16 索引到那时会写不进顶点、三角形全乱。
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.MarkDynamic();
            _mesh.subMeshCount = 1;
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.colors32 = _cols;
            _mf.sharedMesh = _mesh;
        }

        /// <summary>由 BulletPool 在 LateUpdate 里调一次。alpha = 物理步之间的插值系数。</summary>
        public void Render(List<Bullet> active, float alpha)
        {
            if (_mesh == null || active == null) return;
            // 规格：子弹没有上限 —— 顶点缓冲也不设上限，超了就扩容
            if (active.Count > _cap) Alloc(Mathf.NextPowerOfTwo(active.Count + 64));

            int n = Mathf.Min(active.Count, _cap);
            int live = 0;

            for (int i = 0; i < n; i++)
            {
                var b = active[i];
                if (b == null || !b.InUse) continue;

                // 插值后的位置：和 transform / 拖尾用的是同一个点，所以三者不会错开
                Vector2 p = b.RenderedPos(alpha);
                // ★ 近防炮弹丸：**白色 + 更粗**，和普通子弹（阵营色小圆点）一眼分开。
                float r = b.IsCiws ? b.VisualRadius * Cfg.CiwsBulletRadiusScale : b.VisualRadius;
                int v = live * 4;
                Color32 col = b.IsCiws ? (Color32)Cfg.CiwsBulletColor : b.VisualColor;

                _verts[v + 0] = new Vector3(p.x - r, p.y - r, 0f);
                _verts[v + 1] = new Vector3(p.x - r, p.y + r, 0f);
                _verts[v + 2] = new Vector3(p.x + r, p.y + r, 0f);
                _verts[v + 3] = new Vector3(p.x + r, p.y - r, 0f);
                _uvs[v + 0] = new Vector2(0f, 0f);
                _uvs[v + 1] = new Vector2(0f, 1f);
                _uvs[v + 2] = new Vector2(1f, 1f);
                _uvs[v + 3] = new Vector2(1f, 0f);
                _cols[v + 0] = col; _cols[v + 1] = col; _cols[v + 2] = col; _cols[v + 3] = col;
                live++;
            }

            if (live == 0) { _mr.enabled = false; return; }
            _mr.enabled = true;

            _mesh.Clear(false);
            _mesh.subMeshCount = 1;
            _mesh.SetVertices(_verts, 0, live * 4);
            _mesh.SetUVs(0, _uvs, 0, live * 4);
            _mesh.SetColors(_cols, 0, live * 4);
            _mesh.SetTriangles(_tris, 0, live * 6, 0, false);

            // 别用 RecalculateBounds：地图范围是已知的，直接给常量
            float half = Cfg.MapWorldSize * 0.5f + 30f;
            _mesh.bounds = new Bounds(
                new Vector3(Cfg.MapWorldSize * 0.5f, Cfg.MapWorldSize * 0.5f, 0f),
                new Vector3(half * 2f, half * 2f, 1f));
        }
    }
}
