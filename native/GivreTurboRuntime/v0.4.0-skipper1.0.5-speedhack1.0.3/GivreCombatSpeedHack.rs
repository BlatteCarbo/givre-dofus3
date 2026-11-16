use dofus_native_mod_api::{
    DNH_ABI_VERSION_8, DNH_ERROR, DNH_MEMBER_STORAGE_STATIC, DNH_OK, DnhHandle, DnhHostApiV1,
    DnhLogLevel, DnhModInfoV1,
};
use dofus_native_mod_sdk::Runtime;
use std::ffi::{c_char, c_void};
use std::mem::size_of;
use std::path::PathBuf;
use std::ptr::{null, null_mut};
use std::sync::{Mutex, OnceLock};
use std::time::Duration;

const COMBAT_MULTIPLIER: f32 = 8.0;
const MAX_TIME_SCALE: f32 = 20.0;
const PROBE_FRAME_INTERVAL: usize = 5;
const LEASE_MAX_AGE: Duration = Duration::from_secs(2);

type TimeScaleIcall = unsafe extern "system" fn(f32);

#[derive(Clone, Copy)]
struct Capture {
    runtime: Runtime,
    input_checker_class: DnhHandle,
    get_time_scale: DnhHandle,
    set_time_scale: DnhHandle,
    set_time_scale_icall: DnhHandle,
}

#[derive(Default)]
struct SpeedState {
    baseline: Option<f32>,
    applied: bool,
    tick: usize,
    last_restored_token: Option<String>,
}

unsafe impl Send for Capture {}

static CAPTURE: OnceLock<Mutex<Option<Capture>>> = OnceLock::new();
static SPEED_STATE: OnceLock<Mutex<SpeedState>> = OnceLock::new();

fn capture() -> &'static Mutex<Option<Capture>> {
    CAPTURE.get_or_init(|| Mutex::new(None))
}

fn speed_state() -> &'static Mutex<SpeedState> {
    SPEED_STATE.get_or_init(|| Mutex::new(SpeedState::default()))
}

fn stage_root() -> Option<PathBuf> {
    let local_app_data = std::env::var_os("LOCALAPPDATA")?;
    Some(
        PathBuf::from(local_app_data)
            .join("GivreClone")
            .join("NativeTurbo")
            .join(std::process::id().to_string()),
    )
}

fn lease_path() -> Option<PathBuf> {
    Some(stage_root()?.join("combat-speedhack.lease"))
}

fn restored_ack_path() -> Option<PathBuf> {
    Some(stage_root()?.join("combat-speedhack.restored"))
}

fn restore_request_path() -> Option<PathBuf> {
    Some(stage_root()?.join("combat-speedhack.restore-request"))
}

fn lease_is_fresh() -> bool {
    let Some(path) = lease_path() else {
        return false;
    };
    let Ok(modified) = std::fs::metadata(path).and_then(|metadata| metadata.modified()) else {
        return false;
    };
    modified.elapsed().is_ok_and(|age| age <= LEASE_MAX_AGE)
}

fn restore_request_token() -> Option<String> {
    let value = std::fs::read_to_string(restore_request_path()?).ok()?;
    let token = value.trim();
    (!token.is_empty()).then(|| token.to_owned())
}

fn acknowledge_restored(speed: &mut SpeedState) {
    let Some(token) = restore_request_token() else {
        return;
    };
    let Some(path) = restored_ack_path() else {
        return;
    };
    if speed.last_restored_token.as_deref() == Some(token.as_str()) && path.exists() {
        return;
    }
    if std::fs::write(path, token.as_bytes()).is_ok() {
        speed.last_restored_token = Some(token);
    }
}

fn read_time_scale(state: Capture) -> Option<f32> {
    let boxed = state
        .runtime
        .invoke(state.get_time_scale, null_mut(), &[])?;
    state.runtime.unbox::<f32>(boxed)
}

