# Mega Evolution Engineering Playbook (BDSP 1.3.0 + Luminescent Platinum 2.2F)

**Audience**: Mod engineers adding new Mega Evolutions to `Pokemon-Coop-version` (`feature/mega-evolution`).  
**Goal**: Make every new Mega Pokémon a repeatable, crash-free, first-try implementation verified offline before ever booting Ryujinx.

---

## 1. System Architecture & Layering Rules

Our mod runs as a standalone layer on top of **Pokémon Brilliant Diamond 1.3.0** + **Luminescent Platinum 2.2F BD**:

| Layer | Location | Responsibility |
| --- | --- | --- |
| **Runtime Code (`subsdk2`)** | `src/mega/main.cpp` | ARM64 `exlaunch` hooks for Mega Stones/Key Items, Battle UI toggle, `PokeActionCategory::Mega_Evolution`, `DataManager$$GetPokemonCatalog`, battle messages, and post-evolution Ability activation. |
| **3D Models (`romfs-delta`)** | `romfs-delta/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons/battle/pmXXXX_YY_{00,01}` | Switch-native (`platform=38`) LZ4 AssetBundles surgically built from each species' vanilla `battle/pmXXXX_00_00` template. |
| **Asset Manifest (`Dpr.bin`)** | `romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr.bin` | Pure-Python NRBF patch registering every new `pmXXXX_YY_00` bundle with exact byte sizes and `common/pmXXXX_00` dependency IDs. |
| **Offline Verification Suite** | `scripts/test_mega_rig.py` + `scripts/tools/MegaBundleTool/` | Universal automated verifier + headless Blender Cycles CPU visual renderer for all Mega species. |

### Non-Negotiable Architecture Rules
1. **Never collide with Luminescent Platinum's `subsdk9`**: All Mega code lives in `subsdk2`. Shared hooks (`CheckUnbreakablePokeItem`, `DataManager$$GetPokemonCatalog`, `ItemIcon$$LoadSprite`, `MessageManager$$GetSimpleMessage`, `MessageManager$$GetNameMessage`) are installed once in `installSharedPostInitHooks()` triggered lazily on first `ItemData$$GetParam` call.
2. **Never hook `UIBag`, `ItemWork::GetItemInfo`, or `UIText`**: Patch `XLSXContent::ItemTable` rows once in memory (`patchItemTableInMemory`) so Bag A/B/X/Y input remains 100% vanilla.
3. **Never ship Unity-built desktop bundles directly to Switch**: Always inject mesh, texture, and material data into the species' vanilla Switch `battle/pmXXXX_00_00` bundle (see Section 5).
4. **Never test a new 3D rig in Ryujinx before `scripts/test_mega_rig.py` passes 100% and its rendered PNG contact sheet passes visual review.**

---

## 2. Phase 1 — Scaling C++ Code (`src/mega/main.cpp`) for Any New Mega Pokémon

### 2.1 Convert Single-Species Constants into a Data-Driven Table
When adding a second Mega Pokémon (e.g., Mega Venusaur `pm0003`, Mega Blastoise `pm0009`, Mega Gengar `pm0094`, Mega Lucario `pm0448`), replace single-species checks with a `constexpr` registry:

```cpp
struct MegaSpeciesSpec {
    uint16_t species;          // National Dex # (e.g., 6 = Charizard, 94 = Gengar, 448 = Lucario)
    uint8_t  targetForm;       // FormNo in PersonalTable / AssetBundle (1 = Mega / Mega X, 2 = Mega Y)
    uint16_t megaStoneItemId;  // Verified index in src/mod/data/items.h
    uint16_t megaAbilityId;    // BDSP Tokusei ID (e.g., 181 = Tough Claws, 70 = Drought, 23 = Shadow Tag)
    const char* bundleName;    // Exact battle bundle name, e.g. "pm0006_01_00"
    const char* stoneName;     // e.g. "Charizardite X"
    const char* megaName;      // e.g. "Mega Charizard X"
    const char* abilityMsg;    // e.g. "Mega Charizard Y's Drought intensified the sun's rays!"
};
```

