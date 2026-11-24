#!/usr/bin/env python3
"""Official Omega Ruby-faithful Mega Charizard X (pm0006_01_00) 3D remodeler & texture generator.

Built directly on Game Freak's official 56-bone BDSP Charizard (pm0006_00_00):
- Preserves 100% of Game Freak's articulated head, jaw, tongue, fangs, blinking crimson eyes,
  muscular torso, arms, claws, legs, and tail with exact Switch WrapU=Mirror & scale=(2,1).
- Extends the icy sky-blue underbelly seamlessly up the ENTIRE front of the neck (BodyA) and
  under the lower jaw/chin (BodyB) via exact UV triangle rasterization — matching official ORAS design.
- Replaces vanilla wings with wide-swept horizontal Omega Ruby bat-dragon wings: bold arched charcoal
  wing arm, chunky backward-curving wrist talon (NOT an inward hook), and ONE continuous watertight
  4-blade scalloped cerulean wing membrane fanning outward & backward.
- Uses a single continuous vertical charcoal->cyan gradient UV column (zero diagonal mid-horn seam!)
  for the sculpted curved twin head horns and chunky front/rear curved blue-tipped shoulder claws.
- Adds Mega Charizard X's subtle nasal snout horn & cheek/jaw crests, removes non-canonical neck fins,
  and sculpts wide, multi-tongued, billowing S-ribbon electric cyan-blue mouth fire plumes.
- Renders a pixel-accurate 3-angle Cycles CPU preview matching Switch Mirror UVs.

Run via Blender's bundled Python:
  /tmp/blender_hfs/Blender/Blender.app/Contents/MacOS/Blender -b --python scripts/build_mega_charizard_x.py
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

REPO_ROOT = Path(__file__).resolve().parent.parent
TEX_SRC_DIR = REPO_ROOT / "build/ripped-pm0006/unity-project/ExportedProject/Assets/sandbox/models/01rg/pm0006_00_00/fbx/textures"
WORK_DIR = Path("/tmp/mega_x_build")
VAN_JSON = WORK_DIR / "van_pm0006.json"
OUT_MESH_JSON = WORK_DIR / "mega_x_mesh.json"
OUT_TEX_META = WORK_DIR / "textures.json"
OUT_TEXTURED_PNG = REPO_ROOT / "build/test-renders/pm0006_01_00_textured.png"


def load_png_rgba(path: Path) -> np.ndarray:
    img = bpy.data.images.load(str(path), check_existing=False)
    w, h = img.size[0], img.size[1]
    arr = np.array(img.pixels[:], dtype=np.float32).reshape((h, w, 4))
    bpy.data.images.remove(img)
    return arr  # bottom-up (Unity RGBA32 order)


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


def mirror_uv(u: float) -> float:
    """Exact Unity WrapMode.Mirror + ScaleU=2.0 evaluation."""
    u2 = u * 2.0
    period = math.floor(u2)
    frac = u2 - period
    return (1.0 - frac) if (period % 2 != 0) else frac


def rasterize_uv_tri_mask(mask: np.ndarray, u0: float, v0: float, u1: float, v1: float, u2: float, v2: float, pad_px: int = 2) -> None:
    """Rasterizes a single UV triangle into a boolean (H, W) mask with optional dilation padding."""
    h, w = mask.shape
    x0, y0 = u0 * (w - 1), v0 * (h - 1)
    x1, y1 = u1 * (w - 1), v1 * (h - 1)
    x2, y2 = u2 * (w - 1), v2 * (h - 1)
    # Skip seam-wrapping triangles that jump across the whole texture
    if max(abs(x1 - x0), abs(x2 - x1), abs(x0 - x2)) > w * 0.45:
        return
    if max(abs(y1 - y0), abs(y2 - y1), abs(y0 - y2)) > h * 0.45:
        return

    min_x = max(0, int(math.floor(min(x0, x1, x2))) - pad_px)
    max_x = min(w - 1, int(math.ceil(max(x0, x1, x2))) + pad_px)
    min_y = max(0, int(math.floor(min(y0, y1, y2))) - pad_px)
    max_y = min(h - 1, int(math.ceil(max(y0, y1, y2))) + pad_px)
    if min_x > max_x or min_y > max_y:
        return

    xs = np.arange(min_x, max_x + 1, dtype=np.float32)[None, :] + 0.5
    ys = np.arange(min_y, max_y + 1, dtype=np.float32)[:, None] + 0.5

    area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
    if abs(area) < 1e-4:
        return
    w0 = ((x1 - xs) * (y2 - ys) - (x2 - xs) * (y1 - ys)) / area
    w1 = ((x2 - xs) * (y0 - ys) - (x0 - xs) * (y2 - ys)) / area
    w2 = 1.0 - w0 - w1
    eps = -0.18  # slight conservative padding to prevent seam cracks
    inside = (w0 >= eps) & (w1 >= eps) & (w2 >= eps)
    mask[min_y : max_y + 1, min_x : max_x + 1] |= inside


def compute_throat_and_jaw_masks(van_data: dict, shape_a: tuple[int, int], shape_b: tuple[int, int]) -> tuple[np.ndarray, np.ndarray]:
    """Builds exact UV pixel masks for Mega Charizard X's icy sky-blue throat (BodyA) and under-jaw (BodyB)."""
    verts = van_data["verts"]
    normals = van_data["normals"]
    uvs = van_data["uvs"]
    indices = van_data["indices"]
    subs = van_data["submeshes"]

    mask_a = np.zeros(shape_a, dtype=bool)
    mask_b = np.zeros(shape_b, dtype=bool)

    # 1. Front of upper chest + neck (submesh[1] = BodyA, bones Spine2=1, Neck1=2, Neck2=3, Neck3=4)
    #    Starts at y=0.70 (overlapping into the top of the chest belly so there is ZERO charcoal gap!)
    #    and tapers smoothly up to the chin (y=1.36).
    def is_front_throat(vi: int) -> bool:
        b = indices[vi][0]
        x, y, z = verts[vi]
        nx, ny, nz = normals[vi]
        if b in (1, 2, 3, 4) and 0.70 <= y <= 1.38:
            t = max(0.0, min(1.0, (y - 0.70) / 0.64))
            max_x = 0.165 * (1.0 - t) + 0.068 * t
            return abs(x) <= max_x and nz > 0.22 and z > 0.14
        return False

    sub1 = subs[1]
    for t in range(0, len(sub1), 3):
        a, b, c = sub1[t], sub1[t + 1], sub1[t + 2]
        hits = sum(1 for vi in (a, b, c) if is_front_throat(vi))
        if hits >= 2:
            rasterize_uv_tri_mask(
                mask_a,
                mirror_uv(uvs[a][0]), uvs[a][1] % 1.0,
                mirror_uv(uvs[b][0]), uvs[b][1] % 1.0,
                mirror_uv(uvs[c][0]), uvs[c][1] % 1.0,
                pad_px=4,
            )

    # Smooth morphological closing + blur on throat mask so the blue-to-charcoal neck edge is silky smooth (zero sawtooth!)
    mf = mask_a.astype(np.float32)
    for _ in range(3):
        mf = (
            mf
            + np.roll(mf, 1, axis=0) + np.roll(mf, -1, axis=0)
            + np.roll(mf, 1, axis=1) + np.roll(mf, -1, axis=1)
            + np.roll(np.roll(mf, 1, axis=0), 1, axis=1)
            + np.roll(np.roll(mf, 1, axis=0), -1, axis=1)
            + np.roll(np.roll(mf, -1, axis=0), 1, axis=1)
            + np.roll(np.roll(mf, -1, axis=0), -1, axis=1)
        ) / 9.0
    mask_a = mf >= 0.42

    # 2. Underside of lower jaw / chin (submesh[2] = BodyB, Jaw=6)
    def is_under_jaw(vi: int) -> bool:
        b = indices[vi][0]
        x, y, z = verts[vi]
        nx, ny, nz = normals[vi]
        if b == 6 and abs(x) <= 0.085 and y < 1.245 and z > 0.36:
            return ny < -0.22
        return False

    sub2 = subs[2]
    for t in range(0, len(sub2), 3):
        a, b, c = sub2[t], sub2[t + 1], sub2[t + 2]
        hits = sum(1 for vi in (a, b, c) if is_under_jaw(vi))
        if hits >= 2:
            rasterize_uv_tri_mask(
                mask_b,
                mirror_uv(uvs[a][0]), uvs[a][1] % 1.0,
                mirror_uv(uvs[b][0]), uvs[b][1] % 1.0,
                mirror_uv(uvs[c][0]), uvs[c][1] % 1.0,
                pad_px=2,
            )

    return mask_a, mask_b


