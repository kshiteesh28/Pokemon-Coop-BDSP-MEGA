# Mega Pokémon Architecture, Codebase & Engineering Guide

**Target Platform:** Pokémon Brilliant Diamond 1.3.0 + Luminescent Platinum 2.2F BD (Nintendo Switch / Ryujinx)  
**Branch:** `feature/mega-evolution`  
**Scope:** End-to-end technical reference covering the tech stack, C++/IL2CPP runtime hooks, 3D rigging & Switch AssetBundle pipeline, `Dpr.bin` manifest patching, offline testing, and repeatable steps for adding new Mega Pokémon.

---

## 1. System Architecture Overview

Adding a Mega Pokémon requires four synchronized layers — an ARM64 C++ runtime (`subsdk2`) that hooks BDSP's compiled Unity/IL2CPP battle engine, Switch-native 3D AssetBundles (`platform=38`, LZ4), a patched binary asset manifest (`Dpr.bin`), and an offline verification pipeline:

```text
3D Model (.glb/.fbx) ──▶ Blender + Python Rigging ──▶ MegaBundleTool (C# / AssetsTools.NET) ──▶ romfs-delta (LZ4 + Dpr.bin)
                                                                                                          │
                                                                                                  AssetAssistant Load
                                                                                                          ▼
C++ Hooks (src/mega/main.cpp) ──▶ devkitA64 + ExLaunch CI ──▶ exefs/subsdk2 (NSO) ──────────────▶ BDSP IL2CPP Runtime (Ryujinx)
```

### Tech Stack

| Layer | Technology | Role in This Mod |
| --- | --- | --- |
| **Game Runtime** | Unity 2020.3 + **IL2CPP** (ARM64) | BDSP was written in C# and ahead-of-time compiled by IL2CPP into a single ARM64 Switch binary (`main`). Classes, fields, and methods live at fixed virtual offsets with runtime metadata (`Il2CppClass`, `MethodInfo`, `Il2CppFieldInfo`). |
| **Code Injection** | **C++20** + **ExLaunch** + **devkitA64** | Compiles `src/mega/main.cpp` into `exefs/subsdk2` (Switch NSO binary) using `devkitpro/devkita64` in `.github/workflows/build.yml`. ExLaunch installs ARM64 trampoline hooks at verified IL2CPP offsets. |
| **3D Rigging & Rendering** | **Blender** (headless Python) | Imports 3DS/GLB models, transfers/cleans bone weights, remaps extra bones up the parent chain, aligns anatomical joints, and renders 3-angle Cycles CPU preview contact sheets. |
| **AssetBundle Surgery** | **C# (.NET)** + **AssetsTools.NET** | `scripts/tools/MegaBundleTool/Program.cs` surgically injects custom `Mesh`, `m_BindPose`, `Texture2D`, and `Material` data into a copy of the species' vanilla Switch bundle (`platform=38`, LZ4). |
| **Asset Manifest** | **Python** (NRBF binary parser) | `scripts/patch_dpr_manifest.py` patches Unity's `.NET BinaryFormatter` (`Dpr.bin`) manifest with exact byte sizes and `common/pmXXXX_00` dependency IDs. |
| **Offline Verification** | **Python** + **Blender** + **C#** | `scripts/test_mega_rig.py` validates Switch binary safety, bindpose parity, vertex weight sums, CPU skinning bounds, and visual renders before booting Ryujinx. |

---

## 2. C++ Runtime Deep Dive (`src/mega/main.cpp`)

### 2.1 Coexisting with Luminescent Platinum (`subsdk2` vs `subsdk9`)
Luminescent Platinum's own ExLaunch binary ships as `subsdk9`. Our Mega Evolution runtime compiles as **`subsdk2`**, so both binaries load side-by-side:
- **Immediate hooks (`exl_main`)**: Non-conflicting battle and item hooks (`ItemData$$GetParam`, `BUIWazaList`, `CalcActionPriority$$Execute`, `ProcessActionCore$$action`, `Section_StoreActions`, `SetupBattleWild`, `SetupBattleTrainer`).
- **Lazy shared hooks (`installSharedPostInitHooks`)**: Triggered once on the first `ItemData$$GetParam` call so our trampolines chain cleanly after `subsdk9` finishes initializing (`BTL_POKEPARAM`, `CoreParam`, `PersonalSystem`, `DataManager$$GetPokemonCatalog`, `ItemIcon$$LoadSprite`, `MessageManager`).

