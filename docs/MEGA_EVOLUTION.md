# Mega Evolution implementation guide

Target: Pokémon Brilliant Diamond 1.3.0 with a **matched** Luminescent Platinum RomFS and this ExLaunch fork. This document is an implementation plan, not a claim that Mega Evolution currently works. [TeamLumi's FAQ](https://luminescent.team/docs/faq) says Megas are research work and are not planned for Lumi. A working Charizard X/Y implementation requires a compatible RomFS content set in addition to ExeFS code. Start with one existing species and one form; Charizard X is a useful test because this tree already has an X/Y form choice.

## What this fork already contains

- `src/mod/features/mega_evolution.cpp` has a proposed Mega/Ultra Burst implementation, but the entire implementation is inside a block comment, and every `InstallAtOffset` call in `exl_mega_evolution_main()` is commented out. Merely enabling the “Mega Evolution” feature will do nothing.
- `src/mod/features/main.cpp` calls `exl_mega_evolution_main()` only when “Mega Evolution” is enabled by `rom:/Data/ExtraData/Features/features.json`. `src/mod/data/features.h` contains the feature name; `src/mod/features/features.h` declares its initializer. Those three files need inspection, but need no change if their current wiring is retained.
- `src/mod/data/megas.h` is an allowlist of species IDs. It does not provide forms, stones, stats, models, or battle behavior.
- `src/mod/data/items.h` is a positional name-to-ID lookup. It already names Mega Stones and Key Stone. Do not append a duplicate name or assume a name proves the corresponding RomFS item is valid.
- The proposed code reads `rom:/Data/ExtraData/NotDoing/RequiredItem.json`, writes a bit in `BTL_ACTION::PARAM::raw`, uses `Section_FromEvent_FormChange`, and uses `FLAG_MEGA_EVOLUTION_UNAVAILABLE`. Each dependency and its lifetime must be verified on the exact 1.3.0 executable before enabling hooks.

Lumi's ExeFS guide explains the Ghidra → header → hook → build → test workflow. Its headers guide says clean definitions belong in `src/mod/externals`; its hooks guide documents replace, trampoline, and inline hooks. See [ExeFS guide](https://luminescent.team/rom-hacking/exefs/guide), [headers](https://luminescent.team/rom-hacking/exefs/headers), and [hooks](https://luminescent.team/rom-hacking/exefs/hooks).

## Required files and assets for one Mega form

| Location | Required action |
| --- | --- |
| `src/mod/features/mega_evolution.cpp` | Restore and revise the existing implementation in small stages; confirm offsets/signatures and add safety checks. |
| `src/mod/data/megas.h` | Add the species ID only if absent. Charizard is already listed. |
| `src/mod/data/items.h` | Verify stone and Key Stone indices match the installed RomFS data; edit only for genuinely new items and preserve positional IDs. |
| `src/mod/externals/Dpr/Battle/Logic/*`, `src/mod/externals/Dpr/Battle/View/*` | Update or add only the class/field/method declarations actually needed, from 1.3.0 IL2CPP analysis. Check layout before dereferencing. |
| `src/mod/features/main.cpp`, `features.h`, `data/features.h` | Verify existing feature registration; edit only if the feature is split or renamed. |
| RomFS `Data/ExtraData/Features/features.json` | Enable the exact “Mega Evolution” feature key in your matching Lumi content set. Inspect the real schema before editing; this ExLaunch fork does not contain that JSON. |
| RomFS `Data/ExtraData/NotDoing/RequiredItem.json` | Supply the species → `megaFormItem` mapping expected by the proposed code. Inspect the actual file and parser; the filename/path is taken from currently disabled code and may change. |
| RomFS personal/master data | Add or verify the target form's stats, type, ability, and form indexing. Use the matching Lumi data pipeline; verify resulting IDs with an in-game data probe. |
| RomFS item/master data and text | Verify/add stone and Key Stone item records, localization, icons, obtainability, and held-item flags. |
| RomFS `PokemonInfo` and Unity bundles | Register the target form's model/icon/animations/cry and confirm asset paths. Use only assets you can distribute. |
| Optional RomFS encounter/trainer/event data | Grant the stone or make the Mega Pokémon available for a reproducible test; no event edits are inherently required by the battle mechanic. |

The ExLaunch repository is **ExeFS source**, not the RomFS source. Exact paths of the personal table, item table, `PokemonInfo`, and model bundles depend on the selected Lumi content release. Do not invent or overwrite paths: list the matching release's RomFS, identify the asset actually loaded, and record it in a per-species checklist. Lumi's [LumiTool guide](https://luminescent.team/rom-hacking/lumitool-guides/introduction) covers Unity 2019.4.27f1, AssetRipper, AssetBundles, and postprocessing with LumiTool; it does not itself supply a ready-made Mega Pokémon package.

## Implementation sequence

1. Pin the BD 1.3.0 executable build ID, Lumi RomFS release, fork commit, and emulator version. Back up a working save.
2. Inventory the chosen species and stone across the matching RomFS. Verify unique species/form/item IDs. For Charizard X, check form 1, Charizardite X, Key Stone, and a loadable battle model.
3. Run an unchanged fork build and boot it with that RomFS. Keep a known-good package for comparison.
4. Inspect `mega_evolution.cpp` and the 1.3.0 binary in Ghidra. For **each** proposed offset, confirm the target function, signature, caller, original instruction if inline, and whether another Lumi hook occupies it. Record findings in a hook map. Do not simply uncomment the old offsets.
5. First enable only a read-only/logging UI hook to prove the selected Pokémon, item, feature flag, and asset mapping. Next add eligibility; then action encoding; then form change; then battle cleanup. Rebuild and test after each stage.
6. Keep transient transformation state owned by the battle. Verify whether the existing `FlagWork` flag is battle scoped and reset for each trainer/side. If not, replace it with explicit battle-local state. Reject duplicate requests and recheck eligibility at execution time.
7. Verify when form change updates type, ability, stats, HP, visual entity, icon, and messages. Handle switch, faint, end of battle, save/reload, and item loss. Preserve the prebattle form rather than always forcing form 0.
8. Add more Mega species only after the one-species path passes the test matrix.

## Pseudocode: data, eligibility, and action flow

This is **pseudocode**, not a drop-in C++ patch or a verified ABI.

~~~cpp
struct MegaRule {
    SpeciesId species;
    FormId originalForm;
    ItemId requiredStone;     // or an explicitly supported move rule
    FormId megaForm;
};

optional<MegaRule> FindMegaRule(SpeciesId species, FormId form, ItemId item);
bool CanMega(Battle& battle, TrainerId trainer, Pokemon& poke) {
    auto rule = FindMegaRule(poke.species, poke.form, poke.heldItem);
    return rule && poke.hp > 0 && battle.ruleAllowsMega &&
           battle.trainer(trainer).hasKeyStone &&
           !battle.trainer(trainer).megaUsed &&
           battle.assets.formAvailable(rule->megaForm);
}

// UI hook: show control only when CanMega is true.
// On submission, attach a mod-owned MegaIntent to the selected move.
// If reusing a spare action bit, prove it is unused for this build and
// survives every copy/serialization path. Never add fields to a game
// object without verifying its exact layout.
void OnCommandSubmitted(Battle& battle, Command& command) {
    if (command.megaRequested &&
        !CanMega(battle, command.trainer, command.pokemon))
        command.megaRequested = false;
}

// At the verified battle phase, before the move executes:
void ExecuteMega(Battle& battle, Command& command) {
    if (!command.megaRequested) return;
    auto& poke = battle.pokemon(command.slot);
    if (!CanMega(battle, command.trainer, poke)) return;
    auto rule = *FindMegaRule(poke.species, poke.form, poke.heldItem);
    battle.originalForm[command.slot] = poke.form;
    battle.formChangeSection.execute(poke, rule.megaForm);
    // Confirm engine applies ability/stats/HP/visuals; fill gaps explicitly.
    battle.trainer(command.trainer).megaUsed = true;
}

// On battle completion, before writing party state to save:
void RestorePersistentForm(Battle& battle, Pokemon& poke) {
    if (battle.originalForm.contains(poke.slot))
        poke.form = battle.originalForm[poke.slot];
}
~~~

For co-op later, send a validated Mega intent in the battle command protocol and compare resulting battle state on both clients. Do not serialize raw IL2CPP objects or assume equal RNG seeds guarantee identical outcomes.

## Acceptance tests

- Baseline non-Mega battle is unchanged.
- Correct species + stone + Key Stone shows the control; missing or wrong item does not.
- Both forms of a two-Mega species choose the correct target.
- One use per intended trainer/side; a second Pokémon cannot Mega when the limit is spent.
- Battle stats, typing, ability, HP, model, icon, and text agree with the target form.
- Switch, faint, item interaction, escape, battle end, and save/reload preserve a valid party.
- Wild and trainer battles behave as designed; unsupported battle modes reject Mega cleanly.
- Debug logs identify eligibility and transform failures without crashing.

Keep a test record with game build ID, content version, commit SHA, stone ID, species/form IDs, screenshots or logs, and pass/fail notes. Compilation and installation steps are in [BUILD_AND_TEST.md](BUILD_AND_TEST.md).
