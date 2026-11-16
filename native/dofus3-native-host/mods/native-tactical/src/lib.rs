use dofus_native_mod_api::{
    DNH_ABI_VERSION_8, DNH_BUSY, DNH_ERROR, DNH_MEMBER_STORAGE_STATIC, DNH_OK,
    DnhFieldSignatureV3, DnhGcHandleV4, DnhHandle, DnhHostApiV1, DnhLogLevel,
    DnhMethodInfoV2, DnhModInfoV1, DnhUnityFindFieldsBySignatureFn,
};
use dofus_native_mod_sdk::Runtime;
use std::ffi::{CStr, c_char};
use std::mem::size_of;
use std::ptr::{null, null_mut};
use std::sync::Mutex;
use std::time::{Duration, Instant};

mod lease;
mod reflection;
mod diagnostic;
#[cfg(test)]
mod cleanup_tests;

const MAP_CELL_COUNT: usize = 560;
const MAP_WIDTH: i32 = 14;
const CELL_WIDTH: f32 = 86.0;
const HALF_CELL_WIDTH: f32 = 43.0;
const HALF_CELL_HEIGHT: f32 = 21.5;
const ORIGIN_X: f32 = -580.5;
const ORIGIN_Y: f32 = 483.75;
const CELLS_BUILT_PER_TICK: usize = 8;
const VK_F8: i32 = 0x77;

#[repr(C)]
#[derive(Clone, Copy)]
struct Vector3 {
    x: f32,
    y: f32,
    z: f32,
}

#[repr(C)]
#[derive(Clone, Copy)]
struct Quaternion {
    x: f32,
    y: f32,
    z: f32,
    w: f32,
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum CellKind {
    Hidden,
    LineOfSightObstacle,
    Walkable,
}

#[derive(Clone, Copy)]
struct SuppressedRenderer {
    gc_handle: DnhGcHandleV4,
    was_enabled: bool,
}

struct Bindings {
    map_renderer_class: DnhHandle,
    game_object_class: DnhHandle,
    transform_class: DnhHandle,
    renderer_class: DnhHandle,
    sprite_renderer_class: DnhHandle,
    tactical_cell_class: DnhHandle,
    map_accessor_class: DnhHandle,
    map_accessor_field: DnhHandle,
    map_data_field: DnhHandle,
    map_root_field: DnhHandle,
    game_object_ctor: DnhHandle,
    game_object_find: DnhHandle,
    game_object_get_transform: DnhHandle,
    game_object_get_component: DnhHandle,
    game_object_get_components_in_children: DnhHandle,
    game_object_set_active: DnhHandle,
    component_get_game_object: DnhHandle,
    transform_get_parent: DnhHandle,
    transform_set_parent: DnhHandle,
    transform_get_local_position: DnhHandle,
    transform_set_local_position: DnhHandle,
    transform_get_local_rotation: DnhHandle,
    transform_set_local_rotation: DnhHandle,
    transform_get_local_scale: DnhHandle,
    transform_set_local_scale: DnhHandle,
    object_instantiate: DnhHandle,
    object_destroy: DnhHandle,
    object_is_alive: DnhHandle,
    object_set_name: DnhHandle,
    renderer_get_enabled: DnhHandle,
    renderer_set_enabled: DnhHandle,
    sprite_renderer_get_sprite: DnhHandle,
    sprite_renderer_set_sprite: DnhHandle,
    tactical_set_cell_id: DnhHandle,
    tactical_apply_kind: DnhHandle,
    tactical_sprite_fields: Vec<DnhHandle>,
    addressable_load: reflection::Method,
    addressable_release: reflection::Method,
    task_is_completed: DnhHandle,
    task_is_successful: DnhHandle,
}

struct BuildProgress {
    cells: [CellKind; MAP_CELL_COUNT],
    next_cell: usize,
    first_cell_details: String,
}

struct State {
    runtime: Runtime,
    bindings: Bindings,
    enabled: bool,
    f8_was_down: bool,
    expected_map: i64,
    next_map_probe: Instant,
    failed: bool,
    cleanup_pending: bool,
    map_id: i64,
    map_renderer_gc: DnhGcHandleV4,
    layer_gc: [DnhGcHandleV4; 3],
    overlay_root_gc: DnhGcHandleV4,
    unparented_cell_gc: DnhGcHandleV4,
    prefab_gc: DnhGcHandleV4,
    prefab_is_addressable: bool,
    load_task_gc: DnhGcHandleV4,
    build: Option<BuildProgress>,
    suppressed: Vec<SuppressedRenderer>,
    task_getters: Option<(reflection::Getter, reflection::Getter)>,
    last_map_diagnosis: String,
}

// The host invokes every lifecycle callback on Unity's owning thread.
unsafe impl Send for State {}

struct Module {
    runtime: Runtime,
    directory: std::path::PathBuf,
    target_start: u64,
    last_lease: Option<lease::Lease>,
    state: Option<State>,
    binding_failed: bool,
    requested_enabled: bool,
    acknowledged: String,
}

static MODULE: Mutex<Option<Module>> = Mutex::new(None);

fn unique(handles: &[DnhHandle]) -> Option<DnhHandle> {
    match handles { [handle] if !handle.is_null() => Some(*handle), _ => None }
}

fn argument<T>(value: &T) -> DnhHandle {
    std::ptr::from_ref(value).cast_mut().cast()
}

fn required(handle: Option<DnhHandle>) -> Option<DnhHandle> {
    handle.filter(|value| !value.is_null())
}

fn method_info(runtime: Runtime, method: DnhHandle) -> Option<DnhMethodInfoV2> {
    let mut info = DnhMethodInfoV2 {
        struct_size: size_of::<DnhMethodInfoV2>() as u32,
        name: null(),
        return_type_name: null(),
        parameter_count: 0,
        flags: 0,
        is_static: 0,
        reserved: [0; 3],
    };
    unsafe { (runtime.v2().copy_method_info)(method, &mut info) }.then_some(info)
}

fn text(pointer: *const c_char) -> String {
    if pointer.is_null() {
        return String::new();
    }
    unsafe { CStr::from_ptr(pointer) }
        .to_string_lossy()
        .into_owned()
}

fn declared_methods(runtime: Runtime, class: DnhHandle) -> Vec<DnhHandle> {
    let count = unsafe { (runtime.v2().get_class_methods)(class, null_mut(), 0) };
    let mut methods = vec![null_mut(); count];
    if count != 0 {
        unsafe {
            (runtime.v2().get_class_methods)(class, methods.as_mut_ptr(), methods.len());
        }
    }
    methods
}

fn resolve_tactical_class(runtime: Runtime) -> Option<DnhHandle> {
    required(Some(runtime.class(c"Core.dll", c"Core.Rendering.Sprites.Interactive", c"TacticalDebugCell")))
}

fn resolve_map_root_field(
    find: DnhUnityFindFieldsBySignatureFn,
    class: DnhHandle,
) -> Result<DnhHandle, String> {
    // 3.7.12.12: epf.<doqc>k__BackingField has attributes 0x0011 (private static).
    let signature = DnhFieldSignatureV3 {
        struct_size: size_of::<DnhFieldSignatureV3>() as u32,
        name: null(),
        type_name: c"UnityEngine.GameObject".as_ptr(),
        minimum_offset: 1,
        maximum_offset: 0,
        storage: DNH_MEMBER_STORAGE_STATIC,
        reserved: [0; 3],
    };
    let mut field = null_mut();
    let count = unsafe { find(class, &signature, &mut field, 1) };
    if count == 1 && !field.is_null() {
        Ok(field)
    } else {
        Err(format!("epf static field UnityEngine.GameObject: expected one non-null match, found {count}"))
    }
}

fn is_wi_type_name(name: &str) -> bool {
    name == "wi" || name.ends_with(".wi") || name.ends_with("/wi") || name.ends_with("+wi")
}

// The runtime may report the enum parameter with a qualified name; metadata only fixes the short name.
fn resolve_tactical_apply_kind(runtime: Runtime, class: DnhHandle) -> Result<DnhHandle, String> {
    let mut seen = Vec::new();
    let methods = declared_methods(runtime, class).into_iter().filter(|method| {
        let Some(info) = method_info(runtime, *method) else { return false; };
        if info.is_static != 0 || info.parameter_count != 1 || text(info.name) != "qxk" { return false; }
        let parameter = text(unsafe { (runtime.v2().method_parameter_type_name)(*method, 0) });
        let matched = is_wi_type_name(&parameter);
        seen.push(parameter);
        matched
    }).collect::<Vec<_>>();
    unique(&methods).ok_or_else(|| format!("TacticalDebugCell.qxk(wi): parameter types seen {seen:?}"))
}

fn resolve_bindings(runtime: Runtime) -> Result<Bindings, String> {
    // Preserve the exact expression (class, method and parameter types) at every
    // required method failure. DNM_Tick logs the first error once and latches it.
    macro_rules! binding {
        ($value:expr) => {
            required($value).ok_or_else(|| stringify!($value).to_owned())?
        };
    }
    // Verified against 3.7.12.12 metadata v39; see METADATA-3.7.12.12.md.
    let map_renderer_class = runtime.class(c"Core.dll", c"Core.Rendering.Map.GameObjects", c"MapController");
    let map_accessor_class = runtime.class(c"Core.dll", c"", c"epf");
    let game_object_class =
        runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"GameObject");
    let transform_class =
        runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Transform");
    let component_class =
        runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Component");
    let object_class = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Object");
    let sprite_renderer_class = runtime.class(
        c"UnityEngine.CoreModule.dll",
        c"UnityEngine",
        c"SpriteRenderer",
    );
    let renderer_class = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Renderer");
    let tactical_cell_class = resolve_tactical_class(runtime).unwrap_or(null_mut());
    let addressable_class = runtime.class(
        c"Ankama.AddressableUtility.Runtime.dll",
        c"Ankama.AddressableUtilities.Runtime",
        c"AddressableUtility",
    );

