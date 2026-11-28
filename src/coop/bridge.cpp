// Functions other modules can call into the co-op client.
//
// The Mega module (subsdk2) owns the battle-setup hooks, so it calls these
// through nn::ro::LookupSymbol. If subsdk3 isn't installed the lookup simply
// fails and the Mega module behaves exactly as before.

#include "nn/result.h"
#include "exlaunch.hpp"

#include "coop.hpp"

#define COOP_EXPORT extern "C" __attribute__((visibility("default")))

// Called by the Mega module when a wild battle is being set up.
// Returns 1 if someone else in your room owns this legendary or is battling it
// right now (the battle then runs without Poké Balls), 0 otherwise. Waits at
// most timeoutMs for the server; if it doesn't answer in time, the battle is
// allowed as normal.
COOP_EXPORT int coop_legend_check(int32_t species, int32_t timeoutMs) {
    if (species <= 0 || species >= 1024) return 0;
    return coop::CheckLegendForBattle(static_cast<uint16_t>(species), timeoutMs) ? 1 : 0;
}

// Version so the Mega module can tell which features this subsdk3 has.
COOP_EXPORT int coop_bridge_version() {
    return 1;
}
