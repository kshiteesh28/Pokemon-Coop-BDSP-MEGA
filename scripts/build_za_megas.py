#!/usr/bin/env python3
"""Convert official Pokémon Legends: Z-A Trinity models (TRMSH/TRMBF/TRSKL/TRMTR/BNTX)
into BDSP Switch-ready meshes & RGBA32 textures (Normal + Shiny) + 3-angle Cycles QA renders.

Run inside headless Blender 5.x (which natively decodes DX10 BC7/BC1/BC4 DDS):
  /tmp/blender_hfs/Blender/Blender.app/Contents/MacOS/Blender -b --python scripts/build_za_megas.py
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
    0x1D01: (80, 4, 4, 8),   # BC4_UNORM
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
    """Returns float32 (H, W, 4) array in Blender bottom-to-top row order."""
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
    # Bilinear resize in pure numpy
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
# TRMTR parser & Trinity LayerMask compositor
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
    """Composites Trinity BaseColorMap + LayerMaskMap + BaseColorLayer1..4 + AO -> sRGB float32 (H, W, 4)."""
    texs = mat_info["texs"]
    cols = mat_info["cols"]

    alb_name = texs.get("BaseColorMap", "")
    lym_name = texs.get("LayerMaskMap", "")
    ao_name = texs.get("OcclusionMap", "")

    if alb_name in bntx_idx:
        alb_raw = resize_rgba(decode_bntx_rgba(bntx_idx[alb_name], tmp_dir), out_h, out_w)
        # Blender decodes DXGI *_SRGB DDS directly to sRGB in image.pixels; convert to linear for math
        alb_lin = srgb_to_linear(alb_raw[:, :, :3])
    else:
        alb_lin = np.ones((out_h, out_w, 3), dtype=np.float32)

    if lym_name in bntx_idx:
        lym = resize_rgba(decode_bntx_rgba(bntx_idx[lym_name], tmp_dir), out_h, out_w)
        r = lym[:, :, 0:1]
        g = lym[:, :, 1:2]
        b = lym[:, :, 2:3]
        a = lym[:, :, 3:4]
        w_sum = r + g + b + a
        w0 = np.clip(1.0 - w_sum, 0.0, 1.0)
        c0 = np.array(cols.get("BaseColor", (1.0, 1.0, 1.0)), dtype=np.float32)
        c1 = np.array(cols.get("BaseColorLayer1", (1.0, 1.0, 1.0)), dtype=np.float32)
        c2 = np.array(cols.get("BaseColorLayer2", (0.0, 0.0, 0.0)), dtype=np.float32)
        c3 = np.array(cols.get("BaseColorLayer3", (0.0, 0.0, 0.0)), dtype=np.float32)
        c4 = np.array(cols.get("BaseColorLayer4", (0.0, 0.0, 0.0)), dtype=np.float32)

        layer_lin = w0 * c0 + r * c1 + g * c2 + b * c3 + a * c4
        # If total mask weight > 1, normalize cleanly
        norm = np.maximum(w0 + w_sum, 1.0)
        layer_lin = layer_lin / norm
        rgb_lin = alb_lin * layer_lin
    else:
        c1 = np.array(cols.get("BaseColorLayer1", cols.get("BaseColor", (1.0, 1.0, 1.0))), dtype=np.float32)
        rgb_lin = alb_lin * c1

    # Subtle ambient occlusion baking for crisp BDSP form definition
    if not is_eye and ao_name in bntx_idx and "white_msk" not in ao_name:
        ao = resize_rgba(decode_bntx_rgba(bntx_idx[ao_name], tmp_dir), out_h, out_w)[:, :, 0:1]
        rgb_lin = rgb_lin * (0.72 + 0.28 * np.clip(ao, 0.0, 1.0))

    rgb_srgb = linear_to_srgb(rgb_lin)
    out = np.ones((out_h, out_w, 4), dtype=np.float32)
    out[:, :, :3] = rgb_srgb
    return out


def save_tex_raw_and_png(srgb_rgba: np.ndarray, stem: str, out_dir: Path) -> tuple[int, int]:
    """Saves RGBA32 .raw (Unity bottom-to-top) and preview .png. Returns (W, H)."""
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


# ---------------------------------------------------------------------------
# TRSKL + TRMSH + TRMBF parser & bone remapping
# ---------------------------------------------------------------------------

def parse_trskl(path: Path) -> tuple[list[tuple[str, int, int]], dict[int, int]]:
    """Returns (nodes=[(name, parent_idx, rig_idx)], rig_to_node={rig_idx: node_idx})."""
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


def build_bone_remapper(species: str, van_bones: list[str], nodes: list[tuple[str, int, int]], rig_to_node: dict[int, int]) -> dict[int, int]:
    van_idx = {b: i for i, b in enumerate(van_bones)}

    if species == "pm0006":
        direct = {
            "waist": "Hips", "spine_01": "Spine1", "spine_02": "Spine2",
            "neck_01": "Neck1", "neck_02": "Neck2", "neck_03": "Neck3",
            "head": "Head", "jaw": "Jaw", "tongue": "Tongue",
            "left_shoulder": "LShoulder", "left_arm_01": "LArm", "left_arm_02": "LForeArm", "left_hand": "LHand",
            "left_index_01": "LFingerA1", "left_index_02": "LFingerA2",
            "left_middle_01": "LFingerB1", "left_middle_02": "LFingerB2",
            "left_ring_01": "LFingerC1", "left_ring_02": "LFingerC2",
            "right_shoulder": "RShoulder", "right_arm_01": "RArm", "right_arm_02": "RForeArm", "right_hand": "RHand",
            "right_index_01": "RFingerA1", "right_index_02": "RFingerA2",
            "right_middle_01": "RFingerB1", "right_middle_02": "RFingerB2",
            "right_ring_01": "RFingerC1", "right_ring_02": "RFingerC2",
            "left_feeler_b": "LShoulder", "right_feeler_b": "RShoulder",
            "left_wing_a_01": "LFeeler1", "left_wing_a_02": "LFeeler2", "left_wing_a_03": "LFeeler3", "left_wing_a_04": "LFeeler4",
            "left_wing_b": "LFeeler3", "left_wing_c": "LFeeler4", "left_wing_d": "LFeeler5", "left_wing_e": "LFeeler6",
            "right_wing_a_01": "RFeeler1", "right_wing_a_02": "RFeeler2", "right_wing_a_03": "RFeeler3", "right_wing_a_04": "RFeeler4",
            "right_wing_b": "RFeeler3", "right_wing_c": "RFeeler4", "right_wing_d": "RFeeler5", "right_wing_e": "RFeeler6",
            "center_wing": "Spine2",
            "tail_01": "Tail1", "tail_02": "Tail2", "tail_03": "Tail3", "tail_04": "Tail4",
            "tail_05": "Tail5", "tail_06": "Tail6", "tail_07": "Tail7",
            "left_thigh": "LThigh", "left_leg": "LLeg", "left_foot": "LFoot", "left_toe": "LToe",
            "right_thigh": "RThigh", "right_leg": "RLeg", "right_foot": "RFoot", "right_toe": "RToe",
            "left_feeler_a_01": "Head", "left_feeler_a_02": "Head", "left_feeler_a_03": "Head",
            "right_feeler_a_01": "Head", "right_feeler_a_02": "Head", "right_feeler_a_03": "Head",
        }
        fallback = van_idx["Spine1"]
    else:  # pm0359
        direct = {
            "waist": "Hips", "spine_01": "Spine1", "spine_02": "Spine2",
            "neck_01": "Neck1", "neck_02": "Neck2", "neck_03": "Neck2",
            "head": "Head", "horn": "Head",
            "left_hair_01": "Hair1", "left_hair_02": "Hair2", "left_hair_03": "Hair3", "left_hair_04": "Hair3",
            "right_hair_01": "Head", "right_hair_02": "Head", "right_hair": "Head",
            "left_shoulder": "LShoulder", "left_arm_01": "LArm", "left_arm_02": "LForeArm", "left_hand": "LHand",
            "left_index": "LFingerA", "left_middle": "LFingerB", "left_ring": "LFingerC",
            "right_shoulder": "RShoulder", "right_arm_01": "RArm", "right_arm_02": "RForeArm", "right_hand": "RHand",
            "right_index": "RFingerA", "right_middle": "RFingerB", "right_ring": "RFingerC",
            "tail_01": "Tail", "tail_02": "Tail", "tail_03": "Tail", "tail": "Tail",
            "left_thigh": "LThigh", "left_leg": "LLeg", "left_foot": "LFoot",
            "left_toe_a_01": "LToeA1", "left_toe_a_02": "LToeA2", "left_toe_b": "LToeB", "left_toe_c": "LToeC",
            "right_thigh": "RThigh", "right_leg": "RLeg", "right_foot": "RFoot",
            "right_toe_a_01": "RToeA1", "right_toe_a_02": "RToeA2", "right_toe_b": "RToeB", "right_toe_c": "RToeC",
            # Wing/feeler chains on Mega Absol & Mega Absol Z attach cleanly to upper back/chest
            "left_wing_a": "LFeelerB1", "left_wing_b": "LFeelerB2", "left_wing_c": "LFeelerB2", "left_wing_d": "LFeelerB2",
            "right_wing_01": "RFeelerB1", "right_wing_02": "RFeelerB2", "right_wing_03": "RFeelerB2", "right_wing_04": "RFeelerB2",
            "center_feeler_01": "FeelerC1", "center_feeler_02": "FeelerC2",
        }
        fallback = van_idx["Spine1"]

    def resolve_node(nidx: int) -> int:
        cur = nidx
        guard = 0
        while 0 <= cur < len(nodes) and guard < 40:
            name, parent, _ = nodes[cur]
            nl = name.lower()
            if nl in direct and direct[nl] in van_idx:
                return van_idx[direct[nl]]
            if "eyelid" in nl or "jaw" in nl or "teeth" in nl or "mouth" in nl:
                if species == "pm0006" and "jaw" in nl:
                    return van_idx["Jaw"]
                return van_idx["Head"]
            if "tail" in nl:
                return van_idx.get("Tail1", van_idx.get("Tail", fallback))
            if "left_wing" in nl:
                return van_idx.get("LFeeler1", van_idx.get("LFeelerB1", van_idx["Spine2"]))
            if "right_wing" in nl:
                return van_idx.get("RFeeler1", van_idx.get("RFeelerB1", van_idx["Spine2"]))
            cur = parent
            guard += 1
        return fallback

    return {rig: resolve_node(nidx) for rig, nidx in rig_to_node.items()}


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

        attr_offsets = {}
        for k in range(atn):
            ato = atp + k * 4 + struct.unpack_from("<I", msh, atp + k * 4)[0]
            attr = struct.unpack_from("<I", msh, fb_field(msh, ato, 1))[0] if fb_field(msh, ato, 1) else 0
            pos = struct.unpack_from("<I", msh, fb_field(msh, ato, 4))[0] if fb_field(msh, ato, 4) else 0
            if attr not in attr_offsets:
                attr_offsets[attr] = pos

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
            if nlen > 1e-6:
                norms[v] = (nx / nlen, ny / nlen, nz / nlen)
            else:
                norms[v] = (0.0, 1.0, 0.0)
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


# ---------------------------------------------------------------------------
# Form Builders
# ---------------------------------------------------------------------------

def remap_vertex_weights(b_idx: np.ndarray, b_w: np.ndarray, rig_map: dict[int, int], fallback: int) -> tuple[list[list[int]], list[list[float]]]:
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


def render_textured_qa(form_id: str, mesh_json: Path, tex_dir: Path, submesh_tex_names: list[str], out_png: Path) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 1350
    scene.render.resolution_y = 540
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.14, 0.16, 0.21, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.85

    data = json.loads(mesh_json.read_text())
    # Unity (x, y, z) -> Blender (x, -z, y)
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
    for poly in me.polygons:
        poly.use_smooth = True
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            # Simulate BDSP scale=(2, 1) on _Col0Tex!
            uv_layer.data[li].uv = (uvs[vi][0] * 2.0, uvs[vi][1])

    # Load materials
    loaded_mats = {}
    for si, tname in enumerate(submesh_tex_names):
        if tname not in loaded_mats:
            mat = bpy.data.materials.new(name=f"M_{tname}")
            mat.use_nodes = True
            nt = mat.node_tree
            bsdf = nt.nodes["Principled BSDF"]
            bsdf.inputs["Roughness"].default_value = 0.60
            png_path = tex_dir / f"{tname}.png"
            if png_path.exists():
                tex_node = nt.nodes.new("ShaderNodeTexImage")
                tex_node.image = bpy.data.images.load(str(png_path))
                nt.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
            loaded_mats[tname] = mat
        me.materials.append(loaded_mats[tname])

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

    # 3 views: Front-3/4 (left), Front (center), Back-3/4 (right)
    base_obj.location = (0.0, 0.0, 0.0)
    base_obj.rotation_euler = (0.0, 0.0, 0.0)

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

    bpy.ops.mesh.primitive_plane_add(size=max(spacing * 6.0, 12.0), location=(0.0, 0.0, 0.0))
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


def build_form(cfg: dict) -> None:
    form_id = cfg["form_id"]
    species = cfg["species"]
    pack_dir = ZA_ROOT / cfg["za_pack"]
    out_dir = OUT_ROOT / form_id
    tmp_dir = out_dir / "dds_tmp"
    out_dir.mkdir(parents=True, exist_ok=True)
    tmp_dir.mkdir(parents=True, exist_ok=True)
    QA_DIR.mkdir(parents=True, exist_ok=True)

    print(f"\n=== Building {form_id} from {cfg['za_pack']} ({'SHINY' if cfg['is_shiny'] else 'NORMAL'}) ===")

    van_data = json.loads(Path(cfg["van_json"]).read_text())
    van_bones = [b["name"] for b in van_data["bones"]]
    van_ys = [v[1] for v in van_data["verts"]]
    van_min_y, van_max_y = min(van_ys), max(van_ys)
    van_h = van_max_y - van_min_y

    nodes, rig_to_node = parse_trskl(pack_dir / cfg["trskl"])
    rig_map = build_bone_remapper(species, van_bones, nodes, rig_to_node)
    fallback_bone = van_bones.index("Spine1")

    shapes = parse_trmsh_and_trmbf(pack_dir / cfg["trmsh"], pack_dir / cfg["trmbf"])
    trmtr = parse_trmtr(pack_dir / cfg["trmtr"])
    bntx_idx = index_bntx_dir(pack_dir)

    # Compute uniform scale & grounding from non-fire body shapes
    body_ys = []
    for sh in shapes:
        if "fire_" in sh["name"]:
            continue
        body_ys.extend(sh["verts"][:, 1].tolist())
    za_min_y, za_max_y = min(body_ys), max(body_ys)
    za_h = max(za_max_y - za_min_y, 1e-3)
    scale = (van_h * cfg["height_ratio"]) / za_h
    ty = van_min_y - za_min_y * scale
    print(f"  Scale={scale:.4f} (ZA h={za_h:.3f}m -> target {za_h*scale:.3f}m vs vanilla {van_h:.3f}m), ty={ty:.4f}")

    # Build textures & UV transform per material
    textures_meta: dict[str, list[int]] = {}
    uv_xform: dict[str, tuple[float, float]] = {}  # mat_name -> (u_scale, u_offset) BEFORE *0.5

    if species == "pm0006":
        # BodyA_col (1024x1024): body_a has UVScaleOffset.x=2.0 + MirrorU -> bake [half | fliplr(half)]!
        body_a_half = composite_material_srgb(trmtr["body_a"], bntx_idx, tmp_dir, 512, 1024)
        body_a_img = np.concatenate([body_a_half, body_a_half[:, ::-1, :]], axis=1)
        w, h = save_tex_raw_and_png(body_a_img, "BodyA_col", out_dir)
        textures_meta["BodyA_col"] = [w, h]

        # BodyB_col (1024x1024): left [0..768) = [body_b | fliplr(body_b)] (exact MirrorU for UVScaleOffset.x=2.0!),
        # right [768..1024) = fire_a (mouth flames)
        body_b_half = composite_material_srgb(trmtr["body_b"], bntx_idx, tmp_dir, 384, 1024)
        body_b_img = np.concatenate([body_b_half, body_b_half[:, ::-1, :]], axis=1)
        fire_a_img = composite_material_srgb(trmtr["fire_a"], bntx_idx, tmp_dir, 256, 1024)
        # Boost mouth flame vibrancy for glowing blue fire
        fire_a_img[:, :, :3] = np.clip(fire_a_img[:, :, :3] * 1.15, 0.0, 1.0)
        atlas_b = np.zeros((1024, 1024, 4), dtype=np.float32)
        atlas_b[:, :768, :] = body_b_img
        atlas_b[:, 768:, :] = fire_a_img
        w, h = save_tex_raw_and_png(atlas_b, "BodyB_col", out_dir)
        textures_meta["BodyB_col"] = [w, h]

        # BodyB_emi (512x512): _EmissionMaskTex has scale=(1,1), while u_bdsp in [0, 0.5]
        # So fire_a u_bdsp in [0.375, 0.5] maps to x in [192, 256) of BodyB_emi!
        emi = np.zeros((512, 512, 4), dtype=np.float32)
        emi[:, :, 3] = 1.0
        emi[:, 192:256, :3] = 1.0
        w, h = save_tex_raw_and_png(emi, "BodyB_emi", out_dir)
        textures_meta["BodyB_emi"] = [w, h]

        # Eye_col (256x256)
        eye_img = composite_material_srgb(trmtr["r_eye"], bntx_idx, tmp_dir, 256, 256, is_eye=True)
        w, h = save_tex_raw_and_png(eye_img, "Eye_col", out_dir)
        textures_meta["Eye_col"] = [w, h]

        uv_xform["body_b"] = (0.74, 0.005)
        uv_xform["fire_a"] = (0.24, 0.755)
        uv_xform["body_a"] = (1.0, 0.0)
        uv_xform["r_eye"] = (1.0, 0.0)
        uv_xform["l_eye"] = (1.0, 0.0)

        # BDSP pm0006 submeshes: [0: BodyB, 1: BodyA, 2: BodyB, 3: REye, 4: LEye]
        mat_to_sub = {
            "body_b": 0,
            "body_a": 1,
            "fire_a": 2,
            "r_eye": 3,
            "l_eye": 4,
        }
        num_subs = 5
        submesh_tex_names = ["BodyB_col", "BodyA_col", "BodyB_col", "Eye_col", "Eye_col"]
    else:
        # pm0359 submeshes: [0: Body01, 1: Eye, 2: BodyVco, 3: Mouth(empty), 4: Body00, 5: Body02]
        for zmat, bdsp_tex, sz, is_e in [
            ("body_a_01", "Body01_col", 512, False),
            ("r_eye",     "Eye_col",    256, True),
            ("body_b_00", "BodyVco_col", 512, False),
            ("body_a_00", "Body00_col", 512, False),
            ("body_b_01", "Body02_col", 512, False),
        ]:
            uv_so = trmtr[zmat]["cols"].get("UVScaleOffset", (1.0, 1.0, 0.0))
            if uv_so[0] > 1.5:
                # Material uses UVScaleOffset.x=2.0 + MirrorU -> bake [half | fliplr(half)]!
                half = composite_material_srgb(trmtr[zmat], bntx_idx, tmp_dir, sz // 2, sz, is_eye=is_e)
                img = np.concatenate([half, half[:, ::-1, :]], axis=1)
            else:
                img = composite_material_srgb(trmtr[zmat], bntx_idx, tmp_dir, sz, sz, is_eye=is_e)
            w, h = save_tex_raw_and_png(img, bdsp_tex, out_dir)
            textures_meta[bdsp_tex] = [w, h]

        mat_to_sub = {
            "body_a_01": 0,
            "r_eye": 1,
            "l_eye": 1,
            "body_b_00": 2,
            "body_a_00": 4,
            "body_b_01": 5,
        }
        num_subs = 6
        submesh_tex_names = ["Body01_col", "Eye_col", "BodyVco_col", "Body01_col", "Body00_col", "Body02_col"]

    (out_dir / "textures.json").write_text(json.dumps(textures_meta, indent=2))

    # Assemble unified BDSP mesh
    all_verts: list[list[float]] = []
    all_norms: list[list[float]] = []
    all_uvs: list[list[float]] = []
    all_w: list[list[float]] = []
    all_i: list[list[int]] = []
    submeshes: list[list[int]] = [[] for _ in range(num_subs)]

    for sh in shapes:
        # Skip static tail fire_b on Charizard X (BDSP renders animated FireCore/FireMask tinted blue!)
        if species == "pm0006" and "fire_b" in sh["name"]:
            continue

        # Duplicate vertices per material slice if a shape has multiple materials with different UV xforms
        for mname, poly_off, poly_cnt in sh["mats"]:
            if mname not in mat_to_sub:
                continue
            sub_idx = mat_to_sub[mname]
            u_sc, u_of = uv_xform.get(mname, (1.0, 0.0))

            slice_idx = sh["indices"][poly_off : poly_off + poly_cnt]
            used = np.unique(slice_idx)
            local_to_global = {}

            remapped_i, remapped_w = remap_vertex_weights(sh["b_idx"][used], sh["b_w"][used], rig_map, fallback_bone)

            for loc_pos, orig_v in enumerate(used):
                v_za = sh["verts"][orig_v]
                n_za = sh["norms"][orig_v]
                uv_za = sh["uvs"][orig_v]

                # Negate X for Trinity RH -> Unity LH
                vx = float(-v_za[0] * scale)
                vy = float(v_za[1] * scale + ty)
                vz = float(v_za[2] * scale)
                nx = float(-n_za[0])
                ny = float(n_za[1])
                nz = float(n_za[2])

                # Wrap U cleanly into [0, 1) before atlas xform & BDSP *0.5 (for vanilla scale=(2,1))
                u_mod = float(uv_za[0]) % 1.0
                if u_mod == 0.0 and float(uv_za[0]) > 0.5:
                    u_mod = 1.0
                u_bdsp = (u_mod * u_sc + u_of) * 0.5
                v_bdsp = float(uv_za[1])

                g_idx = len(all_verts)
                local_to_global[int(orig_v)] = g_idx
                all_verts.append([vx, vy, vz])
                all_norms.append([nx, ny, nz])
                all_uvs.append([u_bdsp, v_bdsp])
                all_w.append(remapped_w[loc_pos])
                all_i.append(remapped_i[loc_pos])

            # Reverse triangle winding (a, c, b) to match X negation!
            s_out = submeshes[sub_idx]
            for t in range(0, len(slice_idx), 3):
                a = local_to_global[int(slice_idx[t])]
                b = local_to_global[int(slice_idx[t + 1])]
                c = local_to_global[int(slice_idx[t + 2])]
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

    render_textured_qa(form_id, mesh_json, out_dir, submesh_tex_names, QA_DIR / f"{form_id}_za_qa.png")


FORMS = [
    {
        "form_id": "pm0006_01_00",
        "species": "pm0006",
        "is_shiny": False,
        "za_pack": "pm0006_51_00",
        "trmsh": "097_6b622a780d9b5b94.trmsh",
        "trmbf": "094_6b2f22780d6ff463.bin",
        "trskl": "014_14b67077dc810086.bin",
        "trmtr": "096_6b5f0e780d98f629.trmtr",
        "van_json": "/tmp/mega_x_build/van_pm0006.json",
        "height_ratio": 1.05,
    },
    {
        "form_id": "pm0006_01_10",
        "species": "pm0006",
        "is_shiny": True,
        "za_pack": "pm0006_51_00",
        "trmsh": "097_6b622a780d9b5b94.trmsh",
        "trmbf": "094_6b2f22780d6ff463.bin",
        "trskl": "014_14b67077dc810086.bin",
        "trmtr": "225_f23c4a22549055b4.trmtr",
        "van_json": "/tmp/mega_x_build/van_pm0006.json",
        "height_ratio": 1.05,
    },
    {
        "form_id": "pm0359_01_00",
        "species": "pm0359",
        "is_shiny": False,
        "za_pack": "pm0359_51_00",
        "trmsh": "115_bc29271f45a26063.trmsh",
        "trmbf": "118_bc5c231f45cdb330.bin",
        "trskl": "103_aa0add1f3b2620ed.bin",
        "trmtr": "116_bc2d271f45a6493a.trmtr",
        "van_json": "/tmp/mega_absol_build/van_pm0359.json",
        "height_ratio": 1.00,
    },
    {
        "form_id": "pm0359_01_10",
        "species": "pm0359",
        "is_shiny": True,
        "za_pack": "pm0359_51_00",
        "trmsh": "115_bc29271f45a26063.trmsh",
        "trmbf": "118_bc5c231f45cdb330.bin",
        "trskl": "103_aa0add1f3b2620ed.bin",
        "trmtr": "089_92345df8bb3c7e61.trmtr",
        "van_json": "/tmp/mega_absol_build/van_pm0359.json",
        "height_ratio": 1.00,
    },
    {
        "form_id": "pm0359_02_00",
        "species": "pm0359",
        "is_shiny": False,
        "za_pack": "pm0359_52_00",
        "trmsh": "057_45f8eb0634b4448d.trmsh",
        "trmbf": "060_462bdb0634df82f6.bin",
        "trskl": "107_9cc07d0665e6d65b.bin",
        "trmtr": "056_45eedf0634abdba4.trmtr",
        "van_json": "/tmp/mega_absol_build/van_pm0359.json",
        "height_ratio": 1.02,
    },
    {
        "form_id": "pm0359_02_10",
        "species": "pm0359",
        "is_shiny": True,
        "za_pack": "pm0359_52_00",
        "trmsh": "057_45f8eb0634b4448d.trmsh",
        "trmbf": "060_462bdb0634df82f6.bin",
        "trskl": "107_9cc07d0665e6d65b.bin",
        "trmtr": "149_f4050eaa015030cb.trmtr",
        "van_json": "/tmp/mega_absol_build/van_pm0359.json",
        "height_ratio": 1.02,
    },
]


if __name__ == "__main__":
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    for cfg in FORMS:
        build_form(cfg)
    print("\nALL 6 LEGENDS: Z-A NORMAL + SHINY FORMS BUILT & RENDERED!")
