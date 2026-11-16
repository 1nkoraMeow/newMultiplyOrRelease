using System.Collections.Generic;
using UnityEngine;

namespace MultiplyOrRelease
{
    /// <summary>
    /// **所有子弹的拖尾用一张 Mesh 画**（规格见 `Cfg.BulletTrailUsesTrailRenderer`）。
    ///
    /// 为什么要这个：Unity 自带的 `TrailRenderer` 语义很标准（按时间采样、天然
    /// 「长度 = 速度 × 时间」），但**每颗子弹一个 Renderer 就是一次 draw call**。
    /// 场上两千颗子弹 → 两千次 draw call → 帧率直接掉到 15。
    ///
    /// 这里把「所有子弹的拖尾」拼进**同一张网格**：1 次 draw call。
    /// 几何是自己按「历史点 + 时间戳」算的，语义和 TrailRenderer 一一对应：
    ///   · **按时间过期** —— 点的存活时间 = 这颗子弹的 `TrailLife`（基础 `BulletTrailTime` × 数值的对数倍率），
    ///     所以「速度 × 时间」的长度语义、以及「数值耗尽后尾巴一点点收掉」都自动成立；
    ///   · **头粗尾细** —— 宽度沿尾巴从 `BulletTrailRadiusFactor` 插到 `BulletTrailEndRadiusFactor`，
    ///     再乘上贴图自带的锥度（`Primitives.Streak` 里尾端收到 10%），所以尾尖是收成一点的。
    ///   · **头白尾淡** —— 顶点 alpha 按 `BulletTrailHeadAlpha → BulletTrailTailAlpha` 渐隐；
    ///   · 贴图和 TrailRenderer 画法**共享同一张** `Primitives.Streak`。
    ///
    /// 图层沿用 `Cfg.BulletTrailSortingOrder`（8）：护盾之上、墙之下 —— 和换画法之前完全一致。
    /// </summary>
    public class BulletTrailRenderer : MonoBehaviour
    {
        public static BulletTrailRenderer Instance { get; private set; }

        MeshFilter _mf;
        MeshRenderer _mr;
        Mesh _mesh;

        Vector3[] _verts;
        Vector2[] _uvs;
        Color32[] _cols;
        int[] _tris;
        int _capVerts, _capQuads;

        public static BulletTrailRenderer Build(Transform parent)
        {
            var go = new GameObject("BulletTrailRenderer");
            if (parent != null) go.transform.SetParent(parent, false);
            // ★ 必须是子弹层：左栏那个「游荡投影」不看这一层。
            //   （默认层 0 的话，投影里也会画出满屏拖尾 —— 左栏背景会糊成一团。）
            go.layer = Cfg.BulletLayer;

            var r = go.AddComponent<BulletTrailRenderer>();
            r._mf = go.AddComponent<MeshFilter>();
            r._mr = go.AddComponent<MeshRenderer>();

            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            var mat = new Material(sh) { hideFlags = HideFlags.DontSave, name = "bullet_trail_mat" };
            mat.mainTexture = Primitives.Streak.texture;      // 和 TrailRenderer 画法同一张贴图
            r._mr.sharedMaterial = mat;

            r._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r._mr.receiveShadows = false;
            r._mr.sortingOrder = Cfg.BulletTrailSortingOrder;   // 护盾 7 < 拖尾 8 < 墙 15

            r._mesh = new Mesh { name = "bullet_trails", hideFlags = HideFlags.DontSave };
            // ★★ 索引格式必须是 **UInt32**。
            //   默认的 UInt16 只能索引 65535 个顶点，而这个网格是「所有子弹的尾巴拼一起」：
            //   三千颗子弹 × 十来个折点 × 2 就过 65535 了。超了之后 Unity **拒绝写入顶点数据**，
            //   而三角形索引还指着那些越界的下标 —— 屏幕上的表现就是
            //   **横穿整张地图的白色乱线**（正是用户截图里那团东西）。
            r._mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            r._mesh.MarkDynamic();
            r._mesh.subMeshCount = 1;
            r._mf.sharedMesh = r._mesh;

            // 地图范围已知，直接给常量包围盒（别用 RecalculateBounds）
            float half = Cfg.MapWorldSize * 0.5f + 30f;
            r._mesh.bounds = new Bounds(
                new Vector3(Cfg.MapWorldSize * 0.5f, Cfg.MapWorldSize * 0.5f, 0f),
                new Vector3(half * 2f, half * 2f, 1f));

            r.Alloc(16384, 8192);
            Instance = r;
            return r;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // ★ 世界会整棵重建（设置面板换阵营数 / 颜色），运行时资源自己收
            if (_mesh != null) Destroy(_mesh);
            if (_mr != null && _mr.sharedMaterial != null) Destroy(_mr.sharedMaterial);
        }

