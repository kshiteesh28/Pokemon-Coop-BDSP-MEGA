// One-of-each legendaries per room, from inside the game.
//
// Every ~2 seconds (main thread) we check whether a legendary is in your party.
// The first time one shows up we tell the server you caught it: it becomes
// yours for the whole room, and everyone's co-op app shows the owner. If
// someone else in the room already owns that species, the log and status file
// say so.
//
// The Mega module's wild-battle hook also asks us before a legendary battle
// (bridge.cpp → CheckLegendForBattle). If someone else owns it, that battle runs
// without Poké Balls. If you reserved it and don't catch it, it's released.

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
struct QueuedEvent {
    uint16_t species;
    LegendEvent kind;
};
QueuedEvent gQueue[kCount * 2];
int gQueueLen = 0;

// A legendary the server reserved for you when its battle started (main thread only).
uint16_t gBattleSpecies = 0;
uint32_t gFieldFramesSinceBattle = 0;

void push(uint16_t species, LegendEvent kind) {
    nn::os::LockMutex(&gQueueMutex);
    if (gQueueLen < static_cast<int>(sizeof(gQueue) / sizeof(gQueue[0]))) gQueue[gQueueLen++] = {species, kind};
    nn::os::UnlockMutex(&gQueueMutex);
}

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
    // After a reserved legendary battle, wait until you've been back in the
    // field for ~10 s. Field frames don't run during the battle itself, and 10 s
    // is well past the encounter transition. Then: if it's in your party, the
    // scan below reports the catch; if not, release it for everyone else.
    // (Caught into a full party's box counts as escaped here; it gets reported
    // again once you move it to your party.)
    if (gBattleSpecies != 0 && ++gFieldFramesSinceBattle >= 600) {
        auto* party = PlayerWork::get_playerParty();
        if (party != nullptr && !party->CheckPokeExist(gBattleSpecies)) push(gBattleSpecies, LegendEvent::Escaped);
        gBattleSpecies = 0;
    }

    if (++gTick % 120 != 0 || !IsConnected()) return;  // ~2 s at 60 fps, only while online

    auto* party = PlayerWork::get_playerParty();
    if (party == nullptr) return;
    for (int i = 0; i < kCount; i++) {
        if (gReported[i] || !party->CheckPokeExist(kLegendaries[i])) continue;
        gReported[i] = true;
        push(kLegendaries[i], LegendEvent::FoundInParty);
    }
}

bool IsTrackedLegend(uint16_t species) {
    for (uint16_t s : kLegendaries) {
        if (s == species) return true;
    }
    return false;
}

void NoteLegendBattle(uint16_t species) {
    gBattleSpecies = species;
    gFieldFramesSinceBattle = 0;
}

bool PopLegendEvent(uint16_t& species, LegendEvent& kind) {
    ensureQueue();
    nn::os::LockMutex(&gQueueMutex);
    bool any = gQueueLen > 0;
    if (any) {
        species = gQueue[0].species;
        kind = gQueue[0].kind;
        for (int i = 1; i < gQueueLen; i++) gQueue[i - 1] = gQueue[i];
        gQueueLen--;
    }
    nn::os::UnlockMutex(&gQueueMutex);
    return any;
}

void ResetLegendReports() { gResetRequested = true; }

}  // namespace coop
