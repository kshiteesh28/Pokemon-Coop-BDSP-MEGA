#!/usr/bin/env python3
"""Mega Absol (pm0359_01_00) & Mega Absol Z (pm0359_02_00) 3D remodeler & texture generator.

Built directly on Game Freak's official 48-bone BDSP Absol (pm0359_00_00):
- Preserves 100% of Game Freak's articulated head, crimson eyes (Eye), mouth (Mouth),
  torso, limbs, paws, and mane/hair/tail bones with exact Switch WrapU=Mirror & scale=(2,1).
- Mega Absol (pm0359_01_00, Omega Ruby reference):
  * Snow-white fur (Body00/BodyVco) + deep indigo-slate face/horns/paws/tail (Body01/Body02).
  * ONE continuous, solid, sculpted angelic feathered fur wing per side (weighted to LFeelerB/RFeelerB,
    zero air gaps!) + widened crescent main horn (+X), secondary curved horn (-X), elongated bangs,
    curved wavy saw-blade tail, and sharp heel/elbow spurs.
- Mega Absol Z (pm0359_02_00, pokestats.gg / Legends Z-A reference):
  * Midnight dark navy-black fur (Body00/BodyVco) + cool steel-blue face/Z-horn/claws/tail (Body01/Body02).
  * ONE massive, solid, connected 4-taloned crimson-magenta spectral claw-wing on right shoulder (+X,
    continuous vertical navy->crimson gradient UV column, ZERO gaps or seams!).
  * Three crimson-tipped mane tufts on left side (-X), thick navy bangs, bold angular Z-shaped
    steel-blue scythe horn (+X, zero leftover vanilla fragments), sharp left ear-horn (-X),
    solid broad curved 4-toothed serrated blade tail, and sharp heel/elbow dewclaws.

Run via Blender's bundled Python:
  /tmp/blender_hfs/Blender/Blender.app/Contents/MacOS/Blender -b --python scripts/build_mega_absol.py
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

REPO_ROOT = Path(__file__).resolve().parent.parent
WORK_DIR = Path("/tmp/mega_absol_build")
VAN_JSON = WORK_DIR / "van_pm0359.json"
RENDER_DIR = REPO_ROOT / "build/test-renders"
ICON_DIR = REPO_ROOT / "assets/mega"


def save_rgba_and_png(arr: np.ndarray, out_dir: Path, stem: str) -> tuple[int, int]:
    out_dir.mkdir(parents=True, exist_ok=True)
    h, w, _ = arr.shape
    clamped = np.clip(arr, 0.0, 1.0)
    u8 = (clamped * 255.0 + 0.5).astype(np.uint8)
    (out_dir / f"{stem}.raw").write_bytes(u8.tobytes())

    img = bpy.data.images.new(stem, width=w, height=h, alpha=True)
    img.pixels.foreach_set(clamped.ravel())
    img.filepath_raw = str(out_dir / f"{stem}.png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    return w, h


def mirror_uv(u: float) -> float:
    u2 = u * 2.0
    period = math.floor(u2)
    frac = u2 - period
    return (1.0 - frac) if (period % 2 != 0) else frac


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


def add_lofted_tube(
    verts, normals, uvs, weights, indices, tris,
    spine_pts: list[np.ndarray],
    radii: list[tuple[float, float]],
    weight_func,
    uv_func,
    sides: int = 10,
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


def add_sculpted_sheet(
    verts, normals, uvs, weights, indices, tris,
    root_pts_raw: list[tuple[float, float, float]],
    tip_pts_raw: list[tuple[float, float, float]],
    thickness_func,
    weight_func,
    uv_func,
    normal_hint: np.ndarray,
    sub_per_seg: int = 3,
    nv: int = 7,
    camber_amp: float = 0.025,
):
    """Builds ONE continuous, watertight, solid 3D sculpted wing, claw-hand, or serrated blade
    between root_pts_raw (attached cleanly to body) and tip_pts_raw (jagged feather/talon/tooth edge).
    ZERO air gaps between roots!
    """
    # Subdivide tip polyline linearly so feather/talon tips & V-notches stay razor sharp
    tip_pts = []
    for seg in range(len(tip_pts_raw) - 1):
        p0 = np.array(tip_pts_raw[seg], dtype=np.float64)
        p1 = np.array(tip_pts_raw[seg + 1], dtype=np.float64)
        for s in range(sub_per_seg):
            tip_pts.append(p0 + (p1 - p0) * (s / sub_per_seg))
    tip_pts.append(np.array(tip_pts_raw[-1], dtype=np.float64))

    nu = len(tip_pts)
    root_smooth = sample_catmull_rom(root_pts_raw, samples_per_seg=4)
    root_sampled = []
    for iu in range(nu):
        f = iu / max(nu - 1, 1)
        idx_f = f * (len(root_smooth) - 1)
        i0 = int(math.floor(idx_f))
        i1 = min(i0 + 1, len(root_smooth) - 1)
        frac = idx_f - i0
        root_sampled.append(root_smooth[i0] * (1.0 - frac) + root_smooth[i1] * frac)

    n_dir = normal_hint / max(np.linalg.norm(normal_hint), 1e-6)
    front_grid, back_grid = [], []
    for iv in range(nv):
        fv = iv / (nv - 1)  # 0 = body root, 1 = outer feathered/taloned edge
        row_f, row_b = [], []
        for iu in range(nu):
            fu = iu / max(nu - 1, 1)
            rp = root_sampled[iu]
            tp = tip_pts[iu]
            camber = camber_amp * math.sin(math.pi * fv) * math.sin(math.pi * fu)
            center = rp * (1.0 - fv) + tp * fv + n_dir * camber
            th = thickness_func(fu, fv)
            pf = center + n_dir * th
            pb = center - n_dir * th
            u, v = uv_func(fu, fv)

            w_f, i_f = weight_func(pf)
            row_f.append(len(verts))
            verts.append([float(pf[0]), float(pf[1]), float(pf[2])])
            normals.append([float(n_dir[0]), float(n_dir[1]), float(n_dir[2])])
            uvs.append([float(u), float(v)])
            weights.append(w_f)
            indices.append(i_f)

            w_b, i_b = weight_func(pb)
            row_b.append(len(verts))
            verts.append([float(pb[0]), float(pb[1]), float(pb[2])])
            normals.append([float(-n_dir[0]), float(-n_dir[1]), float(-n_dir[2])])
            uvs.append([float(u), float(v)])
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

    # Stitch all 4 perimeter edges watertight
    for ef, eb, flip in ((front_grid[0], back_grid[0], False), (front_grid[-1], back_grid[-1], True)):
        for iu in range(nu - 1):
            if not flip:
                tris.extend([ef[iu], eb[iu], eb[iu + 1], ef[iu], eb[iu + 1], ef[iu + 1]])
            else:
                tris.extend([ef[iu], eb[iu + 1], eb[iu], ef[iu], ef[iu + 1], eb[iu + 1]])
    for iu, flip in ((0, True), (nu - 1, False)):
        for iv in range(nv - 1):
            f0, f1 = front_grid[iv][iu], front_grid[iv + 1][iu]
            b0, b1 = back_grid[iv][iu], back_grid[iv + 1][iu]
            if not flip:
                tris.extend([f0, b0, b1, f0, b1, f1])
            else:
                tris.extend([f0, b1, b0, f0, f1, b1])


def make_solid_shaded_tex(rgb_base: tuple[float, float, float], rgb_hi: tuple[float, float, float], w: int = 256, h: int = 256) -> np.ndarray:
    arr = np.zeros((h, w, 4), dtype=np.float32)
    t = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None]
    for c in range(3):
        arr[..., c] = rgb_base[c] * (1.0 - 0.14 * t) + rgb_hi[c] * (0.14 * t)
    arr[..., 3] = 1.0
    return arr


def build_mega_absol(van_data: dict) -> tuple[dict, Path]:
    """Builds Omega Ruby-faithful Mega Absol (pm0359_01_00)."""
    out_dir = WORK_DIR / "mega_absol"
    out_dir.mkdir(parents=True, exist_ok=True)

    meta = {}
    t_white = make_solid_shaded_tex((0.89, 0.91, 0.96), (0.99, 0.99, 1.00))
    t_slate = make_solid_shaded_tex((0.23, 0.28, 0.42), (0.34, 0.40, 0.56))
    for stem, arr in [
        ("Body00_col", t_white),
        ("BodyVco_col", t_white),
        ("Body01_col", t_slate),
        ("Body02_col", t_slate),
    ]:
        w, h = save_rgba_and_png(arr, out_dir, stem)
        meta[stem] = [w, h]
    (out_dir / "textures.json").write_text(json.dumps(meta, indent=2))

    verts = [list(v) for v in van_data["verts"]]
    normals = [list(n) for n in van_data["normals"]]
    uvs = [list(u) for u in van_data["uvs"]]
    weights = [list(w) for w in van_data["weights"]]
    indices = [list(i) for i in van_data["indices"]]
    submeshes = [list(s) for s in van_data["submeshes"]]

    # 1. Enhance vanilla main scythe horn (+X in sub[0] Body01) slightly wider/longer & smoother
    for vi in set(submeshes[0]):
        if indices[vi][0] == 4:
            x, y, z = verts[vi]
            if x > 0.08 and y > 0.78:
                ht = max(0.0, min(1.0, (y - 0.78) / 0.40))
                verts[vi] = [x + 0.035 * ht, y + 0.07 * ht, z - 0.03 * ht]

    # 2. Elongate flowing left-side bangs (Hair1..3 in sub[2])
    for vi in set(submeshes[2]):
        if indices[vi][0] in (5, 6, 7):
            x, y, z = verts[vi]
            ht = max(0.0, min(1.0, (0.82 - y) / 0.25))
            verts[vi] = [x - 0.025 * ht, y - 0.09 * ht, z + 0.035 * ht]

    # 3. Secondary curved indigo horn on left side (-X, Head=4, sub[0])
    head_wf = lambda _p: ([1.0, 0.0, 0.0, 0.0], [4, 0, 0, 0])
    uv_mid = lambda _f: (0.25, 0.50)
    horn2 = sample_catmull_rom([
        (-0.085, 0.80, 0.26),
        (-0.145, 0.86, 0.23),
        (-0.185, 0.94, 0.18),
        (-0.175, 1.03, 0.15),
    ], samples_per_seg=3)
    r_h2 = [(0.026 * (1.0 - 0.88 * (i / max(len(horn2) - 1, 1))) + 0.003,
             0.015 * (1.0 - 0.88 * (i / max(len(horn2) - 1, 1))) + 0.003) for i in range(len(horn2))]
    add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], horn2, r_h2, head_wf, uv_mid, sides=10)

    # 4. ONE CONTINUOUS, LUSH, SOLID SCULPTED ANGELIC FEATHERED WHITE WING PER SIDE (sub[2] BodyVco)
    #    ZERO air gaps between roots! Broad fluffy base emerging from shoulder/back -> 5 crisp feather tips!
    for sign, b_outer, b_inner in ((-1.0, 13, 12), (1.0, 15, 14)):
        wing_wf = lambda _p, bo=b_outer, bi=b_inner: ([0.65, 0.35, 0.0, 0.0], [bo, bi, 0, 0])
        root_pts = [
            (sign * 0.08, 0.67, 0.08),   # Upper shoulder-mane attachment
            (sign * 0.11, 0.61, 0.01),
            (sign * 0.10, 0.55, -0.07),  # Lower back attachment
        ]
        # 5 lush overlapping angelic feather tips with clean shallow-to-medium notches (solid wing body!)
        tip_pts = [
            (sign * 0.12, 0.72, 0.04),   # Front wing leading edge
            (sign * 0.44, 1.12, -0.36),  # Primary top feather tip (longest)
            (sign * 0.30, 0.88, -0.22),  # Notch 1
            (sign * 0.48, 0.94, -0.40),  # Feather 2 tip
            (sign * 0.31, 0.76, -0.24),  # Notch 2
            (sign * 0.44, 0.78, -0.38),  # Feather 3 tip
            (sign * 0.28, 0.67, -0.22),  # Notch 3
            (sign * 0.37, 0.65, -0.32),  # Feather 4 tip
            (sign * 0.22, 0.58, -0.18),  # Notch 4
            (sign * 0.26, 0.54, -0.24),  # Innermost small covert feather
            (sign * 0.10, 0.53, -0.09),  # Clean lower back closure
        ]
        add_sculpted_sheet(
            verts, normals, uvs, weights, indices, submeshes[2],
            root_pts, tip_pts,
            lambda fu, fv: 0.036 * (1.0 - 0.82 * (fv ** 1.2)) * (0.45 + 0.55 * math.sin(math.pi * fu)) + 0.004,
            wing_wf, lambda _fu, fv: (0.25, 0.20 + 0.70 * fv),
            np.array([sign * 0.82, 0.25, 0.52]),
            sub_per_seg=3, nv=7, camber_amp=0.032,
        )

    # 5. Curved wavy saw-blade tail (Tail=33, sub[0] Body01)
    submeshes[0] = [
        vi for t in range(0, len(submeshes[0]), 3)
        for vi in (submeshes[0][t], submeshes[0][t + 1], submeshes[0][t + 2])
        if not any(indices[v][0] == 33 for v in (submeshes[0][t], submeshes[0][t + 1], submeshes[0][t + 2]))
    ]
    tail_wf = lambda _p: ([1.0, 0.0, 0.0, 0.0], [33, 0, 0, 0])
    tail_root = [(0.0, 0.60, -0.34), (0.0, 0.65, -0.58), (0.0, 0.56, -0.84), (0.0, 0.42, -1.00)]
    tail_edge = [
        (0.0, 0.54, -0.36),
        (0.0, 0.55, -0.50),
        (0.0, 0.59, -0.58),
        (0.0, 0.51, -0.68),
        (0.0, 0.55, -0.76),
        (0.0, 0.46, -0.86),
        (0.0, 0.42, -1.00),
    ]
    add_sculpted_sheet(
        verts, normals, uvs, weights, indices, submeshes[0],
        tail_root, tail_edge,
        lambda fu, fv: 0.022 * (1.0 - 0.75 * fu) * (1.0 - 0.85 * fv) + 0.002,
        tail_wf, lambda _fu, _fv: (0.25, 0.50),
        np.array([1.0, 0.0, 0.0]),
        sub_per_seg=3, nv=5, camber_amp=0.0,
    )

    # 6. Sharp indigo heel & elbow spurs
    for sign, b_arm, b_foot in ((-1.0, 20, 36), (1.0, 27, 43)):
        awf = lambda _p, b=b_arm: ([1.0, 0.0, 0.0, 0.0], [b, 0, 0, 0])
        fwf = lambda _p, b=b_foot: ([1.0, 0.0, 0.0, 0.0], [b, 0, 0, 0])
        el = sample_catmull_rom([(sign * 0.105, 0.28, 0.05), (sign * 0.115, 0.31, -0.02), (sign * 0.120, 0.35, -0.09)], samples_per_seg=2)
        hl = sample_catmull_rom([(sign * 0.115, 0.20, -0.44), (sign * 0.125, 0.25, -0.52), (sign * 0.125, 0.32, -0.57)], samples_per_seg=2)
        for pts, wf in ((el, awf), (hl, fwf)):
            rads = [(0.017 * (1.0 - 0.88 * (i / max(len(pts) - 1, 1))) + 0.002,
                     0.012 * (1.0 - 0.88 * (i / max(len(pts) - 1, 1))) + 0.002) for i in range(len(pts))]
            add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], pts, rads, wf, uv_mid, sides=8)

    mesh_payload = {"verts": verts, "normals": normals, "uvs": uvs, "weights": weights, "indices": indices, "submeshes": submeshes}
    (out_dir / "mesh.json").write_text(json.dumps(mesh_payload))
    print(f"Built Mega Absol -> {out_dir}/mesh.json (verts={len(verts)}, tris={sum(len(s)//3 for s in submeshes)})")
    return mesh_payload, out_dir


def build_mega_absol_z(van_data: dict) -> tuple[dict, Path]:
    """Builds Legends Z-A / pokestats.gg-faithful Mega Absol Z (pm0359_02_00)."""
    out_dir = WORK_DIR / "mega_absol_z"
    out_dir.mkdir(parents=True, exist_ok=True)

    meta = {}
    t_navy = make_solid_shaded_tex((0.055, 0.100, 0.175), (0.095, 0.165, 0.270))
    t_steel = make_solid_shaded_tex((0.20, 0.36, 0.56), (0.32, 0.52, 0.74))

    # Continuous vertical midnight-navy (v in [0.0, 0.44]) -> jagged-sharp crimson-magenta (v in [0.54, 1.0])
    h, w = 256, 256
    t_vco = np.zeros((h, w, 4), dtype=np.float32)
    vt = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None]
    b = np.clip((vt - 0.44) / 0.10, 0.0, 1.0)
    tip = np.clip((vt - 0.54) / 0.46, 0.0, 1.0)
    t_vco[..., 0] = 0.058 * (1.0 - b) + (0.88 + 0.10 * tip) * b
    t_vco[..., 1] = 0.102 * (1.0 - b) + (0.02 + 0.08 * tip) * b
    t_vco[..., 2] = 0.178 * (1.0 - b) + (0.30 + 0.12 * tip) * b
    t_vco[..., 3] = 1.0

    for stem, arr in [
        ("Body00_col", t_navy),
        ("BodyVco_col", t_vco),
        ("Body01_col", t_steel),
        ("Body02_col", t_steel),
    ]:
        tw, th = save_rgba_and_png(arr, out_dir, stem)
        meta[stem] = [tw, th]
    (out_dir / "textures.json").write_text(json.dumps(meta, indent=2))

    verts = [list(v) for v in van_data["verts"]]
    normals = [list(n) for n in van_data["normals"]]
    uvs = [list(u) for u in van_data["uvs"]]
    weights = [list(w) for w in van_data["weights"]]
    indices = [list(i) for i in van_data["indices"]]
    submeshes = [list(s) for s in van_data["submeshes"]]

    # Remap all vanilla BodyVco vertices to v=0.20 (100% midnight navy-black fur!)
    for vi in set(submeshes[2]):
        uvs[vi] = [0.25, 0.20]

    # 1. Elongate thick midnight-navy bangs covering the left eye (Hair1..3 in sub[2])
    for vi in set(submeshes[2]):
        if indices[vi][0] in (5, 6, 7):
            x, y, z = verts[vi]
            ht = max(0.0, min(1.0, (0.85 - y) / 0.28))
            verts[vi] = [x - 0.030 * ht, y - 0.11 * ht, z + 0.045 * ht]

    # 2. Cleanly remove 100% of vanilla curved horn (y > 0.80 and z < 0.35 -> ZERO floating tip!) and vanilla tail
    clean_sub0 = []
    for t in range(0, len(submeshes[0]), 3):
        a, b_i, c = submeshes[0][t], submeshes[0][t + 1], submeshes[0][t + 2]
        is_tail = any(indices[v][0] == 33 for v in (a, b_i, c))
        is_horn = any(indices[v][0] == 4 and verts[v][1] > 0.805 and verts[v][2] < 0.345 for v in (a, b_i, c))
        if not is_tail and not is_horn:
            clean_sub0.extend([a, b_i, c])
    submeshes[0] = clean_sub0

    head_wf = lambda _p: ([1.0, 0.0, 0.0, 0.0], [4, 0, 0, 0])
    uv_steel = lambda _f: (0.25, 0.50)

    # Bold, chunky, angular Z-shaped steel-blue scythe horn on right side (+X, matching Legends Z-A!)
    z_root = [
        (0.075, 0.76, 0.26),
        (0.165, 0.86, 0.23),
        (0.115, 0.98, 0.17),
        (0.245, 1.18, 0.08),
    ]
    z_edge = [
        (0.115, 0.73, 0.32),
        (0.255, 0.83, 0.29),
        (0.205, 0.98, 0.23),
        (0.300, 1.14, 0.14),
        (0.255, 1.24, 0.06),
    ]
    add_sculpted_sheet(
        verts, normals, uvs, weights, indices, submeshes[0],
        z_root, z_edge,
        lambda fu, fv: 0.024 * (1.0 - 0.65 * fu) * (1.0 - 0.70 * fv) + 0.004,
        head_wf, lambda _fu, _fv: (0.25, 0.50),
        np.array([0.25, 0.15, 0.95]),
        sub_per_seg=3, nv=5, camber_amp=0.0,
    )

    # Sharp angular secondary ear-horn on left side (-X)
    l_horn = sample_catmull_rom([(-0.085, 0.80, 0.24), (-0.145, 0.89, 0.20), (-0.195, 1.00, 0.15)], samples_per_seg=2)
    r_lh = [(0.024 * (1.0 - 0.90 * (i / max(len(l_horn) - 1, 1))) + 0.003,
             0.015 * (1.0 - 0.90 * (i / max(len(l_horn) - 1, 1))) + 0.003) for i in range(len(l_horn))]
    add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], l_horn, r_lh, head_wf, uv_steel, sides=8)

    # 3. ONE MASSIVE, SOLID, CONNECTED 4-TALONED CRIMSON-MAGENTA SPECTRAL CLAW-WING (+X right shoulder!)
    #    Thick dark-navy shoulder palm transitioning smoothly into vibrant neon crimson-magenta
    #    fanning into 4 bold curved jagged claw-blades (ZERO air gaps between roots!).
    r_wing_wf = lambda _p: ([0.65, 0.35, 0.0, 0.0], [15, 14, 0, 0])
    wing_root = [
        (0.08, 0.68, 0.08),
        (0.11, 0.60, 0.01),
        (0.09, 0.53, -0.07),
    ]
    wing_tips = [
        (0.14, 0.75, 0.04),   # Bold upper shoulder-claw ridge
        (0.68, 1.24, -0.36),  # Talon 1 (longest sweeping crimson blade)
        (0.36, 0.90, -0.18),  # Deep notch 1 (still solid crimson palm inside!)
        (0.74, 0.96, -0.38),  # Talon 2 (wide curving upper-mid blade)
        (0.37, 0.74, -0.20),  # Deep notch 2
        (0.64, 0.72, -0.34),  # Talon 3 (curved lower-mid blade)
        (0.31, 0.61, -0.18),  # Deep notch 3
        (0.46, 0.52, -0.28),  # Talon 4 (lower trailing crimson claw)
        (0.12, 0.52, -0.09),  # Clean lower back closure
    ]
    add_sculpted_sheet(
        verts, normals, uvs, weights, indices, submeshes[2],
        wing_root, wing_tips,
        lambda fu, fv: 0.044 * (1.0 - 0.80 * (fv ** 1.15)) * (0.45 + 0.55 * math.sin(math.pi * fu)) + 0.004,
        r_wing_wf, lambda _fu, fv: (0.25, 0.22 + 0.74 * fv),  # midnight navy base -> neon crimson-magenta!
        np.array([0.78, 0.22, 0.58]),
        sub_per_seg=3, nv=7, camber_amp=0.038,
    )

    # 4. Three crimson-magenta tipped mane tufts on left side (-X, LFeelerB2=13 / LFeelerB1=12)
    l_tuft_wf = lambda _p: ([0.65, 0.35, 0.0, 0.0], [13, 12, 0, 0])
    uv_tuft = lambda frac: (0.25, 0.25 + 0.70 * frac)
    for ctrl in [
        [(-0.09, 0.67, 0.14), (-0.17, 0.72, 0.12), (-0.23, 0.77, 0.08)],
        [(-0.10, 0.62, 0.12), (-0.18, 0.65, 0.09), (-0.24, 0.68, 0.05)],
        [(-0.10, 0.57, 0.10), (-0.16, 0.58, 0.07), (-0.21, 0.59, 0.03)],
    ]:
        sp = sample_catmull_rom(ctrl, samples_per_seg=2)
        rads = [(0.034 * (1.0 - 0.88 * (i / max(len(sp) - 1, 1))) + 0.003,
                 0.024 * (1.0 - 0.88 * (i / max(len(sp) - 1, 1))) + 0.002) for i in range(len(sp))]
        add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[2], sp, rads, l_tuft_wf, uv_tuft, sides=8)

    # 5. Solid broad curved 4-toothed serrated steel-blue blade tail (Tail=33, sub[0] Body01)
    tail_wf = lambda _p: ([1.0, 0.0, 0.0, 0.0], [33, 0, 0, 0])
    t_root = [(0.0, 0.60, -0.34), (0.0, 0.66, -0.54), (0.0, 0.62, -0.74), (0.0, 0.48, -0.92)]
    t_edge = [
        (0.0, 0.54, -0.36),
        (0.0, 0.46, -0.45),  # Tooth 1
        (0.0, 0.57, -0.50),  # Notch 1
        (0.0, 0.47, -0.59),  # Tooth 2
        (0.0, 0.57, -0.64),  # Notch 2
        (0.0, 0.46, -0.73),  # Tooth 3
        (0.0, 0.54, -0.78),  # Notch 3
        (0.0, 0.43, -0.86),  # Tooth 4
        (0.0, 0.48, -0.92),  # Tail tip
    ]
    add_sculpted_sheet(
        verts, normals, uvs, weights, indices, submeshes[0],
        t_root, t_edge,
        lambda fu, fv: 0.022 * (1.0 - 0.72 * fu) * (1.0 - 0.85 * fv) + 0.003,
        tail_wf, lambda _fu, _fv: (0.25, 0.50),
        np.array([1.0, 0.0, 0.0]),
        sub_per_seg=3, nv=5, camber_amp=0.0,
    )

    # 6. Sharp curved steel-blue heel & elbow dewclaws
    for sign, b_arm, b_foot in ((-1.0, 20, 36), (1.0, 27, 43)):
        awf = lambda _p, b=b_arm: ([1.0, 0.0, 0.0, 0.0], [b, 0, 0, 0])
        fwf = lambda _p, b=b_foot: ([1.0, 0.0, 0.0, 0.0], [b, 0, 0, 0])
        el = sample_catmull_rom([(sign * 0.105, 0.27, 0.05), (sign * 0.115, 0.31, -0.03), (sign * 0.120, 0.37, -0.10)], samples_per_seg=2)
        hl = sample_catmull_rom([(sign * 0.115, 0.19, -0.44), (sign * 0.125, 0.26, -0.53), (sign * 0.125, 0.34, -0.58)], samples_per_seg=2)
        for pts, wf in ((el, awf), (hl, fwf)):
            rads = [(0.019 * (1.0 - 0.88 * (i / max(len(pts) - 1, 1))) + 0.002,
                     0.013 * (1.0 - 0.88 * (i / max(len(pts) - 1, 1))) + 0.002) for i in range(len(pts))]
            add_lofted_tube(verts, normals, uvs, weights, indices, submeshes[0], pts, rads, wf, uv_steel, sides=8)

    mesh_payload = {"verts": verts, "normals": normals, "uvs": uvs, "weights": weights, "indices": indices, "submeshes": submeshes}
    (out_dir / "mesh.json").write_text(json.dumps(mesh_payload))
    print(f"Built Mega Absol Z -> {out_dir}/mesh.json (verts={len(verts)}, tris={sum(len(s)//3 for s in submeshes)})")
    return mesh_payload, out_dir


def render_preview(mesh_data: dict, tex_dir: Path, out_png: Path, title: str) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 1440
    scene.render.resolution_y = 640
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.14, 0.16, 0.21, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9

    def make_tex_mat(name: str, png_path: Path):
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Roughness"].default_value = 0.55
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(str(png_path))
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        return mat

    def make_solid_mat(name: str, rgba: tuple[float, float, float, float]):
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = rgba
        return mat

    sub_mats = [
        make_tex_mat("Body01", tex_dir / "Body01_col.png"),
        make_solid_mat("Eye", (0.90, 0.12, 0.18, 1.0)),
        make_tex_mat("BodyVco", tex_dir / "BodyVco_col.png"),
        make_solid_mat("Mouth", (0.85, 0.35, 0.42, 1.0)),
        make_tex_mat("Body00", tex_dir / "Body00_col.png"),
        make_tex_mat("Body02", tex_dir / "Body02_col.png"),
    ]

    verts_b = [(v[0], -v[2], v[1]) for v in mesh_data["verts"]]
    faces, face_mats = [], []
    for si, sub in enumerate(mesh_data["submeshes"]):
        for t in range(0, len(sub), 3):
            a, b, c = sub[t], sub[t + 1], sub[t + 2]
            if a != b and b != c and a != c:
                faces.append((a, b, c))
                face_mats.append(si)

    mesh = bpy.data.meshes.new(title)
    mesh.from_pydata(verts_b, [], faces)
    for m in sub_mats:
        mesh.materials.append(m)
    uv_layer = mesh.uv_layers.new(name="UVMap")
    uvs = mesh_data["uvs"]
    for pi, poly in enumerate(mesh.polygons):
        poly.material_index = face_mats[pi]
        poly.use_smooth = True
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            uv_layer.data[li].uv = (mirror_uv(uvs[vi][0]), uvs[vi][1] % 1.0)
    mesh.update()

    for name, loc, rot_z in [
        ("FrontLeft", (-1.55, 0.0, 0.0), math.radians(-38.0)),
        ("HeroCenter", (0.0, 0.0, 0.0), math.radians(38.0)),
        ("SideRight", (1.55, 0.0, 0.0), math.radians(118.0)),
    ]:
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_euler = (0.0, 0.0, rot_z)

    for name, loc, energy in [
        ("Key", (2.8, -4.5, 3.8), 1100.0),
        ("Fill", (-3.2, -3.5, 2.5), 550.0),
        ("Rim", (0.0, 3.8, 3.0), 450.0),
    ]:
        ld = bpy.data.lights.new(name, type="POINT")
        ld.energy = energy
        lo = bpy.data.objects.new(name, ld)
        bpy.context.collection.objects.link(lo)
        lo.location = loc

    bpy.ops.mesh.primitive_plane_add(size=18.0, location=(0.0, 0.0, -0.01))
    ground = bpy.context.active_object
    gmat = bpy.data.materials.new("Ground")
    gmat.use_nodes = True
    gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.17, 0.19, 0.24, 1.0)
    ground.data.materials.append(gmat)

    cam_d = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_d)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (0.0, -5.6, 1.15)
    cam.rotation_euler = (Vector((0.0, 0.0, 0.62)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_d.lens = 45.0

    out_png.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(out_png)
    bpy.ops.render.render(write_still=True)
    print(f"Rendered {title} QA preview -> {out_png}")


def build_mega_stone_icon(out_png: Path, c_outer: tuple[float, float, float], c_inner: tuple[float, float, float], c_helix: tuple[float, float, float]) -> None:
    size = 128
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx = (xx - (size - 1) * 0.5) / (size * 0.44)
    ny = (yy - (size - 1) * 0.5) / (size * 0.44)
    r = np.sqrt(nx * nx + ny * ny)
    inside = r <= 1.0
    nz = np.sqrt(np.clip(1.0 - r * r, 0.0, 1.0))

    light = np.clip(nx * -0.45 + ny * 0.55 + nz * 0.72, 0.15, 1.0)
    spec = np.clip(nx * -0.50 + ny * 0.60 + nz * 0.62, 0.0, 1.0) ** 14
    helix_dist = np.abs(nx - 0.24 * np.sin(ny * 3.2))
    helix = (helix_dist < 0.15) & (r < 0.76)

    arr = np.zeros((size, size, 4), dtype=np.float32)
    rim = np.clip((r - 0.65) / 0.35, 0.0, 1.0)
    for c in range(3):
        base = c_inner[c] * (1.0 - rim) + c_outer[c] * rim
        col = np.where(helix, c_helix[c], base)
        arr[..., c] = np.where(inside, np.clip(col * light + 0.65 * spec, 0.0, 1.0), 0.0)
    arr[..., 3] = np.where(inside, 1.0, 0.0)

    out_png.parent.mkdir(parents=True, exist_ok=True)
    img = bpy.data.images.new(out_png.stem, width=size, height=size, alpha=True)
    img.pixels.foreach_set(arr.ravel())
    img.filepath_raw = str(out_png)
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"Generated Mega Stone icon -> {out_png}")


def main() -> None:
    WORK_DIR.mkdir(parents=True, exist_ok=True)
    van_data = json.loads(VAN_JSON.read_text())

    mesh_a, dir_a = build_mega_absol(van_data)
    render_preview(mesh_a, dir_a, RENDER_DIR / "pm0359_01_00_textured.png", "MegaAbsol_ORAS")

    mesh_z, dir_z = build_mega_absol_z(van_data)
    render_preview(mesh_z, dir_z, RENDER_DIR / "pm0359_02_00_textured.png", "MegaAbsolZ_ZA")

    build_mega_stone_icon(ICON_DIR / "absolite.png", (0.28, 0.34, 0.52), (0.86, 0.90, 0.96), (0.85, 0.16, 0.24))
    build_mega_stone_icon(ICON_DIR / "absolite_z.png", (0.08, 0.14, 0.24), (0.26, 0.44, 0.68), (0.94, 0.06, 0.36))


if __name__ == "__main__":
    main()
