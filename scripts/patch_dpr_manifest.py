#!/usr/bin/env python3
"""Patch BDSP / Luminescent Platinum AssetAssistant Dpr.bin (MS-NRBF BinaryFormatter)
to register Mega Charizard X & Y battle and field AssetBundleRecord entries.

Reads the source Dpr.bin from Luminescent Platinum 2.2F BD, appends 8 new
AssetBundleRecord entries (battle + field for pm0006_01_00/01 and pm0006_02_00/01)
using exact file sizes from romfs-delta/, reuses pm0006_00_00's exact allDependencies
array reference, and writes the updated Dpr.bin to romfs-delta/.
"""

import argparse
import struct
from pathlib import Path

RECORDS_LEN_OFFSET = 0x0133
RECORDS_ITEMS_OFFSET = 0x0168
RECORD_METADATA_ID = 6
HASH_METADATA_ID = 0xFFFF9DB3  # signed -25165 (same as vanilla RecordedHash in Dpr.bin)
BATTLE_DEPS_ID = 0x69DD        # allDependencies of pokemons/battle/pm0006_00_00


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


def find_field_deps_id(data: bytes) -> tuple[int, bytes]:
    marker = b"pokemons/field/pm0006_00_00"
    idx = data.find(marker)
    if idx == -1:
        raise ValueError("Could not find pokemons/field/pm0006_00_00 in Dpr.bin")
    # After string bytes:
    # RecordedHash ClassWithId (1 + 4 + 4 + 16 = 25 bytes)
    # DateTime (8 bytes)
    # bool isStreamingSceneAssetBundle (1 byte)
    # MemberReference allDependencies (1 + 4 = 5 bytes)
    # MemberReference assetPaths (1 + 4 = 5 bytes)
    pos = idx + len(marker)
    if data[pos] != 0x01:
        raise ValueError(f"Unexpected RecordedHash opcode 0x{data[pos]:02x} at {pos}")
    hash_and_time = data[pos : pos + 25 + 8]
    pos += 25 + 8 + 1  # skip hash (25), DateTime (8), bool (1)
    if data[pos] != 0x09:
        raise ValueError(f"Expected MemberReference (0x09) for field allDependencies at {pos}")
    deps_id = struct.unpack_from("<I", data, pos + 1)[0]
    return deps_id, hash_and_time


