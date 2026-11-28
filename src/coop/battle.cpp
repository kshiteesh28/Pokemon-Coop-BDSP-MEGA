// Team challenges inside the game.
//
// When your battle lobby has been started (host pressed Start in the app), the
// next trainer battle you begin counts for the team. After it ends and you're
// back in the field, we read the result straight from the game's battle setup
// data and report it to the server. When every teammate has reported, the
// server records the team's win (everyone won) or loss.
//
// Every trainer battle also writes one "[probe]" line to the co-op log with
// its rule, trainer IDs and partner ID. Those values are what real 2-vs-2
// battles will need (see docs/COOP_BATTLES.md).
//
// Main thread only: OnTrainerBattleBegin is called from the Mega module's
// SetupBattleTrainer hook (via bridge.cpp), BattleTick from the field hook.

#include "nn/result.h"
#include "exlaunch.hpp"
#include "externals/Dpr/Battle/Logic/BATTLE_SETUP_PARAM.h"

#include "coop.hpp"

namespace coop {

namespace {
// The game writes the battle outcome into BATTLE_SETUP_PARAM.result when the
// battle ends. We set it to this marker when the battle is set up, so "still
// the marker" means "not finished yet".
constexpr int32_t kNotFinished = -0x434F4F50;  // "COOP"
constexpr int32_t kResultWin = 1;              // BtlResult: 0 lose, 1 win, 2 draw, 3 ran, ...

struct Challenge {
    bool active = false;
    Dpr::Battle::Logic::BATTLE_SETUP_PARAM::Object* param = nullptr;
    uint16_t lobby = 0;
    int32_t trainer = 0;
    uint32_t fieldFrames = 0;
};
Challenge gChallenge;
}  // namespace

void OnTrainerBattleBegin(void* setupParam, int32_t enemyID0, int32_t enemyID1, int32_t partnerID, int32_t rule) {
    Log("[probe] trainer battle: rule %d, enemies %d / %d, partner %d", rule, enemyID0, enemyID1, partnerID);

    uint16_t lobby = 0;
    uint8_t kind = 0;
    uint16_t target = 0;
    if (gChallenge.active || setupParam == nullptr || !GetStartedLobby(lobby, kind, target)) return;

    auto* param = static_cast<Dpr::Battle::Logic::BATTLE_SETUP_PARAM::Object*>(setupParam);
    param->fields.result = kNotFinished;
    gChallenge = Challenge{true, param, lobby, enemyID0, 0};
    Log("Team challenge: this battle (trainer %d) counts for your lobby %d", enemyID0, lobby);
}

void BattleTick() {
    if (!gChallenge.active) return;
    // Give the field a moment after the battle before reading the result.
    if (++gChallenge.fieldFrames < 30) return;
    int32_t result = gChallenge.param->fields.result;
    if (result == kNotFinished) return;  // battle hasn't run yet (we're in the lead-up)

    bool won = result == kResultWin;
    Log("[probe] battle result code %d -> %s", result, won ? "won" : "lost");
    SendBattleReport(gChallenge.lobby, won, gChallenge.trainer);
    gChallenge = Challenge{};
}

}  // namespace coop
