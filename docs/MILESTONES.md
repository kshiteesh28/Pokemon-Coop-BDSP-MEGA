# Mega Charizard delivery milestones

## Milestone 1: battle mechanics with proxy visuals

Target: Brilliant Diamond 1.3.0, Luminescent Platinum 2.2F, Ryujinx/Ryubing. This milestone is complete only when Charizard with the correct stone can transform to form 1 or 2 in battle, use the corresponding stats, type and ability, then revert without save leakage. Existing Charizard model bundles can be used as temporary visuals.

The `MegaExtension` target builds `subsdk2` separately from Lumi's `subsdk1`. The checked-in proxy bundles are byte-for-byte copies of Lumi 2.2F Charizard form 0 battle/field/common bundles under candidate form 1/2 paths. The branch now includes a patched `personal_masterdatas` bundle enabling the existing dormant rows 1014 (Mega X) and 1015 (Mega Y). The patcher validates the exact installed Lumi 2.2F bundle SHA256 and each row's expected stats, types and ability. The proxy bundles do not alter `PokemonInfo` or make Mega Evolution playable by themselves. The current C++ module contains rule constants only; no battle hooks are installed. The existing `src/mod/features/mega_evolution.cpp` prototype is wholly commented out and refers to types absent from current headers. It must be adapted, compiled and tested against the 1.3.0 executable.

`scripts/verify_nso.py` checks the output NSO's entry point, segment structure and MOD0 reference. It passed on the locally installed Lumi `subsdk1`. The installed Lumi IPS32 patch has 155 records; none overlaps the eleven hook offsets proposed in the disabled prototype, which does not rule out runtime hook collisions.

## Milestone 2: custom visuals

After Milestone 1 boots and the mechanics pass battle and save tests, replace proxy visuals with properly rigged, animated Mega Charizard X/Y BDSP Unity bundles. The downloaded GLBs are source references, not game-ready bundles. Conversion must preserve the BDSP bundle's serialized dependencies and shader/animation contracts.

## Build output

`subsdk2/Dockerfile` and `.github/workflows/build.yml` use `devkitpro/devkita64:latest`, CMake and Ninja, and produce `MegaExtension/exefs/subsdk2`. The workflow packages `romfs-delta` alongside it. The workflow must complete successfully before this output can be called a testable build. Do not install the package over the Lumi 2.2F files or claim the mechanics are ready from a successful compile alone.
