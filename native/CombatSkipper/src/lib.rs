use dofus_native_mod_api::{
    DNH_ABI_VERSION_8, DNH_ERROR, DNH_MEMBER_STORAGE_INSTANCE, DNH_OK, DnhHandle, DnhHostApiV1,
    DnhLogLevel, DnhMethodInfoV2, DnhModInfoV1,
};
use dofus_native_mod_sdk::Runtime;
use std::collections::HashMap;
use std::ffi::{CStr, c_char, c_void};
use std::mem::size_of;
use std::ptr::{null, null_mut};
use std::sync::atomic::{AtomicBool, AtomicPtr, AtomicU64, AtomicUsize, Ordering};
use std::sync::{Mutex, OnceLock};

const FAST_FORWARD_SECONDS: f32 = 3600.0;
const FAST_FORWARD_ATTEMPTS: u16 = 1800;
// A long-running bot can create many Animator2D instances as fights and maps
// change. The hook must never retain an unbounded map of obsolete pointers.
const MAX_TRACKED_ANIMATIONS: usize = 8192;

type AnimationRunFn = unsafe extern "system" fn(DnhHandle, f32, DnhHandle);
type SetAnimationFn = unsafe extern "system" fn(DnhHandle, DnhHandle, u8, u8, i32, DnhHandle);
type ServiceEnabledSetterFn = unsafe extern "system" fn(DnhHandle, u8, DnhHandle);

#[derive(Clone, Copy)]
struct Capture {
    runtime: Runtime,
    input_checker_class: DnhHandle,
    fight_entities_class: DnhHandle,
    fight_state_field: DnhHandle,
}

#[derive(Clone, Copy)]
struct AnimationState {
    loops: bool,
    remaining_attempts: u16,
    reported: bool,
}

unsafe impl Send for Capture {}

static CAPTURE: OnceLock<Mutex<Option<Capture>>> = OnceLock::new();
static ANIMATIONS: OnceLock<Mutex<HashMap<usize, AnimationState>>> = OnceLock::new();
static IN_COMBAT: AtomicBool = AtomicBool::new(false);
static TICK_COUNT: AtomicUsize = AtomicUsize::new(0);
static TARGETED_ANIMATION_COUNT: AtomicUsize = AtomicUsize::new(0);
static LAST_REPORTED_COUNT: AtomicUsize = AtomicUsize::new(0);

static RUN_TARGET: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static RUN_ORIGINAL: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SET_ANIMATION_TARGET_A: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SET_ANIMATION_ORIGINAL_A: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SET_ANIMATION_TARGET_B: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SET_ANIMATION_ORIGINAL_B: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SERVICE_TARGET_A: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SERVICE_ORIGINAL_A: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SERVICE_TARGET_B: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static SERVICE_ORIGINAL_B: AtomicPtr<c_void> = AtomicPtr::new(null_mut());

fn capture() -> &'static Mutex<Option<Capture>> {
    CAPTURE.get_or_init(|| Mutex::new(None))
}

fn animations() -> &'static Mutex<HashMap<usize, AnimationState>> {
    ANIMATIONS.get_or_init(|| Mutex::new(HashMap::new()))
}

unsafe extern "system" fn animation_run_detour(
    this: DnhHandle,
    current_time: f32,
    method_info: DnhHandle,
) {
    let original = RUN_ORIGINAL.load(Ordering::Acquire);
    if original.is_null() {
        return;
    }
    let original: AnimationRunFn = unsafe { std::mem::transmute(original) };
    let should_finish = if IN_COMBAT.load(Ordering::Acquire) && !this.is_null() {
        if let Ok(mut animations) = animations().lock()
            && let Some(state) = animations.get_mut(&(this as usize))
        {
            if state.loops || state.remaining_attempts == 0 {
                false
            } else {
                state.remaining_attempts -= 1;
                if !state.reported {
                    state.reported = true;
                    TARGETED_ANIMATION_COUNT.fetch_add(1, Ordering::Relaxed);
                }
                true
            }
        } else {
            false
        }
    } else {
        false
    };
    let effective_time = if should_finish && current_time.is_finite() {
        current_time + FAST_FORWARD_SECONDS
    } else {
        current_time
    };
    unsafe { original(this, effective_time, method_info) };
}

