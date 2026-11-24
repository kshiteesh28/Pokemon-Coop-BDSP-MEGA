#!/usr/bin/env python3
"""Sculpt & texture an authentic, AAA-grade Mega Charizard X (pm0006_01_00)
directly on top of Game Freak's official 56-bone BDSP Charizard (pm0006_00_00).

Run via Blender's bundled Python:
  /tmp/blender_hfs/Blender/Blender.app/Contents/MacOS/Blender -b --python scripts/build_mega_charizard_x.py
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import bpy
import numpy as np

REPO_ROOT = Path(__file__).resolve().parent.parent
TEX_SRC_DIR = REPO_ROOT / "build/ripped-pm0006/unity-project/ExportedProject/Assets/sandbox/models/01rg/pm0006_00_00/fbx/textures"
WORK_DIR = Path("/tmp/mega_x_build")
VAN_JSON = WORK_DIR / "van_pm0006.json"
OUT_MESH_JSON = WORK_DIR / "mega_x_mesh.json"
OUT_TEX_META = WORK_DIR / "textures.json"


def load_png_rgba(path: Path) -> np.ndarray:
    img = bpy.data.images.load(str(path), check_existing=False)
    w, h = img.size[0], img.size[1]
    arr = np.array(img.pixels[:], dtype=np.float32).reshape((h, w, 4))
    bpy.data.images.remove(img)
    return arr  # bottom-up (Unity RGBA32 order!)


def save_rgba_and_png(arr: np.ndarray, stem: str) -> tuple[int, int]:
    h, w, _ = arr.shape
    clamped = np.clip(arr, 0.0, 1.0)
    u8 = (clamped * 255.0 + 0.5).astype(np.uint8)
    (WORK_DIR / f"{stem}.raw").write_bytes(u8.tobytes())

    img = bpy.data.images.new(stem, width=w, height=h, alpha=True)
    img.pixels.foreach_set(clamped.ravel())
    img.filepath_raw = str(WORK_DIR / f"{stem}.png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    return w, h


def rgb_to_hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    maxc = np.maximum(np.maximum(r, g), b)
    minc = np.minimum(np.minimum(r, g), b)
    delta = maxc - minc
    s = np.where(maxc > 1e-6, delta / np.maximum(maxc, 1e-6), 0.0)
    h = np.zeros_like(maxc)
    mask = delta > 1e-6
    rm = mask & (maxc == r)
    gm = mask & (maxc == g)
    bm = mask & (maxc == b)
    h[rm] = (((g[rm] - b[rm]) / delta[rm]) % 6.0) * 60.0
    h[gm] = (((b[gm] - r[gm]) / delta[gm]) + 2.0) * 60.0
    h[bm] = (((r[bm] - g[bm]) / delta[bm]) + 4.0) * 60.0
    return h, s, maxc


def build_textures() -> dict[str, list[int]]:
    meta: dict[str, list[int]] = {}

    # 1. BodyA_col: Orange skin -> Charcoal-Black (#2C3038), Cream belly/tail-underside -> Sky-Blue (#7AC9F5)
    body_a = load_png_rgba(TEX_SRC_DIR / "pm0006_00_00_BodyA_col.png")
    h, s, v = rgb_to_hsv(body_a[..., :3])

    # Cream belly/tail underside: warm yellow-cream (H in [38, 65], moderate sat 0.14..0.46, bright v > 0.65)
    cream_mask = (h >= 36.0) & (h <= 68.0) & (s >= 0.12) & (s <= 0.48) & (v >= 0.62)
    # Orange body skin & shading creases: H in [15, 45], sat >= 0.35
    orange_mask = (h >= 12.0) & (h <= 46.0) & (s >= 0.34) & (~cream_mask)

    out_a = body_a.copy()
    # Map orange to sleek charcoal-black preserving luminance shading & creases
    lum_o = np.clip(v / 0.90, 0.35, 1.15)
    out_a[orange_mask, 0] = np.clip(0.185 * lum_o[orange_mask], 0.06, 0.32)
    out_a[orange_mask, 1] = np.clip(0.200 * lum_o[orange_mask], 0.07, 0.34)
    out_a[orange_mask, 2] = np.clip(0.235 * lum_o[orange_mask], 0.09, 0.39)

    # Map cream belly/tail underside to Mega Charizard X sky-blue (#7AC9F5)
    lum_c = np.clip(v / 0.92, 0.55, 1.10)
    out_a[cream_mask, 0] = np.clip(0.49 * lum_c[cream_mask], 0.25, 0.72)
    out_a[cream_mask, 1] = np.clip(0.80 * lum_c[cream_mask], 0.45, 0.94)
    out_a[cream_mask, 2] = np.clip(0.97 * lum_c[cream_mask], 0.60, 1.00)

    w, ht = save_rgba_and_png(out_a, "BodyA_col")
    meta["BodyA_col"] = [w, ht]

    # 2. BodyB_col: Orange arms/horns -> Charcoal-Black, Teal wing -> Deep Cobalt-Blue (#226099),
    #    Pink tongue/maroon mouth/white fangs untouched, PLUS dedicated Mega X swatches in unused top-right:
    #    - Charcoal swatch: u in [0.64, 0.78], v in [0.80, 0.98]
    #    - Glowing cyan-blue flame gradient: u in [0.80, 0.98], v in [0.80, 0.98] (v=0.80 bright cyan -> v=0.98 white-cyan core)
    body_b = load_png_rgba(TEX_SRC_DIR / "pm0006_00_00_BodyB_col.png")
    hb, sb, vb = rgb_to_hsv(body_b[..., :3])
    teal_mask = (hb >= 165.0) & (hb <= 215.0) & (sb >= 0.30)
    orange_b_mask = (hb >= 14.0) & (hb <= 46.0) & (sb >= 0.34)

    out_b = body_b.copy()
    lum_ob = np.clip(vb / 0.90, 0.35, 1.15)
    out_b[orange_b_mask, 0] = np.clip(0.185 * lum_ob[orange_b_mask], 0.06, 0.32)
    out_b[orange_b_mask, 1] = np.clip(0.200 * lum_ob[orange_b_mask], 0.07, 0.34)
    out_b[orange_b_mask, 2] = np.clip(0.235 * lum_ob[orange_b_mask], 0.09, 0.39)

    # Teal wing membrane -> Deep Cobalt-Blue (#22629C) preserving dark wing vein lines
    lum_tb = np.clip(vb / 0.70, 0.45, 1.15)
    out_b[teal_mask, 0] = np.clip(0.14 * lum_tb[teal_mask], 0.06, 0.28)
    out_b[teal_mask, 1] = np.clip(0.40 * lum_tb[teal_mask], 0.18, 0.62)
    out_b[teal_mask, 2] = np.clip(0.66 * lum_tb[teal_mask], 0.30, 0.88)

    # Paint clean Mega X Charcoal swatch & Electric Cyan-Blue flame gradient in unused top-right corner
    bh, bw, _ = out_b.shape
    y0, y1 = int(0.80 * bh), int(0.99 * bh)
    cx0, cx1 = int(0.64 * bw), int(0.78 * bw)
    fx0, fx1 = int(0.80 * bw), int(0.99 * bw)

    out_b[y0:y1, cx0:cx1, :3] = np.array([0.185, 0.200, 0.235], dtype=np.float32)
    out_b[y0:y1, cx0:cx1, 3] = 1.0

    # Vertical gradient in flame swatch: bottom (t=0) = deep electric cyan (#24A8FF), top (t=1) = white-cyan core (#E6FAFF)
    t = np.linspace(0.0, 1.0, y1 - y0, dtype=np.float32)[:, None]
    out_b[y0:y1, fx0:fx1, 0] = 0.16 + 0.76 * (t ** 1.3)
    out_b[y0:y1, fx0:fx1, 1] = 0.72 + 0.26 * (t ** 0.8)
    out_b[y0:y1, fx0:fx1, 2] = 1.00
    out_b[y0:y1, fx0:fx1, 3] = 1.0

    w, ht = save_rgba_and_png(out_b, "BodyB_col")
    meta["BodyB_col"] = [w, ht]

    # 3. BodyB_emi: Emissive glow strictly on the cyan-blue flame swatch!
    emi_b = np.zeros_like(out_b)
    emi_b[..., 3] = 1.0
    emi_b[y0:y1, fx0:fx1, 0] = 0.18 + 0.65 * (t ** 1.4)
    emi_b[y0:y1, fx0:fx1, 1] = 0.72 + 0.25 * (t ** 0.9)
    emi_b[y0:y1, fx0:fx1, 2] = 1.00
    w, ht = save_rgba_and_png(emi_b, "BodyB_emi")
    meta["BodyB_emi"] = [w, ht]

    # 4. Eye_col: Orange eyelid skin -> Charcoal-Black, Blue iris -> Fierce Crimson-Red (#E02222)
    eye = load_png_rgba(TEX_SRC_DIR / "pm0006_00_00_Eye_col.png")
    he, se, ve = rgb_to_hsv(eye[..., :3])
    eh, ew, _ = eye.shape
    # Avoid touching the bottom-right red placeholder block if present (bottom-up: y < 0.25*eh, x > 0.5*ew)
    orange_e = (he >= 14.0) & (he <= 46.0) & (se >= 0.34)
    blue_iris = (he >= 175.0) & (he <= 235.0) & (se >= 0.28)

    out_e = eye.copy()
    lum_oe = np.clip(ve / 0.90, 0.35, 1.15)
    out_e[orange_e, 0] = np.clip(0.185 * lum_oe[orange_e], 0.06, 0.32)
    out_e[orange_e, 1] = np.clip(0.200 * lum_oe[orange_e], 0.07, 0.34)
    out_e[orange_e, 2] = np.clip(0.235 * lum_oe[orange_e], 0.09, 0.39)

    lum_ie = np.clip(ve / 0.75, 0.35, 1.20)
    out_e[blue_iris, 0] = np.clip(0.92 * lum_ie[blue_iris], 0.35, 1.00)
    out_e[blue_iris, 1] = np.clip(0.13 * lum_ie[blue_iris], 0.04, 0.28)
    out_e[blue_iris, 2] = np.clip(0.13 * lum_ie[blue_iris], 0.04, 0.28)

    w, ht = save_rgba_and_png(out_e, "Eye_col")
    meta["Eye_col"] = [w, ht]

    OUT_TEX_META.write_text(json.dumps(meta, indent=2))
    return meta


def add_tapered_tube(
    verts, normals, uvs, weights, indices, tris,
    spine_pts: list[tuple[float, float, float]],
    radii: list[tuple[float, float]],
    bone_idx: int,
    uv_func,
    sides: int = 8,
    close_tip: bool = True,
):
    """Generates a smooth closed tapered organic spike, horn, or flame plume along a 3D spine."""
    base_v = len(verts)
    n_rings = len(spine_pts)
    pts = [np.array(p, dtype=np.float64) for p in spine_pts]

    rings = []
    for r_i in range(n_rings):
        p = pts[r_i]
        if r_i == 0:
            tang = pts[1] - pts[0]
        elif r_i == n_rings - 1:
            tang = pts[-1] - pts[-2]
        else:
            tang = pts[r_i + 1] - pts[r_i - 1]
        t_norm = np.linalg.norm(tang)
        tang = tang / (t_norm if t_norm > 1e-6 else 1.0)

        up = np.array([0.0, 1.0, 0.0]) if abs(tang[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
        side_a = np.cross(tang, up)
        side_a /= max(np.linalg.norm(side_a), 1e-6)
        side_b = np.cross(tang, side_a)
        side_b /= max(np.linalg.norm(side_b), 1e-6)

        ra, rb = radii[r_i]
        frac = r_i / max(n_rings - 1, 1)
        u, v = uv_func(frac)
        ring_idx = []
        for s_i in range(sides):
            ang = 2.0 * math.pi * s_i / sides
            ca, sa = math.cos(ang), math.sin(ang)
            nrm = side_a * ca + side_b * sa
            nrm /= max(np.linalg.norm(nrm), 1e-6)
            pos = p + side_a * (ra * ca) + side_b * (rb * sa)
            ring_idx.append(len(verts))
            verts.append([float(pos[0]), float(pos[1]), float(pos[2])])
            normals.append([float(nrm[0]), float(nrm[1]), float(nrm[2])])
            uvs.append([float(u), float(v)])
            weights.append([1.0, 0.0, 0.0, 0.0])
            indices.append([bone_idx, 0, 0, 0])
        rings.append(ring_idx)

    for r_i in range(n_rings - 1):
        r0, r1 = rings[r_i], rings[r_i + 1]
        for s_i in range(sides):
            sn = (s_i + 1) % sides
            tris.extend([r0[s_i], r1[s_i], r1[sn]])
            tris.extend([r0[s_i], r1[sn], r0[sn]])

    if close_tip:
        tip_idx = len(verts)
        p_tip = pts[-1]
        tang = pts[-1] - pts[-2]
        tang /= max(np.linalg.norm(tang), 1e-6)
        u, v = uv_func(1.0)
        verts.append([float(p_tip[0]), float(p_tip[1]), float(p_tip[2])])
        normals.append([float(tang[0]), float(tang[1]), float(tang[2])])
        uvs.append([float(u), float(v)])
        weights.append([1.0, 0.0, 0.0, 0.0])
        indices.append([bone_idx, 0, 0, 0])
        last = rings[-1]
        for s_i in range(sides):
            sn = (s_i + 1) % sides
            tris.extend([last[s_i], tip_idx, last[sn]])


def build_mesh() -> None:
    d = json.loads(VAN_JSON.read_text())
    verts = d["verts"]
    normals = d["normals"]
    uvs = d["uvs"]
    weights = d["weights"]
    indices = d["indices"]
    submeshes = d["submeshes"]

    # Bone indices verified from dump-vanilla-pm0006:
    # Neck1=2, Neck2=3, Neck3=4, Head=5, LShoulder=8, RShoulder=18
    # LFeeler1..6 = 28..33, RFeeler1..6 = 34..39
    wing_bones_l = set(range(28, 34))
    wing_bones_r = set(range(34, 40))
    wing_bones = wing_bones_l | wing_bones_r

    # 1. Reshape Charizard's wings into Mega Charizard X's iconic larger, jagged scalloped bat-dragon wings!
    #    - Expand wingspan ~16% outward & sweep slightly upward
    #    - Carve 3 sharp dragon-wing lobes along the bottom membrane edge (low Y on wing vertices)
    #    - Accentuate the top wing hook-claw around Feeler4 (|x|~0.55, y~1.56)
    for vi in range(len(verts)):
        b0 = indices[vi][0]
        w0 = weights[vi][0]
        if b0 in wing_bones and w0 > 0.35:
            x, y, z = verts[vi]
            sign = -1.0 if x < 0.0 else 1.0
            ax = abs(x)
            # Wing span parameter t in [0, 1] from shoulder (0.18) to tip (1.35)
            t = max(0.0, min(1.0, (ax - 0.18) / 1.15))
            # Slightly larger, more dramatic sweeping wingspan
            x = sign * (ax + 0.16 * (t ** 1.1))
            y = y + 0.08 * t

            # Scalloped bottom wing membrane lobes (below the upper wing arm y < 1.25)
            if y < 1.26 and t > 0.12:
                edge_factor = max(0.0, min(1.0, (1.26 - y) / 0.55))
                # 3 sharp lobes along the wing span
                lobe = math.sin(t * 3.0 * math.pi)
                # Pointed tips downward, deep notches upward
                y -= 0.16 * edge_factor * (0.45 * lobe + 0.25 * math.sin(t * 6.0 * math.pi))
                z -= 0.08 * edge_factor * lobe

            # Curved upper wing hook-claw apex near Feeler4 (ax in [0.42, 0.72], y > 1.48)
            if 0.42 <= ax <= 0.75 and y > 1.48:
                apex = 1.0 - abs(ax - 0.56) / 0.18
                if apex > 0.0:
                    y += 0.10 * apex
                    z += 0.07 * apex

            verts[vi] = [float(x), float(y), float(z)]

        # Slightly sharpen & elongate the twin head horns (Head=5, high Y > 1.44, rear Z < 0.44)
        elif b0 == 5 and w0 > 0.5:
            x, y, z = verts[vi]
            if y > 1.44 and z < 0.46 and abs(x) > 0.03:
                ht = max(0.0, min(1.0, (y - 1.44) / 0.22))
                y += 0.11 * ht
                z -= 0.09 * ht
                x += (0.035 if x > 0 else -0.035) * ht
                verts[vi] = [float(x), float(y), float(z)]

    # Append sculpted Mega Charizard X additions to submesh[0] (BodyB material, which has our charcoal & glowing cyan-blue swatches!)
    sub0 = submeshes[0]

    def uv_charcoal(_frac: float) -> tuple[float, float]:
        return (0.71, 0.89)

    def uv_blue_tipped(frac: float) -> tuple[float, float]:
        # Base 45% charcoal -> upper 55% bright glowing cyan-blue tip
        if frac < 0.45:
            return (0.71, 0.89)
        ft = (frac - 0.45) / 0.55
        return (0.89, 0.82 + 0.14 * ft)

    def uv_blue_flame(frac: float) -> tuple[float, float]:
        # Bright electric cyan-blue core at base -> glowing cyan-white flame tip
        return (0.89, 0.96 - 0.14 * frac)

    # 2. Seamless blue-tipped twin head horn extensions (rooted deep inside existing horns, weighted 100% to Head=5)
    for sign in (-1.0, 1.0):
        add_tapered_tube(
            verts, normals, uvs, weights, indices, sub0,
            spine_pts=[
                (sign * 0.085, 1.46, 0.37),
                (sign * 0.110, 1.56, 0.30),
                (sign * 0.132, 1.66, 0.22),
                (sign * 0.148, 1.76, 0.14),
            ],
            radii=[(0.036, 0.032), (0.028, 0.025), (0.016, 0.014), (0.003, 0.003)],
            bone_idx=5,
            uv_func=uv_blue_tipped,
            sides=8,
        )

    # 3. Four sharp charcoal-black dorsal neck fins along the back of the neck (Neck1=2, Neck2=3, Neck3=4)
    neck_fins = [
        (2, 0.99, 0.09, 0.070),
        (3, 1.11, 0.16, 0.080),
        (3, 1.21, 0.23, 0.082),
        (4, 1.31, 0.30, 0.068),
    ]
    for b_idx, ny, nz, length in neck_fins:
        add_tapered_tube(
            verts, normals, uvs, weights, indices, sub0,
            spine_pts=[
                (0.0, ny, nz),
                (0.0, ny + 0.030, nz - length * 0.55),
                (0.0, ny + 0.055, nz - length),
            ],
            radii=[(0.015, 0.030), (0.009, 0.018), (0.002, 0.004)],
            bone_idx=b_idx,
            uv_func=uv_charcoal,
            sides=6,
        )

    # 4. Twin curved blue-tipped shoulder spikes per side (front-top & rear-top, weighted to LShoulder=8 / RShoulder=18)
    for sign, sh_bone in ((-1.0, 8), (1.0, 18)):
        add_tapered_tube(
            verts, normals, uvs, weights, indices, sub0,
            spine_pts=[
                (sign * 0.17, 0.92, 0.12),
                (sign * 0.22, 1.00, 0.14),
                (sign * 0.25, 1.09, 0.16),
                (sign * 0.24, 1.16, 0.18),
            ],
            radii=[(0.042, 0.038), (0.030, 0.028), (0.017, 0.016), (0.003, 0.003)],
            bone_idx=sh_bone,
            uv_func=uv_blue_tipped,
            sides=8,
        )
        add_tapered_tube(
            verts, normals, uvs, weights, indices, sub0,
            spine_pts=[
                (sign * 0.18, 0.93, 0.01),
                (sign * 0.24, 1.02, -0.02),
                (sign * 0.28, 1.12, -0.05),
                (sign * 0.29, 1.19, -0.08),
            ],
            radii=[(0.040, 0.036), (0.028, 0.026), (0.015, 0.014), (0.003, 0.003)],
            bone_idx=sh_bone,
            uv_func=uv_blue_tipped,
            sides=8,
        )

    # 5. Twin organic S-curved glowing cyan-blue mouth flames streaming from the mouth corners (Head=5)
    for sign in (-1.0, 1.0):
        add_tapered_tube(
            verts, normals, uvs, weights, indices, sub0,
            spine_pts=[
                (sign * 0.075, 1.325, 0.615),
                (sign * 0.145, 1.345, 0.560),
                (sign * 0.225, 1.405, 0.480),
                (sign * 0.285, 1.455, 0.390),
                (sign * 0.355, 1.540, 0.310),
                (sign * 0.390, 1.640, 0.250),
            ],
            radii=[
                (0.026, 0.032),
                (0.040, 0.052),
                (0.036, 0.048),
                (0.026, 0.036),
                (0.014, 0.020),
                (0.002, 0.003),
            ],
            bone_idx=5,
            uv_func=uv_blue_flame,
            sides=8,
        )
        add_tapered_tube(
            verts, normals, uvs, weights, indices, sub0,
            spine_pts=[
                (sign * 0.170, 1.375, 0.515),
                (sign * 0.215, 1.455, 0.460),
                (sign * 0.240, 1.545, 0.405),
            ],
            radii=[(0.022, 0.026), (0.014, 0.018), (0.002, 0.003)],
            bone_idx=5,
            uv_func=uv_blue_flame,
            sides=6,
        )
        add_tapered_tube(
            verts, normals, uvs, weights, indices, sub0,
            spine_pts=[
                (sign * 0.250, 1.430, 0.435),
                (sign * 0.320, 1.445, 0.350),
                (sign * 0.375, 1.485, 0.280),
            ],
            radii=[(0.018, 0.022), (0.011, 0.014), (0.002, 0.003)],
            bone_idx=5,
            uv_func=uv_blue_flame,
            sides=6,
        )

    out_payload = {
        "verts": verts,
        "normals": normals,
        "uvs": uvs,
        "weights": weights,
        "indices": indices,
        "submeshes": submeshes,
    }
    OUT_MESH_JSON.write_text(json.dumps(out_payload))
    print(f"Sculpted Mega Charizard X mesh -> {OUT_MESH_JSON} (verts={len(verts)}, sub0_tris={len(sub0)//3})")


def main() -> None:
    WORK_DIR.mkdir(parents=True, exist_ok=True)
    meta = build_textures()
    print("Built Mega Charizard X RGBA32 textures:", meta)
    build_mesh()


if __name__ == "__main__":
    main()
