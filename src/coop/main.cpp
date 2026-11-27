// Pokemon Co-op client module (subsdk3).
//
// We start our network threads right after the game sets up networking
// (nn::socket::Initialize), which happens once at boot on the main thread.
// That way we never initialise sockets ourselves (doing it twice would fail)
// and the OS is fully up before we create threads.

#include "nn/result.h"
#include "exlaunch.hpp"
#include "nn/ro.h"
#include "nn/socket.h"

#include "coop.hpp"

HOOK_DEFINE_TRAMPOLINE(SocketInitWithConfig) {
    static nn::Result Callback(nn::socket::Config const& config) {
        nn::Result r = Orig(config);
        coop::StartNetwork();
        return r;
    }
};

HOOK_DEFINE_TRAMPOLINE(SocketInitWithPool) {
    static nn::Result Callback(void* pool, ulong poolSize, ulong allocPoolSize, int concurLimit) {
        nn::Result r = Orig(pool, poolSize, allocPoolSize, concurLimit);
        coop::StartNetwork();
        return r;
    }
};

// Installs a hook only if the symbol exists (InstallAtSymbol would abort the game otherwise).
template <typename Hook>
static bool installIfPresent(const char* symbol) {
    uintptr_t address = 0;
    if (nn::Result(nn::ro::LookupSymbol(&address, symbol)).isFailure() || address == 0) return false;
    Hook::InstallAtPtr(address);
    return true;
}

extern "C" void exl_main(void*, void*) {
    exl::hook::Initialize();
    installIfPresent<SocketInitWithConfig>("_ZN2nn6socket10InitializeERKNS0_6ConfigE");
    installIfPresent<SocketInitWithPool>("_ZN2nn6socket10InitializeEPvmmi");
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT(0x420);
}
