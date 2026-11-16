use super::*;
use dofus_native_mod_api::*;
use std::cell::RefCell;
use std::collections::BTreeMap;

#[derive(Default)]
struct Fixture {
    roots: BTreeMap<usize, usize>,
    next: usize,
    fail: usize,
    alive: bool,
    restored: Vec<bool>,
    destroyed: bool,
    released: bool,
    array: Vec<DnhHandle>,
}
thread_local! { static FIXTURE: RefCell<Fixture> = RefCell::new(Fixture::default()); }
static ALIVE: bool = true;
static DEAD: bool = false;
fn handle(id: usize) -> DnhHandle { std::ptr::without_provenance_mut(id) }
macro_rules! stub {
    ($name:ident($($arg:ident: $ty:ty),*) -> $ret:ty) => {
        unsafe extern "system" fn $name($($arg: $ty),*) -> $ret { $(let _ = $arg;)* Default::default() }
    };
}
stub!(ready() -> bool);
stub!(attach() -> DnhHandle);
stub!(detach(a: DnhHandle) -> ());
unsafe extern "system" fn class(_: *const c_char, _: *const c_char, _: *const c_char) -> DnhHandle { handle(60) }
stub!(field(a: DnhHandle, b: *const c_char) -> DnhHandle);
stub!(method(a: DnhHandle, b: *const c_char, c: i32) -> DnhHandle);
stub!(offset(a: DnhHandle) -> i32);
stub!(unary(a: DnhHandle) -> DnhHandle);
stub!(members(a: DnhHandle, b: *mut DnhHandle, c: usize) -> usize);
stub!(classes(a: *const c_char, b: *mut DnhHandle, c: usize) -> usize);
stub!(class_info(a: DnhHandle, b: *mut DnhClassInfoV2) -> bool);
stub!(field_info(a: DnhHandle, b: *mut DnhFieldInfoV2) -> bool);
stub!(method_info(a: DnhHandle, b: *mut DnhMethodInfoV2) -> bool);
stub!(parameter(a: DnhHandle, b: u32) -> *const c_char);
stub!(string(a: *const c_char) -> DnhHandle);
unsafe extern "system" fn length(_: DnhHandle) -> usize {
    FIXTURE.with_borrow(|f| f.array.len())
}
stub!(value_size(a: DnhHandle, b: *mut u32) -> i32);
stub!(is_value(a: DnhHandle) -> bool);
unsafe extern "system" fn field_get(_: DnhHandle, field: DnhHandle, value: *mut std::ffi::c_void) -> bool {
    if field.addr() != 15 { return false; }
    // SAFETY: serialized_sprites supplies a writable, aligned DnhHandle slot.
    unsafe { value.cast::<DnhHandle>().write(handle(63)); }
    true
}
stub!(field_set(a: DnhHandle, b: DnhHandle, c: *const std::ffi::c_void) -> bool);
stub!(field_object(a: DnhHandle, b: DnhHandle) -> DnhHandle);
stub!(static_get(a: DnhHandle, b: *mut std::ffi::c_void) -> bool);
stub!(static_set(a: DnhHandle, b: *const std::ffi::c_void) -> bool);
stub!(gc32(a: DnhHandle, b: bool) -> u32);
stub!(target32(a: u32) -> DnhHandle);
stub!(free32(a: u32) -> ());
stub!(icall(a: *const c_char) -> DnhHandle);
stub!(fields(a: DnhHandle, b: *const DnhFieldSignatureV3, c: *mut DnhHandle, d: usize) -> usize);
stub!(methods(a: DnhHandle, b: *const DnhMethodSignatureV3, c: *mut DnhHandle, d: usize) -> usize);
stub!(find_classes(a: *const c_char, b: *const DnhClassSignatureV3, c: *mut DnhHandle, d: usize) -> usize);
stub!(log(a: DnhLogLevel, b: *const u8, c: usize) -> ());
unsafe extern "system" fn gc_new(object: DnhHandle, _: bool) -> usize {
    FIXTURE.with_borrow_mut(|f| { f.next += 1; f.roots.insert(f.next, object.addr()); f.next })
}
unsafe extern "system" fn gc_target(root: usize) -> DnhHandle {
    FIXTURE.with_borrow(|f| handle(f.roots.get(&root).copied().unwrap_or(0)))
}
unsafe extern "system" fn gc_free(root: usize) { FIXTURE.with_borrow_mut(|f| { f.roots.remove(&root); }); }
unsafe extern "system" fn array_new(_: DnhHandle, len: usize) -> DnhHandle {
    FIXTURE.with_borrow_mut(|f| f.array = vec![null_mut(); len]); handle(80)
}
unsafe extern "system" fn array_data(_: DnhHandle) -> DnhHandle {
    FIXTURE.with_borrow_mut(|f| f.array.as_mut_ptr().cast())
}
unsafe extern "system" fn identity(value: DnhHandle) -> DnhHandle { value }
unsafe extern "system" fn invoke(method: DnhHandle, _: DnhHandle, args: *const DnhHandle, exception: *mut DnhHandle) -> DnhHandle {
    FIXTURE.with_borrow_mut(|f| {
        if f.fail == method.addr() {
            // SAFETY: Runtime supplies a writable exception slot for each invocation.
            unsafe { exception.write(handle(99)); }
            return null_mut();
        }
        match method.addr() {
            1 => argument(if f.alive { &ALIVE } else { &DEAD }),
            2 => { f.destroyed = true; null_mut() }
            3 => {
                // SAFETY: renderer_set_enabled receives one pointer to a live bool.
                let value = unsafe { args.read() };
                // SAFETY: the production setter passes argument(&was_enabled).
                f.restored.push(unsafe { value.cast::<bool>().read() });
                null_mut()
            }
            4 => { f.released = true; null_mut() }
            5 => handle(25),
            6 => handle(61),
            9 => handle(62),
            13 => handle(63),
            17 => handle(80),
            _ => null_mut(),
        }
    })
}
fn runtime(host: &DnhHostApiV4) -> Runtime {
    // SAFETY: the fixture owns initialized ABI v4 tables until all State values drop.
    unsafe { Runtime::bind(std::ptr::from_ref(host).cast()).unwrap() }
}
fn unity() -> DnhUnityApiV4 {
    DnhUnityApiV4 { v3: DnhUnityApiV3 { v2: DnhUnityApiV2 {
        abi_version: DNH_ABI_VERSION_4, struct_size: u32::try_from(size_of::<DnhUnityApiV2>()).unwrap(),
        is_ready: ready, thread_attach: attach, thread_detach: detach, get_class: class, get_field: field,
        get_method: method, field_offset: offset, method_address: unary, find_objects: members,
        get_classes: classes, get_object_class: unary, class_type_object: identity, copy_class_info: class_info,
        get_class_fields: members, get_class_methods: members, copy_field_info: field_info, copy_method_info: method_info,
        method_parameter_type_name: parameter, runtime_invoke: invoke, object_unbox: identity, object_new: unary,
        string_new_utf8: string, array_new, array_length: length, array_data, class_value_size: value_size,
        class_is_value_type: is_value, field_get_value: field_get, field_set_value: field_set,
        field_get_value_object: field_object, field_get_static_value: static_get, field_set_static_value: static_set,
        gc_handle_new: gc32, gc_handle_target: target32, gc_handle_free: free32, resolve_icall: icall,
    }, find_fields_by_signature: fields, find_methods_by_signature: methods, find_classes_by_signature: find_classes },
        gc_handle_new_v4: gc_new, gc_handle_target_v4: gc_target, gc_handle_free_v4: gc_free }
}
fn state(runtime: Runtime) -> State {
    let mut bindings = Bindings {
        map_renderer_class: handle(90),
        game_object_class: handle(90),
        transform_class: handle(90),
        renderer_class: handle(90),
        sprite_renderer_class: handle(90),
        tactical_cell_class: handle(90),
        map_accessor_class: handle(90),
        map_accessor_field: handle(90),
        map_data_field: handle(90),
        map_root_field: handle(90),
        game_object_ctor: handle(90),
        game_object_find: handle(90),
        game_object_get_transform: handle(90),
        game_object_get_component: handle(90),
        game_object_get_components_in_children: handle(90),
        game_object_set_active: handle(90),
        component_get_game_object: handle(90),
        transform_get_parent: handle(90),
        transform_set_parent: handle(90),
        transform_get_local_position: handle(90),
        transform_set_local_position: handle(90),
        transform_get_local_rotation: handle(90),
        transform_set_local_rotation: handle(90),
        transform_get_local_scale: handle(90),
        transform_set_local_scale: handle(90),
        object_instantiate: handle(90),
        object_destroy: handle(90),
        object_is_alive: handle(90),
        object_set_name: handle(90),
        renderer_get_enabled: handle(90),
        renderer_set_enabled: handle(90),
        sprite_renderer_get_sprite: handle(90),
        sprite_renderer_set_sprite: handle(90),
        tactical_set_cell_id: handle(90),
        tactical_apply_kind: handle(90),
        task_is_completed: handle(90),
        task_is_successful: handle(90),
        tactical_sprite_fields: Vec::new(),
        addressable_load: reflection::Method::fixture(runtime, handle(50), handle(4)),
        addressable_release: reflection::Method::fixture(runtime, handle(51), handle(4)),
    };
    bindings.object_is_alive = handle(1);
    bindings.object_destroy = handle(2);
    bindings.renderer_set_enabled = handle(3);
    State { runtime, bindings, enabled: true, f8_was_down: false, expected_map: 1234,
        next_map_probe: Instant::now(), failed: false, cleanup_pending: false, map_id: 1234,
        map_renderer_gc: runtime.gc_new(handle(20), false), layer_gc: [0; 3],
        overlay_root_gc: runtime.gc_new(handle(21), false), unparented_cell_gc: 0, prefab_gc: runtime.gc_new(handle(22), false),
        prefab_is_addressable: true, load_task_gc: 0, build: None,
        suppressed: vec![SuppressedRenderer { gc_handle: runtime.gc_new(handle(23), false), was_enabled: true },
            SuppressedRenderer { gc_handle: runtime.gc_new(handle(24), false), was_enabled: false }],
        task_getters: None, last_map_diagnosis: String::new() }
}

