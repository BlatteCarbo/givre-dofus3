//! Coupe le popup client « Vous êtes resté inactif trop longtemps. Le jeu va se fermer »
//! (i18n 1097071). Le type qui tient le minuteur n'a pas de nom stable : le tas de chaînes
//! de global-metadata.dat (v39, client installé) contient les chemins source
//! `\Assets\Core\Services\Player\InactivityService.cs` et
//! `\Assets\Core\Services\Contracts\IInactivityService.cs` dans le blob de valeurs par défaut,
//! mais aucune TypeDefinition ne s'appelle InactivityService. La commande console
//! `resetInactivity` est la méthode obfusquée Core.dll `.qf::pkl` (jeton 0x06001D26) : on ne
//! l'appelle pas.
//!
//! Le seul symbole nommé et résoluble est le getter statique `UnityEngine.Input.get_anyKey`
//! (image `UnityEngine.InputLegacyModule.dll`, type index 43789, méthode index 390698,
//! 0 paramètre, flags 0x896, iflags 0x1000 internal call). L'icall enregistrée dans
//! GameAssembly.dll est `UnityEngine.Input::get_anyKey()`. `get_anyKeyDown` (index 390699,
//! icall `UnityEngine.Input::get_anyKeyDown()`) n'est pas crochetée : un front d'appui
//! synthétique cliquerait l'interface.
//!
//! `inactivity.ini` à côté de cette DLL, écrit par l'application :
//! `inactivity=true` (défaut si le fichier ou la clé manque) ou `inactivity=false`.
//! Faux désactive le détour et rend l'implémentation d'origine. `inactivity.status` vaut
//! `hooked`, `restored` ou `unresolved`.

use std::ffi::c_void;
use std::os::windows::ffi::OsStrExt;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicPtr, Ordering};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use dofus_native_mod_api::{DNH_MEMBER_STORAGE_STATIC, DnhHandle, DnhLogLevel};
use dofus_native_mod_sdk::Runtime;

type ManagedAnyKeyFn = unsafe extern "system" fn(DnhHandle) -> u8;
type IcallAnyKeyFn = unsafe extern "system" fn() -> u8;

static HOOK_ON: AtomicBool = AtomicBool::new(false);
static ICALL_TARGET: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());
static ICALL_ORIGINAL: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());
static MANAGED_TARGET: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());
static MANAGED_ORIGINAL: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());

struct Control {
    next_read: Instant,
    applied: Option<bool>,
    logged_missing: bool,
    announced_ready: bool,
    last_status: &'static str,
}

fn control() -> &'static Mutex<Control> {
    static CONTROL: OnceLock<Mutex<Control>> = OnceLock::new();
    CONTROL.get_or_init(|| {
        Mutex::new(Control {
            next_read: Instant::now(),
            applied: None,
            logged_missing: false,
            announced_ready: false,
            last_status: "",
        })
    })
}

/// Dernière valeur `inactivity` gagne. Absent, vide ou illisible côté appelant : actif.
pub fn parse(text: &str) -> bool {
    let mut enabled = true;
    for raw in text.lines() {
        let line = raw.trim_end_matches('\r').trim();
        if line.is_empty() || line.starts_with('#') {
            continue;
        }
        let Some((key, value)) = line.split_once('=').or_else(|| line.split_once('\t')) else {
            continue;
        };
        if key.trim() != "inactivity" {
            continue;
        }
        match value.trim() {
            "true" => enabled = true,
            "false" => enabled = false,
            _ => {}
        }
    }
    enabled
}

fn read_enabled(dir: &Path) -> bool {
    let Ok(text) = std::fs::read_to_string(dir.join("inactivity.ini")) else {
        return true;
    };
    if text.len() > 128 {
        return true;
    }
    parse(&text)
}

