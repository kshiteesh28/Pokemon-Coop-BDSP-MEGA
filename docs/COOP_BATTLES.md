# In-game team battles: integration plan

Status: **server and app are done; the in-game part is planned here but not built yet.**

| Piece | Status |
|---|---|
| Server lobbies, invites, ready, shared seed, turn relay, timeouts, results, team badges, helpers | ✅ done, tested (`server/internal/coop`) |
| Gyms and Elite Four solo/duo only, Champion up to 4 | ✅ enforced by the server |
| App: open lobby, invite, join, ready, start, report result | ✅ done, tested (`server/internal/launcher`) |
| Game: see others on your map (markers) | ✅ written (`src/coop/ghosts.cpp`), untested in game |
| Game: report caught legendaries to the room | ✅ written (`src/coop/legend.cpp`), untested in game |
| Game: legendary someone else owns → battle without Poké Balls; unclaimed ones released after a failed catch | ✅ written (`src/coop/bridge.cpp` + Mega's `SetupBattleWild`), untested in game |
| Game: real 2-vs-2 battles synced between players | ⏳ steps 1–2 below |

Until the game side exists, a team **opens a lobby in the app**, battles in their own games, and the **host clicks "We won" / "We lost"**. On a win, the server records the badge for everyone in the lobby.

## Why this lives in subsdk2, not subsdk3

The functions a battle sync must hook are **already hooked by the Mega module (subsdk2)**:

| Function | Offset | Used for |
|---|---|---|
| `SetupBattleTrainer` | `0x02c3b800` | Turn a gym or Elite Four fight into a 2-vs-2 multi battle, inject the partner's party, set the shared seed |
| `SetupBattleWild` | `0x02c3abc0` | Ask the server before a legendary encounter; cancel it if someone else owns that species |
| `createPokeAction_FromClientInstruction` | `0x021cdad0` | Capture the local player's chosen move each turn and send it as `BATTLE_ACTION` |
| `setupPokeAction_FromClientInstruction` | `0x021cdbf0` | Feed the partner's move (from `BATTLE_TURN`) into the partner slot instead of the AI |

Two exlaunch trampolines on the same address collide, as `src/mega/main.cpp` notes. So the battle code has to go **inside the existing subsdk2 callbacks**. subsdk3 can expose its network connection to subsdk2 through a small exported C function table, so subsdk2 doesn't need its own socket.

## Steps

1. **Start a team battle**
   - When you talk to a gym leader, an Elite Four member or Cynthia while in a lobby the server has started, read the lobby (kind, target, members, seed) from subsdk3.
   - In `SetupBattleTrainer`, switch the rule to a multi battle, put the partner's team (sent once at `BATTLE_START`, serialised `PokemonParam`s) in the ally slot, and seed the battle RNG with the lobby seed.
   - With every game using the same seed and the same inputs, every game computes the same battle. That's "lockstep".
2. **Exchange moves every turn**
   - `createPokeAction_FromClientInstruction`: when the local player confirms a move, send `BATTLE_ACTION {turn, data}`, where data is the serialised action (move slot / target / switch / item).
   - Wait until `BATTLE_TURN` arrives with everyone's actions, then let the turn run.
   - `setupPokeAction_FromClientInstruction`: for the partner's slot, use their action from `BATTLE_TURN` instead of the AI's choice.
   - An empty action means the partner timed out or disconnected: fall back to the AI for that slot, which the server already allows.
   - At the end, the host sends `BATTLE_RESULT`, and the server records badges for the team.
3. **Legendary guard (done)**
   - Mega's `SetupBattleWild` calls `coop_legend_check` (exported by subsdk3). For a tracked legendary, the co-op client sends `LEGEND_RESERVE` and waits up to 0.8 s. Ordinary Pokémon never wait.
   - If someone else owns it or is battling it, the battle runs with `isCantUseBall = true`: you can fight, but you can't catch it.
   - If it was reserved for you and isn't in your party ~10 s after you're back in the field, it's released (`LEGEND_RESULT escaped`). If it is in your party, the party scan confirms the catch.
   - If subsdk3 isn't installed, the lookup fails once and the Mega module behaves exactly as before.

## How to test (once there's a test loop)

1. Use one real game plus a bot from `coop-sim` that answers lobby messages, to check that the game sends `BATTLE_ACTION` every turn. Watch it with `coop-server -v`.
2. Two real games against the same gym leader: compare HP after every turn. Any difference means the games are out of sync.
3. Unplug one player mid-battle: the other should carry on with the AI controlling the partner.
