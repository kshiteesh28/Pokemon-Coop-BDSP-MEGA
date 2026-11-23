#include "exlaunch.hpp"

#include "externals/BTL_STRID_SET.h"
#include "externals/FlagWork.h"
#include "externals/FlagWork_Enums.h"
#include "externals/GameController.h"
#include "externals/ItemWork.h"
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
#include "externals/Dpr/Battle/Logic/Section.h"
#include "externals/Dpr/Battle/Logic/SectionContainer.h"
#include "externals/Dpr/Battle/Logic/Section_CalcActionPriority.h"
#include "externals/Dpr/Battle/Logic/Section_FromEvent_FormChange.h"
#include "externals/Dpr/Battle/Logic/Section_FromEvent_Message.h"
#include "externals/Dpr/Battle/Logic/Section_ProcessActionCore.h"
#include "externals/Dpr/Battle/Logic/Section_StoreActions.h"
#include "externals/Dpr/Battle/View/BattleViewCore.h"
#include "externals/Dpr/Battle/View/BtlvInput.h"
#include "externals/Dpr/Battle/View/Systems/BattleViewSystem.h"
#include "externals/Dpr/Battle/View/UI/BUIActionList.h"
#include "externals/Dpr/Battle/View/UI/BUIWazaButton.h"
#include "externals/Dpr/Battle/View/UI/BUIWazaList.h"
#include "externals/Dpr/Item/ItemInfo.h"
#include "externals/Dpr/Message/MessageManager.h"
#include "externals/Dpr/UI/UIManager.h"
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
static_assert(kMegaX.hp == 78 && kMegaY.hp == 78);
static_assert(kMegaX.primaryType == 9 && kMegaX.secondaryType == 15);
static_assert(kMegaY.primaryType == 9 && kMegaY.secondaryType == 2);

bool gPostInitHooksInstalled = false;
bool gItemTablePatched = false;
bool gMegaBattleAllowed = true;
bool gMegaUsedThisBattle = false;
bool gMegaArmed = false;

void installPostInitHooks();

inline bool isMegaStone(int32_t itemId) {
    return itemId == kCharizarditeX || itemId == kCharizarditeY;
}

inline bool isMegaKeyItem(int32_t itemId) {
    return itemId == kMegaBracelet || itemId == kMegaRing || itemId == kKeyStone;
}

