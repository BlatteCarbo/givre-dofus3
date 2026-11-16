use super::{argument, is_task_game_object, required};
use dofus_native_mod_api::{DnhGcHandleV4, DnhHandle};
use dofus_native_mod_sdk::Runtime;
use std::ffi::CStr;
use std::ptr::{null, null_mut};

// The pinned host invokes MethodInfo.MakeGenericMethod without virtual dispatch
// and copies the open return type into its inflated wrapper. Resolve the actual
// reflection object's methods instead; never pass a managed MethodInfo as a host
// method handle. See METADATA-3.7.12.12.md for tokens and binary evidence.
pub struct Root {
    runtime: Runtime,
    handle: DnhGcHandleV4,
}

impl Root {
    pub fn new(runtime: Runtime, object: DnhHandle) -> Result<Self, String> {
        if object.is_null() { return Err("reflection returned null".to_owned()); }
        let handle = runtime.gc_new(object, false);
        if handle == 0 { return Err("reflection GC root allocation failed".to_owned()); }
        Ok(Self { runtime, handle })
    }

    pub fn get(&self) -> DnhHandle { self.runtime.gc_target(self.handle) }
}

impl Drop for Root {
    fn drop(&mut self) { self.runtime.gc_free(self.handle); }
}

fn concrete_method(
    runtime: Runtime, object: DnhHandle, name: &CStr, parameters: &[&CStr],
) -> Result<DnhHandle, String> {
    if object.is_null() { return Err(format!("{} on null reflection object", name.to_string_lossy())); }
    let class = unsafe { (runtime.v2().get_object_class)(object) };
    required(runtime.instance_method(class, name, parameters))
        .ok_or_else(|| format!("{}.{}", runtime.class_name(class), name.to_string_lossy()))
}

fn checked_call(
    runtime: Runtime, method: DnhHandle, object: DnhHandle, arguments: &[DnhHandle],
) -> Result<DnhHandle, String> {
    let mut exception = null_mut();
    let result = unsafe {
        (runtime.v2().runtime_invoke)(method, object,
            if arguments.is_empty() { null() } else { arguments.as_ptr() }, &mut exception)
    };
    if exception.is_null() { return Ok(result); }
    let exception = Root::new(runtime, exception)?;
    let mut description = runtime.object_class_name(exception.get());
    let exception_class = runtime.class(c"mscorlib.dll", c"System", c"Exception");
    if let Some(to_string) = runtime.instance_method(exception_class, c"ToString", &[])
        && let Some(api) = runtime.v7() {
        let mut nested = null_mut();
        let message = unsafe {
            (api.runtime_invoke_virtual)(to_string, exception.get(), null(), &mut nested)
        };
        if nested.is_null() && let Some(message) = runtime.managed_string(message) {
            description = message.split_whitespace().collect::<Vec<_>>().join(" ");
        }
    }
    Err(description)
}

fn call(
    runtime: Runtime, object: DnhHandle, name: &CStr, parameters: &[&CStr], arguments: &[DnhHandle],
) -> Result<DnhHandle, String> {
    checked_call(runtime, concrete_method(runtime, object, name, parameters)?, object, arguments)
        .map_err(|error| format!("{}: {error}", name.to_string_lossy()))
}

fn reference_array(runtime: Runtime, element: &CStr, values: &[DnhHandle]) -> Result<Root, String> {
    let class = runtime.class(c"mscorlib.dll", c"System", element);
    if class.is_null() { return Err(format!("System.{}", element.to_string_lossy())); }
    let array = Root::new(runtime, unsafe { (runtime.v2().array_new)(class, values.len()) })?;
    let data = unsafe { (runtime.v2().array_data)(array.get()) };
    if data.is_null() { return Err("reflection array data is null".to_owned()); }
    unsafe { std::ptr::copy_nonoverlapping(values.as_ptr(), data.cast(), values.len()); }
    Ok(array)
}

pub struct Method {
    reflected: Root,
    invoke: DnhHandle,
}

impl Method {
    #[cfg(test)]
    pub(super) fn fixture(runtime: Runtime, object: DnhHandle, invoke: DnhHandle) -> Self {
        Self { reflected: Root::new(runtime, object).unwrap(), invoke }
    }

