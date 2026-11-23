# Mega Charizard X & Y 3D Model & Animation Pipeline (Blender -> Unity 2019.4.27f1 -> LumiTool)

This guide follows Team Luminescent's official [LumiTool Guides](https://luminescent.team/rom-hacking/lumitool-guides/introduction) and [Mesh & Texture Replacement Tutorial](https://luminescent.team/rom-hacking/art/mesh_and_texture_replacement) to rig, animate, and package `assets/mega/6-mega-x.glb` and `assets/mega/6-mega-y.glb` into BDSP 1.3.0 UnityFS bundles (`pm0006_01_00` and `pm0006_02_00`).

## Verified BDSP Base Charizard (`pm0006_00_00` + `pm0006_00`) Animation Architecture
Inspecting `Luminescent Platinum 2.2F BD/romfs/.../Pokemon Database/pokemons/battle/pm0006_00_00` and `common/pm0006_00` confirms:
- **Prefab & Skinned Meshes (`battle/pm0006_00_00`)**:
  - `assets/pokemons/battle/pm0006/pm0006_00_00.prefab` (depends on `pokemons/common/pm0006_00`)
  - Skinned renderers: `pm0006_00_00_BodySkin` (`BodyA`, `BodyB`, `LEye`, `REye`), `pm0006_00_00_FireCoreSkin`, `pm0006_00_00_FireMask1Skin`–`3Skin`
  - Custom UV animation controller (`PokemonCustomNodeAnim`): `CusAnimMat_LEye_UV_*`, `CusAnimMat_REye_UV_*`, `CusAnimMat_FireCore_UV_*`, `CusAnimMat_FireMask_UV_*`
- **All 25 AnimationClips (`common/pm0006_00`)**:
  - **Battle**: `ba01_land01`, `ba02_roar01`, `ba10_waita01`, `ba10_waitb01`, `ba20_buturi01`, `ba20_buturi02`, `ba21_tokusyu01`, `ba30_damages01`, `ba41_down01`
  - **Field**: `fi20_walk01`, `fi21_run01`, `fi31_run_wait01`, `fi31_wait_run01`
  - **Camp/Kawaii**: `kw01_wait01`, `kw20_drowsea01`, `kw20_drowseb01`, `kw20_drowsec01`, `kw30_hate01`, `kw32_happyb01`, `kw50_eata01`, `kw50_eatb01`, `kw50_eatc01`
  - **Material/UV Loops**: `eye01`, `loop01`
- **Core Bone Hierarchy**: `Origin`, `Waist`, `Hips`, `Spine1`, `Spine2`, `Neck1`–`Neck3`, `Head`, `Jaw`, `LFeeler1`–`LFeeler6` / `RFeeler1`–`RFeeler6` (wings), `LShoulder`/`RShoulder`, `LArm`/`RArm`, `LForeArm`/`RForeArm`, `LHand`/`RHand`, `LFingerA1`/`B1`/`C1`, `RFingerA1`/`B1`/`C1`, `LThigh`/`RThigh`, `LLeg`/`RLeg`, `LFoot`/`RFoot`, `LToe`/`RToe`, `Tail1`–`Tail7`.

Because `assets/mega/6-mega-y.glb` already uses this exact Pokémon skeleton (with `_0XX` GLTF export suffixes), normalizing its bone names binds directly to all 25 BDSP Charizard animations without re-animating from scratch.

