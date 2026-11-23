# Mega Charizard X & Y 3D Model Pipeline (Luminescent Platinum / LumiTool Workflow)

This guide follows Team Luminescent's official [LumiTool Guides](https://luminescent.team/rom-hacking/lumitool-guides/introduction) and [Mesh & Texture Replacement Tutorial](https://luminescent.team/rom-hacking/art/mesh_and_texture_replacement) to convert `assets/mega/6-mega-x.glb` and `assets/mega/6-mega-y.glb` into BDSP 1.3.0 UnityFS bundles (`pm0006_01_00` and `pm0006_02_00`).

## Required Tools
- **Blender** (for rig transfer & FBX export)
- **Unity 2019.4.27f1** (`https://unity.com/releases/editor/whats-new/2019.4.27`) with **AssetBundle Browser** and **Melonscript**
- **AssetRipper 1.1.8** (`https://github.com/AssetRipper/AssetRipper/releases/tag/1.1.8`)
- **LumiTool** (`https://github.com/SaltContainer/LumiTool`) and/or **UABEA**
- **Your BDSP 1.3.0 RomFS dump** (specifically base Charizard `Pokemon Database/pokemons/battle/pm0006_00_00` and `common/pm0006_00_00`)

## Steps
1. **Extract base Charizard FBX**:
   - Open your BDSP 1.3.0 `pm0006_00_00` bundle in AssetStudio / AssetRipper 1.1.8 and export base Charizard (Mesh + Armature) as `pm0006_00_00.fbx`.
2. **Rig & export Mega Charizard X & Y with the automated Blender script**:
   ```bash
   blender --background --python scripts/prepare_mega_charizard_fbx.py -- \
     --base-fbx /path/to/pm0006_00_00.fbx \
     --out-dir build/mega-fbx
   ```
   - Open `build/mega-fbx/pm0006_01_00.fbx` and `build/mega-fbx/pm0006_02_00.fbx` in Blender if you want to refine weight painting around Mega X's shoulder spikes/wings before Unity import.
3. **Compile BDSP UnityFS bundles in Unity 2019.4.27f1**:
   - Import `pm0006_00_00.fbx`, `pm0006_01_00.fbx`, and `pm0006_02_00.fbx` into a Unity 2019.4.27f1 project containing BDSP's ripped MonoScripts and `Assets/Editor/Melonscript`.
   - Run `Tools > melonscript > Generate Skinned Mesh Component` against `pm0006_00_00_skin` for each Mega model (check scale factor = `100` if small).
   - Duplicate the mesh component for submesh preservation (`Pokemon Textures`), then build the AssetBundles in **Window > AssetBundle Browser**.
4. **Post-process with LumiTool / UABEA**:
   - Post-process the built Unity AssetBundles with **LumiTool** (or copy submesh arrays & import mesh/texture dumps via **UABEA**, verifying `Col0tex` `m_PathID`s, `.amb`, and normal maps per the Luminescent tutorial).
5. **Stage into `romfs-delta/`**:
   - Replace the placeholder files in `romfs-delta/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons/`:
     - `battle/pm0006_01_00`, `common/pm0006_01_00` (Mega Charizard X)
     - `battle/pm0006_02_00`, `common/pm0006_02_00` (Mega Charizard Y)
   - Push `feature/mega-evolution` to rebuild `MegaExtension-Ryujinx` with the new `romfs/`.
