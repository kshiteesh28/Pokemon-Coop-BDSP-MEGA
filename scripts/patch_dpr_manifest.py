#!/usr/bin/env python3
"""Dynamic BDSP / Luminescent Platinum AssetAssistant Dpr.bin (MS-NRBF BinaryFormatter) patcher.

Auto-discovers ALL custom Mega Pokemon battle bundles (pmXXXX_YY_00 and pmXXXX_YY_10 where YY != 00)
in romfs-delta/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons/battle/,
dynamically resolves each species' vanilla allDependencies ID from Dpr.bin, enforces zero duplicate
assetPath / assetBundleName collisions (preventing BuildLookupTables() Dictionary.Add crashes),
and writes exact file sizes into Dpr.bin.
"""

from __future__ import annotations

import argparse
import re
import struct
from pathlib import Path

RECORDS_LEN_OFFSET = 0x0133
RECORDS_ITEMS_OFFSET = 0x0168
RECORD_METADATA_ID = 6
HASH_METADATA_ID = 0xFFFF9DB3  # signed -25165 (same as vanilla RecordedHash in Dpr.bin)

MEGA_BUNDLE_RE = re.compile(r"^(pm\d{4})_(\d{2})_(00|10)$")


def encode_7bit_len(n: int) -> bytes:
    out = bytearray()
    while n >= 0x80:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    out.append(n)
    return bytes(out)


def encode_binary_string(obj_id: int, text: str) -> bytes:
    raw = text.encode("utf-8")
    return b"\x06" + struct.pack("<I", obj_id) + encode_7bit_len(len(raw)) + raw


def encode_array_single_string(arr_id: int, str_id: int, text: str) -> bytes:
    return (
        b"\x11"
        + struct.pack("<II", arr_id, 1)
        + encode_binary_string(str_id, text)
    )


def find_battle_deps_id(data: bytes, species_prefix: str) -> int:
    marker = f"pokemons/battle/{species_prefix}_00_00".encode("utf-8")
    p = data.find(marker)
    if p == -1:
        raise ValueError(f"Could not find vanilla battle record {marker.decode()} in Dpr.bin")
    dp = p + len(marker) + 25 + 8 + 1
    if dp + 5 > len(data) or data[dp] != 0x09:
        raise ValueError(f"Expected MemberReference (0x09) for {species_prefix} battle allDependencies at 0x{dp:x}")
    return struct.unpack_from("<I", data, dp + 1)[0]


def discover_mega_battle_bundles(delta_root: Path) -> list[tuple[str, str, str]]:
    battle_dir = delta_root / "Pokemon Database" / "pokemons" / "battle"
    if not battle_dir.exists():
        raise FileNotFoundError(f"Missing battle bundle directory: {battle_dir}")

    discovered: list[tuple[str, str, str]] = []
    for p in sorted(battle_dir.iterdir()):
        if not p.is_file():
            continue
        m = MEGA_BUNDLE_RE.match(p.name)
        if not m:
            continue
        species_prefix, form_str, _variant = m.groups()
        if form_str == "00":
            continue
        bundle_name = f"pokemons/battle/{p.name}"
        asset_path = f"assets/pokemons/battle/{species_prefix}/{p.name}.prefab"
        discovered.append((species_prefix, bundle_name, asset_path))
    return discovered


