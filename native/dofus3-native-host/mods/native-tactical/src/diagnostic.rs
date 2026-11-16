//! Read-only evidence and exact typed instance-reference reads shared with ownership discovery.
use super::*;

const MAX_ANCESTORS: usize = 8;
const MAX_CHILDREN: i32 = 12;
const MAX_RENDERERS: usize = 128;
const MAX_RENDERER_PATHS: usize = 24;
const MAX_NAME_CHARS: usize = 96;
const MAX_FIELD_OBJECTS: usize = 12;
const MAX_FIELDS: usize = 512;
const MAX_FIELDS_PER_CLASS: usize = 48;
const MAX_FIELD_CLASSES: usize = 3;
const MAX_FIELD_DEPTH: usize = 2;

fn field_capacity(total: usize, inspected: usize) -> usize {
    total.min(MAX_FIELDS_PER_CLASS).min(MAX_FIELDS.saturating_sub(inspected))
}

pub(super) fn changed(previous: &mut String, current: &str) -> bool {
    if previous == current { return false; }
    *previous = current.to_owned();
    true
}

fn label(name: &str) -> String {
    name.chars().take(MAX_NAME_CHARS)
        .map(|c| if c.is_control() { ' ' } else { c }).collect()
}

// Object-valued field reads and invocations return raw IL2CPP references in
// unity_bridge.cpp, unlike class/field/method metadata wrapper handles.
fn same_reference(left: DnhHandle, right: DnhHandle) -> Option<bool> {
    (!left.is_null() && !right.is_null()).then_some(left == right)
}

fn exact_reference_signature(is_static: u8, name: &str, ty: &str, expected_name: &CStr, expected_type: &str) -> bool {
    is_static == 0 && name == expected_name.to_string_lossy() && ty == expected_type
}

// No property fallback: require the pinned instance-field signature before
// asking the host to copy a reference into a correctly sized output slot.
pub(super) fn reference_field(runtime: Runtime, object: DnhHandle, name: &CStr, ty: &str) -> Option<DnhHandle> {
    if object.is_null() { return None; }
    // SAFETY: object is a non-null IL2CPP reference supplied by the host on its owning thread.
    let class = unsafe { (runtime.v2().get_object_class)(object) };
    if class.is_null() { return None; }
    // SAFETY: class is a host metadata handle and name is NUL-terminated.
    let field = unsafe { (runtime.v2().get_field)(class, name.as_ptr()) };
    let mut info = dofus_native_mod_api::DnhFieldInfoV2 {
        struct_size: size_of::<dofus_native_mod_api::DnhFieldInfoV2>() as u32,
        name: null(), type_name: null(), offset: 0, is_static: 0, reserved: [0; 3],
    };
    // SAFETY: the host validates the initialized output size before copying metadata.
    if field.is_null() || !unsafe { (runtime.v2().copy_field_info)(field, &mut info) }
        || !exact_reference_signature(info.is_static, &text(info.name), &text(info.type_name), name, ty) { return None; }
    let mut value: DnhHandle = null_mut();
    // SAFETY: exact instance reference type verified above; output holds one reference.
    (unsafe { (runtime.v2().field_get_value)(object, field, (&mut value as *mut DnhHandle).cast()) }
        && !value.is_null()).then_some(value)
}

struct Reader<'a> {
    state: &'a State,
    get_name: Option<DnhHandle>,
    child_count: Option<DnhHandle>,
    get_child: Option<DnhHandle>,
    active: Option<DnhHandle>,
    position: Option<DnhHandle>,
    rotation: Option<DnhHandle>,
    scale: Option<DnhHandle>,
}

