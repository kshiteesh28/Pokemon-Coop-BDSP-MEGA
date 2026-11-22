# Build, package, and test guide

Scope: ExeFS builds from this fork and local testing with a matching Pokémon Brilliant Diamond 1.3.0 + Luminescent Platinum RomFS setup.

**Compatibility finding for the current local install:** `Luminescent Platinum 2.2F BD` contains `exefs/subsdk1` and `exefs/94CEAE325C205C4B9D6F7235552F28FD.ips`. This source tree's `config.cmake.template` sets module ID 9, so its Ryujinx artifact is not yet a verified drop-in replacement. The installed 2.2F RomFS has no `Data/ExtraData` directory, although the disabled Mega implementation expects `Data/ExtraData/Features/features.json` and `Data/ExtraData/NotDoing/RequiredItem.json`. Resolve code/content version and loader compatibility before replacing the working install. See [MEGA_EVOLUTION.md](MEGA_EVOLUTION.md) for the feature work.

## Record the baseline first

Before changing hooks, record the game edition/version and executable build ID, Lumi content release, this fork's commit SHA, runtime/emulator version, and a backup save. An ExeFS build can succeed even when its offsets or RomFS dependencies are wrong. Run the unmodified fork once to establish a bootable baseline.

The fork's `config.cmake.template` defines a Diamond variant with title ID `0100000011D90000` and module ID 9. Its `Dockerfile` uses `devkitpro/devkita64:20260219`, configures CMake, and builds `RomBase_release_all`. The `RomBase` target name is part of the present build setup; changing it requires updating Docker and Actions paths. The actual package includes `main.npdm` and `subsdk9` according to [Lumi's ExeFS guide](https://luminescent.team/rom-hacking/exefs/guide). Do not assume `subsdk1` from the earlier design draft.

## GitHub Actions build (no local compiler required)

1. Open this fork's **Actions → Build**. If GitHub asks, enable workflows.
2. Select **Run workflow** and choose `feature/mega-evolution`, or push a commit to that branch. The branch's workflow builds on pushes and has `workflow_dispatch`.
3. Open the run and confirm **Configure Release**, **Build Release**, and **Upload Release artifact** succeeded. For debugging, check the parallel Debug steps.
4. Download `exefs_release` or `exefs_debug` from the run's **Artifacts** section. Choose **exefs_ryujinx_Release** or **exefs_ryujinx_Debug** for Ryujinx. GitHub's [artifact download guide](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts) explains the UI.
5. Extract the Ryujinx artifact and inspect its directory tree before installing it.

This branch corrects the pull request trigger to `master`. The branch's workflow uploads only Ryujinx artifacts and has no nightly release job.

## Optional local Docker build

From a clone with submodules:

~~~sh
git clone --recurse-submodules https://github.com/kshiteesh28/Pokemon-Coop-version.git
cd Pokemon-Coop-version
git switch feature/mega-evolution
git submodule update --init --recursive
docker buildx build --target artifacts --output type=local,dest=./build .
~~~

For debug:

~~~sh
docker buildx build --build-arg BUILD_TYPE=Debug \
  --target artifacts --output type=local,dest=./build .
~~~

The Dockerfile exports `build/atmosphere`, `build/modmanager`, `build/ryujinx`, and `build/yuzu`. This route needs Docker/Buildx on the local machine. The GitHub Action instead uses a Linux runner and the devkitA64 container; it does not invoke the Dockerfile. On Apple Silicon, test the container build before relying on it.

## What the artifact does and does not contain

The ExLaunch build packages code hooks and executable patches. It does **not** generate the full Lumi RomFS or add Mega form data, item records, models, messages, and feature JSON. Prepare a separate matching RomFS overlay from the content sources and asset tools. Keep the game dump, keys, saves, and non-distributable assets out of this repository and public Actions artifacts. Link each ExeFS artifact to the exact RomFS revision used for testing.

## Install and test locally

1. Back up the current working mod directory and save. Keep a clean BD 1.3.0 baseline.
2. Confirm the game edition, title ID, and game update match the package. Confirm the Lumi RomFS release matches the code's data expectations.
3. Extract the artifact. Inspect its paths; copy the packaged mod to the runtime's documented mod directory while preserving the nested structure. Avoid two active ExeFS packages that patch the same game.
4. Boot. If it fails, revert to the known-good package and compare game build ID, package files, hook offsets, and logs.
5. Test a normal wild battle and trainer battle, save and reload, then the single Mega species described in the feature guide. Use a separate test save.
6. Record whether the visible form, ability, typing, stats, HP, icon, model, battle messages, and postbattle party form match expectations.
7. Test a second Mega attempt, wrong stone, no Key Stone, switch, faint, escape, item interactions, and save/reload.
8. Preserve the artifact name, commit SHA, RomFS revision, runtime version, and logs with each result.

A successful Actions run proves **compilation and packaging only**. Gameplay compatibility requires the local runtime tests above. If adding new hooks, follow [Lumi's hook guide](https://luminescent.team/rom-hacking/exefs/hooks) and verify every offset against the exact executable build before installing.

## Troubleshooting order

- **Build fails:** inspect the first compiler error; check submodules, new headers, and CMake source discovery.
- **No artifact:** inspect the upload path and `RomBase_release_all` output; add `if-no-files-found: error` when changing the workflow.
- **Game does not boot:** restore baseline, verify edition/build ID and duplicate ExeFS mods, then inspect new hook offsets.
- **Mega option absent:** verify the feature JSON enabled “Mega Evolution,” the code's install calls are active, required-item JSON resolves, Key Stone and held item IDs match, and UI asset hierarchy is correct.
- **Transformation crashes or wrong form:** verify IL2CPP header layout, action encoding, form ID, personal data, `PokemonInfo`, and Unity bundle resolution.
- **Normal battles regress:** disable the Mega feature and bisect newly enabled hooks, keeping one changed hook per test stage.