### 2.2 Mega Stones, Key Items & Bag UI (Zero UI Hooks)
- **`patchItemTableInMemory()`**: Patches only the Mega item rows (`Charizardite X = 660`, `Charizardite Y = 678`, `Mega Ring = 696`, `Mega Bracelet = 744`, `Key Stone = 773`) once in BDSP's loaded `XLSXContent::ItemTable` using `Charcoal (249)` (holdable item in Items pocket) and `Magma Stone (458)` (non-registerable Key Item) as templates. We never hook `UIBag`, `ItemWork::GetItemInfo`, or `UIText`, keeping Bag A/B/X/Y input 100% vanilla.
- **`CheckUnbreakablePokeItem` (`0x01d0d780`)**: Returns `true` for `(species, megaStone)` so Knock Off, Trick, Thief, and Fling cannot remove a Mega Stone.
- **`ItemIcon$$LoadSprite` (`0x01ca7100`)**: Loads custom sprites (`item_0660`, `item_0678`, `item_0696`, etc.) from `SpriteAtlasID::TEXTUREMASS`, falling back to `Fire Stone (82)` or `Odd Keystone (111)` if an icon is missing.
- **`MessageManager$$GetNameMessage` (`0x0210ce50`) & `GetSimpleMessage` (`0x0210d000`)**: Supply Bag names/descriptions and override BDSP's placeholder battle strings (`BTL_STRID_SET::MegaEvoStart = 1572`, `MegaEvo = 1576`).

### 2.3 Battle UI Toggle & Priority Action Queue
1. **Button Polling (`BUIWazaList$$OnUpdate`, `0x01d2c490`)**: Polls `R / L / X / + / StickL / StickR` *before* calling `Orig` so vanilla UI does not swallow the press. **Never bind `ZL` or `ZR`** because vanilla BDSP maps them to Confirm (`A`).
2. **Action Flag Bit (`BUIWazaList$$OnSubmit`, `0x01d2c6e0`)**: Stores the armed Mega state in unused **bit 34** (`1ULL << 34`) of `BTL_ACTION_PARAM.raw`.
3. **Instruction Splitting (`createPokeAction_FromClientInstruction`, `0x021cdad0`)**: When bit 34 is set, enqueues a `PokeActionCategory::Mega_Evolution` action first, clears bit 34 on a local copy of the instruction, and enqueues the normal move action second.
4. **Priority (`CalcActionPriority$$Execute`, `0x021ad570`)**: Assigns `dominantPri = 2, operationPri = 2, wazaPri = 0, agility = 0` so Mega Evolution resolves before standard moves.

### 2.4 Executing Mega Evolution (`executeCharizardMegaEvolution`)
When `ProcessActionCore$$action` (`0x021c0de0`) encounters `PokeActionCategory::Mega_Evolution`, it runs four stages in order:

1. **Start Message (`BTL_STRID_SET::MegaEvoStart = 1572`)**
   - Executes `Section_FromEvent_Message` with `isDisplayTokuseiWindow = false` to display `"[Charizard]'s Charizardite X/Y is reacting to your Mega Ring!"`.
2. **Native Form Change (`Section_FromEvent_FormChange`, `BTL_STRID_SET::MegaEvo = 1576`)**
   - Plays BDSP's built-in light-burst transformation (`isDisplayChangeEffect = true`), sets `isDontResetFormByOut = true` so the Pokémon stays Mega across switches, and keeps `isDisplayTokuseiWindow = false` so the pre-Mega ability (e.g., Blaze) does not flash mid-evolution.
3. **Zero-Leak In-Place `EventFactor` Hot-Swap (`swapAbilityEventFactorInPlace`)**
   - BDSP's battle engine dispatches ability hooks via `EventFactor` nodes stored in `BattleEnv::m_eventFactorContainer`.
   - **`resolveAbilityHandlerTable(tokuseiNo, &outPrio)`** scans BDSP's statically verified `Handler::Tokusei::GET_FUNC_TABLE` (with `system_load_typeinfo(0x8a5b)`), invokes the ability's native `ADD_*` delegate (e.g., `ADD_Hideri` for Drought, `ADD_KataiTume` for Tough Claws), and obtains BDSP's own singleton `EventHandlerTable::Array*` — **zero heap allocations and zero memory leaks**.
   - **`swapAbilityEventFactorInPlace`** safely scans `BattleEnv::m_eventFactorContainer` (checking IL2CPP `CLASS` / `SZARRAY` type tags and `"EventFactor"` class names with bounded loops), finds the Pokémon's active Ability `EventFactor`, and updates `handlerTable`, `numHandlers`, `subID`, `priority`, and clears `work[]` in place.
   - **Why we do NOT use `Section_FromEvent_ChangeTokusei`:** `Section_FromEvent_ChangeTokusei` is BDSP's Skill Swap / Entrainment / Mummy section. Calling it with `userPokeID == targetPokeID` emits two empty ability banners and can silently fail to write a 2-byte `uint16_t tokusei` field via 4-byte reflection. Hot-swapping `EventFactor::Data` directly is silent (0 extra popups) and deterministic.