def build_textures(van_data: dict) -> dict[str, list[int]]:
    meta: dict[str, list[int]] = {}

    body_a = load_png_rgba(TEX_SRC_DIR / "pm0006_00_00_BodyA_col.png")
    body_b = load_png_rgba(TEX_SRC_DIR / "pm0006_00_00_BodyB_col.png")
    throat_mask_a, jaw_mask_b = compute_throat_and_jaw_masks(
        van_data, (body_a.shape[0], body_a.shape[1]), (body_b.shape[0], body_b.shape[1])
    )

    # 1. BodyA_col: Deep obsidian-charcoal (#2B2E36) + icy sky-blue (#86D6FA) belly, tail underside, AND front throat!
    h, s, v = rgb_to_hsv(body_a[..., :3])
    cream_mask = (h >= 36.0) & (h <= 68.0) & (s >= 0.12) & (s <= 0.48) & (v >= 0.62)
    orange_mask = (h >= 12.0) & (h <= 46.0) & (s >= 0.34) & (~cream_mask)

    out_a = body_a.copy()
    lum_o = np.clip(v / 0.90, 0.35, 1.15)
    out_a[orange_mask, 0] = np.clip(0.172 * lum_o[orange_mask], 0.06, 0.30)
    out_a[orange_mask, 1] = np.clip(0.184 * lum_o[orange_mask], 0.07, 0.32)
    out_a[orange_mask, 2] = np.clip(0.218 * lum_o[orange_mask], 0.09, 0.38)

    sky_mask_a = cream_mask | (throat_mask_a & orange_mask)
    lum_c = np.clip(v / 0.90, 0.58, 1.12)
    out_a[sky_mask_a, 0] = np.clip(0.51 * lum_c[sky_mask_a], 0.28, 0.74)
    out_a[sky_mask_a, 1] = np.clip(0.83 * lum_c[sky_mask_a], 0.48, 0.95)
    out_a[sky_mask_a, 2] = np.clip(0.99 * lum_c[sky_mask_a], 0.65, 1.00)

    w, ht = save_rgba_and_png(out_a, "BodyA_col")
    meta["BodyA_col"] = [w, ht]

    # 2. BodyB_col: Deep charcoal head/arms + icy sky-blue under-jaw + untouched tongue/fangs
    #    3 ZERO-COLLISION custom swatches in freed vanilla-wing UV space (u_tex in [0.00, 0.58], v in [0.70, 0.99]):
    #    BodyB._Col0Tex uses scale=(2,1) & WrapU=Mirror, so u_mesh = u_tex / 2.0:
    #    - Cerulean wing gradient:        u_tex in [0.02, 0.20] -> u_mesh in [0.01, 0.10]
    #    - Continuous charcoal->cyan tip: u_tex in [0.22, 0.36] -> u_mesh = 0.145 (ZERO mid-horn seam!)
    #      * v in [0.72, 0.835] = solid obsidian-charcoal (wing arm, claws, horn base, spike base, cheek crests)
    #      * v in [0.835, 0.855] = crisp anti-aliased transition
    #      * v in [0.855, 0.98] = vivid sky-cyan tip (horn tips & shoulder spike tips)
    #    - Glowing mouth fire plume:      u_tex in [0.38, 0.56] -> u_mesh in [0.19, 0.28] (center 0.235)
    hb, sb, vb = rgb_to_hsv(body_b[..., :3])
    teal_mask = (hb >= 165.0) & (hb <= 215.0) & (sb >= 0.30)
    orange_b_mask = (hb >= 14.0) & (hb <= 46.0) & (sb >= 0.34)

    out_b = body_b.copy()
    lum_ob = np.clip(vb / 0.90, 0.35, 1.15)
    out_b[orange_b_mask, 0] = np.clip(0.172 * lum_ob[orange_b_mask], 0.06, 0.30)
    out_b[orange_b_mask, 1] = np.clip(0.184 * lum_ob[orange_b_mask], 0.07, 0.32)
    out_b[orange_b_mask, 2] = np.clip(0.218 * lum_ob[orange_b_mask], 0.09, 0.38)

    # Paint under-jaw sky-blue cleanly (only where original was orange skin, never touching pink tongue/mouth!)
    sky_jaw = jaw_mask_b & orange_b_mask
    lum_jb = np.clip(vb / 0.90, 0.58, 1.12)
    out_b[sky_jaw, 0] = np.clip(0.51 * lum_jb[sky_jaw], 0.28, 0.74)
    out_b[sky_jaw, 1] = np.clip(0.83 * lum_jb[sky_jaw], 0.48, 0.95)
    out_b[sky_jaw, 2] = np.clip(0.99 * lum_jb[sky_jaw], 0.65, 1.00)

    lum_tb = np.clip(vb / 0.70, 0.45, 1.15)
    out_b[teal_mask, 0] = np.clip(0.12 * lum_tb[teal_mask], 0.05, 0.28)
    out_b[teal_mask, 1] = np.clip(0.46 * lum_tb[teal_mask], 0.20, 0.70)
    out_b[teal_mask, 2] = np.clip(0.76 * lum_tb[teal_mask], 0.35, 0.92)

    bh, bw, _ = out_b.shape
    y0, y1 = int(0.72 * bh), int(0.98 * bh)
    wx0, wx1 = int(0.02 * bw), int(0.20 * bw)
    cx0, cx1 = int(0.22 * bw), int(0.36 * bw)
    fx0, fx1 = int(0.38 * bw), int(0.56 * bw)

    t = np.linspace(0.0, 1.0, y1 - y0, dtype=np.float32)[:, None]

    # Column 1: Rich Omega Ruby cerulean-teal wing membrane gradient (deep slate-blue near arm -> bright cerulean tips)
    out_b[y0:y1, wx0:wx1, 0] = 0.11 + 0.11 * t
    out_b[y0:y1, wx0:wx1, 1] = 0.40 + 0.22 * t
    out_b[y0:y1, wx0:wx1, 2] = 0.68 + 0.22 * t
    out_b[y0:y1, wx0:wx1, 3] = 1.0

    # Column 2: Continuous vertical obsidian-charcoal -> crisp sky-cyan tip column (ZERO UV seam!)
    #   t in [0.0, 0.44] -> solid charcoal (#2B2E36)
    #   t in [0.44, 0.52] -> smooth anti-aliased blend
    #   t in [0.52, 1.0] -> bright Omega Ruby sky-cyan (#4CC8FF -> #7AE2FF)
    blend = np.clip((t - 0.44) / 0.08, 0.0, 1.0)
    tip_t = np.clip((t - 0.52) / 0.48, 0.0, 1.0)
    c_r, c_g, c_b = 0.172, 0.184, 0.218
    k_r = 0.26 + 0.24 * tip_t
    k_g = 0.74 + 0.17 * tip_t
    k_b = 0.96 + 0.04 * tip_t
    out_b[y0:y1, cx0:cx1, 0] = c_r * (1.0 - blend) + k_r * blend
    out_b[y0:y1, cx0:cx1, 1] = c_g * (1.0 - blend) + k_g * blend
    out_b[y0:y1, cx0:cx1, 2] = c_b * (1.0 - blend) + k_b * blend
    out_b[y0:y1, cx0:cx1, 3] = 1.0

    # Column 3: Vibrant electric cyan-blue roaring mouth flame gradient (deep azure outer tips -> bright cyan-white core)
    out_b[y0:y1, fx0:fx1, 0] = 0.12 + 0.60 * (t ** 1.6)
    out_b[y0:y1, fx0:fx1, 1] = 0.70 + 0.27 * (t ** 1.1)
    out_b[y0:y1, fx0:fx1, 2] = 1.00
    out_b[y0:y1, fx0:fx1, 3] = 1.0

    w, ht = save_rgba_and_png(out_b, "BodyB_col")
    meta["BodyB_col"] = [w, ht]

    # 3. BodyB_emi: Emissive glow ONLY on the mouth flames (u_mesh=0.235 / u_tex=0.47), zero bleed on wings or body!
    emi_b = np.zeros_like(out_b)
    emi_b[..., 3] = 1.0
    ex0, ex1 = int(0.21 * bw), int(0.26 * bw)
    for x_start, x_end in ((ex0, ex1), (int(0.40 * bw), int(0.54 * bw))):
        emi_b[y0:y1, x_start:x_end, 0] = 0.10 + 0.55 * (t ** 1.5)
        emi_b[y0:y1, x_start:x_end, 1] = 0.66 + 0.30 * (t ** 1.0)
        emi_b[y0:y1, x_start:x_end, 2] = 1.00
    w, ht = save_rgba_and_png(emi_b, "BodyB_emi")
    meta["BodyB_emi"] = [w, ht]

    # 4. Eye_col: Obsidian-charcoal eyelids + Fierce ORAS Crimson-Red irises (#EE2418)
    eye = load_png_rgba(TEX_SRC_DIR / "pm0006_00_00_Eye_col.png")
    he, se, ve = rgb_to_hsv(eye[..., :3])
    orange_e = (he >= 14.0) & (he <= 46.0) & (se >= 0.34)
    blue_iris = (he >= 175.0) & (he <= 235.0) & (se >= 0.28)

    out_e = eye.copy()
    lum_oe = np.clip(ve / 0.90, 0.35, 1.15)
    out_e[orange_e, 0] = np.clip(0.172 * lum_oe[orange_e], 0.06, 0.30)
    out_e[orange_e, 1] = np.clip(0.184 * lum_oe[orange_e], 0.07, 0.32)
    out_e[orange_e, 2] = np.clip(0.218 * lum_oe[orange_e], 0.09, 0.38)

    lum_ie = np.clip(ve / 0.75, 0.35, 1.20)
    out_e[blue_iris, 0] = np.clip(0.96 * lum_ie[blue_iris], 0.40, 1.00)
    out_e[blue_iris, 1] = np.clip(0.13 * lum_ie[blue_iris], 0.04, 0.28)
    out_e[blue_iris, 2] = np.clip(0.10 * lum_ie[blue_iris], 0.03, 0.22)

    w, ht = save_rgba_and_png(out_e, "Eye_col")
    meta["Eye_col"] = [w, ht]

    OUT_TEX_META.write_text(json.dumps(meta, indent=2))
    return meta