#[test]
fn cleanup_ack_requires_successful_restore_destroy_liveness_and_release() {
    static SERIAL: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
    for failure in [1, 2, 3, 4] {
        FIXTURE.with_borrow_mut(|f| *f = Fixture { fail: failure, alive: true, ..Fixture::default() });
        let unity = unity();
        let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
            struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
        let runtime = runtime(&host);
        let state = state(runtime);
        let overlay = state.overlay_root_gc;
        let prefab = state.prefab_gc;
        let renderer = state.map_renderer_gc;
        let suppressed = state.suppressed.iter().map(|s| s.gc_handle).collect::<Vec<_>>();
        let directory = std::env::temp_dir().join(format!("tactical-cleanup-{}-{}", std::process::id(),
            SERIAL.fetch_add(1, std::sync::atomic::Ordering::Relaxed)));
        std::fs::create_dir(&directory).unwrap();
        let lease = lease::Lease { target_start: 100, owner_pid: 42, owner_start: 200, epoch: 3,
            map_id: 1234, generation: 10, enabled: false, expires: 4000 };
        let mut module = Module { runtime, directory: directory.clone(), target_start: 100,
            last_lease: Some(lease), state: Some(state), binding_failed: false, requested_enabled: true,
            acknowledged: String::new() };

        tick_lease(&mut module, lease);

        assert!(!directory.join(lease::ACK_FILE).exists(), "failure {failure} falsely acknowledged");
        let state = module.state.as_ref().unwrap();
        assert_eq!(state.prefab_gc, prefab);
        if failure != 4 {
            assert_eq!(state.map_id, 1234);
            assert_eq!(state.map_renderer_gc, renderer);
            assert!(state.cleanup_pending);
        }
        FIXTURE.with_borrow(|f| {
            assert!(f.roots.contains_key(&prefab));
            if failure == 1 || failure == 2 { assert!(f.roots.contains_key(&overlay)); }
            if failure == 1 || failure == 3 {
                for root in &suppressed { assert!(f.roots.contains_key(root)); }
            }
        });
        FIXTURE.with_borrow_mut(|f| f.fail = 0);

        tick_lease(&mut module, lease);

        assert_eq!(std::fs::read_to_string(directory.join(lease::ACK_FILE)).unwrap(), lease.token());
        let state = module.state.as_ref().unwrap();
        assert_eq!(state.map_id, 0);
        assert_eq!(state.overlay_root_gc, 0);
        assert_eq!(state.prefab_gc, 0);
        assert!(state.suppressed.is_empty());
        FIXTURE.with_borrow(|f| {
            assert!(f.destroyed && f.released);
            assert_eq!(f.restored, [true, false]);
            assert!(!f.roots.contains_key(&renderer));
        });
        drop(module);
        std::fs::remove_dir_all(directory).unwrap();
    }
}

