#include "dofus_native_mod_api.h"
#include "core/module.hpp"
#include "appearance/features.hpp"
#include "appearance/miniature-category-labels.hpp"
#include "animations/instant-combat.hpp"
#include <fstream>
#include <string>
#include <string_view>
#include <vector>

using namespace managed;
Module* module = nullptr;
namespace {
const DnhHostApiV8* host{};
std::vector<void*> hooks;
using UpdateFn = void(*)(Obj, Method);
UpdateFn originalUpdate{};
std::filesystem::path folder;
unsigned mask = 0;
ULONGLONG nextRead = 0, nextDiscover = 0, nextAppearance = 0;
bool appearanceDirty = true;
bool inVisualUpdate = false;
std::string lastStatus;
constexpr std::string_view CombatModeKey = "combat-mode=";
constexpr std::string_view RoleplayModeKey = "roleplay-mode=";
constexpr std::string_view CombatMiniaturesKey = "combat-miniatures=";
constexpr std::string_view RoleplayMiniaturesKey = "roleplay-miniatures=";

void Status(const std::string& value) {
    if (value == lastStatus) return;
    lastStatus = value;
    auto path = folder / L"visual-options.status";
    auto tmp = folder / L"visual-options.status.tmp";
    { std::ofstream stream(tmp, std::ios::trunc); stream << value << '\n'; }
    MoveFileExW(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING);
}
std::string VisualStatus() {
    return std::string(module->servicesReady ? "READY" : "WAITING_FOR_CHARACTER")
        + " mask=" + std::to_string(mask)
        + " combat-mode=" + std::to_string(module->settings.mode)
        + " roleplay-mode=" + std::to_string(module->settings.roleplayMode)
        + " in_combat=" + (module->InCombat() ? "1" : "0");
}
unsigned ParseMask(const std::string& line) {
    if (!line.starts_with("mask=") || line.size() >= 16) return 0;
    try {
        size_t consumed = 0;
        unsigned value = std::stoul(line.substr(5), &consumed);
        if (consumed != line.size() - 5 || value > 63) return 0;
        return value;
    } catch (...) { return 0; }
}
int ParseMode(const std::string& text) {
    if (text == "0" || text == "1" || text == "2") return text[0] - '0';
    return 0;
}
bool ParseDigits(const std::string& text, size_t width, bool* values) {
    if (text.size() != width) return false;
    for (size_t i = 0; i < width; ++i) {
        if (text[i] != '0' && text[i] != '1') return false;
        values[i] = text[i] == '1';
    }
    return true;
}
template<size_t N>
void ApplyMiniatureLine(bool Settings::* const (&fields)[N], bool saw, const std::string& text) {
    bool digits[N]{};
    bool parsed = saw && ParseDigits(text, N, digits);
    Settings defaults;
    size_t index = 0;
    for (auto field : fields) {
        module->settings.*field = parsed ? digits[index] : defaults.*field;
        ++index;
    }
}
void ReadOptions() {
    if (!module || GetTickCount64() < nextRead) return;
    nextRead = GetTickCount64() + 250;
    unsigned value = 0;
    bool sawCombatMode = false, sawRoleplayMode = false, sawCombatMini = false, sawRoleplayMini = false;
    std::string combatModeText, roleplayModeText, combatMiniText, roleplayMiniText;
    std::ifstream stream(folder / L"visual-options.ini");
    std::string line;
    while (std::getline(stream, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.starts_with("mask=")) value = ParseMask(line);
        else if (line.starts_with(CombatModeKey)) { sawCombatMode = true; combatModeText = line.substr(CombatModeKey.size()); }
        else if (line.starts_with(RoleplayModeKey)) { sawRoleplayMode = true; roleplayModeText = line.substr(RoleplayModeKey.size()); }
        else if (line.starts_with(CombatMiniaturesKey)) { sawCombatMini = true; combatMiniText = line.substr(CombatMiniaturesKey.size()); }
        else if (line.starts_with(RoleplayMiniaturesKey)) { sawRoleplayMini = true; roleplayMiniText = line.substr(RoleplayMiniaturesKey.size()); }
    }
    int nextMode = sawCombatMode ? ParseMode(combatModeText) : 0;
    int nextRole = sawRoleplayMode ? ParseMode(roleplayModeText) : 0;
    if (value != mask || nextMode != module->settings.mode || nextRole != module->settings.roleplayMode)
        appearanceDirty = true;
    unsigned categories = MiniatureCategoryMask(module->settings);
    mask = value;
    module->settings.noSpellEffects = (mask & 1) != 0;
    module->settings.shortSpellCasts = (mask & 2) != 0;
    module->settings.noAttackAnimations = (mask & 4) != 0;
    module->settings.noDeathAnimations = (mask & 8) != 0;
    module->settings.instantMovements = (mask & 16) != 0;
    module->settings.noVisualWaits = (mask & 32) != 0;
    module->settings.mode = nextMode;
    module->settings.roleplayMode = nextRole;
    ApplyMiniatureLine(CombatMiniatureToggles, sawCombatMini, combatMiniText);
    ApplyMiniatureLine(RoleplayMiniatureToggles, sawRoleplayMini, roleplayMiniText);
    if (MiniatureCategoryMask(module->settings) != categories) appearanceDirty = true;
}
Obj Service(Obj dictionary, const char* name) {
    auto type = api.Class("Core.dll", "", name);
    Ref key(api.type_get_object(api.class_get_type(type)));
    if (api.Value<bool>(api.Call(dictionary, "ContainsKey", {key.get()}))) return api.Call(dictionary, "get_Item", {key.get()});
    Ref values(api.Call(dictionary, "get_Values"));
    Ref iterator(api.Call(values.get(), "GetEnumerator"));
    unsigned count = 0;
    while (api.Value<bool>(api.Call(iterator.get(), "MoveNext"))) {
        if (++count > 2048) throw std::runtime_error("Invalid service registry");
        auto object = api.Call(iterator.get(), "get_Current");
        if (object && api.class_is_assignable_from(type, api.object_get_class(object))) { api.Call(iterator.get(), "Dispose"); return object; }
    }
    api.Call(iterator.get(), "Dispose"); return nullptr;
}
bool OptionLoaded(Obj option) {
    return option && api.Value<bool>(api.Call(option, "IsLoaded"));
}
bool OptionValue(Obj option) {
    return api.Value<bool>(api.Call(option, "get_property"));
}
void SetOption(Obj option, bool enabled, bool notify) {
    if (!OptionLoaded(option) || OptionValue(option) == enabled) return;
    api.Call(option, notify ? "set_property" : "SetValueWithoutNotify", {&enabled});
}
void RestoreCreatureModes() {
    if (!module->originalModesKnown || !module->servicesReady) return;
    bool combat = module->InCombat();
    SetOption(module->creatureOption.get(), module->originalCreatureMode, NotifyCreaturePreference(true, combat));
    SetOption(module->roleplayOption.get(), module->originalRoleplayMode, NotifyCreaturePreference(false, combat));
    if (module->features)
        module->features->Refresh(combat ? module->originalCreatureMode : module->originalRoleplayMode);
}
void AdoptCreatureOptions(Obj container) {
    if (!module || module->stopped || module->appearanceRestored) return;
    auto nextCreature = api.Field(container, "creatureModeFight");
    auto nextRole = api.Field(container, "creatureModeRoleplay");
    if (nextCreature == module->creatureOption.get() && nextRole == module->roleplayOption.get()) return;
    if (module->originalModesKnown) {
        try { RestoreCreatureModes(); }
        catch (const std::exception& error) {
            Log(std::string("MODE_RESTORE_ERROR ") + error.what());
            module->stopRequested = true;
        }
    }
    module->creatureOption.reset(nextCreature);
    module->roleplayOption.reset(nextRole);
    module->originalModesKnown = false;
    appearanceDirty = true;
}
bool CaptureOriginalModes() {
    if (module->originalModesKnown) return true;
    auto combat = module->creatureOption.get();
    auto role = module->roleplayOption.get();
    if (!OptionLoaded(combat) || !OptionLoaded(role)) return false;
    module->originalCreatureMode = OptionValue(combat);
    module->originalRoleplayMode = OptionValue(role);
    module->originalModesKnown = true;
    return true;
}
template<size_t N>
std::string ToggleDigits(bool Settings::* const (&fields)[N]) {
    std::string text;
    for (auto field : fields) text += module->settings.*field ? '1' : '0';
    return text;
}
void ApplyAppearance() {
    if (!module || module->stopped || module->appearanceRestored || !module->servicesReady || !module->features) return;
    auto now = GetTickCount64();
    if (module->originalModesKnown && !appearanceDirty && now < nextAppearance) return;
    nextAppearance = now + 200;
    bool first = !module->originalModesKnown;
    if (!CaptureOriginalModes()) return;
    bool combat = module->InCombat();
    bool wantCombat = module->settings.mode != 0;
    bool wantRole = module->settings.roleplayMode != 0;
    bool haveCombat = OptionLoaded(module->creatureOption.get());
    bool haveRole = OptionLoaded(module->roleplayOption.get());
    bool mismatch = (haveCombat && OptionValue(module->creatureOption.get()) != wantCombat)
        || (haveRole && OptionValue(module->roleplayOption.get()) != wantRole);
    if (!first && !appearanceDirty && !mismatch) return;
    if (haveCombat) SetOption(module->creatureOption.get(), wantCombat, NotifyCreaturePreference(true, combat));
    if (haveRole) SetOption(module->roleplayOption.get(), wantRole, NotifyCreaturePreference(false, combat));
    if (first || appearanceDirty) {
        Log("APPEARANCE combat=" + std::to_string(module->settings.mode)
            + " roleplay=" + std::to_string(module->settings.roleplayMode)
            + " miniatures=" + ToggleDigits(CombatMiniatureToggles) + "/" + ToggleDigits(RoleplayMiniatureToggles));
    }
    module->features->Refresh(ActiveDisplayMode(module->settings, combat) != 0);
    appearanceDirty = false;
}
void Update(Obj source, Method method) {
    originalUpdate(source, method);
    if (inVisualUpdate || !module || !module->ready || module->appearanceRestored) return;
    if (module->stopped || module->stopRequested) return;
    struct Guard { Guard() { inVisualUpdate = true; } ~Guard() { inVisualUpdate = false; } } guard;
    try {
        if (GetTickCount64() >= nextDiscover) { nextDiscover = GetTickCount64() + 500; module->Discover(source); }
        if (module->stopRequested) return;
        bool combat = module->InCombat();
        if (combat != module->previousCombat) { module->previousCombat = combat; appearanceDirty = true; }
        ApplyAppearance();
        if (module->features) module->features->Tick();
    } catch (const std::exception& e) { Log(std::string("VISUAL_STATE_ERROR ") + e.what()); module->stopRequested = true; }
}
void RemoveHooks() {
    if (!host || !host->hooks) return;
    for (auto it = hooks.rbegin(); it != hooks.rend(); ++it) host->hooks->disable(*it);
    for (auto it = hooks.rbegin(); it != hooks.rend(); ++it) host->hooks->remove(*it);
    hooks.clear();
}
}

