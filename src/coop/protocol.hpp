#pragma once
// Wire format shared with the Go server (server/protocol). Little-endian, which
// is the Switch's native byte order, so fields are copied with memcpy.

#include <cstdint>
#include <cstring>

namespace coop::proto {

constexpr uint16_t kMagic = 0x4243;  // "BC"
constexpr uint8_t kVersion = 1;
constexpr size_t kHeaderSize = 8;
constexpr size_t kMaxPayload = 4096;

enum MsgType : uint8_t {
    HelloReq = 0x01,
    HelloAck = 0x02,
    PlayerMeta = 0x03,
    ZoneChange = 0x04,
    PlayerState = 0x05,
    WorldSnapshot = 0x06,
    PlayerLeave = 0x07,
    Ping = 0x08,
    Pong = 0x09,
    RoomRoster = 0x11,
    LegendReserve = 0x20,
    LegendReserveAck = 0x21,
    LegendResult = 0x22,
    LegendUpdate = 0x23,
    BattleLobby = 0x31,
    BattleStart = 0x36,
    BattleEnd = 0x3A,
    Progress = 0x3C,
    BattleReport = 0x3D,
    Chat = 0x40,
};

enum HelloStatus : uint8_t {
    StatusOK = 0,
    StatusRoomFull = 1,
    StatusBadPassword = 2,
    StatusBadVersion = 3,
    StatusServerFull = 4,
    StatusBadRoomCode = 5,
};

// Small append-only buffer for building packets.
struct Writer {
    uint8_t buf[kHeaderSize + kMaxPayload];
    size_t len = kHeaderSize;

    template <typename T>
    void put(T v) {
        if (len + sizeof(T) <= sizeof(buf)) {
            memcpy(buf + len, &v, sizeof(T));
            len += sizeof(T);
        }
    }
    // Fixed-size, zero-padded UTF-8 string field.
    void str(const char* s, size_t n) {
        size_t l = s ? strnlen(s, n) : 0;
        if (len + n > sizeof(buf)) return;
        memset(buf + len, 0, n);
        if (l) memcpy(buf + len, s, l);
        len += n;
    }
    // Writes the header for msg type t; returns the full packet length.
    size_t finish(uint8_t t, uint16_t seq) {
        uint16_t payload = static_cast<uint16_t>(len - kHeaderSize);
        memcpy(buf + 0, &kMagic, 2);
        buf[2] = kVersion;
        buf[3] = t;
        memcpy(buf + 4, &payload, 2);
        memcpy(buf + 6, &seq, 2);
        return len;
    }
};

struct Reader {
    const uint8_t* p;
    size_t n, off = 0;
    bool ok = true;

    Reader(const uint8_t* data, size_t size) : p(data), n(size) {}

    template <typename T>
    T get() {
        T v{};
        if (off + sizeof(T) > n) {
            ok = false;
            return v;
        }
        memcpy(&v, p + off, sizeof(T));
        off += sizeof(T);
        return v;
    }
    void str(char* out, size_t field, size_t outSize) {
        if (off + field > n) {
            ok = false;
            if (outSize) out[0] = 0;
            return;
        }
        size_t c = field < outSize - 1 ? field : outSize - 1;
        memcpy(out, p + off, c);
        out[c] = 0;
        off += field;
    }
};

// PLAYER_STATE body (28 bytes): zone i32, x y z yaw speed f32, anim u16, form u8, flags u8.
struct StateBody {
    int32_t zone;
    float x, y, z, yaw, speed;
    uint16_t anim;
    uint8_t form, flags;
};
static_assert(sizeof(StateBody) == 28, "PLAYER_STATE must be 28 bytes");

}  // namespace coop::proto