fn track_animation(known: &mut HashMap<usize, AnimationState>, key: usize, loops: bool) {
    if known.len() >= MAX_TRACKED_ANIMATIONS && !known.contains_key(&key) {
        known.clear();
    }
    known.insert(
        key,
        AnimationState {
            loops,
            remaining_attempts: if loops { 0 } else { FAST_FORWARD_ATTEMPTS },
            reported: false,
        },
    );
}

fn record_animation_state(this: DnhHandle, loops: u8) {
    if !this.is_null()
        && let Ok(mut animations) = animations().lock()
    {
        track_animation(&mut animations, this as usize, loops != 0);
    }
}

unsafe extern "system" fn set_animation_detour_a(
    this: DnhHandle,
    animation_name: DnhHandle,
    loops: u8,
    restart: u8,
    frame: i32,
    method_info: DnhHandle,
) {
    let original = SET_ANIMATION_ORIGINAL_A.load(Ordering::Acquire);
    if original.is_null() {
        return;
    }
    record_animation_state(this, loops);
    let original: SetAnimationFn = unsafe { std::mem::transmute(original) };
    unsafe { original(this, animation_name, loops, restart, frame, method_info) };
}

unsafe extern "system" fn set_animation_detour_b(
    this: DnhHandle,
    animation_name: DnhHandle,
    loops: u8,
    restart: u8,
    frame: i32,
    method_info: DnhHandle,
) {
    let original = SET_ANIMATION_ORIGINAL_B.load(Ordering::Acquire);
    if original.is_null() {
        return;
    }
    record_animation_state(this, loops);
    let original: SetAnimationFn = unsafe { std::mem::transmute(original) };
    unsafe { original(this, animation_name, loops, restart, frame, method_info) };
}

fn record_service_state(this: DnhHandle, enabled: u8) {
    let state = capture().lock().ok().and_then(|state| *state);
    if let Some(state) = state
        && state.runtime.object_is(this, state.fight_entities_class)
    {
        let active = enabled != 0;
        let previous = IN_COMBAT.swap(active, Ordering::AcqRel);
        if previous != active {
            state.runtime.log(
                DnhLogLevel::Info,
                if active {
                    "Combat Animation Skipper: fight service enabled; action animations now finish instantly."
                } else {
                    "Combat Animation Skipper: fight service disabled; normal animation timing restored."
                },
            );
        }
    }
}

unsafe extern "system" fn service_enabled_detour_a(
    this: DnhHandle,
    enabled: u8,
    method_info: DnhHandle,
) {
    let original = SERVICE_ORIGINAL_A.load(Ordering::Acquire);
    if original.is_null() {
        return;
    }
    let original: ServiceEnabledSetterFn = unsafe { std::mem::transmute(original) };
    unsafe { original(this, enabled, method_info) };
    record_service_state(this, enabled);
}

unsafe extern "system" fn service_enabled_detour_b(
    this: DnhHandle,
    enabled: u8,
    method_info: DnhHandle,
) {
    let original = SERVICE_ORIGINAL_B.load(Ordering::Acquire);
    if original.is_null() {
        return;
    }
    let original: ServiceEnabledSetterFn = unsafe { std::mem::transmute(original) };
    unsafe { original(this, enabled, method_info) };
    record_service_state(this, enabled);
}

fn method_address(runtime: Runtime, method: DnhHandle, label: &str) -> Option<DnhHandle> {
    let address = unsafe { (runtime.v2().method_address)(method) };
    if address.is_null() {
        runtime.log(
            DnhLogLevel::Error,
            &format!("Combat Animation Skipper: {label} has no native address."),
        );
        None
    } else {
        Some(address)
    }
}

