// One-of-each legendaries per room, from inside the game.
//
// Every ~2 seconds (main thread) we check whether a legendary is in your party.
// The first time one shows up we tell the server you caught it: it becomes
// yours for the whole room, and everyone's co-op app shows the owner. If
// someone else in the room already owns that species, the log and status file
// say so.
//
// This only *reports* catches. Stopping a second player from encountering an
// owned legendary needs a hook on battle setup, which the Mega module already
// uses; that comes with the in-game battle work (docs/COOP_BATTLES.md).

#include "nn/result.h"
#include "exlaunch.hpp"
#include "externals/PlayerWork.h"
#include "externals/Pml/PokeParty.h"
#include "nn/os.h"

#include "coop.hpp"

namespace coop {

namespace {
// Same list as the server's DefaultLegendaries (National Dex numbers).
constexpr uint16_t kLegendaries[] = {144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 251, 377, 378, 379, 380, 381, 382, 383,
                                     384, 385, 386, 480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493};
constexpr int kCount = sizeof(kLegendaries) / sizeof(kLegendaries[0]);

bool gReported[kCount];
volatile bool gResetRequested = false;
uint32_t gTick = 0;

nn::os::MutexType gQueueMutex;
bool gQueueInit = false;
uint16_t gQueue[kCount];
int gQueueLen = 0;

void ensureQueue() {
    if (!gQueueInit) {
        nn::os::InitializeMutex(&gQueueMutex, false, 0);
        gQueueInit = true;
    }
}
}  // namespace

void LegendTick() {
    ensureQueue();
    if (gResetRequested) {  // reconnected: report everything again (the server ignores repeats)
        for (auto& r : gReported) r = false;
        gResetRequested = false;
    }
    if (++gTick % 120 != 0 || !IsConnected()) return;  // ~2 s at 60 fps, only while online

    auto* party = PlayerWork::get_playerParty();
    if (party == nullptr) return;
    for (int i = 0; i < kCount; i++) {
        if (gReported[i] || !party->CheckPokeExist(kLegendaries[i])) continue;
        gReported[i] = true;
        nn::os::LockMutex(&gQueueMutex);
        if (gQueueLen < kCount) gQueue[gQueueLen++] = kLegendaries[i];
        nn::os::UnlockMutex(&gQueueMutex);
    }
}

bool PopCaughtLegend(uint16_t& species) {
    ensureQueue();
    nn::os::LockMutex(&gQueueMutex);
    bool any = gQueueLen > 0;
    if (any) {
        species = gQueue[0];
        for (int i = 1; i < gQueueLen; i++) gQueue[i - 1] = gQueue[i];
        gQueueLen--;
    }
    nn::os::UnlockMutex(&gQueueMutex);
    return any;
}

void ResetLegendReports() { gResetRequested = true; }

}  // namespace coop