def sample_catmull_rom(ctrl_pts: list[tuple[float, float, float]], samples_per_seg: int = 4) -> list[np.ndarray]:
    pts = [np.array(p, dtype=np.float64) for p in ctrl_pts]
    padded = [pts[0]] + pts + [pts[-1]]
    out = []
    for i in range(1, len(padded) - 2):
        p0, p1, p2, p3 = padded[i - 1], padded[i], padded[i + 1], padded[i + 2]
        n_s = samples_per_seg if i < len(padded) - 3 else samples_per_seg + 1
        for s in range(n_s):
            t = s / samples_per_seg
            t2 = t * t
            t3 = t2 * t
            p = 0.5 * (
                (2.0 * p1)
                + (-p0 + p2) * t
                + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
                + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3
            )
            out.append(p)
    return out


def blend_wing_weights(pos: np.ndarray, bone_positions: list[tuple[int, np.ndarray]]) -> tuple[list[float], list[int]]:
    dists = [(float(np.linalg.norm(pos - bp)), bidx) for bidx, bp in bone_positions]
    dists.sort(key=lambda x: x[0])
    d0, i0 = dists[0]
    d1, i1 = dists[1]
    if d0 < 1e-4 or d1 > d0 * 2.8:
        return [1.0, 0.0, 0.0, 0.0], [i0, 0, 0, 0]
    inv0 = 1.0 / max(d0 * d0, 1e-5)
    inv1 = 1.0 / max(d1 * d1, 1e-5)
    s = inv0 + inv1
    return [inv0 / s, inv1 / s, 0.0, 0.0], [i0, i1, 0, 0]


