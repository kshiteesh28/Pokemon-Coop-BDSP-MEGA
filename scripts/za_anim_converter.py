#!/usr/bin/env python3
"""Legends: Z-A TRSKL + TRANM -> Unity m_DenseClip Converter.

Pure-Python (numpy-only) decoder for Pokemon Legends: Z-A Trinity skeleton (TRSKL)
and skeletal animation (TRANM) FlatBuffers. Produces exact RH->LH Unity Transform
hierarchies and 30fps dense animation sample buffers for MegaBundleTool.
"""

from __future__ import annotations

import json
import math
import os
import re
import struct
from pathlib import Path
from typing import Any

import numpy as np


# ---------------------------------------------------------------------------
# FlatBuffer & Hash Helpers
# ---------------------------------------------------------------------------

def fb_u32(d: bytes, o: int) -> int:
    return struct.unpack_from("<I", d, o)[0]


def fb_i32(d: bytes, o: int) -> int:
    return struct.unpack_from("<i", d, o)[0]


def fb_u16(d: bytes, o: int) -> int:
    return struct.unpack_from("<H", d, o)[0]


def fb_u8(d: bytes, o: int) -> int:
    return d[o]


def fb_root(d: bytes) -> int:
    return fb_u32(d, 0)


def fb_field(d: bytes, t: int, idx: int) -> int:
    vt = t - fb_i32(d, t)
    vlen = fb_u16(d, vt)
    off = 4 + 2 * idx
    if off >= vlen:
        return 0
    fo = fb_u16(d, vt + off)
    return (t + fo) if fo else 0


def fb_str(d: bytes, p: int) -> str:
    s = p + fb_u32(d, p)
    ln = fb_u32(d, s)
    return d[s + 4 : s + 4 + ln].decode("utf-8", errors="replace")


def fb_vec(d: bytes, p: int) -> tuple[int, int]:
    v = p + fb_u32(d, p)
    cnt = fb_u32(d, v)
    return v + 4, cnt


