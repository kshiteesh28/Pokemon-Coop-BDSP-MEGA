#include "exlaunch.hpp"
#include <cstdint>

// This module is intentionally separate from Lumi's subsdk1. Only hooks
// implemented and validated in this directory belong here.
namespace mega {
constexpr int kCharizardSpecies = 6;
constexpr int kBaseForm = 0;
constexpr int kMegaXForm = 1;
constexpr int kMegaYForm = 2;

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

static_assert(charizardMegaForm(6, 0, 1001, 1001, 1002, true, false) == 1);
static_assert(charizardMegaForm(6, 0, 1002, 1001, 1002, true, false) == 2);
static_assert(charizardMegaForm(6, 0, 1003, 1001, 1002, true, false) == -1);
static_assert(charizardMegaForm(6, 0, 1001, 1001, 1002, false, false) == -1);
static_assert(charizardMegaForm(6, 0, 1001, 1001, 1002, true, true) == -1);
}

// Hook installation will be added after the game ABI, loader coexistence,
// Charizard form data, and Ryujinx behavior have been verified.
extern "C" void exl_main(void*, void*) {
    exl::hook::Initialize();
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT(0x420);
}
