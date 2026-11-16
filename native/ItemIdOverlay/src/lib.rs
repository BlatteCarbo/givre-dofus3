//! Givre Item ID Overlay : ajoute « #<gid> » a droite du nom d'un objet dans l'interface (UI Toolkit).
//!
//! Principe : aucun balayage de l'interface (trop couteux). On intercepte `TextElement.set_text` ; quand le texte ecrit est
//! EXACTEMENT un nom d'objet connu (table embarquee items.tsv, nom -> identifiants), on ecrit a la place « Nom  #id ».
//! Tout est local et purement visuel : aucune requete reseau, aucun message envoye au serveur.

mod inactivity;
mod table;
mod settings;

use dofus_native_mod_api::{
    DNH_ABI_VERSION_8, DNH_ERROR, DNH_OK, DnhHandle, DnhHostApiV1, DnhLogLevel, DnhModInfoV1,
};
use dofus_native_mod_sdk::Runtime;
use std::ffi::{c_char, c_void};
use std::mem::size_of;
use std::ptr::{null, null_mut};
use std::sync::atomic::{AtomicPtr, AtomicU64, Ordering};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

type SetTextFn = unsafe extern "system" fn(DnhHandle, DnhHandle, DnhHandle);

static TARGET: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static ORIGINAL: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static CALLS: AtomicU64 = AtomicU64::new(0);
static MATCHED: AtomicU64 = AtomicU64::new(0);

struct State {
    runtime: Runtime,
    settings: settings::Settings,
    last_report: Instant,
    last_calls: u64,
}

unsafe impl Send for State {}

static STATE: OnceLock<Mutex<Option<State>>> = OnceLock::new();

fn state() -> &'static Mutex<Option<State>> {
    STATE.get_or_init(|| Mutex::new(None))
}

/// Remplace le texte ecrit si c'est un nom d'objet connu ; sinon laisse passer tel quel.
unsafe extern "system" fn set_text_detour(this: DnhHandle, value: DnhHandle, method_info: DnhHandle) {
    let original = ORIGINAL.load(Ordering::Acquire);
    if original.is_null() {
        return;
    }
    let original: SetTextFn = unsafe { std::mem::transmute(original) };
    CALLS.fetch_add(1, Ordering::Relaxed);
    let mut replacement = null_mut();
    if !value.is_null()
        && let Ok(mut guard) = state().try_lock()
        && let Some(st) = guard.as_mut()
        && let Some(text) = st.runtime.managed_string(value)
    {
        // TextElement.set_text also receives account names, chat messages and
        // other private UI text. Never journal their values, even in debug.
        let bare = table::strip_mark(text.trim());
        if st.settings.enabled && bare.len() <= 120 && !table::has_mark(&text) {
            let ids = table::ids_by_name(bare);
            if !ids.is_empty() {
                replacement = st.runtime.string(&table::with_format(bare, &ids, st.settings.format));
                MATCHED.fetch_add(1, Ordering::Relaxed);
            }
        }
    }
    unsafe { original(this, if replacement.is_null() { value } else { replacement }, method_info) };
}

fn install(runtime: Runtime) -> Option<()> {
    let ui = c"UnityEngine.UIElementsModule.dll";
    let text_element = runtime.class(ui, c"UnityEngine.UIElements", c"TextElement");
    if text_element.is_null() {
        runtime.log(DnhLogLevel::Error, "Item ID Overlay: TextElement introuvable.");
        return None;
    }
    let method = runtime.instance_method(text_element, c"set_text", &[c"System.String"])?;
    let address = unsafe { (runtime.v2().method_address)(method) };
    if address.is_null() {
        runtime.log(DnhLogLevel::Error, "Item ID Overlay: set_text sans adresse native.");
        return None;
    }
    let Some(original) = runtime.create_hook(address, set_text_detour as *const () as DnhHandle) else {
        runtime.log(DnhLogLevel::Error, "Item ID Overlay: creation du hook set_text impossible.");
        return None;
    };
    ORIGINAL.store(original, Ordering::Release);
    TARGET.store(address, Ordering::Release);
    if !runtime.enable_hook(address) {
        runtime.remove_hook(address);
        ORIGINAL.store(null_mut(), Ordering::Release);
        TARGET.store(null_mut(), Ordering::Release);
        runtime.log(DnhLogLevel::Error, "Item ID Overlay: activation du hook set_text impossible.");
        return None;
    }
    Some(())
}