void Log(const std::string& text) {
    if (host && host->log) host->log(DNH_LOG_INFO, reinterpret_cast<const uint8_t*>(text.data()), text.size());
}
void Features::Restore(bool) {
    sizes.pending.clear();
    roleplayIdentity = {};
    roleplayIdentityKnown = false;
    nextRoleplayIdentityRead = 0;
    previousLocalTeam = -1;
    loggedPendingSize = false;
    ClearInstantWaits();
    InvalidateLookCache();
}
void RestoreAppearanceOnce() {
    if (!module || module->appearanceRestored) return;
    module->appearanceRestored = true;
    module->stopped = true;
    try { RestoreCreatureModes(); }
    catch (const std::exception& error) { Log(std::string("MODE_RESTORE_ERROR ") + error.what()); }
    try { if (module->features) module->features->Restore(true); }
    catch (const std::exception& error) { Log(std::string("RENDER_RESTORE_ERROR ") + error.what()); }
}
bool Module::InCombat() const { return servicesReady && fight.get() && Read<bool>(fight.get(), 0xB0); }
Obj Module::FindService(const char* name) const {
    if (!servicesReady || !compositor.get() || stopped) return nullptr;
    auto dictionary = api.Field(compositor.get(), "eavv");
    return dictionary ? Service(dictionary, name) : nullptr;
}
void Module::Discover(Obj source) {
    if (!source || !Read<bool>(source, 0x48)) { servicesReady = false; creatures.reset(); return; }
    auto dictionary = api.Field(source, "eavv");
    if (!dictionary) { servicesReady = false; creatures.reset(); return; }
    auto found = Service(dictionary, "fsl");
    if (!found) { servicesReady = false; creatures.reset(); return; }
    auto cached = api.Field(found, "drii");
    auto nextFight = api.Field(found, "drih");
    if (!cached || !nextFight) { servicesReady = false; creatures.reset(); return; }
    if (std::string(api.class_get_name(api.object_get_class(found))) != "exf" ||
        std::string(api.class_get_name(api.object_get_class(nextFight))) != "fxc") throw std::runtime_error("Unexpected fight service");
    auto container = api.Call(cached, "get_dofusOptionsContainer");
    auto option = container ? api.Field(container, "allowSpellEffects") : nullptr;
    if (!option) { servicesReady = false; creatures.reset(); return; }
    if (container) AdoptCreatureOptions(container);
    bool becameReady = !servicesReady;
    compositor.reset(source);
    creatures.reset(found);
    fight.reset(nextFight);
    spellEffectsOption.reset(option);
    servicesReady = true;
    if (becameReady) { previousCombat = InCombat(); appearanceDirty = true; }
}
void AddNativeHook(uintptr_t rva, void* replacement, void** original) {
    auto target = api.NativeAt(rva);
    if (host->hooks->create(target, replacement, original) != DNH_OK) throw std::runtime_error("Cannot prepare visual hook");
    hooks.push_back(target);
}
void AddHook(Method method, uintptr_t rva, void* replacement, void** original) { api.Native(method, rva); AddNativeHook(rva, replacement, original); }

