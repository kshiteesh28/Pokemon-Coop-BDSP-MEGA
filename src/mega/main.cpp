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
#include "externals/Dpr/Battle/Logic/PokeActionCategory.h"
#include "externals/Dpr/Battle/Logic/PokeActionContainer.h"
#include "externals/Dpr/Battle/Logic/SVCL_ACTION.h"
#include "externals/Dpr/Battle/Logic/BattleEnv.h"
#include "externals/Dpr/Battle/Logic/BattleProc.h"
#include "externals/Dpr/Battle/Logic/EventID.h"
#include "externals/Dpr/Battle/Logic/EventVar.h"
#include "externals/Dpr/Battle/Logic/EventSystem.h"
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
#include "externals/Pml/PokeParty.h"
#include "externals/System/Action.h"
#include "externals/System/String.h"
#include "externals/UnityEngine/Color.h"
#include "externals/UnityEngine/Component.h"
#include "externals/UnityEngine/GameObject.h"
#include "externals/UnityEngine/Transform.h"
#include "externals/UnityEngine/UI/Image.h"
#include "externals/XLSXContent/ItemTable.h"

#include <cstdint>
#include <cstring>

namespace mega {
constexpr int kCharizardSpecies = 6;
constexpr int kBaseForm = 0;
constexpr int kMegaXForm = 1;
constexpr int kMegaYForm = 2;

// Verified item indices from src/mod/data/items.h (line - 4).
constexpr uint16_t kFireStone = 82;       // Fallback BDSP stone icon sprite
constexpr uint16_t kOddKeystone = 111;    // Fallback BDSP keystone icon sprite
constexpr uint16_t kCharcoal = 249;       // Holdable item in Items pocket (field_func = 0)
constexpr uint16_t kMagmaStone = 458;     // Non-registerable Key Item (field_func = 0)
constexpr uint16_t kCharizarditeX = 660;
constexpr uint16_t kCharizarditeY = 678;
constexpr uint16_t kMegaRing = 696;
constexpr uint16_t kMegaBracelet = 744;
constexpr uint16_t kKeyStone = 773;
constexpr int kToughClaws = 181;
constexpr int kDrought = 70;

constexpr uint32_t kSeUiSelect = 0xb7533038;
constexpr uint64_t kMegaFlagBit = 34;
constexpr uint64_t kMegaFlagMask = 1ULL << kMegaFlagBit;

struct MegaFormData {
    int form;
    int hp, attack, defense, specialAttack, specialDefense, speed;
    int ability;
    int primaryType, secondaryType;
};
constexpr MegaFormData kMegaX{1, 78, 130, 111, 130, 85, 100, kToughClaws, 9, 15};
constexpr MegaFormData kMegaY{2, 78, 104, 78, 159, 115, 100, kDrought, 9, 2};

constexpr int charizardMegaForm(int species, int originalForm,
                                int heldItem, int charizarditeX,
                                int charizarditeY, bool hasMegaAccessoryInBag,
                                bool trainerAlreadyMegaEvolved) {
    if (species != kCharizardSpecies || originalForm != kBaseForm ||
        !hasMegaAccessoryInBag || trainerAlreadyMegaEvolved) {
        return -1;
    }
    if (heldItem == charizarditeX && charizarditeX != charizarditeY)
        return kMegaXForm;
    if (heldItem == charizarditeY && charizarditeX != charizarditeY)
        return kMegaYForm;
    return -1;
}

static_assert(charizardMegaForm(6, 0, kCharizarditeX, kCharizarditeX, kCharizarditeY, true, false) == kMegaXForm);
static_assert(charizardMegaForm(6, 0, kCharizarditeY, kCharizarditeX, kCharizarditeY, true, false) == kMegaYForm);
static_assert(charizardMegaForm(6, 0, 679, kCharizarditeX, kCharizarditeY, true, false) == -1);
static_assert(charizardMegaForm(6, 0, kCharizarditeX, kCharizarditeX, kCharizarditeY, false, false) == -1);
static_assert(charizardMegaForm(6, 0, kCharizarditeX, kCharizarditeX, kCharizarditeY, true, true) == -1);
static_assert(kMegaRing == 696);
static_assert(kMegaBracelet == 744);
static_assert(kKeyStone == 773);
static_assert(kMegaX.ability == kToughClaws && kMegaY.ability == kDrought);

bool gPostInitHooksInstalled = false;
bool gItemTablePatched = false;
bool gMegaBattleAllowed = true;
bool gMegaUsedThisBattle = false;
bool gMegaArmed = false;
int gActiveMegaForm = 0;
bool gInMegaTokuseiSwap = false;
uint16_t gPreMegaTokusei = 66; // Saved dynamically before Mega Evolution (default Blaze)
uint8_t gMegaBattlePokeID = 0xFF;

inline bool isBattleEnded() {
    auto* procClass = Dpr::Battle::Logic::BattleProc::getClass();
    if (procClass == nullptr || procClass->static_fields == nullptr) {
        return true;
    }
    return procClass->static_fields->_isEnd || !procClass->static_fields->_isInitialized;
}

bool gInMegaRevert = false;

inline void revertMegaFormInParty() {
    if (gInMegaRevert) {
        return;
    }
    gInMegaRevert = true;
    PlayerWork::getClass()->initIfNeeded();
    if (auto* party = PlayerWork::get_playerParty(); party != nullptr) {
        const uint32_t count = party->fields.m_memberCount;
        for (uint32_t i = 0; i < count && i < 6; ++i) {
            auto* member = reinterpret_cast<Pml::PokePara::CoreParam::Object*>(party->GetMemberPointer(i));
            if (member == nullptr || member->IsNull()) {
                continue;
            }
            if (member->GetMonsNo() == kCharizardSpecies) {
                const uint16_t form = member->GetFormNo();
                if (form == kMegaXForm || form == kMegaYForm) {
                    member->ChangeFormNo(kBaseForm, nullptr);
                    if (member->fields.m_accessor != nullptr) {
                        member->fields.m_accessor->SetTokuseiNo(gPreMegaTokusei);
                    }
                }
            }
        }
    }
    gActiveMegaForm = 0;
    gMegaArmed = false;
    gMegaBattlePokeID = 0xFF;
    gInMegaRevert = false;
}

struct Il2CppFieldInfo {
    const char* name;
    const Il2CppType* type;
    Il2CppClass* parent;
    int32_t offset;
    uint32_t token;
};

inline Il2CppClass* findNestedClassByName(Il2CppClass* parent, const char* targetName) {
    if (parent == nullptr || parent->_1.nestedTypes == nullptr || targetName == nullptr) {
        return nullptr;
    }
    for (uint16_t i = 0; i < parent->_2.nested_type_count; ++i) {
        Il2CppClass* nested = parent->_1.nestedTypes[i];
        if (nested != nullptr && nested->_1.name != nullptr && std::strcmp(nested->_1.name, targetName) == 0) {
            return nested;
        }
    }
    return nullptr;
}

inline const MethodInfo* findMethodByName(Il2CppClass* klass, const char* targetName, int expectedParams) {
    if (klass == nullptr || klass->_1.methods == nullptr || targetName == nullptr) {
        return nullptr;
    }
    auto** methods = reinterpret_cast<const MethodInfo**>(klass->_1.methods);
    for (uint16_t i = 0; i < klass->_2.method_count; ++i) {
        const MethodInfo* m = methods[i];
        if (m != nullptr && m->name != nullptr && m->methodPointer != nullptr &&
            std::strcmp(m->name, targetName) == 0 &&
            (expectedParams < 0 || m->parameters_count == expectedParams)) {
            return m;
        }
    }
    return nullptr;
}

inline const Il2CppFieldInfo* findFieldByName(Il2CppClass* klass, const char* targetName) {
    if (klass == nullptr || klass->_1.fields == nullptr || targetName == nullptr) {
        return nullptr;
    }
    auto* fields = reinterpret_cast<const Il2CppFieldInfo*>(klass->_1.fields);
    for (uint16_t i = 0; i < klass->_2.field_count; ++i) {
        if (fields[i].name != nullptr && std::strcmp(fields[i].name, targetName) == 0) {
            return &fields[i];
        }
    }
    return nullptr;
}

template <typename T>
inline bool writeFieldIfPresent(void* obj, Il2CppClass* klass, const char* fieldName, T value) {
    if (obj == nullptr || klass == nullptr) {
        return false;
    }
    const Il2CppFieldInfo* fi = findFieldByName(klass, fieldName);
    if (fi == nullptr || fi->offset < 0) {
        return false;
    }
    uint32_t fieldOff = static_cast<uint32_t>(fi->offset);
    uint32_t nextOff = klass->_2.instance_size;
    auto* allFields = reinterpret_cast<const Il2CppFieldInfo*>(klass->_1.fields);
    for (uint16_t i = 0; i < klass->_2.field_count; ++i) {
        if (allFields[i].offset > fi->offset && static_cast<uint32_t>(allFields[i].offset) < nextOff) {
            nextOff = static_cast<uint32_t>(allFields[i].offset);
        }
    }
    if (fieldOff + sizeof(T) > nextOff) {
        return false;
    }
    std::memcpy(reinterpret_cast<uint8_t*>(obj) + fieldOff, &value, sizeof(T));
    return true;
}

// Dynamically invokes BDSP's native Section_FromEvent_ChangeTokusei::Execute using live IL2CPP metadata
// so the pre-Mega EventFactor (e.g. Blaze) is cleanly unregistered, the Mega Ability EventFactor
// (e.g. Drought / Tough Claws / Intimidate / etc.) is registered, BattleView updates its ability cache,
// and Section_AfterTokuseiChanged_Event fires switch-in ability effects natively.
inline bool invokeNativeChangeTokusei(void* sectionChangeTokusei, uint8_t pokeID, uint16_t nextTokusei) {
    if (sectionChangeTokusei == nullptr) {
        return false;
    }
    auto* secObj = reinterpret_cast<Il2CppObject*>(sectionChangeTokusei);
    Il2CppClass* secClass = secObj->klass;
    if (secClass == nullptr) {
        return false;
    }

    Il2CppClass* descClass = findNestedClassByName(secClass, "Description");
    Il2CppClass* resClass = findNestedClassByName(secClass, "Result");
    const MethodInfo* execMethod = findMethodByName(secClass, "Execute", 2);
    if (descClass == nullptr || resClass == nullptr || execMethod == nullptr) {
        return false;
    }

    if ((descClass->_2.bitflags2 >> 1 & 1) && (descClass->_2.cctor_finished == 0)) {
        il2cpp_runtime_class_init(descClass);
    }
    if ((resClass->_2.bitflags2 >> 1 & 1) && (resClass->_2.cctor_finished == 0)) {
        il2cpp_runtime_class_init(resClass);
    }

    void* descObj = il2cpp_object_new(descClass);
    void* resObj = il2cpp_object_new(resClass);
    if (descObj == nullptr || resObj == nullptr) {
        return false;
    }

    if (const MethodInfo* descCtor = findMethodByName(descClass, ".ctor", 0); descCtor != nullptr) {
        reinterpret_cast<void(*)(void*, const MethodInfo*)>(descCtor->methodPointer)(descObj, descCtor);
    }
    if (const MethodInfo* resCtor = findMethodByName(resClass, ".ctor", 0); resCtor != nullptr) {
        reinterpret_cast<void(*)(void*, const MethodInfo*)>(resCtor->methodPointer)(resObj, resCtor);
    }

    writeFieldIfPresent<uint8_t>(descObj, descClass, "userPokeID", pokeID);
    writeFieldIfPresent<uint8_t>(descObj, descClass, "targetPokeID", pokeID);
    writeFieldIfPresent<uint8_t>(descObj, descClass, "pokeID", pokeID);
    writeFieldIfPresent<int32_t>(descObj, descClass, "tokusei", static_cast<int32_t>(nextTokusei));
    writeFieldIfPresent<bool>(descObj, descClass, "isSameTokuseiEnable", true);
    writeFieldIfPresent<bool>(descObj, descClass, "isDisplayTokuseiWindow", false);
    writeFieldIfPresent<bool>(descObj, descClass, "isTokuseiWindowDisplay", false);
    writeFieldIfPresent<bool>(descObj, descClass, "isDisplayEffect", false);
    writeFieldIfPresent<bool>(descObj, descClass, "isStandardMessageDisable", true);
    writeFieldIfPresent<bool>(descObj, descClass, "isNotDisplayMessage", true);

    gInMegaTokuseiSwap = true;
    reinterpret_cast<void(*)(void*, void*, void**, const MethodInfo*)>(execMethod->methodPointer)(
        sectionChangeTokusei, resObj, &descObj, execMethod);
    gInMegaTokuseiSwap = false;
    return true;
}

// Dynamically invokes BDSP's native Section_FromEvent_ChangeWeather::Execute so weather abilities
// (Drought = 1 Harsh Sunlight, Drizzle = 2 Rain, Sand Stream = 4 Sandstorm, Snow Warning = 8 Snow)
// show exactly ONE ability banner, brighten/animate the 3D battlefield identically to Sunny Day,
// and apply the real 5-turn weather field effect.
inline bool invokeNativeChangeWeather(void* sectionChangeWeather, uint8_t pokeID, uint8_t weather, uint8_t turn) {
    if (sectionChangeWeather == nullptr) {
        return false;
    }
    auto* secObj = reinterpret_cast<Il2CppObject*>(sectionChangeWeather);
    Il2CppClass* secClass = secObj->klass;
    if (secClass == nullptr) {
        return false;
    }

    Il2CppClass* descClass = findNestedClassByName(secClass, "Description");
    Il2CppClass* resClass = findNestedClassByName(secClass, "Result");
    const MethodInfo* execMethod = findMethodByName(secClass, "Execute", 2);
    if (descClass == nullptr || resClass == nullptr || execMethod == nullptr) {
        return false;
    }

    if ((descClass->_2.bitflags2 >> 1 & 1) && (descClass->_2.cctor_finished == 0)) {
        il2cpp_runtime_class_init(descClass);
    }
    if ((resClass->_2.bitflags2 >> 1 & 1) && (resClass->_2.cctor_finished == 0)) {
        il2cpp_runtime_class_init(resClass);
    }

    void* descObj = il2cpp_object_new(descClass);
    void* resObj = il2cpp_object_new(resClass);
    if (descObj == nullptr || resObj == nullptr) {
        return false;
    }

    if (const MethodInfo* descCtor = findMethodByName(descClass, ".ctor", 0); descCtor != nullptr) {
        reinterpret_cast<void(*)(void*, const MethodInfo*)>(descCtor->methodPointer)(descObj, descCtor);
    }
    if (const MethodInfo* resCtor = findMethodByName(resClass, ".ctor", 0); resCtor != nullptr) {
        reinterpret_cast<void(*)(void*, const MethodInfo*)>(resCtor->methodPointer)(resObj, resCtor);
    }

    writeFieldIfPresent<uint8_t>(descObj, descClass, "pokeID", pokeID);
    writeFieldIfPresent<uint8_t>(descObj, descClass, "weather", weather);
    writeFieldIfPresent<uint8_t>(descObj, descClass, "turn", turn);
    writeFieldIfPresent<uint8_t>(descObj, descClass, "turnUpCount", 0);
    writeFieldIfPresent<uint8_t>(descObj, descClass, "cause", 0); // ChangeWeatherCause::OTHERS (1-byte enum)
    writeFieldIfPresent<bool>(descObj, descClass, "isDisplayTokuseiWindow", false);
    writeFieldIfPresent<bool>(descObj, descClass, "isTokuseiWindowDisplay", false);
    writeFieldIfPresent<bool>(descObj, descClass, "isDisplayEffect", true);
    writeFieldIfPresent<bool>(descObj, descClass, "isStandardMessageDisable", false);

    reinterpret_cast<void(*)(void*, void*, void**, const MethodInfo*)>(execMethod->methodPointer)(
        sectionChangeWeather, resObj, &descObj, execMethod);
    return true;
}

void installSharedPostInitHooks();

inline bool isMegaStone(int32_t itemId) {
    return itemId == kCharizarditeX || itemId == kCharizarditeY;
}

inline bool isMegaKeyItem(int32_t itemId) {
    return itemId == kMegaBracelet || itemId == kMegaRing || itemId == kKeyStone;
}

// Returns the Mega accessory item ID owned in the Bag, or 0 if none is owned.
inline uint16_t ownedMegaAccessoryInBag() {
    PlayerWork::getClass()->initIfNeeded();
    if (PlayerWork::GetItem(kMegaRing).fields.Count > 0) return kMegaRing;
    if (PlayerWork::GetItem(kMegaBracelet).fields.Count > 0) return kMegaBracelet;
    if (PlayerWork::GetItem(kKeyStone).fields.Count > 0) return kKeyStone;
    return 0;
}

inline bool hasMegaAccessoryInBag() {
    return ownedMegaAccessoryInBag() != 0;
}

// Patch only the 5 Mega rows in the loaded master ItemTable once.
// Zero hooks on UIBag, ItemWork::GetItemInfo, or UIText so Bag A/B/X/Y input is untouched.
inline void patchItemTableInMemory() {
    if (gItemTablePatched) {
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
    auto* fireStone = items->m_Items[kFireStone];
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

    applyTemplate(items->m_Items[kCharizarditeX], charcoal, fireStone);
    applyTemplate(items->m_Items[kCharizarditeY], charcoal, fireStone);
    applyTemplate(items->m_Items[kMegaBracelet], magmaStone, oddKeystone);
    applyTemplate(items->m_Items[kMegaRing], magmaStone, oddKeystone);
    applyTemplate(items->m_Items[kKeyStone], magmaStone, oddKeystone);

    gItemTablePatched = true;
}

inline bool utf16ContainsAscii(System::String::Object* str, const char* needle) {
    if (str == nullptr || needle == nullptr) {
        return false;
    }
    const int32_t hayLen = str->fields.m_stringLength;
    const int32_t needleLen = static_cast<int32_t>(std::strlen(needle));
    if (needleLen == 0 || hayLen < needleLen) {
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

    if (utf16ContainsAscii(label, "660")) {
        return isItemInfo
            ? "One of a variety of mysterious Mega Stones. Have Charizard hold it, and this stone will enable it to Mega Evolve during battle."
            : "Charizardite X";
    }
    if (utf16ContainsAscii(label, "678")) {
        return isItemInfo
            ? "One of a variety of mysterious Mega Stones. Have Charizard hold it, and this stone will enable it to Mega Evolve during battle."
            : "Charizardite Y";
    }
    if (utf16ContainsAscii(label, "696")) {
        return isItemInfo
            ? "This ring contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."
            : "Mega Ring";
    }
    if (utf16ContainsAscii(label, "744")) {
        return isItemInfo
            ? "This bracelet contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."
            : "Mega Bracelet";
    }
    if (utf16ContainsAscii(label, "773")) {
        return isItemInfo
            ? "A stone filled with an unexplained power. It makes Pokémon that hold a Mega Stone Mega Evolve."
            : "Key Stone";
    }
    return nullptr;
}

inline const char* customMegaAtlasSpriteName(int32_t itemId, bool isLarge) {
    switch (itemId) {
        case kCharizarditeX: return isLarge ? "item_0660_L" : "item_0660";
        case kCharizarditeY: return isLarge ? "item_0678_L" : "item_0678";
        case kMegaRing:      return isLarge ? "item_0696_L" : "item_0696";
        case kMegaBracelet:  return isLarge ? "item_0744_L" : "item_0744";
        case kKeyStone:      return isLarge ? "item_0773_L" : "item_0773";
        default:             return nullptr;
    }
}

inline bool getMegaFlag(const Dpr::Battle::Logic::BTL_ACTION_PARAM::Object& param) {
    return ((static_cast<uint64_t>(param.fields.raw) >> kMegaFlagBit) & 1ULL) != 0;
}

inline int targetCharizardMegaForm(Dpr::Battle::Logic::BTL_POKEPARAM::Object* pokeParam) {
    if (pokeParam == nullptr || !gMegaBattleAllowed) {
        return -1;
    }
    return charizardMegaForm(
        pokeParam->GetMonsNo(),
        pokeParam->GetFormNo(),
        pokeParam->GetItem(),
        kCharizarditeX,
        kCharizarditeY,
        hasMegaAccessoryInBag(),
        gMegaUsedThisBattle
    );
}

inline void updateMegaBattleBanner(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList) {
    if (wazaList == nullptr || wazaList->fields._btlPokeParam == nullptr) {
        return;
    }

    auto* bpp = wazaList->fields._btlPokeParam;
    const bool isBaseCharizard = (bpp->GetMonsNo() == kCharizardSpecies && bpp->GetFormNo() == kBaseForm);
    const bool hasStone = isMegaStone(bpp->GetItem());
    const bool hasRing = hasMegaAccessoryInBag();
    const int targetForm = targetCharizardMegaForm(bpp);
    const bool eligible = targetForm > 0;

    // Always display the Mega Evolution button pill in every battle:
    // - Active & choosable (cyan -> magenta-gold) only when the Pokémon can Mega Evolve and Mega Ring is in Bag.
    // - Visible but dimmed/inactive (grey) for all other Pokémon or when Mega Ring / Stone is missing.
    if (wazaList->fields._yButtonText != nullptr) {
        auto* promptText = reinterpret_cast<Dpr::UI::UIText::Object*>(wazaList->fields._yButtonText);
        const char* label = "MEGA EVOLVE (OFF)";
        UnityEngine::Color::Object textColor{};
        textColor.fields = { .r = 0.65f, .g = 0.65f, .b = 0.70f, .a = 1.0f };

        if (eligible && gMegaArmed) {
            label = (targetForm == kMegaXForm) ? "MEGA X ACTIVE" : "MEGA Y ACTIVE";
            textColor.fields = { .r = 1.0f, .g = 0.92f, .b = 0.20f, .a = 1.0f };
        } else if (eligible) {
            label = (targetForm == kMegaXForm) ? "[R] MEGA EVOLVE X" : "[R] MEGA EVOLVE Y";
            textColor.fields = { .r = 1.0f, .g = 1.0f, .b = 1.0f, .a = 1.0f };
        } else if (isBaseCharizard && hasStone && !hasRing && !gMegaUsedThisBattle) {
            label = "MEGA (Need Ring)";
            textColor.fields = { .r = 1.0f, .g = 0.65f, .b = 0.35f, .a = 1.0f };
        } else if (gMegaUsedThisBattle) {
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
        const uint64_t count = wazaList->fields._wazaButtons->max_length;
        for (uint64_t i = 0; i < count; ++i) {
            auto* btn = wazaList->fields._wazaButtons->m_Items[i];
            if (btn != nullptr && btn->fields._text != nullptr) {
                btn->fields._text->virtual_set_color((eligible && gMegaArmed) ? armedColor : normalColor);
            }
        }
    }
}

void executeCharizardMegaEvolution(Dpr::Battle::Logic::SectionContainer::Object* container,
                                   Dpr::Battle::Logic::PokeAction::Object* pokeAction) {
    if (container == nullptr || pokeAction == nullptr) {
        return;
    }

    system_load_typeinfo(0x8910);
    system_load_typeinfo(0x2c99);
    system_load_typeinfo(0xa963);
    Dpr::Battle::Logic::Common::getClass()->initIfNeeded();

    auto* param = pokeAction->fields.bpp;
    const int targetForm = targetCharizardMegaForm(param);
    if (targetForm <= 0) {
        return;
    }

    gActiveMegaForm = targetForm;
    gMegaBattlePokeID = param->GetID();
    if (auto* srcPoke = param->GetSrcData(); srcPoke != nullptr) {
        gPreMegaTokusei = reinterpret_cast<Pml::PokePara::CoreParam::Object*>(srcPoke)->GetTokuseiNo();
    } else {
        gPreMegaTokusei = param->fields.m_tokusei;
    }

    const uint16_t accessoryId = ownedMegaAccessoryInBag();
    const uint16_t displayAccessory = (accessoryId != 0) ? accessoryId : kMegaRing;

    // ORAS-style start sentence: "[Charizard]'s Charizardite X/Y is reacting to [Player]'s Mega Ring!"
    auto* msgDesc = Dpr::Battle::Logic::Section_FromEvent_Message::Description::newInstance();
    msgDesc->fields.pokeID = param->GetID();
    msgDesc->fields.isDisplayTokuseiWindow = false;
    msgDesc->fields.message->Setup(Dpr::Battle::Logic::BtlStrType::BTL_STRTYPE_SET, BTL_STRID_SET::MegaEvoStart);
    msgDesc->fields.message->AddArg(param->GetID());
    msgDesc->fields.message->AddArg(pokeAction->fields.clientID);
    msgDesc->fields.message->AddArg(param->GetItem());
    msgDesc->fields.message->AddArg(displayAccessory);

    auto* msgResult = Dpr::Battle::Logic::Section_FromEvent_Message::Result::newInstance();
    container->fields.m_section_FromEvent_Message->Execute(msgResult, &msgDesc);

    // Trigger BDSP's native form-change light-burst transformation animation & stay Mega on switch-out.
    auto* formDesc = Dpr::Battle::Logic::Section_FromEvent_FormChange::Description::newInstance();
    formDesc->fields.pokeID = param->GetID();
    formDesc->fields.formNo = static_cast<uint8_t>(targetForm);
    formDesc->fields.isDontResetFormByOut = true;
    formDesc->fields.isEnableInCaseOfDead = false;
    formDesc->fields.isDisplayTokuseiWindow = false;
    formDesc->fields.isDisplayChangeEffect = true;
    formDesc->fields.successMessage->Setup(Dpr::Battle::Logic::BtlStrType::BTL_STRTYPE_SET, BTL_STRID_SET::MegaEvo);
    formDesc->fields.successMessage->AddArg(param->GetID());
    formDesc->fields.successMessage->AddArg(param->GetID());

    system_load_typeinfo(0x2c56);
    auto* formResult = Dpr::Battle::Logic::Section_FromEvent_FormChange::Result::newInstance();
    container->fields.m_section_FromEvent_FormChange->Execute(formResult, &formDesc);

    // Swap the active Ability EventFactor (e.g. Blaze -> Tough Claws / Drought) via BDSP's native
    // Section_FromEvent_ChangeTokusei so the new Ability's EventHandlerTable is registered and
    // switch-in abilities (Drought, Intimidate, Drizzle, etc.) trigger natively with their real
    // BDSP ability banner and weather/field effects.
    const uint16_t megaAbility = static_cast<uint16_t>(targetForm == kMegaXForm ? kToughClaws : kDrought);
    invokeNativeChangeTokusei(container->fields.m_section_FromEvent_ChangeTokusei, param->GetID(), megaAbility);

    // Ensure BTL_POKEPARAM, CoreParam/Accessor, and BattleView client info all reflect the Mega Ability.
    param->fields.m_tokusei = megaAbility;
    if (param->fields.m_coreParam != nullptr) {
        param->fields.m_coreParam->fields.defaultTokusei = megaAbility;
    }
    if (auto* srcPoke = param->GetSrcData(); srcPoke != nullptr && srcPoke->fields.m_accessor != nullptr) {
        srcPoke->fields.m_accessor->SetTokuseiNo(megaAbility);
    }

    auto* msgSec = reinterpret_cast<Dpr::Battle::Logic::Section::Object*>(container->fields.m_section_FromEvent_Message);
    if (msgSec != nullptr && msgSec->fields.m_pServerCmdPutter != nullptr) {
        msgSec->fields.m_pServerCmdPutter->PublishClientInformation_AppeardPokemon(&param);
    }

    // Trigger real Harsh Sunlight weather (brightens 3D battlefield like Sunny Day + 5-turn weather + 1 Drought banner).
    if (targetForm == kMegaYForm) {
        void* weatherSec = container->fields.m_section_FromEvent_ChangeWeather != nullptr
            ? container->fields.m_section_FromEvent_ChangeWeather
            : container->fields.m_section_ChangeWeather;
        invokeNativeChangeWeather(weatherSec, param->GetID(), /*Harsh Sunlight=*/1, /*turns=*/5);
    }
}
} // namespace mega

HOOK_DEFINE_TRAMPOLINE(ItemData$$GetParam) {
    static int32_t Callback(uint16_t itemno, Pml::Item::ItemData::PrmID prmID) {
        if (!mega::gPostInitHooksInstalled) {
            mega::installSharedPostInitHooks();
        }
        if (!mega::gItemTablePatched) {
            mega::patchItemTableInMemory();
        }
        if (mega::gActiveMegaForm > 0 && mega::isBattleEnded()) {
            mega::revertMegaFormInParty();
        }
        return Orig(itemno, prmID);
    }
};

HOOK_DEFINE_TRAMPOLINE(BTL_POKEPARAM$$GetValue) {
    static int32_t Callback(Dpr::Battle::Logic::BTL_POKEPARAM::Object* __this,
                            Dpr::Battle::Logic::BTL_POKEPARAM::ValueID vid) {
        if (!mega::gInMegaTokuseiSwap && __this != nullptr &&
            (vid == Dpr::Battle::Logic::BTL_POKEPARAM::ValueID::BPP_TOKUSEI ||
             vid == Dpr::Battle::Logic::BTL_POKEPARAM::ValueID::BPP_TOKUSEI_EFFECTIVE)) {
            if (__this->GetMonsNo() == mega::kCharizardSpecies) {
                const uint8_t form = __this->GetFormNo();
                if (form == mega::kMegaXForm) return mega::kToughClaws;
                if (form == mega::kMegaYForm) return mega::kDrought;
            }
        }
        return Orig(__this, vid);
    }
};

HOOK_DEFINE_TRAMPOLINE(CoreParam$$GetTokuseiNo) {
    static uint16_t Callback(Pml::PokePara::CoreParam::Object* __this) {
        if (mega::gActiveMegaForm > 0 && mega::isBattleEnded()) {
            mega::revertMegaFormInParty();
        }
        if (!mega::gInMegaTokuseiSwap && mega::gActiveMegaForm > 0 && !mega::isBattleEnded() &&
            __this != nullptr && __this->GetMonsNo() == mega::kCharizardSpecies) {
            const uint16_t form = __this->GetFormNo();
            if (form == mega::kMegaXForm) return static_cast<uint16_t>(mega::kToughClaws);
            if (form == mega::kMegaYForm) return static_cast<uint16_t>(mega::kDrought);
        }
        return Orig(__this);
    }
};

HOOK_DEFINE_TRAMPOLINE(CheckUnbreakablePokeItem) {
    static bool Callback(uint16_t monsno, uint16_t itemID) {
        if (monsno == mega::kCharizardSpecies && mega::isMegaStone(itemID)) {
            return true;
        }
        return Orig(monsno, itemID);
    }
};

HOOK_DEFINE_TRAMPOLINE(DataManager$$GetPokemonCatalog) {
    static XLSXContent::PokemonInfo::SheetCatalog::Object* Callback(
        int32_t monsNo, int32_t formNo, Pml::Sex sex, bool isRare, bool isEgg, const void* methodInfo) {
        if (monsNo == mega::kCharizardSpecies &&
            (formNo == mega::kMegaXForm || formNo == mega::kMegaYForm)) {
            auto* base = Orig(monsNo, mega::kBaseForm, sex, isRare, isEgg, methodInfo);
            if (base != nullptr && base->klass != nullptr) {
                auto* clone = reinterpret_cast<XLSXContent::PokemonInfo::SheetCatalog::Object*>(
                    il2cpp_object_new(reinterpret_cast<Il2CppClass*>(base->klass)));
                if (clone != nullptr) {
                    std::memcpy(&clone->fields, &base->fields,
                                sizeof(XLSXContent::PokemonInfo::SheetCatalog::Fields));
                    clone->fields.FormNo = formNo;
                    clone->fields.Rare = isRare;
                    const char* bundleName = (formNo == mega::kMegaXForm)
                        ? "pm0006_01_00"
                        : "pm0006_02_00";
                    clone->fields.AssetBundleName = System::String::Create(bundleName);
                    return clone;
                }
            }
        }
        return Orig(monsNo, formNo, sex, isRare, isEgg, methodInfo);
    }
};

HOOK_DEFINE_TRAMPOLINE(ItemIcon$$LoadSprite) {
    static void Callback(int32_t itemId, System::Action::Object* onLoadedCallback, bool isUnload, bool isLarge) {
        if (const char* customName = mega::customMegaAtlasSpriteName(itemId, isLarge); customName != nullptr) {
            Dpr::UI::UIManager::getClass()->initIfNeeded();
            if (auto* uiMgr = Dpr::UI::UIManager::get_Instance(); uiMgr != nullptr) {
                if (auto* exactSpr = uiMgr->GetAtlasSprite(SpriteAtlasID::TEXTUREMASS, System::String::Create(customName));
                    exactSpr != nullptr) {
                    if (onLoadedCallback != nullptr) {
                        onLoadedCallback->Invoke(reinterpret_cast<Il2CppObject*>(exactSpr));
                    }
                    return;
                }
            }
            const int32_t fallbackId = mega::isMegaStone(itemId) ? mega::kFireStone : mega::kOddKeystone;
            Orig(fallbackId, onLoadedCallback, isUnload, isLarge);
            return;
        }
        Orig(itemId, onLoadedCallback, isUnload, isLarge);
    }
};

HOOK_DEFINE_TRAMPOLINE(MessageManager$$GetSimpleMessage) {
    static System::String::Object* Callback(Dpr::Message::MessageManager::Object* __this,
                                            System::String::Object* fileName,
                                            System::String::Object* label) {
        if (mega::gActiveMegaForm > 0 && label != nullptr) {
            if (mega::utf16ContainsAscii(label, "1572") || mega::utf16ContainsAscii(label, "megaevostart")) {
                return System::String::Create(
                    mega::gActiveMegaForm == mega::kMegaXForm
                        ? "Charizard's Charizardite X is reacting to your Mega Ring!"
                        : "Charizard's Charizardite Y is reacting to your Mega Ring!");
            }
            if (mega::utf16ContainsAscii(label, "1576") || mega::utf16ContainsAscii(label, "megaevo")) {
                return System::String::Create(
                    mega::gActiveMegaForm == mega::kMegaXForm
                        ? "Charizard has Mega Evolved into Mega Charizard X!"
                        : "Charizard has Mega Evolved into Mega Charizard Y!");
            }
        }
        if (const char* custom = mega::lookupMegaItemTextByLabel(fileName, label); custom != nullptr) {
            return System::String::Create(custom);
        }
        return Orig(__this, fileName, label);
    }
};

HOOK_DEFINE_TRAMPOLINE(MessageManager$$GetNameMessage) {
    static System::String::Object* Callback(Dpr::Message::MessageManager::Object* __this,
                                            System::String::Object* fileName,
                                            System::String::Object* label) {
        if (const char* custom = mega::lookupMegaItemTextByLabel(fileName, label); custom != nullptr) {
            return System::String::Create(custom);
        }
        return Orig(__this, fileName, label);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$Initialize) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this,
                         Dpr::Battle::Logic::BTL_POKEPARAM::Object* bpp,
                         uint8_t pokeIndex,
                         Dpr::Battle::Logic::BTL_ACTION_PARAM_OBJ::Object* dest) {
        const bool eligible = mega::targetCharizardMegaForm(bpp) > 0;
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !eligible);

        Orig(__this, bpp, pokeIndex, dest);

        mega::gMegaArmed = false;
        mega::updateMegaBattleBanner(__this);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnShow) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this) {
        const bool eligible = mega::targetCharizardMegaForm(__this->fields._btlPokeParam) > 0;
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !eligible);