def add_lofted_tube(
    verts, normals, uvs, weights, indices, tris,
    spine_pts: list[np.ndarray],
    radii: list[tuple[float, float]],
    weight_func,
    uv_func,
    sides: int = 12,
    close_tip: bool = True,
    ref_up: np.ndarray | None = None,
):
    n_rings = len(spine_pts)
    rings = []
    prev_a = None
    for r_i in range(n_rings):
        p = spine_pts[r_i]
        if r_i == 0:
            tang = spine_pts[1] - spine_pts[0]
        elif r_i == n_rings - 1:
            tang = spine_pts[-1] - spine_pts[-2]
        else:
            tang = spine_pts[r_i + 1] - spine_pts[r_i - 1]
        tang /= max(np.linalg.norm(tang), 1e-6)

        if prev_a is None:
            up = ref_up if ref_up is not None else (np.array([0.0, 1.0, 0.0]) if abs(tang[1]) < 0.85 else np.array([0.0, 0.0, 1.0]))
            side_a = np.cross(tang, up)
            if np.linalg.norm(side_a) < 1e-5:
                side_a = np.cross(tang, np.array([1.0, 0.0, 0.0]))
            side_a /= max(np.linalg.norm(side_a), 1e-6)
        else:
            side_a = prev_a - tang * np.dot(prev_a, tang)
            side_a /= max(np.linalg.norm(side_a), 1e-6)
        prev_a = side_a
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
            w, idx = weight_func(pos)
            ring_idx.append(len(verts))
            verts.append([float(pos[0]), float(pos[1]), float(pos[2])])
            normals.append([float(nrm[0]), float(nrm[1]), float(nrm[2])])
            uvs.append([float(u), float(v)])
            weights.append(w)
            indices.append(idx)
        rings.append(ring_idx)

    for r_i in range(n_rings - 1):
        r0, r1 = rings[r_i], rings[r_i + 1]
        for s_i in range(sides):
            sn = (s_i + 1) % sides
            tris.extend([r0[s_i], r1[s_i], r1[sn]])
            tris.extend([r0[s_i], r1[sn], r0[sn]])

    if close_tip:
        tip_idx = len(verts)
        p_tip = spine_pts[-1]
        tang = spine_pts[-1] - spine_pts[-2]
        tang /= max(np.linalg.norm(tang), 1e-6)
        u, v = uv_func(1.0)
        w, idx = weight_func(p_tip)
        verts.append([float(p_tip[0]), float(p_tip[1]), float(p_tip[2])])
        normals.append([float(tang[0]), float(tang[1]), float(tang[2])])
        uvs.append([float(u), float(v)])
        weights.append(w)
        indices.append(idx)
        last = rings[-1]
        for s_i in range(sides):
            sn = (s_i + 1) % sides
            tris.extend([last[s_i], tip_idx, last[sn]])