impl Reader<'_> {
    fn object_transform(&self, object: DnhHandle) -> Option<DnhHandle> {
        let runtime = self.state.runtime;
        let b = &self.state.bindings;
        let go = if runtime.object_is(object, b.game_object_class) {
            Some(object)
        } else {
            let component = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Component");
            if !runtime.object_is(object, component) { return None; }
            runtime.invoke(b.component_get_game_object, object, &[])
        }?;
        runtime.invoke(b.game_object_get_transform, go, &[]).filter(|t| !t.is_null())
    }

    fn describe_object(&self, object: DnhHandle, prefix: &str) {
        if let Some(t) = self.object_transform(object) {
            self.describe(t, prefix);
            let mut current = Some(t);
            let mut path = Vec::new();
            for _ in 0..MAX_ANCESTORS {
                let Some(node) = current else { break; };
                path.push(self.name(node));
                current = self.parent(node);
            }
            path.reverse();
            self.log(&format!("{prefix} ancestors={path:?} ancestors_truncated={}", current.is_some()));
        } else {
            self.log(&format!("{prefix} transform unavailable"));
        }
    }

    fn name(&self, object: DnhHandle) -> String {
        self.get_name.and_then(|m| self.state.runtime.invoke(m, object, &[]))
            .and_then(|s| self.state.runtime.managed_string(s))
            .map(|s| label(&s)).unwrap_or_else(|| "<name unavailable>".to_owned())
    }

    fn vector(&self, transform: DnhHandle, method: Option<DnhHandle>) -> String {
        method.and_then(|m| self.state.runtime.invoke(m, transform, &[]))
            .and_then(|v| self.state.runtime.unbox::<Vector3>(v))
            .map(|v| format!("({:.3},{:.3},{:.3})", v.x, v.y, v.z))
            .unwrap_or_else(|| "unavailable".to_owned())
    }

    fn quaternion(&self, transform: DnhHandle, method: Option<DnhHandle>) -> String {
        method.and_then(|m| self.state.runtime.invoke(m, transform, &[]))
            .and_then(|v| self.state.runtime.unbox::<Quaternion>(v))
            .map(|v| format!("({:.3},{:.3},{:.3},{:.3})", v.x, v.y, v.z, v.w))
            .unwrap_or_else(|| "unavailable".to_owned())
    }

    fn describe(&self, transform: DnhHandle, prefix: &str) {
        let b = &self.state.bindings;
        let children = self.child_count.and_then(|m| self.state.runtime.invoke(m, transform, &[]))
            .and_then(|v| self.state.runtime.unbox::<i32>(v));
        let mut names = Vec::new();
        if let (Some(count), Some(get_child)) = (children, self.get_child) {
            for index in 0..count.clamp(0, MAX_CHILDREN) {
                if let Some(child) = self.state.runtime.invoke(get_child, transform, &[argument(&index)])
                    && !child.is_null() {
                    names.push(self.name(child));
                }
            }
        }
        self.log(&format!("{prefix} name={:?} children={children:?} first_children={names:?} local_position={} local_rotation={} local_scale={} world_position={} world_rotation={} world_scale={}",
            self.name(transform),
            self.vector(transform, Some(b.transform_get_local_position)),
            self.quaternion(transform, Some(b.transform_get_local_rotation)),
            self.vector(transform, Some(b.transform_get_local_scale)),
            self.vector(transform, self.position), self.quaternion(transform, self.rotation),
            self.vector(transform, self.scale)));
    }

    fn parent(&self, transform: DnhHandle) -> Option<DnhHandle> {
        self.state.runtime.invoke(self.state.bindings.transform_get_parent, transform, &[])
            .filter(|p| !p.is_null())
    }

    fn log(&self, message: &str) {
        self.state.runtime.log(DnhLogLevel::Info, &format!("Native Tactical diagnostic: {message}"));
    }
}