    for (name, class) in [
        ("Core.Rendering.Map.GameObjects.MapController", map_renderer_class),
        ("epf", map_accessor_class),
        ("UnityEngine.GameObject", game_object_class),
        ("UnityEngine.Transform", transform_class),
        ("UnityEngine.Component", component_class),
        ("UnityEngine.Object", object_class),
        ("UnityEngine.Renderer", renderer_class),
        ("UnityEngine.SpriteRenderer", sprite_renderer_class),
        ("Core.Rendering.Sprites.Interactive.TacticalDebugCell", tactical_cell_class),
        ("Ankama.AddressableUtilities.Runtime.AddressableUtility", addressable_class),
    ] {
        if class.is_null() {
            return Err(format!("required class {name}"));
        }
    }

    let accessor_fields = runtime.fields_by_type(map_renderer_class, c"fpm");
    let map_accessor_field = unique(&accessor_fields).ok_or_else(|| format!(
        "MapController instance field fpm: expected one match, found {}", accessor_fields.len()))?;
    let data_fields = runtime.fields_by_type(map_accessor_class, c"AleCore.Data.UnityClient.ClientMapData");
    let map_data_field = unique(&data_fields).ok_or_else(|| format!(
        "epf instance field AleCore.Data.UnityClient.ClientMapData: expected one match, found {}", data_fields.len()))?;
    let map_root_field = resolve_map_root_field(runtime.v3().find_fields_by_signature, map_accessor_class)?;

    let game_object_ctor =
        binding!(runtime.instance_method(game_object_class, c".ctor", &[c"System.String"]));
    let game_object_find =
        binding!(runtime.method(game_object_class, c"Find", &[c"System.String"], DNH_MEMBER_STORAGE_STATIC));
    let game_object_get_transform =
        binding!(runtime.instance_method(game_object_class, c"get_transform", &[]));
    let game_object_get_component =
        binding!(runtime.instance_method(game_object_class, c"GetComponent", &[c"System.Type"]));
    let game_object_get_components_in_children = binding!(runtime.instance_method(
        game_object_class,
        c"GetComponentsInChildren",
        &[c"System.Type", c"System.Boolean"],
    ));
    let game_object_set_active =
        binding!(runtime.instance_method(game_object_class, c"SetActive", &[c"System.Boolean"]));
    let component_get_game_object =
        binding!(runtime.instance_method(component_class, c"get_gameObject", &[]));
    let transform_get_parent =
        binding!(runtime.instance_method(transform_class, c"get_parent", &[]));
    let transform_set_parent = binding!(runtime.instance_method(
        transform_class,
        c"SetParent",
        &[c"UnityEngine.Transform", c"System.Boolean"],
    ));
    let transform_get_local_position =
        binding!(runtime.instance_method(transform_class, c"get_localPosition", &[]));
    let transform_set_local_position = binding!(runtime.instance_method(
        transform_class,
        c"set_localPosition",
        &[c"UnityEngine.Vector3"],
    ));
    let transform_get_local_rotation =
        binding!(runtime.instance_method(transform_class, c"get_localRotation", &[]));
    let transform_set_local_rotation = binding!(runtime.instance_method(
        transform_class,
        c"set_localRotation",
        &[c"UnityEngine.Quaternion"],
    ));
    let transform_get_local_scale =
        binding!(runtime.instance_method(transform_class, c"get_localScale", &[]));
    let transform_set_local_scale = binding!(runtime.instance_method(
        transform_class,
        c"set_localScale",
        &[c"UnityEngine.Vector3"],
    ));
    let object_destroy = binding!(runtime.method(
        object_class,
        c"Destroy",
        &[c"UnityEngine.Object"],
        DNH_MEMBER_STORAGE_STATIC,
    ));
    let object_is_alive = binding!(runtime.method(
        object_class,
        c"op_Implicit",
        &[c"UnityEngine.Object"],
        DNH_MEMBER_STORAGE_STATIC,
    ));
    let object_instantiate = binding!(runtime.method(
        object_class,
        c"Instantiate",
        &[c"UnityEngine.Object"],
        DNH_MEMBER_STORAGE_STATIC,
    ));
    let object_set_name =
        binding!(runtime.instance_method(object_class, c"set_name", &[c"System.String"]));
    let renderer_get_enabled =
        binding!(runtime.instance_method(renderer_class, c"get_enabled", &[]));
    let renderer_set_enabled =
        binding!(runtime.instance_method(renderer_class, c"set_enabled", &[c"System.Boolean"]));
    let sprite_renderer_get_sprite =
        binding!(runtime.instance_method(sprite_renderer_class, c"get_sprite", &[]));
    let sprite_renderer_set_sprite = binding!(runtime.instance_method(
        sprite_renderer_class,
        c"set_sprite",
        &[c"UnityEngine.Sprite"],
    ));
    runtime.log(
        DnhLogLevel::Info,
        "Native Tactical: Unity object/transform/renderer bindings resolved.",
    );
    // Metadata v39: qxl(Int32), qxk(wi). qxj is the enum setter, not the cell ID.
    let tactical_set_cell_id =
        binding!(runtime.instance_method(tactical_cell_class, c"qxl", &[c"System.Int32"]));
    let tactical_apply_kind = resolve_tactical_apply_kind(runtime, tactical_cell_class)?;
    let tactical_sprite_fields = runtime.fields_by_type(tactical_cell_class, c"UnityEngine.Sprite");
    if tactical_sprite_fields.len() < 2 {
        return Err(format!("TacticalDebugCell instance fields UnityEngine.Sprite: expected at least two, found {}",
            tactical_sprite_fields.len()));
    }
    runtime.log(
        DnhLogLevel::Info,
        &format!(
            "Native Tactical: cell methods and {} Sprite field(s) resolved.",
            tactical_sprite_fields.len()
        ),
    );