static MOD_ID: &[u8] = b"givre.dofus3.item-id-overlay\0";
static MOD_NAME: &[u8] = b"Givre Item ID Overlay\0";
static MOD_VERSION: &[u8] = b"0.4.1\0";
static MOD_AUTHOR: &[u8] = b"Givre\0";

struct SyncModInfo(DnhModInfoV1);
unsafe impl Sync for SyncModInfo {}

static MOD_INFO: SyncModInfo = SyncModInfo(DnhModInfoV1 {
    abi_version: DNH_ABI_VERSION_8,
    struct_size: size_of::<DnhModInfoV1>() as u32,
    id: MOD_ID.as_ptr().cast::<c_char>(),
    name: MOD_NAME.as_ptr().cast::<c_char>(),
    version: MOD_VERSION.as_ptr().cast::<c_char>(),
    author: MOD_AUTHOR.as_ptr().cast::<c_char>(),
});

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Query(host_abi_version: u32) -> *const DnhModInfoV1 {
    if host_abi_version == DNH_ABI_VERSION_8 { &MOD_INFO.0 } else { null() }
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `host_api` doit etre la table ABI v8 fournie par le host natif, valide pour toute la vie du mod.
pub unsafe extern "system" fn DNM_Load(host_api: *const DnhHostApiV1) -> i32 {
    let Some(runtime) = (unsafe { Runtime::bind(host_api) }) else {
        return DNH_ERROR;
    };
    if let Ok(mut guard) = state().lock() {
        *guard = Some(State { runtime, settings: settings::Settings::load(), last_report: Instant::now(), last_calls: 0 });
    }
    if install(runtime).is_none() {
        if let Ok(mut guard) = state().lock() {
            *guard = None;
        }
        return DNH_ERROR;
    }
    inactivity::maintain(runtime);
    runtime.log(DnhLogLevel::Info, "Item ID Overlay 0.4.1 charge : hook TextElement.set_text actif, get_anyKey selon inactivity.ini, valeurs privees jamais journalisees.");
    DNH_OK
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Tick() {
    let runtime = {
        let Ok(guard) = state().try_lock() else { return };
        let Some(st) = guard.as_ref() else { return };
        st.runtime
    };
    inactivity::maintain(runtime);
    let Ok(mut guard) = state().try_lock() else { return };
    let Some(st) = guard.as_mut() else { return };
    if st.last_report.elapsed() < Duration::from_secs(15) {
        return;
    }
    st.last_report = Instant::now();
    let calls = CALLS.load(Ordering::Relaxed);
    if calls != st.last_calls {
        st.last_calls = calls;
        st.runtime.log(
            DnhLogLevel::Info,
            &format!("Item ID Overlay: {calls} ecritures de texte vues, {} noms d'objet completes.", MATCHED.load(Ordering::Relaxed)),
        );
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Unload() {
    let target = TARGET.swap(null_mut(), Ordering::AcqRel);
    let runtime = state().lock().ok().and_then(|mut g| g.take()).map(|s| s.runtime);
    if let Some(runtime) = runtime {
        inactivity::shutdown(runtime);
        if !target.is_null() {
            runtime.disable_hook(target);
            runtime.remove_hook(target);
        }
        runtime.log(DnhLogLevel::Info, "Item ID Overlay decharge.");
    }
    ORIGINAL.store(null_mut(), Ordering::Release);
}