def patch_dpr_bin(source: Path, delta_root: Path, output: Path) -> None:
    data = source.read_bytes()
    if data[-1] != 0x0B:
        raise ValueError("Dpr.bin does not end with NRBF MessageEnd (0x0B)")

    orig_count = struct.unpack_from("<I", data, RECORDS_LEN_OFFSET)[0]
    array_end = RECORDS_ITEMS_OFFSET + orig_count * 5
    # Verify every entry in _records is a MemberReference (0x09)
    if data[RECORDS_ITEMS_OFFSET] != 0x09 or data[array_end - 5] != 0x09:
        raise ValueError("Unexpected _records array layout in Dpr.bin")
    if data[array_end] == 0x09:
        raise ValueError("_records array end offset mismatch")

    # Verify battle template allDependencies ID exists
    if data.find(b"\x11" + struct.pack("<I", BATTLE_DEPS_ID)) == -1:
        raise ValueError("Battle allDependencies array 0x69dd not found in Dpr.bin")

    field_deps_id, _ = find_field_deps_id(data)

    # Grab exact DateTime bytes from pm0006_00_00 battle record
    b_idx = data.find(b"pokemons/battle/pm0006_00_00")
    if b_idx == -1:
        raise ValueError("Could not find pokemons/battle/pm0006_00_00 in Dpr.bin")
    battle_datetime = data[b_idx + len(b"pokemons/battle/pm0006_00_00") + 25 : b_idx + len(b"pokemons/battle/pm0006_00_00") + 33]

    # Dynamically look up each species' vanilla battle allDependencies ID in Dpr.bin
    def find_battle_deps_id(species_prefix: str) -> int:
        m = f"pokemons/battle/{species_prefix}_00_00".encode("utf-8")
        p = data.find(m)
        if p == -1:
            raise ValueError(f"Could not find {m.decode()} in Dpr.bin")
        dp = p + len(m) + 25 + 8 + 1
        if data[dp] != 0x09:
            raise ValueError(f"Expected MemberReference (0x09) for {species_prefix} battle allDependencies")
        return struct.unpack_from("<I", data, dp + 1)[0]

    charizard_deps_id = find_battle_deps_id("pm0006")
    absol_deps_id = find_battle_deps_id("pm0359")

    entries = [
        ("pokemons/battle/pm0006_01_00", "assets/pokemons/battle/pm0006/pm0006_01_00.prefab", charizard_deps_id),
        ("pokemons/battle/pm0006_02_00", "assets/pokemons/battle/pm0006/pm0006_02_00.prefab", charizard_deps_id),
        ("pokemons/battle/pm0359_01_00", "assets/pokemons/battle/pm0359/pm0359_01_00.prefab", absol_deps_id),
        ("pokemons/battle/pm0359_02_00", "assets/pokemons/battle/pm0359/pm0359_02_00.prefab", absol_deps_id),
    ]

    # Guard against AssetBundleDownloadManifest.BuildLookupTables() Dictionary.Add duplicate-key crashes
    seen_paths = set()
    for bundle_name, asset_path, _ in entries:
        key = asset_path.lower().encode("utf-8")
        if key in seen_paths or data.find(key) != -1:
            raise ValueError(f"Duplicate assetPath would crash BuildLookupTables(): {asset_path}")
        if data.find(bundle_name.encode("utf-8")) != -1:
            raise ValueError(f"Duplicate assetBundleName already in Dpr.bin: {bundle_name}")
        seen_paths.add(key)

    # Locate end of AssetBundleRecord block (right after last record id=25063, before first ArraySingleString 0x11)
    first_arr_marker = b"\x11\x4e\x62\x00\x00"
    records_block_end = data.find(first_arr_marker)
    if records_block_end == -1:
        raise ValueError("Could not locate end of AssetBundleRecord block in Dpr.bin")

    # Use clean sequential IDs immediately above vanilla's max positive ID (0x042e9a) and below min negative ID (-150450)
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

        rec_id = next_id; next_id += 1
        proj_str_id = next_id; next_id += 1
        name_str_id = next_id; next_id += 1
        paths_arr_id = next_id; next_id += 1
        path_str_id = next_id; next_id += 1
        hash_obj_id = next_neg_id; next_neg_id -= 1

        # Add MemberReference into _records[]
        new_refs.extend(b"\x09" + struct.pack("<I", rec_id))

        # ClassWithId(AssetBundleRecord, metadataId=6) placed inside the AssetBundleRecord block
        new_record_objs.extend(b"\x01" + struct.pack("<II", rec_id, RECORD_METADATA_ID))
        new_record_objs.extend(encode_binary_string(proj_str_id, "Pokemon Database"))
        new_record_objs.extend(encode_binary_string(name_str_id, bundle_name))
        # Inline RecordedHash ClassWithId (16 zero bytes)
        new_record_objs.extend(b"\x01" + struct.pack("<iI", hash_obj_id, HASH_METADATA_ID) + b"\x00" * 16)
        # DateTime (8 bytes) + bool isStreamingSceneAssetBundle (0)
        new_record_objs.extend(battle_datetime + b"\x00")
        # MemberReference allDependencies + MemberReference assetPaths
        new_record_objs.extend(b"\x09" + struct.pack("<I", deps_id))
        new_record_objs.extend(b"\x09" + struct.pack("<I", paths_arr_id))
        # int64 size
        new_record_objs.extend(struct.pack("<q", file_size))

        # Place referenced assetPaths ArraySingleString in the ArraySingleString section at the end
        new_path_arrays.extend(encode_array_single_string(paths_arr_id, path_str_id, asset_path.lower()))

        print(f"Registered {bundle_name} (rec_id=0x{rec_id:x}, size={file_size:,}, asset={asset_path}, deps_id=0x{deps_id:x})")

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
    out.append(0x0B)  # NRBF MessageEnd

    # Update existing Message/english record size in-place if overridden in romfs-delta
    msg_en = delta_root / "Message" / "english"
    if msg_en.exists():
        m_marker = b"\x07Message\x06\x1e:\x01\x00\x07english"
        m_idx = out.find(m_marker)
        if m_idx != -1:
            # After string: RecordedHash(25) + DateTime(8) + bool(1) + allDeps(5) + assetPaths(5) = 44 bytes -> int64 size
            sz_pos = m_idx + len(m_marker) + 44
            old_sz = struct.unpack_from("<q", out, sz_pos)[0]
            new_sz = msg_en.stat().st_size
            struct.pack_into("<q", out, sz_pos, new_sz)
            print(f"Updated message/english size in Dpr.bin ({old_sz:,} -> {new_sz:,})")

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(out)
    print(f"Wrote {output} ({len(out):,} bytes, records: {orig_count} -> {new_count})")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--delta-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    patch_dpr_bin(args.source, args.delta_root, args.output)
