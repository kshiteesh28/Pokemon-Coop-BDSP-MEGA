#!/usr/bin/env python3
"""Universal Legends: Z-A -> BDSP Switch Mesh + Texture + QA Pipeline.

Converts any extracted Legends: Z-A Mega Evolution pack (/tmp/za_extract/<pack>)
against its vanilla BDSP species skeleton (/tmp/za_bdsp_build/van_<species>.json)
into Switch-ready mesh JSON + RGBA32 textures (Normal & Shiny) + 3-angle Cycles QA renders.

Run inside headless Blender 5.x:
  /tmp/blender_hfs/Blender/Blender.app/Contents/MacOS/Blender -b --python scripts/build_za_batch.py -- pm0398_01 pm0445_01 ...
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

REPO_ROOT = Path("/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version")
ZA_ROOT = Path("/tmp/za_extract")
OUT_ROOT = Path("/tmp/za_bdsp_build")
QA_DIR = REPO_ROOT / "build/test-renders"

sys.path.insert(0, str(REPO_ROOT / "scripts"))
from za_mega_catalog import MEGA_CATALOG, MegaFormSpec


# ---------------------------------------------------------------------------
# FlatBuffers helpers
# ---------------------------------------------------------------------------

def fb_table(buf: bytes, off: int) -> tuple[int, int]:
    voff = off - struct.unpack_from("<i", buf, off)[0]
    vlen, _ = struct.unpack_from("<HH", buf, voff)
    return voff, vlen


def fb_field(buf: bytes, off: int, idx: int) -> int:
    voff, vlen = fb_table(buf, off)
    fpos = 4 + idx * 2
    if fpos >= vlen:
        return 0
    foff = struct.unpack_from("<H", buf, voff + fpos)[0]
    return off + foff if foff else 0


def fb_str(buf: bytes, off: int) -> str:
    p = off + struct.unpack_from("<I", buf, off)[0]
    n = struct.unpack_from("<I", buf, p)[0]
    return buf[p + 4 : p + 4 + n].decode("utf-8", errors="replace")


def fb_vec(buf: bytes, off: int) -> tuple[int, int]:
    p = off + struct.unpack_from("<I", buf, off)[0]
    n = struct.unpack_from("<I", buf, p)[0]
    return p + 4, n


def fnv1a_64(s: str) -> int:
    h = 0xCBF29CE484222325
    for b in s.encode("utf-8"):
        h ^= b
        h = (h * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return h


# ---------------------------------------------------------------------------
# Pack Auto-Discovery
# ---------------------------------------------------------------------------

def discover_za_pack(pack_dir: Path) -> dict[str, Path]:
    by_hash: dict[str, Path] = {}
    for f in pack_dir.iterdir():
        parts = f.stem.split("_")
        if len(parts) >= 2:
            by_hash[parts[1].lower()] = f

    trmsh_path = None
    trmsh_shapes = 0
    trmbf_name = ""
    for f in sorted(pack_dir.glob("*.trmsh")):
        b = f.read_bytes()
        if b".trmbf" not in b or b"lod" in b.lower():
            continue
        try:
            root = struct.unpack_from("<I", b, 0)[0]
            f2 = fb_field(b, root, 2)
            if not f2:
                continue
            bfn = fb_str(b, f2)
            if bfn.endswith(".trmbf") and "lod" not in bfn.lower():
                _, sn = fb_vec(b, fb_field(b, root, 1))
                if sn > 0:
                    trmsh_path = f
                    trmsh_shapes = sn
                    trmbf_name = bfn
                    break
        except Exception:
            pass
    if not trmsh_path:
        raise FileNotFoundError(f"No primary TRMSH found in {pack_dir}")

    trmbf_path = by_hash.get(f"{fnv1a_64(trmbf_name):016x}")
    if not trmbf_path:
        cands = []
        for f in pack_dir.glob("*.bin"):
            b = f.read_bytes()
            if len(b) < 10000:
                continue
            try:
                root = struct.unpack_from("<I", b, 0)[0]
                f1 = fb_field(b, root, 1)
                if f1 and fb_vec(b, f1)[1] == trmsh_shapes:
                    cands.append((f.stat().st_size, f))
            except Exception:
                pass
        cands.sort(reverse=True)
        if not cands:
            raise FileNotFoundError(f"No matching TRMBF found in {pack_dir}")
        trmbf_path = cands[0][1]

    skls = []
    for f in pack_dir.glob("*.bin"):
        b = f.read_bytes()
        if (b"waist\x00" in b or b"spine_01\x00" in b or b"origin\x00" in b) and b".trmbf" not in b and b"BaseColorMap" not in b:
            try:
                root = struct.unpack_from("<I", b, 0)[0]
                f1 = fb_field(b, root, 1)
                if f1:
                    _, vn = fb_vec(b, f1)
                    if 20 < vn < 500:
                        skls.append((vn, f))
            except Exception:
                pass
    skls.sort(reverse=True)
    if not skls:
        raise FileNotFoundError(f"No full TRSKL found in {pack_dir}")
    trskl_path = skls[0][1]

    trmsh_h = int(trmsh_path.stem.split("_")[1], 16)
    mtrs = []
    for f in sorted(pack_dir.glob("*.trmtr")):
        if f.stat().st_size < 3000:
            continue
        if b"BaseColorMap" in f.read_bytes():
            mh = int(f.stem.split("_")[1], 16)
            mtrs.append((f.stat().st_size, abs(mh - trmsh_h), f))
    if not mtrs:
        for f in sorted(pack_dir.glob("*.trmtr")):
            if b"BaseColorMap" in f.read_bytes():
                mh = int(f.stem.split("_")[1], 16)
                mtrs.append((f.stat().st_size, abs(mh - trmsh_h), f))
    if not mtrs:
        raise FileNotFoundError(f"No TRMTR found in {pack_dir}")
    # Keep the top 2 largest TRMTR files (Normal & Shiny), then pick Normal as the one closest in hash to TRMSH
    mtrs.sort(key=lambda x: -x[0])
    top2 = sorted(mtrs[:2], key=lambda x: x[1])
    norm_trmtr = top2[0][2]
    rare_trmtr = top2[1][2] if len(top2) > 1 else top2[0][2]

    return {
        "trmsh": trmsh_path,
        "trmbf": trmbf_path,
        "trskl": trskl_path,
        "norm_trmtr": norm_trmtr,
        "rare_trmtr": rare_trmtr,
    }


# ---------------------------------------------------------------------------
# BNTX Tegra Block-Linear deswizzler -> DX10 DDS -> Blender float32 RGBA
# ---------------------------------------------------------------------------

def div_round_up(n: int, d: int) -> int:
    return (n + d - 1) // d


def get_addr_block_linear(x: int, y: int, w: int, bpp: int, block_height: int) -> int:
    image_width_in_gobs = div_round_up(w * bpp, 64)
    gob_address = (
        (y // (8 * block_height)) * 512 * block_height * image_width_in_gobs
        + (x * bpp // 64) * 512 * block_height
        + (y % (8 * block_height) // 8) * 512
    )
    x_rem = x * bpp
    gob_address += (
        ((x_rem % 64) // 32) * 256
        + ((y % 8) // 2) * 64
        + ((x_rem % 32) // 16) * 32
        + (y % 2) * 16
        + (x_rem % 16)
    )
    return gob_address


BNTX_FMT_MAP = {
    0x1A01: (71, 4, 4, 8),   # BC1_UNORM
    0x1A06: (72, 4, 4, 8),   # BC1_UNORM_SRGB
    0x1B01: (74, 4, 4, 16),  # BC2_UNORM
    0x1B06: (75, 4, 4, 16),  # BC2_UNORM_SRGB
    0x1C01: (77, 4, 4, 16),  # BC3_UNORM
    0x1C06: (78, 4, 4, 16),  # BC3_UNORM_SRGB
    0x1D01: (80, 4, 4, 8),   # BC4_UNORM
    0x1E01: (83, 4, 4, 16),  # BC5_UNORM
    0x2001: (98, 4, 4, 16),  # BC7_UNORM
    0x2006: (99, 4, 4, 16),  # BC7_UNORM_SRGB
}


def index_bntx_dir(pack_dir: Path) -> dict[str, Path]:
    by_name: dict[str, Path] = {}
    for f in pack_dir.glob("*.bntx"):
        b = f.read_bytes()
        brti = b.find(b"BRTI")
        if brti == -1:
            continue
        nptr = struct.unpack_from("<Q", b, brti + 0x60)[0]
        nlen = struct.unpack_from("<H", b, nptr)[0]
        tname = b[nptr + 2 : nptr + 2 + nlen].decode("utf-8", errors="replace")
        by_name[tname] = f
        by_name[tname + ".bntx"] = f
    return by_name


_DECODED_CACHE: dict[Path, np.ndarray] = {}


def decode_bntx_rgba(bntx_path: Path, tmp_dir: Path) -> np.ndarray:
    if bntx_path in _DECODED_CACHE:
        return _DECODED_CACHE[bntx_path]
    buf = bntx_path.read_bytes()
    brti = buf.find(b"BRTI")
    fmt = struct.unpack_from("<I", buf, brti + 0x1C)[0]
    w, h = struct.unpack_from("<II", buf, brti + 0x24)
    bh_log2 = struct.unpack_from("<I", buf, brti + 0x34)[0] & 7
    block_height = 1 << bh_log2
    mip_ptr = struct.unpack_from("<Q", buf, brti + 0x70)[0]
    data_off = struct.unpack_from("<Q", buf, mip_ptr)[0]

    if fmt == 0x0B01 or fmt == 0x0B06:
        # Uncompressed R8G8B8A8_UNORM / SRGB
        dxgi = 28 if fmt == 0x0B01 else 29
        bw, bh_px, bpp = 1, 1, 4
    else:
        dxgi, bw, bh_px, bpp = BNTX_FMT_MAP[fmt]

    wb = div_round_up(w, bw)
    hb = div_round_up(h, bh_px)
    while block_height > 1 and (block_height * 8) >= hb * 2:
        block_height //= 2

    out = bytearray(wb * hb * bpp)
    src = buf[data_off:]
    for y in range(hb):
        for x in range(wb):
            addr = get_addr_block_linear(x, y, wb, bpp, block_height)
            dst_off = (y * wb + x) * bpp
            if addr + bpp <= len(src):
                out[dst_off : dst_off + bpp] = src[addr : addr + bpp]

    hdr = bytearray(148)
    hdr[0:4] = b"DDS "
    struct.pack_into("<IIIIIII", hdr, 4, 124, 0x1007, h, w, len(out), 1, 1)
    struct.pack_into("<II4s", hdr, 76, 32, 4, b"DX10")
    struct.pack_into("<I", hdr, 108, 0x1000)
    struct.pack_into("<IIIII", hdr, 128, dxgi, 3, 0, 1, 0)

    dds_path = tmp_dir / (bntx_path.stem + ".dds")
    dds_path.write_bytes(hdr + out)
    img = bpy.data.images.load(str(dds_path), check_existing=False)
    arr = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    _DECODED_CACHE[bntx_path] = arr
    return arr


def resize_rgba(arr: np.ndarray, out_h: int, out_w: int) -> np.ndarray:
    in_h, in_w = arr.shape[:2]
    if in_h == out_h and in_w == out_w:
        return arr
    ys = np.linspace(0, in_h - 1, out_h, dtype=np.float32)
    xs = np.linspace(0, in_w - 1, out_w, dtype=np.float32)
    y0 = np.floor(ys).astype(np.int32)
    x0 = np.floor(xs).astype(np.int32)
    y1 = np.minimum(y0 + 1, in_h - 1)
    x1 = np.minimum(x0 + 1, in_w - 1)
    wy = (ys - y0)[:, None, None]
    wx = (xs - x0)[None, :, None]
    top = arr[y0][:, x0] * (1.0 - wx) + arr[y0][:, x1] * wx
    bot = arr[y1][:, x0] * (1.0 - wx) + arr[y1][:, x1] * wx
    return top * (1.0 - wy) + bot * wy


def srgb_to_linear(x: np.ndarray) -> np.ndarray:
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


def linear_to_srgb(x: np.ndarray) -> np.ndarray:
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1.0 / 2.4) - 0.055)


# ---------------------------------------------------------------------------
# TRMTR, TRSKL, TRMSH/TRMBF parsers
# ---------------------------------------------------------------------------

def parse_trmtr(path: Path) -> dict[str, dict]:
    buf = path.read_bytes()
    root = struct.unpack_from("<I", buf, 0)[0]
    vp, vn = fb_vec(buf, fb_field(buf, root, 1))
    mats: dict[str, dict] = {}
    for i in range(vn):
        mo = vp + i * 4 + struct.unpack_from("<I", buf, vp + i * 4)[0]
        mname = fb_str(buf, fb_field(buf, mo, 0))
        texs: dict[str, str] = {}
        f2 = fb_field(buf, mo, 2)
        if f2:
            tp, tn = fb_vec(buf, f2)
            for j in range(tn):
                to = tp + j * 4 + struct.unpack_from("<I", buf, tp + j * 4)[0]
                t0 = fb_str(buf, fb_field(buf, to, 0)) if fb_field(buf, to, 0) else ""
                t1 = fb_str(buf, fb_field(buf, to, 1)) if fb_field(buf, to, 1) else ""
                texs[t0] = t1
        cols: dict[str, tuple[float, float, float]] = {}
        f7 = fb_field(buf, mo, 7)
        if f7:
            cp, cn = fb_vec(buf, f7)
            for j in range(cn):
                co = cp + j * 4 + struct.unpack_from("<I", buf, cp + j * 4)[0]
                cn2 = fb_str(buf, fb_field(buf, co, 0)) if fb_field(buf, co, 0) else ""
                cv2 = fb_field(buf, co, 1)
                if cv2:
                    r, g, b, _ = struct.unpack_from("<ffff", buf, cv2)
                    cols[cn2] = (r, g, b)
        mats[mname] = {"texs": texs, "cols": cols}
    return mats


def composite_material_srgb(
    mat_info: dict,
    bntx_idx: dict[str, Path],
    tmp_dir: Path,
    out_w: int,
    out_h: int,
    is_eye: bool = False,
) -> np.ndarray:
    texs = mat_info["texs"]
    cols = mat_info["cols"]
    alb_name = texs.get("BaseColorMap", "")
    lym_name = texs.get("LayerMaskMap", "")
    ao_name = texs.get("OcclusionMap", "")

    if alb_name in bntx_idx:
        alb_raw = resize_rgba(decode_bntx_rgba(bntx_idx[alb_name], tmp_dir), out_h, out_w)
        alb_lin = srgb_to_linear(alb_raw[:, :, :3])
    else:
        alb_lin = np.ones((out_h, out_w, 3), dtype=np.float32)

    if lym_name in bntx_idx:
        lym = resize_rgba(decode_bntx_rgba(bntx_idx[lym_name], tmp_dir), out_h, out_w)
        r, g, b, a = lym[:, :, 0:1], lym[:, :, 1:2], lym[:, :, 2:3], lym[:, :, 3:4]
        w_sum = r + g + b + a
        w0 = np.clip(1.0 - w_sum, 0.0, 1.0)
        c0 = np.array(cols.get("BaseColor", (1.0, 1.0, 1.0)), dtype=np.float32)
        c1 = np.array(cols.get("BaseColorLayer1", (1.0, 1.0, 1.0)), dtype=np.float32)
        c2 = np.array(cols.get("BaseColorLayer2", (0.0, 0.0, 0.0)), dtype=np.float32)
        c3 = np.array(cols.get("BaseColorLayer3", (0.0, 0.0, 0.0)), dtype=np.float32)
        c4 = np.array(cols.get("BaseColorLayer4", (0.0, 0.0, 0.0)), dtype=np.float32)
        layer_lin = (w0 * c0 + r * c1 + g * c2 + b * c3 + a * c4) / np.maximum(w0 + w_sum, 1.0)
        rgb_lin = alb_lin * layer_lin
    else:
        c1 = np.array(cols.get("BaseColorLayer1", cols.get("BaseColor", (1.0, 1.0, 1.0))), dtype=np.float32)
        rgb_lin = alb_lin * c1

    if not is_eye and ao_name in bntx_idx and "white_msk" not in ao_name:
        ao = resize_rgba(decode_bntx_rgba(bntx_idx[ao_name], tmp_dir), out_h, out_w)[:, :, 0:1]
        rgb_lin = rgb_lin * (0.72 + 0.28 * np.clip(ao, 0.0, 1.0))

    rgb_srgb = linear_to_srgb(rgb_lin)
    out = np.ones((out_h, out_w, 4), dtype=np.float32)
    out[:, :, :3] = rgb_srgb
    return out


def composite_za_mat_with_mirror(
    mat_info: dict,
    bntx_idx: dict[str, Path],
    tmp_dir: Path,
    tile_w: int,
    tile_h: int,
    is_eye: bool = False,
) -> np.ndarray:
    uv_so = mat_info["cols"].get("UVScaleOffset", (1.0, 1.0, 0.0))
    if uv_so[0] > 1.5:
        half = composite_material_srgb(mat_info, bntx_idx, tmp_dir, tile_w // 2, tile_h, is_eye=is_eye)
        mirrored = np.concatenate([half, half[:, ::-1, :]], axis=1)
        return resize_rgba(mirrored, tile_h, tile_w)
    return composite_material_srgb(mat_info, bntx_idx, tmp_dir, tile_w, tile_h, is_eye=is_eye)


def save_tex_raw_and_png(srgb_rgba: np.ndarray, stem: str, out_dir: Path) -> tuple[int, int]:
    h, w = srgb_rgba.shape[:2]
    u8 = np.clip(np.round(srgb_rgba * 255.0), 0, 255).astype(np.uint8)
    (out_dir / f"{stem}.raw").write_bytes(u8.tobytes())
    img = bpy.data.images.new(name=f"tmp_{stem}", width=w, height=h, alpha=True)
    img.pixels.foreach_set(srgb_rgba.ravel())
    img.filepath_raw = str(out_dir / f"{stem}.png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    return w, h


def parse_trskl(path: Path) -> tuple[list[tuple[str, int, int]], dict[int, int]]:
    buf = path.read_bytes()
    root = struct.unpack_from("<I", buf, 0)[0]
    vp, vn = fb_vec(buf, fb_field(buf, root, 1))
    nodes: list[tuple[str, int, int]] = []
    rig_to_node: dict[int, int] = {}
    for i in range(vn):
        no = vp + i * 4 + struct.unpack_from("<I", buf, vp + i * 4)[0]
        name = fb_str(buf, fb_field(buf, no, 0)) if fb_field(buf, no, 0) else ""
        parent = struct.unpack_from("<i", buf, fb_field(buf, no, 4))[0] if fb_field(buf, no, 4) else -1
        rig = struct.unpack_from("<i", buf, fb_field(buf, no, 5))[0] if fb_field(buf, no, 5) else -1
        nodes.append((name, parent, rig))
        if rig >= 0:
            rig_to_node[rig] = i
    return nodes, rig_to_node


def build_universal_bone_remapper(
    van_bones: list[str],
    nodes: list[tuple[str, int, int]],
    rig_to_node: dict[int, int],
    lock_arms_to_torso: bool = False,
) -> tuple[dict[int, int], int]:
    import re
    van_idx = {b: i for i, b in enumerate(van_bones)}
    van_lower = {b.lower(): i for i, b in enumerate(van_bones)}

    def first_match(*cands: str) -> int | None:
        for c in cands:
            if c in van_idx:
                return van_idx[c]
            cl = c.lower()
            if cl in van_lower:
                return van_lower[cl]
        return None

    fallback = first_match("Spine1", "Spine", "Waist", "Hips", "Hip", "Origin")
    if fallback is None:
        fallback = 0
    torso_anchor = first_match("Spine2", "Spine1", "Chest", "Spine", "Waist", "Hips")
    if torso_anchor is None:
        torso_anchor = fallback

    semantic_candidates: dict[str, tuple[str, ...]] = {
        "waist": ("Hips", "Waist", "Hip", "Pelvis", "Spine1"),
        "spine_01": ("Spine1", "Spine", "Waist", "Hips"),
        "spine_02": ("Spine2", "Spine1", "Chest", "Spine", "Waist", "Hips"),
        "spine_03": ("Spine3", "Spine2", "Chest", "Spine1", "Waist", "Hips"),
        "neck_01": ("Neck1", "Neck", "Spine2"),
        "neck_02": ("Neck2", "Neck1", "Neck"),
        "neck_03": ("Neck3", "Neck2", "Neck1", "Head"),
        "head": ("Head", "Neck2", "Neck1", "Neck", "Spine2"),
        "jaw": ("Jaw", "Head"),
        "tongue": ("Tongue", "Jaw", "Head"),
        "left_shoulder": ("LShoulder", "LArm", "Spine2"),
        "right_shoulder": ("RShoulder", "RArm", "Spine2"),
        "left_arm_01": ("LArm", "LArm1", "LShoulder"),
        "left_arm_02": ("LForeArm", "LArm2", "LArm"),
        "left_hand": ("LHand", "LForeArm", "LArm"),
        "right_arm_01": ("RArm", "RArm1", "RShoulder"),
        "right_arm_02": ("RForeArm", "RArm2", "RArm"),
        "right_hand": ("RHand", "RForeArm", "RArm"),
        "left_thigh": ("LThigh", "LLeg1", "LLeg", "Hips", "Waist"),
        "left_leg_01": ("LThigh", "LLeg1", "LLeg", "Hips", "Waist"),
        "left_leg_02": ("LLeg", "LLeg2", "LThigh"),
        "left_leg_03": ("LFoot", "LLeg2", "LLeg"),
        "left_leg": ("LLeg", "LLeg2", "LThigh"),
        "left_foot": ("LFoot", "LLeg"),
        "left_toe": ("LToe", "LToeA1", "LFoot"),
        "right_thigh": ("RThigh", "RLeg1", "RLeg", "Hips", "Waist"),
        "right_leg_01": ("RThigh", "RLeg1", "RLeg", "Hips", "Waist"),
        "right_leg_02": ("RLeg", "RLeg2", "RThigh"),
        "right_leg_03": ("RFoot", "RLeg2", "RLeg"),
        "right_leg": ("RLeg", "RLeg2", "RThigh"),
        "right_foot": ("RFoot", "RLeg"),
        "right_toe": ("RToe", "RToeA1", "RFoot"),
    }

    def has_limb_or_tail_ancestor(nidx: int) -> bool:
        cur = nodes[nidx][1] if 0 <= nidx < len(nodes) else -1
        guard = 0
        while 0 <= cur < len(nodes) and guard < 20:
            pnm = nodes[cur][0].lower()
            if any(k in pnm for k in ("hand", "arm", "shoulder", "leg", "foot", "thigh", "tail", "wing")):
                return True
            if any(k in pnm for k in ("head", "neck", "spine", "waist", "hips")):
                return False
            cur = nodes[cur][1]
            guard += 1
        return False

    def resolve_name(nl: str, nidx: int) -> int | None:
        if lock_arms_to_torso and any(k in nl for k in ("shoulder", "arm", "hand", "finger", "index", "middle", "ring", "thumb", "pinky")) and not any(k in nl for k in ("foot", "toe", "leg", "thigh", "ankle")):
            return torso_anchor
        if nl in van_lower:
            return van_lower[nl]
        if nl in semantic_candidates:
            m = first_match(*semantic_candidates[nl])
            if m is not None:
                return m

        # Numbered tail chain: tail_01..tail_12 -> Tail{k} (clamping down to max available TailN)
        m_tail = re.match(r"^tail_(?:[a-z]_)?0*(\d+)$", nl)
        if m_tail:
            k = int(m_tail.group(1))
            cands = [f"Tail{j}" for j in range(k, 0, -1)] + ["Tail", "Hips", "Waist"]
            return first_match(*cands)

        # Context-aware feeler_*: if attached to a hand/arm/leg/tail/wing (e.g. Mega Heracross claw shell,
        # Charizard Y tail tip), return None so resolve_node inherits its anatomical parent bone!
        if "feeler" in nl and has_limb_or_tail_ancestor(nidx):
            return None

        side = "L" if nl.startswith("left_") else ("R" if nl.startswith("right_") else "")
        if side:
            rest = nl.split("_", 1)[1]
            # 1) Numbered wing chain BEFORE arm (BDSP Charizard wings are LFeeler1..6!)
            if "wing" in rest:
                m_num = re.search(r"0*(\d+)$", rest)
                k = int(m_num.group(1)) if m_num else 1
                cands = []
                for j in range(k, 0, -1):
                    cands.extend([f"{side}Wing{j}", f"{side}Feeler{j}", f"{side}Feather{j}"])
                cands.extend([f"{side}Wing", f"{side}FeelerA1", f"{side}Arm", f"{side}Shoulder", "Spine2"])
                return first_match(*cands)
            # 2) Foot / Toe STRICTLY BEFORE Finger/Index/Middle/Ring (fixes left_foot_index -> LToe/LFoot!)
            if "foot" in rest or "toe" in rest or "ankle" in rest:
                if any(k in rest for k in ("toe", "index", "middle", "ring", "thumb", "pinky")):
                    return first_match(f"{side}Toe", f"{side}ToeA1", f"{side}Foot", f"{side}Leg")
                return first_match(f"{side}Foot", f"{side}Leg")
            # 3) Hand fingers (only when NOT a foot bone)
            if any(k in rest for k in ("finger", "index", "middle", "ring", "thumb", "pinky")):
                return first_match(f"{side}FingerA1", f"{side}FingerA", f"{side}Hand", f"{side}ForeArm")
            # 4) Thigh vs Shin (leg_01 -> LThigh, leg_02 -> LLeg)
            if "leg" in rest or "knee" in rest or "thigh" in rest:
                if ("thigh" in rest or re.search(r"(^|_)0*1($|_)", rest)) and "knee" not in rest:
                    return first_match(f"{side}Thigh", f"{side}Leg1", f"{side}Leg")
                return first_match(f"{side}Leg", f"{side}Leg2", f"{side}Thigh")
            # 5) Upper arm vs Forearm
            if "arm" in rest:
                if re.search(r"(^|_)0*2($|_)", rest):
                    return first_match(f"{side}ForeArm", f"{side}Arm2", f"{side}Arm")
                return first_match(f"{side}Arm", f"{side}Arm1", f"{side}Shoulder")
            if "hand" in rest:
                return first_match(f"{side}Hand", f"{side}ForeArm", f"{side}Arm")
            if "ear" in rest or "horn" in rest:
                return first_match(f"{side}Ear1", f"{side}Ear", f"{side}Feeler1", f"{side}FeelerA1", "Head")
            if "hair" in rest:
                return first_match(f"{side}Hair1", f"{side}Hair", "Hair1", "Hair", "Head")
            if "feeler" in rest:
                m_num = re.search(r"0*(\d+)$", rest)
                k = int(m_num.group(1)) if m_num else 1
                cands = []
                for j in range(k, 0, -1):
                    cands.extend([f"{side}FeelerA{j}", f"{side}Feeler{j}", f"{side}FeelerB{j}"])
                cands.extend([f"{side}Feeler1", f"{side}FeelerA1", "Head"])
                return first_match(*cands)
        if "eyelid" in nl or "eye" in nl or "teeth" in nl or "mouth" in nl or "beak" in nl:
            return first_match("Head", "Neck2", "Neck1", "Spine2")
        if "jaw" in nl:
            return first_match("Jaw", "Head")
        if "tail" in nl:
            return first_match("Tail1", "Tail", "Hips", "Waist")
        return None

    def resolve_node(nidx: int) -> int:
        cur = nidx
        guard = 0
        while 0 <= cur < len(nodes) and guard < 40:
            name, parent, _ = nodes[cur]
            hit = resolve_name(name.lower(), cur)
            if hit is not None:
                return hit
            cur = parent
            guard += 1
        return fallback

    return {rig: resolve_node(nidx) for rig, nidx in rig_to_node.items()}, fallback


def parse_trmsh_and_trmbf(trmsh_path: Path, trmbf_path: Path) -> list[dict]:
    msh = trmsh_path.read_bytes()
    mbf = trmbf_path.read_bytes()

    root_s = struct.unpack_from("<I", msh, 0)[0]
    sp, sn = fb_vec(msh, fb_field(msh, root_s, 1))
    root_b = struct.unpack_from("<I", mbf, 0)[0]
    bp, bn = fb_vec(mbf, fb_field(mbf, root_b, 1))
    assert sn == bn, f"TRMSH shapes ({sn}) != TRMBF buffers ({bn})"

    shapes = []
    for si in range(sn):
        so = sp + si * 4 + struct.unpack_from("<I", msh, sp + si * 4)[0]
        sname = fb_str(msh, fb_field(msh, so, 0))
        ap, _ = fb_vec(msh, fb_field(msh, so, 3))
        ao = ap + struct.unpack_from("<I", msh, ap)[0]
        atp, atn = fb_vec(msh, fb_field(msh, ao, 0))
        szp, _ = fb_vec(msh, fb_field(msh, ao, 1))
        szo = szp + struct.unpack_from("<I", msh, szp)[0]
        stride = struct.unpack_from("<I", msh, fb_field(msh, szo, 0))[0]

        attr_offsets: dict[int, int] = {}
        attr_types: dict[int, int] = {}
        for k in range(atn):
            ato = atp + k * 4 + struct.unpack_from("<I", msh, atp + k * 4)[0]
            attr = struct.unpack_from("<I", msh, fb_field(msh, ato, 1))[0] if fb_field(msh, ato, 1) else 0
            atype = struct.unpack_from("<I", msh, fb_field(msh, ato, 3))[0] if fb_field(msh, ato, 3) else 0
            pos = struct.unpack_from("<I", msh, fb_field(msh, ato, 4))[0] if fb_field(msh, ato, 4) else 0
            if attr not in attr_offsets:
                attr_offsets[attr] = pos
                attr_types[attr] = atype

        mp, mn = fb_vec(msh, fb_field(msh, so, 4))
        mats = []
        for k in range(mn):
            mo = mp + k * 4 + struct.unpack_from("<I", msh, mp + k * 4)[0]
            pc = struct.unpack_from("<I", msh, fb_field(msh, mo, 0))[0] if fb_field(msh, mo, 0) else 0
            po = struct.unpack_from("<I", msh, fb_field(msh, mo, 1))[0] if fb_field(msh, mo, 1) else 0
            mname = fb_str(msh, fb_field(msh, mo, 3)) if fb_field(msh, mo, 3) else ""
            mats.append((mname, po, pc))

        bo = bp + si * 4 + struct.unpack_from("<I", mbf, bp + si * 4)[0]
        ip, _ = fb_vec(mbf, fb_field(mbf, bo, 0))
        vp, _ = fb_vec(mbf, fb_field(mbf, bo, 1))
        v0 = vp + struct.unpack_from("<I", mbf, vp)[0]
        dp, dn = fb_vec(mbf, fb_field(mbf, v0, 0))
        i0 = ip + struct.unpack_from("<I", mbf, ip)[0]
        idp, idn = fb_vec(mbf, fb_field(mbf, i0, 0))

        vcount = dn // stride
        pos_off = attr_offsets[1]
        nrm_off = attr_offsets.get(2, 12)
        uv_off = attr_offsets.get(6, 28)
        bi_off = attr_offsets.get(7, 36)
        bw_off = attr_offsets.get(8, 40)

        verts = np.zeros((vcount, 3), dtype=np.float32)
        norms = np.zeros((vcount, 3), dtype=np.float32)
        uvs = np.zeros((vcount, 2), dtype=np.float32)
        b_idx = np.zeros((vcount, 4), dtype=np.int32)
        b_w = np.zeros((vcount, 4), dtype=np.float32)

        for v in range(vcount):
            base = dp + v * stride
            verts[v] = struct.unpack_from("<fff", mbf, base + pos_off)
            nx, ny, nz = np.frombuffer(mbf, dtype="<f2", count=3, offset=base + nrm_off).astype(np.float32)
            nlen = math.sqrt(nx * nx + ny * ny + nz * nz)
            norms[v] = (nx / nlen, ny / nlen, nz / nlen) if nlen > 1e-6 else (0.0, 1.0, 0.0)
            uvs[v] = struct.unpack_from("<ff", mbf, base + uv_off)
            b_idx[v] = struct.unpack_from("<BBBB", mbf, base + bi_off)
            w0, w1, w2, w3 = struct.unpack_from("<HHHH", mbf, base + bw_off)
            b_w[v] = (w0 / 65535.0, w1 / 65535.0, w2 / 65535.0, w3 / 65535.0)

        indices = np.frombuffer(mbf, dtype="<u2", count=idn // 2, offset=idp).astype(np.int32)
        shapes.append({
            "name": sname,
            "verts": verts,
            "norms": norms,
            "uvs": uvs,
            "b_idx": b_idx,
            "b_w": b_w,
            "indices": indices,
            "mats": mats,
        })
    return shapes


def remap_vertex_weights(
    b_idx: np.ndarray,
    b_w: np.ndarray,
    rig_map: dict[int, int],
    fallback: int,
) -> tuple[list[list[int]], list[list[float]]]:
    out_i = []
    out_w = []
    for v in range(len(b_idx)):
        merged: dict[int, float] = {}
        for k in range(4):
            wk = float(b_w[v, k])
            if wk <= 1e-6:
                continue
            target = rig_map.get(int(b_idx[v, k]), fallback)
            merged[target] = merged.get(target, 0.0) + wk
        if not merged:
            merged[fallback] = 1.0
        items = sorted(merged.items(), key=lambda x: -x[1])[:4]
        tot = sum(w for _, w in items)
        iv = [0, 0, 0, 0]
        wv = [0.0, 0.0, 0.0, 0.0]
        for k, (bi, bw) in enumerate(items):
            iv[k] = int(bi)
            wv[k] = float(bw / tot)
        out_i.append(iv)
        out_w.append(wv)
    return out_i, out_w


# ---------------------------------------------------------------------------
# Dynamic Material Slot & Atlas Planner
# ---------------------------------------------------------------------------

def plan_material_slots(
    za_mat_names: list[str],
    van_materials: list[dict],
) -> tuple[dict[str, dict], dict[int, list[str]], dict[int, float]]:
    """
    Maps each Z-A material to a unique vanilla BDSP material slot (using grid atlasing
    automatically if Z-A has more materials than unique vanilla BDSP material slots).

    Returns:
      za_to_slot: { za_mat: { "slot": int, "u_scale": float, "u_off": float, "v_scale": float, "v_off": float, "van_scale_x": float } }
      slot_to_zamats: { slot: [za_mat, ...] }
      slot_scale_x: { slot: van_scale_x }
    """
    # Deduplicate vanilla slots by matPid so two slots sharing one Material don't fight
    unique_slots: list[dict] = []
    seen_pids: set[int] = set()
    slot_scale_x: dict[int, float] = {}
    for m in van_materials:
        slot_scale_x[m["slot"]] = float(m["scale"][0]) if m["scale"][0] > 0.1 else 1.0
        pid = m.get("pid", m["slot"])
        if pid not in seen_pids:
            seen_pids.add(pid)
            unique_slots.append(m)

    # Filter out VFX slash/aura quads (eff_*)
    active_za = [m for m in za_mat_names if not m.lower().startswith("eff_")]

    # CRITICAL: BDSP battle animations keyframe vertical UV offsets (+0.25/+0.50/+0.75) on
    # Eye/LEye/REye/AEye/BEye/Mouth/Iris material slots for vanilla 4-frame blink/mouth sheets.
    # Legends: Z-A eye/mouth textures are single-frame, so mapping them into BDSP's UV-animated
    # facial slots causes eyes to slide up and down randomly (e.g., Mega Dragonite). Route ALL
    # Z-A materials strictly into static Body* material slots, leaving UV-animated slots empty.
    def _is_uv_anim_or_special(mat_name: str) -> bool:
        part = mat_name.split("-")[-1].lower()
        return (
            part.startswith(("eye", "leye", "reye", "aeye", "beye", "mouth", "iris", "bug"))
            or part.endswith(("eye", "mouth", "iris"))
        )

    static_slots = [m for m in unique_slots if m["name"].split("-")[-1].lower().startswith("body")]
    if not static_slots:
        static_slots = [m for m in unique_slots if not _is_uv_anim_or_special(m["name"])]
    if not static_slots:
        static_slots = unique_slots

    slot_to_zamats: dict[int, list[str]] = {}

    # Assign all active Z-A materials (including eyes & mouths) round-robin across static slots
    for idx, zm in enumerate(active_za):
        target = static_slots[idx % len(static_slots)]
        slot_to_zamats.setdefault(target["slot"], []).append(zm)

    # Compute UV tile transforms for each slot
    za_to_slot: dict[str, dict] = {}
    for slot, zmats in slot_to_zamats.items():
        n = len(zmats)
        sx_van = slot_scale_x.get(slot, 1.0)
        if n == 1:
            za_to_slot[zmats[0]] = {
                "slot": slot, "u_scale": 1.0, "u_off": 0.0, "v_scale": 1.0, "v_off": 0.0,
                "van_scale_x": sx_van, "grid": (1, 1), "cell": (0, 0),
            }
        else:
            cols = 2 if n <= 4 else 3
            rows = math.ceil(n / cols)
            pad_u = 0.008 / cols
            pad_v = 0.008 / rows
            for k, zm in enumerate(zmats):
                c = k % cols
                r = k // cols
                za_to_slot[zm] = {
                    "slot": slot,
                    "u_scale": (1.0 / cols) - 2.0 * pad_u,
                    "u_off": (c / cols) + pad_u,
                    "v_scale": (1.0 / rows) - 2.0 * pad_v,
                    "v_off": (r / rows) + pad_v,
                    "van_scale_x": sx_van,
                    "grid": (cols, rows),
                    "cell": (c, r),
                }

    return za_to_slot, slot_to_zamats, slot_scale_x


# ---------------------------------------------------------------------------
# Cycles 3-Angle Visual QA Renderer
# ---------------------------------------------------------------------------

def render_textured_qa(
    form_id: str,
    mesh_json: Path,
    tex_dir: Path,
    slot_scale_x: dict[int, float],
    out_png: Path,
) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 480
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.14, 0.16, 0.21, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.85

    data = json.loads(mesh_json.read_text())
    verts = [(v[0], -v[2], v[1]) for v in data["verts"]]
    uvs = data["uvs"]
    subs = data["submeshes"]

    faces = []
    face_mats = []
    for si, idx_list in enumerate(subs):
        for t in range(0, len(idx_list), 3):
            a, b, c = idx_list[t], idx_list[t + 1], idx_list[t + 2]
            if a != b and b != c and a != c:
                faces.append((a, c, b))
                face_mats.append(si)

    me = bpy.data.meshes.new(form_id)
    me.from_pydata(verts, [], faces)
    me.update()

    uv_layer = me.uv_layers.new(name="UVMap")
    for pi, poly in enumerate(me.polygons):
        poly.use_smooth = True
        si = face_mats[pi]
        sx_van = slot_scale_x.get(si, 1.0)
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            uv_layer.data[li].uv = (uvs[vi][0] * sx_van, uvs[vi][1])

    for si in range(len(subs)):
        mat = bpy.data.materials.new(name=f"Sub{si}")
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        bsdf.inputs["Roughness"].default_value = 0.60
        png_path = tex_dir / f"Sub{si}_col.png"
        if png_path.exists():
            tex_node = nt.nodes.new("ShaderNodeTexImage")
            tex_node.image = bpy.data.images.load(str(png_path))
            nt.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
        me.materials.append(mat)

    for pi, poly in enumerate(me.polygons):
        poly.material_index = face_mats[pi]

    base_obj = bpy.data.objects.new(form_id, me)
    bpy.context.collection.objects.link(base_obj)

    bbox = [Vector(c) for c in base_obj.bound_box]
    min_z = min(p.z for p in bbox)
    max_z = max(p.z for p in bbox)
    span_x = max(p.x for p in bbox) - min(p.x for p in bbox)
    span_y = max(p.y for p in bbox) - min(p.y for p in bbox)
    height = max(max_z - min_z, 0.5)
    radius = max(span_x, span_y, height, 0.9)
    spacing = max(radius * 0.95, 1.35)

    base_obj.location = (0.0, 0.0, 0.0)
    left_obj = base_obj.copy()
    left_obj.data = me
    left_obj.location = (-spacing, 0.0, 0.0)
    left_obj.rotation_euler = (0.0, 0.0, math.radians(-38.0))
    bpy.context.collection.objects.link(left_obj)

    right_obj = base_obj.copy()
    right_obj.data = me
    right_obj.location = (spacing, 0.0, 0.0)
    right_obj.rotation_euler = (0.0, 0.0, math.radians(145.0))
    bpy.context.collection.objects.link(right_obj)

    for name, loc, energy in [
        ("Key", (radius * 2.2, -radius * 3.0, radius * 2.8), 320.0 * radius * radius),
        ("Fill", (-radius * 2.2, -radius * 2.4, radius * 1.8), 160.0 * radius * radius),
        ("Rim", (0.0, radius * 2.8, radius * 2.2), 180.0 * radius * radius),
    ]:
        ldata = bpy.data.lights.new(name, type="POINT")
        ldata.energy = energy
        lobj = bpy.data.objects.new(name, ldata)
        bpy.context.collection.objects.link(lobj)
        lobj.location = loc

    bpy.ops.mesh.primitive_plane_add(size=max(spacing * 6.0, 12.0), location=(0.0, 0.0, min(0.0, min_z)))
    ground = bpy.context.active_object
    gmat = bpy.data.materials.new(name="Ground")
    gmat.use_nodes = True
    gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.18, 0.20, 0.24, 1.0)
    ground.data.materials.append(gmat)

    cam_data = bpy.data.cameras.new("Cam")
    cam_obj = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    cam_dist = max(spacing * 3.15, height * 3.2)
    mid_z = 0.5 * (min_z + max_z)
    cam_obj.location = (0.0, -cam_dist, mid_z + height * 0.22)
    direction = Vector((0.0, 0.0, mid_z)) - cam_obj.location
    cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = 45.0

    scene.render.filepath = str(out_png)
    bpy.ops.render.render(write_still=True)
    print(f"  [QA RENDER] Saved {out_png}")


# ---------------------------------------------------------------------------
# Main Form Builder
# ---------------------------------------------------------------------------

SKIP_SHAPES_BY_PACK: dict[str, set[str]] = {
    # Mega Charizard Y: uses native BDSP animated FireCore/FireMask tail flame
    "pm0006_52_00": {"pm0006_52_00_fire_mesh_shape"},
    # Mega Blastoise: keep extended mega cannons, skip retracted closed covers
    "pm0009_51_00": {
        "pm0009_51_00_closed_mesh_shape",
        "pm0009_51_00_left_armclosed_mesh_shape",
        "pm0009_51_00_right_armclosed_mesh_shape",
    },
    # Mega Raichu X: keep primary tail_a, skip swappable tail_b/tail_c duplicates
    "pm0026_51_00": {"pm0026_51_00_tail_b_mesh_shape", "pm0026_51_00_tail_c_mesh_shape"},
    # Mega Raichu Y: keep primary rip_a, skip swappable rip_b/rip_c duplicates
    "pm0026_52_00": {"pm0026_52_00_rip_b_mesh_shape", "pm0026_52_00_rip_c_mesh_shape"},
    # Mega Victreebel: keep default _a face, skip alternate _b face
    "pm0071_51_00": {
        "pm0071_51_00_mouth_b_mesh_shape",
        "pm0071_51_00_eye_b_mesh_shape",
        "pm0071_51_00_teeth_b_mesh_shape",
    },
    # Mega Slowbro: keep open eye_a + Shellder eye_c + arms, skip closed eye_b/eyelid/futa
    "pm0080_51_00": {
        "pm0080_51_00_eye_b_mesh_shape",
        "pm0080_51_00_eyelid_mesh_shape",
        "pm0080_51_00_futa_mesh_shape",
    },
    # Mega Kangaskhan: skip closed baby eyelidb overlay
    "pm0115_51_00": {"pm0115_51_00_eyelidb_mesh_shape"},
    # Mega Feraligatr: keep primary arm_a, skip swappable arm_b & closed eyelid
    "pm0160_51_00": {"pm0160_51_00_arm_b_mesh_shape", "pm0160_51_00_eyelid_mesh_shape"},
    # Mega Steelix: skip closed eyelid overlay
    "pm0208_51_00": {"pm0208_51_00_eyelid_mesh_shape"},
    # Mega Skarmory: keep primary hair_a crest, skip swappable hair_b duplicate
    "pm0227_51_00": {"pm0227_51_00_hair_b_mesh_shape"},
    # Mega Sharpedo: keep primary eye_a, skip swappable eye_b duplicate
    "pm0319_51_00": {"pm0319_51_00_eye_b_mesh_shape"},
    # Phase 4 (24 BDSP-unlocked forms): skip closed eyelids & folded/alternate overlays
    "pm0018_51_00": {
        "pm0018_51_00_l_wing_b_mesh_shape",
        "pm0018_51_00_r_wing_b_mesh_shape",
        "pm0018_51_00_leg_b_mesh_shape",
    },
    "pm0127_51_00": {
        "pm0127_51_00_upper_eyelid_mesh_shape",
        "pm0127_51_00_lower_eyelid_mesh_shape",
    },
    "pm0260_51_00": {"pm0260_51_00_lower_eyelid_mesh_shape"},
    "pm0308_51_00": {"pm0308_51_00_eye_b_mesh_shape"},
    "pm0354_51_00": {"pm0354_51_00_eyelid_mesh_shape", "pm0354_51_00_eye_b_mesh_shape"},
    "pm0362_51_00": {"pm0362_51_00_eyelid_mesh_shape"},
    "pm0376_51_00": {"pm0376_51_00_eye_b_mesh_shape"},
    "pm0428_51_00": {"pm0428_51_00_eyelid_mesh_shape"},
    "pm0485_51_00": {"pm0485_51_00_eyelid_mesh_shape"},
    # Regression-audit fixes: skip overlapping closed wings (Staraptor) and eyelid overlays (Chandelure)
    "pm0398_51_00": {
        "pm0398_51_00_left_closed_wing_mesh_shape",
        "pm0398_51_00_right_closed_wing_mesh_shape",
    },
    "pm0609_51_00": {
        "pm0609_51_00_eyelid_a_mesh_shape",
        "pm0609_51_00_eyelid_b_mesh_shape",
        "pm0609_51_00_eyelid_c_mesh_shape",
        "pm0609_51_00_eyelid_d_mesh_shape",
    },
}


# Megas whose Z-A arms are already sculpted forward/downward in battle pose while their base BDSP species
# is T-posed (causing WaitA01 to double-rotate & hyper-extend arms). Bind arms to Spine2 to keep exact proportions.
LOCK_ARMS_TO_TORSO_PACKS: set[str] = {
    "pm0260_51_00",  # Mega Swampert
    "pm0460_51_00",  # Mega Abomasnow
    "pm0094_51_00",  # Mega Gengar
    "pm0491_51_00",  # Mega Darkrai
}

MAX_DIM_CLAMP_BY_PACK: dict[str, float] = {
    "pm0384_51_00": 11.5,  # Mega Rayquaza (prevents 128m stretched-out blowup)
    "pm0208_51_00": 7.5,   # Mega Steelix
}


def build_variant(spec: MegaFormSpec, is_shiny: bool, files: dict[str, Path]) -> None:
    species = spec.species_prefix
    form_id = spec.shiny_bundle if is_shiny else spec.normal_bundle
    pack_dir = ZA_ROOT / spec.za_pack
    out_dir = OUT_ROOT / form_id
    tmp_dir = out_dir / "dds_tmp"
    out_dir.mkdir(parents=True, exist_ok=True)
    tmp_dir.mkdir(parents=True, exist_ok=True)
    QA_DIR.mkdir(parents=True, exist_ok=True)

    print(f"\n=== Building {spec.name} ({form_id}, {'SHINY' if is_shiny else 'NORMAL'}) ===")
    van_data = json.loads((OUT_ROOT / f"van_{species}.json").read_text())
    van_bones = [b["name"] for b in van_data["bones"]]
    van_materials = van_data["materials"]
    van_min_y = float(van_data["minY"])
    van_h = float(van_data["height"])
    if species == "pm0362" and van_h < 0.5:
        van_h = 1.50
        van_min_y = 0.15

    nodes, rig_to_node = parse_trskl(files["trskl"])
    rig_map, fallback_bone = build_universal_bone_remapper(
        van_bones, nodes, rig_to_node, lock_arms_to_torso=(spec.za_pack in LOCK_ARMS_TO_TORSO_PACKS)
    )

    skip_shapes = SKIP_SHAPES_BY_PACK.get(spec.za_pack, set())
    shapes = [s for s in parse_trmsh_and_trmbf(files["trmsh"], files["trmbf"]) if s["name"] not in skip_shapes]
    trmtr_path = files["rare_trmtr"] if is_shiny else files["norm_trmtr"]
    trmtr = parse_trmtr(trmtr_path)
    bntx_idx = index_bntx_dir(pack_dir)

    # Collect actually referenced material names in TRMSH
    used_mats: list[str] = []
    for sh in shapes:
        for mname, _, pc in sh["mats"]:
            if pc > 0 and mname in trmtr and mname not in used_mats:
                used_mats.append(mname)

    za_to_slot, slot_to_zamats, slot_scale_x = plan_material_slots(used_mats, van_materials)

    # Compute skeleton-anchored core-body scale, Y grounding, and Z alignment (prevents tall horns/wings
    # from shrinking body proportions like Mega Heracross 0.66x, and aligns spine/hip pivots in Z).
    body_ys = []
    bone_acc: dict[int, list[float]] = {}
    for sh in shapes:
        if any(m[0].lower().startswith("eff_") for m in sh["mats"]):
            continue
        if spec.za_pack == "pm0071_51_00" and "leaf" in sh["name"].lower():
            continue
        body_ys.extend(sh["verts"][:, 1].tolist())
        v_arr = sh["verts"]
        bi_arr = sh["b_idx"]
        bw_arr = sh["b_w"]
        for vi in range(len(v_arr)):
            vy_v = float(v_arr[vi, 1])
            vz_v = float(v_arr[vi, 2])
            for k in range(4):
                wk = float(bw_arr[vi, k])
                if wk <= 1e-4:
                    continue
                tb = rig_map.get(int(bi_arr[vi, k]), fallback_bone)
                acc = bone_acc.get(tb)
                if acc is None:
                    bone_acc[tb] = [vy_v * wk, vz_v * wk, wk]
                else:
                    acc[0] += vy_v * wk
                    acc[1] += vz_v * wk
                    acc[2] += wk

    za_min_y, za_max_y = min(body_ys), max(body_ys)
    za_h = max(za_max_y - za_min_y, 1e-3)
    scale_bbox = (van_h * spec.height_ratio) / za_h

    core_scale_names = {"hips", "spine", "spine1", "spine2", "head", "larm", "rarm", "lthigh", "rthigh"}
    core_z_names = {"hips", "spine", "spine1", "spine2", "head", "lthigh", "rthigh"}
    van_bone_pos = {b["idx"]: (b["name"].lower(), b["pos"]) for b in van_data["bones"]}

    implied_scales = []
    for bi, (bnl, bpos) in van_bone_pos.items():
        if bnl not in core_scale_names or bi not in bone_acc:
            continue
        acc = bone_acc[bi]
        if acc[2] < 15.0:
            continue
        za_cy = acc[0] / acc[2]
        van_span = float(bpos[1]) - van_min_y
        za_span = za_cy - za_min_y
        if van_span > 0.15 and za_span > 0.15:
            implied_scales.append((van_span / za_span) * spec.height_ratio)

    if len(implied_scales) >= 2:
        implied_scales.sort()
        scale_core = implied_scales[len(implied_scales) // 2]
        scale = max(0.85 * scale_bbox, min(1.20 * scale_bbox, scale_core))
    else:
        scale = scale_bbox

    if spec.za_pack in MAX_DIM_CLAMP_BY_PACK:
        all_body_v = np.concatenate([sh["verts"] for sh in shapes if not any(m[0].lower().startswith("eff_") for m in sh["mats"])], axis=0)
        za_max_dim = float(np.max(np.ptp(all_body_v, axis=0)))
        if za_max_dim * scale > MAX_DIM_CLAMP_BY_PACK[spec.za_pack]:
            scale = MAX_DIM_CLAMP_BY_PACK[spec.za_pack] / max(za_max_dim, 1e-3)

    ty = van_min_y - za_min_y * scale

    implied_tzs = []
    for bi, (bnl, bpos) in van_bone_pos.items():
        if bnl not in core_z_names or bi not in bone_acc:
            continue
        acc = bone_acc[bi]
        if acc[2] < 15.0:
            continue
        za_cz = acc[1] / acc[2]
        implied_tzs.append(float(bpos[2]) - za_cz * scale)
    if implied_tzs:
        implied_tzs.sort()
        raw_tz = implied_tzs[len(implied_tzs) // 2]
        tz = max(-0.45 * van_h, min(0.45 * van_h, raw_tz))
    else:
        tz = 0.0

    print(f"  Scale={scale:.4f} (bbox={scale_bbox:.4f}, ZA h={za_h:.3f}m -> {za_h*scale:.3f}m, minY={van_min_y:.3f}, tz={tz:+.3f}), slots={len(van_materials)}, used_mats={len(used_mats)}")

    # Bake textures per populated vanilla slot (Sub{slot}_col)
    textures_meta: dict[str, list[int]] = {}
    for slot, zmats in sorted(slot_to_zamats.items()):
        if len(zmats) == 1:
            zm = zmats[0]
            is_eye = "eye" in zm.lower()
            sz = 256 if is_eye else 512
            img = composite_za_mat_with_mirror(trmtr[zm], bntx_idx, tmp_dir, sz, sz, is_eye=is_eye)
        else:
            cols, rows = za_to_slot[zmats[0]]["grid"]
            atlas_w = 1024
            atlas_h = 512 if rows == 1 else 1024
            tile_w = atlas_w // cols
            tile_h = atlas_h // rows
            img = np.zeros((atlas_h, atlas_w, 4), dtype=np.float32)
            img[:, :, 3] = 1.0
            for zm in zmats:
                c, r = za_to_slot[zm]["cell"]
                is_eye = "eye" in zm.lower()
                tile = composite_za_mat_with_mirror(trmtr[zm], bntx_idx, tmp_dir, tile_w, tile_h, is_eye=is_eye)
                img[r * tile_h : (r + 1) * tile_h, c * tile_w : (c + 1) * tile_w, :] = tile
        stem = f"Sub{slot}_col"
        w, h = save_tex_raw_and_png(img, stem, out_dir)
        textures_meta[stem] = [w, h]

    (out_dir / "textures.json").write_text(json.dumps(textures_meta, indent=2))

    # Assemble unified BDSP mesh
    num_subs = len(van_materials)
    all_verts: list[list[float]] = []
    all_norms: list[list[float]] = []
    all_uvs: list[list[float]] = []
    all_w: list[list[float]] = []
    all_i: list[list[int]] = []
    submeshes: list[list[int]] = [[] for _ in range(num_subs)]

    for sh in shapes:
        for mname, poly_off, poly_cnt in sh["mats"]:
            if poly_cnt == 0 or mname not in za_to_slot:
                continue
            info = za_to_slot[mname]
            sub_idx = info["slot"]
            u_sc, u_of = info["u_scale"], info["u_off"]
            v_sc, v_of = info["v_scale"], info["v_off"]
            sx_van = info["van_scale_x"]

            slice_idx = sh["indices"][poly_off : poly_off + poly_cnt]
            is_victreebel_leaf = (spec.za_pack == "pm0071_51_00" and "leaf" in sh["name"].lower())
            if is_victreebel_leaf:
                kept = []
                for t in range(0, len(slice_idx), 3):
                    ia, ib, ic = int(slice_idx[t]), int(slice_idx[t + 1]), int(slice_idx[t + 2])
                    if min(sh["verts"][ia][1], sh["verts"][ib][1], sh["verts"][ic][1]) > 3.9:
                        kept.extend([ia, ib, ic])
                slice_idx = np.array(kept, dtype=np.int32)
                if len(slice_idx) == 0:
                    continue
            used = np.unique(slice_idx)
            local_to_global: dict[int, int] = {}
            remapped_i, remapped_w = remap_vertex_weights(sh["b_idx"][used], sh["b_w"][used], rig_map, fallback_bone)

            for loc_pos, orig_v in enumerate(used):
                v_za = sh["verts"][orig_v]
                n_za = sh["norms"][orig_v]
                uv_za = sh["uvs"][orig_v]

                vx = float(-v_za[0] * scale)
                vy = float(v_za[1] * scale + ty)
                vz = float(v_za[2] * scale + tz)
                nx = float(-n_za[0])
                ny = float(n_za[1])
                nz = float(n_za[2])

                u_raw = float(uv_za[0])
                v_raw = float(uv_za[1])
                u_mod = u_raw % 1.0
                if u_mod == 0.0 and u_raw > 0.5:
                    u_mod = 1.0
                v_mod = v_raw % 1.0
                if v_mod == 0.0 and v_raw > 0.5:
                    v_mod = 1.0

                u_bdsp = (u_mod * u_sc + u_of) / sx_van
                v_bdsp = v_mod * v_sc + v_of

                g_idx = len(all_verts)
                local_to_global[int(orig_v)] = g_idx
                all_verts.append([vx, vy, vz])
                all_norms.append([nx, ny, nz])
                all_uvs.append([u_bdsp, v_bdsp])
                all_w.append(remapped_w[loc_pos])
                all_i.append(remapped_i[loc_pos])

            is_victreebel_leaf = (spec.za_pack == "pm0071_51_00" and "leaf" in sh["name"].lower())
            if is_victreebel_leaf:
                # Reposition Mega Victreebel top leaf crown onto head and drop the 12m unposed straight vine stem
                for orig_v, g_idx in local_to_global.items():
                    if sh["verts"][orig_v][1] > 3.9:
                        all_verts[g_idx][1] -= 1.35 * scale
            s_out = submeshes[sub_idx]
            for t in range(0, len(slice_idx), 3):
                ia, ib, ic = int(slice_idx[t]), int(slice_idx[t + 1]), int(slice_idx[t + 2])
                if is_victreebel_leaf and min(sh["verts"][ia][1], sh["verts"][ib][1], sh["verts"][ic][1]) <= 3.9:
                    continue
                a = local_to_global[ia]
                b = local_to_global[ib]
                c = local_to_global[ic]
                s_out.extend([a, c, b])

    mesh_json = out_dir / f"{form_id}_mesh.json"
    mesh_json.write_text(json.dumps({
        "verts": all_verts,
        "normals": all_norms,
        "uvs": all_uvs,
        "weights": all_w,
        "indices": all_i,
        "submeshes": submeshes,
    }))
    tris_total = sum(len(s) // 3 for s in submeshes)
    print(f"  Wrote {mesh_json}: verts={len(all_verts)}, tris={tris_total}, submesh_tris={[len(s)//3 for s in submeshes]}")
    if not getattr(build_variant, "no_render", False):
        render_textured_qa(form_id, mesh_json, out_dir, slot_scale_x, QA_DIR / f"{form_id}_za_qa.png")


def main() -> None:
    argv = sys.argv
    raw_args = argv[argv.index("--") + 1 :] if "--" in argv else []
    no_render = "--no-render" in raw_args
    build_variant.no_render = no_render
    targets = [a for a in raw_args if a != "--no-render"]
    by_key = {f"pm{m.species:04d}_{m.form:02d}": m for m in MEGA_CATALOG}
    selected = [by_key[t] for t in targets] if targets else []
    if not selected:
        raise SystemExit("Pass target form keys after --, e.g. -- pm0398_01 pm0445_01 ...")

    for spec in selected:
        pack_dir = ZA_ROOT / spec.za_pack
        files = discover_za_pack(pack_dir)
        print(f"\n[DISCOVERED] {spec.za_pack}: trmsh={files['trmsh'].name}, trmbf={files['trmbf'].name}, trskl={files['trskl'].name}, norm={files['norm_trmtr'].name}, rare={files['rare_trmtr'].name}")
        build_variant(spec, False, files)
        build_variant(spec, True, files)

    print(f"\nALL {len(selected) * 2} VARIANTS BUILT!")


if __name__ == "__main__":
    main()