fn log_string_method_candidates(runtime: Runtime, class: DnhHandle) {
    let count = unsafe { (runtime.v2().get_class_methods)(class, null_mut(), 0) };
    let mut methods = vec![null_mut(); count];
    if count != 0 {
        unsafe {
            (runtime.v2().get_class_methods)(class, methods.as_mut_ptr(), methods.len());
        }
    }
    for method in methods {
        let mut info = DnhMethodInfoV2 {
            struct_size: size_of::<DnhMethodInfoV2>() as u32,
            name: null(),
            return_type_name: null(),
            parameter_count: 0,
            flags: 0,
            is_static: 0,
            reserved: [0; 3],
        };
        if !unsafe { (runtime.v2().copy_method_info)(method, &mut info) }
            || info.is_static != 0
            || info.parameter_count == 0
            || info.parameter_count > 6
        {
            continue;
        }
        let first = unsafe { (runtime.v2().method_parameter_type_name)(method, 0) };
        if first.is_null() || unsafe { CStr::from_ptr(first) } != c"System.String" {
            continue;
        }
        let parameters = (0..info.parameter_count)
            .map(|index| {
                let parameter = unsafe { (runtime.v2().method_parameter_type_name)(method, index) };
                if parameter.is_null() {
                    "<unknown>".to_owned()
                } else {
                    unsafe { CStr::from_ptr(parameter) }
                        .to_string_lossy()
                        .into_owned()
                }
            })
            .collect::<Vec<_>>()
            .join(", ");
        let return_type = if info.return_type_name.is_null() {
            "<unknown>".to_owned()
        } else {
            unsafe { CStr::from_ptr(info.return_type_name) }
                .to_string_lossy()
                .into_owned()
        };
        runtime.log(
            DnhLogLevel::Info,
            &format!(
                "Combat Animation Skipper candidate: {}({parameters}) -> {return_type}.",
                runtime.method_name(method)
            ),
        );
    }
}

fn deduplicate_by_address(runtime: Runtime, methods: Vec<DnhHandle>) -> Vec<DnhHandle> {
    let mut unique = Vec::new();
    let mut addresses = Vec::new();
    for method in methods {
        let address = unsafe { (runtime.v2().method_address)(method) };
        if address.is_null() || addresses.contains(&address) {
            continue;
        }
        addresses.push(address);
        unique.push(method);
    }
    unique
}

fn find_service_state_bindings(
    runtime: Runtime,
    fight_class: DnhHandle,
) -> (Vec<DnhHandle>, DnhHandle) {
    let mut class = runtime.class_parent(fight_class);
    let mut selected = Vec::new();
    let mut selected_class = null_mut();
    let mut selected_field = null_mut();
    for _ in 0..16 {
        if class.is_null() {
            break;
        }
        let boolean_fields = runtime.fields_by_type(class, c"System.Boolean");
        let setters = deduplicate_by_address(
            runtime,
            runtime
                .methods_by_signature(
                    class,
                    None,
                    Some(c"System.Void"),
                    &[c"System.Boolean"],
                    DNH_MEMBER_STORAGE_INSTANCE,
                )
                .into_iter()
                .filter(|method| runtime.method_name(*method) != ".ctor")
                .collect(),
        );
        if boolean_fields.len() == 1 && !setters.is_empty() {
            selected = setters;
            selected_class = class;
            selected_field = boolean_fields[0];
        }
        class = runtime.class_parent(class);
    }
    if selected.len() > 2 {
        runtime.log(
            DnhLogLevel::Error,
            &format!(
                "Combat Animation Skipper: {} lifecycle setter candidates; refusing ambiguous hooks.",
                selected.len()
            ),
        );
        return (Vec::new(), null_mut());
    }
    if !selected.is_empty() {
        runtime.log(
            DnhLogLevel::Info,
            &format!(
                "Combat Animation Skipper: {} unique fight lifecycle setter(s) resolved in {}.",
                selected.len(),
                runtime.class_name(selected_class)
            ),
        );
    }
    (selected, selected_field)
}

fn create_and_enable(
    runtime: Runtime,
    target: DnhHandle,
    detour: DnhHandle,
    target_slot: &AtomicPtr<c_void>,
    original_slot: &AtomicPtr<c_void>,
    label: &str,
) -> bool {
    let Some(original) = runtime.create_hook(target, detour) else {
        runtime.log(
            DnhLogLevel::Error,
            &format!("Combat Animation Skipper: failed to create {label} hook."),
        );
        return false;
    };
    original_slot.store(original, Ordering::Release);
    target_slot.store(target, Ordering::Release);
    if runtime.enable_hook(target) {
        true
    } else {
        runtime.remove_hook(target);
        original_slot.store(null_mut(), Ordering::Release);
        target_slot.store(null_mut(), Ordering::Release);
        runtime.log(
            DnhLogLevel::Error,
            &format!("Combat Animation Skipper: failed to enable {label} hook."),
        );
        false
    }
}

fn remove_hook(
    runtime: Runtime,
    target_slot: &AtomicPtr<c_void>,
    original_slot: &AtomicPtr<c_void>,
) {
    let target = target_slot.swap(null_mut(), Ordering::AcqRel);
    if !target.is_null() {
        runtime.disable_hook(target);
        runtime.remove_hook(target);
    }
    original_slot.store(null_mut(), Ordering::Release);
}