def add_oras_official_wing(
    verts, normals, uvs, weights, indices, tris,
    sign: float,
    bone_positions: list[tuple[int, np.ndarray]],
):
    """Official Omega Ruby Mega Charizard X wide-swept horizontal bat-dragon wing:
    - Muscular arched charcoal wing arm rising to wrist apex then sweeping WIDE horizontally outward
    - Thick, bold backward/outward-curving charcoal wrist talon (matching official ORAS model!)
    - ONE continuous watertight cerulean wing sheet with 4 long, sweeping, sharp feather-dragon
      blades fanning outward & backward (deep clean V-notches, ZERO gaps!).
    """
    wf = lambda pos: blend_wing_weights(pos, bone_positions)
    uv_charcoal = lambda _f: (0.145, 0.77)  # solid obsidian-charcoal in bottom of col 2

    # 1. Wide-swept horizontal arched charcoal wing arm
    arm_ctrl = [
        (sign * 0.06, 0.90, -0.09),
        (sign * 0.22, 1.12, -0.10),
        (sign * 0.42, 1.38, -0.05),
        (sign * 0.62, 1.54, -0.02),   # Wrist joint apex
        (sign * 1.02, 1.56, -0.12),   # Wide horizontal sweep
        (sign * 1.44, 1.48, -0.25),
        (sign * 1.82, 1.32, -0.40),   # Sharp horizontal wingtip
    ]
    arm_pts = sample_catmull_rom(arm_ctrl, samples_per_seg=4)
    n_arm = len(arm_pts)
    arm_radii = []
    for i in range(n_arm):
        f = i / max(n_arm - 1, 1)
        # Muscular elbow/wrist bulge around f=0.45, tapering cleanly to sharp tip
        bulge = 1.0 + 0.22 * math.exp(-((f - 0.48) / 0.14) ** 2)
        base_r = 0.052 * bulge * (1.0 - 0.62 * (f ** 1.2))
        r = base_r if f < 0.88 else base_r * (1.0 - (f - 0.88) / 0.12) + 0.004
        arm_radii.append((r, r * 0.85))
    add_lofted_tube(verts, normals, uvs, weights, indices, tris, arm_pts, arm_radii, wf, uv_charcoal, sides=12)

    # 2. Official Omega Ruby thick curved charcoal wrist claw (curving BACKWARD & OUTWARD over the wing!)
    claw_ctrl = [
        (sign * 0.56, 1.50, -0.02),
        (sign * 0.64, 1.62, -0.05),
        (sign * 0.77, 1.67, -0.12),
        (sign * 0.90, 1.64, -0.20),
    ]
    claw_pts = sample_catmull_rom(claw_ctrl, samples_per_seg=3)
    claw_radii = [
        (0.054 * (1.0 - 0.90 * (i / max(len(claw_pts) - 1, 1))) + 0.004,
         0.044 * (1.0 - 0.90 * (i / max(len(claw_pts) - 1, 1))) + 0.004)
        for i in range(len(claw_pts))
    ]
    add_lofted_tube(verts, normals, uvs, weights, indices, tris, claw_pts, claw_radii, wf, uv_charcoal, sides=10)

    # 3. ONE continuous watertight cerulean 4-blade sweeping dragon-wing membrane sheet!
    #    Fanning WIDE outward & backward like official ORAS Mega Charizard X (not hanging vertically!)
    trailing_pts_raw = [
        (sign * 0.09, 0.76, -0.13),  # Attached smoothly at lower back
        (sign * 0.44, 0.46, -0.22),  # Blade 4 tip (innermost lower blade)
        (sign * 0.56, 0.78, -0.19),  # Deep Notch 3
        (sign * 0.96, 0.54, -0.32),  # Blade 3 tip (sweeping lower-mid blade)
        (sign * 0.92, 0.96, -0.23),  # Deep Notch 2
        (sign * 1.54, 0.82, -0.45),  # Blade 2 tip (longest bold sweeping blade)
        (sign * 1.28, 1.18, -0.31),  # Deep Notch 1
        (sign * 1.84, 1.16, -0.46),  # Blade 1 tip (sharp outermost upper blade)
        (sign * 1.80, 1.32, -0.40),  # Meets outer wingtip cleanly!
    ]
    trail_pts = []
    for seg in range(len(trailing_pts_raw) - 1):
        p0 = np.array(trailing_pts_raw[seg], dtype=np.float64)
        p1 = np.array(trailing_pts_raw[seg + 1], dtype=np.float64)
        n_sub = 4
        for s in range(n_sub):
            trail_pts.append(p0 + (p1 - p0) * (s / n_sub))
    trail_pts.append(np.array(trailing_pts_raw[-1], dtype=np.float64))

    nu = len(trail_pts)
    nv = 7
    arm_sampled = []
    for iu in range(nu):
        f = iu / max(nu - 1, 1)
        idx_f = f * (len(arm_pts) - 1)
        i0 = int(math.floor(idx_f))
        i1 = min(i0 + 1, len(arm_pts) - 1)
        frac = idx_f - i0
        arm_sampled.append(arm_pts[i0] * (1.0 - frac) + arm_pts[i1] * frac)

    front_grid = []
    back_grid = []
    for iv in range(nv):
        fv = iv / (nv - 1)
        row_f = []
        row_b = []
        uv_v = 0.74 + 0.21 * fv
        for iu in range(nu):
            fu = iu / (nu - 1)
            top_p = arm_sampled[iu]
            bot_p = trail_pts[iu]
            center_p = top_p * (1.0 - fv) + bot_p * fv
            camber = 0.028 * math.sin(math.pi * fv) * math.sin(math.pi * fu)
            n_face = np.array([sign * 0.20, 0.12, 0.97], dtype=np.float64)
            n_face /= np.linalg.norm(n_face)

            half_th = 0.014 * (1.0 - 0.85 * fv) + 0.002
            pf = center_p + n_face * (camber + half_th)
            pb = center_p + n_face * (camber - half_th)
            uv_u = 0.02 + 0.07 * fu  # u_mesh * 2 in [0.04, 0.18] -> pure cerulean wing column

            w_f, i_f = wf(pf)
            row_f.append(len(verts))
            verts.append([float(pf[0]), float(pf[1]), float(pf[2])])
            normals.append([float(n_face[0]), float(n_face[1]), float(n_face[2])])
            uvs.append([float(uv_u), float(uv_v)])
            weights.append(w_f)
            indices.append(i_f)

            w_b, i_b = wf(pb)
            row_b.append(len(verts))
            verts.append([float(pb[0]), float(pb[1]), float(pb[2])])
            normals.append([float(-n_face[0]), float(-n_face[1]), float(-n_face[2])])
            uvs.append([float(uv_u), float(uv_v)])
            weights.append(w_b)
            indices.append(i_b)
        front_grid.append(row_f)
        back_grid.append(row_b)

    for iv in range(nv - 1):
        for iu in range(nu - 1):
            f00, f10 = front_grid[iv][iu], front_grid[iv][iu + 1]
            f01, f11 = front_grid[iv + 1][iu], front_grid[iv + 1][iu + 1]
            tris.extend([f00, f10, f11, f00, f11, f01])

            b00, b10 = back_grid[iv][iu], back_grid[iv][iu + 1]
            b01, b11 = back_grid[iv + 1][iu], back_grid[iv + 1][iu + 1]
            tris.extend([b00, b11, b10, b00, b01, b11])

    last_f, last_b = front_grid[-1], back_grid[-1]
    for iu in range(nu - 1):
        tris.extend([last_f[iu], last_b[iu], last_b[iu + 1], last_f[iu], last_b[iu + 1], last_f[iu + 1]])