        void Alloc(int verts, int quads)
        {
            _capVerts = Mathf.Max(verts, 512);
            _capQuads = Mathf.Max(quads, 256);
            _verts = new Vector3[_capVerts];
            _uvs = new Vector2[_capVerts];
            _cols = new Color32[_capVerts];
            _tris = new int[_capQuads * 6];
        }

        /// <summary>扩容但**保留这一帧已经写进去的内容** —— 中途扩容不能把前面画好的丢了。</summary>
        void Grow(int needVerts, int needQuads)
        {
            if (needVerts <= _capVerts && needQuads <= _capQuads) return;

            int nv = _capVerts, nq = _capQuads;
            while (nv < needVerts) nv *= 2;
            while (nq < needQuads) nq *= 2;

            var verts = new Vector3[nv];
            var uvs = new Vector2[nv];
            var cols = new Color32[nv];
            var tris = new int[nq * 6];
            System.Array.Copy(_verts, verts, _verts.Length);
            System.Array.Copy(_uvs, uvs, _uvs.Length);
            System.Array.Copy(_cols, cols, _cols.Length);
            System.Array.Copy(_tris, tris, _tris.Length);
            _capVerts = nv; _capQuads = nq;
            _verts = verts; _uvs = uvs; _cols = cols; _tris = tris;
        }

        /// <summary>由 BulletPool 在 LateUpdate 里调一次。**必须包含拖尾正在收掉的子弹**（InUse=false 但还在场上的）。</summary>
        public void Render(List<Bullet> active)
        {
            if (_mesh == null || active == null) return;

            float now = Bullet.TrailClock;

            int v = 0, q = 0;

            // ★ 挤的时候先把形状降级：子弹一多，顶点数 = 子弹数 × 段数 × 4，四段顶不住。
            //   再配一道硬预算，超了就当颗不画（而不是把画面截掉一半）。
            int maxSeg = active.Count > Cfg.BulletTrailCrowdCount
                         ? 1 : Mathf.Max(Cfg.BulletTrailMaxSegments, 1);
            int budget = Mathf.Max(Cfg.BulletTrailVertexBudget, 16);

            for (int i = 0; i < active.Count; i++)
            {
                var b = active[i];
                if (b == null) continue;

                int n = ValidPointCount(b, now);
                if (n < 2) continue;
                if (v + 4 > budget) continue;        // 预算用完：这颗这一帧就不画拖尾了

                // ★ 每颗子弹的拖尾长度不同（按它自己的单发数值算，见 `Cfg.BulletTrailTimeFor`）
                float life = Mathf.Max(b.TrailLife, 0.0001f);

                // ★★ 抽稀：**每颗子弹最多 BulletTrailMaxSegments 段**。
                //   场上九千颗子弹时，13 个折点 × 2 顶点 × 九千 ≈ 二十三万顶点，
                //   光是把这些顶点写进数组、再拷进 Mesh 就是好几毫秒（而且不在 pool 的计时里）。
                //   而拖尾只是 0.9 米长、几个像素宽的一条细线 —— 抽成几段肉眼看不出来。
                int segs = Mathf.Min(n - 1, maxSeg);
                int use = segs + 1;

                Grow(v + use * 2, q + segs);

                int baseV = v;
                for (int k = 0; k < use; k++)
                {
                    // 在原序列里均匀取样（头、尾一定取到）
                    int si = (k == use - 1) ? n - 1
                                            : Mathf.RoundToInt((float)k * (n - 1) / segs);

                    // u：0 = 头（刚记的），1 = 尾（最老）
                    float u = Mathf.Clamp01((now - b.TrailT[si]) / life);
                    float hw = Mathf.Lerp(Cfg.BulletTrailRadiusFactor,
                                          Cfg.BulletTrailEndRadiusFactor, u) * b.VisualRadius;

                    // 这一点的切线方向（头/尾用相邻段，中间用前后两点）
                    Vector2 dir;
                    if (si == 0) dir = b.TrailPts[0] - b.TrailPts[1];
                    else if (si == n - 1) dir = b.TrailPts[n - 2] - b.TrailPts[n - 1];
                    else dir = b.TrailPts[si - 1] - b.TrailPts[si + 1];
                    if (dir.sqrMagnitude < 1e-10f) dir = Vector2.right;
                    Vector2 nrm = new Vector2(-dir.y, dir.x).normalized;

                    Vector2 p = b.TrailPts[si];
                    Color32 col = HeadTailColor(u, b.TrailColor);
                    float ux = u;                           // 贴图 x：0 = 头、1 = 尾尖

                    _verts[v] = new Vector3(p.x - nrm.x * hw, p.y - nrm.y * hw, 0f);
                    _uvs[v] = new Vector2(ux, 0f);
                    _cols[v] = col;
                    _verts[v + 1] = new Vector3(p.x + nrm.x * hw, p.y + nrm.y * hw, 0f);
                    _uvs[v + 1] = new Vector2(ux, 1f);
                    _cols[v + 1] = col;
                    v += 2;
                }

                for (int k = 0; k < segs; k++)
                {
                    int v0 = baseV + k * 2, t = q * 6;
                    _tris[t + 0] = v0 + 0; _tris[t + 1] = v0 + 1; _tris[t + 2] = v0 + 3;
                    _tris[t + 3] = v0 + 0; _tris[t + 4] = v0 + 3; _tris[t + 5] = v0 + 2;
                    q++;
                }
            }

            if (q == 0) { _mr.enabled = false; return; }
            _mr.enabled = true;

            _mesh.Clear(false);
            _mesh.subMeshCount = 1;
            _mesh.SetVertices(_verts, 0, v);
            _mesh.SetUVs(0, _uvs, 0, v);
            _mesh.SetColors(_cols, 0, v);
            _mesh.SetTriangles(_tris, 0, q * 6, 0, false);
        }