def fnv1a_64(s: str) -> int:
    h = 0xCBF29CE484222645
    for b in s.encode("utf-8"):
        h ^= b
        h = (h * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return h


# ---------------------------------------------------------------------------
# Math: Euler XYZ, 48-bit Trinity Quaternion Unpacking, RH -> LH Conversion
# ---------------------------------------------------------------------------

# Trinity 48-bit quaternion quantization range is [-pi/4, +pi/4] (NOT 1/sqrt(2)!)
# Using 1/sqrt(2) caused 15-28 deg single-frame snaps whenever `missing` switched indices.
PI_QUARTER = math.pi / 4.0


def euler_xyz_to_quat(rx: float, ry: float, rz: float) -> tuple[float, float, float, float]:
    """Converts Trinity local Euler XYZ (radians) to (x, y, z, w) quaternion."""
    cx, sx = math.cos(rx * 0.5), math.sin(rx * 0.5)
    cy, sy = math.cos(ry * 0.5), math.sin(ry * 0.5)
    cz, sz = math.cos(rz * 0.5), math.sin(rz * 0.5)
    qx = sx * cy * cz - cx * sy * sz
    qy = cx * sy * cz + sx * cy * sz
    qz = cx * cy * sz - sx * sy * cz
    qw = cx * cy * cz + sx * sy * sz
    n = math.sqrt(qx * qx + qy * qy + qz * qz + qw * qw)
    if n < 1e-8:
        return (0.0, 0.0, 0.0, 1.0)
    return (qx / n, qy / n, qz / n, qw / n)


def unpack_quat48(u0: int, u1: int, u2: int) -> tuple[float, float, float, float]:
    """Unpacks Trinity 48-bit quantized quaternion ([-pi/4, +pi/4]) -> (x, y, z, w) in RH space."""
    pack = (u2 << 32) | (u1 << 16) | u0
    missing = pack & 3  # 0=x, 1=y, 2=z, 3=w
    is_neg = (pack >> 2) & 1

    q1 = (((pack >> 3) & 0x7FFF) / 32767.0) * (2.0 * PI_QUARTER) - PI_QUARTER
    q2 = (((pack >> 18) & 0x7FFF) / 32767.0) * (2.0 * PI_QUARTER) - PI_QUARTER
    q3 = (((pack >> 33) & 0x7FFF) / 32767.0) * (2.0 * PI_QUARTER) - PI_QUARTER
    rem = math.sqrt(max(0.0, 1.0 - (q1 * q1 + q2 * q2 + q3 * q3)))

    if missing == 0:
        qx, qy, qz, qw = rem, q1, q2, q3
    elif missing == 1:
        qx, qy, qz, qw = q1, rem, q2, q3
    elif missing == 2:
        qx, qy, qz, qw = q1, q2, rem, q3
    else:
        qx, qy, qz, qw = q1, q2, q3, rem

    if is_neg:
        qx, qy, qz, qw = -qx, -qy, -qz, -qw

    n = math.sqrt(qx * qx + qy * qy + qz * qz + qw * qw)
    if n < 1e-8 or math.isnan(n):
        return (0.0, 0.0, 0.0, 1.0)
    return (qx / n, qy / n, qz / n, qw / n)


def rh_pos_to_unity(p: tuple[float, float, float]) -> tuple[float, float, float]:
    """Reflects X axis: Trinity RH Y-up -> Unity LH Y-up."""
    return (-float(p[0]), float(p[1]), float(p[2]))


def rh_quat_to_unity(q: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    """Conjugates rotation by diag(-1, 1, 1): (qx, qy, qz, qw) -> (qx, -qy, -qz, qw)."""
    qx, qy, qz, qw = float(q[0]), -float(q[1]), -float(q[2]), float(q[3])
    n = math.sqrt(qx * qx + qy * qy + qz * qz + qw * qw)
    if n < 1e-8 or math.isnan(n):
        return (0.0, 0.0, 0.0, 1.0)
    return (qx / n, qy / n, qz / n, qw / n)


def quat_slerp(
    q0: tuple[float, float, float, float],
    q1: tuple[float, float, float, float],
    t: float,
) -> tuple[float, float, float, float]:
    x0, y0, z0, w0 = q0
    x1, y1, z1, w1 = q1
    dot = x0 * x1 + y0 * y1 + z0 * z1 + w0 * w1
    if dot < 0.0:
        x1, y1, z1, w1 = -x1, -y1, -z1, -w1
        dot = -dot
    if dot > 0.9995:
        rx = x0 + t * (x1 - x0)
        ry = y0 + t * (y1 - y0)
        rz = z0 + t * (z1 - z0)
        rw = w0 + t * (w1 - w0)
    else:
        theta_0 = math.acos(min(1.0, max(-1.0, dot)))
        sin_0 = math.sin(theta_0)
        s0 = math.sin((1.0 - t) * theta_0) / sin_0
        s1 = math.sin(t * theta_0) / sin_0
        rx = s0 * x0 + s1 * x1
        ry = s0 * y0 + s1 * y1
        rz = s0 * z0 + s1 * z1
        rw = s0 * w0 + s1 * w1
    n = math.sqrt(rx * rx + ry * ry + rz * rz + rw * rw)
    if n < 1e-8:
        return (0.0, 0.0, 0.0, 1.0)
    return (rx / n, ry / n, rz / n, rw / n)


def quat_to_mat4(q: tuple[float, float, float, float], t: tuple[float, float, float], s: tuple[float, float, float]) -> np.ndarray:
    """Row-vector-compatible or column-vector 4x4 TRS matrix (column-vector convention: M @ [x,y,z,1])."""
    x, y, z, w = q
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    sx, sy, sz = s
    m = np.eye(4, dtype=np.float64)
    m[0, 0] = (1.0 - 2.0 * (yy + zz)) * sx
    m[0, 1] = 2.0 * (xy - wz) * sy
    m[0, 2] = 2.0 * (xz + wy) * sz
    m[0, 3] = t[0]
    m[1, 0] = 2.0 * (xy + wz) * sx
    m[1, 1] = (1.0 - 2.0 * (xx + zz)) * sy
    m[1, 2] = 2.0 * (yz - wx) * sz
    m[1, 3] = t[1]
    m[2, 0] = 2.0 * (xz - wy) * sx
    m[2, 1] = 2.0 * (yz + wx) * sy
    m[2, 2] = (1.0 - 2.0 * (xx + yy)) * sz
    m[2, 3] = t[2]
    return m


# ---------------------------------------------------------------------------
# Full TRSKL Parser
# ---------------------------------------------------------------------------

def parse_trskl_full(path: Path) -> dict[str, Any]:
    """Parses full TRSKL transform_nodes with local SRT (already converted to Unity LH)."""
    d = path.read_bytes()
    r = fb_root(d)
    f1 = fb_field(d, r, 1)
    if not f1:
        raise ValueError(f"TRSKL {path} missing transform_nodes")
    vs, cnt = fb_vec(d, f1)

    nodes: list[dict[str, Any]] = []
    rig_to_node: dict[int, int] = {}
    name_to_node: dict[str, int] = {}

    for idx in range(cnt):
        ep = vs + 4 * idx
        no = ep + fb_u32(d, ep)
        name = fb_str(d, fb_field(d, no, 0)) if fb_field(d, no, 0) else f"node_{idx}"
        parent = fb_i32(d, fb_field(d, no, 4)) if fb_field(d, no, 4) else -1
        rig = fb_i32(d, fb_field(d, no, 5)) if fb_field(d, no, 5) else -1

        scl_rh = (1.0, 1.0, 1.0)
        rot_rh_euler = (0.0, 0.0, 0.0)
        pos_rh = (0.0, 0.0, 0.0)
        srt_p = fb_field(d, no, 1)
        if srt_p:
            srt = srt_p + fb_u32(d, srt_p)
            fp_s = fb_field(d, srt, 0)
            fp_r = fb_field(d, srt, 1)
            fp_t = fb_field(d, srt, 2)
            if fp_s:
                scl_rh = struct.unpack_from("<3f", d, fp_s)
            if fp_r:
                rot_rh_euler = struct.unpack_from("<3f", d, fp_r)
            if fp_t:
                pos_rh = struct.unpack_from("<3f", d, fp_t)

        q_rh = euler_xyz_to_quat(*rot_rh_euler)
        pos_u = rh_pos_to_unity(pos_rh)
        rot_u = rh_quat_to_unity(q_rh)
        scl_u = (float(scl_rh[0]), float(scl_rh[1]), float(scl_rh[2]))

        nodes.append({
            "idx": idx,
            "name": name,
            "parent": parent,
            "rig_idx": rig,
            "pos": list(pos_u),
            "rot": list(rot_u),
            "scl": list(scl_u),
        })
        if rig >= 0:
            rig_to_node[rig] = idx
        if name not in name_to_node:
            name_to_node[name] = idx

    # Build unique path strings relative to root (under "ZA_Rig")
    # Disambiguate any duplicate sibling names just in case
    children_by_parent: dict[int, list[int]] = {}
    for n in nodes:
        children_by_parent.setdefault(n["parent"], []).append(n["idx"])
    for plist in children_by_parent.values():
        seen: dict[str, int] = {}
        for ni in plist:
            nm = nodes[ni]["name"]
            if nm in seen:
                seen[nm] += 1
                nodes[ni]["unique_name"] = f"{nm}__dup{seen[nm]}"
            else:
                seen[nm] = 0
                nodes[ni]["unique_name"] = nm

    paths: list[str] = [""] * len(nodes)
    for idx in range(len(nodes)):
        parts: list[str] = []
        cur = idx
        guard = 0
        while 0 <= cur < len(nodes) and guard < 64:
            parts.append(nodes[cur]["unique_name"])
            cur = nodes[cur]["parent"]
            guard += 1
        parts.reverse()
        paths[idx] = "ZA_Rig/" + "/".join(parts)
        nodes[idx]["path"] = paths[idx]

    # Build bone order by rig_idx (0..max_rig)
    max_rig = max(rig_to_node.keys()) if rig_to_node else -1
    rig_node_list = [rig_to_node.get(r, 0) for r in range(max_rig + 1)]

    return {
        "nodes": nodes,
        "rig_to_node": rig_to_node,
        "rig_node_list": rig_node_list,
        "name_to_node": name_to_node,
        "bone_count": len(rig_node_list),
    }


# ---------------------------------------------------------------------------
# TRANM Discovery & Decoder
# ---------------------------------------------------------------------------

def discover_tranms(pack_dir: Path, za_pack: str) -> dict[str, Path]:
    """Maps .tranm base filenames (e.g. 'pm0254_51_00_00001_battlewait01_loop.tranm') -> Path."""
    species_dir = za_pack.split("_")[0]  # e.g. 'pm0254'
    hash_to_file: dict[str, Path] = {}
    for f in pack_dir.iterdir():
        if f.suffix == ".bin" and "_" in f.stem:
            hhex = f.stem.split("_")[1].lower()
            hash_to_file[hhex] = f

    tranm_names: list[str] = []
    for f in pack_dir.glob("*.bin"):
        raw = f.read_bytes()
        if b".tranm" in raw:
            found = [m.decode("utf-8", errors="ignore") for m in re.findall(rb"[a-zA-Z0-9_./-]+\.tranm", raw)]
            if len(found) > len(tranm_names):
                tranm_names = found

    # Candidate FNV1a prefixes used in Legends: Z-A trpak archives
    prefixes = [
        f"ik_pokemon/data/{species_dir}/{za_pack}/",
        f"pokemon/data/{species_dir}/{za_pack}/",
        f"ik_pokemon/data/{species_dir}/{za_pack}/motion/",
        "",
    ]

    result: dict[str, Path] = {}
    for name in tranm_names:
        base = os.path.basename(name)
        for pfx in prefixes:
            h = f"{fnv1a_64(pfx + base):016x}"
            if h in hash_to_file:
                result[base] = hash_to_file[h]
                break
    return result


def _sample_vec3_track(d: bytes, ttype: int, field_p: int, src_frames: int) -> list[tuple[float, float, float]] | None:
    if ttype == 0 or field_p == 0:
        return None
    tbl = field_p + fb_u32(d, field_p)
    if ttype == 1:  # FixedVectorTrack
        fp = fb_field(d, tbl, 0)
        if not fp:
            return None
        v = struct.unpack_from("<3f", d, fp)
        return [v] * src_frames
    if ttype == 2:  # DynamicVectorTrack
        vp = fb_field(d, tbl, 0)
        if not vp:
            return None
        vs, cnt = fb_vec(d, vp)
        vals = [struct.unpack_from("<3f", d, vs + 12 * i) for i in range(cnt)]
        if not vals:
            return None
        return [vals[min(f, cnt - 1)] for f in range(src_frames)]
    if ttype in (3, 4):  # Framed16VectorTrack (3) / Framed8VectorTrack (4)
        fp = fb_field(d, tbl, 0)
        vp = fb_field(d, tbl, 1)
        if not fp or not vp:
            return None
        fs, fc = fb_vec(d, fp)
        vs, vc = fb_vec(d, vp)
        n = min(fc, vc)
        if n == 0:
            return None
        if ttype == 3:
            keys = [struct.unpack_from("<H", d, fs + 2 * i)[0] for i in range(n)]
        else:
            keys = [d[fs + i] for i in range(n)]
        vals = [struct.unpack_from("<3f", d, vs + 12 * i) for i in range(n)]
        out: list[tuple[float, float, float]] = []
        ki = 0
        for f in range(src_frames):
            while ki + 1 < n and keys[ki + 1] <= f:
                ki += 1
            if f <= keys[0] or ki + 1 >= n:
                out.append(vals[min(ki, n - 1)])
            else:
                k0, k1 = keys[ki], keys[ki + 1]
                if k1 <= k0:
                    out.append(vals[ki])
                else:
                    alpha = (f - k0) / (k1 - k0)
                    v0, v1 = vals[ki], vals[ki + 1]
                    out.append((
                        v0[0] + alpha * (v1[0] - v0[0]),
                        v0[1] + alpha * (v1[1] - v0[1]),
                        v0[2] + alpha * (v1[2] - v0[2]),
                    ))
        return out
    return None


def _sample_rot_track(d: bytes, rtype: int, field_p: int, src_frames: int) -> list[tuple[float, float, float, float]] | None:
    if rtype == 0 or field_p == 0:
        return None
    tbl = field_p + fb_u32(d, field_p)
    if rtype == 1:  # FixedRotationTrack
        fp = fb_field(d, tbl, 0)
        if not fp:
            return None
        q = unpack_quat48(*struct.unpack_from("<3H", d, fp))
        return [q] * src_frames
    if rtype == 2:  # DynamicRotationTrack
        vp = fb_field(d, tbl, 0)
        if not vp:
            return None
        vs, cnt = fb_vec(d, vp)
        vals = [unpack_quat48(*struct.unpack_from("<3H", d, vs + 6 * i)) for i in range(cnt)]
        if not vals:
            return None
        return [vals[min(f, cnt - 1)] for f in range(src_frames)]
    if rtype in (3, 4):  # Framed16 (3) / Framed8 (4)
        fp = fb_field(d, tbl, 0)
        vp = fb_field(d, tbl, 1)
        if not fp or not vp:
            return None
        fs, fc = fb_vec(d, fp)
        vs, vc = fb_vec(d, vp)
        n = min(fc, vc)
        if n == 0:
            return None
        if rtype == 3:
            keys = [struct.unpack_from("<H", d, fs + 2 * i)[0] for i in range(n)]
        else:
            keys = [d[fs + i] for i in range(n)]
        vals = [unpack_quat48(*struct.unpack_from("<3H", d, vs + 6 * i)) for i in range(n)]
        out: list[tuple[float, float, float, float]] = []
        ki = 0
        for f in range(src_frames):
            while ki + 1 < n and keys[ki + 1] <= f:
                ki += 1
            if f <= keys[0] or ki + 1 >= n:
                out.append(vals[min(ki, n - 1)])
            else:
                k0, k1 = keys[ki], keys[ki + 1]
                if k1 <= k0:
                    out.append(vals[ki])
                else:
                    alpha = (f - k0) / (k1 - k0)
                    out.append(quat_slerp(vals[ki], vals[ki + 1], alpha))
        return out
    return None


def decode_tranm(path: Path, skel: dict[str, Any], target_fps: int = 30) -> dict[str, Any]:
    """Decodes a TRANM file into 30fps Unity LH dense curves keyed by bone node index."""
    d = path.read_bytes()
    r = fb_root(d)
    f0 = fb_field(d, r, 0)
    f1 = fb_field(d, r, 1)
    if not f0 or not f1:
        raise ValueError(f"Invalid TRANM {path}")

    info_t = f0 + fb_u32(d, f0)
    does_loop = bool(fb_u32(d, fb_field(d, info_t, 0))) if fb_field(d, info_t, 0) else False
    src_frames = fb_u32(d, fb_field(d, info_t, 1)) if fb_field(d, info_t, 1) else 1
    src_fps = fb_u32(d, fb_field(d, info_t, 2)) if fb_field(d, info_t, 2) else 60
    if src_fps not in (30, 60):
        src_fps = 60
    src_frames = max(1, src_frames)

    step = 2 if (src_fps == 60 and target_fps == 30) else 1
    sample_indices = list(range(0, src_frames, step))
    if sample_indices[-1] != src_frames - 1 and src_frames > 1:
        sample_indices.append(src_frames - 1)
    if len(sample_indices) < 2:
        sample_indices = [0, 0]
    out_frames = len(sample_indices)

    ba_t = f1 + fb_u32(d, f1)
    tvec = fb_field(d, ba_t, 0)
    name_to_node = skel["name_to_node"]
    nodes = skel["nodes"]

    # Track data per node_idx: {'pos': [(x,y,z)...], 'rot': [(x,y,z,w)...], 'scl': [(x,y,z)...]}
    tracks: dict[int, dict[str, Any]] = {}
    if tvec:
        vs, cnt = fb_vec(d, tvec)
        for idx in range(cnt):
            ep = vs + 4 * idx
            tr = ep + fb_u32(d, ep)
            bname = fb_str(d, fb_field(d, tr, 0)) if fb_field(d, tr, 0) else ""
            if bname not in name_to_node:
                continue
            nidx = name_to_node[bname]

            st = fb_u8(d, fb_field(d, tr, 1)) if fb_field(d, tr, 1) else 0
            sp = fb_field(d, tr, 2)
            rt = fb_u8(d, fb_field(d, tr, 3)) if fb_field(d, tr, 3) else 0
            rp = fb_field(d, tr, 4)
            tt = fb_u8(d, fb_field(d, tr, 5)) if fb_field(d, tr, 5) else 0
            tp = fb_field(d, tr, 6)

            entry: dict[str, Any] = {}
            pos_rh = _sample_vec3_track(d, tt, tp, src_frames)
            if pos_rh is not None:
                entry["pos"] = [rh_pos_to_unity(pos_rh[f]) for f in sample_indices]

            rot_rh = _sample_rot_track(d, rt, rp, src_frames)
            if rot_rh is not None:
                rot_u = [rh_quat_to_unity(rot_rh[f]) for f in sample_indices]
                # Enforce shortest-path quaternion continuity across consecutive frames!
                for fi in range(1, len(rot_u)):
                    q0 = rot_u[fi - 1]
                    q1 = rot_u[fi]
                    if q0[0] * q1[0] + q0[1] * q1[1] + q0[2] * q1[2] + q0[3] * q1[3] < 0.0:
                        rot_u[fi] = (-q1[0], -q1[1], -q1[2], -q1[3])
                entry["rot"] = rot_u

            scl_rh = _sample_vec3_track(d, st, sp, src_frames)
            if scl_rh is not None:
                # Only include scale curve if it meaningfully deviates from rest scale
                rest_s = nodes[nidx]["scl"]
                max_diff = max(
                    abs(scl_rh[f][0] - rest_s[0]) + abs(scl_rh[f][1] - rest_s[1]) + abs(scl_rh[f][2] - rest_s[2])
                    for f in sample_indices
                )
                if max_diff > 1e-3:
                    entry["scl"] = [(float(scl_rh[f][0]), float(scl_rh[f][1]), float(scl_rh[f][2])) for f in sample_indices]

            if entry:
                tracks[nidx] = entry

    return {
        "does_loop": does_loop,
        "frame_count": out_frames,
        "sample_rate": float(target_fps),
        "tracks": tracks,
    }


def evaluate_skeleton_world_mats(
    skel: dict[str, Any],
    clip: dict[str, Any] | None = None,
    frame: int = 0,
) -> list[np.ndarray]:
    """Computes 4x4 world matrices for all TRSKL nodes in Unity LH unscaled space."""
    nodes = skel["nodes"]
    tracks = clip["tracks"] if clip else {}
    worlds: list[np.ndarray | None] = [None] * len(nodes)

    def get_w(i: int) -> np.ndarray:
        if worlds[i] is not None:
            return worlds[i]  # type: ignore[return-value]
        n = nodes[i]
        tr = tracks.get(i)
        pos = tr["pos"][frame] if (tr and "pos" in tr) else tuple(n["pos"])
        rot = tr["rot"][frame] if (tr and "rot" in tr) else tuple(n["rot"])
        scl = tr["scl"][frame] if (tr and "scl" in tr) else tuple(n["scl"])
        local = quat_to_mat4(rot, pos, scl)
        p = n["parent"]
        w = local if (p < 0 or p >= len(nodes)) else (get_w(p) @ local)
        worlds[i] = w
        return w

    return [get_w(i) for i in range(len(nodes))]


def compute_posed_metrics(
    skel: dict[str, Any],
    rest_verts_unity: np.ndarray,
    rig_indices: np.ndarray,
    rig_weights: np.ndarray,
    idle_clip: dict[str, Any],
) -> dict[str, float]:
    """Evaluates waitA01 across 5 evenly spaced frames and computes full, core-body, and foot-grounded bounds."""
    rest_w = evaluate_skeleton_world_mats(skel, None, 0)
    rig_nodes = skel["rig_node_list"]
    nodes = skel["nodes"]
    n_bones = len(rig_nodes)
    inv_rests = [np.linalg.inv(rest_w[nidx]) for nidx in rig_nodes]

    # Classify skin bones: exclude appendages (wings/feelers/fins/ribbons/flames/hairs/tails) from core height
    non_core_kw = ("wing", "feeler", "fin", "feather", "ribbon", "flame", "fire", "hair", "ear", "horn", "antenna", "petal", "leaf", "cloth", "cape")
    foot_kw = ("foot", "toe", "ankle", "sole", "heel", "claw")
    is_core_bone = np.ones(n_bones, dtype=bool)
    is_foot_bone = np.zeros(n_bones, dtype=bool)
    for bi, nidx in enumerate(rig_nodes):
        nm = nodes[nidx]["name"].lower()
        if any(k in nm for k in non_core_kw):
            is_core_bone[bi] = False
        if any(k in nm for k in foot_kw) and not any(k in nm for k in ("hand", "arm", "finger", "wing")):
            is_foot_bone[bi] = True

    prim_bi = np.clip(rig_indices[:, 0], 0, max(0, n_bones - 1))
    core_mask = is_core_bone[prim_bi]
    foot_mask = is_foot_bone[prim_bi] & (rig_weights[:, 0] > 0.35)

    fc = idle_clip["frame_count"]
    sample_frames = sorted(set([0, fc // 4, fc // 2, (3 * fc) // 4, max(0, fc - 1)]))
    v_h = np.ones((len(rest_verts_unity), 4), dtype=np.float64)
    v_h[:, :3] = rest_verts_unity

    full_mins, full_maxs, core_mins, core_maxs, foot_mins, med_zs = [], [], [], [], [], []
    for fr in sample_frames:
        pose_w = evaluate_skeleton_world_mats(skel, idle_clip, fr)
        skin_mats = np.zeros((n_bones, 4, 4), dtype=np.float64)
        for bi, nidx in enumerate(rig_nodes):
            skin_mats[bi] = pose_w[nidx] @ inv_rests[bi]
        posed = np.zeros((len(rest_verts_unity), 3), dtype=np.float64)
        for k in range(4):
            wk = rig_weights[:, k]
            mask = wk > 1e-6
            if not np.any(mask):
                continue
            ik = np.clip(rig_indices[mask, k], 0, n_bones - 1)
            mats = skin_mats[ik]
            vp = np.einsum("mij,mj->mi", mats[:, :3, :], v_h[mask])
            posed[mask] += vp * wk[mask, None]
        ys = posed[:, 1]
        full_mins.append(float(np.percentile(ys, 0.5)))
        full_maxs.append(float(np.max(ys)))
        med_zs.append(float(np.median(posed[:, 2])))
        if np.sum(core_mask) >= max(50, int(0.20 * len(ys))):
            cys = ys[core_mask]
            core_mins.append(float(np.percentile(cys, 0.5)))
            core_maxs.append(float(np.percentile(cys, 99.5)))
        if np.sum(foot_mask) >= 12:
            foot_mins.append(float(np.percentile(ys[foot_mask], 2.0)))

    full_min_y = float(np.median(full_mins))
    full_max_y = float(np.median(full_maxs))
    core_h = (float(np.median(core_maxs)) - float(np.median(core_mins))) if core_maxs else (full_max_y - full_min_y)
    # Anchor grounding on foot soles ONLY when feet sit within 10% of the model's lowest point
    # (fixes tail-dip bipeds like Tyranitar while preventing ballgowns like Gardevoir from sinking!).
    if foot_mins:
        f_min = float(np.median(foot_mins))
        ground_min_y = f_min if abs(f_min - full_min_y) < 0.10 * max(full_max_y - full_min_y, 0.5) else full_min_y
    else:
        ground_min_y = full_min_y

    return {
        "full_min_y": full_min_y,
        "full_max_y": full_max_y,
        "full_h": max(full_max_y - full_min_y, 0.20),
        "core_h": max(core_h, 0.20),
        "ground_min_y": ground_min_y,
        "med_z": float(np.median(med_zs)),
    }


def compute_posed_bounds_y(
    skel: dict[str, Any],
    rest_verts_unity: np.ndarray,
    rig_indices: np.ndarray,
    rig_weights: np.ndarray,
    idle_clip: dict[str, Any],
    frame: int = 0,
) -> tuple[float, float, float]:
    """Backward-compatible wrapper around compute_posed_metrics."""
    m = compute_posed_metrics(skel, rest_verts_unity, rig_indices, rig_weights, idle_clip)
    return m["ground_min_y"], m["full_max_y"], m["med_z"]


# ---------------------------------------------------------------------------
# BDSP Clip Slot Mapping & Dense Buffer Packing
# ---------------------------------------------------------------------------

# Maps canonical BDSP clip role -> ordered list of Legends: Z-A .tranm substrings to match
BDSP_ROLE_TO_ZA_PATTERNS: list[tuple[str, bool, list[str]]] = [
    ("waitA01", True, ["battlewait01_loop", "wait01_loop", "idle01_loop"]),
    ("waitB01", False, ["battleidle02", "battleidle01", "idle02", "battlewait01_loop"]),
    ("landA01", False, ["land02", "land01", "jumpdown01_loop", "roar01", "battlewait01_loop"]),
    ("roar01", False, ["megaappeal01", "roar01", "battleidle02", "battlewait01_loop"]),
    ("buturi01", False, ["attack01", "attack02", "attack03_start", "rangeattack01", "roar01"]),
    ("tokusyu01", False, ["rangeattack01", "rangeattack02_start", "attack02", "attack01", "roar01"]),
    ("damageS01", False, ["damage01", "damage02", "stun01_start", "battlewait01_loop"]),
    ("down01", False, ["down01_start", "down01_loop", "damage02", "damage01"]),
    ("walk01", True, ["walk01_loop", "run01_loop", "battlewait01_loop"]),
]


def match_tranm_for_role(tranms: dict[str, Path], patterns: list[str]) -> tuple[str, Path] | None:
    lower_map = {k.lower(): (k, v) for k, v in tranms.items()}
    for pat in patterns:
        # Exact suffix match before .tranm first (e.g. _attack01.tranm)
        for lk, (orig, path) in lower_map.items():
            if lk.endswith(f"_{pat}.tranm") or lk == f"{pat}.tranm":
                return orig, path
        # Substring fallback
        for lk, (orig, path) in lower_map.items():
            if pat in lk:
                return orig, path
    return None


def export_rig_and_anims(
    pack_dir: Path,
    za_pack: str,
    trskl_path: Path,
    out_dir: Path,
    form_id: str,
    scale: float,
    ty: float,
    tz: float,
) -> dict[str, Any]:
    """Exports <form_id>_rig_anim.json + <form_id>_anim_samples.bin for MegaBundleTool."""
    skel = parse_trskl_full(trskl_path)
    tranms = discover_tranms(pack_dir, za_pack)
    if not tranms:
        raise FileNotFoundError(f"No .tranm files resolved in {pack_dir} ({za_pack})")

    decoded_cache: dict[Path, dict[str, Any]] = {}
    clips_meta: list[dict[str, Any]] = []
    samples_blob = bytearray()
    nodes = skel["nodes"]

    for role, force_loop, patterns in BDSP_ROLE_TO_ZA_PATTERNS:
        hit = match_tranm_for_role(tranms, patterns)
        if hit is None:
            # Ultimate fallback: pick first available tranm
            orig_name, tpath = next(iter(tranms.items()))
        else:
            orig_name, tpath = hit

        if tpath not in decoded_cache:
            decoded_cache[tpath] = decode_tranm(tpath, skel, target_fps=30)
        clip = decoded_cache[tpath]

        frame_count = clip["frame_count"]
        sample_rate = clip["sample_rate"]
        tracks = clip["tracks"]

        # Build Unity genericBindings + flat DenseClip m_SampleArray (frame-major: [f * curveCount + c])
        # CRITICAL Unity AnimationClip invariant: transform bindings MUST be grouped strictly by
        # attribute order: all attr=1 (Position), then all attr=2 (Rotation), then all attr=3 (Scale)!
        bindings: list[dict[str, Any]] = []
        curve_channels: list[list[float]] = []  # list of per-channel float[frame_count]

        sorted_nidxs = sorted(tracks.keys())
        for nidx in sorted_nidxs:
            tr = tracks[nidx]
            if "pos" in tr:
                bindings.append({"path": nodes[nidx]["path"], "attr": 1})
                pts = tr["pos"]
                curve_channels.append([p[0] for p in pts])
                curve_channels.append([p[1] for p in pts])
                curve_channels.append([p[2] for p in pts])
        for nidx in sorted_nidxs:
            tr = tracks[nidx]
            if "rot" in tr:
                bindings.append({"path": nodes[nidx]["path"], "attr": 2})
                qs = tr["rot"]
                curve_channels.append([q[0] for q in qs])
                curve_channels.append([q[1] for q in qs])
                curve_channels.append([q[2] for q in qs])
                curve_channels.append([q[3] for q in qs])
        for nidx in sorted_nidxs:
            tr = tracks[nidx]
            if "scl" in tr:
                bindings.append({"path": nodes[nidx]["path"], "attr": 3})
                ss = tr["scl"]
                curve_channels.append([s[0] for s in ss])
                curve_channels.append([s[1] for s in ss])
                curve_channels.append([s[2] for s in ss])

        curve_count = len(curve_channels)
        # Transpose to frame-major float32 array of shape (frame_count, curve_count)
        arr = np.array(curve_channels, dtype=np.float32).T.copy()
        if not np.all(np.isfinite(arr)):
            arr = np.nan_to_num(arr, nan=0.0, posinf=0.0, neginf=0.0)

        byte_offset = len(samples_blob)
        raw_bytes = arr.tobytes()
        samples_blob.extend(raw_bytes)

        clips_meta.append({
            "role": role,
            "source_tranm": orig_name,
            "loop": bool(force_loop or clip["does_loop"]) if role in ("waitA01", "walk01") else False,
            "frame_count": frame_count,
            "curve_count": curve_count,
            "sample_rate": sample_rate,
            "sample_byte_offset": byte_offset,
            "sample_float_count": int(arr.size),
            "bindings": bindings,
        })

    samples_path = out_dir / f"{form_id}_anim_samples.bin"
    samples_path.write_bytes(bytes(samples_blob))

    rig_anim = {
        "za_pack": za_pack,
        "rig_scale": float(scale),
        "rig_offset": [0.0, float(ty), float(tz)],
        "nodes": skel["nodes"],
        "rig_node_list": skel["rig_node_list"],
        "samples_file": samples_path.name,
        "clips": clips_meta,
    }
    rig_json = out_dir / f"{form_id}_rig_anim.json"
    rig_json.write_text(json.dumps(rig_anim))
    return rig_anim


if __name__ == "__main__":
    # Quick self-test on Mega Sceptile (pm0254_51_00)
    pack = Path("/tmp/za_extract/pm0254_51_00")
    trskl = pack / "142_fd77795c82ef246d.bin"
    out = Path("/tmp/za_anim_test")
    out.mkdir(parents=True, exist_ok=True)
    res = export_rig_and_anims(pack, "pm0254_51_00", trskl, out, "pm0254_51_00", 1.0, 0.0, 0.0)
    print(f"Exported {len(res['nodes'])} nodes, {len(res['rig_node_list'])} skin bones, {len(res['clips'])} clips:")
    for c in res["clips"]:
        print(f"  {c['role']:10s} <- {c['source_tranm']:42s} frames={c['frame_count']:3d} curves={c['curve_count']:4d} loop={c['loop']}")