fn remove_all_hooks(runtime: Runtime) {
    remove_hook(runtime, &RUN_TARGET, &RUN_ORIGINAL);
    remove_hook(runtime, &SET_ANIMATION_TARGET_A, &SET_ANIMATION_ORIGINAL_A);
    remove_hook(runtime, &SET_ANIMATION_TARGET_B, &SET_ANIMATION_ORIGINAL_B);
    remove_hook(runtime, &SERVICE_TARGET_A, &SERVICE_ORIGINAL_A);
    remove_hook(runtime, &SERVICE_TARGET_B, &SERVICE_ORIGINAL_B);
}

// InputChecker n'existe que pendant MON tour : l'etat « combat » est maintenu HOLD_MS apres la derniere observation active, sinon
// les tours des autres combattants (animations des monstres) repassaient en vitesse normale (constate en jeu sur 3.7.11.11).
static LAST_ACTIVE_MS: AtomicU64 = AtomicU64::new(0);
const HOLD_MS: u64 = 9000;

fn now_ms() -> u64 {
    static ORIGIN: OnceLock<std::time::Instant> = OnceLock::new();
    ORIGIN.get_or_init(std::time::Instant::now).elapsed().as_millis() as u64 + 1
}

fn is_combat_active(input_checker_active: bool, service_active: Option<bool>) -> bool {
    let now = now_ms();
    if input_checker_active || service_active.unwrap_or(false) {
        LAST_ACTIVE_MS.store(now, Ordering::Release);
        return true;
    }
    let last = LAST_ACTIVE_MS.load(Ordering::Acquire);
    last != 0 && now.saturating_sub(last) < HOLD_MS
}

fn update_combat_state(state: Capture) {
    let input_checker_active =
        unsafe { (state.runtime.v2().find_objects)(state.input_checker_class, null_mut(), 0) } != 0;
    let mut service_active = None;
    let services = if state.fight_entities_class.is_null() {
        Vec::new()
    } else {
        state.runtime.find_objects(state.fight_entities_class)
    };
    for service in services {
        let mut enabled = 0_u8;
        if !service.is_null()
            && unsafe {
                (state.runtime.v2().field_get_value)(
                    service,
                    state.fight_state_field,
                    (&mut enabled as *mut u8).cast(),
                )
            }
        {
            let active = enabled != 0;
            service_active = Some(service_active.unwrap_or(false) || active);
        }
    }
    // Even if the optional fight service exists but its state cannot be read, the
    // InputChecker witness must be allowed to restore normal timing after combat.
    // Otherwise an unreadable service leaves IN_COMBAT permanently stuck at true.
    let active = is_combat_active(input_checker_active, service_active);
    let previous = IN_COMBAT.swap(active, Ordering::AcqRel);
    if previous != active {
        if !active {
            // Object addresses may be recycled by Unity between combat scenes.
            // No animation state is carried into the next fight.
            if let Ok(mut known) = animations().lock() {
                known.clear();
            }
        }
        state.runtime.log(
            DnhLogLevel::Info,
            if active {
                "Combat Animation Skipper: combat transition ON; non-looping action animations accelerated."
            } else {
                "Combat Animation Skipper: combat transition OFF; normal animation timing restored."
            },
        );
    }
}