        /// <summary>
        /// 拖尾头→尾的透明度曲线。★ 第 102 轮（用户）：「子弹拖尾改成阵营颜色（但饱和度调低）」——
        /// 所以 RGB 用**子弹的拖尾色**（阵营色降饱和，见 `Bullet.TrailColor`），只有 alpha 走这条曲线。
        /// </summary>
        static Color32 HeadTailColor(float u, Color32 rgb)
        {
            float a;
            if (u <= 0.45f) a = Mathf.Lerp(Cfg.BulletTrailHeadAlpha, Cfg.BulletTrailHeadAlpha * 0.35f, u / 0.45f);
            else a = Mathf.Lerp(Cfg.BulletTrailHeadAlpha * 0.35f, Cfg.BulletTrailTailAlpha, (u - 0.45f) / 0.55f);
            byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(a) * 255f), 0, 255);
            return new Color32(rgb.r, rgb.g, rgb.b, v);
        }

        /// <summary>
        /// 把按时间过期的点从尾巴上剪掉，返回还有效的点数。
        /// **这就是「数值耗尽后拖尾逐渐缩短到没有」的实现** —— 不再加点之后，
        /// 尾部的点先到期、一个个掉，尾巴就一点点收回去。
        /// </summary>
        public static int TrimPoints(Bullet b, float now)
        {
            if (b == null || b.TrailPts == null) return 0;
            float life = Mathf.Max(b.TrailLife, 0.0001f);
            int n = b.TrailCount;
            while (n > 0 && (now - b.TrailT[n - 1]) > life) n--;

            // ★ 保险：**尾巴的长度有硬上限**。
            //   子弹最快也就 `BulletMaxSpeed`，`TrailLife` 秒内它**走**不了更远
            //   （走过的路程 ≥ 直线距离），所以离头超过这个距离的点**不可能是这条尾巴上的** ——
            //   一旦出现，说明时间戳出了岔子。有这一条，「横穿地图的白色乱线」就再也画不出来。
            float maxLen = Cfg.BulletMaxSpeed * life;
            float maxLen2 = maxLen * maxLen;
            if (n > 0)
            {
                Vector2 head = b.TrailPts[0];
                while (n > 1 && (b.TrailPts[n - 1] - head).sqrMagnitude > maxLen2) n--;
            }

            // ★ 第 64 轮曾在这里按「还买得起多长的路」剪短尾巴 ——
            //   用户明确说了**拖尾不需要改**，所以撤掉：尾巴就是尾巴，长度只由
            //   `TrailLife`（= 基础时间 × 数值的对数倍率）和速度决定。

            b.TrailCount = n;
            return n;
        }

        static int ValidPointCount(Bullet b, float now)
        {
            return TrimPoints(b, now);
        }
    }
}