    pub fn resolve(
        runtime: Runtime, declaring_class: DnhHandle, argument_class: DnhHandle,
        token: i32, expected_name: &str,
    ) -> Result<Self, String> {
        let declaring_type = Root::new(runtime, unsafe {
            (runtime.v2().class_type_object)(declaring_class)
        })?;
        let flags = 24_i32; // BindingFlags.Public | BindingFlags.Static.
        let methods = Root::new(runtime, call(runtime, declaring_type.get(), c"GetMethods",
            &[c"System.Reflection.BindingFlags"], &[argument(&flags)])?)?;
        let mut matches = Vec::new();
        for method in runtime.array_references(methods.get()) {
            let value = call(runtime, method, c"get_MetadataToken", &[], &[])?;
            if runtime.unbox::<i32>(value) == Some(token) { matches.push(method); }
        }
        let method = super::unique(&matches)
            .ok_or_else(|| format!("{expected_name} token 0x{token:08x}: {} matches", matches.len()))?;
        let name = runtime.managed_string(call(runtime, method, c"get_Name", &[], &[])?);
        let generic = call(runtime, method, c"get_IsGenericMethodDefinition", &[], &[])?;
        if name.as_deref() != Some(expected_name)
            || runtime.unbox::<bool>(generic) != Some(true) {
            return Err(format!("token 0x{token:08x} is not {expected_name}<T>"));
        }
        let argument_type = Root::new(runtime, unsafe {
            (runtime.v2().class_type_object)(argument_class)
        })?;
        let types = reference_array(runtime, c"Type", &[argument_type.get()])?;
        let reflected = Root::new(runtime, call(runtime, method, c"MakeGenericMethod",
            &[c"System.Type[]"], &[types.get()])?)?;
        let invoke = concrete_method(runtime, reflected.get(), c"Invoke", &[
            c"System.Object", c"System.Reflection.BindingFlags", c"System.Reflection.Binder",
            c"System.Object[]", c"System.Globalization.CultureInfo",
        ])?;
        Ok(Self { reflected, invoke })
    }

    pub fn require_task_game_object(&self) -> Result<(), String> {
        let runtime = self.reflected.runtime;
        let result_type = Root::new(runtime, call(runtime, self.reflected.get(), c"get_ReturnType", &[], &[])?)?;
        let name = call(runtime, result_type.get(), c"ToString", &[], &[])?;
        let name = runtime.managed_string(name).ok_or("reflection return type has no name")?;
        if is_task_game_object(&name) { Ok(()) }
        else { Err(format!("unsupported reflected prefab return type: {name}")) }
    }

    pub fn call(&self, arguments: &[DnhHandle]) -> Result<DnhHandle, String> {
        let runtime = self.reflected.runtime;
        let arguments = reference_array(runtime, c"Object", arguments)?;
        let flags = 0_i32;
        checked_call(runtime, self.invoke, self.reflected.get(),
            &[null_mut(), argument(&flags), null_mut(), arguments.get(), null_mut()])
    }
}

pub fn boxed_int32(runtime: Runtime, value: i32) -> Result<Root, String> {
    let class = runtime.class(c"mscorlib.dll", c"System", c"Int32");
    if class.is_null() { return Err("System.Int32".to_owned()); }
    let boxed = Root::new(runtime, unsafe { (runtime.v2().object_new)(class) })?;
    let data = runtime.unboxed_handle(boxed.get()).ok_or("could not unbox Int32")?;
    unsafe { data.cast::<i32>().write(value); }
    Ok(boxed)
}

/// Public instance getter found through reflection, so inherited members count:
/// the host's signature lookup only sees methods declared by the object's own class.
pub struct Getter {
    reflected: Root,
    invoke: DnhHandle,
}

impl Getter {
    pub fn resolve(runtime: Runtime, class: DnhHandle, name: &str) -> Result<Self, String> {
        let declaring_type = Root::new(runtime, unsafe { (runtime.v2().class_type_object)(class) })?;
        let flags = 20_i32; // BindingFlags.Public | BindingFlags.Instance, inherited included.
        let methods = Root::new(runtime, call(runtime, declaring_type.get(), c"GetMethods",
            &[c"System.Reflection.BindingFlags"], &[argument(&flags)])?)?;
        let mut found = None;
        for method in runtime.array_references(methods.get()) {
            let method_name = runtime.managed_string(call(runtime, method, c"get_Name", &[], &[])?);
            if method_name.as_deref() == Some(name) { found = Some(method); break; }
        }
        let reflected = Root::new(runtime, found.ok_or_else(|| format!("{name}: not a public instance method of {}", runtime.class_name(class)))?)?;
        let invoke = concrete_method(runtime, reflected.get(), c"Invoke", &[
            c"System.Object", c"System.Reflection.BindingFlags", c"System.Reflection.Binder",
            c"System.Object[]", c"System.Globalization.CultureInfo",
        ])?;
        Ok(Self { reflected, invoke })
    }

    pub fn bool_on(&self, target: DnhHandle) -> Result<bool, String> {
        let runtime = self.reflected.runtime;
        let arguments = reference_array(runtime, c"Object", &[])?;
        let flags = 0_i32;
        let value = checked_call(runtime, self.invoke, self.reflected.get(),
            &[target, argument(&flags), null_mut(), arguments.get(), null_mut()])?;
        runtime.unbox::<bool>(value).ok_or_else(|| "getter did not return a Boolean".to_owned())
    }
}
