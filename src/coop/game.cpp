// Reads the local player on the game's main thread.
//
// We don't know a fixed address for the player's per-frame update, so instead
// of a code hook we swap the OnUpdate entry in the FieldPlayerEntity class's
// virtual table (il2cpp keeps it in writable memory). Our function calls the
// original first, then copies the player's map, position and facing into a
// small shared struct for the network thread.

#include "nn/result.h"
#include "exlaunch.hpp"
#include "externals/EntityManager.h"
#include "externals/FieldPlayerEntity.h"
#include "externals/PlayerWork.h"
#include "nn/os.h"

#include "coop.hpp"

namespace coop {

namespace {
nn::os::MutexType gStateMutex;
bool gStateMutexInit = false;
LocalState gState;

using OnUpdateFn = void (*)(FieldPlayerEntity::Object*, float, const MethodInfo*);
OnUpdateFn gOrigOnUpdate = nullptr;
void* gHookedClass = nullptr;  // we only ever patch one class, so one original pointer is enough

void ensureMutex() {
    if (!gStateMutexInit) {
        nn::os::InitializeMutex(&gStateMutex, false, 0);
        gStateMutexInit = true;
    }
}

FieldPlayerEntity::Object* activePlayer() {
    auto* em = EntityManager::getClass();
    if (em == nullptr || em->static_fields == nullptr) return nullptr;
    return em->static_fields->_activeFieldPlayer_k__BackingField;
}

void OnUpdateHook(FieldPlayerEntity::Object* self, float deltaTime, const MethodInfo* method) {
    gOrigOnUpdate(self, deltaTime, method);
    if (self == nullptr || self != activePlayer()) return;  // only the local trainer

    auto& f = self->fields;
    LocalState s;
    s.inField = true;
    s.zone = PlayerWork::get_zoneID();
    s.x = f.worldPosition.fields.x;
    s.y = f.worldPosition.fields.y;
    s.z = f.worldPosition.fields.z;
    s.yaw = f.yawAngle;
    s.speed = f._MoveSpeed_k__BackingField;
    int form = f.FormType;
    s.form = static_cast<uint8_t>(form < 0 ? 0 : (form > 255 ? 255 : form));

    nn::os::LockMutex(&gStateMutex);
    s.frame = gState.frame + 1;
    gState = s;
    nn::os::UnlockMutex(&gStateMutex);

    UpdateGhosts(s.zone, deltaTime);
    LegendTick();
    BattleTick();
}
}  // namespace

LocalState GetLocalState() {
    ensureMutex();
    nn::os::LockMutex(&gStateMutex);
    LocalState s = gState;
    nn::os::UnlockMutex(&gStateMutex);
    return s;
}

bool TryInstallFieldHook() {
    ensureMutex();
    auto* player = activePlayer();
    if (player == nullptr || player->klass == nullptr) return false;

    if (gHookedClass != nullptr && gHookedClass != player->klass) {
        return true;  // a different player class; leave it alone rather than mix up originals
    }
    auto& slot = player->klass->vtable._10_OnUpdate;
    auto ours = reinterpret_cast<Il2CppMethodPointer>(&OnUpdateHook);
    if (slot.methodPtr == ours) return true;
    if (slot.methodPtr == nullptr) return false;

    // Publish the original before switching, so the game never calls our hook
    // without a valid original to forward to.
    __atomic_store_n(&gOrigOnUpdate, reinterpret_cast<OnUpdateFn>(slot.methodPtr), __ATOMIC_SEQ_CST);
    __atomic_store_n(&slot.methodPtr, ours, __ATOMIC_SEQ_CST);
    gHookedClass = player->klass;
    Log("Field hook installed");
    return true;
}

}  // namespace coop