        Orig(__this);

        mega::gMegaArmed = false;
        mega::updateMegaBattleBanner(__this);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnUpdate) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this, float deltaTime) {
        // Poll Mega toggle BEFORE Orig so vanilla BUIWazaList never swallows the button press.
        // Do NOT bind ZL/ZR because vanilla BDSP maps ZL/ZR to Confirm (A).
        if (__this != nullptr &&
            __this->fields._IsShow_k__BackingField &&
            __this->fields._IsFocus_k__BackingField &&
            mega::targetCharizardMegaForm(__this->fields._btlPokeParam) > 0) {
            constexpr int32_t kToggleMask =
                GameController::ButtonMask::R |
                GameController::ButtonMask::L |
                GameController::ButtonMask::X |
                GameController::ButtonMask::Plus |
                GameController::ButtonMask::StickL |
                GameController::ButtonMask::StickR;

            if (Dpr::Battle::View::BtlvInput::GetPush(kToggleMask, true)) {
                mega::gMegaArmed = !mega::gMegaArmed;
                Dpr::Battle::View::BattleViewCore::getClass()->initIfNeeded();
                if (auto* core = Dpr::Battle::View::BattleViewCore::get_Instance();
                    core != nullptr && core->fields._UISystem_k__BackingField != nullptr) {
                    core->fields._UISystem_k__BackingField->PlaySe(mega::kSeUiSelect);
                }
            }
        }

        Orig(__this, deltaTime);

        if (__this != nullptr && __this->fields._IsShow_k__BackingField) {
            mega::updateMegaBattleBanner(__this);
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnSubmit) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this) {
        Orig(__this);
        if (__this == nullptr) {
            return;
        }
        auto* destActionParam = __this->fields._destActionParam;
        if (destActionParam != nullptr &&
            mega::gMegaArmed &&
            mega::targetCharizardMegaForm(__this->fields._btlPokeParam) > 0) {
            destActionParam->fields.value.fields.raw |= static_cast<long>(mega::kMegaFlagMask);
        }
        if (__this->fields._IsValid_k__BackingField) {
            mega::gMegaArmed = false;
        }
    }
};

