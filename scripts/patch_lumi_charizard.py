#!/usr/bin/env python3
"""Enable the two dormant Mega Charizard personal rows in Lumi 2.2F BD."""

import argparse
import hashlib
import struct
from pathlib import Path

EXPECTED_SHA256 = "bfe5505d3e62da1e8d9d5eb9a7eda6a0b7027f97e2ba27e1f08f00a73d1c8584"
RECORD_SIZE = 172
RECORD_COUNT = 1466


def lz4_decode(data: bytes, expected: int) -> bytes:
    result = bytearray()
    pos = 0
    while pos < len(data):
        token = data[pos]
        pos += 1
        literal = token >> 4
        if literal == 15:
            while True:
                extra = data[pos]
                pos += 1
                literal += extra
                if extra != 255:
                    break
        result.extend(data[pos:pos + literal])
        pos += literal
        if pos == len(data):
            break
        distance = int.from_bytes(data[pos:pos + 2], "little")
        pos += 2
        if not 0 < distance <= len(result):
            raise ValueError("invalid LZ4 distance")
        match = token & 15
        if match == 15:
            while True:
                extra = data[pos]
                pos += 1
                match += extra
                if extra != 255:
                    break
        for _ in range(match + 4):
            result.append(result[-distance])
    if len(result) != expected:
        raise ValueError("LZ4 size mismatch")
    return bytes(result)


def lz4_compress(data: bytes) -> bytes:
    """Greedy LZ4 block encoder; no external compressor is required."""
    result = bytearray()
    seen = {}
    anchor = 0
    pos = 0

    def extension(length: int) -> None:
        while length >= 255:
            result.append(255)
            length -= 255
        result.append(length)

    while pos + 4 <= len(data) - 5:
        key = bytes(data[pos:pos + 4])
        previous = seen.get(key)
        seen[key] = pos
        if previous is None or pos - previous > 65535:
            pos += 1
            continue
        end = pos + 4
        while end < len(data) - 5 and data[previous + end - pos] == data[end]:
            end += 1
        literal_length = pos - anchor
        match_length = end - pos - 4
        result.append((min(literal_length, 15) << 4) | min(match_length, 15))
        if literal_length >= 15:
            extension(literal_length - 15)
        result.extend(data[anchor:pos])
        result.extend((pos - previous).to_bytes(2, "little"))
        if match_length >= 15:
            extension(match_length - 15)
        for cursor in range(pos + 1, end):
            if cursor + 4 <= len(data):
                seen[bytes(data[cursor:cursor + 4])] = cursor
        pos = end
        anchor = end
    literal_length = len(data) - anchor
    result.append(min(literal_length, 15) << 4)
    if literal_length >= 15:
        extension(literal_length - 15)
    result.extend(data[anchor:])
    return bytes(result)


def patch(source: Path, destination: Path) -> None:
    bundle = source.read_bytes()
    digest = hashlib.sha256(bundle).hexdigest()
    if digest != EXPECTED_SHA256:
        raise ValueError(f"expected installed Lumi 2.2F bundle, got SHA256 {digest}")
    if bundle[:8] != b"UnityFS\0" or struct.unpack_from(">I", bundle, 8)[0] != 7:
        raise ValueError("unexpected UnityFS header")
    header_offset = 12
    for _ in range(2):
        header_offset = bundle.index(0, header_offset) + 1
    total_size = struct.unpack_from(">Q", bundle, header_offset)[0]
    info_size, plain_info_size, flags = struct.unpack_from(">III", bundle, header_offset + 8)
    if total_size != len(bundle) or (info_size, plain_info_size, flags) != (91, 91, 0x40):
        raise ValueError("unexpected Lumi bundle layout")
    info_offset = 0x40
    data_offset = info_offset + info_size
    if struct.unpack_from(">I", bundle, info_offset + 16)[0] != 1:
        raise ValueError("expected one UnityFS block")
    plain_size, packed_size, block_flags = struct.unpack_from(">IIH", bundle, info_offset + 20)
    if (plain_size, block_flags) != (963904, 2) or data_offset + packed_size != len(bundle):
        raise ValueError("unexpected UnityFS data block")
    contents = bytearray(lz4_decode(bundle[data_offset:], plain_size))
    marker = b"\r\0\0\0PersonalTable\0"
    name_offset = contents.rfind(marker)
    if name_offset != 646180:
        raise ValueError("unexpected PersonalTable location")
    count_offset = name_offset + len(marker)
    # Unity aligns the name to four bytes before the table length.
    count_offset = (count_offset + 3) & ~3
    if struct.unpack_from("<I", contents, count_offset)[0] != RECORD_COUNT:
        raise ValueError("unexpected personal record count")
    row_start = count_offset + 4
    for index, expected_stats, expected_types, expected_ability in (
        (1014, (78, 130, 111, 100, 130, 85), (9, 15), 181),
        (1015, (78, 104, 78, 100, 159, 115), (9, 2), 70),
    ):
        offset = row_start + index * RECORD_SIZE
        row = struct.unpack_from("<43I", contents, offset)
        if row[0] != 0 or row[2] != 6 or row[3] != 1014:
            raise ValueError(f"unexpected Mega Charizard row {index}")
        if row[7:13] != expected_stats or row[13:15] != expected_types or row[28] != expected_ability:
            raise ValueError(f"Mega Charizard row {index} data mismatch")
        struct.pack_into("<I", contents, offset, 1)
    packed = lz4_compress(contents)
    result = bytearray(bundle[:data_offset])
    struct.pack_into(">Q", result, header_offset, data_offset + len(packed))
    struct.pack_into(">I", result, info_offset + 24, len(packed))
    result.extend(packed)
    if lz4_decode(result[data_offset:], plain_size) != contents:
        raise ValueError("repacked UnityFS block failed round trip")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(result)
    print(f"enabled Mega Charizard X/Y rows in {destination}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    patch(args.source, args.destination)
