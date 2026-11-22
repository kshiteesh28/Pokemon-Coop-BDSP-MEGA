#include "exlaunch.hpp"
#include <cstdint>

// This module is intentionally separate from Lumi's subsdk1. Only hooks
// implemented and validated in this directory belong here.
namespace mega {
constexpr int kCharizardSpecies = 6;
constexpr int kBaseForm = 0;
constexpr int kMegaXForm = 1;
constexpr int kMegaYForm = 2;
// Indices from this fork's data headers; validate against the installed RomFS.
constexpr int kCharizarditeX = 660;
constexpr int kCharizarditeY = 678;
constexpr int kKeyStone = 773;
constexpr int kToughClaws = 181;
constexpr int kDrought = 70;

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
}

// Hook installation will be added after the game ABI, loader coexistence,
// Charizard form data, and Ryujinx behavior have been verified.
extern "C" void exl_main(void*, void*) {
    exl::hook::Initialize();
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT(0x420);
}