## Required Tools
- **Blender** (`5.x` / `4.x`, for rig normalization, weight transfer, and FBX export)
- **AssetRipper** ([AssetRipper 1.1.8](https://github.com/AssetRipper/AssetRipper/releases/tag/1.1.8) or newer)
- **Unity 2019.4.27f1** ([Unity 2019.4.27f1](https://unity.com/releases/editor/whats-new/2019.4.27)) with **AssetBundle Browser** and **Melonscript**
- **LumiTool** ([SaltContainer/LumiTool](https://github.com/SaltContainer/LumiTool)) and/or **UABEA**
- **BDSP 1.3.0 / Luminescent Platinum 2.2F RomFS** (`battle/pm0006_00_00`, `common/pm0006_00`, `field/pm0006_00_00`)

## Step 1: Blender Rig & Animation-Ready FBX Preparation (Automated)
Run `scripts/prepare_mega_charizard_fbx.py` in headless Blender:
- **Standalone mode** (uses `6-mega-y.glb`'s BDSP-named Pokémon skeleton, strips the junk `Icosphere`, preserves Mega Y's hand-authored weights, transfers weights to Mega X, limits to 4 bone influences/vertex for Unity `SkinnedMeshRenderer`, and unpacks PNG textures):
  ```bash
  blender --background --python scripts/prepare_mega_charizard_fbx.py -- \
    --out-dir build/mega-fbx
  ```
- **With ripped BDSP base Charizard FBX/GLB** (optional, if you want exact BDSP rest-pose bone transforms from AssetRipper):
  ```bash
  blender --background --python scripts/prepare_mega_charizard_fbx.py -- \
    --base-fbx build/ripped-pm0006/pm0006_00_00.fbx \
    --out-dir build/mega-fbx
  ```
Outputs in `build/mega-fbx/`:
- `pm0006_01_00.fbx`, `pm0006_01_00.blend`, `textures/pm0006_01_00/*.png` (Mega Charizard X)
- `pm0006_02_00.fbx`, `pm0006_02_00.blend`, `textures/pm0006_02_00/*.png` (Mega Charizard Y)

## Step 2: Rip Base Charizard Unity Project with AssetRipper
1. Launch `AssetRipper.GUI.Free` and open a folder containing `battle/pm0006_00_00`, `common/pm0006_00`, and `field/pm0006_00_00` from your Luminescent Platinum 2.2F RomFS.
2. Export as a **Unity Project** (Unity `2019.4.27f1`) into `build/ripped-pm0006/unity-project`. This preserves `pm0006_00_00.prefab`, its `RuntimeAnimatorController`, all 25 `.anim` clips, `PokemonCustomNodeAnim`, and `Eff*` locators.

## Step 3: Bind Animations & Build AssetBundles in Unity 2019.4.27f1
1. Open `build/ripped-pm0006/unity-project` in **Unity 2019.4.27f1** (with BDSP precompiled Unity scripts and **AssetBundle Browser** installed).
2. Copy `scripts/unity/MegaCharizardAssetBundleSetup.cs` into `Assets/Editor/`.
3. Copy `build/mega-fbx/pm0006_01_00.fbx`, `build/mega-fbx/pm0006_02_00.fbx`, and `build/mega-fbx/textures/` into `Assets/MegaCharizard/`.
4. Run **`Tools > MegaExtension > Setup Mega Charizard Prefabs & Bundles`**:
   - Clones `pm0006_00_00.prefab` into `pm0006_01_00.prefab` and `pm0006_02_00.prefab`, keeping the `Animator`, `RuntimeAnimatorController` (all 25 Charizard animations), `AnimationPlayer`, `PokeAnimSound`, `PokemonCustomNodeAnim`, and `Eff*` nodes intact.
   - Rebinds `SkinnedMeshRenderer.bones` by name onto BDSP's skeleton and tags the AssetBundle names (`pokemons/battle/pm0006_01_00` and `pokemons/battle/pm0006_02_00`).
5. Preview `ba10_waita01`, `ba02_roar01`, and `ba20_buturi01` in Unity's Animation/Scene view (if the mesh appears tiny, set the FBX `Scale Factor` to `100` or use Melonscript's `Tools > melonscript > Generate Skinned Mesh Component`).
6. Open **Window > AssetBundle Browser** and build the Nintendo Switch / standalone AssetBundles.

## Step 4: Post-Process with LumiTool (and UABEA for Eye/Shadow Tweaks)
1. Run **[LumiTool](https://github.com/SaltContainer/LumiTool)** on the built bundles:
   - **Bundle Prepper**: Convert the Unity-built bundles back into BDSP-compatible UnityFS bundles and reassign dependencies to `pokemons/common/pm0006_00` (or `pm0006_01`/`pm0006_02`).
   - **Shader Bundle PathID Fixer**: Re-link BDSP Pokémon shader/material PathIDs against the vanilla/Lumi shader bundle so materials render properly instead of magenta.
   - **Bundle Renamer**: Verify each bundle's internal preload table name and CAB name match `pokemons/battle/pm0006_01_00` and `pokemons/battle/pm0006_02_00`.
   - **AssetAssistant Manifest**: Refresh the manifest sizes so BDSP loads the updated bundles cleanly.
2. *(Optional visual polish via UABEA)*: Verify `PokemonCustomNodeAnim` eye material PathIDs (`LEye`/`REye`) and `.amb`/normal maps per the [Luminescent Mesh & Texture Replacement Tutorial](https://luminescent.team/rom-hacking/art/mesh_and_texture_replacement).

## Step 5: Stage into `romfs-delta/`
1. Replace the placeholder bundles in `romfs-delta/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons/`:
   - `battle/pm0006_01_00`, `common/pm0006_01` (Mega Charizard X)
   - `battle/pm0006_02_00`, `common/pm0006_02` (Mega Charizard Y)
2. Commit and push `feature/mega-evolution` (or copy directly into `z_MegaExtension/romfs/` in Ryujinx) to test Mega Evolution in battle.
