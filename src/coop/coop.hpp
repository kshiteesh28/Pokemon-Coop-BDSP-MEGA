#pragma once
// Pokemon Co-op client (subsdk3).
//
// Stage 1: connect to the co-op server from inside the game and send where the
// player is (map + position) 20 times a second, and keep a list of the other
// players in the room.
// Stage 2a: show the other players on your map as marker figures (ghosts.cpp).
//
// Threads:
//   main game thread  -> game.cpp: per-frame hook reads the player (only place
//                        that touches game objects)
//   coop-send thread  -> net.cpp: connects, sends HELLO / state / pings
//   coop-recv thread  -> net.cpp: reads snapshots, metas, leaves, chat

#include <cstdint>
#include <cstddef>

namespace coop {

// ---------------------------------------------------------------- settings (config.cpp)
struct Settings {
    char host[64] = {};
    uint16_t port = 4100;
    char room[17] = {};
    char name[25] = {};
    char password[17] = {};
    bool loaded = false;
};

// Reads sd:/config/pokemon-coop.ini (written by the app's PLAY button).
bool LoadSettings(Settings& out);

// "sd:" if the main Luminescent module already mounted the SD card, otherwise
// our own "coopsd:" mount. Builds paths like "sd:/config/x".
void SdPath(char* out, size_t cap, const char* rel);

// ---------------------------------------------------------------- local player (game.cpp)
struct LocalState {
    bool inField = false;   // a field player exists right now
    int32_t zone = -1;
    float x = 0, y = 0, z = 0, yaw = 0, speed = 0;
    uint8_t form = 0;       // 0 walk, 1 run, 2 bike, 3 surf (best effort)
    uint8_t flags = 0;
    uint32_t frame = 0;     // increments every captured frame
};

// Thread-safe copy of the latest captured local state.
LocalState GetLocalState();

// Installs the per-frame hook once the field player exists. Called by the send
// thread until it returns true.
bool TryInstallFieldHook();

// ---------------------------------------------------------------- remote players (net.cpp)
constexpr int kMaxRemote = 8;

struct RemotePlayer {
    bool active = false;
    uint16_t id = 0;
    char name[25] = {};
    int32_t zone = -1;
    float x = 0, y = 0, z = 0, yaw = 0, speed = 0;
    uint8_t form = 0, flags = 0;
    bool hasPos = false;
    uint64_t lastUpdateTick = 0;
};

// Copies the current remote player table (for drawing them later).
int GetRemotePlayers(RemotePlayer* out, int max);

// Shows/moves/removes marker figures for players on your map (ghosts.cpp).
// Main thread only.
void UpdateGhosts(int32_t myZone, float deltaTime);
void ClearGhosts();

// Legendary registry (legend.cpp). LegendTick runs on the main thread; the
// network thread takes catches from the queue and reports them.
void LegendTick();
void ResetLegendReports();
bool IsConnected();

// Legendary events waiting for the network thread.
enum class LegendEvent : uint8_t { FoundInParty, Escaped };
bool PopLegendEvent(uint16_t& species, LegendEvent& kind);

// Battle guard (net.cpp). Asks the server to reserve `species` for you and
// waits up to timeoutMs. Returns true if someone else owns it or is battling it.
// Main thread only (called from the Mega module's wild-battle hook via bridge.cpp).
bool CheckLegendForBattle(uint16_t species, int32_t timeoutMs);
// Set by CheckLegendForBattle when the server reserved a legendary for you, so
// legend.cpp can release it again if you don't end up catching it.
void NoteLegendBattle(uint16_t species);
// True for species on the one-of-each list (no server round trip needed to know).
bool IsTrackedLegend(uint16_t species);

// Starts the network threads once (safe to call more than once).
void StartNetwork();

// Small text log + status file under sd:/config/ for debugging (log.cpp).
void Log(const char* fmt, ...);
void WriteStatusFile(const char* text);

}  // namespace coop