fn owned_branches(reader: &Reader<'_>, accessor: DnhHandle, controller: DnhHandle,
    background: DnhHandle, permitted: &impl Fn() -> bool) -> bool {
    let runtime = reader.state.runtime;
    if !permitted() { reader.log("ABORT lease revoked"); return false; }
    reader.log("OwnedBranches BEGIN references_not_ownership_proof=true");
    let owner = reference_field(runtime, accessor, c"doqa", "ue").unwrap_or(null_mut());
    reader.log(&format!("OwnedBranches accessor.doqa class={:?}", label(&runtime.object_class_name(owner))));
    if runtime.object_class_name(owner) != "ue" {
        reader.log("OwnedBranches unavailable: accessor.doqa requires runtime ue; END");
        return true;
    }
    let backlink = reference_field(runtime, owner, c"dgjn", "Core.Rendering.Map.GameObjects.MapController")
        .filter(|value| runtime.object_is(*value, reader.state.bindings.map_renderer_class))
        .unwrap_or(null_mut());
    reader.log(&format!("OwnedBranches ue.dgjn class={:?} unavailable={} same_reference_as_current_controller={:?}",
        label(&runtime.object_class_name(backlink)), backlink.is_null(), same_reference(backlink, controller)));
    let branches = reference_field(runtime, owner, c"dgjp", "ub").unwrap_or(null_mut());
    reader.log(&format!("OwnedBranches ue.dgjp class={:?}", label(&runtime.object_class_name(branches))));
    let root = reference_field(runtime, owner, c"dgjm", "UnityEngine.GameObject").unwrap_or(null_mut());
    let transform_class = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Transform");
    let background_transform = reader.object_transform(background).unwrap_or(null_mut());
    let describe = |value: DnhHandle, prefix: &str, class: DnhHandle| {
        reader.log(&format!("{prefix} class={:?}", label(&runtime.object_class_name(value))));
        if runtime.object_is(value, class) {
            let transform = reader.object_transform(value).unwrap_or(null_mut());
            reader.log(&format!("{prefix} same_transform_as_named_background={:?}", same_reference(transform, background_transform)));
            let mut ancestor = Some(transform).filter(|t| !t.is_null());
            let mut relation = None;
            for _ in 0..MAX_ANCESTORS {
                let Some(node) = ancestor else { break; };
                if let Some(equal) = same_reference(node, background_transform) {
                    relation = Some(equal);
                    if equal { break; }
                }
                ancestor = reader.parent(node);
            }
            reader.log(&format!("{prefix} named_background_in_ancestry={relation:?} ancestry_truncated={}",
                ancestor.is_some() && relation != Some(true)));
            reader.describe_object(value, prefix);
        } else { reader.log(&format!("{prefix} unavailable: required Unity runtime type")); }
    };
    if !permitted() { reader.log("ABORT lease revoked"); return false; }
    describe(root, "OwnedBranches ue.dgjm", reader.state.bindings.game_object_class);
    if runtime.object_class_name(branches) != "ub" {
        reader.log("OwnedBranches unavailable: ue.dgjp requires runtime ub; END");
        return true;
    }
    for (layer, go_field, transform_field) in [
        ("0", c"dgih", c"dgii"), ("1", c"dgij", c"dgik"),
        ("2", c"dgil", c"dgim"), ("unproven", c"dgin", c"dgio"),
    ] {
        if !permitted() { reader.log("ABORT lease revoked"); return false; }
        let go = reference_field(runtime, branches, go_field, "UnityEngine.GameObject")
            .filter(|value| runtime.object_is(*value, reader.state.bindings.game_object_class)).unwrap_or(null_mut());
        let transform = reference_field(runtime, branches, transform_field, "UnityEngine.Transform")
            .filter(|value| runtime.object_is(*value, transform_class)).unwrap_or(null_mut());
        let prefix = format!("OwnedBranches ub.{}/{} layer={layer}", go_field.to_string_lossy(), transform_field.to_string_lossy());
        let go_transform = reader.object_transform(go).unwrap_or(null_mut());
        reader.log(&format!("{prefix} game_object_transform_equals_stored_transform={:?}", same_reference(go_transform, transform)));
        describe(go, &format!("{prefix} GameObject"), reader.state.bindings.game_object_class);
        if !permitted() { reader.log("ABORT lease revoked"); return false; }
        describe(transform, &format!("{prefix} Transform"), transform_class);
    }
    reader.log("OwnedBranches END references_not_ownership_proof=true");
    true
}

