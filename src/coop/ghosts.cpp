// Stage 2a: show the other players in your world.
//
// Each friend on your map gets a marker figure (a capsule) that walks to where
// they really are. It's made with Unity's built-in GameObject.CreatePrimitive,
// looked up by name through the game's internal-call table (the same way the
// main mod finds Screen.SetResolution), so no hard-coded addresses are guessed.
// If that lookup fails we log it and simply show nothing instead of crashing.
//
// Runs only on the game's main thread (called from the player's OnUpdate hook).

#include <cmath>

#include "nn/result.h"
#include "exlaunch.hpp"
#include "externals/UnityEngine/GameObject.h"
#include "externals/UnityEngine/Transform.h"
#include "externals/UnityEngine/_Object.h"
#include "externals/UnityEngine/Vector3.h"

#include "coop.hpp"

extern void* (*resolve_unity_method)(const char* signature);

namespace coop {

namespace {
constexpr int32_t kPrimitiveCapsule = 1;
constexpr int32_t kLayerIgnoreRaycast = 2;      // keeps the marker out of the game's ground/obstacle checks
constexpr float kMarkerHeight = 1.2f;           // a bit shorter than a trainer
constexpr uint64_t kTicksPerSecond = 19200000;  // Switch system tick rate

using CreatePrimitiveFn = UnityEngine::GameObject::Object* (*)(int32_t type);
using SetLayerFn = void (*)(UnityEngine::GameObject::Object* self, int32_t layer);

CreatePrimitiveFn gCreatePrimitive = nullptr;
SetLayerFn gSetLayer = nullptr;
bool gResolved = false, gAvailable = false;

struct Ghost {
    uint16_t id = 0;
    UnityEngine::GameObject::Object* go = nullptr;
    float x = 0, y = 0, z = 0, yaw = 0;
};
Ghost gGhosts[kMaxRemote];

bool resolveOnce() {
    if (gResolved) return gAvailable;
    gResolved = true;
    gCreatePrimitive = reinterpret_cast<CreatePrimitiveFn>(
        resolve_unity_method("UnityEngine.GameObject::CreatePrimitive(UnityEngine.PrimitiveType)"));
    gSetLayer = reinterpret_cast<SetLayerFn>(resolve_unity_method("UnityEngine.GameObject::set_layer(System.Int32)"));
    gAvailable = gCreatePrimitive != nullptr;
    Log(gAvailable ? "Player markers ready" : "Player markers unavailable (CreatePrimitive not found)");
    return gAvailable;
}

// A Unity object whose native side was destroyed (e.g. on a scene change) keeps
// its managed wrapper but m_CachedPtr becomes 0.
bool alive(UnityEngine::GameObject::Object* go) {
    return go != nullptr && reinterpret_cast<UnityEngine::_Object::Object*>(go)->fields.m_CachedPtr != 0;
}

void destroyGhost(Ghost& g) {
    if (alive(g.go)) UnityEngine::_Object::Destroy(reinterpret_cast<UnityEngine::_Object::Object*>(g.go));
    g = Ghost{};
}

UnityEngine::Vector3::Object vec(float x, float y, float z) {
    UnityEngine::Vector3::Object v{};
    v.fields.x = x;
    v.fields.y = y;
    v.fields.z = z;
    return v;
}

void place(Ghost& g) {
    auto* t = g.go->get_transform();
    if (t == nullptr) return;
    t->set_position(vec(g.x, g.y + kMarkerHeight * 0.5f, g.z));  // capsule pivot is its middle
    t->set_localEulerAngles(vec(0, g.yaw, 0));
}

Ghost* spawn(const RemotePlayer& rp) {
    Ghost* slot = nullptr;
    for (auto& g : gGhosts) {
        if (g.go == nullptr) {
            slot = &g;
            break;
        }
    }
    if (slot == nullptr) return nullptr;
    auto* go = gCreatePrimitive(kPrimitiveCapsule);
    if (go == nullptr) return nullptr;
    if (gSetLayer) gSetLayer(go, kLayerIgnoreRaycast);
    if (auto* t = go->get_transform()) t->set_localScale(vec(0.5f, kMarkerHeight * 0.5f, 0.5f));  // capsule is 2 units tall
    slot->id = rp.id;
    slot->go = go;
    slot->x = rp.x, slot->y = rp.y, slot->z = rp.z, slot->yaw = rp.yaw;
    place(*slot);
    Log("Showing %s on your map", rp.name[0] ? rp.name : "a player");
    return slot;
}

float lerpAngle(float a, float b, float t) {
    float d = std::fmod(b - a + 540.0f, 360.0f) - 180.0f;  // shortest way round
    return a + d * t;
}
}  // namespace

void UpdateGhosts(int32_t myZone, float deltaTime) {
    if (!resolveOnce()) return;

    RemotePlayer remote[kMaxRemote];
    int n = GetRemotePlayers(remote, kMaxRemote);
    uint64_t now = svcGetSystemTick();

    // Remove markers for players who left, changed map, went quiet, or whose
    // object Unity destroyed (scene change).
    for (auto& g : gGhosts) {
        if (g.go == nullptr) continue;
        bool keep = false;
        for (int i = 0; i < n; i++) {
            const auto& rp = remote[i];
            if (rp.id == g.id && rp.hasPos && rp.zone == myZone && now - rp.lastUpdateTick < 3 * kTicksPerSecond) keep = true;
        }
        if (!keep || !alive(g.go)) destroyGhost(g);
    }

    float t = deltaTime * 12.0f;  // smooth out the 20 Hz updates
    if (t > 1.0f || t <= 0.0f) t = 1.0f;
    for (int i = 0; i < n; i++) {
        const auto& rp = remote[i];
        if (!rp.hasPos || rp.zone != myZone || now - rp.lastUpdateTick >= 3 * kTicksPerSecond) continue;
        Ghost* g = nullptr;
        for (auto& x : gGhosts) {
            if (x.go != nullptr && x.id == rp.id) g = &x;
        }
        if (g == nullptr) {
            spawn(rp);
            continue;
        }
        float dx = rp.x - g->x, dz = rp.z - g->z;
        if (dx * dx + dz * dz > 25.0f) {  // teleported/warped: snap instead of sliding
            g->x = rp.x, g->y = rp.y, g->z = rp.z;
        } else {
            g->x += dx * t;
            g->y += (rp.y - g->y) * t;
            g->z += dz * t;
        }
        g->yaw = lerpAngle(g->yaw, rp.yaw, t);
        place(*g);
    }
}

void ClearGhosts() {
    for (auto& g : gGhosts) destroyGhost(g);
}

}  // namespace coop