4. **Native Switch-In Event (`EventID::MEMBER_IN = 108` + `MEMBER_IN_COMP = 111`)**
   - Pushes `EventVar::Label::POKEID = param->GetID()` and fires `EventID::MEMBER_IN` (`108`) followed by `EventID::MEMBER_IN_COMP` (`111`) via `EventSystem::CallEvent`.
   - Weather setters (Drought, Drizzle, Sand Stream, Snow Warning), terrain setters, and Intimidate listen to **`MEMBER_IN` (`108`)**, while post-entry abilities (Zen Mode) listen to **`MEMBER_IN_COMP` (`111`)**.
   - Result for Drought: **exactly 1** official Drought banner, immediate 3D sunlight brightening, `"The sunlight turned harsh!"`, and 5-turn Harsh Sunlight.

### 2.5 Stats, Typing, Model Loading & Clean Reversion
- **`PersonalSystem$$GetPersonalData` (`0x024a0ca0`) & `PersonalSystem$$LoadPersonalData` (`0x024a0d50`):**
  - Vanilla BDSP's `PersonalTable` has no Mega Charizard rows, and Luminescent Platinum changes base Charizard (`form == 0`) to Fire/Dragon ([Luminescent Platinum Pokédex](https://luminescent.team/pokedex/charizard)).
  - Hooking both `GetPersonalData` and `LoadPersonalData` (updating `PersonalSystem::s_PersonalData`) enforces Fire/Flying on base Charizard (`form 0`), Fire/Dragon + `78/130/111/130/85/100` + Tough Claws + `110.5 kg` on Mega Charizard X (`form 1`), and Fire/Flying + `78/104/78/159/115/100` + Drought + `100.5 kg` on Mega Charizard Y (`form 2`).
- **Battle & Party Type Hooks:**
  - `BTL_POKEPARAM$$GetPokeType` (`0x01fdfa80`), `BTL_POKEPARAM$$IsMatchType` (`0x01fdfad0`), `CoreParam$$GetType1` (`0x0204a9f0`), and `CoreParam$$GetType2` (`0x0204aaf0`) guarantee consistent STAB, type matchups, and Battle Info UI badges.
  - `makeDualTypePair()` dynamically probes `PokeTypePair::MakePure` + `PokeTypePair::Split` once at runtime to verify BDSP's exact bit-shift layout while preserving `typeEx` (Trick-or-Treat / Forest's Curse).
- **Model Catalog Hook (`DataManager$$GetPokemonCatalog`, `0x02cc76a0`):**
  - Clones base Charizard's `SheetCatalog` via `il2cpp_object_new` and rewrites `AssetBundleName` to `"pm0006_01_00"` or `"pm0006_02_00"`. Without this, BDSP falls back to Ditto (`pm0132_00_00`).
  - **ARM64 ABI rule:** Must accept and forward the 6th parameter `const void* methodInfo` (register `x5`) to `Orig(...)`, or IL2CPP crashes on lookup.
- **Reversion on Faint or Battle End:**
  - `ProcessActionCore$$action` checks `megaBpp->IsDead()` after every action to revert `formNo` and `tokusei` immediately when Mega Charizard faints.
  - `isBattleEnded()` checks `BattleViewCore::get_Instance() == nullptr` (crash-safe without uninitialized `BattleProc` typeinfo) and calls `revertMegaFormInParty()` to restore base form, original ability, and Fire/Flying typing after battle.

### 2.6 Generic vs Species-Specific Code
- **Already 100% generic (works for any Mega Pokémon & ability):** `resolveAbilityHandlerTable`, `swapAbilityEventFactorInPlace`, `EventID::MEMBER_IN` / `MEMBER_IN_COMP` triggering, `makeDualTypePair`, and the bit-34 action queue / priority pipeline.
- **Currently Charizard-specific:** `charizardMegaForm`, `kMegaX`/`kMegaY`, and the `monsNo == kCharizardSpecies` guards inside the hooks. Scaling to all 48 Megas only requires replacing those checks with a `constexpr MegaSpeciesSpec kMegaRegistry[]` table (`species, megaStoneItemId, targetForm, stats, ability, type1, type2, weight, bundleName, strings`).

