#include "exlaunch.hpp"

#include "externals/BTL_STRID_SET.h"
#include "externals/FlagWork.h"
#include "externals/FlagWork_Enums.h"
#include "externals/GameController.h"
#include "externals/ItemWork.h"
#include "externals/PlayerWork.h"
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
#include "externals/Dpr/Battle/View/UI/BUIWazaButton.h"
#include "externals/Dpr/Battle/View/UI/BUIWazaList.h"
#include "externals/Pml/Item/ItemData.h"
#include "externals/Pml/PokeParty.h"
#include "externals/System/String.h"
#include "externals/UnityEngine/Color.h"
#include "externals/UnityEngine/Component.h"
#include "externals/UnityEngine/GameObject.h"
#include "externals/UnityEngine/Transform.h"

#include <cstdint>

namespace mega {
constexpr int kCharizardSpecies = 6;
constexpr int kBaseForm = 0;
constexpr int kMegaXForm = 1;
constexpr int kMegaYForm = 2;

// Verified indices from src/mod/data/items.h.
constexpr uint16_t kFireStone = 82;       // Guaranteed BDSP stone icon sprite
constexpr uint16_t kOddKeystone = 111;    // Guaranteed BDSP keystone icon sprite
constexpr uint16_t kCharcoal = 249;       // Holdable item in Items pocket
constexpr uint16_t kMagmaStone = 458;     // Non-registerable Key Item (F_FUNC = 0)
constexpr uint16_t kCharizarditeX = 660;
constexpr uint16_t kCharizarditeY = 678;
constexpr uint16_t kMegaRing = 696;
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
                                int charizarditeY, bool hasKeyStone,
                                bool trainerAlreadyMegaEvolved) {
    if (species != kCharizardSpecies || originalForm != kBaseForm ||
        !hasKeyStone || trainerAlreadyMegaEvolved) {
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
static_assert(kKeyStone == 773);
static_assert(kMegaRing == 696);
static_assert(kMegaX.ability == kToughClaws && kMegaY.ability == kDrought);
static_assert(kMegaX.hp == 78 && kMegaY.hp == 78);
static_assert(kMegaX.primaryType == 9 && kMegaX.secondaryType == 15);
static_assert(kMegaY.primaryType == 9 && kMegaY.secondaryType == 2);

bool gMegaArmed = false;

inline bool isMegaStone(uint16_t itemId) {
    return itemId == kCharizarditeX || itemId == kCharizarditeY;
}

inline bool isMegaKeyItem(uint16_t itemId) {
    return itemId == kKeyStone || itemId == kMegaRing;
}

inline bool hasMegaAccessory() {
    PlayerWork::getClass()->initIfNeeded();
    return PlayerWork::GetItem(kKeyStone).fields.Count > 0 ||
           PlayerWork::GetItem(kMegaRing).fields.Count > 0;
}

inline void ensureMegaStarterItems() {
    static bool grantedThisSession = false;
    if (grantedThisSession) {
        return;
    }
    grantedThisSession = true;

    PlayerWork::getClass()->initIfNeeded();
    if (!hasMegaAccessory() && ItemWork::IsAddItem(kKeyStone, 1)) {
        ItemWork::AddItem(kKeyStone, 1);
    }
    if (PlayerWork::GetItem(kCharizarditeX).fields.Count == 0 && ItemWork::IsAddItem(kCharizarditeX, 1)) {
        ItemWork::AddItem(kCharizarditeX, 1);
    }
    if (PlayerWork::GetItem(kCharizarditeY).fields.Count == 0 && ItemWork::IsAddItem(kCharizarditeY, 1)) {
        ItemWork::AddItem(kCharizarditeY, 1);
    }
}

inline bool getMegaFlag(const Dpr::Battle::Logic::BTL_ACTION_PARAM::Object& param) {
    return ((static_cast<uint64_t>(param.fields.raw) >> kMegaFlagBit) & 1ULL) != 0;
}

inline int targetCharizardMegaForm(Dpr::Battle::Logic::BTL_POKEPARAM::Object* pokeParam) {
    if (pokeParam == nullptr) {
        return -1;
    }
    const bool megaUsed = FlagWork::GetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE);
    return charizardMegaForm(
        pokeParam->GetMonsNo(),
        pokeParam->GetFormNo(),
        pokeParam->GetItem(),
        kCharizarditeX,
        kCharizarditeY,
        hasMegaAccessory(),
        megaUsed
    );
}

inline void syncGimmickUiVisuals(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList, bool eligible, bool armed) {
    if (wazaList == nullptr) {
        return;
    }

    auto* rootTransform = reinterpret_cast<UnityEngine::Component::Object*>(wazaList)->get_transform();
    if (rootTransform != nullptr) {
        auto* gimmickButton = rootTransform->Find(System::String::Create("GimmickButton"));
        if (gimmickButton != nullptr) {
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

    // Visual fallback that works on both Lumi and vanilla battle UIs: tint move text gold when Mega is armed.
    if (wazaList->fields._wazaButtons != nullptr) {
        const UnityEngine::Color::Fields armedColor{ .r = 1.0f, .g = 0.85f, .b = 0.2f, .a = 1.0f };
        const UnityEngine::Color::Fields normalColor = wazaList->fields._wazaTextColor.fields;
        const uint64_t count = wazaList->fields._wazaButtons->max_length;
        for (uint64_t i = 0; i < count; ++i) {
            auto* btn = wazaList->fields._wazaButtons->m_Items[i];
            if (btn != nullptr && btn->fields._text != nullptr) {
                btn->fields._text->virtual_set_color((eligible && armed) ? armedColor : normalColor);
            }
        }
    }
}

inline bool isGimmickIconActive(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList) {
    if (wazaList == nullptr) {
        return false;
    }
    auto* rootTransform = reinterpret_cast<UnityEngine::Component::Object*>(wazaList)->get_transform();
    if (rootTransform == nullptr) {
        return false;
    }

    auto* gimmickButton = rootTransform->Find(System::String::Create("GimmickButton"));
    if (gimmickButton == nullptr) {
        return false;
    }

    if (auto* gimmicks = gimmickButton->Find(System::String::Create("Gimmicks")); gimmicks != nullptr) {
        if (auto* megaIcon = gimmicks->Find(System::String::Create("MegaIcon")); megaIcon != nullptr) {
            auto* megaIconGo = reinterpret_cast<UnityEngine::Component::Object*>(megaIcon)->get_gameObject();
            if (megaIconGo == nullptr || !megaIconGo->get_activeSelf()) {
                return false;
            }
            if (auto* activeTf = megaIcon->Find(System::String::Create("Active")); activeTf != nullptr) {
                auto* activeGo = reinterpret_cast<UnityEngine::Component::Object*>(activeTf)->get_gameObject();
                return activeGo != nullptr && activeGo->get_activeSelf();
            }
        }
        return false;
    }

    if (gimmickButton->get_childCount() > 0) {
        if (auto* stateTf = gimmickButton->GetChild(0); stateTf != nullptr) {
            auto* stateGo = reinterpret_cast<UnityEngine::Component::Object*>(stateTf)->get_gameObject();
            return stateGo != nullptr && stateGo->get_activeSelf();
        }
    }
    return false;
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

    auto* msgDesc = Dpr::Battle::Logic::Section_FromEvent_Message::Description::newInstance();
    msgDesc->fields.pokeID = param->GetID();
    msgDesc->fields.message->Setup(Dpr::Battle::Logic::BtlStrType::BTL_STRTYPE_SET, BTL_STRID_SET::MegaEvoStart);
    msgDesc->fields.message->AddArg(param->GetID());
    msgDesc->fields.message->AddArg(pokeAction->fields.clientID);
    msgDesc->fields.message->AddArg(param->GetItem());
    msgDesc->fields.message->AddArg(kKeyStone);

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

// Read-only parameter delegation for Mega Stones and Key Stone / Mega Ring.
// Never mutates ItemTable or ItemInfo in place, keeping Bag closing and Y-shortcuts intact.
HOOK_DEFINE_TRAMPOLINE(ItemData$$GetParam) {
    static int32_t Callback(uint16_t itemno, Pml::Item::ItemData::PrmID prmID) {
        using PrmID = Pml::Item::ItemData::PrmID;

        if (mega::isMegaStone(itemno)) {
            switch (prmID) {
                case PrmID::ICONID:
                    return Orig(mega::kFireStone, PrmID::ICONID);
                case PrmID::F_POCKET:
                case PrmID::ITEM_TYPE:
                case PrmID::GROUP:
                case PrmID::GROUPID:
                case PrmID::SET_TO_POKE:
                case PrmID::INACTIVE:
                    return Orig(mega::kCharcoal, prmID);
                default:
                    break;
            }
        } else if (mega::isMegaKeyItem(itemno)) {
            switch (prmID) {
                case PrmID::ICONID:
                    return Orig(mega::kOddKeystone, PrmID::ICONID);
                case PrmID::F_POCKET:
                case PrmID::ITEM_TYPE:
                case PrmID::GROUP:
                case PrmID::GROUPID:
                case PrmID::SET_TO_POKE:
                case PrmID::INACTIVE:
                case PrmID::F_FUNC:
                    return Orig(mega::kMagmaStone, prmID);
                default:
                    break;
            }
        }

        return Orig(itemno, prmID);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$Initialize) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this,
                         Dpr::Battle::Logic::BTL_POKEPARAM::Object* bpp,
                         uint8_t pokeIndex,
                         Dpr::Battle::Logic::BTL_ACTION_PARAM_OBJ::Object* dest) {
        Orig(__this, bpp, pokeIndex, dest);
        mega::gMegaArmed = false;
        const bool eligible = mega::targetCharizardMegaForm(bpp) > 0;
        mega::syncGimmickUiVisuals(__this, eligible, false);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnShow) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this) {
        Orig(__this);
        mega::gMegaArmed = false;
        const bool eligible = mega::targetCharizardMegaForm(__this->fields._btlPokeParam) > 0;
        mega::syncGimmickUiVisuals(__this, eligible, false);
    }
};

HOOK_DEFINE_TRAMPOLINE(BUIWazaList$$OnUpdate) {
    static void Callback(Dpr::Battle::View::UI::BUIWazaList::Object* __this, float deltaTime) {
        Orig(__this, deltaTime);

        if (__this == nullptr || !__this->fields._IsShow_k__BackingField) {
            return;
        }

        const bool eligible = mega::targetCharizardMegaForm(__this->fields._btlPokeParam) > 0;
        if (!eligible) {
            return;
        }

        constexpr int32_t kToggleMask =
            GameController::ButtonMask::L |
            GameController::ButtonMask::R |
            GameController::ButtonMask::StickL |
            GameController::ButtonMask::StickR;

        if (Dpr::Battle::View::BtlvInput::GetPush(kToggleMask, true)) {
            mega::gMegaArmed = !mega::gMegaArmed;
            mega::syncGimmickUiVisuals(__this, true, mega::gMegaArmed);

            Dpr::Battle::View::BattleViewCore::getClass()->initIfNeeded();
            if (auto* core = Dpr::Battle::View::BattleViewCore::get_Instance();
                core != nullptr && core->fields._UISystem_k__BackingField != nullptr) {
                core->fields._UISystem_k__BackingField->PlaySe(mega::kSeUiSelect);
            }
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

HOOK_DEFINE_INLINE(OnSubmitWazaButton) {
    static void Callback(exl::hook::nx64::InlineCtx* ctx) {
        auto* __this = reinterpret_cast<Dpr::Battle::View::UI::BUIWazaList::Object*>(ctx->X[19]);
        auto* destActionParam = __this->fields._destActionParam;
        ctx->X[8] = reinterpret_cast<u64>(destActionParam);

        if (destActionParam != nullptr &&
            mega::targetCharizardMegaForm(__this->fields._btlPokeParam) > 0 &&
            (mega::gMegaArmed || mega::isGimmickIconActive(__this))) {
            destActionParam->fields.value.fields.raw |= static_cast<long>(mega::kMegaFlagMask);
        }
        mega::gMegaArmed = false;
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
        Dpr::Battle::Logic::BTL_ACTION_PARAM::Object firstInstruction{};
        firstInstruction.fields.raw = Dpr::Battle::Logic::SVCL_ACTION::Get(instructions, clientID, 0)->fields.raw;

        if (!mega::getMegaFlag(firstInstruction)) {
            Orig(__this, actionContainer, instructions, clientID);
            return;
        }

        const uint8_t numAction = Dpr::Battle::Logic::SVCL_ACTION::GetNumAction(instructions, clientID);
        if (party != nullptr && numAction != 0) {
            for (uint64_t i = 0; i < static_cast<uint64_t>(numAction) + 1; ++i) {
                auto* newAction = Dpr::Battle::Logic::PokeAction::newInstance();
                if (i == numAction) {
                    firstInstruction.fields.raw &= ~static_cast<long>(mega::kMegaFlagMask);
                }
                if (__this->setupPokeAction_FromClientInstruction(newAction, &firstInstruction, clientID)) {
                    actionContainer->Add(&newAction);
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
        mega::ensureMegaStarterItems();
        FlagWork::SetFlag(
            FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE,
            isCaptureDemo || battleSetupParam->fields.rule == Dpr::Battle::Logic::BtlRule::BTL_RULE_SAFARI
        );
        Orig(battleSetupParam, iPtrEnemyParty, arenaID, mapAttrib, weatherType, isSwim, isFishing, partnerID,
             isCaptureDemo, safariBallNum, isSymbol, isMitu, overlapBgm, overlapSetupEffectId, isCantUseBall);
    }
};

HOOK_DEFINE_TRAMPOLINE(SetupBattleTrainer) {
    static void Callback(Dpr::Battle::Logic::BATTLE_SETUP_PARAM::Object* battleSetupParam, int32_t arenaID,
                         int32_t mapAttrib, int32_t weatherType, int32_t rule, int32_t enemyID0, int32_t enemyID1,
                         int32_t partnerID) {
        mega::gMegaArmed = false;
        mega::ensureMegaStarterItems();
        FlagWork::SetFlag(
            FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE,
            rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_SAFARI) ||
            rule == static_cast<int32_t>(Dpr::Battle::Logic::BtlRule::BTL_RULE_RAID)
        );
        Orig(battleSetupParam, arenaID, mapAttrib, weatherType, rule, enemyID0, enemyID1, partnerID);
    }
};

extern "C" void exl_main(void*, void*) {
    exl::hook::Initialize();

    ItemData$$GetParam::InstallAtOffset(0x0249b950);
    BUIWazaList$$Initialize::InstallAtOffset(0x01d2b800);
    BUIWazaList$$OnShow::InstallAtOffset(0x01d2cb70);
    BUIWazaList$$OnUpdate::InstallAtOffset(0x01d2c490);
    OnSubmitWazaButton::InstallAtOffset(0x01d2cee8);
    CalcActionPriority$$Execute::InstallAtOffset(0x021ad570);
    ProcessActionCore$$action::InstallAtOffset(0x021c0de0);
    setupPokeAction_FromClientInstruction::InstallAtOffset(0x021cdbf0);
    createPokeAction_FromClientInstruction::InstallAtOffset(0x021cdad0);
    CMD_ACT_PokeChangeEffect_WaitCore::InstallAtOffset(0x01c86aa0);
    CMD_ChangeForm_Start::InstallAtOffset(0x01c86d9c);
    SetupBattleWild::InstallAtOffset(0x02c3abc0);
    SetupBattleTrainer::InstallAtOffset(0x02c3b800);
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT(0x420);
}