    binding!(runtime.method(
        addressable_class,
        c"LoadAssetAsync",
        &[c"System.String", c"System.Int32"],
        DNH_MEMBER_STORAGE_STATIC,
    ));
    runtime.log(
        DnhLogLevel::Info,
        "Native Tactical: generic AddressableUtility.LoadAssetAsync method resolved.",
    );
    // Exact 3.7.12.12 method tokens within AddressableUtility, not overload order.
    let addressable_load = reflection::Method::resolve(
        runtime, addressable_class, game_object_class, 0x06000022, "LoadAssetAsync")?;
    addressable_load.require_task_game_object()?;
    let addressable_release = reflection::Method::resolve(
        runtime, addressable_class, object_class, 0x06000027, "Release")?;
    // Signature lookup searches declared methods, not Task<T>'s base class.
    let task_class = runtime.class(c"mscorlib.dll", c"System.Threading.Tasks", c"Task");
    // Live 3.7.12.12: the by-name Task lookup returns nothing; fall back to the
    // returned object's own class hierarchy at the first poll.
    let task_is_completed = runtime.instance_method(task_class, c"get_IsCompleted", &[]).unwrap_or(null_mut());
    let task_is_successful = runtime.instance_method(task_class, c"get_IsCompletedSuccessfully", &[]).unwrap_or(null_mut());
    runtime.log(DnhLogLevel::Info,
        "Native Tactical: concrete reflection LoadAssetAsync<GameObject> and Release<Object> resolved; Task<GameObject> verified.");

    Ok(Bindings {
        map_renderer_class,
        game_object_class,
        transform_class,
        renderer_class,
        sprite_renderer_class,
        tactical_cell_class,
        map_accessor_class,
        map_accessor_field,
        map_data_field,
        map_root_field,
        game_object_ctor,
        game_object_find,
        game_object_get_transform,
        game_object_get_component,
        game_object_get_components_in_children,
        game_object_set_active,
        component_get_game_object,
        transform_get_parent,
        transform_set_parent,
        transform_get_local_position,
        transform_set_local_position,
        transform_get_local_rotation,
        transform_set_local_rotation,
        transform_get_local_scale,
        transform_set_local_scale,
        object_instantiate,
        object_destroy,
        object_is_alive,
        object_set_name,
        renderer_get_enabled,
        renderer_set_enabled,
        sprite_renderer_get_sprite,
        sprite_renderer_set_sprite,
        tactical_set_cell_id,
        tactical_apply_kind,
        tactical_sprite_fields,
        addressable_load,
        addressable_release,
        task_is_completed,
        task_is_successful,
    })
}

fn read_cells(runtime: Runtime, map_data: DnhHandle) -> Option<[CellKind; MAP_CELL_COUNT]> {
    let raw_cells = runtime.read_reference(map_data, c"cellsData")?;
    let list_class = unsafe { (runtime.v2().get_object_class)(raw_cells) };
    let count_method = runtime.instance_method(list_class, c"get_Count", &[])?;
    let item_method = runtime.instance_method(list_class, c"get_Item", &[c"System.Int32"])?;
    let count: i32 = runtime.unbox(runtime.invoke(count_method, raw_cells, &[])?)?;
    if count != MAP_CELL_COUNT as i32 {
        return None;
    }

    let mut cells = [CellKind::Hidden; MAP_CELL_COUNT];
    let mut found = [false; MAP_CELL_COUNT];
    let mut found_count = 0;
    for index in 0..count {
        let cell = runtime
            .invoke(item_method, raw_cells, &[argument(&index)])
            .unwrap_or(null_mut());
        let Some(cell_id) = runtime.read_value::<u32>(cell, c"cellNumber") else {
            continue;
        };
        if cell_id >= MAP_CELL_COUNT as u32 || found[cell_id as usize] {
            return None;
        }
        let Some(movable) = runtime.read_value::<bool>(cell, c"mov") else {
            continue;
        };
        let Some(line_of_sight) = runtime.read_value::<bool>(cell, c"los") else {
            continue;
        };
        let cell_index = cell_id as usize;
        cells[cell_index] = if movable {
            CellKind::Walkable
        } else if line_of_sight {
            CellKind::Hidden
        } else {
            CellKind::LineOfSightObstacle
        };
        if !found[cell_index] {
            found[cell_index] = true;
            found_count += 1;
        }
    }
    (found_count == MAP_CELL_COUNT).then_some(cells)
}

// Only exact typed instance fields from the confirmed owner chain admit a layer.
const OWNED_LAYER_FIELDS: [(&CStr, &CStr); 3] = [
    (c"dgih", c"dgii"), (c"dgij", c"dgik"), (c"dgil", c"dgim"),
];

fn live(state: &State, object: DnhHandle) -> bool {
    !object.is_null() && state.runtime.invoke(state.bindings.object_is_alive, null_mut(), &[object])
        .and_then(|value| state.runtime.unbox::<bool>(value)).unwrap_or(false)
}

fn owned_layers(state: &State, accessor: DnhHandle, controller: DnhHandle) -> Option<[DnhHandle; 3]> {
    let runtime = state.runtime;
    let owner = diagnostic::reference_field(runtime, accessor, c"doqa", "ue")?;
    if runtime.object_class_name(owner) != "ue" { return None; }
    let backlink = diagnostic::reference_field(runtime, owner, c"dgjn", "Core.Rendering.Map.GameObjects.MapController")?;
    if backlink != controller || !live(state, backlink) { return None; }
    let branches = diagnostic::reference_field(runtime, owner, c"dgjp", "ub")?;
    if runtime.object_class_name(branches) != "ub" { return None; }
    let mut layers = [null_mut(); 3];
    for (index, (go_field, transform_field)) in OWNED_LAYER_FIELDS.iter().enumerate() {
        let go = diagnostic::reference_field(runtime, branches, go_field, "UnityEngine.GameObject")?;
        let transform = diagnostic::reference_field(runtime, branches, transform_field, "UnityEngine.Transform")?;
        if !runtime.object_is(go, state.bindings.game_object_class)
            || !runtime.object_is(transform, state.bindings.transform_class)
            || !live(state, go) || !live(state, transform)
            || runtime.invoke(state.bindings.game_object_get_transform, go, &[])? != transform {
            return None;
        }
        layers[index] = go;
    }
    // Distinct top-level roots keep the sibling overlay outside suppression.
    for (index, layer) in layers.iter().enumerate() {
        if layers[..index].contains(layer) { return None; }
        let transform = runtime.invoke(state.bindings.game_object_get_transform, *layer, &[])?;
        if !runtime.invoke(state.bindings.transform_get_parent, transform, &[])?.is_null() { return None; }
    }
    Some(layers)
}