static const DnhModInfoV1 info{DNH_ABI_VERSION_8, sizeof(DnhModInfoV1), "givre.dofus3.combat-visuals", "Givre Combat Visuals", "1.0.0", "Givre / Altansar69 (Unlicense)"};
DNH_EXPORT const DnhModInfoV1* DNH_CALL DNM_Query(uint32_t abi) { return abi == DNH_ABI_VERSION_8 ? &info : nullptr; }
DNH_EXPORT int32_t DNH_CALL DNM_Load(const DnhHostApiV1* apiHost) {
    if (!apiHost || apiHost->abi_version != DNH_ABI_VERSION_8 || apiHost->struct_size < sizeof(DnhHostApiV8)) return DNH_ERROR;
    host = reinterpret_cast<const DnhHostApiV8*>(apiHost);
    try {
        HMODULE own{};
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&DNM_Load), &own)) throw std::runtime_error("Module path unavailable");
        folder = ModulePath(own).parent_path();
        if (!SupportedClient(ModulePath(nullptr))) throw std::runtime_error("Dofus 3.7.12.12 fingerprints differ; visual hooks refused");
        api.Load(GetModuleHandleW(L"GameAssembly.dll"));
        api.SelfTest();
        appearanceDirty = true;
        nextAppearance = 0;
        nextRead = 0;
        module = new Module;
        module->features = std::make_unique<Features>();
        ReadOptions();
        module->features->Install();
        auto compositor = api.Class("Core.dll", "Core.Engine.Compositor", "Compositor");
        AddHook(api.Find(compositor, "Update", 0), 0x20F9080, reinterpret_cast<void*>(Update), reinterpret_cast<void**>(&originalUpdate));
        for (auto target : hooks) if (host->hooks->enable(target) != DNH_OK) throw std::runtime_error("Cannot enable visual hook");
        module->ready = true;
        Log("VISUAL_OPTIONS_LOADED exact client hashes and method signatures checked; no packet modified; creature display restored on unload");
        Status(VisualStatus());
        return DNH_OK;
    } catch (const std::exception& e) {
        Log(std::string("VISUAL_OPTIONS_ERROR ") + e.what()); Status(std::string("ERROR ") + e.what());
        if (module) RestoreAppearanceOnce();
        RemoveHooks(); delete module; module = nullptr; return DNH_ERROR;
    }
}
DNH_EXPORT void DNH_CALL DNM_Tick() {
    if (!module) return;
    ReadOptions();
    if (module->stopRequested || module->stopped) {
        RestoreAppearanceOnce();
        Status("ERROR visual callback stopped; native game behavior restored");
        return;
    }
    Status(VisualStatus());
}
DNH_EXPORT void DNH_CALL DNM_Unload() {
    if (module) RestoreAppearanceOnce();
    if (module) module->ready = false;
    RemoveHooks();
    delete module; module = nullptr; Status("DISABLED");
}