Lookup helpers (`findMegaSpec(species, formNo, heldItem)` and `findMegaSpecByForm(species, formNo)`) then drive **every** hook automatically with zero per-species copy-paste:
- `CheckUnbreakablePokeItem` (`0x01d0d780`): returns `true` for any `(species, megaStoneItemId)` in the table so Knock Off / Trick / Fling / Thief cannot strip a Mega Stone.
- `patchItemTableInMemory()`: copies `kCharcoal` (`249`, holdable item in Items pocket, `field_func = 0`) onto every Mega Stone row and `kFireStone` (`82`) as its fallback `iconid`. Copies `kMagmaStone` (`458`, non-registerable Key Item) onto Mega Ring (`696`), Mega Bracelet (`744`), and Key Stone (`773`).
- `ItemIcon$$LoadSprite` (`0x01ca7100`): loads `"item_XXXX"` / `"item_XXXX_L"` from `SpriteAtlasID::TEXTUREMASS` if present, otherwise falls back cleanly to `kFireStone` (`82`) or `kOddKeystone` (`111`).
- `lookupMegaItemTextByLabel`: returns item name and Bag description for every registered Mega Stone ID.

### 2.2 `DataManager$$GetPokemonCatalog` (`0x02cc76a0`) — Critical ARM64 ABI Rule
If `DataManager$$GetPokemonCatalog` cannot find `(monsNo, formNo)`, BDSP silently falls back to **Ditto (`pm0132_00_00`)**.

When hooking `0x02cc76a0`:
```cpp
HOOK_DEFINE_TRAMPOLINE(DataManager$$GetPokemonCatalog) {
    static XLSXContent::PokemonInfo::SheetCatalog::Object* Callback(
        int32_t monsNo, int32_t formNo, Pml::Sex sex, bool isRare, bool isEgg, const void* methodInfo) {
        if (const auto* spec = mega::findMegaSpecByForm(monsNo, formNo); spec != nullptr) {
            auto* base = Orig(monsNo, mega::kBaseForm, sex, isRare, isEgg, methodInfo);
            if (base != nullptr && base->klass != nullptr) {
                auto* clone = reinterpret_cast<XLSXContent::PokemonInfo::SheetCatalog::Object*>(
                    il2cpp_object_new(reinterpret_cast<Il2CppClass*>(base->klass)));
                if (clone != nullptr) {
                    std::memcpy(&clone->fields, &base->fields,
                                sizeof(XLSXContent::PokemonInfo::SheetCatalog::Fields));
                    clone->fields.FormNo = formNo;
                    clone->fields.Rare = isRare;
                    clone->fields.AssetBundleName = System::String::Create(spec->bundleName);
                    return clone;
                }
            }
        }
        return Orig(monsNo, formNo, sex, isRare, isEgg, methodInfo);
    }
};
```
> **Minute Detail (Crash Prevention)**: You **MUST** include `const void* methodInfo` as the 6th parameter (ARM64 register `x5`) and forward it to `Orig(...)`. Omitting `methodInfo` leaves garbage in `x5` and crashes IL2CPP on the first catalog lookup.

### 2.3 Battle UI & Action Queue Rules
- **Button Polling (`BUIWazaList$$OnUpdate`, `0x01d2c490`)**: Poll the Mega toggle **before** calling `Orig(...)` so vanilla UI input does not consume the press. **Never bind `ZL` or `ZR`** — vanilla BDSP maps `ZL`/`ZR` to Confirm (`A`).
- **Action Flag Bit**: Store the armed Mega state in bit `34` (`kMegaFlagBit = 34`, `1ULL << 34`) of `BTL_ACTION_PARAM::Object::fields.raw`.
- **Action Priority (`CalcActionPriority$$Execute`, `0x021ad570`)**: Assign `dominantPri = 2, operationPri = 2, wazaPri = 0, agility = 0` so Mega Evolution executes before standard moves.
- **Instruction Splitting (`createPokeAction_FromClientInstruction`, `0x021cdad0`)**: When bit 34 is set, enqueue a `PokeActionCategory::Mega_Evolution` action first, clear bit 34 on a local copy of `BTL_ACTION_PARAM`, and enqueue the normal move action second.

### 2.4 Exact 3-Stage Battle Execution & Message Interception
Execute these three stages in strict order inside `executeMegaEvolution`:

1. **Stage 1 — Start Sentence (`BTL_STRID_SET::MegaEvoStart = 1572`)**
   - `msgDesc->fields.isDisplayTokuseiWindow = false;`
   - **Minute Detail**: Vanilla BDSP 1.3.0 ships `1572` as an empty `".."` placeholder in `english_dp_btl_set`. You **must** intercept `1572` / `megaevostart` in `MessageManager$$GetSimpleMessage` (`0x0210d000`).