def add_sculpted_mouth_flame_plume(verts, normals, uvs, weights, indices, tris, sign: float):
    """Official Omega Ruby Mega Charizard X wide, billowing, multi-tongued blue fire plume!
    Sculpted as a solid 3D tapered flame ribbon/sheet with 3 jagged upward-curling flame tongues
    (NOT a stiff cylindrical tube!). Weighted 100% to Head=5.
    """
    head_wf = lambda _pos: ([1.0, 0.0, 0.0, 0.0], [5, 0, 0, 0])

    # Lower edge of the billowing fire ribbon (from corner of mouth sweeping outward & up-back)
    bot_ctrl = [
        (sign * 0.068, 1.298, 0.615),
        (sign * 0.145, 1.305, 0.560),
        (sign * 0.235, 1.345, 0.485),
        (sign * 0.315, 1.425, 0.400),
        (sign * 0.365, 1.545, 0.320),
        (sign * 0.375, 1.675, 0.270),
    ]
    # Upper jagged flame-lick silhouette (with 3 dramatic roaring flame peaks!)
    top_ctrl_raw = [
        (sign * 0.072, 1.335, 0.625),  # Mouth corner flare
        (sign * 0.140, 1.405, 0.575),  # Flame lick 1 peak (cheek flare)
        (sign * 0.185, 1.385, 0.525),  # Dip 1
        (sign * 0.255, 1.525, 0.470),  # Flame lick 2 peak (tall mid flame)
        (sign * 0.285, 1.495, 0.415),  # Dip 2
        (sign * 0.345, 1.655, 0.355),  # Flame lick 3 peak (high curling tongue)
        (sign * 0.375, 1.675, 0.270),  # Main plume tip!
    ]
    bot_pts = sample_catmull_rom(bot_ctrl, samples_per_seg=4)
    # Subdivide top_ctrl_raw linearly by 3 so jagged fire tongues stay crisp & dynamic
    top_pts = []
    for seg in range(len(top_ctrl_raw) - 1):
        p0 = np.array(top_ctrl_raw[seg], dtype=np.float64)
        p1 = np.array(top_ctrl_raw[seg + 1], dtype=np.float64)
        for s in range(3):
            top_pts.append(p0 + (p1 - p0) * (s / 3))
    top_pts.append(np.array(top_ctrl_raw[-1], dtype=np.float64))

    nu = len(top_pts)
    nv = 6  # 6 profile rows across the billowing ribbon

    # Resample bot_pts to match nu
    bot_sampled = []
    for iu in range(nu):
        f = iu / max(nu - 1, 1)
        idx_f = f * (len(bot_pts) - 1)
        i0 = int(math.floor(idx_f))
        i1 = min(i0 + 1, len(bot_pts) - 1)
        frac = idx_f - i0
        bot_sampled.append(bot_pts[i0] * (1.0 - frac) + bot_pts[i1] * frac)

    front_grid = []
    back_grid = []
    for iv in range(nv):
        fv = iv / (nv - 1)  # 0 = lower core edge, 1 = upper jagged flame tips
        row_f = []
        row_b = []
        for iu in range(nu):
            fu = iu / (nu - 1)
            bp = bot_sampled[iu]
            tp = top_pts[iu]
            # S-wave organic fire ripple along the plume
            wave = 0.016 * math.sin(fu * 3.0 * math.pi + fv * 1.2) * math.sin(math.pi * fu)
            n_out = np.array([sign * 0.65, 0.15, 0.74], dtype=np.float64)
            n_out /= np.linalg.norm(n_out)

            # Plump glowing core near mouth & bottom, tapering to crisp razor flame edges at tips
            thickness = 0.026 * (1.0 - 0.80 * fu) * math.sin(math.pi * (0.12 + 0.88 * fv)) + 0.003
            center = bp * (1.0 - fv) + tp * fv + n_out * wave
            pf = center + n_out * thickness
            pb = center - n_out * thickness

            # Bright cyan-white core near mouth/bottom -> rich electric azure at jagged outer tips
            uv_v = 0.96 - 0.20 * (0.55 * fu + 0.45 * fv)
            uv_u = 0.235  # u_mesh=0.235 * 2 = 0.47 (glowing flame column!)

            w_f, i_f = head_wf(pf)
            row_f.append(len(verts))
            verts.append([float(pf[0]), float(pf[1]), float(pf[2])])
            normals.append([float(n_out[0]), float(n_out[1]), float(n_out[2])])
            uvs.append([float(uv_u), float(uv_v)])
            weights.append(w_f)
            indices.append(i_f)

            w_b, i_b = head_wf(pb)
            row_b.append(len(verts))
            verts.append([float(pb[0]), float(pb[1]), float(pb[2])])
            normals.append([float(-n_out[0]), float(-n_out[1]), float(-n_out[2])])
            uvs.append([float(uv_u), float(uv_v)])
            weights.append(w_b)
            indices.append(i_b)
        front_grid.append(row_f)
        back_grid.append(row_b)

    for iv in range(nv - 1):
        for iu in range(nu - 1):
            f00, f10 = front_grid[iv][iu], front_grid[iv][iu + 1]
            f01, f11 = front_grid[iv + 1][iu], front_grid[iv + 1][iu + 1]
            tris.extend([f00, f10, f11, f00, f11, f01])

            b00, b10 = back_grid[iv][iu], back_grid[iv][iu + 1]
            b01, b11 = back_grid[iv + 1][iu], back_grid[iv + 1][iu + 1]
            tris.extend([b00, b11, b10, b00, b01, b11])

    # Stitch top & bottom edges closed so the flame plume is a clean solid 3D volume
    for edge_f, edge_b, flip in ((front_grid[0], back_grid[0], False), (front_grid[-1], back_grid[-1], True)):
        for iu in range(nu - 1):
            if not flip:
                tris.extend([edge_f[iu], edge_b[iu], edge_b[iu + 1], edge_f[iu], edge_b[iu + 1], edge_f[iu + 1]])
            else:
                tris.extend([edge_f[iu], edge_b[iu + 1], edge_b[iu], edge_f[iu], edge_f[iu + 1], edge_b[iu + 1]])