// Read fields, not arbitrary property getters or coordinate conversion methods.
// Object IDs are capture-local reference equality, not Unity ownership claims.
fn fields(reader: &Reader<'_>, seeds: Vec<(DnhHandle, String)>, background: DnhHandle,
    permitted: &impl Fn() -> bool) -> bool {
    let runtime = reader.state.runtime;
    let unity_object = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Object");
    let mut objects: Vec<(DnhHandle, String, usize)> = Vec::new();
    let mut roots = Vec::new();
    for (object, source) in seeds {
        if !object.is_null() && !objects.iter().any(|(o, _, _)| *o == object)
            && let Ok(root) = reflection::Root::new(runtime, object) {
            roots.push(root);
            objects.push((object, source, 0));
        }
    }
    let mut inspected = 0;
    let mut visited = 0;
    while visited < objects.len().min(MAX_FIELD_OBJECTS) && inspected < MAX_FIELDS {
        if !permitted() { reader.log("ABORT lease revoked"); return false; }
        let (object, source, depth) = objects[visited].clone();
        reader.log(&format!("FieldObject[{visited}] source={source:?} class={:?} depth={depth}", label(&runtime.object_class_name(object))));
        let mut class = unsafe { (runtime.v2().get_object_class)(object) };
        for _ in 0..MAX_FIELD_CLASSES {
            if class.is_null() || inspected == MAX_FIELDS { break; }
            let total = unsafe { (runtime.v2().get_class_fields)(class, null_mut(), 0) };
            let mut handles = vec![null_mut(); field_capacity(total, inspected)];
            if !handles.is_empty() {
                unsafe { (runtime.v2().get_class_fields)(class, handles.as_mut_ptr(), handles.len()); }
            }
            reader.log(&format!("FieldObject[{visited}] declared_fields={total} sampled={}", handles.len()));
            for field in handles {
                if !permitted() { reader.log("ABORT lease revoked"); return false; }
                inspected += 1;
                let mut info = dofus_native_mod_api::DnhFieldInfoV2 {
                    struct_size: size_of::<dofus_native_mod_api::DnhFieldInfoV2>() as u32,
                    name: null(), type_name: null(), offset: 0, is_static: 0, reserved: [0; 3],
                };
                if field.is_null() || !unsafe { (runtime.v2().copy_field_info)(field, &mut info) } { continue; }
                let name = label(&text(info.name));
                let ty = text(info.type_name);
                // The host boxes value types and returns references unchanged; no raw
                // field-size assumptions are made. Static reads use a null instance.
                let value = unsafe { (runtime.v2().field_get_value_object)(
                    if info.is_static != 0 { null_mut() } else { object }, field) };
                let prefix = format!("FieldObject[{visited}] field={name:?} type={:?} static={}", label(&ty), info.is_static != 0);
                if value.is_null() { reader.log(&format!("{prefix} value=null_or_unavailable")); continue; }
                let scalar = match ty.as_str() {
                    "System.Int32" => runtime.unbox::<i32>(value).map(|v| v.to_string()),
                    "System.UInt32" => runtime.unbox::<u32>(value).map(|v| v.to_string()),
                    "System.Single" => runtime.unbox::<f32>(value).map(|v| v.to_string()),
                    "UnityEngine.Vector2" => runtime.unbox::<[f32; 2]>(value).map(|v| format!("({},{})", v[0], v[1])),
                    "UnityEngine.Vector3" => runtime.unbox::<Vector3>(value).map(|v| format!("({},{},{})", v.x, v.y, v.z)),
                    _ => None,
                };
                if let Some(value) = scalar { reader.log(&format!("{prefix} value={value}")); continue; }
                if runtime.object_is(value, unity_object) {
                    let same_background = !background.is_null() && (value == background
                        || reader.object_transform(value).is_some_and(|t| Some(t) == reader.object_transform(background)));
                    reader.log(&format!("{prefix} unity_name={:?} class={:?} same_transform_as_named_background={same_background}", reader.name(value), label(&runtime.object_class_name(value))));
                    reader.describe_object(value, &prefix);
                } else {
                    let value_class = unsafe { (runtime.v2().get_object_class)(value) };
                    let existing = objects.iter().position(|(o, _, _)| *o == value);
                    let next = if existing.is_some() { existing } else if depth < MAX_FIELD_DEPTH
                        && objects.len() < MAX_FIELD_OBJECTS
                        && !unsafe { (runtime.v2().class_is_value_type)(value_class) }
                        && !runtime.object_class_name(value).starts_with("System.")
                        && let Ok(root) = reflection::Root::new(runtime, value) {
                        let index = objects.len();
                        roots.push(root);
                        objects.push((value, format!("object[{visited}].{name}"), depth + 1));
                        Some(index)
                    } else { None };
                    reader.log(&format!("{prefix} class={:?} reference_object={next:?}", label(&runtime.object_class_name(value))));
                }
            }
            class = runtime.class_parent(class);
        }
        visited += 1;
    }
    reader.log(&format!("Fields END objects_visited={visited} objects_discovered={} fields_sampled={inspected} limits objects={MAX_FIELD_OBJECTS} fields={MAX_FIELDS} per_class={MAX_FIELDS_PER_CLASS} classes={MAX_FIELD_CLASSES} depth={MAX_FIELD_DEPTH}; references_not_ownership_proof=true", objects.len()));
    true
}

