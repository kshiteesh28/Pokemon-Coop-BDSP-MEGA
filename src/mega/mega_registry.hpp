#pragma once

#include <cstddef>
#include <cstdint>

namespace mega {

// Verified type IDs from src/mod/data/types.h.
constexpr uint8_t kTypeNormal   = 0;
constexpr uint8_t kTypeFighting = 1;
constexpr uint8_t kTypeFlying   = 2;
constexpr uint8_t kTypePoison   = 3;
constexpr uint8_t kTypeGround   = 4;
constexpr uint8_t kTypeRock     = 5;
constexpr uint8_t kTypeBug      = 6;
constexpr uint8_t kTypeGhost    = 7;
constexpr uint8_t kTypeSteel    = 8;
constexpr uint8_t kTypeFire     = 9;
constexpr uint8_t kTypeWater    = 10;
constexpr uint8_t kTypeGrass    = 11;
constexpr uint8_t kTypeElectric = 12;
constexpr uint8_t kTypePsychic  = 13;
constexpr uint8_t kTypeIce      = 14;
constexpr uint8_t kTypeDragon   = 15;
constexpr uint8_t kTypeDark     = 16;
constexpr uint8_t kTypeFairy    = 17;

// Verified move ID from src/mod/data/moves.h (line 624 - 4).
constexpr int32_t kDragonAscent = 620;

// Verified ability IDs from src/mod/data/abilities.h (line - 4).
// Every ID below is either native in BDSP 1.3.0 (1..267) or registered in Luminescent Platinum (Sharpness=292).
constexpr int kSpeedBoost    = 3;
constexpr int kVoltAbsorb    = 10;
constexpr int kInsomnia      = 15;
constexpr int kFlashFire     = 18;
constexpr int kIntimidate    = 22;
constexpr int kShadowTag     = 23;
constexpr int kRoughSkin     = 24;
constexpr int kLevitate      = 26;
constexpr int kLightningRod  = 31;
constexpr int kSereneGrace   = 32;
constexpr int kSwiftSwim     = 33;
constexpr int kChlorophyll   = 34;
constexpr int kTrace         = 36;
constexpr int kHugePower     = 37;
constexpr int kInnerFocus    = 39;
constexpr int kSandStream    = 45;
constexpr int kThickFat      = 47;
constexpr int kDrought       = 70;
constexpr int kPurePower     = 74;
constexpr int kShellArmor    = 75;
constexpr int kSteadfast     = 80;
constexpr int kAdaptability  = 91;
constexpr int kSkillLink     = 92;
constexpr int kSolarPower    = 94;
constexpr int kMagicGuard    = 98;
constexpr int kNoGuard       = 99;
constexpr int kTechnician    = 101;
constexpr int kMoldBreaker   = 104;
constexpr int kFilter        = 111;
constexpr int kScrappy       = 113;
constexpr int kSnowWarning   = 117;
constexpr int kReckless      = 120;
constexpr int kBadDreams     = 123;
constexpr int kSheerForce    = 125;
constexpr int kMultiscale    = 136;
constexpr int kMagicBounce   = 156;
constexpr int kPrankster     = 158;
constexpr int kSandForce     = 159;
constexpr int kStrongJaw     = 173;
constexpr int kRefrigerate   = 174;
constexpr int kMegaLauncher  = 178;
constexpr int kToughClaws    = 181;
constexpr int kPixilate      = 182;
constexpr int kAerilate      = 184;
constexpr int kParentalBond  = 185;
constexpr int kDeltaStream   = 191;
constexpr int kSlushRush     = 202;
constexpr int kGalvanize     = 206;
constexpr int kPsychicSurge  = 227;
constexpr int kGrassySurge   = 229;
constexpr int kSharpness     = 292; // Registered natively in Luminescent Platinum sharpness.cpp

struct MegaSpeciesEntry {
    int species;
    int form;
    uint16_t stoneItemId;       // 0 only for move-triggered Mega Rayquaza
    int32_t requiredMoveId;     // 0 for stone-triggered Megas; kDragonAscent (620) for Rayquaza
    int hp, attack, defense, specialAttack, specialDefense, speed;
    int ability;
    uint8_t primaryType, secondaryType;
    uint16_t weight;            // hectograms (e.g. 1105 = 110.5 kg)
    const char* bundleName;     // Normal Switch AssetBundle name
    const char* shinyBundleName;// Shiny Switch AssetBundle name
    bool hasCustomModel;        // Guard: true ONLY when Switch bundles are packed & registered in Dpr.bin
    const char* speciesName;
    const char* megaName;
    const char* stoneName;
};

// All 63 Gen 1-4 Mega Evolutions playable in BDSP / Luminescent Platinum (46 Gen VI + 17 Gen IX Legends: Z-A).
// Forms with hasCustomModel=false safely retain their base species' 3D AssetBundle until their Z-A bundle
// is packed & registered in Dpr.bin — zero Unity AssetBundle crash risk!
constexpr MegaSpeciesEntry kMegaRegistry[] = {
    // ======================== KANTO (21) ========================
    { 3, 1, 659,  0,  80, 100, 123, 122, 120,  80, kThickFat,     kTypeGrass,    kTypePoison,   1555, "pm0003_51_00", "pm0003_51_10", true,  "Venusaur",    "Mega Venusaur",    "Venusaurite" },
    { 6, 1, 660,  0,  78, 130, 111, 130,  85, 100, kToughClaws,   kTypeFire,     kTypeDragon,   1105, "pm0006_01_00", "pm0006_01_10", true,   "Charizard",   "Mega Charizard X", "Charizardite X" },
    { 6, 2, 678,  0,  78, 104,  78, 159, 115, 100, kDrought,      kTypeFire,     kTypeFlying,   1005, "pm0006_02_00", "pm0006_02_10", true,   "Charizard",   "Mega Charizard Y", "Charizardite Y" },
    { 9, 1, 661,  0,  79, 103, 120, 135, 115,  78, kMegaLauncher, kTypeWater,    kTypeWater,    1011, "pm0009_51_00", "pm0009_51_10", true,  "Blastoise",   "Mega Blastoise",   "Blastoisinite" },
    { 15, 1, 770,  0,  65, 150,  40,  15,  80, 145, kAdaptability, kTypeBug,      kTypePoison,    405, "pm0015_51_00", "pm0015_51_10", false, "Beedrill",    "Mega Beedrill",    "Beedrillite" },
    { 18, 1, 762,  0,  83,  80,  80, 135,  80, 121, kNoGuard,      kTypeNormal,   kTypeFlying,    505, "pm0018_51_00", "pm0018_51_10", false, "Pidgeot",     "Mega Pidgeot",     "Pidgeotite" },
    { 26, 1, 1073, 0,  60, 135,  95,  90,  95, 110, kToughClaws,   kTypeElectric, kTypeElectric,  380, "pm0026_51_00", "pm0026_51_10", true,  "Raichu",      "Mega Raichu X",    "Raichunite X" },
    { 26, 2, 1078, 0,  60, 100,  55, 160,  80, 130, kGalvanize,    kTypeElectric, kTypeElectric,  260, "pm0026_52_00", "pm0026_52_10", true,  "Raichu",      "Mega Raichu Y",    "Raichunite Y" },
    { 36, 1, 837,  0,  95,  80,  93, 135, 110,  70, kMagicGuard,   kTypeFairy,    kTypeFlying,    423, "pm0036_51_00", "pm0036_51_10", false, "Clefable",    "Mega Clefable",    "Clefablite" },
    { 65, 1, 679,  0,  55,  50,  65, 175, 105, 150, kTrace,        kTypePsychic,  kTypePsychic,   480, "pm0065_51_00", "pm0065_51_10", false, "Alakazam",    "Mega Alakazam",    "Alakazite" },
    { 71, 1, 838,  0,  80, 125,  85, 135,  95,  70, kChlorophyll,  kTypeGrass,    kTypePoison,   1255, "pm0071_51_00", "pm0071_51_10", true,  "Victreebel",  "Mega Victreebel",  "Victreebelite" },
    { 80, 1, 760,  0,  95,  75, 180, 130,  80,  30, kShellArmor,   kTypeWater,    kTypePsychic,  1200, "pm0080_51_00", "pm0080_51_10", true,  "Slowbro",     "Mega Slowbro",     "Slowbronite" },
    { 94, 1, 656,  0,  60,  65,  80, 170,  95, 130, kShadowTag,    kTypeGhost,    kTypePoison,    405, "pm0094_51_00", "pm0094_51_10", false, "Gengar",      "Mega Gengar",      "Gengarite" },
    { 115, 1, 675,  0, 105, 125, 100,  60, 100, 100, kParentalBond, kTypeNormal,   kTypeNormal,   1000, "pm0115_51_00", "pm0115_51_10", true,  "Kangaskhan",  "Mega Kangaskhan",  "Kangaskhanite" },
    { 121, 1, 839,  0,  60, 140, 105, 130, 105, 120, kHugePower,    kTypeWater,    kTypePsychic,   800, "pm0121_51_00", "pm0121_51_10", false, "Starmie",     "Mega Starmie",     "Starminite" },
    { 127, 1, 671,  0,  65, 155, 120,  65,  90, 105, kAerilate,     kTypeBug,      kTypeFlying,    590, "pm0127_51_00", "pm0127_51_10", false, "Pinsir",      "Mega Pinsir",      "Pinsirite" },
    { 130, 1, 676,  0,  95, 155, 109,  70, 130,  81, kMoldBreaker,  kTypeWater,    kTypeDark,     3050, "pm0130_51_00", "pm0130_51_10", true,  "Gyarados",    "Mega Gyarados",    "Gyaradosite" },
    { 142, 1, 672,  0,  80, 135,  85,  70,  95, 150, kToughClaws,   kTypeRock,     kTypeFlying,    790, "pm0142_51_00", "pm0142_51_10", false, "Aerodactyl",  "Mega Aerodactyl",  "Aerodactylite" },
    { 149, 1, 840,  0,  91, 124, 115, 145, 125, 100, kMultiscale,   kTypeDragon,   kTypeFlying,   2900, "pm0149_51_00", "pm0149_51_10", true,  "Dragonite",   "Mega Dragonite",   "Dragoninite" },
    { 150, 1, 662,  0, 106, 190, 100, 154, 100, 130, kSteadfast,    kTypePsychic,  kTypeFighting, 1270, "pm0150_51_00", "pm0150_51_10", true,  "Mewtwo",      "Mega Mewtwo X",    "Mewtwonite X" },
    { 150, 2, 663,  0, 106, 150,  70, 194, 120, 140, kInsomnia,     kTypePsychic,  kTypePsychic,   330, "pm0150_52_00", "pm0150_52_10", true,  "Mewtwo",      "Mega Mewtwo Y",    "Mewtwonite Y" },

    // ======================== JOHTO (9) =========================
    { 154, 1, 848,  0,  80,  92, 115, 143, 115,  80, kGrassySurge,  kTypeGrass,    kTypeFairy,    2010, "pm0154_51_00", "pm0154_51_10", false, "Meganium",    "Mega Meganium",    "Meganiumite" },
    { 160, 1, 859,  0,  85, 160, 125,  89,  93,  78, kStrongJaw,    kTypeWater,    kTypeDragon,   1088, "pm0160_51_00", "pm0160_51_10", false, "Feraligatr",  "Mega Feraligatr",  "Feraligite" },
    { 181, 1, 658,  0,  90,  95, 105, 165, 110,  45, kMoldBreaker,  kTypeElectric, kTypeDragon,    615, "pm0181_51_00", "pm0181_51_10", false, "Ampharos",    "Mega Ampharos",    "Ampharosite" },
    { 208, 1, 761,  0,  75, 125, 230,  55,  95,  30, kSandForce,    kTypeSteel,    kTypeGround,   7400, "pm0208_51_00", "pm0208_51_10", false, "Steelix",     "Mega Steelix",     "Steelixite" },
    { 212, 1, 670,  0,  70, 150, 140,  65, 100,  75, kTechnician,   kTypeBug,      kTypeSteel,    1250, "pm0212_51_00", "pm0212_51_10", false, "Scizor",      "Mega Scizor",      "Scizorite" },
    { 214, 1, 680,  0,  80, 185, 115,  40, 105,  75, kSkillLink,    kTypeBug,      kTypeFighting,  625, "pm0214_51_00", "pm0214_51_10", false, "Heracross",   "Mega Heracross",   "Heracronite" },
    { 227, 1, 867,  0,  65, 140, 110,  40, 100, 110, kSharpness,    kTypeSteel,    kTypeFlying,    404, "pm0227_51_00", "pm0227_51_10", false, "Skarmory",    "Mega Skarmory",    "Skarmorite" },
    { 229, 1, 666,  0,  75,  90,  90, 140,  90, 115, kSolarPower,   kTypeDark,     kTypeFire,      495, "pm0229_51_00", "pm0229_51_10", false, "Houndoom",    "Mega Houndoom",    "Houndoominite" },
    { 248, 1, 669,  0, 100, 164, 150,  95, 120,  71, kSandStream,   kTypeRock,     kTypeDark,     2550, "pm0248_51_00", "pm0248_51_10", false, "Tyranitar",   "Mega Tyranitar",   "Tyranitarite" },

    // ======================== HOENN (22) ========================
    { 254, 1, 753,  0,  70, 110,  75, 145,  85, 145, kLightningRod, kTypeGrass,    kTypeDragon,    552, "pm0254_51_00", "pm0254_51_10", false, "Sceptile",    "Mega Sceptile",    "Sceptilite" },
    { 257, 1, 664,  0,  80, 160,  80, 130,  80, 100, kSpeedBoost,   kTypeFire,     kTypeFighting,  520, "pm0257_51_00", "pm0257_51_10", false, "Blaziken",    "Mega Blaziken",    "Blazikenite" },
    { 260, 1, 752,  0, 100, 150, 110,  95, 110,  70, kSwiftSwim,    kTypeWater,    kTypeGround,   1020, "pm0260_51_00", "pm0260_51_10", false, "Swampert",    "Mega Swampert",    "Swampertite" },
    { 282, 1, 657,  0,  68,  85,  65, 165, 135, 100, kPixilate,     kTypePsychic,  kTypeFairy,     484, "pm0282_51_00", "pm0282_51_10", false, "Gardevoir",   "Mega Gardevoir",   "Gardevoirite" },
    { 302, 1, 754,  0,  50,  85, 125,  85, 115,  20, kMagicBounce,  kTypeDark,     kTypeGhost,    1610, "pm0302_51_00", "pm0302_51_10", false, "Sableye",     "Mega Sableye",     "Sablenite" },
    { 303, 1, 681,  0,  50, 105, 125,  55,  95,  50, kHugePower,    kTypeSteel,    kTypeFairy,     235, "pm0303_51_00", "pm0303_51_10", false, "Mawile",      "Mega Mawile",      "Mawilite" },
    { 306, 1, 667,  0,  70, 140, 230,  60,  80,  50, kFilter,       kTypeSteel,    kTypeSteel,    3950, "pm0306_51_00", "pm0306_51_10", false, "Aggron",      "Mega Aggron",      "Aggronite" },
    { 308, 1, 665,  0,  60, 100,  85,  80,  85, 100, kPurePower,    kTypeFighting, kTypePsychic,   315, "pm0308_51_00", "pm0308_51_10", false, "Medicham",    "Mega Medicham",    "Medichamite" },
    { 310, 1, 682,  0,  70,  75,  80, 135,  80, 135, kIntimidate,   kTypeElectric, kTypeElectric,  440, "pm0310_51_00", "pm0310_51_10", false, "Manectric",   "Mega Manectric",   "Manectite" },
    { 319, 1, 759,  0,  70, 140,  70, 110,  65, 105, kStrongJaw,    kTypeWater,    kTypeDark,     1303, "pm0319_51_00", "pm0319_51_10", false, "Sharpedo",    "Mega Sharpedo",    "Sharpedonite" },
    { 323, 1, 767,  0,  70, 120, 100, 145, 105,  20, kSheerForce,   kTypeFire,     kTypeGround,   3205, "pm0323_51_00", "pm0323_51_10", false, "Camerupt",    "Mega Camerupt",    "Cameruptite" },
    { 334, 1, 755,  0,  75, 110, 110, 110, 105,  80, kPixilate,     kTypeDragon,   kTypeFairy,     206, "pm0334_51_00", "pm0334_51_10", false, "Altaria",     "Mega Altaria",     "Altarianite" },
    { 354, 1, 668,  0,  64, 165,  75,  93,  83,  75, kPrankster,    kTypeGhost,    kTypeGhost,     130, "pm0354_51_00", "pm0354_51_10", false, "Banette",     "Mega Banette",     "Banettite" },
    { 358, 1, 1079, 0,  75,  50, 110, 135, 120,  65, kLevitate,     kTypePsychic,  kTypeSteel,      80, "pm0358_51_00", "pm0358_51_10", false, "Chimecho",    "Mega Chimecho",    "Chimechite" },
    { 359, 1, 677,  0,  65, 150,  60, 115,  60, 115, kMagicBounce,  kTypeDark,     kTypeDark,      490, "pm0359_01_00", "pm0359_01_10", true,   "Absol",       "Mega Absol",       "Absolite" },
    { 359, 2, 622,  0,  65, 154,  60,  75,  60, 151, kSharpness,    kTypeDark,     kTypeGhost,     470, "pm0359_02_00", "pm0359_02_10", true,   "Absol",       "Mega Absol Z",     "Absolite Z" },
    { 362, 1, 763,  0,  80, 120,  80, 120,  80, 100, kRefrigerate,  kTypeIce,      kTypeIce,      3502, "pm0362_51_00", "pm0362_51_10", false, "Glalie",      "Mega Glalie",      "Glalitite" },
    { 373, 1, 769,  0,  95, 145, 130, 120,  90, 120, kAerilate,     kTypeDragon,   kTypeFlying,   1126, "pm0373_51_00", "pm0373_51_10", false, "Salamence",   "Mega Salamence",   "Salamencite" },
    { 376, 1, 758,  0,  80, 145, 150, 105, 110, 110, kToughClaws,   kTypeSteel,    kTypePsychic,  9429, "pm0376_51_00", "pm0376_51_10", false, "Metagross",   "Mega Metagross",   "Metagrossite" },
    { 380, 1, 684,  0,  80, 100, 120, 140, 150, 110, kLevitate,     kTypeDragon,   kTypePsychic,   520, "pm0380_51_00", "pm0380_51_10", false, "Latias",      "Mega Latias",      "Latiasite" },
    { 381, 1, 685,  0,  80, 130, 100, 160, 120, 110, kLevitate,     kTypeDragon,   kTypePsychic,   700, "pm0381_51_00", "pm0381_51_10", false, "Latios",      "Mega Latios",      "Latiosite" },
    { 384, 1, 0, kDragonAscent, 105, 180, 100, 180, 100, 115, kDeltaStream, kTypeDragon, kTypeFlying, 3920, "pm0384_51_00", "pm0384_51_10", false, "Rayquaza", "Mega Rayquaza", "Dragon Ascent" },

    // ======================== SINNOH (11) =======================
    { 398, 1, 1082, 0,  85, 140, 100,  60,  90, 110, kReckless,     kTypeFighting, kTypeFlying,    500, "pm0398_51_00", "pm0398_51_10", true,  "Staraptor",   "Mega Staraptor",   "Staraptite" },
    { 428, 1, 768,  0,  65, 136,  94,  54,  96, 135, kScrappy,      kTypeNormal,   kTypeFighting,  283, "pm0428_51_00", "pm0428_51_10", false, "Lopunny",     "Mega Lopunny",     "Lopunnite" },
    { 445, 1, 683,  0, 108, 170, 115, 120,  95,  92, kSandForce,    kTypeDragon,   kTypeGround,    950, "pm0445_51_00", "pm0445_51_10", true,  "Garchomp",    "Mega Garchomp",    "Garchompite" },
    { 445, 2, 1083, 0, 108, 130,  85, 141,  85, 151, kRoughSkin,    kTypeDragon,   kTypeDragon,    990, "pm0445_52_00", "pm0445_52_10", true,  "Garchomp",    "Mega Garchomp Z",  "Garchompite Z" },
    { 448, 1, 673,  0,  70, 145,  88, 140,  70, 112, kAdaptability, kTypeFighting, kTypeSteel,     575, "pm0448_51_00", "pm0448_51_10", true,  "Lucario",     "Mega Lucario",     "Lucarionite" },
    { 448, 2, 1101, 0,  70, 100,  70, 164,  70, 151, kMegaLauncher, kTypeFighting, kTypeSteel,     494, "pm0448_52_00", "pm0448_52_10", true,  "Lucario",     "Mega Lucario Z",   "Lucarionite Z" },
    { 460, 1, 674,  0,  90, 132, 105, 132, 105,  30, kSnowWarning,  kTypeGrass,    kTypeIce,      1850, "pm0460_51_00", "pm0460_51_10", true,  "Abomasnow",   "Mega Abomasnow",   "Abomasite" },
    { 475, 1, 756,  0,  68, 165,  95,  65, 115, 110, kInnerFocus,   kTypePsychic,  kTypeFighting,  564, "pm0475_51_00", "pm0475_51_10", true,  "Gallade",     "Mega Gallade",     "Galladite" },
    { 478, 1, 868,  0,  70,  80,  70, 140, 100, 120, kSlushRush,    kTypeIce,      kTypeGhost,     296, "pm0478_51_00", "pm0478_51_10", false, "Froslass",    "Mega Froslass",    "Froslassite" },
    { 485, 1, 869,  0,  91, 120, 106, 175, 141,  67, kFlashFire,    kTypeFire,     kTypeSteel,    5700, "pm0485_51_00", "pm0485_51_10", false, "Heatran",     "Mega Heatran",     "Heatranite" },
    { 491, 1, 870,  0,  70, 120, 130, 165, 130,  85, kBadDreams,    kTypeDark,     kTypeDark,     2400, "pm0491_51_00", "pm0491_51_10", true,  "Darkrai",     "Mega Darkrai",     "Darkranite" },
};

constexpr size_t kMegaRegistryCount = sizeof(kMegaRegistry) / sizeof(kMegaRegistry[0]);

constexpr bool isValidBDSPAbility(int abilityId) {
    return (abilityId >= 1 && abilityId <= 267) || abilityId == kSharpness;
}

constexpr bool validateMegaRegistry() {
    if (kMegaRegistryCount != 63) return false;
    for (size_t i = 0; i < kMegaRegistryCount; ++i) {
        const auto& e = kMegaRegistry[i];
        if (e.species <= 0 || e.species > 493 || e.form <= 0) return false;
        // Must have either a valid Mega Stone OR a required move (never both 0!)
        if (e.stoneItemId == 0 && e.requiredMoveId == 0) return false;
        if (e.stoneItemId >= 1278) return false;
        if (e.hp < 1 || e.hp > 255 || e.attack < 1 || e.attack > 255 ||
            e.defense < 1 || e.defense > 255 || e.specialAttack < 1 || e.specialAttack > 255 ||
            e.specialDefense < 1 || e.specialDefense > 255 || e.speed < 1 || e.speed > 255) return false;
        if (e.primaryType >= 18 || e.secondaryType >= 18) return false;
        if (!isValidBDSPAbility(e.ability)) return false;
        if (e.bundleName == nullptr || e.shinyBundleName == nullptr) return false;
        if (e.speciesName == nullptr || e.megaName == nullptr || e.stoneName == nullptr) return false;
        for (size_t j = i + 1; j < kMegaRegistryCount; ++j) {
            const auto& o = kMegaRegistry[j];
            if (e.species == o.species && e.form == o.form) return false;
            if (e.stoneItemId != 0 && e.stoneItemId == o.stoneItemId) return false;
        }
    }
    return true;
}

static_assert(validateMegaRegistry(), "kMegaRegistry failed compile-time safety & uniqueness audit");

} // namespace mega