inline bool isMegaItem(int32_t itemId) {
    return isMegaStone(itemId) || isMegaKeyItem(itemId);
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

// Patch the loaded master ItemTable in memory once so ItemWork, UIBag, BagItemPanel,
// Party, and Battle all see consistent pocket/hold/flag metadata with zero Bag input corruption.
// Mega Stones use Charcoal (249) as template (Items pocket, holdable, field_func = 0).
// Mega Ring / Bracelet / Key Stone use Magma Stone (458) as template (Key Items pocket,
// non-registerable, field_func = 0) so UIBag A/B/X/Y input and context menus stay intact.
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

inline const char* megaItemTextById(int32_t itemId, bool isInfo) {
    switch (itemId) {
        case kCharizarditeX:
            return isInfo
                ? "One of a variety of mysterious Mega Stones. Have Charizard hold it, and this stone will enable it to Mega Evolve during battle."
                : "Charizardite X";
        case kCharizarditeY:
            return isInfo
                ? "One of a variety of mysterious Mega Stones. Have Charizard hold it, and this stone will enable it to Mega Evolve during battle."
                : "Charizardite Y";
        case kMegaRing:
            return isInfo
                ? "This ring contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."
                : "Mega Ring";
        case kMegaBracelet:
            return isInfo
                ? "This bracelet contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."
                : "Mega Bracelet";
        case kKeyStone:
            return isInfo
                ? "A stone filled with an unexplained power. It makes Pokémon that hold a Mega Stone Mega Evolve."
                : "Key Stone";
        default:
            return nullptr;
    }
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

    if (utf16ContainsAscii(label, "660")) return megaItemTextById(kCharizarditeX, isItemInfo);
    if (utf16ContainsAscii(label, "678")) return megaItemTextById(kCharizarditeY, isItemInfo);
    if (utf16ContainsAscii(label, "696")) return megaItemTextById(kMegaRing, isItemInfo);
    if (utf16ContainsAscii(label, "744")) return megaItemTextById(kMegaBracelet, isItemInfo);
    if (utf16ContainsAscii(label, "773")) return megaItemTextById(kKeyStone, isItemInfo);
    return nullptr;
}

inline const char* lookupMegaItemTextByIndex(System::String::Object* fileName, int32_t messageIndex) {
    if (fileName == nullptr || !isMegaItem(messageIndex)) {
        return nullptr;
    }
    if (utf16ContainsAscii(fileName, "iteminfo")) {
        return megaItemTextById(messageIndex, true);
    }
    if (utf16ContainsAscii(fileName, "itemname")) {
        return megaItemTextById(messageIndex, false);
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

inline UnityEngine::GameObject* findMegaActiveObject(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList) {
    if (wazaList == nullptr) {
        return nullptr;
    }
    auto* rootTransform = reinterpret_cast<UnityEngine::Component::Object*>(wazaList)->get_transform();
    if (rootTransform == nullptr) {
        return nullptr;
    }
    auto* gimmickButton = rootTransform->Find(System::String::Create("GimmickButton"));
    if (gimmickButton == nullptr) {
        return nullptr;
    }
    if (auto* gimmicks = gimmickButton->Find(System::String::Create("Gimmicks")); gimmicks != nullptr) {
        if (auto* megaIcon = gimmicks->Find(System::String::Create("MegaIcon")); megaIcon != nullptr) {
            if (auto* activeTf = megaIcon->Find(System::String::Create("Active")); activeTf != nullptr) {
                return reinterpret_cast<UnityEngine::Component::Object*>(activeTf)->get_gameObject();
            }
        }
        return nullptr;
    }
    if (gimmickButton->get_childCount() > 0) {
        if (auto* stateTf = gimmickButton->GetChild(0); stateTf != nullptr) {
            return reinterpret_cast<UnityEngine::Component::Object*>(stateTf)->get_gameObject();
        }
    }
    return nullptr;
}

inline bool hasGimmickButton(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList) {
    if (wazaList == nullptr) {
        return false;
    }
    auto* rootTransform = reinterpret_cast<UnityEngine::Component::Object*>(wazaList)->get_transform();
    return rootTransform != nullptr && rootTransform->Find(System::String::Create("GimmickButton")) != nullptr;
}

inline bool isGimmickIconActive(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList) {
    auto* activeGo = findMegaActiveObject(wazaList);
    return activeGo != nullptr && activeGo->get_activeSelf();
}

inline void syncGimmickUiVisuals(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList, int targetForm, bool armed) {
    if (wazaList == nullptr) {
        return;
    }

    const bool eligible = targetForm > 0;
    const bool madridUi = hasGimmickButton(wazaList);

    // 1. Update Madrid UI GimmickButton / MegaIcon / Active highlight when present.
    if (madridUi) {
        auto* rootTransform = reinterpret_cast<UnityEngine::Component::Object*>(wazaList)->get_transform();
        if (auto* gimmickButton = rootTransform->Find(System::String::Create("GimmickButton")); gimmickButton != nullptr) {
            if (auto* btnGo = reinterpret_cast<UnityEngine::Component::Object*>(gimmickButton)->get_gameObject(); btnGo != nullptr) {
                btnGo->SetActive(eligible);
            }
            if (auto* gimmicks = gimmickButton->Find(System::String::Create("Gimmicks")); gimmicks != nullptr) {
                if (auto* megaIcon = gimmicks->Find(System::String::Create("MegaIcon")); megaIcon != nullptr) {
                    if (auto* megaGo = reinterpret_cast<UnityEngine::Component::Object*>(megaIcon)->get_gameObject(); megaGo != nullptr) {
                        megaGo->SetActive(eligible);
                    }
                    if (auto* activeTf = megaIcon->Find(System::String::Create("Active")); activeTf != nullptr) {
                        if (auto* activeGo = reinterpret_cast<UnityEngine::Component::Object*>(activeTf)->get_gameObject(); activeGo != nullptr) {
                            activeGo->SetActive(eligible && armed);
                        }
                    }
                }
            } else if (gimmickButton->get_childCount() > 0) {
                if (auto* stateTf = gimmickButton->GetChild(0); stateTf != nullptr) {
                    if (auto* stateGo = reinterpret_cast<UnityEngine::Component::Object*>(stateTf)->get_gameObject(); stateGo != nullptr) {
                        stateGo->SetActive(eligible && armed);
                    }
                }
            }
        }
    }

    // 2. Always render a clear, unmistakable Mega Evolution prompt on _yButtonText
    // so both Vanilla BDSP Battle UI (which has no GimmickButton) and Madrid UI show it.
    if (eligible && wazaList->fields._yButtonText != nullptr) {
        auto* promptText = reinterpret_cast<Dpr::UI::UIText::Object*>(wazaList->fields._yButtonText);
        const char* label = nullptr;
        if (armed) {
            label = (targetForm == kMegaXForm) ? "MEGA X READY" : "MEGA Y READY";
        } else if (madridUi) {
            label = (targetForm == kMegaXForm) ? "[L3/ZL] Mega X" : "[L3/ZL] Mega Y";
        } else {
            label = (targetForm == kMegaXForm) ? "[L/R] Mega X" : "[L/R] Mega Y";
        }
        promptText->virtual_set_text(System::String::Create(label));
        UnityEngine::Color::Object promptColor{};
        if (armed) {
            promptColor.fields = { .r = 1.0f, .g = 0.85f, .b = 0.15f, .a = 1.0f };
        } else {
            promptColor.fields = { .r = 0.45f, .g = 0.90f, .b = 1.0f, .a = 1.0f };
        }
        promptText->virtual_set_color(promptColor);
    }

    // 3. Highlight move button text in gold when Mega Evolution is armed.
    if (wazaList->fields._wazaButtons != nullptr && eligible) {
        const UnityEngine::Color::Fields armedColor{ .r = 1.0f, .g = 0.85f, .b = 0.15f, .a = 1.0f };
        const UnityEngine::Color::Fields normalColor = wazaList->fields._wazaTextColor.fields;
        const uint64_t count = wazaList->fields._wazaButtons->max_length;
        for (uint64_t i = 0; i < count; ++i) {
            auto* btn = wazaList->fields._wazaButtons->m_Items[i];
            if (btn != nullptr && btn->fields._text != nullptr && armed) {
                btn->fields._text->virtual_set_color(armedColor);
            } else if (btn != nullptr && btn->fields._text != nullptr && !madridUi) {
                btn->fields._text->virtual_set_color(normalColor);
            }
        }
    }
}

inline void handleMegaToggleAfterOrig(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList, bool fromActionList) {
    if (wazaList == nullptr || !wazaList->fields._IsShow_k__BackingField) {
        return;
    }

    const int targetForm = targetCharizardMegaForm(wazaList->fields._btlPokeParam);
    if (targetForm <= 0) {
        return;
    }

    const bool madridUi = hasGimmickButton(wazaList);

    // If Lumi's Madrid UI toggled MegaIcon/Active via StickL/StickR inside Orig,
    // adopt its highlighted state.
    if (fromActionList && madridUi) {
        const bool iconActive = isGimmickIconActive(wazaList);
        if (iconActive != gMegaArmed) {
            gMegaArmed = iconActive;
        }
        // Also support ZL/ZR (unused in Madrid UI) as a convenient Mega toggle.
        constexpr int32_t kMadridExtraMask =
            GameController::ButtonMask::ZL | GameController::ButtonMask::ZR;
        if (Dpr::Battle::View::BtlvInput::GetPush(kMadridExtraMask, true)) {
            gMegaArmed = !gMegaArmed;
            Dpr::Battle::View::BattleViewCore::getClass()->initIfNeeded();
            if (auto* core = Dpr::Battle::View::BattleViewCore::get_Instance();
                core != nullptr && core->fields._UISystem_k__BackingField != nullptr) {
                core->fields._UISystem_k__BackingField->PlaySe(kSeUiSelect);
            }
        }
        syncGimmickUiVisuals(wazaList, targetForm, gMegaArmed);
        return;
    }

    // Vanilla BDSP Fight menu: allow L, R, ZL, ZR, StickL, StickR, or Plus.
    constexpr int32_t kVanillaToggleMask =
        GameController::ButtonMask::L |
        GameController::ButtonMask::R |
        GameController::ButtonMask::ZL |
        GameController::ButtonMask::ZR |
        GameController::ButtonMask::StickL |
        GameController::ButtonMask::StickR |
        GameController::ButtonMask::Plus;

    if (Dpr::Battle::View::BtlvInput::GetPush(kVanillaToggleMask, true)) {
        gMegaArmed = !gMegaArmed;
        Dpr::Battle::View::BattleViewCore::getClass()->initIfNeeded();
        if (auto* core = Dpr::Battle::View::BattleViewCore::get_Instance();
            core != nullptr && core->fields._UISystem_k__BackingField != nullptr) {
            core->fields._UISystem_k__BackingField->PlaySe(kSeUiSelect);
        }
    }

    syncGimmickUiVisuals(wazaList, targetForm, gMegaArmed);
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

    const uint16_t accessoryId = ownedMegaAccessoryInBag();
    const uint16_t displayAccessory = (accessoryId != 0) ? accessoryId : kMegaRing;

    auto* msgDesc = Dpr::Battle::Logic::Section_FromEvent_Message::Description::newInstance();
    msgDesc->fields.pokeID = param->GetID();
    msgDesc->fields.message->Setup(Dpr::Battle::Logic::BtlStrType::BTL_STRTYPE_SET, BTL_STRID_SET::MegaEvoStart);
    msgDesc->fields.message->AddArg(param->GetID());
    msgDesc->fields.message->AddArg(pokeAction->fields.clientID);
    msgDesc->fields.message->AddArg(param->GetItem());
    msgDesc->fields.message->AddArg(displayAccessory);

    auto* msgResult = Dpr::Battle::Logic::Section_FromEvent_Message::Result::newInstance();
    container->fields.m_section_FromEvent_Message->Execute(msgResult, &msgDesc);

    auto* formDesc = Dpr::Battle::Logic::Section_FromEvent_FormChange::Description::newInstance();
    formDesc->fields.pokeID = param->GetID();
    formDesc->fields.formNo = static_cast<uint8_t>(targetForm);
    formDesc->fields.isDontResetFormByOut = false;
    formDesc->fields.isEnableInCaseOfDead = false;
    formDesc->fields.isDisplayChangeEffect = true;
    formDesc->fields.successMessage->Setup(Dpr::Battle::Logic::BtlStrType::BTL_STRTYPE_SET, BTL_STRID_SET::MegaEvo);
    formDesc->fields.successMessage->AddArg(param->GetID());
    formDesc->fields.successMessage->AddArg(param->GetID());

    system_load_typeinfo(0x2c56);
    auto* formResult = Dpr::Battle::Logic::Section_FromEvent_FormChange::Result::newInstance();
    container->fields.m_section_FromEvent_FormChange->Execute(formResult, &formDesc);
}
} // namespace mega

HOOK_DEFINE_TRAMPOLINE(ItemData$$GetParam) {
    static int32_t Callback(uint16_t itemno, Pml::Item::ItemData::PrmID prmID) {
        if (!mega::gPostInitHooksInstalled) {
            mega::installPostInitHooks();
        }
        if (!mega::gItemTablePatched) {
            mega::patchItemTableInMemory();
        }
        return Orig(itemno, prmID);
    }
};

HOOK_DEFINE_TRAMPOLINE(ItemWork$$GetItemData) {
    static XLSXContent::ItemTable::SheetItem::Object* Callback(uint16_t id) {
        if (!mega::gItemTablePatched) {
            mega::patchItemTableInMemory();
        }
        return Orig(id);
    }
};

HOOK_DEFINE_TRAMPOLINE(ItemWork$$GetItemInfo) {
    static Dpr::Item::ItemInfo::Object* Callback(int32_t itemno) {
        if (!mega::gItemTablePatched) {
            mega::patchItemTableInMemory();
        }
        auto* info = Orig(itemno);
        if (info != nullptr && mega::isMegaItem(itemno)) {
            const int32_t refId = mega::isMegaStone(itemno) ? mega::kCharcoal : mega::kMagmaStone;
            if (auto* ref = Orig(refId); ref != nullptr) {
                info->fields._type = ref->fields._type;
                info->fields._categoryType = ref->fields._categoryType;
                info->fields._groupId = ref->fields._groupId;
                if (info->fields._sortId == 0) {
                    info->fields._sortId = ref->fields._sortId;
                }
            }
        }
        return info;
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

HOOK_DEFINE_TRAMPOLINE(UIText$$SetupMessageByIndex) {
    static void Callback(Dpr::UI::UIText::Object* __this, System::String::Object* messageFile, int32_t messageIndex) {
        Orig(__this, messageFile, messageIndex);
        if (__this != nullptr) {
            if (const char* custom = mega::lookupMegaItemTextByIndex(messageFile, messageIndex); custom != nullptr) {
                __this->fields._useMessage = false;
                __this->virtual_set_text(System::String::Create(custom));
            }
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(UIText$$SetupMessageById) {
    static void Callback(Dpr::UI::UIText::Object* __this, System::String::Object* messageFile, System::String::Object* messageId) {
        Orig(__this, messageFile, messageId);
        if (__this != nullptr) {
            if (const char* custom = mega::lookupMegaItemTextByLabel(messageFile, messageId); custom != nullptr) {
                __this->fields._useMessage = false;
                __this->virtual_set_text(System::String::Create(custom));
            }
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(MessageManager$$GetSimpleMessage) {
    static System::String::Object* Callback(Dpr::Message::MessageManager::Object* __this,
                                            System::String::Object* fileName,
                                            System::String::Object* label) {
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
        const int targetForm = mega::targetCharizardMegaForm(bpp);
        const bool eligible = targetForm > 0;
        // Set FLAG_MEGA_EVOLUTION_UNAVAILABLE before Orig so Lumi's SetGimmickIconsFromFlags
        // displays the Mega Evolution icon ONLY for eligible Pokémon.
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !eligible);

        Orig(__this, bpp, pokeIndex, dest);

        mega::gMegaArmed = false;
        mega::syncGimmickUiVisuals(__this, targetForm, false);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnShow) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this) {
        const int targetForm = mega::targetCharizardMegaForm(__this->fields._btlPokeParam);
        const bool eligible = targetForm > 0;
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !eligible);

        Orig(__this);

        mega::gMegaArmed = false;
        mega::syncGimmickUiVisuals(__this, targetForm, false);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnUpdate) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this, float deltaTime) {
        Orig(__this, deltaTime);
        if (__this != nullptr && __this->fields._IsFocus_k__BackingField) {
            mega::handleMegaToggleAfterOrig(__this, false);
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
            (mega::gMegaArmed || mega::isGimmickIconActive(__this)) &&
            mega::targetCharizardMegaForm(__this->fields._btlPokeParam) > 0) {
            destActionParam->fields.value.fields.raw |= static_cast<long>(mega::kMegaFlagMask);
        }
        if (__this->fields._IsValid_k__BackingField) {
            mega::gMegaArmed = false;
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIActionList$$OnUpdate) {
    static void Callback(Dpr::Battle::View::UI::BUIActionList::Object* __this, float deltaTime) {
        Orig(__this, deltaTime);
        if (__this == nullptr || !__this->fields._IsFocus_k__BackingField) {
            return;
        }
        Dpr::Battle::View::BattleViewCore::getClass()->initIfNeeded();
        if (auto* core = Dpr::Battle::View::BattleViewCore::get_Instance();
            core != nullptr && core->fields._UISystem_k__BackingField != nullptr) {
            mega::handleMegaToggleAfterOrig(core->fields._UISystem_k__BackingField->fields._wazaList, true);
        }
    }
};

HOOK_DEFINE_INLINE(CMD_ChangeForm_Start) {
    static void Callback(exl::hook::nx64::InlineCtx* ctx) {
        reinterpret_cast<Dpr::Battle::View::Systems::BattleViewSystem::Object*>(ctx->X[19])->fields.m_subSequence = 20;
    }
};

HOOK_DEFINE_TRAMPOLINE(CMD_ACT_PokeChangeEffect_WaitCore) {
    static bool Callback(Dpr::Battle::View::Systems::BattleViewSystem::Object* __this, bool isGChange) {
        if (__this->fields.m_subSequence == 20) {
            __this->PlaySequenceEffect(0, false);
            __this->fields.m_subSequence = 7;
            return false;
        }
        return Orig(__this, isGChange);
    }
};

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
        mega::gMegaArmed = false;
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
        mega::gMegaArmed = false;
        mega::gMegaUsedThisBattle = false;
        mega::gMegaBattleAllowed = !(rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_SAFARI) ||
                                     rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_RAID));
        FlagWork::SetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE, !mega::gMegaBattleAllowed);
        Orig(battleSetupParam, arenaID, mapAttrib, weatherType, rule, enemyID0, enemyID1, partnerID);
    }
};

namespace mega {
void installPostInitHooks() {
    if (gPostInitHooksInstalled) {
        return;
    }
    gPostInitHooksInstalled = true;

    ItemWork$$GetItemData::InstallAtOffset(0x01aea840);
    ItemWork$$GetItemInfo::InstallAtOffset(0x01aea5b0);
    CheckUnbreakablePokeItem::InstallAtOffset(0x01d0d780);
    ItemIcon$$LoadSprite::InstallAtOffset(0x01ca7100);
    UIText$$SetupMessageByIndex::InstallAtOffset(0x01dd1940);
    UIText$$SetupMessageById::InstallAtOffset(0x01dd18d0);
    MessageManager$$GetSimpleMessage::InstallAtOffset(0x0210d000);
    MessageManager$$GetNameMessage::InstallAtOffset(0x0210ce50);
    BUIWazaList$$Initialize::InstallAtOffset(0x01d2b800);
    BUIWazaList$$OnShow::InstallAtOffset(0x01d2cb70);
    BUIWazaList$$OnUpdate::InstallAtOffset(0x01d2c490);
    BUIWazaList$$OnSubmit::InstallAtOffset(0x01d2c6e0);
    BUIActionList$$OnUpdate::InstallAtOffset(0x01e8bdb0);
    CalcActionPriority$$Execute::InstallAtOffset(0x021ad570);
    ProcessActionCore$$action::InstallAtOffset(0x021c0de0);
    setupPokeAction_FromClientInstruction::InstallAtOffset(0x021cdbf0);
    createPokeAction_FromClientInstruction::InstallAtOffset(0x021cdad0);
    CMD_ACT_PokeChangeEffect_WaitCore::InstallAtOffset(0x01c86aa0);
    CMD_ChangeForm_Start::InstallAtOffset(0x01c86d9c);
    SetupBattleWild::InstallAtOffset(0x02c3abc0);
    SetupBattleTrainer::InstallAtOffset(0x02c3b800);
}
} // namespace mega

extern "C" void exl_main(void*, void*) {
    exl::hook::Initialize();
    ItemData$$GetParam::InstallAtOffset(0x0249b950);
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT(0x420);
}