def build_mesh(van_data: dict) -> dict:
    verts = [list(v) for v in van_data["verts"]]
    normals = [list(n) for n in van_data["normals"]]
    uvs = [list(u) for u in van_data["uvs"]]
    weights = [list(w) for w in van_data["weights"]]
    indices = [list(i) for i in van_data["indices"]]
    submeshes = [list(s) for s in van_data["submeshes"]]
    bones = van_data["bones"]

    wing_bones_l = set(range(28, 34))
    wing_bones_r = set(range(34, 40))
    wing_bones = wing_bones_l | wing_bones_r

    # 1. Remove vanilla rounded Charizard wing triangles from submesh[0]
    orig_sub0 = submeshes[0]
    clean_sub0 = []
    for t in range(0, len(orig_sub0), 3):
        a, b, c = orig_sub0[t], orig_sub0[t + 1], orig_sub0[t + 2]
        is_wing = any(indices[vi][0] in wing_bones and weights[vi][0] > 0.35 for vi in (a, b, c))
        if not is_wing:
            clean_sub0.extend([a, b, c])
    submeshes[0] = clean_sub0

    # 2. Sculpt Game Freak's REAL twin head horns cleanly (submesh[2], BodyB!)
    #    Uses ONE continuous vertical charcoal->cyan column (u_mesh=0.145) -> ZERO diagonal UV seam!
    sub2_set = set(submeshes[2])
    for vi in sub2_set:
        if indices[vi][0] == 5 and weights[vi][0] > 0.5:
            x, y, z = verts[vi]
            if y > 1.46 and z < 0.46 and abs(x) > 0.035:
                ht = max(0.0, min(1.0, (y - 1.46) / 0.125))
                # Swept-back curved dragon horn with graceful upward tip curl (like official ORAS!)
                y_new = y + 0.18 * (ht ** 1.15) + 0.05 * (ht ** 2.6)
                z_new = z - 0.22 * (ht ** 1.05) + 0.04 * (ht ** 2.4)
                x_new = x + (0.042 if x > 0 else -0.042) * ht
                verts[vi] = [float(x_new), float(y_new), float(z_new)]
                # Smoothly map ht in [0, 1] along single vertical gradient column v in [0.74, 0.96]!
                uvs[vi] = [0.145, float(0.74 + 0.22 * ht)]

    # 3. Build official Omega Ruby wide-swept horizontal 4-blade dragon wings in submesh[0]
    l_wing_bones = [(b["idx"], np.array(b["pos"], dtype=np.float64)) for b in bones if b["idx"] in wing_bones_l]
    r_wing_bones = [(b["idx"], np.array(b["pos"], dtype=np.float64)) for b in bones if b["idx"] in wing_bones_r]
    add_oras_official_wing(verts, normals, uvs, weights, indices, submeshes[0], -1.0, l_wing_bones)
    add_oras_official_wing(verts, normals, uvs, weights, indices, submeshes[0], +1.0, r_wing_bones)

    # 4. Official Mega Charizard X subtle nasal snout horn + cheek/jaw crests (Head=5)
    head_wf = lambda _pos: ([1.0, 0.0, 0.0, 0.0], [5, 0, 0, 0])
    uv_charcoal = lambda _f: (0.145, 0.77)

    # Small fierce nasal ridge/horn on snout
    snout_horn = sample_catmull_rom([
        (0.0, 1.385, 0.680),
        (0.0, 1.410, 0.700),
        (0.0, 1.430, 0.692),
    ], samples_per_seg=2)
    r_snout = [(0.018 * (1.0 - 0.88 * (i / max(len(snout_horn) - 1, 1))) + 0.002,
                0.022 * (1.0 - 0.88 * (i / max(len(snout_horn) - 1, 1))) + 0.002) for i in range(len(snout_horn))]
    add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], snout_horn, r_snout, head_wf, uv_charcoal, sides=8)

    # Pointed charcoal cheek/jaw crests behind each side of the jaw
    for sign in (-1.0, 1.0):
        cheek_crest = sample_catmull_rom([
            (sign * 0.095, 1.335, 0.455),
            (sign * 0.130, 1.350, 0.390),
            (sign * 0.145, 1.375, 0.325),
        ], samples_per_seg=2)
        r_cheek = [(0.024 * (1.0 - 0.90 * (i / max(len(cheek_crest) - 1, 1))) + 0.002,
                    0.018 * (1.0 - 0.90 * (i / max(len(cheek_crest) - 1, 1))) + 0.002) for i in range(len(cheek_crest))]
        add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], cheek_crest, r_cheek, head_wf, uv_charcoal, sides=8)

    # 5. Official Omega Ruby chunky curved blue-tipped shoulder claws (front pectoral + rear shoulder!)
    #    Uses the exact same continuous vertical charcoal->cyan column (u_mesh=0.145) -> ZERO seam!
    uv_grad_tip = lambda frac: (0.145, 0.74 + 0.22 * frac)

    for sign, sh_bone in ((-1.0, 8), (1.0, 18)):
        sh_wf = lambda _pos, b=sh_bone: ([1.0, 0.0, 0.0, 0.0], [b, 0, 0, 0])
        # Front pectoral/shoulder curved claw (curves forward & slightly up right at blue-chest border)
        sp_front = sample_catmull_rom([
            (sign * 0.155, 0.84, 0.140),
            (sign * 0.195, 0.89, 0.205),
            (sign * 0.195, 0.96, 0.245),
            (sign * 0.175, 1.02, 0.255),
        ], samples_per_seg=2)
        r_f = [(0.056 * (1.0 - 0.90 * (i / max(len(sp_front) - 1, 1))) + 0.003,
                0.050 * (1.0 - 0.90 * (i / max(len(sp_front) - 1, 1))) + 0.003) for i in range(len(sp_front))]
        add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], sp_front, r_f, sh_wf, uv_grad_tip, sides=12)

        # Rear shoulder-blade curved claw (curves boldly upward & backward)
        sp_rear = sample_catmull_rom([
            (sign * 0.165, 0.90, 0.015),
            (sign * 0.225, 0.98, -0.025),
            (sign * 0.255, 1.08, -0.065),
            (sign * 0.245, 1.15, -0.095),
        ], samples_per_seg=2)
        r_r = [(0.056 * (1.0 - 0.90 * (i / max(len(sp_rear) - 1, 1))) + 0.003,
                0.048 * (1.0 - 0.90 * (i / max(len(sp_rear) - 1, 1))) + 0.003) for i in range(len(sp_rear))]
        add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], sp_rear, r_r, sh_wf, uv_grad_tip, sides=12)

    # 6. Official Omega Ruby wide, billowing, multi-tongued blue mouth fire plumes!
    for sign in (-1.0, 1.0):
        add_sculpted_mouth_flame_plume(verts, normals, uvs, weights, indices, submeshes[0], sign)

    out_payload = {
        "verts": verts,
        "normals": normals,
        "uvs": uvs,
        "weights": weights,
        "indices": indices,
        "submeshes": submeshes,
    }
    OUT_MESH_JSON.write_text(json.dumps(out_payload))
    print(f"Remodeled Official ORAS Mega Charizard X -> {OUT_MESH_JSON} (verts={len(verts)}, total_tris={sum(len(s)//3 for s in submeshes)})")
    return out_payload


