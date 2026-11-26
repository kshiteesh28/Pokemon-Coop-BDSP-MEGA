#include "exlaunch.hpp"

#include "externals/BTL_STRID_SET.h"
#include "externals/FlagWork.h"
#include "externals/FlagWork_Enums.h"
#include "externals/GameController.h"
#include "externals/PlayerWork.h"
#include "externals/SpriteAtlasID.h"
#include "externals/Dpr/Battle/Logic/ActPri.h"
#include "externals/Dpr/Battle/Logic/BATTLE_SETUP_PARAM.h"
#include "externals/Dpr/Battle/Logic/BTL_ACTION_PARAM.h"
#include "externals/Dpr/Battle/Logic/BTL_POKEPARAM.h"
#include "externals/Dpr/Battle/Logic/BtlRule.h"
#include "externals/Dpr/Battle/Logic/BtlStrType.h"
#include "externals/Dpr/Battle/Logic/Common.h"
#include "externals/Dpr/Battle/Logic/PokeAction.h"
#include "externals/Dpr/Battle/Logic/PokeTypePair.h"
#include "externals/Dpr/Battle/Logic/PokeActionCategory.h"
#include "externals/Dpr/Battle/Logic/PokeActionContainer.h"
#include "externals/Dpr/Battle/Logic/SVCL_ACTION.h"
#include "externals/Dpr/Battle/Logic/BattleEnv.h"
#include "externals/Dpr/Battle/Logic/EventFactor.h"
#include "externals/Dpr/Battle/Logic/EventID.h"
#include "externals/Dpr/Battle/Logic/EventVar.h"
#include "externals/Dpr/Battle/Logic/EventSystem.h"
#include "externals/Dpr/Battle/Logic/Handler/Tokusei.h"
#include "externals/Dpr/Battle/Logic/Section.h"
#include "externals/Dpr/Battle/Logic/ServerCommandPutter.h"
#include "externals/Dpr/Battle/Logic/SectionContainer.h"
#include "externals/Dpr/Battle/Logic/Section_CalcActionPriority.h"
#include "externals/Dpr/Battle/Logic/Section_FromEvent_FormChange.h"
#include "externals/Dpr/Battle/Logic/Section_FromEvent_Message.h"
#include "externals/Dpr/Battle/Logic/Section_ProcessActionCore.h"
#include "externals/Dpr/Battle/Logic/Section_StoreActions.h"
#include "externals/Dpr/Battle/View/BattleViewCore.h"
#include "externals/Dpr/Battle/View/BtlvInput.h"
#include "externals/Dpr/Battle/View/Systems/BattleViewSystem.h"
#include "externals/Dpr/Battle/View/UI/BUIButtonBase.h"
#include "externals/Dpr/Battle/View/UI/BUIWazaButton.h"
#include "externals/Dpr/Battle/View/UI/BUIWazaList.h"
#include "externals/Dpr/Message/MessageManager.h"
#include "externals/Dpr/UI/UIManager.h"
#include "externals/GameData/DataManager.h"
#include "externals/Dpr/UI/UIText.h"
#include "externals/Pml/PmlUse.h"
#include "externals/Pml/Item/ItemData.h"
#include "externals/Pml/Item/ItemManager.h"
#include "externals/Pml/Personal/PersonalSystem.h"
#include "externals/Pml/PokeParty.h"
#include "externals/SmartPoint/AssetAssistant/AssetManager.h"
#include "externals/System/Action.h"
#include "externals/System/String.h"
#include "externals/UnityEngine/Color.h"
#include "externals/UnityEngine/Component.h"
#include "externals/UnityEngine/GameObject.h"
#include "externals/UnityEngine/Transform.h"
#include "externals/UnityEngine/UI/Image.h"
#include "externals/XLSXContent/ItemTable.h"