fn map_snapshot(state: &State, controller: DnhHandle) -> Option<(DnhHandle, [DnhHandle; 3], i64)> {
    if unique(&state.runtime.find_objects(state.bindings.map_renderer_class))? != controller { return None; }
    if !state.runtime.object_is(controller, state.bindings.map_renderer_class) || !live(state, controller) { return None; }
    let mut accessor = null_mut();
    // SAFETY: the binding is the unique reference-typed fpm field; the host copies
    // one IL2CPP reference into this pointer-sized slot on Unity's owning thread.
    if !unsafe { (state.runtime.v2().field_get_value)(controller, state.bindings.map_accessor_field,
        (&mut accessor as *mut DnhHandle).cast()) }
        || !state.runtime.object_is(accessor, state.bindings.map_accessor_class) { return None; }
    // Use the existing signature-resolved data field, not a guessed obfuscated name.
    let mut map_data: DnhHandle = null_mut();
    // SAFETY: map_data_field was resolved by its reference type and this output is pointer-sized.
    if !unsafe { (state.runtime.v2().field_get_value)(accessor, state.bindings.map_data_field,
        (&mut map_data as *mut DnhHandle).cast()) } { return None; }
    let id = i64::from(state.runtime.read_value::<i32>(map_data, c"id")?);
    if id != state.expected_map { return None; }
    Some((map_data, owned_layers(state, accessor, controller)?, id))
}

fn same_layers(current: [DnhHandle; 3], retained: [DnhHandle; 3]) -> bool {
    current.iter().zip(retained).all(|(current, retained)| !current.is_null() && *current == retained)
}

fn retained_layers(state: &State, current: [DnhHandle; 3]) -> bool {
    same_layers(current, state.layer_gc.map(|handle| state.runtime.gc_target(handle)))
}

// Failed ownership is retried with the existing bounded, change-only diagnostic.
fn map_diagnosis(state: &State) -> String {
    if try_resolve_map(state).is_some() { return "map resolved".to_owned(); }
    "current leased map requires controller -> epf.doqa ue -> dgjp ub, matching dgjn backlink, three live typed GO/Transform pairs and 560 cells".to_owned()
}

fn try_resolve_map(state: &State) -> Option<(DnhHandle, [DnhHandle; 3], i64, [CellKind; MAP_CELL_COUNT])> {
    let controllers = state.runtime.find_objects(state.bindings.map_renderer_class);
    let controller = unique(&controllers)?;
    let (data, layers, id) = map_snapshot(state, controller)?;
    Some((controller, layers, id, read_cells(state.runtime, data)?))
}

fn copy_transform(state: &State, source_object: DnhHandle, target_object: DnhHandle) -> bool {
    let Some(source) =
        state
            .runtime
            .invoke(state.bindings.game_object_get_transform, source_object, &[])
    else {
        return false;
    };
    let Some(target) =
        state
            .runtime
            .invoke(state.bindings.game_object_get_transform, target_object, &[])
    else {
        return false;
    };
    let parent = state
        .runtime
        .invoke(state.bindings.transform_get_parent, source, &[])
        .unwrap_or(null_mut());
    let world_position_stays = false;
    state.runtime.invoke(
        state.bindings.transform_set_parent,
        target,
        &[parent, argument(&world_position_stays)],
    );

    let position = state
        .runtime
        .invoke(state.bindings.transform_get_local_position, source, &[]);
    let rotation = state
        .runtime
        .invoke(state.bindings.transform_get_local_rotation, source, &[]);
    let scale = state
        .runtime
        .invoke(state.bindings.transform_get_local_scale, source, &[]);
    let Some(position) = position.and_then(|value| state.runtime.unbox::<Vector3>(value)) else {
        return false;
    };
    let Some(rotation) = rotation.and_then(|value| state.runtime.unbox::<Quaternion>(value)) else {
        return false;
    };
    let Some(scale) = scale.and_then(|value| state.runtime.unbox::<Vector3>(value)) else {
        return false;
    };
    state.runtime.invoke(
        state.bindings.transform_set_local_position,
        target,
        &[argument(&position)],
    );
    state.runtime.invoke(
        state.bindings.transform_set_local_rotation,
        target,
        &[argument(&rotation)],
    );
    state.runtime.invoke(
        state.bindings.transform_set_local_scale,
        target,
        &[argument(&scale)],
    );
    true
}

fn restore_renderers(state: &mut State) {
    state.suppressed.retain(|suppressed| {
        let renderer = state.runtime.gc_target(suppressed.gc_handle);
        // GC roots can outlive Unity's native objects after a scene transition.
        // Object.op_Implicit is the supported destroyed-object check.
        if !renderer.is_null() {
            let alive = state.runtime.invoke(state.bindings.object_is_alive, null_mut(), &[renderer])
                .and_then(|value| state.runtime.unbox::<bool>(value));
            if alive.is_none() { return true; }
            if alive == Some(true) && state.runtime.invoke(
                state.bindings.renderer_set_enabled,
                renderer,
                &[argument(&suppressed.was_enabled)],
            ).is_none() { return true; }
        }
        state.runtime.gc_free(suppressed.gc_handle);
        false
    });
}

fn suppress_branch(state: &mut State, branch_object: DnhHandle, scenery: &[DnhHandle]) -> bool {
    let renderer_type =
        unsafe { (state.runtime.v2().class_type_object)(state.bindings.renderer_class) };
    let include_inactive = true;
    let array = state
        .runtime
        .invoke(
            state.bindings.game_object_get_components_in_children,
            branch_object,
            &[renderer_type, argument(&include_inactive)],
        )
        .unwrap_or(null_mut());
    if array.is_null() {
        return false;
    }
    let count = unsafe { (state.runtime.v2().array_length)(array) };
    let data = unsafe { (state.runtime.v2().array_data)(array) };
    if data.is_null() {
        return false;
    }
    let renderers = unsafe { std::slice::from_raw_parts(data.cast::<DnhHandle>(), count) };
    for &renderer in renderers {
        if !scenery.contains(&renderer) { continue; }
        let Some(was_enabled) = state
            .runtime
            .invoke(state.bindings.renderer_get_enabled, renderer, &[])
            .and_then(|value| state.runtime.unbox::<bool>(value))
        else {
            return false;
        };
        let gc_handle = state.runtime.gc_new(renderer, false);
        if gc_handle == 0 { return false; }
        state.suppressed.push(SuppressedRenderer { gc_handle, was_enabled });
        let disabled = false;
        if state.runtime.invoke(
            state.bindings.renderer_set_enabled,
            renderer,
            &[argument(&disabled)],
        ).is_none() { return false; }
    }
    true
}

