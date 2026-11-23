# Mega Charizard X & Y RomFS Delta Bundles

- **`pokemons/battle/pm0006_01_00` & `pm0006_01_01` (Mega Charizard X)**: Switch-targeted UnityFS v7 (`2019.4.27f1`) bundles built from `assets/mega/6-mega-x.glb`, rigged to BDSP's `pm0006_00_00` bone hierarchy in Blender, compiled in Unity 2019.4.27f1 with `MegaCharizardAssetBundleSetup.cs`, and post-processed with `LumiTool.LumiToolConsole bundle-prepper -p -c` (Switch platform + `pokemons/common/pm0006_00` animation dependency).
- **`pokemons/battle/pm0006_02_00` & `pm0006_02_01` (Mega Charizard Y)**: Switch-targeted UnityFS v7 (`2019.4.27f1`) bundles built from `assets/mega/6-mega-y.glb` via the same Blender -> Unity 2019.4.27f1 -> LumiTool pipeline, depending on `pokemons/common/pm0006_00`.
- **`pokemons/field/pm0006_01_*`, `pm0006_02_*`, `pokemons/common/pm0006_01`, `pm0006_02`**: Form 0 Charizard fallback bundles from Luminescent Platinum 2.2F BD RomFS (GPL-3.0-or-later, https://github.com/TeamLumi/Luminescent).