fn module_dir() -> Option<PathBuf> {
    unsafe extern "system" {
        fn GetModuleHandleExW(flags: u32, address: *const u16, module: *mut *mut c_void) -> i32;
        fn GetModuleFileNameW(module: *mut c_void, filename: *mut u16, size: u32) -> u32;
    }
    const FROM_ADDRESS: u32 = 0x4;
    const UNCHANGED_REFCOUNT: u32 = 0x2;
    let mut module = std::ptr::null_mut();
    let marker = module_dir as *const u16;
    if unsafe { GetModuleHandleExW(FROM_ADDRESS | UNCHANGED_REFCOUNT, marker, &mut module) } == 0
        || module.is_null()
    {
        return None;
    }
    let mut buffer = [0u16; 32768];
    let written = unsafe { GetModuleFileNameW(module, buffer.as_mut_ptr(), buffer.len() as u32) };
    if written == 0 || written as usize >= buffer.len() {
        return None;
    }
    let path = PathBuf::from(String::from_utf16_lossy(&buffer[..written as usize]));
    path.parent().map(Path::to_path_buf)
}

fn write_status(dir: &Path, value: &'static str, last: &mut &'static str) {
    if *last == value {
        return;
    }
    unsafe extern "system" {
        fn MoveFileExW(existing: *const u16, new_name: *const u16, flags: u32) -> i32;
    }
    const REPLACE: u32 = 1;
    let path = dir.join("inactivity.status");
    let temporary = dir.join("inactivity.status.tmp");
    if std::fs::write(&temporary, format!("{value}\n")).is_err() {
        return;
    }
    let from = temporary.as_os_str().encode_wide().chain(std::iter::once(0)).collect::<Vec<_>>();
    let to = path.as_os_str().encode_wide().chain(std::iter::once(0)).collect::<Vec<_>>();
    if unsafe { MoveFileExW(from.as_ptr(), to.as_ptr(), REPLACE) } != 0 {
        *last = value;
    }
}

unsafe extern "system" fn any_key_icall_detour() -> u8 {
    if HOOK_ON.load(Ordering::Relaxed) {
        return 1;
    }
    let original = ICALL_ORIGINAL.load(Ordering::Acquire);
    if original.is_null() {
        return 0;
    }
    let original: IcallAnyKeyFn = unsafe { std::mem::transmute(original) };
    unsafe { original() }
}

unsafe extern "system" fn any_key_managed_detour(method_info: DnhHandle) -> u8 {
    if HOOK_ON.load(Ordering::Relaxed) {
        return 1;
    }
    let original = MANAGED_ORIGINAL.load(Ordering::Acquire);
    if original.is_null() {
        return 0;
    }
    let original: ManagedAnyKeyFn = unsafe { std::mem::transmute(original) };
    unsafe { original(method_info) }
}

fn hook_one(runtime: Runtime, address: *mut c_void, detour: *mut c_void) -> Option<*mut c_void> {
    if address.is_null() {
        return None;
    }
    let original = runtime.create_hook(address, detour)?;
    Some(original)
}

fn install(runtime: Runtime) -> bool {
    if !ICALL_TARGET.load(Ordering::Acquire).is_null()
        || !MANAGED_TARGET.load(Ordering::Acquire).is_null()
    {
        return true;
    }
    let class = runtime.class(
        c"UnityEngine.InputLegacyModule.dll",
        c"UnityEngine",
        c"Input",
    );
    if class.is_null() {
        return false;
    }
    let methods = runtime.methods_by_signature(
        class,
        Some(c"get_anyKey"),
        Some(c"System.Boolean"),
        &[],
        DNH_MEMBER_STORAGE_STATIC,
    );
    if methods.len() != 1 {
        return false;
    }
    let managed = unsafe { (runtime.v2().method_address)(methods[0]) };
    // Chaîne présente dans GameAssembly.dll, pas inventée. Le code IL2CPP généré
    // appelle cette icall plutôt que le wrapper managé.
    let icall = unsafe { (runtime.v2().resolve_icall)(c"UnityEngine.Input::get_anyKey()".as_ptr()) };
    let mut hooked = false;
    if !icall.is_null()
        && let Some(original) = hook_one(runtime, icall, any_key_icall_detour as *mut c_void)
    {
        ICALL_ORIGINAL.store(original, Ordering::Release);
        ICALL_TARGET.store(icall, Ordering::Release);
        hooked = true;
    }
    if !managed.is_null() && managed != icall
        && let Some(original) = hook_one(runtime, managed, any_key_managed_detour as *mut c_void)
    {
        MANAGED_ORIGINAL.store(original, Ordering::Release);
        MANAGED_TARGET.store(managed, Ordering::Release);
        hooked = true;
    }
    hooked
}