// qii can remove pool entries before returning; post-load index pairing is not
// an ownership proof. Only a captured per-assignment association may reach here.
fn suppress_map(state: &mut State, layers: [DnhHandle; 3]) -> bool {
    let _ = layers;
    state.runtime.log(DnhLogLevel::Warn, "Native Tactical: construction association unavailable; original renderers preserved.");
    false
}

fn component(state: &State, game_object: DnhHandle, class: DnhHandle) -> DnhHandle {
    let type_object = unsafe { (state.runtime.v2().class_type_object)(class) };
    state
        .runtime
        .invoke(
            state.bindings.game_object_get_component,
            game_object,
            &[type_object],
        )
        .unwrap_or(null_mut())
}

fn set_active(state: &State, game_object: DnhHandle, active: bool) -> Option<DnhHandle> {
    state.runtime.invoke(
        state.bindings.game_object_set_active,
        game_object,
        &[argument(&active)],
    )
}

fn serialized_sprites(
    state: &State,
    tactical_cell: DnhHandle,
    current: DnhHandle,
) -> (DnhHandle, DnhHandle) {
    let mut candidates = Vec::new();
    let mut movement = null_mut();
    let mut line_of_sight = null_mut();
    for &field in &state.bindings.tactical_sprite_fields {
        let mut sprite: DnhHandle = null_mut();
        if !unsafe {
            (state.runtime.v2().field_get_value)(
                tactical_cell,
                field,
                (&mut sprite as *mut DnhHandle).cast(),
            )
        } || sprite.is_null()
        {
            continue;
        }
        let name = state.runtime.field_name(field).to_ascii_lowercase();
        if name.contains("mov") {
            movement = sprite;
        } else if name.contains("los") {
            line_of_sight = sprite;
        }
        if !candidates.contains(&sprite) {
            candidates.push(sprite);
        }
    }
    if movement.is_null() {
        movement = if candidates.contains(&current) {
            current
        } else {
            candidates.first().copied().unwrap_or(null_mut())
        };
    }
    if line_of_sight.is_null() {
        line_of_sight = candidates
            .iter()
            .copied()
            .find(|sprite| *sprite != movement)
            .or_else(|| candidates.last().copied())
            .unwrap_or(null_mut());
    }
    (movement, line_of_sight)
}

fn build_cell(
    state: &mut State,
    root: DnhHandle,
    prefab: DnhHandle,
    cell_id: usize,
    kind: CellKind,
) -> Option<String> {
    let cell = state
        .runtime
        .invoke(state.bindings.object_instantiate, null_mut(), &[prefab])
        .unwrap_or(null_mut());
    if cell.is_null() {
        return None;
    }
    state.unparented_cell_gc = state.runtime.gc_new(cell, false);
    if state.unparented_cell_gc == 0 {
        state.runtime.log(DnhLogLevel::Error, "Could not retain the instantiated tactical cell for cleanup.");
        state.runtime.invoke(state.bindings.object_destroy, null_mut(), &[cell]);
        return None;
    }
    set_active(state, cell, false)?;
    let name = state.runtime.string(&format!("cell::{cell_id}"));
    state
        .runtime
        .invoke(state.bindings.object_set_name, cell, &[name]);

    let cell_transform = state
        .runtime
        .invoke(state.bindings.game_object_get_transform, cell, &[])
        .unwrap_or(null_mut());
    let root_transform = state
        .runtime
        .invoke(state.bindings.game_object_get_transform, root, &[])
        .unwrap_or(null_mut());
    if cell_transform.is_null() || root_transform.is_null() {
        return None;
    }
    let world_position_stays = false;
    state.runtime.invoke(
        state.bindings.transform_set_parent,
        cell_transform,
        &[root_transform, argument(&world_position_stays)],
    )?;
    state.runtime.gc_free(state.unparented_cell_gc);
    state.unparented_cell_gc = 0;
    let cell_id_i32 = cell_id as i32;
    let row = cell_id_i32 / MAP_WIDTH;
    let column = cell_id_i32 % MAP_WIDTH;
    let position = Vector3 {
        x: ORIGIN_X + column as f32 * CELL_WIDTH + (row & 1) as f32 * HALF_CELL_WIDTH,
        y: ORIGIN_Y - row as f32 * HALF_CELL_HEIGHT,
        z: 0.0,
    };
    state.runtime.invoke(
        state.bindings.transform_set_local_position,
        cell_transform,
        &[argument(&position)],
    )?;

    // Awake caches the SpriteRenderer, so activate before applying the native
    // TacticalDebugCell classification exactly like the former MelonLoader mod.
    set_active(state, cell, true)?;
    let tactical = component(state, cell, state.bindings.tactical_cell_class);
    let sprite_renderer = component(state, cell, state.bindings.sprite_renderer_class);
    if tactical.is_null() || sprite_renderer.is_null() {
        return None;
    }
    state.runtime.invoke(
        state.bindings.tactical_set_cell_id,
        tactical,
        &[argument(&cell_id_i32)],
    )?;
    let native_kind = match kind {
        CellKind::Hidden => 1_i32,
        CellKind::LineOfSightObstacle => 2_i32,
        CellKind::Walkable => 3_i32,
    };
    state.runtime.invoke(
        state.bindings.tactical_apply_kind,
        tactical,
        &[argument(&native_kind)],
    )?;

    let current = state
        .runtime
        .invoke(
            state.bindings.sprite_renderer_get_sprite,
            sprite_renderer,
            &[],
        )
        .unwrap_or(null_mut());
    let (movement, line_of_sight) = serialized_sprites(state, tactical, current);
    let visible = kind != CellKind::Hidden;
    state.runtime.invoke(
        state.bindings.renderer_set_enabled,
        sprite_renderer,
        &[argument(&visible)],
    )?;
    let expected = match kind {
        CellKind::Hidden => null_mut(),
        CellKind::LineOfSightObstacle => line_of_sight,
        CellKind::Walkable => movement,
    };
    if !expected.is_null() {
        state.runtime.invoke(
            state.bindings.sprite_renderer_set_sprite,
            sprite_renderer,
            &[expected],
        )?;
    }
    Some(if cell_id == 0 {
        let actual_position = state.runtime.invoke(state.bindings.transform_get_local_position, cell_transform, &[])
            .and_then(|value| state.runtime.unbox::<Vector3>(value));
        let actual_sprite = state.runtime.invoke(state.bindings.sprite_renderer_get_sprite, sprite_renderer, &[])
            .unwrap_or(null_mut());
        let actual_enabled = state.runtime.invoke(state.bindings.renderer_get_enabled, sprite_renderer, &[])
            .and_then(|value| state.runtime.unbox::<bool>(value));
        format!("cell0 local_position={:?} sprite_nonnull={} expected_sprite_matches={} enabled={actual_enabled:?}",
            actual_position.map(|p| (p.x, p.y, p.z)), !actual_sprite.is_null(), actual_sprite == expected)
    } else { String::new() })
}