fn load(runtime: Runtime) -> Option<()> {
    let animator = runtime.class(
        c"Ankama.Animator2D.dll",
        c"Ankama.Animations",
        c"Animator2D",
    );
    let input_checker = runtime.class(c"Core.dll", c"Core.Services.Fight", c"InputChecker");
    let fight_entities = runtime.class(
        c"Core.dll",
        c"Core.Services.Fight.FightEntitiesService",
        c"FightEntitiesService",
    );
    if animator.is_null() || input_checker.is_null() {
        runtime.log(
            DnhLogLevel::Error,
            "Combat Animation Skipper: Animator2D or InputChecker is unavailable.",
        );
        return None;
    }
    if fight_entities.is_null() {
        runtime.log(
            DnhLogLevel::Warn,
            "Combat Animation Skipper (Givre): FightEntitiesService absent de ce client, detection du combat par InputChecker seule.",
        );
    }

    let run = runtime.unique_method_by_signature(
        animator,
        None,
        Some(c"System.Void"),
        &[c"System.Single"],
        DNH_MEMBER_STORAGE_INSTANCE,
        "Animator2D.Run(float)",
    )?;
    let set_animation_candidates = deduplicate_by_address(
        runtime,
        runtime.methods_by_signature(
            animator,
            None,
            Some(c"System.Void"),
            &[
                c"System.String",
                c"System.Boolean",
                c"System.Boolean",
                c"System.Int32",
            ],
            DNH_MEMBER_STORAGE_INSTANCE,
        ),
    );
    if set_animation_candidates.is_empty() {
        runtime.log(
            DnhLogLevel::Error,
            "Animator2D.SetAnimation(string,bool,bool,int): no native address was resolved.",
        );
        log_string_method_candidates(runtime, animator);
        return None;
    }
    if set_animation_candidates.len() > 2 {
        runtime.log(
            DnhLogLevel::Error,
            &format!(
                "Combat Animation Skipper: {} animation selection entries resolved; refusing ambiguous hooks.",
                set_animation_candidates.len()
            ),
        );
        return None;
    }
    let run_target = method_address(runtime, run, "Animator2D.Run")?;
    let set_animation_target_a = method_address(
        runtime,
        set_animation_candidates[0],
        "Animator2D.SetAnimation A",
    )?;
    let set_animation_target_b = set_animation_candidates
        .get(1)
        .and_then(|method| method_address(runtime, *method, "Animator2D.SetAnimation B"));
    let (service_methods, fight_state_field) = if fight_entities.is_null() {
        (Vec::new(), null_mut())
    } else {
        find_service_state_bindings(runtime, fight_entities)
    };
    if !fight_entities.is_null() && (service_methods.is_empty() || fight_state_field.is_null()) {
        runtime.log(
            DnhLogLevel::Error,
            "Combat Animation Skipper: no fight lifecycle setter could be resolved.",
        );
        return None;
    }
    let service_target_a = match service_methods.first() {
        Some(method) => Some(method_address(
            runtime,
            *method,
            "fight lifecycle setter A",
        )?),
        None => None,
    };
    let service_target_b = service_methods
        .get(1)
        .and_then(|method| method_address(runtime, *method, "fight lifecycle setter B"));

    if let Ok(mut state) = capture().lock() {
        *state = Some(Capture {
            runtime,
            input_checker_class: input_checker,
            fight_entities_class: fight_entities,
            fight_state_field,
        });
    } else {
        return None;
    }
    IN_COMBAT.store(false, Ordering::Release);
    TICK_COUNT.store(0, Ordering::Release);
    TARGETED_ANIMATION_COUNT.store(0, Ordering::Release);
    LAST_REPORTED_COUNT.store(0, Ordering::Release);

    if service_target_a.is_some_and(|target| {
        !create_and_enable(
            runtime,
            target,
            service_enabled_detour_a as *const () as DnhHandle,
            &SERVICE_TARGET_A,
            &SERVICE_ORIGINAL_A,
            "fight lifecycle A",
        )
    }) || service_target_b.is_some_and(|target| {
        !create_and_enable(
            runtime,
            target,
            service_enabled_detour_b as *const () as DnhHandle,
            &SERVICE_TARGET_B,
            &SERVICE_ORIGINAL_B,
            "fight lifecycle B",
        )
    }) || !create_and_enable(
        runtime,
        set_animation_target_a,
        set_animation_detour_a as *const () as DnhHandle,
        &SET_ANIMATION_TARGET_A,
        &SET_ANIMATION_ORIGINAL_A,
        "animation selection A",
    ) || set_animation_target_b.is_some_and(|target| {
        !create_and_enable(
            runtime,
            target,
            set_animation_detour_b as *const () as DnhHandle,
            &SET_ANIMATION_TARGET_B,
            &SET_ANIMATION_ORIGINAL_B,
            "animation selection B",
        )
    }) || !create_and_enable(
        runtime,
        run_target,
        animation_run_detour as *const () as DnhHandle,
        &RUN_TARGET,
        &RUN_ORIGINAL,
        "animation update",
    ) {
        remove_all_hooks(runtime);
        if let Ok(mut state) = capture().lock() {
            *state = None;
        }
        return None;
    }

    update_combat_state(Capture {
        runtime,
        input_checker_class: input_checker,
        fight_entities_class: fight_entities,
        fight_state_field,
    });
    runtime.log(
        DnhLogLevel::Info,
        "Combat Animation Skipper ready: dual fight detection and Animator2D hooks installed.",
    );
    Some(())
}