def patch_dpr_bin(source: Path, delta_root: Path, output: Path) -> int:
    data = source.read_bytes()
    if not data or data[-1] != 0x0B:
        raise ValueError("Dpr.bin does not end with NRBF MessageEnd (0x0B)")

    orig_count = struct.unpack_from("<I", data, RECORDS_LEN_OFFSET)[0]
    array_end = RECORDS_ITEMS_OFFSET + orig_count * 5
    if data[RECORDS_ITEMS_OFFSET] != 0x09 or data[array_end - 5] != 0x09:
        raise ValueError("Unexpected _records array layout in Dpr.bin")
    if data[array_end] == 0x09:
        raise ValueError("_records array end offset mismatch")

    b_marker = b"pokemons/battle/pm0006_00_00"
    b_idx = data.find(b_marker)
    if b_idx == -1:
        raise ValueError("Could not find pokemons/battle/pm0006_00_00 in Dpr.bin")
    battle_datetime = data[b_idx + len(b_marker) + 25 : b_idx + len(b_marker) + 33]

    discovered = discover_mega_battle_bundles(delta_root)
    if not discovered:
        raise RuntimeError(f"No Mega battle bundles found under {delta_root}")

    deps_cache: dict[str, int] = {}
    entries: list[tuple[str, str, int]] = []
    seen_paths: set[bytes] = set()
    seen_names: set[str] = set()

    for species_prefix, bundle_name, asset_path in discovered:
        if species_prefix not in deps_cache:
            deps_cache[species_prefix] = find_battle_deps_id(data, species_prefix)

        key = asset_path.lower().encode("utf-8")
        if key in seen_paths or data.find(key) != -1:
            raise ValueError(f"Duplicate assetPath would crash BuildLookupTables(): {asset_path}")
        if bundle_name in seen_names or data.find(bundle_name.encode("utf-8")) != -1:
            raise ValueError(f"Duplicate assetBundleName already in Dpr.bin: {bundle_name}")
        seen_paths.add(key)
        seen_names.add(bundle_name)
        entries.append((bundle_name, asset_path, deps_cache[species_prefix]))

    first_arr_marker = b"\x11\x4e\x62\x00\x00"
    records_block_end = data.find(first_arr_marker)
    if records_block_end == -1:
        raise ValueError("Could not locate end of AssetBundleRecord block in Dpr.bin")

    next_id = 0x00042F00
    next_neg_id = -200000

    new_refs = bytearray()
    new_record_objs = bytearray()
    new_path_arrays = bytearray()

    for bundle_name, asset_path, deps_id in entries:
        bundle_file = delta_root / "Pokemon Database" / bundle_name
        if not bundle_file.exists():
            raise FileNotFoundError(f"Missing bundle in romfs-delta: {bundle_file}")
        file_size = bundle_file.stat().st_size
        if file_size <= 0:
            raise ValueError(f"Empty bundle file: {bundle_file}")

        rec_id = next_id; next_id += 1
        proj_str_id = next_id; next_id += 1
        name_str_id = next_id; next_id += 1
        paths_arr_id = next_id; next_id += 1
        path_str_id = next_id; next_id += 1
        hash_obj_id = next_neg_id; next_neg_id -= 1

        new_refs.extend(b"\x09" + struct.pack("<I", rec_id))
        new_record_objs.extend(b"\x01" + struct.pack("<II", rec_id, RECORD_METADATA_ID))
        new_record_objs.extend(encode_binary_string(proj_str_id, "Pokemon Database"))
        new_record_objs.extend(encode_binary_string(name_str_id, bundle_name))
        new_record_objs.extend(b"\x01" + struct.pack("<iI", hash_obj_id, HASH_METADATA_ID) + b"\x00" * 16)
        new_record_objs.extend(battle_datetime + b"\x00")
        new_record_objs.extend(b"\x09" + struct.pack("<I", deps_id))
        new_record_objs.extend(b"\x09" + struct.pack("<I", paths_arr_id))
        new_record_objs.extend(struct.pack("<q", file_size))

        new_path_arrays.extend(encode_array_single_string(paths_arr_id, path_str_id, asset_path.lower()))
        print(f"Registered {bundle_name} (rec_id=0x{rec_id:x}, size={file_size:,}, deps_id=0x{deps_id:x})")

    new_count = orig_count + len(entries)
    out = bytearray()
    out.extend(data[:RECORDS_LEN_OFFSET])
    out.extend(struct.pack("<I", new_count))
    out.extend(data[RECORDS_LEN_OFFSET + 4 : array_end])
    out.extend(new_refs)
    out.extend(data[array_end:records_block_end])
    out.extend(new_record_objs)
    out.extend(data[records_block_end:-1])
    out.extend(new_path_arrays)
    out.append(0x0B)

    msg_en = delta_root / "Message" / "english"
    if msg_en.exists():
        m_marker = b"\x07Message\x06\x1e:\x01\x00\x07english"
        m_idx = out.find(m_marker)
        if m_idx != -1:
            sz_pos = m_idx + len(m_marker) + 44
            old_sz = struct.unpack_from("<q", out, sz_pos)[0]
            new_sz = msg_en.stat().st_size
            struct.pack_into("<q", out, sz_pos, new_sz)
            print(f"Updated message/english size in Dpr.bin ({old_sz:,} -> {new_sz:,})")

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(out)
    print(f"Wrote {output} ({len(out):,} bytes, records: {orig_count} -> {new_count}, +{len(entries)} Mega bundles)")
    return len(entries)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--delta-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    patch_dpr_bin(args.source, args.delta_root, args.output)