// Use BDSP's native CMD_ChangeForm_Start / CMD_ACT_PokeChangeEffect_WaitCore
// so the built-in form-change light-burst transformation animation plays cleanly.

HOOK_DEFINE_TRAMPOLINE(ProcessActionCore$$action) {
    static void Callback(Dpr::Battle::Logic::Section_ProcessActionCore::Object* __this,
                         Dpr::Battle::Logic::PokeAction::Object* pokeAction) {
        if (pokeAction != nullptr &&
            pokeAction->fields.actionCategory == Dpr::Battle::Logic::PokeActionCategory::Mega_Evolution) {
            auto* container = reinterpret_cast<Dpr::Battle::Logic::SectionContainer::Object*>(
                __this->fields.m_pSectionContainer);
            mega::executeCharizardMegaEvolution(container, pokeAction);
            mega::gMegaUsedThisBattle = true;
            FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, true);
            return;
        }
        Orig(__this, pokeAction);
        // If our Mega Evolved Pokemon fainted during this action, immediately revert it to base form
        // so its original Fire/Flying type and original Ability (Blaze) are restored right away.
        if (mega::gActiveMegaForm > 0 && mega::gMegaBattlePokeID != 0xFF && __this != nullptr) {
            auto* sec = reinterpret_cast<Dpr::Battle::Logic::Section::Object*>(__this);
            if (auto* megaBpp = sec->GetPokeParam(mega::gMegaBattlePokeID); megaBpp != nullptr && megaBpp->IsDead()) {
                megaBpp->fields.m_formNo = mega::kBaseForm;
                megaBpp->fields.m_tokusei = mega::gPreMegaTokusei;
                if (megaBpp->fields.m_coreParam != nullptr) {
                    megaBpp->fields.m_coreParam->fields.formno = mega::kBaseForm;
                    megaBpp->fields.m_coreParam->fields.defaultTokusei = mega::gPreMegaTokusei;
                }
                mega::revertMegaFormInParty();
            }
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(createPokeAction_FromClientInstruction) {
    static void Callback(Dpr::Battle::Logic::Section_StoreActions::Object* __this,
                         Dpr::Battle::Logic::PokeActionContainer::Object* actionContainer,
                         Dpr::Battle::Logic::SVCL_ACTION::Object* instructions,
                         uint8_t clientID) {
        system_load_typeinfo(0x78dd);
        system_load_typeinfo(0x78c7);

        auto* party = reinterpret_cast<Dpr::Battle::Logic::Section::Object*>(__this)->GetPokeParty(clientID);
        const uint8_t numAction = Dpr::Battle::Logic::SVCL_ACTION::GetNumAction(instructions, clientID);

        bool anyMega = false;
        for (uint8_t idx = 0; idx < numAction; ++idx) {
            auto* rawInst = Dpr::Battle::Logic::SVCL_ACTION::Get(instructions, clientID, idx);
            if (rawInst != nullptr && mega::getMegaFlag(*rawInst)) {
                anyMega = true;
                break;
            }
        }

        if (!anyMega) {
            Orig(__this, actionContainer, instructions, clientID);
            return;
        }

        if (party != nullptr && numAction != 0) {
            for (uint8_t idx = 0; idx < numAction; ++idx) {
                auto* rawInst = Dpr::Battle::Logic::SVCL_ACTION::Get(instructions, clientID, idx);
                if (rawInst == nullptr) {
                    continue;
                }
                Dpr::Battle::Logic::BTL_ACTION_PARAM::Object inst{};
                inst.fields.raw = rawInst->fields.raw;

                if (mega::getMegaFlag(inst)) {
                    auto* megaAction = Dpr::Battle::Logic::PokeAction::newInstance();
                    if (__this->setupPokeAction_FromClientInstruction(megaAction, &inst, clientID)) {
                        actionContainer->Add(&megaAction);
                    }
                    inst.fields.raw &= ~static_cast<long>(mega::kMegaFlagMask);
                }

                auto* normalAction = Dpr::Battle::Logic::PokeAction::newInstance();
                if (__this->setupPokeAction_FromClientInstruction(normalAction, &inst, clientID)) {
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
                         uint8_t clientID) {
        Dpr::Battle::Logic::BTL_ACTION_PARAM::Object instruction{};
        instruction.fields.raw = clientInstruction->fields.raw;

        if (mega::getMegaFlag(instruction)) {
            pokeAction->fields.actionCategory = Dpr::Battle::Logic::PokeActionCategory::Mega_Evolution;
            pokeAction->fields.bpp = __this->getActionPoke(clientInstruction, clientID);
            pokeAction->fields.priority = 2;
            pokeAction->fields.clientID = clientID;
            pokeAction->fields.fDone = false;
            pokeAction->fields.fIntrCheck = false;
            pokeAction->fields.fRecalcPriority = false;

            auto* battleEnv = reinterpret_cast<Dpr::Battle::Logic::BattleEnv::Object*>(__this->fields.m_pBattleEnv);
            battleEnv->fields.m_actionSerialNoManager->AssignSerialNo(pokeAction->fields.actionDesc);
            return true;
        }

        return Orig(__this, pokeAction, clientInstruction, clientID);
    }
};

HOOK_DEFINE_TRAMPOLINE(CalcActionPriority$$Execute) {
    static void Callback(Dpr::Battle::Logic::Section_CalcActionPriority::Object* __this,
                         Dpr::Battle::Logic::Section_CalcActionPriority::Result::Object* pResult,
                         Dpr::Battle::Logic::Section_CalcActionPriority::Description::Object** description) {
        auto* pokeAction = (*description)->fields.pokeAction;
        if (pokeAction != nullptr &&
            pokeAction->fields.actionCategory == Dpr::Battle::Logic::PokeActionCategory::Mega_Evolution) {
            constexpr uint8_t operationPri = 2;
            constexpr uint8_t dominantPri = 2;
            constexpr uint8_t wazaPri = 0;
            constexpr uint8_t agility = 0;
            const auto spPri = static_cast<uint8_t>((*description)->fields.specialPriority);
            pResult->fields.priority = Dpr::Battle::Logic::ActPri::Make(dominantPri, operationPri, wazaPri, spPri, agility);
            return;
        }
        Orig(__this, pResult, description);
    }
};

HOOK_DEFINE_TRAMPOLINE(SetupBattleWild) {
    static void Callback(Dpr::Battle::Logic::BATTLE_SETUP_PARAM::Object* battleSetupParam,
                         Pml::PokeParty::Object* iPtrEnemyParty, int32_t arenaID, int32_t mapAttrib, int32_t weatherType,
                         bool isSwim, bool isFishing, int32_t partnerID, bool isCaptureDemo, int32_t safariBallNum,
                         bool isSymbol, bool isMitu, System::String::Object* overlapBgm, int32_t overlapSetupEffectId,
                         bool isCantUseBall) {
        mega::revertMegaFormInParty();
        mega::gMegaUsedThisBattle = false;
        mega::gMegaBattleAllowed = !(isCaptureDemo || battleSetupParam->fields.rule == Dpr::Battle::Logic::BtlRule::BTL_RULE_SAFARI);
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !mega::gMegaBattleAllowed);
        Orig(battleSetupParam, iPtrEnemyParty, arenaID, mapAttrib, weatherType, isSwim, isFishing, partnerID,
             isCaptureDemo, safariBallNum, isSymbol, isMitu, overlapBgm, overlapSetupEffectId, isCantUseBall);
    }
};

HOOK_DEFINE_TRAMPOLINE(SetupBattleTrainer) {
    static void Callback(Dpr::Battle::Logic::BATTLE_SETUP_PARAM::Object* battleSetupParam, int32_t arenaID,
                         int32_t mapAttrib, int32_t weatherType, int32_t rule, int32_t enemyID0, int32_t enemyID1,
                         int32_t partnerID) {
        mega::revertMegaFormInParty();
        mega::gMegaUsedThisBattle = false;
        mega::gMegaBattleAllowed = !(rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_SAFARI) ||
                                     rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_RAID));
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !mega::gMegaBattleAllowed);
        Orig(battleSetupParam, arenaID, mapAttrib, weatherType, rule, enemyID0, enemyID1, partnerID);
    }
};

namespace mega {
void installSharedPostInitHooks() {
    if (gPostInitHooksInstalled) {
        return;
    }
    gPostInitHooksInstalled = true;

    BTL_POKEPARAM$$GetValue::InstallAtOffset(0x01fdd010);
    CoreParam$$GetTokuseiNo::InstallAtOffset(0x020488a0);
    CheckUnbreakablePokeItem::InstallAtOffset(0x01d0d780);
    DataManager$$GetPokemonCatalog::InstallAtOffset(0x02cc76a0);
    ItemIcon$$LoadSprite::InstallAtOffset(0x01ca7100);
    MessageManager$$GetSimpleMessage::InstallAtOffset(0x0210d000);
    MessageManager$$GetNameMessage::InstallAtOffset(0x0210ce50);
}
} // namespace mega

extern "C" void exl_main(void*, void*) {
    exl::hook::Initialize();

    // Install non-conflicting Battle & ItemData hooks immediately so they are always active.
    ItemData$$GetParam::InstallAtOffset(0x0249b950);
    BUIWazaList$$Initialize::InstallAtOffset(0x01d2b800);
    BUIWazaList$$OnShow::InstallAtOffset(0x01d2cb70);
    BUIWazaList$$OnUpdate::InstallAtOffset(0x01d2c490);
    BUIWazaList$$OnSubmit::InstallAtOffset(0x01d2c6e0);
    CalcActionPriority$$Execute::InstallAtOffset(0x021ad570);
    ProcessActionCore$$action::InstallAtOffset(0x021c0de0);
    setupPokeAction_FromClientInstruction::InstallAtOffset(0x021cdbf0);
    createPokeAction_FromClientInstruction::InstallAtOffset(0x021cdad0);
    SetupBattleWild::InstallAtOffset(0x02c3abc0);
    SetupBattleTrainer::InstallAtOffset(0x02c3b800);
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT(0x420);
}