fn set_enabled(runtime: Runtime, enabled: bool) -> bool {
    HOOK_ON.store(enabled, Ordering::Release);
    let targets = [
        ICALL_TARGET.load(Ordering::Acquire),
        MANAGED_TARGET.load(Ordering::Acquire),
    ];
    let mut any = false;
    for target in targets {
        if target.is_null() {
            continue;
        }
        any = true;
        let ok = if enabled {
            runtime.enable_hook(target)
        } else {
            runtime.disable_hook(target)
        };
        if !ok {
            HOOK_ON.store(false, Ordering::Release);
            return false;
        }
    }
    any
}

pub fn maintain(runtime: Runtime) {
    let Ok(mut slot) = control().try_lock() else {
        return;
    };
    let now = Instant::now();
    if now < slot.next_read {
        return;
    }
    slot.next_read = now + Duration::from_millis(250);
    let dir = module_dir();
    let want = dir.as_deref().map(read_enabled).unwrap_or(true);
    if ICALL_TARGET.load(Ordering::Acquire).is_null()
        && MANAGED_TARGET.load(Ordering::Acquire).is_null()
        && !install(runtime)
    {
        if !slot.logged_missing {
            slot.logged_missing = true;
            runtime.log(
                DnhLogLevel::Info,
                "Item ID Overlay: UnityEngine.Input.get_anyKey pas encore resolu, nouvel essai.",
            );
        }
        if let Some(dir) = dir.as_deref() {
            write_status(dir, "unresolved", &mut slot.last_status);
        }
        return;
    }
    if !slot.announced_ready {
        slot.announced_ready = true;
        runtime.log(
            DnhLogLevel::Info,
            "Item ID Overlay: UnityEngine.Input.get_anyKey resolu (InputLegacyModule, icall UnityEngine.Input::get_anyKey()).",
        );
    }
    if slot.applied == Some(want) {
        return;
    }
    // Un crochet créé mais jamais activé laisse déjà l'icall d'origine en place.
    // MinHook refuse de désactiver un détour qui n'a pas été activé.
    let changed = if want || slot.applied == Some(true) {
        set_enabled(runtime, want)
    } else {
        HOOK_ON.store(false, Ordering::Release);
        true
    };
    if changed {
        slot.applied = Some(want);
        if let Some(dir) = dir.as_deref() {
            write_status(dir, if want { "hooked" } else { "restored" }, &mut slot.last_status);
        }
        runtime.log(
            DnhLogLevel::Info,
            if want {
                "Item ID Overlay: anti-inactivite client actif (get_anyKey renvoie vrai)."
            } else {
                "Item ID Overlay: anti-inactivite client retire, get_anyKey d'origine."
            },
        );
    }
}

pub fn shutdown(runtime: Runtime) {
    HOOK_ON.store(false, Ordering::Release);
    for target in [
        ICALL_TARGET.swap(std::ptr::null_mut(), Ordering::AcqRel),
        MANAGED_TARGET.swap(std::ptr::null_mut(), Ordering::AcqRel),
    ] {
        if target.is_null() {
            continue;
        }
        runtime.disable_hook(target);
        runtime.remove_hook(target);
    }
    ICALL_ORIGINAL.store(std::ptr::null_mut(), Ordering::Release);
    MANAGED_ORIGINAL.store(std::ptr::null_mut(), Ordering::Release);
    if let Ok(mut slot) = control().lock() {
        slot.applied = Some(false);
        if let Some(dir) = module_dir() {
            write_status(&dir, "restored", &mut slot.last_status);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::parse;

    #[test]
    fn defaults_on_unless_the_flag_is_false() {
        assert!(parse(""));
        assert!(parse("inactivity=true\n"));
        assert!(!parse("inactivity=false\n"));
        assert!(!parse("note=ignore\ninactivity\tfalse\n"));
        assert!(parse("inactivity=false\ninactivity=true\n"));
        assert!(parse("inactivity=yes\n"));
    }
}