---

## 3. 3D Rigging, Switch AssetBundles & `Dpr.bin` Pipeline

Aligned with the [Luminescent Platinum Mesh & Texture Replacement Tutorial](https://luminescent.team/rom-hacking/art/mesh_and_texture_replacement) and [LumiTool Guides](https://luminescent.team/rom-hacking/lumitool-guides/introduction):

1. **Exact Vanilla `m_Bones` & `m_BindPose`:** Never compute bindposes as `Inv(prefabWorld)` — BDSP prefabs store bones like `Jaw` and `Tongue` pre-posed (16.68° off on Charizard), flipping the jaw upside down during `AnimationClip` playback. Always copy `m_Bones` and `m_BindPose` verbatim from the species' vanilla `battle/pmXXXX_00_00`.
2. **Parent-Chain Bone Remapping (Zero `Origin` Fallbacks):** Walk extra 3DS bones (`Tongue`, `Tail8`, `LToeA/B/C`, `End*`) up their Blender armature parent chain until hitting a vanilla BDSP bone. Sum duplicate weights per vertex, cap at 4 influences, and normalize to `1.0`.
3. **Anatomical Joint Alignment:** Scale by `Head.Y - minY`, ground `minY` to vanilla `vanMinY`, and anchor `Z` depth 70% on `Head.Z` + 30% on `Feet.Z` (never bounding-box center `Z`, which shifts the jaw hinge backward when a Mega has a longer tail or wings).
4. **Surgical Switch Bundle Injection (`platform=38`):** Start from a copy of vanilla `battle/pmXXXX_00_00` and inject only `Mesh`, `RGBA32` Repeat `Texture2D`, and `Material` data:
   - `TargetPlatform == 38` (Switch), `0` embedded `Shader` assets (reference vanilla `fileID = 1`), exact vanilla `MonoBehaviour` byte sizes, unique `CAB-` archive name per bundle, LZ4 compression, and a shiny slot copy (`pmXXXX_YY_01`).
5. **`Dpr.bin` NRBF Manifest Patching:** Register every new bundle in `romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr.bin` with its exact LZ4 byte size and `common/pmXXXX_00` dependency IDs via `scripts/patch_dpr_manifest.py`.

---

## 4. Repeatable Workflow for Adding a New Mega Pokémon

1. **Collect IDs & 3D Assets:** Place the Mega model (`.glb`/`.fbx`) in `assets/mega/` and look up the National Dex ID (`pmXXXX`), `formNo` (`1` or `2`), Mega Stone item ID (`src/mod/data/items.h`), Ability ID, base stats, types, and weight.
2. **Add the Species Entry in `src/mega/main.cpp`:** Register the species, Mega Stone, stats, typing, ability, bundle name, and strings. The ability hot-swap and `MEMBER_IN` trigger require zero changes.
3. **Rig & Align in Blender:** Remap extra bones up the parent chain onto the vanilla species skeleton, align landmarks (`Head`, `Jaw`, `Feet`), clean any weight bleed, and zero out untextured 3DS stencil submeshes if BDSP already renders particles for that effect.
4. **Rebuild Switch Bundle & Patch `Dpr.bin`:**
   ```bash
   python3 scripts/test_mega_rig.py --forms pmXXXX_YY_00 --rebuild
   ```
5. **Pass the Offline Verification Gate:** Verify `ALL PASSED` (platform=38, 0 shaders, exact bindpose parity, valid weights, CPU skinning bounds) and visually inspect `build/test-renders/pmXXXX_YY_00_preview.png` (roar stress pose, 3/4 battle idle, side profile).
6. **Commit, Push & Test in Ryujinx:** Commit `src/mega/main.cpp`, `pmXXXX_YY_{00,01}`, and `Dpr.bin`, then push to `feature/mega-evolution` (or `main`) to trigger `.github/workflows/build.yml`, which compiles `subsdk2`, verifies the NSO via `scripts/verify_nso.py`, and uploads `MegaExtension-Ryujinx`.

---

## References
- [Luminescent Platinum — Mesh & Texture Replacement Tutorial](https://luminescent.team/rom-hacking/art/mesh_and_texture_replacement)
- [Luminescent Platinum — LumiTool Guides](https://luminescent.team/rom-hacking/lumitool-guides/introduction)
- [Luminescent Platinum Pokédex — Charizard](https://luminescent.team/pokedex/charizard)