#[test]
fn unload_waits_for_scene_restoration_and_prefab_release() {
    FIXTURE.with_borrow_mut(|f| *f = Fixture { alive: true, ..Fixture::default() });
    let unity = unity();
    let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
        struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
    let runtime = runtime(&host);
    let mut state = state(runtime);

    assert_eq!(prepare_state_unload(&state), DNH_BUSY);
    FIXTURE.with_borrow(|f| assert!(!f.destroyed && f.restored.is_empty() && !f.released));
    assert!(disable_current_map(&mut state, true));
    assert_eq!(prepare_state_unload(&state), DNH_BUSY);
    assert!(release_prefab(&mut state));
    assert_eq!(prepare_state_unload(&state), DNH_OK);

    state.load_task_gc = runtime.gc_new(handle(30), false);
    assert_eq!(prepare_state_unload(&state), DNH_BUSY);
    runtime.gc_free(state.load_task_gc);
}

#[test]
fn destroyed_unity_objects_release_roots_without_setter_or_destroy() {
    FIXTURE.with_borrow_mut(|f| *f = Fixture::default());
    let unity = unity();
    let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
        struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
    let runtime = runtime(&host);
    let mut state = state(runtime);

    assert!(disable_current_map(&mut state, true));

    assert_eq!(state.map_id, 0);
    assert!(state.suppressed.is_empty());
    FIXTURE.with_borrow(|f| {
        assert!(!f.destroyed);
        assert!(f.restored.is_empty());
    });
}