fn begin_overlay(
    state: &mut State,
    renderer: DnhHandle,
    layers: [DnhHandle; 3],
    map_id: i64,
    cells: [CellKind; MAP_CELL_COUNT],
) -> bool {
    let overlay = unsafe { (state.runtime.v2().object_new)(state.bindings.game_object_class) };
    if overlay.is_null() {
        return false;
    }
    let name = state
        .runtime
        .string(&format!("NativeTacticalOverlay_{map_id}"));
    state
        .runtime
        .invoke(state.bindings.game_object_ctor, overlay, &[name]);
    if !copy_transform(state, layers[0], overlay) {
        state
            .runtime
            .invoke(state.bindings.object_destroy, null_mut(), &[overlay]);
        return false;
    }
    let overlay_gc = state.runtime.gc_new(overlay, false);
    let renderer_gc = state.runtime.gc_new(renderer, false);
    if overlay_gc == 0 || renderer_gc == 0 {
        if overlay_gc != 0 {
            state.runtime.gc_free(overlay_gc);
        }
        if renderer_gc != 0 {
            state.runtime.gc_free(renderer_gc);
        }
        state
            .runtime
            .invoke(state.bindings.object_destroy, null_mut(), &[overlay]);
        return false;
    }
    // Suppress the original only after every cell has built successfully.
    state.overlay_root_gc = overlay_gc;
    state.map_renderer_gc = renderer_gc;
    for (index, layer) in layers.into_iter().enumerate() {
        state.layer_gc[index] = state.runtime.gc_new(layer, false);
        if state.layer_gc[index] == 0 {
            disable_current_map(state, true);
            return false;
        }
    }
    state.map_id = map_id;
    state.build = Some(BuildProgress {
        cells,
        next_cell: 0,
        first_cell_details: String::new(),
    });
    true
}

fn process_build(state: &mut State) {
    let Some(mut build) = state.build.take() else {
        return;
    };
    let root = state.runtime.gc_target(state.overlay_root_gc);
    let prefab = state.runtime.gc_target(state.prefab_gc);
    if root.is_null() || prefab.is_null() {
        state.runtime.log(
            DnhLogLevel::Error,
            "The tactical prefab or overlay root disappeared while building.",
        );
        disable_current_map(state, true);
        state.failed = true;
        return;
    }
    let end = (build.next_cell + CELLS_BUILT_PER_TICK).min(MAP_CELL_COUNT);
    while build.next_cell < end {
        let cell_id = build.next_cell;
        let Some(details) = build_cell(state, root, prefab, cell_id, build.cells[cell_id]) else {
            state.runtime.log(
                DnhLogLevel::Error,
                &format!("Could not instantiate native tactical sprite for cell {cell_id}."),
            );
            disable_current_map(state, true);
            state.failed = true;
            return;
        };
        if cell_id == 0 { build.first_cell_details = details; }
        build.next_cell += 1;
    }
    if build.next_cell == MAP_CELL_COUNT {
        if let Some((_, layers, id)) = map_snapshot(state, state.runtime.gc_target(state.map_renderer_gc))
            && id == state.map_id && retained_layers(state, layers) {
            if !suppress_map(state, layers) {
                disable_current_map(state, true);
                state.failed = true;
                return;
            }
            let object_class = state.runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Object");
            let names = state.runtime.instance_method(object_class, c"get_name", &[])
                .map(|get_name| layers.map(|layer| state.runtime.invoke(get_name, layer, &[])
                    .and_then(|name| state.runtime.managed_string(name))
                    .map(|name| name.chars().take(96).map(|c| if c.is_control() { ' ' } else { c }).collect())
                    .unwrap_or_else(|| "?".to_owned())))
                .unwrap_or_else(|| std::array::from_fn(|_| "?".to_owned()));
            state.runtime.log(DnhLogLevel::Info, &format!(
                "Native Tactical: verified map {id} owned layers {names:?}; 560 native tacticalCell sprites constructed; {}.",
                build.first_cell_details));
        } else {
            disable_current_map(state, true);
            return;
        }
    } else {
        state.build = Some(build);
    }
}

fn destroy_handle(state: &State, handle: &mut DnhGcHandleV4) {
    if *handle == 0 {
        return;
    }
    let object = state.runtime.gc_target(*handle);
    if !object.is_null() {
        let alive = state.runtime.invoke(state.bindings.object_is_alive, null_mut(), &[object])
            .and_then(|value| state.runtime.unbox::<bool>(value));
        if alive.is_none() { return; }
        if alive == Some(true) && state
            .runtime
            .invoke(state.bindings.object_destroy, null_mut(), &[object]).is_none() { return; }
    }
    state.runtime.gc_free(*handle);
    *handle = 0;
}

fn disable_current_map(state: &mut State, restore: bool) -> bool {
    state.build = None;
    let mut cell = std::mem::take(&mut state.unparented_cell_gc);
    destroy_handle(state, &mut cell);
    state.unparented_cell_gc = cell;
    let mut overlay = std::mem::take(&mut state.overlay_root_gc);
    destroy_handle(state, &mut overlay);
    state.overlay_root_gc = overlay;
    if restore {
        restore_renderers(state);
    } else {
        for suppressed in state.suppressed.drain(..) {
            state.runtime.gc_free(suppressed.gc_handle);
        }
    }
    state.cleanup_pending = state.unparented_cell_gc != 0 || state.overlay_root_gc != 0 || !state.suppressed.is_empty();
    if state.cleanup_pending { return false; }
    if state.map_renderer_gc != 0 {
        state.runtime.gc_free(state.map_renderer_gc);
        state.map_renderer_gc = 0;
    }
    for handle in &mut state.layer_gc {
        if *handle != 0 { state.runtime.gc_free(*handle); *handle = 0; }
    }
    state.map_id = 0;
    true
}

fn is_task_game_object(name: &str) -> bool {
    matches!(name, "System.Threading.Tasks.Task<UnityEngine.GameObject>"
        | "System.Threading.Tasks.Task`1<UnityEngine.GameObject>"
        | "System.Threading.Tasks.Task`1[UnityEngine.GameObject]")
}

fn start_prefab_load(state: &mut State) -> bool {
    if state.failed { return false; }
    if state.prefab_gc != 0 || state.load_task_gc != 0 { return true; }
    let result = (|| {
        let address = reflection::Root::new(state.runtime, state.runtime.string("tacticalCell"))?;
        let choice = reflection::boxed_int32(state.runtime, 0)?;
        let task = state.bindings.addressable_load.call(&[address.get(), choice.get()])?;
        let handle = state.runtime.gc_new(task, false);
        if handle == 0 { return Err("prefab Task GC root allocation failed".to_owned()); }
        Ok(handle)
    })();
    let task_gc = match result {
        Ok(handle) => handle,
        Err(error) => {
            state.failed = true;
            state.runtime.log(DnhLogLevel::Error, &format!(
                "Native Tactical: prefab load failed: {error}; no retry until the next map request."));
            return false;
        }
    };
    state.load_task_gc = task_gc;
    true
}

// Task.Result is called only after completion AND success. Never wait on the
// Unity thread, and never invoke a Task method on a boxed UniTask value.
fn completed_prefab(state: &State, task: DnhHandle) -> Option<Option<DnhGcHandleV4>> {
    let class = unsafe { (state.runtime.v2().get_object_class)(task) };
    let result = state.runtime.instance_method(class, c"get_Result", &[])?;
    if text(method_info(state.runtime, result)?.return_type_name) != "UnityEngine.GameObject" { return None; }
    let getters = state.task_getters.as_ref();
    if !task_flag(state, task, state.bindings.task_is_completed, getters.map(|g| &g.0))? { return Some(None); }
    if !task_flag(state, task, state.bindings.task_is_successful, getters.map(|g| &g.1))? { return None; }
    let prefab = state.runtime.invoke(result, task, &[])?;
    if !state.runtime.object_is(prefab, state.bindings.game_object_class) { return None; }
    let handle = state.runtime.gc_new(prefab, false);
    (handle != 0).then_some(Some(handle))
}

