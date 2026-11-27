// Connection to the co-op server: one thread sends (HELLO, 20 Hz position,
// pings, map changes), one thread receives (snapshots, player info, chat).

#include <cstdio>
#include <cstring>

#include "nn/result.h"
#include "exlaunch.hpp"
#include "nn/os.h"
#include "nn/socket.h"

#include "coop.hpp"
#include "protocol.hpp"

namespace coop {

namespace {
using namespace proto;

constexpr int kAfInet = 2, kSockStream = 1, kIpProtoTcp = 6, kTcpNoDelay = 1;
constexpr s64 kMs = 1000000;  // nanoseconds per millisecond

Settings gSettings;
volatile int gFd = -1;
volatile bool gConnected = false;
uint16_t gMyId = 0;
uint16_t gSeq = 0;
char gLastError[96] = "starting";

nn::os::MutexType gSendMutex, gRemoteMutex;
RemotePlayer gRemote[kMaxRemote];

nn::os::ThreadType gSendThread, gRecvThread;
alignas(0x1000) uint8_t gSendStack[0x8000];
alignas(0x1000) uint8_t gRecvStack[0x8000];
bool gStarted = false;

void setError(const char* fmt, const char* arg = "") {
    snprintf(gLastError, sizeof(gLastError), fmt, arg);
}

bool sendAll(int fd, const uint8_t* p, size_t n) {
    while (n > 0) {
        s32 r = nn::socket::Send(fd, p, n, 0);
        if (r <= 0) return false;
        p += r;
        n -= static_cast<size_t>(r);
    }
    return true;
}

bool recvAll(int fd, uint8_t* p, size_t n) {
    while (n > 0) {
        s32 r = nn::socket::Recv(fd, p, n, 0);
        if (r <= 0) return false;
        p += r;
        n -= static_cast<size_t>(r);
    }
    return true;
}

bool sendPacket(Writer& w, uint8_t type) {
    nn::os::LockMutex(&gSendMutex);
    bool ok = false;
    int fd = gFd;
    if (fd >= 0) {
        size_t len = w.finish(type, ++gSeq);
        ok = sendAll(fd, w.buf, len);
    }
    nn::os::UnlockMutex(&gSendMutex);
    return ok;
}

void disconnect(const char* why) {
    nn::os::LockMutex(&gSendMutex);
    if (gFd >= 0) {
        nn::socket::Close(gFd);
        gFd = -1;
    }
    bool was = gConnected;
    gConnected = false;
    nn::os::UnlockMutex(&gSendMutex);

    nn::os::LockMutex(&gRemoteMutex);
    for (auto& r : gRemote) r = RemotePlayer{};
    nn::os::UnlockMutex(&gRemoteMutex);
    if (was) Log("Disconnected: %s", why);
    setError("%s", why);
}

RemotePlayer* findRemote(uint16_t id, bool create) {
    RemotePlayer* free = nullptr;
    for (auto& r : gRemote) {
        if (r.active && r.id == id) return &r;
        if (!r.active && free == nullptr) free = &r;
    }
    if (create && free != nullptr) {
        *free = RemotePlayer{};
        free->active = true;
        free->id = id;
        return free;
    }
    return nullptr;
}

// ---------------------------------------------------------------- connect + HELLO

bool connectAndHello(const LocalState& st) {
    int fd = nn::socket::Socket(kAfInet, kSockStream, 0);
    if (fd < 0) {
        setError("couldn't create a socket");
        return false;
    }
    int one = 1;
    nn::socket::SetSockOpt(fd, kIpProtoTcp, kTcpNoDelay, &one, sizeof(one));

    in_addr addr{};
    if (nn::socket::InetAton(gSettings.host, &addr) == 0) {
        nn::socket::Close(fd);
        setError("address '%s' must be an IP like 100.67.109.85", gSettings.host);
        return false;
    }
    sockaddr sa{};
    sa.family = kAfInet;
    sa.port = nn::socket::InetHtons(gSettings.port);
    sa.address = addr;
    if (nn::socket::Connect(fd, &sa, sizeof(sa)) != 0) {
        nn::socket::Close(fd);
        setError("can't reach %s (is the host's server running?)", gSettings.host);
        return false;
    }

    // HELLO_REQ: room[16] name[24] fashion u16 color u8 gender u8 species u16 form u8 shiny u8 zone i32 password[16]
    Writer w;
    w.str(gSettings.room, 16);
    w.str(gSettings.name, 24);
    w.put<uint16_t>(0);
    w.put<uint8_t>(0);
    w.put<uint8_t>(0);
    w.put<uint16_t>(0);
    w.put<uint8_t>(0);
    w.put<uint8_t>(0);
    w.put<int32_t>(st.zone);
    w.str(gSettings.password, 16);
    size_t len = w.finish(HelloReq, 1);
    if (!sendAll(fd, w.buf, len)) {
        nn::socket::Close(fd);
        setError("connection dropped during hello");
        return false;
    }

    // Wait for HELLO_ACK (skip anything else).
    uint8_t hdr[kHeaderSize];
    static uint8_t payload[kMaxPayload];
    for (int tries = 0; tries < 8; tries++) {
        if (!recvAll(fd, hdr, sizeof(hdr))) break;
        uint16_t magic, plen;
        memcpy(&magic, hdr, 2);
        memcpy(&plen, hdr + 4, 2);
        if (magic != kMagic || plen > kMaxPayload || !recvAll(fd, payload, plen)) break;
        if (hdr[3] != HelloAck) continue;
        Reader r(payload, plen);
        uint16_t id = r.get<uint16_t>();
        r.get<uint8_t>();  // tick rate
        uint8_t status = r.get<uint8_t>();
        if (status != StatusOK) {
            static const char* why[] = {"ok", "room is full", "wrong password", "server is a different version", "server is full", "bad room code"};
            setError("server refused: %s", status < 6 ? why[status] : "unknown reason");
            Log("Join refused: %s", gLastError);
            nn::socket::Close(fd);
            return false;
        }
        nn::os::LockMutex(&gSendMutex);
        gFd = fd;
        gMyId = id;
        gSeq = 1;
        gConnected = true;
        nn::os::UnlockMutex(&gSendMutex);
        Log("Joined room %s on %s:%d as #%d", gSettings.room, gSettings.host, gSettings.port, id);
        setError("%s", "");
        return true;
    }
    nn::socket::Close(fd);
    setError("no answer from the server");
    return false;
}

// ---------------------------------------------------------------- receive thread

void handlePacket(uint8_t type, const uint8_t* p, size_t n) {
    Reader r(p, n);
    switch (type) {
        case WorldSnapshot: {
            r.get<uint32_t>();  // server time
            int32_t zone = r.get<int32_t>();
            uint8_t count = r.get<uint8_t>();
            nn::os::LockMutex(&gRemoteMutex);
            for (int i = 0; i < count && r.ok; i++) {
                uint16_t id = r.get<uint16_t>();
                StateBody b = r.get<StateBody>();
                if (!r.ok) break;
                if (RemotePlayer* rp = findRemote(id, true)) {
                    rp->zone = b.zone ? b.zone : zone;
                    rp->x = b.x, rp->y = b.y, rp->z = b.z, rp->yaw = b.yaw, rp->speed = b.speed;
                    rp->form = b.form, rp->flags = b.flags;
                    rp->hasPos = true;
                    rp->lastUpdateTick = svcGetSystemTick();
                }
            }
            nn::os::UnlockMutex(&gRemoteMutex);
            break;
        }
        case PlayerMeta: {
            uint16_t id = r.get<uint16_t>();
            int32_t zone = r.get<int32_t>();
            char name[25];
            r.str(name, 24, sizeof(name));
            if (!r.ok) break;
            nn::os::LockMutex(&gRemoteMutex);
            if (RemotePlayer* rp = findRemote(id, true)) {
                bool isNew = rp->name[0] == 0;
                strncpy(rp->name, name, sizeof(rp->name) - 1);
                rp->zone = zone;
                if (isNew) Log("%s is on your map (#%d, map %d)", name, id, zone);
            }
            nn::os::UnlockMutex(&gRemoteMutex);
            break;
        }
        case PlayerLeave: {
            uint16_t id = r.get<uint16_t>();
            uint8_t reason = r.get<uint8_t>();
            nn::os::LockMutex(&gRemoteMutex);
            if (RemotePlayer* rp = findRemote(id, false)) {
                Log("%s %s", rp->name, reason == 0 ? "went to another map" : "left");
                *rp = RemotePlayer{};
            }
            nn::os::UnlockMutex(&gRemoteMutex);
            break;
        }
        case Chat: {
            uint16_t id = r.get<uint16_t>();
            char name[25];
            r.str(name, 24, sizeof(name));
            if (!r.ok) break;
            char text[201];
            size_t tl = n - r.off < 200 ? n - r.off : 200;
            memcpy(text, p + r.off, tl);
            text[tl] = 0;
            Log("[chat] %s: %s", id == 0 ? "server" : name, text);
            break;
        }
        default:
            break;  // PONG and features we don't use yet
    }
}

void recvThreadMain(void*) {
    static uint8_t payload[kMaxPayload];
    for (;;) {
        int fd = gFd;
        if (!gConnected || fd < 0) {
            svcSleepThread(100 * kMs);
            continue;
        }
        uint8_t hdr[kHeaderSize];
        if (!recvAll(fd, hdr, sizeof(hdr))) {
            if (gFd == fd) disconnect("connection lost");
            continue;
        }
        uint16_t magic, plen;
        memcpy(&magic, hdr, 2);
        memcpy(&plen, hdr + 4, 2);
        if (magic != kMagic || plen > kMaxPayload || !recvAll(fd, payload, plen)) {
            if (gFd == fd) disconnect("bad data from server");
            continue;
        }
        handlePacket(hdr[3], payload, plen);
    }
}

// ---------------------------------------------------------------- send thread

void writeStatus(const LocalState& st, bool fresh) {
    static char buf[1536];
    int n = snprintf(buf, sizeof(buf),
                     "state: %s\nserver: %s:%d\nroom: %s\nyou: #%d %s\nmap: %d  pos (%.1f, %.1f, %.1f)  %s\nnote: %s\nplayers:\n",
                     gConnected ? "connected" : "not connected", gSettings.host, gSettings.port, gSettings.room, gMyId,
                     gSettings.name, st.zone, st.x, st.y, st.z, fresh ? "in field" : "not in field (menu/battle/loading)",
                     gLastError[0] ? gLastError : "-");
    nn::os::LockMutex(&gRemoteMutex);
    for (auto& r : gRemote) {
        if (!r.active || n <= 0 || n >= static_cast<int>(sizeof(buf)) - 96) continue;
        n += snprintf(buf + n, sizeof(buf) - n, "  #%d %s  map %d  (%.1f, %.1f)\n", r.id, r.name[0] ? r.name : "?", r.zone,
                      r.x, r.z);
    }
    nn::os::UnlockMutex(&gRemoteMutex);
    WriteStatusFile(buf);
}

void sendThreadMain(void*) {
    Log("Co-op client started");
    while (!LoadSettings(gSettings)) {
        setError("%s", "waiting for sd:/config/pokemon-coop.ini (press PLAY in the app)");
        WriteStatusFile("state: waiting for settings\nnote: press PLAY in the Pokemon Co-op app so it writes sd:/config/pokemon-coop.ini\n");
        svcSleepThread(5000 * kMs);
    }
    Log("Settings: server %s:%d room %s name %s", gSettings.host, gSettings.port, gSettings.room, gSettings.name);

    bool hooked = false;
    uint32_t lastFrame = 0;
    int staleTicks = 0, tick = 0, backoff = 0;
    int32_t lastZone = -1;

    for (;; tick++) {
        svcSleepThread(50 * kMs);  // 20 Hz
        if (!hooked && tick % 10 == 0) hooked = TryInstallFieldHook();

        LocalState st = GetLocalState();
        bool fresh = st.frame != lastFrame;
        staleTicks = fresh ? 0 : staleTicks + 1;
        lastFrame = st.frame;
        bool inField = st.inField && staleTicks < 20;  // no new frame for 1 s = menu/battle/loading

        if (tick % 40 == 0) writeStatus(st, inField);

        if (!gConnected) {
            if (backoff > 0) {
                backoff--;
                continue;
            }
            if (!inField) continue;  // join once you're actually walking around
            if (!connectAndHello(st)) {
                Log("Connect failed: %s", gLastError);
                backoff = 100;  // retry in 5 s
                continue;
            }
            lastZone = st.zone;
        }

        if (inField) {
            if (st.zone != lastZone) {
                Writer w;
                w.put<int32_t>(lastZone);
                w.put<int32_t>(st.zone);
                w.put<float>(st.x);
                w.put<float>(st.y);
                w.put<float>(st.z);
                w.put<float>(st.yaw);
                if (!sendPacket(w, ZoneChange)) {
                    disconnect("send failed");
                    continue;
                }
                Log("Map %d -> %d", lastZone, st.zone);
                lastZone = st.zone;
            }
            Writer w;
            StateBody b{st.zone, st.x, st.y, st.z, st.yaw, st.speed, 0, st.form, st.flags};
            w.put(b);
            if (!sendPacket(w, PlayerState)) {
                disconnect("send failed");
                continue;
            }
        }
        if (tick % 40 == 0) {  // keep-alive every 2 s, also while in menus/battles
            Writer w;
            w.put<uint32_t>(static_cast<uint32_t>(tick * 50));
            if (!sendPacket(w, Ping)) disconnect("send failed");
        }
    }
}
}  // namespace

int GetRemotePlayers(RemotePlayer* out, int max) {
    int n = 0;
    nn::os::LockMutex(&gRemoteMutex);
    for (auto& r : gRemote) {
        if (r.active && n < max) out[n++] = r;
    }
    nn::os::UnlockMutex(&gRemoteMutex);
    return n;
}

void StartNetwork() {
    if (gStarted) return;
    gStarted = true;
    nn::os::InitializeMutex(&gSendMutex, false, 0);
    nn::os::InitializeMutex(&gRemoteMutex, false, 0);
    // Priority 44: below the game's main and render threads.
    if (nn::Result(nn::os::CreateThread(&gSendThread, sendThreadMain, nullptr, gSendStack, sizeof(gSendStack), 44)).isSuccess()) {
        nn::os::SetThreadNamePointer(&gSendThread, "coop-send");
        nn::os::StartThread(&gSendThread);
    }
    if (nn::Result(nn::os::CreateThread(&gRecvThread, recvThreadMain, nullptr, gRecvStack, sizeof(gRecvStack), 44)).isSuccess()) {
        nn::os::SetThreadNamePointer(&gRecvThread, "coop-recv");
        nn::os::StartThread(&gRecvThread);
    }
}

}  // namespace coop