#[test]
fn unparented_cell_blocks_cleanup_until_destroy_succeeds() {
    FIXTURE.with_borrow_mut(|f| *f = Fixture { fail: 7, alive: true, ..Fixture::default() });
    let unity = unity();
    let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
        struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
    let runtime = runtime(&host);
    let mut state = state(runtime);
    state.bindings.object_instantiate = handle(5);
    state.bindings.game_object_get_transform = handle(6);
    state.bindings.transform_set_parent = handle(7);

    assert!(build_cell(&mut state, handle(21), handle(22), 0, CellKind::Walkable).is_none());
    FIXTURE.with_borrow_mut(|f| f.fail = 2);

    assert!(!disable_current_map(&mut state, true));
    FIXTURE.with_borrow(|f| assert!(f.roots.values().any(|object| *object == 25)));
    FIXTURE.with_borrow_mut(|f| f.fail = 0);
    assert!(disable_current_map(&mut state, true));
    FIXTURE.with_borrow(|f| assert!(!f.roots.values().any(|object| *object == 25)));
}

#[test]
fn failed_cell_configuration_never_counts_as_constructed() {
    for failure in [0, 7, 8, 10, 11, 12, 14, 16] {
        FIXTURE.with_borrow_mut(|f| *f = Fixture { fail: failure, alive: true, ..Fixture::default() });
        let unity = unity();
        let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
            struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
        let runtime = runtime(&host);
        let mut state = state(runtime);
        state.bindings.object_instantiate = handle(5);
        state.bindings.game_object_get_transform = handle(6);
        state.bindings.transform_set_parent = handle(7);
        state.bindings.transform_set_local_position = handle(8);
        state.bindings.game_object_get_component = handle(9);
        state.bindings.tactical_set_cell_id = handle(10);
        state.bindings.tactical_apply_kind = handle(11);
        state.bindings.renderer_set_enabled = handle(12);
        state.bindings.sprite_renderer_get_sprite = handle(13);
        state.bindings.sprite_renderer_set_sprite = handle(14);
        state.bindings.tactical_sprite_fields = vec![handle(15)];
        state.bindings.game_object_set_active = handle(16);

        assert_eq!(build_cell(&mut state, handle(21), handle(22), 0, CellKind::Walkable).is_some(),
            failure == 0, "cell construction result with setter failure {failure}");
        FIXTURE.with_borrow_mut(|f| f.fail = 0);
        assert!(disable_current_map(&mut state, true));
    }
}