2. **Stage 2 — Native Form Change Animation (`Section_FromEvent_FormChange`, `BTL_STRID_SET::MegaEvo = 1576`)**
   - `formDesc->fields.isDontResetFormByOut = true;` (stays Mega after switching out)
   - `formDesc->fields.isDisplayTokuseiWindow = false;` (**Critical**: setting this `true` pops up the *pre-Mega* ability such as Blaze during evolution!)
   - `formDesc->fields.isDisplayChangeEffect = true;` (plays BDSP's native light-burst transformation)
3. **Stage 3 — Post-Evolution Ability Activation (`BTL_STRID_SET::MegaEvo_Y = 1577`)**
   - Immediately after `FormChange::Execute` returns, write the Mega ability to **both**:
     - `param->fields.m_tokusei = spec->megaAbilityId;`
     - `param->fields.m_coreParam->fields.defaultTokusei = spec->megaAbilityId;`
   - Execute `Section_FromEvent_Message` with `isDisplayTokuseiWindow = true` and `BTL_STRID_SET::MegaEvo_Y` (`1577`).

> **Minute Detail (Substring Collision Bug in `MessageManager$$GetSimpleMessage`)**: Always check `1572`/`megaevostart` **first**, `1577`/`megaevo_y` **second**, and `1576`/`megaevo` **last**. Both `"megaevostart"` and `"megaevo_y"` contain the substring `"megaevo"` — checking `"megaevo"` first silently breaks the start and ability messages.

---

## 3. Phase 2 — 3D Rigging, Skinning & BindPose Engineering (Luminescent Platinum Standard)

This is where 90% of subtle 3D animation bugs originate. Follow the [Luminescent Platinum Mesh & Texture Replacement Tutorial](https://luminescent.team/rom-hacking/art/mesh_and_texture_replacement) and these exact mathematical invariants:

### 3.1 The Golden Rule: Exact Vanilla Skeleton + Exact Vanilla `m_BindPose`
In Unity linear blend skinning, every vertex $v$ is deformed at runtime by:

$$v_{\text{skinned}}(t) = \sum_{k=0}^{3} w_k \left( M_{\text{bone}_{i_k}}^{\text{anim}}(t) \cdot B_{i_k} \right) v$$

- **NEVER compute `B_i = Inv(prefabTransformWorld[i])`**. In BDSP prefabs, certain bones are stored pre-posed rather than in bind pose (for example, in vanilla `pm0006_00_00`, `Jaw [06]` and `Tongue [07]` differ from `Inv(prefabWorld)` by **16.68°**). Computing `Inv(prefabWorld)` flips the jaw and tongue upside down above the snout during every `AnimationClip`.
- **ALWAYS copy the exact `m_Bones` array and exact `m_BindPose` array verbatim from the species' vanilla `battle/pmXXXX_00_00` + `common/pmXXXX_00` `BodySkin`** (this is mathematically identical to Luminescent Platinum's Melonscript *Generate Skinned Mesh Component*).

### 3.2 Anatomical Parent-Chain Bone Remapping (Zero `Origin` Fallbacks)
3DS/GLB Mega models often contain extra bones (`Tongue`, `Tail8`, `LToeA/B/C`, `LFingerD1/D2`, `End*`) that do not exist in BDSP's base `pmXXXX_00_00` skeleton.
- **NEVER map unmapped bones to `Origin` `(0, 0, 0)`**: Pinning vertices to `Origin` nails them to the arena floor, creating vertical spikes from wrists/jaws whenever the Pokémon breathes or roars.
- **ALWAYS walk each unmapped bone up its Blender armature parent chain** (`Tongue -> Jaw`, `Tail8 -> Tail7`, `LToeA/B/C -> LToe`, `EndLFoot -> LFoot`) until hitting a bone in vanilla `pmXXXX_00_00`'s `m_Bones` list.
- When multiple weights on a single vertex collapse to the same vanilla bone index, **sum their weights, keep at most 4 unique influences per vertex, and renormalize so $\sum w_k = 1.0$**.

### 3.3 Anatomical Joint Alignment (Never Bounding-Box Z Centering)
- **NEVER align a Mega mesh by its bounding-box center `origCenter.Z`**: A longer tail, wing, or crest shifts the bounding-box center backward, which pushes the body/head/jaw forward off BDSP's `Waist`, `Head`, and `Jaw` rotation hinges. A 0.45 m lever-arm error on `Jaw` swings the lower jaw above the eyes during idle breathing and pitches the torso into the floor during roar animations.
- **ALWAYS align vertices in BDSP meter space by anatomical landmarks**:
  1. **Scale**: Match `Head.Y - minY` to vanilla `pmXXXX_00_00`'s `Head.Y - vanMinY`, clamped within `[0.85×, 1.30×]` of vanilla total height.
  2. **Vertical (`Y`)**: Ground `minY` to vanilla `vanMinY` (supports both grounded and hovering/flying species).
  3. **Depth (`Z`)**: Anchor `70%` on `Head.Z` + `30%` on `Feet.Z` so the jaw hinge and torso pivot sit directly on BDSP's `Jaw` and `Waist` bones.
  4. **Update both `Mesh.m_LocalAABB` and `SkinnedMeshRenderer.m_AABB`** to the baked BDSP-space bounds so Unity never frustum-culls the model.

### 3.4 Per-Model Geometry & Weight Hygiene
- **Untextured 3DS effect shells**: 3DS models frequently include untextured stencil submeshes for flames/auras (e.g., Mega Charizard Y submeshes 3–4). If BDSP's vanilla prefab already renders that effect via particles (`FireCore`/`FireMask`), disable those submeshes by setting `indexCount = 0` so they do not render as dark blobs.
- **Unrigged Mega meshes (`DATA_TRANSFER`)**: When transferring weights from a donor rig onto an unrigged mesh (e.g., Mega Charizard X), nearest-surface transfer can bleed wing/arm weights (`LFeeler*`, `LArm`, `LHand`) onto cheek flames, horns, or head crests. Always inspect a synthetic **roar / wing-flap stress pose** in the offline test renderer to catch weight bleed before booting Ryujinx.

---

## 4. Phase 3 — Switch AssetBundle Binary Invariants (Zero-Ryujinx-Crash Checklist)

Unity Editor on macOS/Windows produces bundles that **crash Ryujinx on load or on Mega Evolution** due to:
1. Stripped 32-byte `MonoBehaviour` stubs from AssetRipper,
2. Embedded macOS/DirectX `Shader` assets,
3. Broken `m_FileID` externals (`file=1/2`),
4. Desktop-only compressed textures (DXT1/BC1).

### Mandatory Surgical Injection Procedure
Start from a copy of the species' vanilla Switch bundle (`battle/pmXXXX_00_00`) and inject **only** your custom `Mesh`, `Texture2D`, and `Material` data while preserving every vanilla Switch structural invariant:

| Invariant | Required Value | Why It Prevents Crashes / Visual Bugs |
| --- | --- | --- |
| `TargetPlatform` | `38` (Nintendo Switch) | Non-Switch platform tags fail deserialization on Switch/Ryujinx. |
| Embedded `Shader` count | `0` | Materials must reference the vanilla Switch Pokémon shader in `Externals[1]` (`fileID = 1`). |
| `MonoBehaviour` byte sizes | Exact match to vanilla `pmXXXX_00_00` | Preserves `BattlePokemonEntity`, `PokemonCustomNodeAnim`, and `PokemonPrefabInfo` verbatim. |
| `Externals` | Exact match to vanilla `pmXXXX_00_00` (`fileID=1` shaders/anims, `fileID=2` `common/pmXXXX_00`) | Ensures all vanilla `AnimationClip`s and shaders resolve cleanly. |
| `Texture2D` format & wrap | `RGBA32` (`fmt=4`), `WrapU = Repeat (0)`, `WrapV = Repeat (0)` | Switch-safe uncompressed format; `Repeat` matches Luminescent Platinum's UV tiling spec. |
| Archive `CAB-` name | Globally unique per bundle (different from `pmXXXX_00_00` and from sibling forms) | Duplicate CAB names cause Unity's AssetBundle cache to collide or crash on load. |
| Root `GameObject.m_Name` | Exact form ID (`pmXXXX_YY_00`) | Matches Luminescent Platinum's custom form convention (`pm0006_15_00`). |
| Compression | LZ4 (`AssetBundleCompressionType.LZ4`) | Matches BDSP's native streaming loader. |
| Shiny slot | Copy `pmXXXX_YY_00` -> `pmXXXX_YY_01` (or provide shiny textures) | Prevents Ditto fallback when a shiny Pokémon Mega Evolves. |

---

## 5. Phase 4 — Patching `Dpr.bin` (NRBF Asset Manifest)

Every new bundle placed under `romfs-delta/Data/StreamingAssets/AssetAssistant/` **must** be registered in `Dpr.bin` or BDSP's `AssetAssistant` will refuse to load it (silently falling back to Ditto) or crash on size mismatch.

Run the pure-Python NRBF patcher after **every** bundle edit:

```bash
python3 scripts/patch_dpr_manifest.py \
  --source "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant/Dpr.bin" \
  --delta-root "romfs-delta/Data/StreamingAssets/AssetAssistant" \
  --output "romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr.bin"
```

### `Dpr.bin` Invariants
- Sequential NRBF record IDs (`0x00042F00+`), zero duplicate keys.
- Exact 4-byte little-endian file byte length matching the final LZ4-packed bundle on disk.
- `deps_id` inherited from base `pokemons/battle/pmXXXX_00_00` so `common/pmXXXX_00` (all 24+ animations) loads automatically alongside the Mega bundle.

---

## 6. Phase 5 — Mandatory Offline Automated Verification Gate

**Never boot Ryujinx to discover a broken rig, flipped jaw, wrong scale, or missing `Dpr.bin` entry.** Run the universal offline test suite after every change:

```bash
# 1. Rebuild from Blender + BuiltBundles, patch Dpr.bin, verify all invariants, and render 3-angle PNGs
python3 scripts/test_mega_rig.py --forms pmXXXX_YY_00 --rebuild

# 2. Verify ALL Mega forms currently in romfs-delta (auto-discovered)
python3 scripts/test_mega_rig.py

# 3. Fast CI check without PNG rendering
python3 scripts/test_mega_rig.py --no-render
```

### What `scripts/test_mega_rig.py` Automatically Verifies (Per Species)
1. **Switch Binary Safety**: `TargetPlatform == 38`, `Shaders == 0`, exact vanilla `Externals` count, exact vanilla `MonoBehaviour` byte sizes, and exact `Dpr.bin` byte size.
2. **Luminescent Platinum Rig Parity**: Exact `m_Bones.Count` and `m_BindPose.Count` matching vanilla `pmXXXX_00_00`, with `maxErr < 1e-4` across all bindpose matrices.
3. **Mesh & Weight Hygiene**: Zero `NaN` vertices, all active bone indices in `[0..N-1]`, all vertex weight sums equal to `1.0`.
4. **CPU Linear-Blend Skinning & Anatomical Assertions**:
   - Grounding / hover alignment (`minY` within `[vanMinY - 0.20, vanMinY + 0.30]`)
   - Proportionate height (`[0.65×, 1.60×]` of vanilla `pmXXXX_00_00` height)
   - `Jaw.Y < Head.Y` and `Tongue.Y < Head.Y` (whenever `Head`/`Jaw`/`Tongue` exist on that species)
5. **Visual Contact Sheet (`build/test-renders/pmXXXX_YY_00_preview.png`)**:
   - Headless Blender Cycles CPU render with auto-framed camera showing **Left**: Roar / Wing-or-Arm stress pose, **Center**: Battle Idle 3/4 view, **Right**: Pure Side Profile.
   - **Manual Visual Gate**: Open the PNG and confirm: (a) jaw is below snout, (b) no spikes/stretched vertices in the roar pose, (c) feet sit cleanly on the ground plane, (d) no dark untextured stencil blobs.

---

## 7. Quick-Start Checklist for Adding a New Mega Pokémon

1. **Source Assets**: Place the Mega model (`.glb`/`.fbx`) in `assets/mega/` and note its species ID (`pmXXXX`), target form (`YY`), Mega Stone item ID, and Mega Ability ID from `src/mod/data/items.h` and Luminescent Platinum's `PersonalTable`.
2. **Code (`src/mega/main.cpp`)**: Add one entry to the Mega species table (stone ID, form, ability, bundle name, item/battle strings). Verify `subsdk2` compiles cleanly via CI or local toolchain and passes `scripts/verify_nso.py`.
3. **3D Prep**: Export `<formId>.blend` + Unity `BuiltBundles/pokemons/battle/<formId>` and create the initial Switch bundle from vanilla `battle/pmXXXX_00_00` with unique `CAB-` name, `RGBA32` Repeat textures, and vanilla shader references.
4. **Lumi 56-Bone Rebuild + Manifest**: Run `python3 scripts/test_mega_rig.py --forms pmXXXX_YY_00 --rebuild` to bake anatomical alignment, exact vanilla `m_Bones` + `m_BindPose`, shiny slot copy, and `Dpr.bin` registration.
5. **Offline Visual Gate**: Confirm `ALL PASSED` in terminal AND visually inspect `build/test-renders/pmXXXX_YY_00_preview.png`.
6. **Commit & Push**: Commit `src/mega/main.cpp`, the `pmXXXX_YY_{00,01}` bundles, and `Dpr.bin`, then push to trigger the GitHub Actions release build.

---

## References
- [Luminescent Platinum — Mesh & Texture Replacement Tutorial](https://luminescent.team/rom-hacking/art/mesh_and_texture_replacement)
- [Luminescent Platinum — LumiTool Guides](https://luminescent.team/rom-hacking/lumitool-guides/introduction)
- [SaltContainer/LumiTool](https://github.com/SaltContainer/LumiTool)