static MOD_ID: &[u8] = b"bubble.dofus3.combat-animation-skipper\0";
static MOD_NAME: &[u8] = b"Dofus Combat Animation Skipper\0";
static MOD_VERSION: &[u8] = b"1.0.8-givre\0";
static MOD_AUTHOR: &[u8] = b"Bubble\0";

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
    if host_abi_version == DNH_ABI_VERSION_8 {
        &MOD_INFO.0
    } else {
        null()
    }
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `host_api` must be the process-lifetime ABI v8 table supplied by the host.
pub unsafe extern "system" fn DNM_Load(host_api: *const DnhHostApiV1) -> i32 {
    let Some(runtime) = (unsafe { Runtime::bind(host_api) }) else {
        return DNH_ERROR;
    };
    if load(runtime).is_some() {
        DNH_OK
    } else {
        DNH_ERROR
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Tick() {
    let tick = TICK_COUNT.fetch_add(1, Ordering::Relaxed);
    let state = capture().lock().ok().and_then(|state| *state);
    if tick.is_multiple_of(15)
        && let Some(state) = state
    {
        update_combat_state(state);
    }
    if tick.is_multiple_of(60)
        && let Some(state) = state
    {
        let total = TARGETED_ANIMATION_COUNT.load(Ordering::Acquire);
        let previous = LAST_REPORTED_COUNT.swap(total, Ordering::AcqRel);
        if total > previous {
            state.runtime.log(
                DnhLogLevel::Info,
                &format!(
                    "Combat Animation Skipper: {} non-looping Animator2D animation(s) continuously fast-forwarded (total {}).",
                    total - previous,
                    total
                ),
            );
        }
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Unload() {
    let state = capture().lock().ok().and_then(|mut state| state.take());
    if let Some(state) = state {
        remove_all_hooks(state.runtime);
    }
    IN_COMBAT.store(false, Ordering::Release);
    TICK_COUNT.store(0, Ordering::Release);
    TARGETED_ANIMATION_COUNT.store(0, Ordering::Release);
    LAST_REPORTED_COUNT.store(0, Ordering::Release);
    if let Ok(mut animations) = animations().lock() {
        animations.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::{FAST_FORWARD_ATTEMPTS, LAST_ACTIVE_MS, MAX_TRACKED_ANIMATIONS, is_combat_active, track_animation};
    use std::collections::HashMap;
    use std::sync::Mutex;
    use std::sync::atomic::Ordering;

    // L'etat « combat » tenu est global : les tests qui le touchent s'executent un par un.
    static SERIAL: Mutex<()> = Mutex::new(());

    #[test]
    fn input_checker_witness_is_held_between_my_turns() {
        let _g = SERIAL.lock().unwrap_or_else(|e| e.into_inner());
        LAST_ACTIVE_MS.store(0, Ordering::Release);
        assert!(!is_combat_active(false, None));
        assert!(is_combat_active(true, None));
        // InputChecker disparait pendant les tours des autres : l'etat combat est maintenu (HOLD_MS).
        assert!(is_combat_active(false, None));
    }

    #[test]
    fn either_authoritative_fight_witness_can_keep_combat_active() {
        let _g = SERIAL.lock().unwrap_or_else(|e| e.into_inner());
        assert!(is_combat_active(true, Some(false)));
        assert!(is_combat_active(false, Some(true)));
    }

    #[test]
    fn animation_cache_is_bounded_for_long_running_sessions() {
        let mut known = HashMap::new();
        for address in 1..=MAX_TRACKED_ANIMATIONS {
            track_animation(&mut known, address, false);
        }
        assert_eq!(known.len(), MAX_TRACKED_ANIMATIONS);
        track_animation(&mut known, MAX_TRACKED_ANIMATIONS + 1, true);
        assert_eq!(known.len(), 1);
        assert!(known[&(MAX_TRACKED_ANIMATIONS + 1)].loops);
        track_animation(&mut known, MAX_TRACKED_ANIMATIONS + 1, false);
        assert_eq!(known.len(), 1);
        assert_eq!(
            known[&(MAX_TRACKED_ANIMATIONS + 1)].remaining_attempts,
            FAST_FORWARD_ATTEMPTS
        );
    }
}