fn task_flag(state: &State, task: DnhHandle, bound: DnhHandle, getter: Option<&reflection::Getter>) -> Option<bool> {
    if !bound.is_null() { return state.runtime.unbox::<bool>(state.runtime.invoke(bound, task, &[])?); }
    getter?.bool_on(task).ok()
}

fn poll_prefab_load(state: &mut State) {
    if state.load_task_gc == 0 { return; }
    let task = state.runtime.gc_target(state.load_task_gc);
    if !task.is_null() && state.task_getters.is_none()
        && (state.bindings.task_is_completed.is_null() || state.bindings.task_is_successful.is_null()) {
        let class = unsafe { (state.runtime.v2().get_object_class)(task) };
        match (reflection::Getter::resolve(state.runtime, class, "get_IsCompleted"),
               reflection::Getter::resolve(state.runtime, class, "get_IsCompletedSuccessfully")) {
            (Ok(completed), Ok(successful)) => state.task_getters = Some((completed, successful)),
            (Err(error), _) | (_, Err(error)) => state.runtime.log(DnhLogLevel::Error,
                &format!("Native Tactical: Task completion getters unavailable: {error}")),
        }
    }
    let result = if task.is_null() { None } else { completed_prefab(state, task) };
    if matches!(result, Some(None)) { return; }
    state.runtime.gc_free(state.load_task_gc);
    state.load_task_gc = 0;
    if let Some(Some(prefab_gc)) = result {
        state.prefab_gc = prefab_gc;
        state.prefab_is_addressable = true;
        state.runtime.log(DnhLogLevel::Info,
            "Native Tactical: tacticalCell prefab acquired from completed Task<GameObject>.");
        return;
    }
    state.failed = true;
    state.runtime.log(DnhLogLevel::Error, "Native Tactical: prefab async operation failed or has an unsupported shape; left inactive.");
}

fn enable_current_map(state: &mut State, log_failure: bool) -> bool {
    if state.failed || state.cleanup_pending { return false; }
    if state.prefab_gc == 0 {
        return start_prefab_load(state);
    }
    let Some((renderer, normal_root, map_id, cells)) = try_resolve_map(state) else {
        if log_failure {
            state.runtime.log(
                DnhLogLevel::Warn,
                "Current map or its 560 tactical cells are not ready yet.",
            );
        }
        return false;
    };
    if !begin_overlay(state, renderer, normal_root, map_id, cells) {
        state.failed = true;
        state.runtime.log(
            DnhLogLevel::Error,
            "Could not begin the native tactical sprite overlay.",
        );
        return false;
    }
    true
}

fn toggle(state: &mut State) {
    if state.enabled {
        state.enabled = false;
        disable_current_map(state, true);
        state
            .runtime
            .log(DnhLogLevel::Info, "Native tactical mode disabled.");
    } else {
        state.enabled = true;
        state.failed = false;
        if !enable_current_map(state, true) {
            state.enabled = false;
        }
    }
}

fn follow_map_changes(state: &mut State, lease: lease::Lease) {
    if !state.enabled || state.failed { return; }
    let renderer = state.runtime.gc_target(state.map_renderer_gc);
    if state.map_id != 0 && !map_snapshot(state, renderer)
        .is_some_and(|(_, layers, id)| id == state.map_id && retained_layers(state, layers)) {
        disable_current_map(state, true);
    }
    if state.map_id != 0 || Instant::now() < state.next_map_probe { return; }
    state.next_map_probe = Instant::now() + Duration::from_millis(500);
    if !enable_current_map(state, false) && state.prefab_gc != 0 && !state.failed {
        let diagnosis = map_diagnosis(state);
        if diagnostic::changed(&mut state.last_map_diagnosis, &diagnosis) {
            state.runtime.log(DnhLogLevel::Warn, &format!("Native Tactical: map not usable yet: {diagnosis}."));
            diagnostic::capture(state, lease);
        }
    }
}

fn release_prefab(state: &mut State) -> bool {
    if state.load_task_gc != 0 {
        state.runtime.gc_free(state.load_task_gc);
        state.load_task_gc = 0;
    }
    if state.prefab_gc == 0 {
        return true;
    }
    let prefab = state.runtime.gc_target(state.prefab_gc);
    if state.prefab_is_addressable
        && !prefab.is_null()
        && let Err(error) = state.bindings.addressable_release.call(&[prefab])
    {
        state.runtime.log(DnhLogLevel::Error, &format!("Native Tactical: prefab release failed: {error}"));
        return false;
    }
    state.runtime.gc_free(state.prefab_gc);
    state.prefab_gc = 0;
    state.prefab_is_addressable = false;
    true
}

static MOD_ID: &[u8] = b"bubble.dofus3.native-tactical\0";
static MOD_NAME: &[u8] = b"Dofus 3 Native Tactical\0";
static MOD_VERSION: &[u8] = b"5.0.5\0";
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

