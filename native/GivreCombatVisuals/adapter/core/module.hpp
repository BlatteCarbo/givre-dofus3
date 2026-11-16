#pragma once
#include "core/runtime.hpp"
#include "core/settings.hpp"
#include <atomic>
#include <memory>
struct Features;
struct Module {
    Settings settings;
    managed::Ref compositor, creatures, fight, spellEffectsOption, creatureOption, roleplayOption;
    bool originalCreatureMode = false, originalRoleplayMode = false, originalModesKnown = false;
    bool previousCombat = false, appearanceRestored = false;
    std::atomic_bool ready{false}, stopped{false}, stopRequested{false};
    bool servicesReady = false;
    std::unique_ptr<Features> features;
    bool InCombat() const;
    void Discover(managed::Obj source);
    managed::Obj FindService(const char* name) const;
};
extern Module* module;
void AddNativeHook(uintptr_t rva, void* replacement, void** original);
void AddHook(managed::Method method, uintptr_t rva, void* replacement, void** original);
