#!/usr/bin/env python3
"""Check that an ExLaunch subsdk is an NSO with a reachable MOD0 header."""

import argparse
import struct
from pathlib import Path


def lz4_block(data: bytes, expected: int) -> bytes:
    result = bytearray()
    pos = 0
    while pos < len(data):
        token = data[pos]
        pos += 1
        literal_length = token >> 4
        if literal_length == 15:
            while True:
                extension = data[pos]
                pos += 1
                literal_length += extension
                if extension != 255:
                    break
        result.extend(data[pos : pos + literal_length])
        pos += literal_length
        if pos == len(data):
            break
        distance = int.from_bytes(data[pos : pos + 2], "little")
        pos += 2
        if not 0 < distance <= len(result):
            raise ValueError("invalid LZ4 match distance")
        match_length = token & 15
        if match_length == 15:
            while True:
                extension = data[pos]
                pos += 1
                match_length += extension
                if extension != 255:
                    break
        match_length += 4
        for _ in range(match_length):
            result.append(result[-distance])
    if len(result) != expected:
        raise ValueError(f"decompressed {len(result)} bytes; expected {expected}")
    return bytes(result)


def verify(path: Path) -> None:
    binary = path.read_bytes()
    if binary[:4] != b"NSO0" or len(binary) < 0x100:
        raise ValueError("not a Nintendo Switch NSO")
    flags = struct.unpack_from("<I", binary, 0xC)[0]
    segments = []
    for index, header in enumerate((0x10, 0x20, 0x30)):
        file_offset, memory_offset, size = struct.unpack_from("<III", binary, header)
        stored_size = struct.unpack_from("<I", binary, 0x60 + 4 * index)[0]
        stored = binary[file_offset : file_offset + stored_size]
        if len(stored) != stored_size:
            raise ValueError("truncated NSO segment")
        contents = lz4_block(stored, size) if flags & (1 << index) else stored
        if len(contents) != size:
            raise ValueError("NSO segment size mismatch")
        segments.append((memory_offset, contents))
    text_offset, text = segments[0]
    if len(text) < 8:
        raise ValueError("missing NSO entry point")
    mod0_relative = struct.unpack_from("<I", text, 4)[0]
    mod0_address = text_offset + mod0_relative
    for memory_offset, contents in segments:
        location = mod0_address - memory_offset
        if 0 <= location <= len(contents) - 4 and contents[location : location + 4] == b"MOD0":
            print(f"{path}: valid NSO, MOD0 at memory offset 0x{mod0_address:x}")
            return
    raise ValueError(f"MOD0 not found at entry point offset 0x{mod0_address:x}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("nso", type=Path)
    verify(parser.parse_args().nso)
