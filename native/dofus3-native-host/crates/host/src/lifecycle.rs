use super::*;
use dofus_native_mod_api::{DnmPrepareUnloadFn, PREPARE_UNLOAD_EXPORT};

static RETAINED: OnceLock<Mutex<Vec<Library>>> = OnceLock::new();

pub(super) fn retained_libraries() -> &'static Mutex<Vec<Library>> {
    RETAINED.get_or_init(|| Mutex::new(Vec::new()))
}

// One loader reference. No automatic FreeLibrary: destruction is fallible.
pub(super) struct Library {
    pub module: HModule,
    pub path: PathBuf,
    prepare: Option<DnmPrepareUnloadFn>,
    unload: Option<DnmUnloadFn>,
    pub active: bool,
    pub load_started: bool,
    prepared: bool,
    unloaded: bool,
}

// SAFETY: records move only under STATE/RETAINED locks. Export invocation is
// restricted to the initialization thread; moving a loader handle does not call it.
unsafe impl Send for Library {}

impl Library {
    pub fn new(module: HModule, path: &Path) -> Self {
        // SAFETY: module owns a live LoadLibrary reference; export signatures
        // are the native ABI contract, including the optional prepare function.
        let prepare = unsafe { export(module, PREPARE_UNLOAD_EXPORT) };
        // SAFETY: same live module and ABI contract.
        let unload = unsafe { export(module, UNLOAD_EXPORT) };
        Self { module, path: path.to_owned(), prepare, unload, active: false, load_started: false,
            prepared: false, unloaded: false }
    }

    pub fn teardown(&mut self) -> Result<(), String> {
        self.active = false;
        if !self.prepared {
            if let Some(prepare) = self.prepare {
                // SAFETY: owning reference remains live throughout this call.
                // Caller serializes lifecycle on the initialization thread. The
                // module must prevent foreign/reentrant entry before returning OK.
                let status = unsafe { prepare() };
                if status != DNH_OK {
                    return Err(format!("{}: DNM_PrepareUnload returned {status}; DLL retained", self.path.display()));
                }
            }
            self.prepared = true;
        }
        // Never release callback storage while a hook can still enter it.
        remove_hooks_for_module(self.module)?;
        if !self.unloaded {
            if let Some(unload) = self.unload.filter(|_| self.load_started) {
                // SAFETY: prepare acknowledged quiescence (or legacy module
                // assumes it); owned hooks are removed and DLL remains loaded.
                unsafe { unload() };
            }
            self.unloaded = true;
        }
        // A legacy unload may remove hooks itself; any remaining or newly
        // registered hook must still be removed successfully before release.
        remove_hooks_for_module(self.module)?;
        // SAFETY: successful prepare, hook removal and unload precede the only
        // release of this loader reference. On failure the record is retained.
        if unsafe { FreeLibrary(self.module) } == 0 {
            return Err(format!("FreeLibrary failed for {}; DLL retained", self.path.display()));
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::Cell;

    static TEST_LOCK: Mutex<()> = Mutex::new(());
    thread_local! {
        static PREPARE_STATUS: Cell<i32> = const { Cell::new(DNH_BUSY) };
        static UNLOADS: Cell<u32> = const { Cell::new(0) };
    }
    unsafe extern "system" fn prepare() -> i32 { PREPARE_STATUS.get() }
    unsafe extern "system" fn unload() { UNLOADS.set(UNLOADS.get() + 1); }

    fn library() -> Library {
        let path = Path::new("kernel32.dll");
        let wide = wide_null(path);
        // SAFETY: terminated path and supported flags; acquires our own reference.
        let module = unsafe { LoadLibraryExW(wide.as_ptr(), null_mut(), 0) };
        assert!(!module.is_null());
        let mut library = Library::new(module, path);
        library.prepare = Some(prepare);
        library.unload = Some(unload);
        library.load_started = true;
        library.active = true;
        UNLOADS.set(0);
        library
    }

    #[test]
    fn veto_retains_callback_state_until_explicit_successful_retry() {
        let _guard = TEST_LOCK.lock().unwrap();
        for status in [DNH_BUSY, DNH_ERROR, 42] {
            let mut library = library();
            PREPARE_STATUS.set(status);
            assert!(library.teardown().is_err());
            assert_eq!(UNLOADS.get(), 0);
            assert!(!library.active);
            PREPARE_STATUS.set(DNH_OK);
            assert!(library.teardown().is_ok());
            assert_eq!(UNLOADS.get(), 1);
        }
    }

    #[test]
    fn legacy_module_without_prepare_still_unloads() {
        let _guard = TEST_LOCK.lock().unwrap();
        let mut library = library();
        library.prepare = None;
        assert!(library.teardown().is_ok());
        assert_eq!(UNLOADS.get(), 1);
    }

    #[test]
    fn control_reload_preserves_busy_record_without_loading_replacement() {
        let _guard = TEST_LOCK.lock().unwrap();
        let library = library();
        let (worker_sender, worker_receiver) = channel();
        let mut state = HostState {
            unity_thread: unsafe { GetCurrentThreadId() }, shutting_down: false,
            mod_directory: PathBuf::new(), control_directory: PathBuf::new(),
            control_tick: 0, f1_down: false, disabled: BTreeSet::new(),
            mods: vec![LoadedMod { library, path: PathBuf::from("kernel32.dll"),
                abi_version: DNH_ABI_VERSION_8, id: "test".to_owned(), name: String::new(),
                version: String::new(), author: String::new(), tick: unload }],
            ui: None, worker_sender, worker_receiver, workers: Vec::new(),
        };
        PREPARE_STATUS.set(DNH_BUSY);
        let result = execute_control_command(&mut state, "RELOAD\ttest\tmissing.dll");
        assert!(result.unwrap_err().contains("DNM_PrepareUnload"));
        assert_eq!(state.mods.len(), 1);
        assert_eq!(UNLOADS.get(), 0);
        PREPARE_STATUS.set(DNH_OK);
        assert!(unload_mod_at(&mut state, 0).is_ok());
    }

    #[test]
    fn failed_hook_removal_keeps_registry_and_vetoes_module_unload() {
        let _guard = TEST_LOCK.lock().unwrap();
        let mut library = library();
        PREPARE_STATUS.set(DNH_OK);
        // A registered target absent from MinHook deterministically fails disable.
        // The detour address belongs to the library whose teardown is attempted.
        let detour = unsafe { GetProcAddress(library.module, c"GetCurrentThreadId".as_ptr()) };
        assert!(!detour.is_null());
        let mut owner = null_mut();
        assert_ne!(unsafe { GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, detour.cast(), &mut owner) }, 0);
        // Forwarded kernel32 exports can reside in KernelBase. Acquire that module.
        unsafe { FreeLibrary(library.module) };
        assert_ne!(unsafe { GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, detour.cast(), &mut library.module) }, 0);
        hooks().lock().unwrap().insert(1, detour as usize);
        let result = library.teardown();
        let retained = hooks().lock().unwrap().remove(&1);
        assert!(result.is_err());
        assert_eq!(retained, Some(detour as usize));
        assert_eq!(UNLOADS.get(), 0);
        assert!(library.teardown().is_ok());
    }
}

pub(super) fn retire_library(mut library: Library) -> Result<(), String> {
    match library.teardown() {
        Ok(()) => Ok(()),
        Err(error) => {
            // Preserve even a poisoned registry: it contains ownership records,
            // and forgetting one would falsely acknowledge host shutdown.
            retained_libraries().lock().unwrap_or_else(|e| e.into_inner()).push(library);
            set_error(&error);
            Err(error)
        }
    }
}