#[link(name = "user32")]
unsafe extern "system" {
    fn GetAsyncKeyState(virtual_key: i32) -> i16;
    fn GetForegroundWindow() -> *mut std::ffi::c_void;
    fn GetWindowThreadProcessId(window: *mut std::ffi::c_void, process_id: *mut u32) -> u32;
}

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
/// host_api must remain valid until DNM_Unload.
pub unsafe extern "system" fn DNM_Load(host_api: *const DnhHostApiV1) -> i32 {
    let Some(runtime) = (unsafe { Runtime::bind(host_api) }) else { return DNH_ERROR; };
    let (Some(directory), Some(target_start)) = (lease::directory(), lease::process_start(std::process::id())) else { return DNH_ERROR; };
    let mut guard = MODULE.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    if guard.is_some() { return DNH_ERROR; }
    *guard = Some(Module { runtime, directory, target_start, last_lease: None, state: None,
        binding_failed: false, requested_enabled: false, acknowledged: String::new() });
    runtime.log(DnhLogLevel::Info, "Native Tactical: waiting for a fresh owned map lease; no managed calls on load.");
    DNH_OK
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Tick() {
    let mut guard = MODULE.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let Some(module) = guard.as_mut() else { return; };
    let Some(lease) = lease::read(&module.directory, module.target_start) else {
        module.last_lease = None;
        return; // No discovery, prefab polling, scene cleanup or F8 invocation off-map.
    };
    tick_lease(module, lease);
}

fn tick_lease(module: &mut Module, lease: lease::Lease) {
    if let Some(state) = module.state.as_mut()
        && state.cleanup_pending && !disable_current_map(state, true) {
        module.last_lease = None;
        return;
    }
    let requested_again = !module.requested_enabled && lease.enabled;
    module.requested_enabled = lease.enabled;
    let changed = module.last_lease.is_none_or(|old| !old.same_map(lease));
    module.last_lease = Some(lease);
    if lease.enabled && module.state.is_none() && !module.binding_failed {
        match resolve_bindings(module.runtime) {
            Ok(bindings) => {
            module.state = Some(State {
                runtime: module.runtime, bindings, enabled: true, f8_was_down: false,
                expected_map: lease.map_id, next_map_probe: Instant::now(), failed: false, cleanup_pending: false,
                map_id: 0, map_renderer_gc: 0, layer_gc: [0; 3], overlay_root_gc: 0, unparented_cell_gc: 0, prefab_gc: 0,
                prefab_is_addressable: false, load_task_gc: 0, build: None, suppressed: Vec::new(), task_getters: None, last_map_diagnosis: String::new(),
            });
            }
            Err(binding) => {
                module.binding_failed = true;
                module.runtime.log(DnhLogLevel::Error, &format!(
                    "Native Tactical: required binding failed: {binding}; module remains inactive."));
            }
        }
    }
    if let Some(state) = module.state.as_mut() {
        if requested_again {
            state.enabled = true;
            state.failed = false;
        }
        if changed {
            if !disable_current_map(state, true) {
                module.last_lease = None;
                return;
            }
            state.expected_map = lease.map_id;
            state.last_map_diagnosis.clear();
            state.failed = false;
            state.next_map_probe = Instant::now();
        }
        if !lease.enabled {
            state.enabled = false;
            if !disable_current_map(state, true) { return; }
            poll_prefab_load(state); // Drain a pending operation before allowing DLL unload.
            if state.load_task_gc != 0 { return; }
            if !release_prefab(state) { return; }
        } else {
            let foreground = unsafe { GetForegroundWindow() };
            let mut foreground_pid = 0;
            unsafe { GetWindowThreadProcessId(foreground, &mut foreground_pid); }
            let down = unsafe { GetAsyncKeyState(VK_F8) } < 0 && foreground_pid == std::process::id();
            if down && !state.f8_was_down { toggle(state); }
            state.f8_was_down = down;
            poll_prefab_load(state);
            follow_map_changes(state, lease);
            process_build(state);
        }
    }
    if !lease.enabled {
        let token = lease.token();
        if module.acknowledged != token && std::fs::write(module.directory.join(lease::ACK_FILE), &token).is_ok() {
            module.acknowledged = token;
        }
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_PrepareUnload() -> i32 {
    let Ok(guard) = MODULE.try_lock() else { return DNH_BUSY; };
    match guard.as_ref().and_then(|module| module.state.as_ref()) {
        Some(state) => prepare_state_unload(state),
        None => DNH_OK,
    }
}

fn prepare_state_unload(state: &State) -> i32 {
    // Native bookkeeping only: expired/revoked map authority cannot authorize
    // restoration here. A legal tick must finish it before physical unload.
    if state.cleanup_pending || state.build.is_some() || state.overlay_root_gc != 0
        || state.unparented_cell_gc != 0 || state.load_task_gc != 0
        || state.prefab_gc != 0 || !state.suppressed.is_empty() {
        DNH_BUSY
    } else {
        DNH_OK
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn DNM_Unload() {
    let mut guard = MODULE.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    if let Some(module) = guard.take() && let Some(state) = module.state {
        // The app waits for the matching cleanup ACK before unloading. Unexpected
        // host teardown must not invoke Unity using a revoked or expired lease.
        for handle in [state.map_renderer_gc, state.overlay_root_gc, state.unparented_cell_gc, state.prefab_gc, state.load_task_gc]
            .into_iter().chain(state.layer_gc).chain(state.suppressed.into_iter().map(|renderer| renderer.gc_handle)) {
            if handle != 0 { state.runtime.gc_free(handle); }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn retained_layers_reject_replacement_reordering_and_missing_roots() {
        let roots = [std::ptr::dangling_mut::<u8>().cast(), std::ptr::dangling_mut::<u16>().cast(),
            std::ptr::dangling_mut::<u32>().cast()];
        assert!(same_layers(roots, roots));
        for index in 0..3 {
            let mut changed = roots;
            changed[index] = std::ptr::dangling_mut::<u64>().cast();
            assert!(!same_layers(changed, roots));
            changed[index] = null_mut();
            assert!(!same_layers(changed, roots));
            assert!(!same_layers(roots, changed));
        }
        assert!(!same_layers([roots[1], roots[0], roots[2]], roots));
        assert!(!same_layers([null_mut(); 3], [null_mut(); 3]));
    }

    struct FieldFixture {
        is_static: bool,
        offset: i32,
        handle: DnhHandle,
    }

    unsafe extern "system" fn find_map_root_fields(
        class: DnhHandle,
        signature: *const DnhFieldSignatureV3,
        output: *mut DnhHandle,
        capacity: usize,
    ) -> usize {
        let fields = unsafe { &*class.cast::<Vec<FieldFixture>>() };
        let signature = unsafe { &*signature };
        if signature.struct_size != size_of::<DnhFieldSignatureV3>() as u32
            || text(signature.type_name) != "UnityEngine.GameObject"
        {
            return 0;
        }
        let mut count = 0;
        for field in fields {
            if signature.storage != if field.is_static { DNH_MEMBER_STORAGE_STATIC }
                else { dofus_native_mod_api::DNH_MEMBER_STORAGE_INSTANCE }
                || (signature.minimum_offset <= signature.maximum_offset
                    && !(signature.minimum_offset..=signature.maximum_offset).contains(&field.offset))
            {
                continue;
            }
            if count < capacity && !output.is_null() {
                unsafe { output.add(count).write(field.handle); }
            }
            count += 1;
        }
        count
    }

    #[test]
    fn map_root_resolves_static_storage_at_zero_offset() {
        let root = std::ptr::dangling_mut::<u8>().cast();
        let fields = vec![
            FieldFixture { is_static: false, offset: 280, handle: null_mut() },
            FieldFixture { is_static: true, offset: 0, handle: root },
        ];
        assert_eq!(resolve_map_root_field(find_map_root_fields, argument(&fields)), Ok(root));
    }

    #[test]
    fn map_root_rejects_missing_ambiguous_and_null_static_bindings() {
        let root = std::ptr::dangling_mut::<u8>().cast();
        for fields in [
            vec![],
            vec![FieldFixture { is_static: true, offset: 0, handle: null_mut() }],
            vec![FieldFixture { is_static: true, offset: 0, handle: root },
                FieldFixture { is_static: true, offset: 8, handle: root }],
        ] {
            assert!(resolve_map_root_field(find_map_root_fields, argument(&fields)).is_err());
        }
    }

    #[test]
    fn wi_parameter_accepts_qualified_names_only_for_that_type() {
        for name in ["wi", "Core.wi", "A.B/wi", "A+wi"] { assert!(is_wi_type_name(name)); }
        for name in ["System.Int32", "kiwi", "wi2", ""] { assert!(!is_wi_type_name(name)); }
    }

    #[test]
    fn only_the_verified_inflated_task_shape_is_invoked() {
        assert!(is_task_game_object("System.Threading.Tasks.Task<UnityEngine.GameObject>"));
        assert!(is_task_game_object("System.Threading.Tasks.Task`1<UnityEngine.GameObject>"));
        assert!(is_task_game_object("System.Threading.Tasks.Task`1[UnityEngine.GameObject]"));
        for other in ["Cysharp.Threading.Tasks.UniTask<UnityEngine.GameObject>", "System.Threading.Tasks.Task<T>",
            "System.Threading.Tasks.Task<UnityEngine.Object>", "System.Threading.Tasks.Task`1[T]",
            "System.Threading.Tasks.Task`1[UnityEngine.Object]", "System.Object", ""] {
            assert!(!is_task_game_object(other));
        }
    }
}