fn write_time_scale(state: Capture, value: f32) -> bool {
    if !value.is_finite() || value < 0.0 {
        return false;
    }
    let mut argument = value;
    let argument_handle = (&mut argument as *mut f32).cast::<c_void>();
    state
        .runtime
        .invoke(state.set_time_scale, null_mut(), &[argument_handle])
        .is_some()
}

fn write_time_scale_direct(state: Capture, value: f32) -> bool {
    if !value.is_finite() || value < 0.0 || state.set_time_scale_icall.is_null() {
        return false;
    }
    let setter: TimeScaleIcall = unsafe { std::mem::transmute(state.set_time_scale_icall) };
    unsafe { setter(value) };
    true
}

fn fight_is_active(state: Capture) -> bool {
    // Certified visible-client witness. Avoid heuristic service/base-class booleans:
    // a false positive here would accelerate the whole Unity client outside combat.
    (unsafe { (state.runtime.v2().find_objects)(state.input_checker_class, null_mut(), 0) }) != 0
}

fn restore_baseline(state: Capture, speed: &mut SpeedState, reason: &str) -> bool {
    if !speed.applied {
        speed.baseline = None;
        return true;
    }
    let Some(baseline) = speed.baseline else {
        speed.applied = false;
        return true;
    };
    let mut restored = false;
    for _ in 0..8 {
        if write_time_scale(state, baseline) {
            restored = true;
            break;
        }
    }
    if !restored && write_time_scale_direct(state, baseline) {
        restored = true;
        state.runtime.log(
            DnhLogLevel::Warn,
            "Givre Combat SpeedHack: Runtime.invoke restore failed; exact Unity Time.set_timeScale icall fallback restored the baseline.",
        );
    }
    if restored {
        state.runtime.log(
            DnhLogLevel::Info,
            &format!("Givre Combat SpeedHack: restored timeScale={baseline:.3} ({reason})."),
        );
        speed.applied = false;
        speed.baseline = None;
        true
    } else {
        state.runtime.log(
            DnhLogLevel::Error,
            "Givre Combat SpeedHack: failed to restore the pre-fight timeScale after 8 runtime retries and the exact Unity icall fallback.",
        );
        false
    }
}

fn update_speed(state: Capture) {
    let leased = lease_is_fresh();
    let active = leased && fight_is_active(state);
    let Ok(mut speed) = speed_state().lock() else {
        return;
    };

    if !leased {
        if restore_baseline(state, &mut speed, "lease inactive") {
            acknowledge_restored(&mut speed);
        }
        return;
    }

    speed.last_restored_token = None;

    if !active {
        let _ = restore_baseline(state, &mut speed, "fight inactive");
        return;
    }

    if !speed.applied {
        let Some(baseline) = read_time_scale(state) else {
            state.runtime.log(
                DnhLogLevel::Error,
                "Givre Combat SpeedHack: unable to read Unity timeScale; acceleration stays disabled.",
            );
            return;
        };
        if !baseline.is_finite() || baseline <= 0.0 {
            return;
        }
        let target = (baseline * COMBAT_MULTIPLIER).min(MAX_TIME_SCALE);
        if write_time_scale(state, target) {
            speed.baseline = Some(baseline);
            speed.applied = true;
            state.runtime.log(
                DnhLogLevel::Info,
                &format!(
                    "Givre Combat SpeedHack: fight active, timeScale {baseline:.3} -> {target:.3} (x{COMBAT_MULTIPLIER:.1})."
                ),
            );
        }
        return;
    }

    // Some Unity/game paths can rewrite Time.timeScale. Re-assert only while the
    // certified fight witness is active; the pre-fight baseline is never replaced.
    if let Some(baseline) = speed.baseline {
        let target = (baseline * COMBAT_MULTIPLIER).min(MAX_TIME_SCALE);
        if let Some(current) = read_time_scale(state)
            && current.is_finite()
            && (current - target).abs() > 0.01
        {
            let _ = write_time_scale(state, target);
        }
    }
}