#include "mega_registry.hpp"

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace mega {
constexpr int kCharizardSpecies = 6;
constexpr int kAbsolSpecies = 359;
constexpr int kBaseForm = 0;
constexpr int kMegaXForm = 1;
constexpr int kMegaYForm = 2;
constexpr int kMegaAbsolForm = 1;
constexpr int kMegaAbsolZForm = 2;

// Verified item indices from src/mod/data/items.h (line - 4).
constexpr uint16_t kSunStone = 80;        // Fallback BDSP stone icon sprites
constexpr uint16_t kMoonStone = 81;
constexpr uint16_t kFireStone = 82;
constexpr uint16_t kThunderStone = 83;
constexpr uint16_t kWaterStone = 84;
constexpr uint16_t kLeafStone = 85;
constexpr uint16_t kShinyStone = 107;
constexpr uint16_t kDuskStone = 108;
constexpr uint16_t kDawnStone = 109;
constexpr uint16_t kOddKeystone = 111;    // Fallback BDSP keystone icon sprite
constexpr uint16_t kCharcoal = 249;       // Holdable item in Items pocket (field_func = 0)
constexpr uint16_t kMagmaStone = 458;     // Non-registerable Key Item (field_func = 0)
constexpr uint16_t kAbsoliteZ = 622;      // Safe unused ItemTable slot ("622", < kKeyStone)
constexpr uint16_t kCharizarditeX = 660;
constexpr uint16_t kAbsolite = 677;
constexpr uint16_t kCharizarditeY = 678;
constexpr uint16_t kMegaRing = 696;
constexpr uint16_t kMegaBracelet = 744;
constexpr uint16_t kKeyStone = 773;
constexpr uint16_t kIceStone = 849;

struct MegaStoneCatalogEntry {
    uint16_t itemId;
    uint16_t fallbackIconId;
    const char* stoneName;
    const char* stoneDesc;
};

// All 92 official Mega Stones (47 Gen VI XY/ORAS + 45 Gen IX Legends: Z-A / Mega Dimension DLC).
// Every itemId is strictly < 1278 (safe within BDSP ItemTable and SAV8BS.MaxItemID).
constexpr MegaStoneCatalogEntry kAllMegaStones[] = {
    // --- Gen VI: Pokémon X & Y (30) ---
    { 656, kDuskStone,    "Gengarite",      "One of a variety of mysterious Mega Stones. Have Gengar hold it, and this stone will enable it to Mega Evolve during battle." },
    { 657, kShinyStone,   "Gardevoirite",   "One of a variety of mysterious Mega Stones. Have Gardevoir hold it, and this stone will enable it to Mega Evolve during battle." },
    { 658, kThunderStone, "Ampharosite",    "One of a variety of mysterious Mega Stones. Have Ampharos hold it, and this stone will enable it to Mega Evolve during battle." },
    { 659, kLeafStone,    "Venusaurite",    "One of a variety of mysterious Mega Stones. Have Venusaur hold it, and this stone will enable it to Mega Evolve during battle." },
    { 660, kFireStone,    "Charizardite X", "One of a variety of mysterious Mega Stones. Have Charizard hold it, and this stone will enable it to Mega Evolve during battle." },
    { 661, kWaterStone,   "Blastoisinite",  "One of a variety of mysterious Mega Stones. Have Blastoise hold it, and this stone will enable it to Mega Evolve during battle." },
    { 662, kMoonStone,    "Mewtwonite X",   "One of a variety of mysterious Mega Stones. Have Mewtwo hold it, and this stone will enable it to Mega Evolve during battle." },
    { 663, kShinyStone,   "Mewtwonite Y",   "One of a variety of mysterious Mega Stones. Have Mewtwo hold it, and this stone will enable it to Mega Evolve during battle." },
    { 664, kFireStone,    "Blazikenite",    "One of a variety of mysterious Mega Stones. Have Blaziken hold it, and this stone will enable it to Mega Evolve during battle." },
    { 665, kSunStone,     "Medichamite",    "One of a variety of mysterious Mega Stones. Have Medicham hold it, and this stone will enable it to Mega Evolve during battle." },
    { 666, kFireStone,    "Houndoominite",  "One of a variety of mysterious Mega Stones. Have Houndoom hold it, and this stone will enable it to Mega Evolve during battle." },
    { 667, kMoonStone,    "Aggronite",      "One of a variety of mysterious Mega Stones. Have Aggron hold it, and this stone will enable it to Mega Evolve during battle." },
    { 668, kDuskStone,    "Banettite",      "One of a variety of mysterious Mega Stones. Have Banette hold it, and this stone will enable it to Mega Evolve during battle." },
    { 669, kDuskStone,    "Tyranitarite",   "One of a variety of mysterious Mega Stones. Have Tyranitar hold it, and this stone will enable it to Mega Evolve during battle." },
    { 670, kFireStone,    "Scizorite",      "One of a variety of mysterious Mega Stones. Have Scizor hold it, and this stone will enable it to Mega Evolve during battle." },
    { 671, kSunStone,     "Pinsirite",      "One of a variety of mysterious Mega Stones. Have Pinsir hold it, and this stone will enable it to Mega Evolve during battle." },
    { 672, kMoonStone,    "Aerodactylite",  "One of a variety of mysterious Mega Stones. Have Aerodactyl hold it, and this stone will enable it to Mega Evolve during battle." },
    { 673, kDawnStone,    "Lucarionite",    "One of a variety of mysterious Mega Stones. Have Lucario hold it, and this stone will enable it to Mega Evolve during battle." },
    { 674, kIceStone,     "Abomasite",      "One of a variety of mysterious Mega Stones. Have Abomasnow hold it, and this stone will enable it to Mega Evolve during battle." },
    { 675, kSunStone,     "Kangaskhanite",  "One of a variety of mysterious Mega Stones. Have Kangaskhan hold it, and this stone will enable it to Mega Evolve during battle." },
    { 676, kWaterStone,   "Gyaradosite",    "One of a variety of mysterious Mega Stones. Have Gyarados hold it, and this stone will enable it to Mega Evolve during battle." },
    { 677, kDuskStone,    "Absolite",       "One of a variety of mysterious Mega Stones. Have Absol hold it, and this stone will enable it to Mega Evolve during battle." },
    { 678, kFireStone,    "Charizardite Y", "One of a variety of mysterious Mega Stones. Have Charizard hold it, and this stone will enable it to Mega Evolve during battle." },
    { 679, kSunStone,     "Alakazite",      "One of a variety of mysterious Mega Stones. Have Alakazam hold it, and this stone will enable it to Mega Evolve during battle." },
    { 680, kDawnStone,    "Heracronite",    "One of a variety of mysterious Mega Stones. Have Heracross hold it, and this stone will enable it to Mega Evolve during battle." },
    { 681, kShinyStone,   "Mawilite",       "One of a variety of mysterious Mega Stones. Have Mawile hold it, and this stone will enable it to Mega Evolve during battle." },
    { 682, kThunderStone, "Manectite",      "One of a variety of mysterious Mega Stones. Have Manectric hold it, and this stone will enable it to Mega Evolve during battle." },
    { 683, kDawnStone,    "Garchompite",    "One of a variety of mysterious Mega Stones. Have Garchomp hold it, and this stone will enable it to Mega Evolve during battle." },
    { 684, kShinyStone,   "Latiasite",      "One of a variety of mysterious Mega Stones. Have Latias hold it, and this stone will enable it to Mega Evolve during battle." },
    { 685, kDawnStone,    "Latiosite",      "One of a variety of mysterious Mega Stones. Have Latios hold it, and this stone will enable it to Mega Evolve during battle." },
    // --- Gen VI: Pokémon Omega Ruby & Alpha Sapphire (17) ---
    { 752, kWaterStone,   "Swampertite",    "One of a variety of mysterious Mega Stones. Have Swampert hold it, and this stone will enable it to Mega Evolve during battle." },
    { 753, kLeafStone,    "Sceptilite",     "One of a variety of mysterious Mega Stones. Have Sceptile hold it, and this stone will enable it to Mega Evolve during battle." },
    { 754, kDuskStone,    "Sablenite",      "One of a variety of mysterious Mega Stones. Have Sableye hold it, and this stone will enable it to Mega Evolve during battle." },
    { 755, kShinyStone,   "Altarianite",    "One of a variety of mysterious Mega Stones. Have Altaria hold it, and this stone will enable it to Mega Evolve during battle." },
    { 756, kDawnStone,    "Galladite",      "One of a variety of mysterious Mega Stones. Have Gallade hold it, and this stone will enable it to Mega Evolve during battle." },
    { 757, kShinyStone,   "Audinite",       "One of a variety of mysterious Mega Stones. Have Audino hold it, and this stone will enable it to Mega Evolve during battle." },
    { 758, kDawnStone,    "Metagrossite",   "One of a variety of mysterious Mega Stones. Have Metagross hold it, and this stone will enable it to Mega Evolve during battle." },
    { 759, kWaterStone,   "Sharpedonite",   "One of a variety of mysterious Mega Stones. Have Sharpedo hold it, and this stone will enable it to Mega Evolve during battle." },
    { 760, kWaterStone,   "Slowbronite",    "One of a variety of mysterious Mega Stones. Have Slowbro hold it, and this stone will enable it to Mega Evolve during battle." },
    { 761, kMoonStone,    "Steelixite",     "One of a variety of mysterious Mega Stones. Have Steelix hold it, and this stone will enable it to Mega Evolve during battle." },
    { 762, kSunStone,     "Pidgeotite",     "One of a variety of mysterious Mega Stones. Have Pidgeot hold it, and this stone will enable it to Mega Evolve during battle." },
    { 763, kIceStone,     "Glalitite",      "One of a variety of mysterious Mega Stones. Have Glalie hold it, and this stone will enable it to Mega Evolve during battle." },
    { 764, kShinyStone,   "Diancite",       "One of a variety of mysterious Mega Stones. Have Diancie hold it, and this stone will enable it to Mega Evolve during battle." },
    { 767, kFireStone,    "Cameruptite",    "One of a variety of mysterious Mega Stones. Have Camerupt hold it, and this stone will enable it to Mega Evolve during battle." },
    { 768, kSunStone,     "Lopunnite",      "One of a variety of mysterious Mega Stones. Have Lopunny hold it, and this stone will enable it to Mega Evolve during battle." },
    { 769, kDawnStone,    "Salamencite",    "One of a variety of mysterious Mega Stones. Have Salamence hold it, and this stone will enable it to Mega Evolve during battle." },
    { 770, kThunderStone, "Beedrillite",    "One of a variety of mysterious Mega Stones. Have Beedrill hold it, and this stone will enable it to Mega Evolve during battle." },
    // --- Gen IX: Pokémon Legends: Z-A & Mega Dimension DLC (45) ---
    { 837,  kMoonStone,    "Clefablite",     "One of a variety of mysterious Mega Stones. Have Clefable hold it, and this stone will enable it to Mega Evolve during battle." },
    { 838,  kLeafStone,    "Victreebelite",  "One of a variety of mysterious Mega Stones. Have Victreebel hold it, and this stone will enable it to Mega Evolve during battle." },
    { 839,  kWaterStone,   "Starminite",     "One of a variety of mysterious Mega Stones. Have Starmie hold it, and this stone will enable it to Mega Evolve during battle." },
    { 840,  kSunStone,     "Dragoninite",    "One of a variety of mysterious Mega Stones. Have Dragonite hold it, and this stone will enable it to Mega Evolve during battle." },
    { 848,  kLeafStone,    "Meganiumite",    "One of a variety of mysterious Mega Stones. Have Meganium hold it, and this stone will enable it to Mega Evolve during battle." },
    { 859,  kWaterStone,   "Feraligite",     "One of a variety of mysterious Mega Stones. Have Feraligatr hold it, and this stone will enable it to Mega Evolve during battle." },
    { 867,  kMoonStone,    "Skarmorite",     "One of a variety of mysterious Mega Stones. Have Skarmory hold it, and this stone will enable it to Mega Evolve during battle." },
    { 868,  kIceStone,     "Froslassite",    "One of a variety of mysterious Mega Stones. Have Froslass hold it, and this stone will enable it to Mega Evolve during battle." },
    { 869,  kFireStone,    "Heatranite",     "One of a variety of mysterious Mega Stones. Have Heatran hold it, and this stone will enable it to Mega Evolve during battle." },
    { 870,  kDuskStone,    "Darkranite",     "One of a variety of mysterious Mega Stones. Have Darkrai hold it, and this stone will enable it to Mega Evolve during battle." },
    { 871,  kFireStone,    "Emboarite",      "One of a variety of mysterious Mega Stones. Have Emboar hold it, and this stone will enable it to Mega Evolve during battle." },
    { 897,  kSunStone,     "Excadrite",      "One of a variety of mysterious Mega Stones. Have Excadrill hold it, and this stone will enable it to Mega Evolve during battle." },
    { 898,  kDuskStone,    "Scolipite",      "One of a variety of mysterious Mega Stones. Have Scolipede hold it, and this stone will enable it to Mega Evolve during battle." },
    { 899,  kDuskStone,    "Scraftinite",    "One of a variety of mysterious Mega Stones. Have Scrafty hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1058, kThunderStone, "Eelektrossite",  "One of a variety of mysterious Mega Stones. Have Eelektross hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1059, kDuskStone,    "Chandelurite",   "One of a variety of mysterious Mega Stones. Have Chandelure hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1060, kLeafStone,    "Chesnaughtite",  "One of a variety of mysterious Mega Stones. Have Chesnaught hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1061, kFireStone,    "Delphoxite",     "One of a variety of mysterious Mega Stones. Have Delphox hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1062, kWaterStone,   "Greninjite",     "One of a variety of mysterious Mega Stones. Have Greninja hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1063, kFireStone,    "Pyroarite",      "One of a variety of mysterious Mega Stones. Have Pyroar hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1064, kShinyStone,   "Floettite",      "One of a variety of mysterious Mega Stones. Have Floette hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1065, kDuskStone,    "Malamarite",     "One of a variety of mysterious Mega Stones. Have Malamar hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1066, kWaterStone,   "Barbaracite",    "One of a variety of mysterious Mega Stones. Have Barbaracle hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1067, kDuskStone,    "Dragalgite",     "One of a variety of mysterious Mega Stones. Have Dragalge hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1068, kSunStone,     "Hawluchanite",   "One of a variety of mysterious Mega Stones. Have Hawlucha hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1069, kLeafStone,    "Zygardite",      "One of a variety of mysterious Mega Stones. Have Zygarde hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1070, kShinyStone,   "Drampanite",     "One of a variety of mysterious Mega Stones. Have Drampa hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1071, kThunderStone, "Zeraorite",      "One of a variety of mysterious Mega Stones. Have Zeraora hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1072, kSunStone,     "Falinksite",     "One of a variety of mysterious Mega Stones. Have Falinks hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1073, kThunderStone, "Raichunite X",   "One of a variety of mysterious Mega Stones. Have Raichu hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1078, kThunderStone, "Raichunite Y",   "One of a variety of mysterious Mega Stones. Have Raichu hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1079, kShinyStone,   "Chimechite",     "One of a variety of mysterious Mega Stones. Have Chimecho hold it, and this stone will enable it to Mega Evolve during battle." },
    { 622,  kDuskStone,    "Absolite Z",     "One of a variety of mysterious Mega Stones. An Absol holding this stone will be able to Mega Evolve into Mega Absol Z during battle." },
    { 1082, kMoonStone,    "Staraptite",     "One of a variety of mysterious Mega Stones. Have Staraptor hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1083, kDawnStone,    "Garchompite Z",  "One of a variety of mysterious Mega Stones. Have Garchomp hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1101, kDawnStone,    "Lucarionite Z",  "One of a variety of mysterious Mega Stones. Have Lucario hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1102, kDuskStone,    "Golurkite",      "One of a variety of mysterious Mega Stones. Have Golurk hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1268, kDawnStone,    "Meowsticite",    "One of a variety of mysterious Mega Stones. Have Meowstic hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1272, kIceStone,     "Crabominite",    "One of a variety of mysterious Mega Stones. Have Crabominable hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1273, kWaterStone,   "Golisopite",     "One of a variety of mysterious Mega Stones. Have Golisopod hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1274, kShinyStone,   "Magearnite",     "One of a variety of mysterious Mega Stones. Have Magearna hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1275, kFireStone,    "Scovillainite",  "One of a variety of mysterious Mega Stones. Have Scovillain hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1276, kIceStone,     "Baxcalibrite",   "One of a variety of mysterious Mega Stones. Have Baxcalibur hold it, and this stone will enable it to Mega Evolve during battle." },
    { 1277, kWaterStone,   "Tatsugirinite",  "One of a variety of mysterious Mega Stones. Have Tatsugiri hold it, and this stone will enable it to Mega Evolve during battle." },
    { 426,  kDawnStone,    "Glimmoranite",   "One of a variety of mysterious Mega Stones. Have Glimmora hold it, and this stone will enable it to Mega Evolve during battle." },
};

constexpr const MegaStoneCatalogEntry* findMegaStoneCatalog(int32_t itemId) {
    for (const auto& s : kAllMegaStones) {
        if (static_cast<int32_t>(s.itemId) == itemId) {
            return &s;
        }
    }
    return nullptr;
}

constexpr bool validateMegaStoneCatalog() {
    constexpr size_t kCount = sizeof(kAllMegaStones) / sizeof(kAllMegaStones[0]);
    if (kCount != 92) return false;
    for (size_t i = 0; i < kCount; ++i) {
        if (kAllMegaStones[i].itemId == 0 || kAllMegaStones[i].itemId >= 1278) return false;
        if (kAllMegaStones[i].fallbackIconId == 0 || kAllMegaStones[i].fallbackIconId >= 1278) return false;
        for (size_t j = i + 1; j < kCount; ++j) {
            if (kAllMegaStones[i].itemId == kAllMegaStones[j].itemId) return false;
        }
    }
    return true;
}
static_assert(validateMegaStoneCatalog());
static_assert(findMegaStoneCatalog(kAbsoliteZ) != nullptr);
static_assert(findMegaStoneCatalog(kCharizarditeX) != nullptr);
static_assert(findMegaStoneCatalog(426) != nullptr);

constexpr uint32_t kSeUiSelect = 0xb7533038;
constexpr uint64_t kMegaFlagBit = 34;
constexpr uint64_t kMegaFlagMask = 1ULL << kMegaFlagBit;

constexpr const MegaSpeciesEntry* findMegaByStone(int species, int originalForm, int heldItem) {
    // Guard: heldItem == 0 must NEVER match move-triggered Mega Rayquaza (stoneItemId == 0).
    if (originalForm != kBaseForm || heldItem <= 0) return nullptr;
    for (const auto& e : kMegaRegistry) {
        if (e.species == species && e.stoneItemId != 0 && static_cast<int>(e.stoneItemId) == heldItem) {
            return &e;
        }
    }
    return nullptr;
}

constexpr const MegaSpeciesEntry* findMegaByForm(int species, int formNo) {
    if (formNo <= kBaseForm) return nullptr;
    for (const auto& e : kMegaRegistry) {
        if (e.species == species && e.form == formNo) {
            return &e;
        }
    }
    return nullptr;
}

constexpr const MegaSpeciesEntry* findMegaByItemId(int32_t itemId) {
    if (itemId <= 0) return nullptr;
    for (const auto& e : kMegaRegistry) {
        if (e.stoneItemId != 0 && static_cast<int32_t>(e.stoneItemId) == itemId) {
            return &e;
        }
    }
    return nullptr;
}

constexpr bool isSupportedMegaSpecies(int species) {
    for (const auto& e : kMegaRegistry) {
        if (e.species == species) return true;
    }
    return false;
}

constexpr bool validateRegistryAgainstStoneCatalog() {
    for (const auto& e : kMegaRegistry) {
        if (e.stoneItemId != 0 && findMegaStoneCatalog(e.stoneItemId) == nullptr) {
            return false;
        }
    }
    return true;
}
static_assert(validateRegistryAgainstStoneCatalog(), "Every Mega stone in kMegaRegistry must exist in kAllMegaStones");

constexpr int resolveMegaForm(int species, int originalForm, int heldItem,
                              bool hasMegaAccessoryInBag, bool trainerAlreadyMegaEvolved) {
    if (!hasMegaAccessoryInBag || trainerAlreadyMegaEvolved) return -1;
    if (const auto* entry = findMegaByStone(species, originalForm, heldItem); entry != nullptr) {
        return entry->form;
    }
    return -1;
}

static_assert(resolveMegaForm(6, 0, kCharizarditeX, true, false) == kMegaXForm);
static_assert(resolveMegaForm(6, 0, kCharizarditeY, true, false) == kMegaYForm);
static_assert(resolveMegaForm(359, 0, kAbsolite, true, false) == kMegaAbsolForm);
static_assert(resolveMegaForm(359, 0, kAbsoliteZ, true, false) == kMegaAbsolZForm);
static_assert(resolveMegaForm(445, 0, 683, true, false) == 1);   // Mega Garchomp
static_assert(resolveMegaForm(445, 0, 1083, true, false) == 2);  // Mega Garchomp Z
static_assert(resolveMegaForm(448, 0, 673, true, false) == 1);   // Mega Lucario
static_assert(resolveMegaForm(448, 0, 1101, true, false) == 2);  // Mega Lucario Z
static_assert(resolveMegaForm(384, 0, 0, true, false) == -1);    // Rayquaza cannot trigger via empty item!
static_assert(resolveMegaForm(359, 0, kCharizarditeX, true, false) == -1);
static_assert(resolveMegaForm(6, 0, kAbsolite, true, false) == -1);
static_assert(resolveMegaForm(359, 0, kAbsoliteZ, false, false) == -1);
static_assert(resolveMegaForm(359, 0, kAbsoliteZ, true, true) == -1);
static_assert(kAbsolite == 677 && kAbsoliteZ == 622);
static_assert(kMagicBounce == 156 && kSharpness == 292);

constexpr uint8_t kMaxBattleClients = 4;
constexpr uint8_t kMaxBattlePokes = 32;
constexpr uint8_t kBtlActionFight = 1; // BTL_ACTION_FIGHT

std::atomic<bool> gPostInitHooksInstalled{false};
std::atomic<bool> gItemTablePatched{false};
bool gIsTrainerBattle = false;
bool gMegaBattleAllowed = true;
bool gClientMegaUsed[kMaxBattleClients] = {false, false, false, false};
bool gTrainerStonesEquipped[kMaxBattleClients] = {false, false, false, false};
bool gMegaArmed = false;
bool gAnyMegaActiveInBattle = false;

// Player-only post-battle revert tracking (enemy battle parties are ephemeral).
int gPlayerMegaSpecies = 0;
int gPlayerMegaForm = 0;
uint16_t gPlayerPreMegaTokusei = 0;

// Per-pokeID pre-Mega ability tracking so player & enemy Mega Evolutions never clobber each other.
uint16_t gPreMegaTokuseiByPokeID[kMaxBattlePokes] = {};

// Transient display context for MessageManager$$GetSimpleMessage during active Mega Evolution.
int gCurrentEvoSpecies = 0;
int gCurrentEvoForm = 0;
uint8_t gCurrentEvoClientID = 0;

bool gInMegaTokuseiSwap = false;

inline bool isKnownPreMegaTokusei(uint16_t tok) {
    if (tok == 0) return false;
    if (gPlayerPreMegaTokusei == tok) return true;
    for (uint8_t i = 0; i < kMaxBattlePokes; ++i) {
        if (gPreMegaTokuseiByPokeID[i] == tok) return true;
    }
    return false;
}

inline void resetBattleMegaState() {
    for (uint8_t i = 0; i < kMaxBattleClients; ++i) {
        gClientMegaUsed[i] = false;
        gTrainerStonesEquipped[i] = false;
    }
    for (uint8_t i = 0; i < kMaxBattlePokes; ++i) {
        gPreMegaTokuseiByPokeID[i] = 0;
    }
    gMegaArmed = false;
    gAnyMegaActiveInBattle = false;
    gCurrentEvoSpecies = 0;
    gCurrentEvoForm = 0;
    gCurrentEvoClientID = 0;
}

inline bool isBattleEnded() {
    if (auto* cls = Dpr::Battle::View::BattleViewCore::getClass(); cls != nullptr) {
        cls->initIfNeeded();
    }
    return Dpr::Battle::View::BattleViewCore::get_Instance() == nullptr;
}

bool gInMegaRevert = false;

inline void revertMegaFormInParty() {
    if (gInMegaRevert) {
        return;
    }
    gInMegaRevert = true;
    if (auto* pwCls = PlayerWork::getClass(); pwCls != nullptr) {
        pwCls->initIfNeeded();
        if (auto* party = PlayerWork::get_playerParty();
            party != nullptr && party->fields.m_member != nullptr) {
            const uint32_t count = party->fields.m_memberCount;
            const uint64_t arrLen = party->fields.m_member->max_length;
            for (uint32_t i = 0; i < count && i < 6 && i < arrLen; ++i) {
                auto* member = reinterpret_cast<Pml::PokePara::CoreParam::Object*>(party->GetMemberPointer(i));
                if (member == nullptr || member->IsNull()) {
                    continue;
                }
                const int monsNo = member->GetMonsNo();
                const uint16_t form = member->GetFormNo();
                if (findMegaByForm(monsNo, form) != nullptr) {
                    const bool isTrackedPlayerMega =
                        (monsNo == gPlayerMegaSpecies && static_cast<int>(form) == gPlayerMegaForm);
                    if (member->IsHpZero()) {
                        // Never call ChangeFormNo on a fainted Pokemon (it can recalculate/restore HP).
                        if (member->fields.m_accessor != nullptr) {
                            member->fields.m_accessor->SetFormNo(kBaseForm);
                        }
                    } else {
                        member->ChangeFormNo(kBaseForm, nullptr);
                    }
                    if (isTrackedPlayerMega && gPlayerPreMegaTokusei > 0 && member->fields.m_accessor != nullptr) {
                        member->fields.m_accessor->SetTokuseiNo(gPlayerPreMegaTokusei);
                    }
                }
            }
        }
    }
    gPlayerMegaSpecies = 0;
    gPlayerMegaForm = 0;
    gPlayerPreMegaTokusei = 0;
    resetBattleMegaState();
    gInMegaRevert = false;
}

struct Il2CppFieldInfo {
    const char* name;
    const Il2CppType* type;
    Il2CppClass* parent;
    int32_t offset;
    uint32_t token;
};

// Resolves an ability's singleton EventHandlerTable::Array* and priority directly from BDSP's
// Handler::Tokusei::GET_FUNC_TABLE (zero extra allocations, zero memory leaks).
inline Dpr::Battle::Logic::EventFactor::EventHandlerTable::Array* resolveAbilityHandlerTable(
    uint16_t tokuseiNo, uint8_t* outPrio) {
    using namespace Dpr::Battle::Logic;
    system_load_typeinfo(0x8a5b);
    auto* tokCls = Handler::Tokusei::getClass();
    if (tokCls == nullptr) {
        return nullptr;
    }
    tokCls->initIfNeeded();
    auto* sf = tokCls->static_fields;
    if (sf == nullptr || sf->GET_FUNC_TABLE == nullptr) {
        return nullptr;
    }
    auto* tbl = sf->GET_FUNC_TABLE;
    for (uint64_t i = 0; i < tbl->max_length; ++i) {
        const auto& elem = tbl->m_Items[i].fields;
        if (elem.tokusei == static_cast<int32_t>(tokuseiNo) && elem.func != nullptr) {
            const auto& del = elem.func->fields;
            if (del.method_ptr != 0) {
                auto fn = reinterpret_cast<EventFactor::EventHandlerTable::Array* (*)(uint8_t*, const MethodInfo*)>(
                    del.method_ptr);
                return fn(outPrio, del.method);
            }
        }
    }
    return nullptr;
}

// Silently hot-swaps a Pokemon's live Ability EventFactor in-place inside BattleEnv::m_eventFactorContainer.
inline bool swapAbilityEventFactorInPlace(Dpr::Battle::Logic::BattleEnv::Object* battleEnv,
                                          uint8_t pokeID,
                                          uint16_t prevTokusei,
                                          uint16_t nextTokusei) {
    using namespace Dpr::Battle::Logic;
    if (battleEnv == nullptr || battleEnv->fields.m_eventFactorContainer == nullptr) {
        return false;
    }

    uint8_t newPrio = 0;
    auto* newTable = resolveAbilityHandlerTable(nextTokusei, &newPrio);

    auto trySwapFactor = [&](EventFactor::Object* factor) -> bool {
        if (factor == nullptr) {
            return false;
        }
        auto* fObj = reinterpret_cast<Il2CppObject*>(factor);
        if (fObj->klass == nullptr || fObj->klass->_1.name == nullptr ||
            std::strcmp(fObj->klass->_1.name, "EventFactor") != 0) {
            return false;
        }
        auto* data = factor->fields.m_data;
        if (data == nullptr || !data->fields.existFlag || data->fields.pokeID != pokeID) {
            return false;
        }
        if (data->fields.subID != prevTokusei &&
            !(data->fields.subID == nextTokusei && newTable != nullptr && data->fields.handlerTable != newTable)) {
            return false;
        }

        data->fields.subID = nextTokusei;
        if (newTable != nullptr) {
            data->fields.handlerTable = newTable;
            data->fields.numHandlers = static_cast<uint32_t>(newTable->max_length);
            if (newPrio != 0) {
                data->fields.priority = newPrio;
            }
            data->fields.sleepFlag = false;
            data->fields.rmReserveFlag = false;
        } else {
            // Passive/unregistered ability: silence pre-Mega EventFactor so the old ability never lingers.
            data->fields.sleepFlag = true;
        }
        if (data->fields.work != nullptr) {
            for (uint64_t w = 0; w < data->fields.work->max_length && w < 16; ++w) {
                data->fields.work->m_Items[w] = 0;
            }
        }
        return true;
    };

    auto scanContainerObject = [&](Il2CppObject* obj, int depth, auto&& selfRef) -> bool {
        if (obj == nullptr || obj->klass == nullptr || obj->klass->_1.fields == nullptr || depth > 2) {
            return false;
        }
        Il2CppClass* cClass = obj->klass;
        const uint32_t instSize = cClass->_2.instance_size;
        auto* fields = reinterpret_cast<const Il2CppFieldInfo*>(cClass->_1.fields);
        for (uint16_t i = 0; i < cClass->_2.field_count; ++i) {
            const auto& fi = fields[i];
            // Skip static/literal fields whose offset indexes static_fields rather than the instance.
            constexpr uint32_t kFieldAttrStaticOrLiteral = 0x0010U | 0x0040U;
            if (fi.type == nullptr ||
                (fi.type->bits & kFieldAttrStaticOrLiteral) != 0 ||
                fi.offset < static_cast<int32_t>(sizeof(Il2CppObject)) ||
                (fi.offset % 8) != 0 ||
                static_cast<uint32_t>(fi.offset) + sizeof(void*) > instSize) {
                continue;
            }
            const uint8_t ilType = static_cast<uint8_t>((fi.type->bits >> 16) & 0xFF);
            if (ilType != 0x12 /* IL2CPP_TYPE_CLASS */ && ilType != 0x1D /* IL2CPP_TYPE_SZARRAY */) {
                continue;
            }
            void* ptr = nullptr;
            std::memcpy(&ptr, reinterpret_cast<const uint8_t*>(obj) + fi.offset, sizeof(void*));
            if (ptr == nullptr) {
                continue;
            }

            auto* refObj = reinterpret_cast<Il2CppObject*>(ptr);
            if (refObj->klass == nullptr) {
                continue;
            }

            if (ilType == 0x12 && refObj->klass->_1.name != nullptr &&
                std::strcmp(refObj->klass->_1.name, "EventFactor") == 0) {
                auto* cur = reinterpret_cast<EventFactor::Object*>(refObj);
                for (int step = 0; cur != nullptr && step < 128; ++step) {
                    if (trySwapFactor(cur)) {
                        return true;
                    }
                    cur = cur->fields.m_nextFactor;
                }
            } else if (ilType == 0x1D && refObj->klass->_1.element_class != nullptr &&
                       refObj->klass->_1.element_class->_1.name != nullptr &&
                       std::strcmp(refObj->klass->_1.element_class->_1.name, "EventFactor") == 0) {
                auto* arr = reinterpret_cast<EventFactor::Array*>(refObj);
                const uint64_t len = (arr->max_length < 256) ? arr->max_length : 256;
                for (uint64_t idx = 0; idx < len; ++idx) {
                    if (trySwapFactor(arr->m_Items[idx])) {
                        return true;
                    }
                }
            } else if (ilType == 0x12 && depth < 1) {
                if (selfRef(refObj, depth + 1, selfRef)) {
                    return true;
                }
            }
        }
        return false;
    };

    auto* containerObj = reinterpret_cast<Il2CppObject*>(battleEnv->fields.m_eventFactorContainer);
    return scanContainerObject(containerObj, 0, scanContainerObject);
}

// Builds a dual-type PokeTypePair preserving any extra type (Trick-or-Treat / Forest's Curse).
inline Dpr::Battle::Logic::PokeTypePair::Object makeDualTypePair(
    Dpr::Battle::Logic::PokeTypePair::Object origPair, uint8_t type1, uint8_t type2) {
    using namespace Dpr::Battle::Logic;
    static bool sProbed = false;
    static uint8_t sShift1 = 0;
    static uint8_t sShift2 = 5;
    static uint16_t sMask = 0x1F;
    static uint16_t sExMask = 0xFC00;

    if (!sProbed) {
        sProbed = true;
        const uint16_t pure0 = PokeTypePair::MakePure(0).fields.value;
        const uint16_t pure1 = PokeTypePair::MakePure(1).fields.value;
        const uint16_t diff = static_cast<uint16_t>(pure1 ^ pure0);
        uint8_t bits[2] = {0, 5};
        int found = 0;
        for (uint8_t b = 0; b < 16 && found < 2; ++b) {
            if ((diff >> b) & 1U) {
                bits[found++] = b;
            }
        }
        if (found == 2) {
            const uint8_t width = static_cast<uint8_t>(bits[1] - bits[0]);
            const uint16_t mask = (width < 16) ? static_cast<uint16_t>((1U << width) - 1U) : 0x1F;
            PokeTypePair::Object probe{};
            probe.fields.value = static_cast<uint16_t>(
                (pure0 & ~((mask << bits[0]) | (mask << bits[1]))) |
                ((kTypeFire & mask) << bits[0]) |
                ((kTypeDragon & mask) << bits[1]));
            uint8_t t1 = 0, t2 = 0, tEx = 0;
            PokeTypePair::Split(probe, &t1, &t2, &tEx);
            if (t1 == kTypeFire && t2 == kTypeDragon) {
                sShift1 = bits[0];
                sShift2 = bits[1];
                sMask = mask;
                sExMask = static_cast<uint16_t>(~((mask << sShift1) | (mask << sShift2)));
            } else if (t1 == kTypeDragon && t2 == kTypeFire) {
                sShift1 = bits[1];
                sShift2 = bits[0];
                sMask = mask;
                sExMask = static_cast<uint16_t>(~((mask << sShift1) | (mask << sShift2)));
            }
        }
    }

    PokeTypePair::Object candidate{};
    candidate.fields.value = static_cast<uint16_t>(
        (origPair.fields.value & sExMask) |
        ((type1 & sMask) << sShift1) |
        ((type2 & sMask) << sShift2));

    uint8_t chk1 = 0, chk2 = 0, chkEx = 0;
    PokeTypePair::Split(candidate, &chk1, &chk2, &chkEx);
    if (chk1 == type1 && chk2 == type2) {
        return candidate;
    }
    return origPair;
}

void installSharedPostInitHooks();

inline bool isMegaStone(int32_t itemId) {
    return findMegaStoneCatalog(itemId) != nullptr;
}

inline bool isMegaKeyItem(int32_t itemId) {
    return itemId == kMegaBracelet || itemId == kMegaRing || itemId == kKeyStone;
}

inline uint16_t ownedMegaAccessoryInBag() {
    auto* pwCls = PlayerWork::getClass();
    if (pwCls == nullptr) return 0;
    pwCls->initIfNeeded();
    if (PlayerWork::GetItem(kMegaRing).fields.Count > 0) return kMegaRing;
    if (PlayerWork::GetItem(kMegaBracelet).fields.Count > 0) return kMegaBracelet;
    if (PlayerWork::GetItem(kKeyStone).fields.Count > 0) return kKeyStone;
    return 0;
}

inline bool hasMegaAccessoryInBag() {
    return ownedMegaAccessoryInBag() != 0;
}

// Patch all 92 Mega Stone rows & 3 Mega Key Item rows in the loaded master ItemTable once.
inline void patchItemTableInMemory() {
    if (gItemTablePatched.load(std::memory_order_acquire)) {
        return;
    }

    XLSXContent::ItemTable::Object* table = nullptr;
    if (auto* pmlUse = Pml::PmlUse::get_Instance(); pmlUse != nullptr) {
        table = pmlUse->fields.itemPrmTotal;
    }
    if (table == nullptr) {
        if (auto* mgrClass = Pml::Item::ItemManager::getClass();
            mgrClass != nullptr && mgrClass->static_fields != nullptr && mgrClass->static_fields->s_Instance != nullptr) {
            table = mgrClass->static_fields->s_Instance->fields.m_alldata;
        }
    }
    if (table == nullptr || table->fields.Item == nullptr) {
        return;
    }

    auto* items = table->fields.Item;
    if (items->max_length <= kKeyStone) {
        return;
    }

    auto* charcoal = items->m_Items[kCharcoal];
    auto* magmaStone = items->m_Items[kMagmaStone];
    auto* oddKeystone = items->m_Items[kOddKeystone];
    if (charcoal == nullptr || magmaStone == nullptr) {
        return;
    }

    auto applyTemplate = [](XLSXContent::ItemTable::SheetItem::Object* target,
                            XLSXContent::ItemTable::SheetItem::Object* tmpl,
                            XLSXContent::ItemTable::SheetItem::Object* iconTmpl) {
        if (target == nullptr || tmpl == nullptr) {
            return;
        }
        target->fields.type = tmpl->fields.type;
        target->fields.group = tmpl->fields.group;
        target->fields.group_id = tmpl->fields.group_id;
        target->fields.fld_pocket = tmpl->fields.fld_pocket;
        target->fields.field_func = tmpl->fields.field_func;
        target->fields.battle_func = tmpl->fields.battle_func;
        target->fields.flags0 = tmpl->fields.flags0;
        if (target->fields.sort == 0) {
            target->fields.sort = tmpl->fields.sort;
        }
        if (iconTmpl != nullptr) {
            target->fields.iconid = iconTmpl->fields.iconid;
        }
    };

    for (const auto& s : kAllMegaStones) {
        if (s.itemId < items->max_length) {
            auto* target = items->m_Items[s.itemId];
            const bool keepNativeIcon = ((s.itemId >= 656 && s.itemId <= 685) ||
                                         (s.itemId >= 752 && s.itemId <= 764) ||
                                         (s.itemId >= 767 && s.itemId <= 770)) &&
                                        target != nullptr && target->fields.iconid > 0;
            auto* iconTmpl = (!keepNativeIcon && s.fallbackIconId < items->max_length)
                                 ? items->m_Items[s.fallbackIconId]
                                 : nullptr;
            applyTemplate(target, charcoal, iconTmpl);
        }
    }

    applyTemplate(items->m_Items[kMegaBracelet], magmaStone, oddKeystone);
    applyTemplate(items->m_Items[kMegaRing], magmaStone, oddKeystone);
    applyTemplate(items->m_Items[kKeyStone], magmaStone, oddKeystone);

    gItemTablePatched.store(true, std::memory_order_release);
}

inline bool utf16ContainsAscii(System::String::Object* str, const char* needle) {
    if (str == nullptr || needle == nullptr) {
        return false;
    }
    const int32_t hayLen = str->fields.m_stringLength;
    const int32_t needleLen = static_cast<int32_t>(std::strlen(needle));
    if (needleLen == 0 || hayLen < needleLen || hayLen > 1024) {
        return false;
    }
    const uint16_t* chars = reinterpret_cast<const uint16_t*>(&str->fields.m_firstChar);
    for (int32_t i = 0; i <= hayLen - needleLen; ++i) {
        bool match = true;
        for (int32_t j = 0; j < needleLen; ++j) {
            uint16_t hc = chars[i + j];
            if (hc >= 'A' && hc <= 'Z') hc = static_cast<uint16_t>(hc + ('a' - 'A'));
            unsigned char nc = static_cast<unsigned char>(needle[j]);
            if (nc >= 'A' && nc <= 'Z') nc = static_cast<unsigned char>(nc + ('a' - 'A'));
            if (hc != static_cast<uint16_t>(nc)) {
                match = false;
                break;
            }
        }
        if (match) {
            return true;
        }
    }
    return false;
}

inline bool utf16EqualsAsciiRange(const uint16_t* chars, int32_t len, const char* ascii) {
    if (chars == nullptr || ascii == nullptr) {
        return false;
    }
    const int32_t asciiLen = static_cast<int32_t>(std::strlen(ascii));
    if (len != asciiLen) {
        return false;
    }
    for (int32_t i = 0; i < len; ++i) {
        uint16_t hc = chars[i];
        if (hc >= 'A' && hc <= 'Z') hc = static_cast<uint16_t>(hc + ('a' - 'A'));
        unsigned char nc = static_cast<unsigned char>(ascii[i]);
        if (nc >= 'A' && nc <= 'Z') nc = static_cast<unsigned char>(nc + ('a' - 'A'));
        if (hc != static_cast<uint16_t>(nc)) {
            return false;
        }
    }
    return true;
}

// Fast, zero-allocation redirect for UIModelViewController (Party 3D preview & Summary):
// Rewrites "pokemons/field/<customMegaBundle>" -> "pokemons/battle/<customMegaBundle>"
// cleanly at AssetManager entry points without fragile mid-function inline hooks.
inline System::String::Object* redirectCustomMegaFieldBundlePath(System::String::Object* bundlePath) {
    if (bundlePath == nullptr) {
        return bundlePath;
    }
    constexpr const char* kFieldPrefix = "pokemons/field/";
    constexpr int32_t kPrefixLen = 15;
    const int32_t len = bundlePath->fields.m_stringLength;
    if (len <= kPrefixLen || len > 128) {
        return bundlePath;
    }
    const uint16_t* chars = reinterpret_cast<const uint16_t*>(&bundlePath->fields.m_firstChar);
    if (!utf16EqualsAsciiRange(chars, kPrefixLen, kFieldPrefix)) {
        return bundlePath;
    }
    const uint16_t* suffix = chars + kPrefixLen;
    const int32_t suffixLen = len - kPrefixLen;
    for (const auto& e : kMegaRegistry) {
        if (!e.hasCustomModel) {
            continue;
        }
        const char* matched = nullptr;
        if (e.bundleName != nullptr && utf16EqualsAsciiRange(suffix, suffixLen, e.bundleName)) {
            matched = e.bundleName;
        } else if (e.shinyBundleName != nullptr && utf16EqualsAsciiRange(suffix, suffixLen, e.shinyBundleName)) {
            matched = e.shinyBundleName;
        }
        if (matched != nullptr) {
            char battlePath[64];
            std::snprintf(battlePath, sizeof(battlePath), "pokemons/battle/%s", matched);
            return System::String::Create(battlePath);
        }
    }
    return bundlePath;
}

// Parses the exact trailing numeric item ID from BDSP message labels (e.g. "itemname_0622" -> 622).
inline int32_t parseTrailingItemId(System::String::Object* label) {
    if (label == nullptr) {
        return -1;
    }
    const int32_t len = label->fields.m_stringLength;
    if (len <= 0) {
        return -1;
    }
    const uint16_t* chars = reinterpret_cast<const uint16_t*>(&label->fields.m_firstChar);
    int32_t end = len - 1;
    while (end >= 0 && (chars[end] == ' ' || chars[end] == '\0')) {
        --end;
    }
    if (end < 0 || chars[end] < '0' || chars[end] > '9') {
        return -1;
    }
    int32_t start = end;
    while (start > 0 && chars[start - 1] >= '0' && chars[start - 1] <= '9') {
        --start;
    }
    if (start > 0 && chars[start - 1] != '_') {
        return -1;
    }
    int32_t val = 0;
    for (int32_t i = start; i <= end; ++i) {
        val = (val * 10) + static_cast<int32_t>(chars[i] - '0');
        if (val > 99999) {
            return -1;
        }
    }
    return val;
}

constexpr bool isNativeGen6MegaStone(int32_t itemId) {
    return (itemId >= 656 && itemId <= 685) ||
           (itemId >= 752 && itemId <= 764) ||
           (itemId >= 767 && itemId <= 770);
}

inline const char* lookupMegaItemTextById(int32_t itemId, bool isItemInfo) {
    if (itemId <= 0) {
        return nullptr;
    }
    if (const auto* stone = findMegaStoneCatalog(itemId); stone != nullptr) {
        return isItemInfo ? stone->stoneDesc : stone->stoneName;
    }
    if (itemId == kMegaRing) {
        return isItemInfo
            ? "This ring contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."
            : "Mega Ring";
    }
    if (itemId == kMegaBracelet) {
        return isItemInfo
            ? "This bracelet contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."
            : "Mega Bracelet";
    }
    if (itemId == kKeyStone) {
        return isItemInfo
            ? "A stone filled with an unexplained power. It makes Pokémon that hold a Mega Stone Mega Evolve."
            : "Key Stone";
    }
    return nullptr;
}

inline const char* lookupMegaItemTextByLabel(System::String::Object* fileName, System::String::Object* label) {
    if (label == nullptr) {
        return nullptr;
    }
    const bool isItemName =
        utf16ContainsAscii(label, "itemname_") ||
        (fileName != nullptr && utf16ContainsAscii(fileName, "itemname"));
    const bool isItemInfo =
        utf16ContainsAscii(label, "iteminfo_") ||
        (fileName != nullptr && utf16ContainsAscii(fileName, "iteminfo"));

    if (!isItemName && !isItemInfo) {
        return nullptr;
    }

    const int32_t itemId = parseTrailingItemId(label);
    return lookupMegaItemTextById(itemId, isItemInfo);
}

inline bool formatCustomMegaAtlasSpriteName(int32_t itemId, bool isLarge, char* outBuf, size_t bufSize) {
    if (outBuf == nullptr || bufSize < 16 || itemId <= 0 || itemId > 9999) {
        return false;
    }
    if (!isMegaStone(itemId) && !isMegaKeyItem(itemId)) {
        return false;
    }
    outBuf[0] = 'i'; outBuf[1] = 't'; outBuf[2] = 'e'; outBuf[3] = 'm'; outBuf[4] = '_';
    outBuf[5] = static_cast<char>('0' + ((itemId / 1000) % 10));
    outBuf[6] = static_cast<char>('0' + ((itemId / 100) % 10));
    outBuf[7] = static_cast<char>('0' + ((itemId / 10) % 10));
    outBuf[8] = static_cast<char>('0' + (itemId % 10));
    if (isLarge) {
        outBuf[9] = '_'; outBuf[10] = 'L'; outBuf[11] = '\0';
    } else {
        outBuf[9] = '\0';
    }
    return true;
}

inline bool getMegaFlag(const Dpr::Battle::Logic::BTL_ACTION_PARAM::Object& param) {
    return ((static_cast<uint64_t>(param.fields.raw) >> kMegaFlagBit) & 1ULL) != 0;
}

inline bool pokeKnowsMove(Dpr::Battle::Logic::BTL_POKEPARAM::Object* pokeParam, int32_t moveId) {
    if (pokeParam == nullptr || moveId <= 0) {
        return false;
    }
    const uint8_t count = pokeParam->WAZA_GetCount();
    for (uint8_t i = 0; i < count && i < 4; ++i) {
        if (pokeParam->WAZA_GetID(i) == moveId) {
            return true;
        }
    }
    return false;
}

inline const MegaSpeciesEntry* matchMegaCandidate(Dpr::Battle::Logic::BTL_POKEPARAM::Object* pokeParam) {
    if (pokeParam == nullptr || pokeParam->GetFormNo() != kBaseForm) {
        return nullptr;
    }
    const int monsNo = pokeParam->GetMonsNo();
    const int heldItem = pokeParam->GetItem();
    if (const auto* byStone = findMegaByStone(monsNo, kBaseForm, heldItem); byStone != nullptr) {
        return byStone;
    }
    for (const auto& e : kMegaRegistry) {
        if (e.species == monsNo && e.requiredMoveId > 0 && pokeKnowsMove(pokeParam, e.requiredMoveId)) {
            return &e;
        }
    }
    return nullptr;
}

inline const MegaSpeciesEntry* targetMegaEntry(Dpr::Battle::Logic::BTL_POKEPARAM::Object* pokeParam) {
    if (pokeParam == nullptr || pokeParam->HENSIN_Check() || !gMegaBattleAllowed ||
        !hasMegaAccessoryInBag() || gClientMegaUsed[0]) {
        return nullptr;
    }
    return matchMegaCandidate(pokeParam);
}

inline const MegaSpeciesEntry* targetTrainerMegaEntry(Dpr::Battle::Logic::BTL_POKEPARAM::Object* pokeParam,
                                                      uint8_t clientID) {
    if (pokeParam == nullptr || pokeParam->IsDead() || pokeParam->HENSIN_Check() ||
        !gIsTrainerBattle || !gMegaBattleAllowed ||
        clientID == 0 || clientID >= kMaxBattleClients || gClientMegaUsed[clientID]) {
        return nullptr;
    }
    return matchMegaCandidate(pokeParam);
}

// Safe wrapper for BTL_ACTION_PARAM::get_gen_cmd passing explicit nullptr MethodInfo* in x1.
inline uint8_t getActionGenCmd(Dpr::Battle::Logic::BTL_ACTION_PARAM::Object* param) {
    if (param == nullptr) return 0;
    using Fn = uint8_t (*)(Dpr::Battle::Logic::BTL_ACTION_PARAM::Object*, const void*);
    return reinterpret_cast<Fn>(exl::util::modules::GetTargetOffset(0x01f4d680))(param, nullptr);
}

// Selects the best Mega form for an NPC trainer's live BTL_POKEPARAM (stat-aware for dual-Mega species).
inline const MegaSpeciesEntry* chooseBestTrainerMegaEntry(Dpr::Battle::Logic::BTL_POKEPARAM::Object* bpp) {
    if (bpp == nullptr || bpp->IsDead() || bpp->GetFormNo() != kBaseForm) {
        return nullptr;
    }
    const int monsNo = bpp->GetMonsNo();
    const int heldItem = bpp->GetItem();
    if (const auto* byStone = findMegaByStone(monsNo, kBaseForm, heldItem); byStone != nullptr) {
        return byStone;
    }
    const MegaSpeciesEntry* form1 = nullptr;
    const MegaSpeciesEntry* form2 = nullptr;
    for (const auto& e : kMegaRegistry) {
        if (e.species != monsNo) {
            continue;
        }
        if (e.requiredMoveId > 0 && e.stoneItemId == 0) {
            if (pokeKnowsMove(bpp, e.requiredMoveId)) {
                return &e;
            }
            continue;
        }
        if (e.form == 1) form1 = &e;
        else if (e.form == 2) form2 = &e;
    }
    if (form1 != nullptr && form2 != nullptr) {
        const int32_t atk = bpp->GetValue(Dpr::Battle::Logic::BTL_POKEPARAM::ValueID::BPP_ATTACK);
        const int32_t spAtk = bpp->GetValue(Dpr::Battle::Logic::BTL_POKEPARAM::ValueID::BPP_SP_ATTACK);
        // Absol Z (form 2) is faster/more physical than Mega Absol (form 1); for all other dual-Megas
        // (Charizard X/Y, Mewtwo X/Y, Raichu X/Y, Garchomp/Z, Lucario/Z), form 1 is physical and form 2 is special.
        if (monsNo == kAbsolSpecies) {
            return (atk >= spAtk) ? form2 : form1;
        }
        return (atk >= spAtk) ? form1 : form2;
    }
    return (form1 != nullptr) ? form1 : form2;
}

// Lazily equips an NPC trainer's ace Mega-capable Pokemon using BDSP's verified live BTL_PARTY
// (never touches BATTLE_SETUP_PARAM::fields.party during SetupBattleTrainer).
// Rules:
//  1. If any party member ALREADY holds its matching Mega Stone in TrainerTable (including Fantina's
//     Lv. 32-33 Gengar and all X/Y Mega bosses), respect it verbatim and never equip a second stone.
//  2. Otherwise, only auto-equip a Mega Stone when the trainer's highest-level Pokemon is > Lv. 33.
inline void equipTrainerMegaStonesForClient(Dpr::Battle::Logic::BTL_PARTY::Object* party, uint8_t clientID) {
    if (clientID == 0 || clientID >= kMaxBattleClients || gTrainerStonesEquipped[clientID]) {
        return;
    }
    gTrainerStonesEquipped[clientID] = true;
    if (party == nullptr || party->fields.m_pMember == nullptr) {
        return;
    }
    const uint8_t count = party->fields.m_memberCount;
    const uint64_t arrLen = party->fields.m_pMember->max_length;
    if (count == 0 || arrLen == 0) {
        return;
    }

    constexpr uint8_t kAutoMegaLevelThreshold = 33;
    uint8_t maxPartyLevel = 0;
    Dpr::Battle::Logic::BTL_POKEPARAM::Object* bestBpp = nullptr;
    const MegaSpeciesEntry* bestEntry = nullptr;
    uint8_t bestLevel = 0;

    for (uint8_t i = 0; i < count && i < 6 && i < arrLen; ++i) {
        auto* bpp = party->GetMemberDataConst(i);
        if (bpp == nullptr || bpp->fields.m_coreParam == nullptr) {
            continue;
        }
        const uint8_t lv = bpp->fields.m_coreParam->fields.level;
        if (lv > maxPartyLevel) {
            maxPartyLevel = lv;
        }
        // Respect pre-equipped Mega Stones from TrainerTable (even if fainted) without overwriting or double-equipping.
        if (findMegaByStone(bpp->GetMonsNo(), kBaseForm, bpp->GetItem()) != nullptr) {
            return;
        }
        if (bpp->IsDead() || bpp->GetFormNo() != kBaseForm) {
            continue;
        }
        const auto* candidate = chooseBestTrainerMegaEntry(bpp);
        if (candidate == nullptr) {
            continue;
        }
        if (bestBpp == nullptr || lv >= bestLevel) {
            bestBpp = bpp;
            bestEntry = candidate;
            bestLevel = lv;
        }
    }

    if (maxPartyLevel <= kAutoMegaLevelThreshold) {
        return;
    }

    if (bestBpp != nullptr && bestEntry != nullptr && bestEntry->stoneItemId != 0) {
        bestBpp->fields.m_coreParam->fields.item = bestEntry->stoneItemId;
        if (auto* srcPoke = reinterpret_cast<Pml::PokePara::CoreParam::Object*>(bestBpp->GetSrcData());
            srcPoke != nullptr && !srcPoke->IsNull()) {
            srcPoke->SetItem(bestEntry->stoneItemId);
        }
    }
}

inline void updateMegaBattleBanner(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList,
                                   bool forceRefresh = false) {
    if (wazaList == nullptr || wazaList->fields._btlPokeParam == nullptr) {
        return;
    }

    auto* bpp = wazaList->fields._btlPokeParam;
    const auto* candidate = matchMegaCandidate(bpp);
    const bool hasRing = hasMegaAccessoryInBag();
    const bool playerMegaUsed = gClientMegaUsed[0];
    const auto* entry = targetMegaEntry(bpp);
    const bool eligible = entry != nullptr;

    // Avoid 60 FPS IL2CPP System::String allocations & TextMeshPro mesh rebuilds when UI state is unchanged.
    static const void* sLastWazaList = nullptr;
    static const MegaSpeciesEntry* sLastEntry = nullptr;
    static int sLastBannerState = -1;
    const int bannerState = (eligible ? 1 : 0) |
                            (gMegaArmed ? 2 : 0) |
                            (playerMegaUsed ? 4 : 0) |
                            (hasRing ? 8 : 0) |
                            (candidate != nullptr ? 16 : 0);
    if (!forceRefresh && sLastWazaList == wazaList && sLastEntry == entry && sLastBannerState == bannerState) {
        return;
    }
    sLastWazaList = wazaList;
    sLastEntry = entry;
    sLastBannerState = bannerState;

    if (wazaList->fields._yButtonText != nullptr) {
        auto* promptText = reinterpret_cast<Dpr::UI::UIText::Object*>(wazaList->fields._yButtonText);
        char bannerBuf[64];
        const char* label = "MEGA EVOLVE (OFF)";
        UnityEngine::Color::Object textColor{};
        textColor.fields = { .r = 0.65f, .g = 0.65f, .b = 0.70f, .a = 1.0f };

        if (eligible && gMegaArmed) {
            std::snprintf(bannerBuf, sizeof(bannerBuf), "%s ACTIVE", entry->megaName);
            label = bannerBuf;
            textColor.fields = { .r = 1.0f, .g = 0.92f, .b = 0.20f, .a = 1.0f };
        } else if (eligible) {
            std::snprintf(bannerBuf, sizeof(bannerBuf), "[R] MEGA EVOLVE");
            label = bannerBuf;
            textColor.fields = { .r = 1.0f, .g = 1.0f, .b = 1.0f, .a = 1.0f };
        } else if (candidate != nullptr && !hasRing && !playerMegaUsed) {
            label = "MEGA (Need Ring)";
            textColor.fields = { .r = 1.0f, .g = 0.65f, .b = 0.35f, .a = 1.0f };
        } else if (playerMegaUsed) {
            label = "MEGA USED";
            textColor.fields = { .r = 0.65f, .g = 0.65f, .b = 0.70f, .a = 1.0f };
        }

        promptText->virtual_set_text(System::String::Create(label));
        promptText->virtual_set_color(textColor);
    }

    if (wazaList->fields._wazaDescriptionButton != nullptr) {
        auto* descBtn = reinterpret_cast<Dpr::Battle::View::UI::BUIButtonBase::Object*>(
            wazaList->fields._wazaDescriptionButton);
        if (descBtn->fields._backgroundImage != nullptr) {
            UnityEngine::Color::Fields bgColor{};
            if (eligible && gMegaArmed) {
                bgColor = { .r = 0.95f, .g = 0.18f, .b = 0.58f, .a = 1.0f };
            } else if (eligible) {
                bgColor = { .r = 0.12f, .g = 0.62f, .b = 0.95f, .a = 1.0f };
            } else {
                bgColor = { .r = 0.30f, .g = 0.30f, .b = 0.34f, .a = 0.85f };
            }
            descBtn->fields._backgroundImage->virtual_set_color(bgColor);
        }
    }

    if (wazaList->fields._wazaButtons != nullptr) {
        const UnityEngine::Color::Fields armedColor{ .r = 1.0f, .g = 0.85f, .b = 0.12f, .a = 1.0f };
        const UnityEngine::Color::Fields normalColor = wazaList->fields._wazaTextColor.fields;
        const uint64_t rawCount = wazaList->fields._wazaButtons->max_length;
        const uint64_t count = (rawCount < 4) ? rawCount : 4;
        for (uint64_t i = 0; i < count; ++i) {
            auto* btn = wazaList->fields._wazaButtons->m_Items[i];
            if (btn != nullptr && btn->fields._text != nullptr) {
                btn->fields._text->virtual_set_color((eligible && gMegaArmed) ? armedColor : normalColor);
            }
        }
    }
}

bool executeMegaEvolution(Dpr::Battle::Logic::SectionContainer::Object* container,
                          Dpr::Battle::Logic::PokeAction::Object* pokeAction) {
    if (container == nullptr || pokeAction == nullptr) {
        return false;
    }

    system_load_typeinfo(0x8910);
    system_load_typeinfo(0x2c99);
    system_load_typeinfo(0xa963);
    if (auto* commonCls = Dpr::Battle::Logic::Common::getClass(); commonCls != nullptr) {
        commonCls->initIfNeeded();
    }

    auto* param = pokeAction->fields.bpp;
    if (param == nullptr || param->IsDead()) {
        return false;
    }
    const uint8_t clientID = pokeAction->fields.clientID;
    const auto* entry = (clientID == 0) ? targetMegaEntry(param) : targetTrainerMegaEntry(param, clientID);
    if (entry == nullptr) {
        return false;
    }

    const uint8_t pokeID = param->GetID();
    const uint16_t prevTokusei = param->fields.m_tokusei;
    if (pokeID < kMaxBattlePokes) {
        gPreMegaTokuseiByPokeID[pokeID] = prevTokusei;
    }
    if (clientID == 0) {
        gPlayerMegaSpecies = entry->species;
        gPlayerMegaForm = entry->form;
        gPlayerPreMegaTokusei = prevTokusei;
    }
    gCurrentEvoSpecies = entry->species;
    gCurrentEvoForm = entry->form;
    gCurrentEvoClientID = clientID;
    gAnyMegaActiveInBattle = true;

    const uint16_t accessoryId = (clientID == 0) ? ownedMegaAccessoryInBag() : kMegaRing;
    const uint16_t displayAccessory = (accessoryId != 0) ? accessoryId : kMegaRing;

    if (container->fields.m_section_FromEvent_Message != nullptr) {
        if (auto* msgDesc = Dpr::Battle::Logic::Section_FromEvent_Message::Description::newInstance();
            msgDesc != nullptr && msgDesc->fields.message != nullptr) {
            msgDesc->fields.pokeID = pokeID;
            msgDesc->fields.isDisplayTokuseiWindow = false;
            msgDesc->fields.message->Setup(Dpr::Battle::Logic::BtlStrType::BTL_STRTYPE_SET, BTL_STRID_SET::MegaEvoStart);
            msgDesc->fields.message->AddArg(pokeID);
            msgDesc->fields.message->AddArg(clientID);
            msgDesc->fields.message->AddArg(param->GetItem());
            msgDesc->fields.message->AddArg(displayAccessory);

            if (auto* msgResult = Dpr::Battle::Logic::Section_FromEvent_Message::Result::newInstance();
                msgResult != nullptr) {
                container->fields.m_section_FromEvent_Message->Execute(msgResult, &msgDesc);
            }
        }
    }

    if (container->fields.m_section_FromEvent_FormChange != nullptr) {
        if (auto* formDesc = Dpr::Battle::Logic::Section_FromEvent_FormChange::Description::newInstance();
            formDesc != nullptr && formDesc->fields.successMessage != nullptr) {
            formDesc->fields.pokeID = pokeID;
            formDesc->fields.formNo = static_cast<uint8_t>(entry->form);
            formDesc->fields.isDontResetFormByOut = true;
            formDesc->fields.isEnableInCaseOfDead = false;
            formDesc->fields.isDisplayTokuseiWindow = false;
            formDesc->fields.isDisplayChangeEffect = true;
            formDesc->fields.successMessage->Setup(Dpr::Battle::Logic::BtlStrType::BTL_STRTYPE_SET, BTL_STRID_SET::MegaEvo);
            formDesc->fields.successMessage->AddArg(pokeID);
            formDesc->fields.successMessage->AddArg(pokeID);

            system_load_typeinfo(0x2c56);
            if (auto* formResult = Dpr::Battle::Logic::Section_FromEvent_FormChange::Result::newInstance();
                formResult != nullptr) {
                container->fields.m_section_FromEvent_FormChange->Execute(formResult, &formDesc);
            }
        }
    }

    // Clear transient message-override context so later unrelated messages are never rewritten.
    gCurrentEvoSpecies = 0;
    gCurrentEvoForm = 0;
    gCurrentEvoClientID = 0;

    const uint16_t megaAbility = static_cast<uint16_t>(entry->ability);
    param->fields.m_tokusei = megaAbility;
    if (param->fields.m_coreParam != nullptr) {
        param->fields.m_coreParam->fields.defaultTokusei = megaAbility;
    }
    if (auto* srcPoke = reinterpret_cast<Pml::PokePara::CoreParam::Object*>(param->GetSrcData());
        srcPoke != nullptr && !srcPoke->IsNull() && srcPoke->fields.m_accessor != nullptr) {
        srcPoke->fields.m_accessor->SetTokuseiNo(megaAbility);
    }

    auto* msgSec = reinterpret_cast<Dpr::Battle::Logic::Section::Object*>(container->fields.m_section_FromEvent_Message);
    if (msgSec != nullptr && msgSec->fields.m_pServerCmdPutter != nullptr) {
        msgSec->fields.m_pServerCmdPutter->PublishClientInformation_AppeardPokemon(&param);
    }

    if (msgSec != nullptr && msgSec->fields.m_pBattleEnv != nullptr) {
        auto* battleEnv = reinterpret_cast<Dpr::Battle::Logic::BattleEnv::Object*>(msgSec->fields.m_pBattleEnv);
        swapAbilityEventFactorInPlace(battleEnv, pokeID, prevTokusei, megaAbility);
    }

    if (msgSec != nullptr && msgSec->fields.m_pEventSystem != nullptr) {
        auto* eventSys = reinterpret_cast<Dpr::Battle::Logic::EventSystem::Object*>(msgSec->fields.m_pEventSystem);
        // Use CHANGE_TOKUSEI_AFTER + MEMBER_IN_EVO + MEMBER_IN_COMP instead of MEMBER_IN so Mega Evolving
        // triggers new Mega abilities (Drought/Drizzle/Intimidate/Trace/Delta Stream/etc.) WITHOUT
        // re-triggering Stealth Rock, Spikes, Toxic Spikes, or Sticky Web entry hazards.
        eventSys->EVENTVAR_Push();
        eventSys->EVENTVAR_SetConstValue(Dpr::Battle::Logic::EventVar::Label::POKEID, param->GetID());
        eventSys->CallEvent(Dpr::Battle::Logic::EventID::CHANGE_TOKUSEI_AFTER);
        eventSys->EVENTVAR_Pop();

        eventSys->EVENTVAR_Push();
        eventSys->EVENTVAR_SetConstValue(Dpr::Battle::Logic::EventVar::Label::POKEID, param->GetID());
        eventSys->CallEvent(Dpr::Battle::Logic::EventID::MEMBER_IN_EVO);
        eventSys->EVENTVAR_Pop();

        eventSys->EVENTVAR_Push();
        eventSys->EVENTVAR_SetConstValue(Dpr::Battle::Logic::EventVar::Label::POKEID, param->GetID());
        eventSys->CallEvent(Dpr::Battle::Logic::EventID::MEMBER_IN_COMP);
        eventSys->EVENTVAR_Pop();
    }
    return true;
}
} // namespace mega

HOOK_DEFINE_TRAMPOLINE(ItemData$$GetParam) {
    static int32_t Callback(uint16_t itemno, Pml::Item::ItemData::PrmID prmID, const void* methodInfo) {
        if (!mega::gPostInitHooksInstalled.load(std::memory_order_acquire)) {
            mega::installSharedPostInitHooks();
        }
        if (!mega::gItemTablePatched.load(std::memory_order_acquire)) {
            mega::patchItemTableInMemory();
        }
        if (mega::gAnyMegaActiveInBattle && mega::isBattleEnded()) {
            mega::revertMegaFormInParty();
        }
        const auto* stone = mega::findMegaStoneCatalog(itemno);
        const bool isKey = mega::isMegaKeyItem(itemno);
        if (prmID == Pml::Item::ItemData::PrmID::INACTIVE && (stone != nullptr || isKey)) {
            return 0;
        }
        if (prmID == Pml::Item::ItemData::PrmID::ICONID) {
            if (stone != nullptr) {
                if (mega::isNativeGen6MegaStone(itemno)) {
                    const int32_t nativeIcon = Orig(itemno, prmID, methodInfo);
                    if (nativeIcon > 0) {
                        return nativeIcon;
                    }
                }
                return Orig(stone->fallbackIconId, prmID, methodInfo);
            }
            if (isKey) {
                const int32_t nativeIcon = Orig(itemno, prmID, methodInfo);
                return (nativeIcon > 0) ? nativeIcon : Orig(mega::kOddKeystone, prmID, methodInfo);
            }
        }
        return Orig(itemno, prmID, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(BTL_POKEPARAM$$GetValue) {
    static int32_t Callback(Dpr::Battle::Logic::BTL_POKEPARAM::Object* __this,
                            Dpr::Battle::Logic::BTL_POKEPARAM::ValueID vid,
                            const void* methodInfo) {
        const int32_t origVal = Orig(__this, vid, methodInfo);
        // Never override BPP_TOKUSEI_EFFECTIVE (preserves Gastro Acid, Mold Breaker, Neutralizing Gas,
        // Skill Swap, Mummy, Worry Seed, and Mega Alakazam's Trace). Only patch BPP_TOKUSEI if
        // FormChange has not yet updated m_tokusei away from the pre-Mega ability.
        if (!mega::gInMegaTokuseiSwap && __this != nullptr &&
            vid == Dpr::Battle::Logic::BTL_POKEPARAM::ValueID::BPP_TOKUSEI) {
            if (const auto* entry = mega::findMegaByForm(__this->GetMonsNo(), __this->GetFormNo()); entry != nullptr) {
                const uint8_t pid = __this->GetID();
                const uint16_t preTok = (pid < mega::kMaxBattlePokes) ? mega::gPreMegaTokuseiByPokeID[pid] : 0;
                if (origVal == 0 || (preTok > 0 && origVal == static_cast<int32_t>(preTok))) {
                    return entry->ability;
                }
            }
        }
        return origVal;
    }
};

HOOK_DEFINE_TRAMPOLINE(CoreParam$$GetTokuseiNo) {
    static uint16_t Callback(Pml::PokePara::CoreParam::Object* __this, const void* methodInfo) {
        if (mega::gAnyMegaActiveInBattle && mega::isBattleEnded()) {
            mega::revertMegaFormInParty();
        }
        const uint16_t origVal = Orig(__this, methodInfo);
        if (!mega::gInMegaTokuseiSwap && mega::gAnyMegaActiveInBattle && !mega::isBattleEnded() &&
            __this != nullptr && !__this->IsNull()) {
            if (const auto* entry = mega::findMegaByForm(__this->GetMonsNo(), __this->GetFormNo()); entry != nullptr) {
                if (origVal == 0 || mega::isKnownPreMegaTokusei(origVal)) {
                    return static_cast<uint16_t>(entry->ability);
                }
            }
        }
        return origVal;
    }
};

HOOK_DEFINE_TRAMPOLINE(PersonalSystem$$GetPersonalData) {
    static XLSXContent::PersonalTable::SheetPersonal::Object* Callback(
        int32_t monsno, uint16_t formno, const void* methodInfo) {
        if (formno != mega::kBaseForm) {
            if (const auto* md = mega::findMegaByForm(monsno, formno); md != nullptr) {
                auto* base = Orig(monsno, mega::kBaseForm, methodInfo);
                if (base != nullptr && base->klass != nullptr) {
                    auto* clone = reinterpret_cast<XLSXContent::PersonalTable::SheetPersonal::Object*>(
                        il2cpp_object_new(reinterpret_cast<Il2CppClass*>(base->klass)));
                    if (clone != nullptr) {
                        std::memcpy(&clone->fields, &base->fields,
                                    sizeof(XLSXContent::PersonalTable::SheetPersonal::Fields));
                        // Mega Evolution NEVER changes Base HP; keep base->fields.basic_hp intact
                        // so Luminescent Platinum base-HP buffs (e.g. Absol) never drop HP on Mega Evolve.
                        clone->fields.basic_atk = static_cast<uint8_t>(md->attack);
                        clone->fields.basic_def = static_cast<uint8_t>(md->defense);
                        clone->fields.basic_spatk = static_cast<uint8_t>(md->specialAttack);
                        clone->fields.basic_spdef = static_cast<uint8_t>(md->specialDefense);
                        clone->fields.basic_agi = static_cast<uint8_t>(md->speed);
                        clone->fields.type1 = md->primaryType;
                        clone->fields.type2 = md->secondaryType;
                        clone->fields.tokusei1 = static_cast<uint16_t>(md->ability);
                        clone->fields.tokusei2 = static_cast<uint16_t>(md->ability);
                        clone->fields.tokusei3 = static_cast<uint16_t>(md->ability);
                        clone->fields.weight = md->weight;
                        return clone;
                    }
                }
            }
        }
        return Orig(monsno, formno, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(PersonalSystem$$LoadPersonalData) {
    static void Callback(int32_t monsno, uint16_t formno, const void* methodInfo) {
        Orig(monsno, formno, methodInfo);
        if (formno != mega::kBaseForm) {
            if (const auto* md = mega::findMegaByForm(monsno, formno); md != nullptr) {
                if (auto* pClass = Pml::Personal::PersonalSystem::getClass();
                    pClass != nullptr && pClass->static_fields != nullptr) {
                    auto* cur = pClass->static_fields->s_PersonalData;
                    const bool alreadyPatched = cur != nullptr &&
                                                cur->fields.basic_atk == static_cast<uint8_t>(md->attack) &&
                                                cur->fields.basic_agi == static_cast<uint8_t>(md->speed) &&
                                                cur->fields.type1 == md->primaryType &&
                                                cur->fields.type2 == md->secondaryType;
                    if (!alreadyPatched) {
                        if (auto* patched = Pml::Personal::PersonalSystem::GetPersonalData(monsno, formno);
                            patched != nullptr) {
                            pClass->static_fields->s_PersonalData = patched;
                        }
                    }
                }
            }
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(BTL_POKEPARAM$$GetPokeType) {
    static Dpr::Battle::Logic::PokeTypePair::Object Callback(
        Dpr::Battle::Logic::BTL_POKEPARAM::Object* __this, const void* methodInfo) {
        auto origPair = Orig(__this, methodInfo);
        if (__this != nullptr) {
            const int monsNo = __this->GetMonsNo();
            const uint8_t form = __this->GetFormNo();
            if (const auto* md = mega::findMegaByForm(monsNo, form); md != nullptr) {
                // Only rewrite if origPair still reflects the un-Mega'd base form types and this Mega
                // changes type; preserves in-battle type changes (Roost, Soak, Burn Up, Reflect Type, etc.).
                uint8_t curT1 = 0, curT2 = 0, curEx = 0;
                Dpr::Battle::Logic::PokeTypePair::Split(origPair, &curT1, &curT2, &curEx);
                if (auto* basePersonal = Pml::Personal::PersonalSystem::GetPersonalData(monsNo, mega::kBaseForm);
                    basePersonal != nullptr) {
                    const bool megaChangesType = (md->primaryType != basePersonal->fields.type1 ||
                                                  md->secondaryType != basePersonal->fields.type2);
                    const bool stillBaseTypes = (curT1 == basePersonal->fields.type1 &&
                                                 curT2 == basePersonal->fields.type2);
                    if (megaChangesType && stillBaseTypes) {
                        return mega::makeDualTypePair(origPair, md->primaryType, md->secondaryType);
                    }
                }
            }
        }
        return origPair;
    }
};

HOOK_DEFINE_TRAMPOLINE(BTL_POKEPARAM$$IsMatchType) {
    static bool Callback(Dpr::Battle::Logic::BTL_POKEPARAM::Object* __this, uint8_t type, const void* methodInfo) {
        if (__this != nullptr) {
            const int monsNo = __this->GetMonsNo();
            const uint8_t form = __this->GetFormNo();
            if (mega::findMegaByForm(monsNo, form) != nullptr) {
                uint8_t t1 = 0, t2 = 0, tEx = 0;
                Dpr::Battle::Logic::PokeTypePair::Split(__this->GetPokeType(), &t1, &t2, &tEx);
                return type == t1 || type == t2 || (tEx < 18 && type == tEx);
            }
        }
        return Orig(__this, type, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(CoreParam$$GetType1) {
    static uint8_t Callback(Pml::PokePara::CoreParam::Object* __this, const void* methodInfo) {
        if (mega::gAnyMegaActiveInBattle && mega::isBattleEnded()) {
            mega::revertMegaFormInParty();
        }
        if (__this != nullptr && !__this->IsNull() && mega::gAnyMegaActiveInBattle && !mega::isBattleEnded()) {
            if (const auto* md = mega::findMegaByForm(__this->GetMonsNo(), __this->GetFormNo()); md != nullptr) {
                return md->primaryType;
            }
        }
        return Orig(__this, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(CoreParam$$GetType2) {
    static uint8_t Callback(Pml::PokePara::CoreParam::Object* __this, const void* methodInfo) {
        if (mega::gAnyMegaActiveInBattle && mega::isBattleEnded()) {
            mega::revertMegaFormInParty();
        }
        if (__this != nullptr && !__this->IsNull() && mega::gAnyMegaActiveInBattle && !mega::isBattleEnded()) {
            if (const auto* md = mega::findMegaByForm(__this->GetMonsNo(), __this->GetFormNo()); md != nullptr) {
                return md->secondaryType;
            }
        }
        return Orig(__this, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(CheckUnbreakablePokeItem) {
    static bool Callback(uint16_t monsno, uint16_t itemID, const void* methodInfo) {
        if (itemID != 0 && mega::findMegaByStone(monsno, mega::kBaseForm, itemID) != nullptr) {
            return true;
        }
        return Orig(monsno, itemID, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(DataManager$$GetPokemonCatalog) {
    static XLSXContent::PokemonInfo::SheetCatalog::Object* Callback(
        int32_t monsNo, int32_t formNo, Pml::Sex sex, bool isRare, bool isEgg, const void* methodInfo) {
        if (!isEgg) {
            if (const auto* md = mega::findMegaByForm(monsNo, formNo); md != nullptr) {
                auto* base = Orig(monsNo, mega::kBaseForm, sex, isRare, isEgg, methodInfo);
                if (base != nullptr && base->klass != nullptr) {
                    auto* clone = reinterpret_cast<XLSXContent::PokemonInfo::SheetCatalog::Object*>(
                        il2cpp_object_new(reinterpret_cast<Il2CppClass*>(base->klass)));
                    if (clone != nullptr) {
                        std::memcpy(&clone->fields, &base->fields,
                                    sizeof(XLSXContent::PokemonInfo::SheetCatalog::Fields));
                        clone->fields.FormNo = formNo;
                        clone->fields.Rare = isRare;
                        // Crash guard: ONLY swap AssetBundleName when custom Switch bundle is packed & registered in Dpr.bin.
                        if (md->hasCustomModel) {
                            const char* targetBundle = (isRare && md->shinyBundleName != nullptr)
                                                           ? md->shinyBundleName
                                                           : md->bundleName;
                            clone->fields.AssetBundleName = System::String::Create(targetBundle);
                        }
                        return clone;
                    }
                }
            }
        }
        return Orig(monsNo, formNo, sex, isRare, isEgg, methodInfo);
    }
};

// Prevents Mega Absol Z (and all 17 Legends: Z-A Megas) from showing as an Egg icon in Party UI
// when Luminescent Platinum's UIDatabase.PokemonIcon table has no icon row for their Mega formNo.
HOOK_DEFINE_TRAMPOLINE(UIManager$$GetPokemonIconData) {
    static XLSXContent::UIDatabase::SheetPokemonIcon::Object* Callback(
        Dpr::UI::UIManager::Object* __this,
        int32_t monsNo, uint16_t formNo, Pml::Sex sex, Pml::PokePara::RareType rareType, bool isEgg,
        const void* methodInfo) {
        auto* res = Orig(__this, monsNo, formNo, sex, rareType, isEgg, methodInfo);
        if (__this != nullptr && !isEgg && mega::findMegaByForm(monsNo, formNo) != nullptr) {
            const bool missingOrEgg = (res == nullptr) || (res->fields.UniqueID == 0) ||
                                      (res->fields.AssetName != nullptr &&
                                       mega::utf16ContainsAscii(res->fields.AssetName, "0000"));
            if (missingOrEgg) {
                return Orig(__this, monsNo, mega::kBaseForm, sex, rareType, false, methodInfo);
            }
        }
        return res;
    }
};

// Fixes Party 3D preview & Summary showing Ditto for Mega-Evolved Pokemon WITHOUT using
// HOOK_DEFINE_INLINE (whose exlaunch inline_impl.cpp trampoline+4 skip & unsaved NEON regs
// crash on Switch ASLR when subsdk2 is >128MB from String::Concat).
HOOK_DEFINE_TRAMPOLINE(AssetManager$$AppendAssetBundleRequest) {
    static SmartPoint::AssetAssistant::AssetRequestOperation::Object* Callback(
        System::String::Object* assetBundleName, bool loadAllAssets, void* callback,
        System::String::Array* variants, const void* methodInfo) {
        auto* resolved = mega::redirectCustomMegaFieldBundlePath(assetBundleName);
        return Orig(resolved, loadAllAssets, callback, variants, methodInfo);
    }
};

// Paired UnloadAssetBundle redirect so AssetBundle refcounts stay balanced (zero leaks).
HOOK_DEFINE_TRAMPOLINE(AssetManager$$UnloadAssetBundle) {
    static int32_t Callback(System::String::Object* assetBundleName, const void* methodInfo) {
        auto* resolved = mega::redirectCustomMegaFieldBundlePath(assetBundleName);
        return Orig(resolved, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(MessageManager$$GetSimpleMessage) {
    static System::String::Object* Callback(Dpr::Message::MessageManager::Object* __this,
                                            System::String::Object* fileName,
                                            System::String::Object* label,
                                            const void* methodInfo) {
        if (mega::gCurrentEvoForm > 0 && label != nullptr) {
            if (const auto* md = mega::findMegaByForm(mega::gCurrentEvoSpecies, mega::gCurrentEvoForm); md != nullptr) {
                char msgBuf[192];
                const bool isPlayer = (mega::gCurrentEvoClientID == 0);
                if (mega::utf16ContainsAscii(label, "1572") || mega::utf16ContainsAscii(label, "megaevostart")) {
                    if (md->requiredMoveId > 0 && md->stoneItemId == 0) {
                        std::snprintf(msgBuf, sizeof(msgBuf),
                                      isPlayer ? "Your fervent wish has reached %s!"
                                               : "A fervent wish has reached the opposing %s!",
                                      md->speciesName);
                    } else if (isPlayer) {
                        const uint16_t accId = mega::ownedMegaAccessoryInBag();
                        const char* accName = (accId == mega::kMegaBracelet) ? "Mega Bracelet"
                                            : (accId == mega::kKeyStone)     ? "Key Stone"
                                                                             : "Mega Ring";
                        std::snprintf(msgBuf, sizeof(msgBuf), "%s's %s is reacting to your %s!",
                                      md->speciesName, md->stoneName, accName);
                    } else {
                        std::snprintf(msgBuf, sizeof(msgBuf),
                                      "The opposing %s's %s is reacting to its trainer's Mega Ring!",
                                      md->speciesName, md->stoneName);
                    }
                    return System::String::Create(msgBuf);
                }
                if (mega::utf16ContainsAscii(label, "1576") || mega::utf16ContainsAscii(label, "megaevo")) {
                    std::snprintf(msgBuf, sizeof(msgBuf),
                                  isPlayer ? "%s has Mega Evolved into %s!"
                                           : "The opposing %s has Mega Evolved into %s!",
                                  md->speciesName, md->megaName);
                    return System::String::Create(msgBuf);
                }
            }
        }
        if (const char* custom = mega::lookupMegaItemTextByLabel(fileName, label); custom != nullptr) {
            return System::String::Create(custom);
        }
        return Orig(__this, fileName, label, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(MessageManager$$GetNameMessage) {
    static System::String::Object* Callback(Dpr::Message::MessageManager::Object* __this,
                                            System::String::Object* fileName,
                                            System::String::Object* label,
                                            const void* methodInfo) {
        if (const char* custom = mega::lookupMegaItemTextByLabel(fileName, label); custom != nullptr) {
            return System::String::Create(custom);
        }
        return Orig(__this, fileName, label, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(UIText$$SetupMessage_Index) {
    static void Callback(Dpr::UI::UIText::Object* __this,
                         System::String::Object* messageFile,
                         int32_t messageIndex,
                         const void* methodInfo) {
        Orig(__this, messageFile, messageIndex, methodInfo);
        if (__this == nullptr || messageFile == nullptr || messageIndex <= 0) {
            return;
        }
        const bool isItemName = mega::utf16ContainsAscii(messageFile, "itemname");
        const bool isItemInfo = mega::utf16ContainsAscii(messageFile, "iteminfo");
        if (!isItemName && !isItemInfo) {
            return;
        }
        if (const char* custom = mega::lookupMegaItemTextById(messageIndex, isItemInfo); custom != nullptr) {
            __this->virtual_set_text(System::String::Create(custom));
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(UIText$$SetupMessage_Label) {
    static void Callback(Dpr::UI::UIText::Object* __this,
                         System::String::Object* messageFile,
                         System::String::Object* messageId,
                         const void* methodInfo) {
        Orig(__this, messageFile, messageId, methodInfo);
        if (__this == nullptr || messageId == nullptr) {
            return;
        }
        if (const char* custom = mega::lookupMegaItemTextByLabel(messageFile, messageId); custom != nullptr) {
            __this->virtual_set_text(System::String::Create(custom));
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$Initialize) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this,
                         Dpr::Battle::Logic::BTL_POKEPARAM::Object* bpp,
                         uint8_t pokeIndex,
                         Dpr::Battle::Logic::BTL_ACTION_PARAM_OBJ::Object* dest,
                         const void* methodInfo) {
        const bool eligible = mega::targetMegaEntry(bpp) != nullptr;
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !eligible);

        Orig(__this, bpp, pokeIndex, dest, methodInfo);

        mega::gMegaArmed = false;
        mega::updateMegaBattleBanner(__this, true);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnShow) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this, const void* methodInfo) {
        if (__this != nullptr) {
            const bool eligible = mega::targetMegaEntry(__this->fields._btlPokeParam) != nullptr;
            FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !eligible);
        }

        Orig(__this, methodInfo);

        mega::gMegaArmed = false;
        mega::updateMegaBattleBanner(__this, true);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnUpdate) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this, float deltaTime, const void* methodInfo) {
        if (__this != nullptr &&
            __this->fields._IsShow_k__BackingField &&
            __this->fields._IsFocus_k__BackingField &&
            mega::targetMegaEntry(__this->fields._btlPokeParam) != nullptr) {
            constexpr int32_t kToggleMask =
                GameController::ButtonMask::R |
                GameController::ButtonMask::L |
                GameController::ButtonMask::X |
                GameController::ButtonMask::Plus |
                GameController::ButtonMask::StickL |
                GameController::ButtonMask::StickR;

            if (Dpr::Battle::View::BtlvInput::GetPush(kToggleMask, true)) {
                mega::gMegaArmed = !mega::gMegaArmed;
                if (auto* bvCls = Dpr::Battle::View::BattleViewCore::getClass(); bvCls != nullptr) {
                    bvCls->initIfNeeded();
                    if (auto* core = Dpr::Battle::View::BattleViewCore::get_Instance();
                        core != nullptr && core->fields._UISystem_k__BackingField != nullptr) {
                        core->fields._UISystem_k__BackingField->PlaySe(mega::kSeUiSelect);
                    }
                }
            }
        }

        Orig(__this, deltaTime, methodInfo);

        if (__this != nullptr && __this->fields._IsShow_k__BackingField) {
            mega::updateMegaBattleBanner(__this, false);
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnSubmit) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this, const void* methodInfo) {
        Orig(__this, methodInfo);
        if (__this == nullptr) {
            return;
        }
        auto* destActionParam = __this->fields._destActionParam;
        if (destActionParam != nullptr &&
            mega::gMegaArmed &&
            mega::targetMegaEntry(__this->fields._btlPokeParam) != nullptr) {
            destActionParam->fields.value.fields.raw |= static_cast<long>(mega::kMegaFlagMask);
        }
        if (__this->fields._IsValid_k__BackingField) {
            mega::gMegaArmed = false;
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(ProcessActionCore$$action) {
    static void Callback(Dpr::Battle::Logic::Section_ProcessActionCore::Object* __this,
                         Dpr::Battle::Logic::PokeAction::Object* pokeAction,
                         const void* methodInfo) {
        if (__this != nullptr && pokeAction != nullptr &&
            pokeAction->fields.actionCategory == Dpr::Battle::Logic::PokeActionCategory::Mega_Evolution) {
            auto* container = reinterpret_cast<Dpr::Battle::Logic::SectionContainer::Object*>(
                __this->fields.m_pSectionContainer);
            if (mega::executeMegaEvolution(container, pokeAction)) {
                const uint8_t cid = pokeAction->fields.clientID;
                if (cid < mega::kMaxBattleClients) {
                    mega::gClientMegaUsed[cid] = true;
                }
                // Only disable the player's Mega button when the player (clientID == 0) Mega Evolves.
                if (cid == 0) {
                    FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, true);
                }
            }
            return;
        }
        // Never mutate m_formNo or call ChangeFormNo mid-battle when fainting — let BDSP finish
        // fainting/switching cleanly, and revert Mega form safely once the battle ends.
        Orig(__this, pokeAction, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(createPokeAction_FromClientInstruction) {
    static void Callback(Dpr::Battle::Logic::Section_StoreActions::Object* __this,
                         Dpr::Battle::Logic::PokeActionContainer::Object* actionContainer,
                         Dpr::Battle::Logic::SVCL_ACTION::Object* instructions,
                         uint8_t clientID,
                         const void* methodInfo) {
        system_load_typeinfo(0x78dd);
        system_load_typeinfo(0x78c7);

        if (__this == nullptr || actionContainer == nullptr || instructions == nullptr) {
            Orig(__this, actionContainer, instructions, clientID, methodInfo);
            return;
        }

        auto* sec = reinterpret_cast<Dpr::Battle::Logic::Section::Object*>(__this);
        auto* party = sec->GetPokeParty(clientID);
        const uint8_t numAction = Dpr::Battle::Logic::SVCL_ACTION::GetNumAction(instructions, clientID);
        const bool clientUsedMega = (clientID < mega::kMaxBattleClients) ? mega::gClientMegaUsed[clientID] : true;

        // For NPC trainers (clientID > 0), lazily equip their ace Mega Stone once via BDSP's verified
        // live BTL_PARTY, then trigger Mega Evolution right before their Mega-capable Pokemon's first Fight move.
        int trainerMegaSlot = -1;
        if (clientID > 0 && clientID < mega::kMaxBattleClients && mega::gIsTrainerBattle && mega::gMegaBattleAllowed) {
            const auto comp = sec->GetCompetitor();
            if (comp == Dpr::Battle::Logic::BtlCompetitor::BTL_COMPETITOR_TRAINER ||
                comp == Dpr::Battle::Logic::BtlCompetitor::BTL_COMPETITOR_INST) {
                mega::equipTrainerMegaStonesForClient(party, clientID);
                if (!clientUsedMega) {
                    for (uint8_t idx = 0; idx < numAction; ++idx) {
                        auto* rawInst = Dpr::Battle::Logic::SVCL_ACTION::Get(instructions, clientID, idx);
                        if (rawInst == nullptr) {
                            continue;
                        }
                        Dpr::Battle::Logic::BTL_ACTION_PARAM::Object probe{};
                        probe.fields.raw = rawInst->fields.raw;
                        if (mega::getActionGenCmd(&probe) != mega::kBtlActionFight) {
                            continue;
                        }
                        auto* bpp = __this->getActionPoke(&probe, clientID);
                        if (mega::targetTrainerMegaEntry(bpp, clientID) != nullptr) {
                            trainerMegaSlot = static_cast<int>(idx);
                            break;
                        }
                    }
                }
            }
        }

        bool anyMega = (trainerMegaSlot >= 0);
        if (!anyMega) {
            for (uint8_t idx = 0; idx < numAction; ++idx) {
                auto* rawInst = Dpr::Battle::Logic::SVCL_ACTION::Get(instructions, clientID, idx);
                if (rawInst != nullptr && mega::getMegaFlag(*rawInst)) {
                    anyMega = true;
                    break;
                }
            }
        }

        if (!anyMega) {
            Orig(__this, actionContainer, instructions, clientID, methodInfo);
            return;
        }

        if (party != nullptr && numAction != 0) {
            bool queuedMegaForClient = false;
            for (uint8_t idx = 0; idx < numAction; ++idx) {
                auto* rawInst = Dpr::Battle::Logic::SVCL_ACTION::Get(instructions, clientID, idx);
                if (rawInst == nullptr) {
                    continue;
                }
                Dpr::Battle::Logic::BTL_ACTION_PARAM::Object inst{};
                inst.fields.raw = rawInst->fields.raw;
                if (static_cast<int>(idx) == trainerMegaSlot) {
                    inst.fields.raw |= static_cast<long>(mega::kMegaFlagMask);
                }

                if (mega::getMegaFlag(inst)) {
                    if (!queuedMegaForClient && !clientUsedMega) {
                        auto* megaAction = Dpr::Battle::Logic::PokeAction::newInstance();
                        if (megaAction != nullptr &&
                            __this->setupPokeAction_FromClientInstruction(megaAction, &inst, clientID)) {
                            actionContainer->Add(&megaAction);
                            queuedMegaForClient = true;
                        }
                    }
                    inst.fields.raw &= ~static_cast<long>(mega::kMegaFlagMask);
                }

                auto* normalAction = Dpr::Battle::Logic::PokeAction::newInstance();
                if (normalAction != nullptr &&
                    __this->setupPokeAction_FromClientInstruction(normalAction, &inst, clientID)) {
                    actionContainer->Add(&normalAction);
                }
            }
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(setupPokeAction_FromClientInstruction) {
    static bool Callback(Dpr::Battle::Logic::Section_StoreActions::Object* __this,
                         Dpr::Battle::Logic::PokeAction::Object* pokeAction,
                         Dpr::Battle::Logic::BTL_ACTION_PARAM::Object* clientInstruction,
                         uint8_t clientID,
                         const void* methodInfo) {
        if (__this == nullptr || pokeAction == nullptr || clientInstruction == nullptr) {
            return Orig(__this, pokeAction, clientInstruction, clientID, methodInfo);
        }
        Dpr::Battle::Logic::BTL_ACTION_PARAM::Object instruction{};
        instruction.fields.raw = clientInstruction->fields.raw;

        if (mega::getMegaFlag(instruction)) {
            auto* actionPoke = __this->getActionPoke(clientInstruction, clientID);
            if (actionPoke == nullptr) {
                return false;
            }
            pokeAction->fields.actionCategory = Dpr::Battle::Logic::PokeActionCategory::Mega_Evolution;
            pokeAction->fields.bpp = actionPoke;
            pokeAction->fields.priority = 2;
            pokeAction->fields.clientID = clientID;
            pokeAction->fields.fDone = false;
            pokeAction->fields.fIntrCheck = false;
            pokeAction->fields.fRecalcPriority = false;

            auto* battleEnv = reinterpret_cast<Dpr::Battle::Logic::BattleEnv::Object*>(__this->fields.m_pBattleEnv);
            if (battleEnv != nullptr && battleEnv->fields.m_actionSerialNoManager != nullptr &&
                pokeAction->fields.actionDesc != nullptr) {
                battleEnv->fields.m_actionSerialNoManager->AssignSerialNo(pokeAction->fields.actionDesc);
            }
            return true;
        }

        return Orig(__this, pokeAction, clientInstruction, clientID, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(CalcActionPriority$$Execute) {
    static void Callback(Dpr::Battle::Logic::Section_CalcActionPriority::Object* __this,
                         Dpr::Battle::Logic::Section_CalcActionPriority::Result::Object* pResult,
                         Dpr::Battle::Logic::Section_CalcActionPriority::Description::Object** description,
                         const void* methodInfo) {
        if (pResult != nullptr && description != nullptr && *description != nullptr) {
            auto* pokeAction = (*description)->fields.pokeAction;
            if (pokeAction != nullptr &&
                pokeAction->fields.actionCategory == Dpr::Battle::Logic::PokeActionCategory::Mega_Evolution) {
                constexpr uint8_t operationPri = 2;
                constexpr uint8_t dominantPri = 2;
                constexpr uint8_t wazaPri = 0;
                int32_t rawSpeed = 0;
                if (pokeAction->fields.bpp != nullptr) {
                    rawSpeed = pokeAction->fields.bpp->GetValue(
                        Dpr::Battle::Logic::BTL_POKEPARAM::ValueID::BPP_AGILITY);
                }
                const uint8_t agility = (rawSpeed > 0)
                                            ? static_cast<uint8_t>((rawSpeed >> 2) > 255 ? 255 : (rawSpeed >> 2))
                                            : 0;
                const auto spPri = static_cast<uint8_t>((*description)->fields.specialPriority);
                pResult->fields.priority = Dpr::Battle::Logic::ActPri::Make(dominantPri, operationPri, wazaPri, spPri, agility);
                return;
            }
        }
        Orig(__this, pResult, description, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(SetupBattleWild) {
    static void Callback(Dpr::Battle::Logic::BATTLE_SETUP_PARAM::Object* battleSetupParam,
                         Pml::PokeParty::Object* iPtrEnemyParty, int32_t arenaID, int32_t mapAttrib, int32_t weatherType,
                         bool isSwim, bool isFishing, int32_t partnerID, bool isCaptureDemo, int32_t safariBallNum,
                         bool isSymbol, bool isMitu, System::String::Object* overlapBgm, int32_t overlapSetupEffectId,
                         bool isCantUseBall, const void* methodInfo) {
        mega::revertMegaFormInParty();
        mega::gIsTrainerBattle = false;
        const bool isSafari = (battleSetupParam != nullptr &&
                               battleSetupParam->fields.rule == Dpr::Battle::Logic::BtlRule::BTL_RULE_SAFARI);
        mega::gMegaBattleAllowed = !(isCaptureDemo || isSafari);
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !mega::gMegaBattleAllowed);
        Orig(battleSetupParam, iPtrEnemyParty, arenaID, mapAttrib, weatherType, isSwim, isFishing, partnerID,
             isCaptureDemo, safariBallNum, isSymbol, isMitu, overlapBgm, overlapSetupEffectId, isCantUseBall,
             methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(SetupBattleTrainer) {
    static void Callback(Dpr::Battle::Logic::BATTLE_SETUP_PARAM::Object* battleSetupParam, int32_t arenaID,
                         int32_t mapAttrib, int32_t weatherType, int32_t rule, int32_t enemyID0, int32_t enemyID1,
                         int32_t partnerID, const void* methodInfo) {
        if (!mega::gItemTablePatched.load(std::memory_order_acquire)) {
            mega::patchItemTableInMemory();
        }
        mega::revertMegaFormInParty();
        mega::gIsTrainerBattle = true;
        mega::gMegaBattleAllowed = !(rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_SAFARI) ||
                                     rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_RAID));
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !mega::gMegaBattleAllowed);
        Orig(battleSetupParam, arenaID, mapAttrib, weatherType, rule, enemyID0, enemyID1, partnerID, methodInfo);
    }
};

namespace mega {
void installSharedPostInitHooks() {
    if (gPostInitHooksInstalled.exchange(true, std::memory_order_acq_rel)) {
        return;
    }

    // All hooks shared with Luminescent Platinum subsdk1 MUST install here so subsdk2 chains cleanly
    // AFTER subsdk1's exl_main() finishes (preventing ARM64 16-byte trampoline stub collisions).
    BTL_POKEPARAM$$GetValue::InstallAtOffset(0x01fdd010);
    BTL_POKEPARAM$$GetPokeType::InstallAtOffset(0x01fdfa80);
    BTL_POKEPARAM$$IsMatchType::InstallAtOffset(0x01fdfad0);
    CoreParam$$GetTokuseiNo::InstallAtOffset(0x020488a0);
    CoreParam$$GetType1::InstallAtOffset(0x0204a9f0);
    CoreParam$$GetType2::InstallAtOffset(0x0204aaf0);
    PersonalSystem$$GetPersonalData::InstallAtOffset(0x024a0ca0);
    PersonalSystem$$LoadPersonalData::InstallAtOffset(0x024a0d50);
    DataManager$$GetPokemonCatalog::InstallAtOffset(0x02cc76a0);
    CheckUnbreakablePokeItem::InstallAtOffset(0x01d0d780);
    MessageManager$$GetNameMessage::InstallAtOffset(0x0210ce50);
    BUIWazaList$$Initialize::InstallAtOffset(0x01d2b800);
    BUIWazaList$$OnShow::InstallAtOffset(0x01d2cb70);
    BUIWazaList$$OnUpdate::InstallAtOffset(0x01d2c490);
    SetupBattleWild::InstallAtOffset(0x02c3abc0);
    SetupBattleTrainer::InstallAtOffset(0x02c3b800);
}
} // namespace mega

extern "C" void exl_main(void*, void*) {
    exl::hook::Initialize();

    // Strictly non-colliding hooks installed single-threaded before nnMain (zero subsdk1 overlap).
    ItemData$$GetParam::InstallAtOffset(0x0249b950);
    UIManager$$GetPokemonIconData::InstallAtOffset(0x017c1250);
    AssetManager$$AppendAssetBundleRequest::InstallAtOffset(0x022dbfb0);
    AssetManager$$UnloadAssetBundle::InstallAtOffset(0x022dbde0);
    MessageManager$$GetSimpleMessage::InstallAtOffset(0x0210d000);
    UIText$$SetupMessage_Label::InstallAtOffset(0x01dd18d0);
    UIText$$SetupMessage_Index::InstallAtOffset(0x01dd1940);
    BUIWazaList$$OnSubmit::InstallAtOffset(0x01d2c6e0);
    CalcActionPriority$$Execute::InstallAtOffset(0x021ad570);
    ProcessActionCore$$action::InstallAtOffset(0x021c0de0);
    setupPokeAction_FromClientInstruction::InstallAtOffset(0x021cdbf0);
    createPokeAction_FromClientInstruction::InstallAtOffset(0x021cdad0);
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT(0x420);
}