#[test]
fn suppression_refuses_activation_when_batch_ownership_is_unavailable() {
    FIXTURE.with_borrow_mut(|f| *f = Fixture { alive: true,
        array: vec![handle(23), handle(24)], ..Fixture::default() });
    let unity = unity();
    let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
        struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
    let runtime = runtime(&host);
    let mut state = state(runtime);
    state.suppressed.clear();
    state.bindings.game_object_get_components_in_children = handle(17);
    state.bindings.renderer_get_enabled = handle(1);

    assert!(!suppress_map(&mut state, [handle(21); 3]));

    assert!(state.suppressed.is_empty());
    FIXTURE.with_borrow(|f| assert!(f.restored.is_empty()));
}

#[test]
fn suppression_leaves_unclassified_renderers_unchanged() {
    FIXTURE.with_borrow_mut(|f| *f = Fixture { alive: true,
        array: vec![handle(23), handle(24)], ..Fixture::default() });
    let unity = unity();
    let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
        struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
    let runtime = runtime(&host);
    let mut state = state(runtime);
    state.suppressed.clear();
    state.bindings.game_object_get_components_in_children = handle(17);
    state.bindings.renderer_get_enabled = handle(1);

    assert!(suppress_branch(&mut state, handle(21), &[handle(23)]));

    FIXTURE.with_borrow(|f| assert_eq!(f.restored, [false]));
    assert_eq!(state.suppressed.len(), 1);
    assert_eq!(runtime.gc_target(state.suppressed[0].gc_handle), handle(23));
}

#[test]
fn suppression_failure_keeps_original_state_for_restoration() {
    for failure in [1, 3, 17] {
        FIXTURE.with_borrow_mut(|f| *f = Fixture { fail: failure, alive: true,
            array: vec![handle(23)], ..Fixture::default() });
        let unity = unity();
        let host = DnhHostApiV4 { abi_version: DNH_ABI_VERSION_4,
            struct_size: u32::try_from(size_of::<DnhHostApiV4>()).unwrap(), log, unity: &unity };
        let runtime = runtime(&host);
        let mut state = state(runtime);
        state.suppressed.clear();
        state.bindings.game_object_get_components_in_children = handle(17);
        state.bindings.renderer_get_enabled = handle(1);

        assert!(!suppress_branch(&mut state, handle(21), &[handle(23)]),
            "suppression falsely succeeded with method failure {failure}");

        assert_eq!(state.suppressed.len(), usize::from(failure == 3));
        FIXTURE.with_borrow_mut(|f| f.fail = 0);
        assert!(disable_current_map(&mut state, true));
        assert!(state.suppressed.is_empty());
        FIXTURE.with_borrow(|f| assert_eq!(f.restored, if failure == 3 { vec![true] } else { vec![] }));
    }
}