fn load(runtime: Runtime) -> Option<()> {
    let input_checker = runtime.class(c"Core.dll", c"Core.Services.Fight", c"InputChecker");
    let time = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Time");
    if input_checker.is_null() || time.is_null() {
        runtime.log(
            DnhLogLevel::Error,
            "Givre Combat SpeedHack: InputChecker or UnityEngine.Time is unavailable.",
        );
        return None;
    }

    let get_time_scale = runtime.unique_method_by_signature(
        time,
        Some(c"get_timeScale"),
        Some(c"System.Single"),
        &[],
        DNH_MEMBER_STORAGE_STATIC,
        "UnityEngine.Time.get_timeScale()",
    )?;
    let set_time_scale = runtime.unique_method_by_signature(
        time,
        Some(c"set_timeScale"),
        Some(c"System.Void"),
        &[c"System.Single"],
        DNH_MEMBER_STORAGE_STATIC,
        "UnityEngine.Time.set_timeScale(float)",
    )?;
    let set_time_scale_icall = unsafe {
        (runtime.v2().resolve_icall)(c"UnityEngine.Time::set_timeScale(System.Single)".as_ptr())
    };
    if set_time_scale_icall.is_null() {
        runtime.log(
            DnhLogLevel::Error,
            "Givre Combat SpeedHack: exact UnityEngine.Time::set_timeScale(System.Single) icall is unavailable; refusing to arm x8 without a direct restore fallback.",
        );
        return None;
    }
    let state = Capture {
        runtime,
        input_checker_class: input_checker,
        get_time_scale,
        set_time_scale,
        set_time_scale_icall,
    };
    if let Ok(mut capture) = capture().lock() {
        *capture = Some(state);
    } else {
        return None;
    }
    if let Ok(mut speed) = speed_state().lock() {
        *speed = SpeedState::default();
    }
    if lease_is_fresh()
        && let Some(path) = restored_ack_path()
    {
        let _ = std::fs::remove_file(path);
    }
    update_speed(state);
    runtime.log(
        DnhLogLevel::Info,
        "Givre Combat SpeedHack ready: x8 Unity timeScale is armed only while the Dofus fight witness is active.",
    );
    Some(())
}

static MOD_ID: &[u8] = b"givre.dofus3.combat-speedhack\0";
static MOD_NAME: &[u8] = b"Givre Dofus Combat SpeedHack\0";
static MOD_VERSION: &[u8] = b"1.0.3\0";
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
    if host_abi_version == DNH_ABI_VERSION_8 {
        &MOD_INFO.0
    } else {
        null()
    }
}

#[unsafe(no_mangle)]
/// # Safety
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
    let state = capture().lock().ok().and_then(|state| *state);
    let Some(state) = state else {
        return;
    };
    let should_probe = if let Ok(mut speed) = speed_state().lock() {
        let tick = speed.tick;
        speed.tick = speed.tick.wrapping_add(1);
        tick.is_multiple_of(PROBE_FRAME_INTERVAL)
    } else {
        false
    };
    if should_probe {
        update_speed(state);
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Unload() {
    let Ok(mut capture_guard) = capture().lock() else {
        return;
    };
    let Some(state) = *capture_guard else {
        return;
    };
    if let Ok(mut speed) = speed_state().lock() {
        let restored = restore_baseline(state, &mut speed, "mod unload");
        speed.tick = 0;
        if restored {
            *capture_guard = None;
        } else {
            // Host ABI v8 has a void unload callback, so there is no veto channel here.
            // Keep the only known baseline/capture alive until return after exhausting
            // runtime-invoke retries plus the exact native setter fallback.
            state.runtime.log(
                DnhLogLevel::Error,
                "Givre Combat SpeedHack: unload exhausted every available timeScale restoration path.",
            );
        }
    }
}