pub(super) fn capture(state: &State, lease: lease::Lease) {
    // A diagnostic never extends cleanup/discovery permission past revocation.
    let Some(directory) = lease::directory() else { return; };
    let permitted = || lease::read(&directory, lease.target_start)
        .is_some_and(|current| current.enabled && current.same_map(lease));
    if !lease.enabled || !permitted() { return; }
    let runtime = state.runtime;
    let controllers = runtime.find_objects(state.bindings.map_renderer_class);
    let Some(controller) = unique(&controllers) else { return; };
    let mut accessor: DnhHandle = null_mut();
    let mut data: DnhHandle = null_mut();
    if !unsafe { (runtime.v2().field_get_value)(controller, state.bindings.map_accessor_field,
        (&mut accessor as *mut DnhHandle).cast()) }
        || !runtime.object_is(accessor, state.bindings.map_accessor_class)
        || !unsafe { (runtime.v2().field_get_value)(accessor, state.bindings.map_data_field,
            (&mut data as *mut DnhHandle).cast()) } { return; }
    let Some(id) = runtime.read_value::<i32>(data, c"id") else { return; };
    if id as i64 != lease.map_id || id as i64 != state.expected_map
        || map_snapshot(state, controller).is_some() { return; }

    // Optional diagnostic getters do not alter the module's required bindings.
    let object = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Object");
    let transform = runtime.class(c"UnityEngine.CoreModule.dll", c"UnityEngine", c"Transform");
    let reader = Reader {
        state,
        get_name: runtime.instance_method(object, c"get_name", &[]),
        child_count: runtime.instance_method(transform, c"get_childCount", &[]),
        get_child: runtime.instance_method(transform, c"GetChild", &[c"System.Int32"]),
        active: runtime.instance_method(state.bindings.game_object_class, c"get_activeInHierarchy", &[]),
        position: runtime.instance_method(transform, c"get_position", &[]),
        rotation: runtime.instance_method(transform, c"get_rotation", &[]),
        scale: runtime.instance_method(transform, c"get_lossyScale", &[]),
    };
    reader.log(&format!("BEGIN lease={} actual_map={id}; limits ancestors={MAX_ANCESTORS} children={MAX_CHILDREN} renderer_sample={MAX_RENDERERS} renderer_paths={MAX_RENDERER_PATHS}", lease.token()));
    let game_object = runtime.invoke(state.bindings.component_get_game_object, controller, &[]);
    let mut current = game_object.and_then(|g| runtime.invoke(state.bindings.game_object_get_transform, g, &[]))
        .filter(|t| !t.is_null());
    for depth in 0..MAX_ANCESTORS {
        if !permitted() { reader.log("ABORT lease revoked"); return; }
        let Some(t) = current else { break; };
        reader.describe(t, &format!("MapController ancestor[{depth}]"));
        current = reader.parent(t);
    }
    reader.log(&format!("MapController ancestors_truncated={}", current.is_some()));

    // Signatures can identify the service owning cell-coordinate conversion;
    // never invoke an unknown game method merely to guess its semantics.
    for (name, object) in [("MapController", controller), ("map service", accessor)] {
        let class = unsafe { (runtime.v2().get_object_class)(object) };
        let total = unsafe { (runtime.v2().get_class_methods)(class, null_mut(), 0) };
        let mut methods = vec![null_mut(); total.min(48)];
        if !methods.is_empty() {
            unsafe { (runtime.v2().get_class_methods)(class, methods.as_mut_ptr(), methods.len()); }
        }
        reader.log(&format!("MethodMetadata {name} total={total} sampled={}", methods.len()));
        for method in methods {
            if !permitted() { reader.log("ABORT lease revoked"); return; }
            let Some(info) = method_info(runtime, method) else { continue; };
            let parameters = (0..info.parameter_count.min(4)).map(|index| {
                label(&text(unsafe { (runtime.v2().method_parameter_type_name)(method, index) }))
            }).collect::<Vec<_>>();
            reader.log(&format!("MethodMetadata {name} name={:?} returns={:?} static={} parameter_count={} first_parameters={parameters:?}",
                label(&text(info.name)), label(&text(info.return_type_name)), info.is_static != 0, info.parameter_count));
        }
    }

    // The live sample placed mesh renderers under a separate MapBackground.
    // Describe it explicitly, without admitting or hiding it by name.
    if !permitted() { reader.log("ABORT lease revoked"); return; }
    let background_name = runtime.string("MapBackground");
    let background = runtime.invoke(state.bindings.game_object_find, null_mut(), &[background_name])
        .unwrap_or(null_mut());
    if !background.is_null() {
        reader.describe_object(background, "Named candidate MapBackground (not admitted)");
    } else { reader.log("Named candidate MapBackground not found"); }
    let mut static_root: DnhHandle = null_mut();
    if unsafe { (runtime.v2().field_get_value)(null_mut(), state.bindings.map_root_field,
        (&mut static_root as *mut DnhHandle).cast()) } && !static_root.is_null() {
        reader.describe_object(static_root, "epf static GameObject (not admitted)");
    }
    // The previous live capture exhausted its object limit on controller
    // dependencies before inspecting the map service's rendering dependency.
    // Prioritize that observed reference without increasing the capture bounds.
    let mut seeds = Vec::new();
    if let Some(rendering) = runtime.read_reference(accessor, c"doow")
        && runtime.object_class_name(rendering) == "epk" {
        seeds.push((rendering, "map service.doow (observed epk dependency)".to_owned()));
    }
    seeds.extend([(accessor, "MapController.fpm".to_owned()), (controller, "MapController".to_owned()),
        (data, "ClientMapData".to_owned())]);
    let prefab = runtime.gc_target(state.prefab_gc);
    if !prefab.is_null() {
        reader.describe_object(prefab, "tacticalCell prefab");
        let tactical = component(state, prefab, state.bindings.tactical_cell_class);
        seeds.push((tactical, "tacticalCell component".to_owned()));
    }
    // Sample indices span both row parity and the map extent. They are data
    // samples only; no cell setter, instantiation or transform mutation runs.
    if let Some(cells) = runtime.read_reference(data, c"cellsData") {
        let class = unsafe { (runtime.v2().get_object_class)(cells) };
        if let (Some(count), Some(item)) = (runtime.instance_method(class, c"get_Count", &[]),
            runtime.instance_method(class, c"get_Item", &[c"System.Int32"]))
            && runtime.invoke(count, cells, &[]).and_then(|v| runtime.unbox::<i32>(v)) == Some(MAP_CELL_COUNT as i32) {
            for index in [0_i32, 14, 559] {
                if !permitted() { reader.log("ABORT lease revoked"); return; }
                if let Some(cell) = runtime.invoke(item, cells, &[argument(&index)]) {
                    seeds.push((cell, format!("cellsData[{index}]")));
                }
            }
        }
    }
    if !owned_branches(&reader, accessor, controller, background, &permitted) { return; }
    if !fields(&reader, seeds, background, &permitted) { return; }

    if reader.active.is_none() {
        reader.log("Renderer paths unavailable: GameObject.get_activeInHierarchy unsupported; END");
        return;
    }
    // Bound the host output buffer and every subsequent renderer/path traversal.
    // The host's Unity find operation itself may enumerate the scene internally.
    let total = unsafe { (runtime.v2().find_objects)(state.bindings.renderer_class, null_mut(), 0) };
    let mut renderers = vec![null_mut(); total.min(MAX_RENDERERS)];
    if !renderers.is_empty() {
        unsafe { (runtime.v2().find_objects)(state.bindings.renderer_class, renderers.as_mut_ptr(), renderers.len()); }
    }
    let mut emitted = 0;
    for renderer in renderers {
        if emitted == MAX_RENDERER_PATHS { break; }
        if !permitted() { reader.log("ABORT lease revoked"); return; }
        if renderer.is_null() { continue; }
        let Some(go) = runtime.invoke(state.bindings.component_get_game_object, renderer, &[]) else { continue; };
        let active = reader.active.and_then(|m| runtime.invoke(m, go, &[]))
            .and_then(|v| runtime.unbox::<bool>(v));
        let enabled = runtime.invoke(state.bindings.renderer_get_enabled, renderer, &[])
            .and_then(|v| runtime.unbox::<bool>(v));
        if active != Some(true) || enabled != Some(true) { continue; }
        let mut t = runtime.invoke(state.bindings.game_object_get_transform, go, &[]).filter(|t| !t.is_null());
        let mut path = Vec::new();
        for _ in 0..MAX_ANCESTORS {
            let Some(node) = t else { break; };
            path.push(reader.name(node));
            t = reader.parent(node);
        }
        path.reverse();
        reader.log(&format!("Renderer[{emitted}] class={} active=true enabled=true ancestors={path:?} ancestors_truncated={}",
            label(&runtime.object_class_name(renderer)), t.is_some()));
        if let Some(node) = runtime.invoke(state.bindings.game_object_get_transform, go, &[]).filter(|t| !t.is_null()) {
            reader.describe(node, &format!("Renderer[{emitted}] transform"));
        }
        emitted += 1;
    }
    reader.log(&format!("END renderer_total={total} sampled={} emitted={emitted}; sample_not_ownership_proof=true", total.min(MAX_RENDERERS)));
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn owned_field_requires_exact_instance_name_and_reference_type() {
        assert!(exact_reference_signature(0, "doqa", "ue", c"doqa", "ue"));
        for (storage, name, ty) in [(1, "doqa", "ue"), (0, "doqb", "ue"),
            (0, "doqa", "System.Int32"), (0, "doqa", "ub"), (0, "doqa", "")] {
            assert!(!exact_reference_signature(storage, name, ty, c"doqa", "ue"));
        }
    }

    #[test]
    fn reference_comparison_never_treats_unavailable_as_equal() {
        let first = std::ptr::dangling_mut::<u8>().cast();
        let other = std::ptr::dangling_mut::<u16>().cast();
        assert_eq!(same_reference(first, first), Some(true));
        assert_eq!(same_reference(first, other), Some(false));
        assert_eq!(same_reference(first, null_mut()), None);
        assert_eq!(same_reference(null_mut(), first), None);
        assert_eq!(same_reference(null_mut(), null_mut()), None);
    }

    #[test]
    fn failure_capture_is_deduplicated_until_diagnosis_or_map_changes() {
        let mut previous = String::new();
        assert!(changed(&mut previous, "missing root"));
        assert!(!changed(&mut previous, "missing root"));
        assert!(changed(&mut previous, "missing controller"));
        assert!(!changed(&mut previous, "missing controller"));
        previous.clear(); // DNM_Tick resets this on a new map lease.
        assert!(changed(&mut previous, "missing controller"));
    }

    #[test]
    fn diagnostic_names_are_bounded_and_cannot_inject_log_lines() {
        assert_eq!(label("root\r\nchild\t"), "root  child ");
        assert_eq!(label(&"é".repeat(200)).chars().count(), MAX_NAME_CHARS);
    }

    #[test]
    fn field_sampling_respects_class_and_capture_limits() {
        assert_eq!(field_capacity(0, 0), 0);
        assert_eq!(field_capacity(usize::MAX, 0), MAX_FIELDS_PER_CLASS);
        assert_eq!(field_capacity(100, MAX_FIELDS - 2), 2);
        assert_eq!(field_capacity(100, MAX_FIELDS), 0);
        assert_eq!(field_capacity(100, usize::MAX), 0);
    }
}
