#include "exlaunch.hpp"

#include "externals/BTL_STRID_SET.h"
#include "externals/FlagWork.h"
#include "externals/FlagWork_Enums.h"
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
#include "externals/Dpr/Battle/View/Systems/BattleViewSystem.h"
#include "externals/Dpr/Battle/View/UI/BUIWazaList.h"
#include "externals/Pml/PokeParty.h"
#include "externals/System/String.h"
#include "externals/UnityEngine/Component.h"
#include "externals/UnityEngine/GameObject.h"
#include "externals/UnityEngine/Transform.h"

#include <cstdint>

namespace mega {
constexpr int kCharizardSpecies = 6;
constexpr int kBaseForm = 0;
constexpr int kMegaXForm = 1;
constexpr int kMegaYForm = 2;

// Indices from this fork's data headers; validated against Lumi 2.2F RomFS.
constexpr int kCharizarditeX = 660;
constexpr int kCharizarditeY = 678;
constexpr int kKeyStone = 773;
constexpr int kToughClaws = 181;
constexpr int kDrought = 70;

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
static_assert(kMegaX.ability == kToughClaws && kMegaY.ability == kDrought);
static_assert(kMegaX.hp == 78 && kMegaY.hp == 78);
static_assert(kMegaX.primaryType == 9 && kMegaX.secondaryType == 15);
static_assert(kMegaY.primaryType == 9 && kMegaY.secondaryType == 2);

inline bool getMegaFlag(const Dpr::Battle::Logic::BTL_ACTION_PARAM::Object& param) {
    return ((static_cast<uint64_t>(param.fields.raw) >> kMegaFlagBit) & 1ULL) != 0;
}

inline int targetCharizardMegaForm(Dpr::Battle::Logic::BTL_POKEPARAM::Object* pokeParam) {
    if (pokeParam == nullptr) {
        return -1;
    }
    PlayerWork::getClass()->initIfNeeded();
    const bool hasKeyStone = PlayerWork::GetItem(kKeyStone).fields.Count > 0;
    const bool megaUsed = FlagWork::GetFlag(FlagWork_Flag::FLAG_MEGA_EVOLUTION_UNAVAILABLE);
    return charizardMegaForm(
        pokeParam->GetMonsNo(),
        pokeParam->GetFormNo(),
        pokeParam->GetItem(),
        kCharizarditeX,
        kCharizarditeY,
        hasKeyStone,
        megaUsed
    );
}

inline bool isMegaToggleActive(Dpr::Battle::View::UI::BUIWazaList::Object* wazaList) {
    if (wazaList == nullptr) {
        return false;
    }
    auto* rootTransform = reinterpret_cast<UnityEngine::Component::Object*>(wazaList)->get_transform();
    if (rootTransform == nullptr) {
        return true;
    }

    // Lumi Madrid UI places the toggle under GimmickButton/Gimmicks/MegaIcon/Active.
    auto* gimmickButton = rootTransform->Find(System::String::Create("GimmickButton"));
    if (gimmickButton != nullptr) {
        auto* gimmicks = gimmickButton->Find(System::String::Create("Gimmicks"));
        if (gimmicks != nullptr) {
            auto* megaIcon = gimmicks->Find(System::String::Create("MegaIcon"));
            if (megaIcon != nullptr) {
                auto* megaIconGo = reinterpret_cast<UnityEngine::Component::Object*>(megaIcon)->get_gameObject();
                if (megaIconGo == nullptr || !megaIconGo->get_activeSelf()) {
                    return false;
                }
                auto* activeTf = megaIcon->Find(System::String::Create("Active"));
                if (activeTf != nullptr) {
                    auto* activeGo = reinterpret_cast<UnityEngine::Component::Object*>(activeTf)->get_gameObject();
                    return activeGo != nullptr && activeGo->get_activeSelf();
                }
            }
        }
        // Vanilla-style GimmickButton fallback: child 0 active state.
        if (gimmickButton->get_childCount() > 0) {
            auto* stateTf = gimmickButton->GetChild(0);
            if (stateTf != nullptr) {
                auto* stateGo = reinterpret_cast<UnityEngine::Component::Object*>(stateTf)->get_gameObject();
                return stateGo != nullptr && stateGo->get_activeSelf();
            }
        }
        return false;
    }

    // If no GimmickButton exists in the active UI prefab, allow eligible Charizard to Mega Evolve.
    return true;
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
            mega::isMegaToggleActive(__this)) {
            destActionParam->fields.value.fields.raw |= static_cast<long>(mega::kMegaFlagMask);
        }
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
