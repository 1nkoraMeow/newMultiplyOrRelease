#!/usr/bin/env python3
"""把 LayoutDump 导出的 layout.json 画成 PNG，用来看真实版面。"""
import json, sys
from PIL import Image, ImageDraw, ImageFont

SRC = sys.argv[1] if len(sys.argv) > 1 else "/tmp/mor-layout/layout.json"
OUT = sys.argv[2] if len(sys.argv) > 2 else "/tmp/mor-layout/layout.png"

d = json.load(open(SRC))
W, H = d["screen"]
img = Image.new("RGB", (W, H), (10, 10, 14))
dr = ImageDraw.Draw(img, "RGBA")

zone_w = d["zoneScreenRect"][2]
dr.rectangle([0, 0, zone_w, H], fill=(3, 3, 6))
dr.rectangle([zone_w, 0, W, H], fill=(6, 6, 10))

sprites = sorted(d["sprites"], key=lambda s: s["z"])
# 战场是一整张地图精灵（sortingOrder -100），用暗色打底而不是它的白色 tint
for s in sprites:
    if s["z"] <= -100:
        s["r"] = s["g"] = s["b"] = 0.055

# ★ 圆环精灵（prims_ring / prims_softring）必须画成**空心的环**，不能画成实心圆 ——
#   不然炮台的黑描边会被吞掉，离线图就跟游戏里长得不一样（骗人）。
#   值 = 内圆直径 / 外圆直径。对照 Primitives.CircleTexture 的 innerRatio：
#   Ring = CircleTexture(192, 0.90)，SoftRing 是羽化环，这里给 0.70。
RING_INNER = {"prims_ring": 0.90, "prims_softring": 0.70}
SS = 3   # 圆环超采样倍数（掩膜抗锯齿）

# ★★ 炮台外形（Primitives.TurretOutline / TurretFill）：**圆身子 ∪ 贴着的小圆头**，
#   参数必须和 Primitives.cs 里那份**一模一样**（否则离线图又会骗人）。
TURRET_SPAN = 1.5        # 贴图横跨 ±1.5 个「炮台半径」
TURRET_BODY_R = 1.0
TURRET_MUZZLE_DIST = 0.95
TURRET_MUZZLE_R = 0.55
TURRET_OUTLINE_W = 0.16


def turret_mask(size, outline):
    """按 Primitives.TurretShapeField 的场函数烘一张掩膜（outline=轮廓线，否则实心内部）。"""
    n = max(int(size) * SS, 8)
    px = [0] * (n * n)
    k = n / (2.0 * TURRET_SPAN)          # 每个「炮台半径」占多少像素
    aa = 1.5 / k                         # 1.5 像素换算成场单位
    half = TURRET_OUTLINE_W * 0.5
    for j in range(n):
        v = ((j + 0.5) / n - 0.5) * 2.0 * TURRET_SPAN
        for i in range(n):
            u = ((i + 0.5) / n - 0.5) * 2.0 * TURRET_SPAN
            f = max(TURRET_BODY_R - (u * u + v * v) ** 0.5,
                    TURRET_MUZZLE_R - ((u - TURRET_MUZZLE_DIST) ** 2 + v * v) ** 0.5)
            a = (half - abs(f)) / aa if outline else f / aa
            px[j * n + i] = 0 if a < 0 else (255 if a > 1 else int(a * 255))
    m = Image.new("L", (n, n))
    m.putdata(px)
    return m.resize((max(int(size), 2),) * 2, Image.LANCZOS)


def ellipse_mask(size, inner_frac):
    """外圆实心、内圆挖空的一张 L 掩膜（0 = 透明）。"""
    n = max(int(size) * SS, 2)
    m = Image.new("L", (n, n), 0)
    md = ImageDraw.Draw(m)
    md.ellipse([0, 0, n - 1, n - 1], fill=255)
    if inner_frac > 0:
        pad = n * (1.0 - inner_frac) / 2.0
        md.ellipse([pad, pad, n - 1 - pad, n - 1 - pad], fill=0)
    return m.resize((max(int(size), 2),) * 2, Image.LANCZOS)


# 逐帧按 z 序做 RGBA 合成（环要挖洞，不能直接往上糊）
img = img.convert("RGBA")
for s in sprites:
    x, y, w, h = s["x"], s["y"], s["w"], s["h"]
    col = (int(s["r"] * 255), int(s["g"] * 255), int(s["b"] * 255), int(s["a"] * 255))
    if s["d"] < 1.2 or col[3] == 0:
        continue
    box = [x - w / 2, y - h / 2, x + w / 2, y + h / 2]
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    spr = s.get("spr", "")
    rot = s.get("rot", 0.0)
    if s.get("shape") == "rect":
        ld.rectangle(box, fill=col)
    elif spr in ("prims_turretshape", "prims_turretfill"):
        size = max(int(max(w, h)), 2)
        mask = turret_mask(size, spr == "prims_turretshape")
        # Unity 的正角度是世界坐标里的逆时针；屏幕 y 朝下 → 画的时候要反过来
        if rot:
            mask = mask.rotate(-rot, resample=Image.BILINEAR, expand=False)
        sw, sh = mask.size
        solid = Image.new("RGBA", (sw, sh), col)
        layer.paste(solid, (int(round(x - sw / 2)), int(round(y - sh / 2))), mask)
    else:
        inner = RING_INNER.get(spr, 0.0)
        if inner > 0:
            size = max(int(max(w, h)), 2)
            mask = ellipse_mask(size, inner)
            sw, sh = mask.size
            solid = Image.new("RGBA", (sw, sh), col)
            layer.paste(solid, (int(round(x - sw / 2)), int(round(y - sh / 2))), mask)
        else:
            ld.ellipse(box, fill=col)
    img = Image.alpha_composite(img, layer)
img = img.convert("RGB")
dr = ImageDraw.Draw(img)

FONT_CANDIDATES = [
    ("/System/Library/Fonts/PingFang.ttc", 1),
    ("/System/Library/Fonts/Helvetica.ttc", 0),
    ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf", 0),
]
def get_font(size):
    for path, idx in FONT_CANDIDATES:
        try:
            return ImageFont.truetype(path, size, index=idx)
        except Exception:
            continue
    return ImageFont.load_default()

cache = {}
for t in d["texts"]:
    size = max(int(t["size"]), 6)
    if size not in cache:
        cache[size] = get_font(size)
    f = cache[size]
    col = (int(t["r"] * 255), int(t["g"] * 255), int(t["b"] * 255))
    txt = t["text"]
    bbox = dr.textbbox((0, 0), txt, font=f)
    tw = bbox[2] - bbox[0]
    x = t["x"] - tw / 2
    y = t["y"] - bbox[1]                       # y 现在是文字顶边
    outline = (0, 0, 0) if sum(col) > 240 else (255, 255, 255)
    for ox, oy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        dr.text((x + ox, y + oy), txt, font=f, fill=outline)
    dr.text((x, y), txt, font=f, fill=col)

dr.rectangle([0, 0, zone_w - 1, H - 1], outline=(45, 45, 55))
img.save(OUT)
print("saved", OUT, "sprites=", len(sprites), "texts=", len(d["texts"]))