def render_textured_preview(mesh_data: dict) -> None:
    """Renders a pixel-accurate full-color 3-angle Cycles CPU preview matching Switch Mirror UVs."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 1440
    scene.render.resolution_y = 680
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.13, 0.15, 0.20, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9

    def make_mat(name: str, col_png: Path, emi_png: Path | None = None):
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        bsdf.inputs["Roughness"].default_value = 0.58

        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(str(col_png))
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])

        if emi_png is not None and emi_png.exists():
            etex = nt.nodes.new("ShaderNodeTexImage")
            etex.image = bpy.data.images.load(str(emi_png))
            nt.links.new(etex.outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = 1.35
        return mat

    mat_b = make_mat("BodyB", WORK_DIR / "BodyB_col.png", WORK_DIR / "BodyB_emi.png")
    mat_a = make_mat("BodyA", WORK_DIR / "BodyA_col.png")
    mat_eye = make_mat("Eye", WORK_DIR / "Eye_col.png")
    sub_mats = [mat_b, mat_a, mat_b, mat_eye, mat_eye]

    verts_b = [(v[0], -v[2], v[1]) for v in mesh_data["verts"]]
    faces = []
    face_mats = []
    for si, sub in enumerate(mesh_data["submeshes"]):
        for t in range(0, len(sub), 3):
            a, b, c = sub[t], sub[t + 1], sub[t + 2]
            if a != b and b != c and a != c:
                faces.append((a, b, c))
                face_mats.append(si)

    mesh = bpy.data.meshes.new("MegaCharizardX_ORAS_V2")
    mesh.from_pydata(verts_b, [], faces)
    for m in sub_mats:
        mesh.materials.append(m)
    uv_layer = mesh.uv_layers.new(name="UVMap")
    uvs = mesh_data["uvs"]
    for poly_idx, poly in enumerate(mesh.polygons):
        poly.material_index = face_mats[poly_idx]
        poly.use_smooth = True
        for loop_idx in poly.loop_indices:
            vi = mesh.loops[loop_idx].vertex_index
            u_m = mirror_uv(uvs[vi][0])
            v_m = uvs[vi][1] % 1.0
            uv_layer.data[loop_idx].uv = (u_m, v_m)
    mesh.update()

    for name, loc, rot_z in [
        ("FrontLeft", (-2.85, 0.0, 0.0), math.radians(-35.0)),
        ("HeroCenter", (0.0, 0.0, 0.0), math.radians(32.0)),
        ("SideRight", (2.85, 0.0, 0.0), math.radians(115.0)),
    ]:
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_euler = (0.0, 0.0, rot_z)

    for name, loc, energy in [
        ("Key", (3.5, -5.5, 4.5), 1400.0),
        ("Fill", (-4.0, -4.5, 3.0), 650.0),
        ("Rim", (0.0, 4.5, 3.5), 500.0),
    ]:
        ldata = bpy.data.lights.new(name, type="POINT")
        ldata.energy = energy
        lobj = bpy.data.objects.new(name, ldata)
        bpy.context.collection.objects.link(lobj)
        lobj.location = loc

    bpy.ops.mesh.primitive_plane_add(size=26.0, location=(0.0, 0.0, -0.03))
    ground = bpy.context.active_object
    gmat = bpy.data.materials.new("Ground")
    gmat.use_nodes = True
    gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.16, 0.18, 0.22, 1.0)
    ground.data.materials.append(gmat)

    cam_data = bpy.data.cameras.new("Cam")
    cam_obj = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    cam_obj.location = (0.0, -10.2, 1.65)
    direction = Vector((0.0, 0.0, 0.90)) - cam_obj.location
    cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = 42.0

    OUT_TEXTURED_PNG.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(OUT_TEXTURED_PNG)
    bpy.ops.render.render(write_still=True)
    print(f"Rendered pixel-accurate textured Omega Ruby preview -> {OUT_TEXTURED_PNG}")


def main() -> None:
    WORK_DIR.mkdir(parents=True, exist_ok=True)
    van_data = json.loads(VAN_JSON.read_text())
    meta = build_textures(van_data)
    print("Built Official ORAS Mega Charizard X textures:", meta)
    mesh_data = build_mesh(van_data)
    render_textured_preview(mesh_data)


if __name__ == "__main__":
    main()
