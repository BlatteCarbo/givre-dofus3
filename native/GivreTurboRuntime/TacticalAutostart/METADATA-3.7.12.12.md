# Dofus 3.7.12.12 tactical evidence

Reverified locally on November 1, 2026, without injection.

- `Dofus_Data/il2cpp_data/Metadata/global-metadata.dat`: version 39,
  SHA-256 `ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6`.
- `GameAssembly.dll`: SHA-256
  `70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663`.
- Metadata string heap starts at `0x203824`; type definitions at `0x21db13c`
  have 82-byte rows; methods at `0x85574c` have 32-byte rows; fields at
  `0x1cb8ba0` and parameters at `0x18a78f4` have 12-byte rows.
  Method return-type indices start at byte 6, not byte 8.

## Map and cells

`Core.Rendering.Map.GameObjects.MapController`, row `0x21ee842`, inherits
`UnityEngine.MonoBehaviour`. Field `dhcf`, row `0x1ccc8b0`, has type index
100873, resolving through GameAssembly to global interface `fpm`.

Its concrete service `epf`, row `0x226cbd4`, is not a Unity object. Its unique
`ClientMapData` field is `<doqb>k__BackingField`, row `0x1d2f274`; its unique
`GameObject` field is `<doqc>k__BackingField`, row `0x1d2f280`.
Only MapController is passed to Unity object discovery.

`AleCore.Data.UnityClient.ClientMapData.id`, row `0x1ee70b0`, type index 78866,
is `System.Int32` (IL2CPP kind 8), not Int64. `cellsData` is at `0x1ee7170`.
`ClientCellData.cellNumber`, row `0x1ee6f78`, type index 93602, is UInt32
(kind 9); `mov` and `los` are at `0x1ee6fb4` and `0x1ee6fc0`.

`TacticalDebugCell.qxl`, method `0x8a058c`, parameter 8534, takes Int32.
`qxk`, method `0x8a056c`, parameter 8533, takes enum `wi`.
`qxj` also takes `wi`: it is not the cell ID setter.
The enum constants have compressed default values 0, 1, 2, 3.
Serialized sprites remain `m_spriteCellMov` and `m_spriteCellLos`.

## The prefab loader returns Task, not UniTask

`AddressableUtility.LoadAssetAsync`, method row `0x1445cac`, has return-type
index **51812**. The generic return cannot be established from the metadata
type-definition table alone.

The GameAssembly PE image base is `0x180000000`. Its metadata-registration
field-offset count at raw offset `0x601ab30` is 44466, followed 16 bytes later
by the same type-definition size count. The types table pointer, 24 bytes
before that field, is `0x1860c6690`.

Type 51812 points to `0x186063560`, IL2CPP kind 21 (generic instance), whose
generic-class pointer is `0x186d00cf8`. That points to type `0x186f9d288`,
whose type-definition index is **20028**: `System.Threading.Tasks.Task`1`.
It does not resolve to `Cysharp.Threading.Tasks.UniTask`1` (index 32715).

The native code additionally checks the closed reflected return type at runtime
before calling the loader. It polls `Task.get_IsCompleted` (`0xe95f2c`) and
`get_IsCompletedSuccessfully` (`0xe95f6c`) before reading `Task<GameObject>.Result`.
Pending, faulted, cancelled, unknown, and UniTask-shaped results never reach
a blocking Result call. The returned prefab is GC-rooted before the Task is freed.

## November 2: the pinned host calls the throwing base reflection method

The shipped host SHA-256
`22d2c6743c23d48f80bc0a9e114ba13bcfb07587470aedb188a9fdba8846ec1b`
does **not** contain the virtual-dispatch step now present in
`unity_bridge.cpp`. Disassembly at host VA `0x1801b3a8c` loads the base
MethodInfo pointer directly; `0x1801b3acb` passes it to the runtime-invoke
wrapper called at `0x1801b3aef`. There is no object virtual-method resolution
between finding `MakeGenericMethod` and invoking it.

In the installed game metadata, `System.Reflection.MethodInfo.MakeGenericMethod`
is at `0xeb788c`, flags `0x01c6`, virtual slot 51; the
`RuntimeMethodInfo` override is at `0xebcd4c`, flags `0x00c6`, the same slot.
The mscorlib code-generation module is at raw `0x62b20a0`; its method-pointer
table is `0x186fdc900`. Tokens `0x060027fa` and `0x06002aa0` map respectively
to base VA `0x1841d1440` and override VA `0x1842188e0`.
The base unconditionally calls `NotSupportedException..ctor(String)`
at `0x1842d4ba0` (token `0x060009a9`, metadata `0xe7ae6c`), then throws
at `0x1841d1489`. Its literal at encoded slot `0x186c22778` decodes to:
`Derived classes must provide an implementation.`
This is binary evidence of the swallowed exception, not a new live exception capture.

Missing AOT code is ruled out: metadata-registration method specs are at
`0x185df85c0`, count 137296; generic instantiations are at `0x185d590d0`.
Spec **124067** binds method **391211** (`LoadAssetAsync`) to instantiation
**1731**, `UnityEngine.GameObject` (type definition 29144).
Spec **124091** binds method **391216**, `Release<T>(T)`, to instantiation
**904**, `UnityEngine.Object` (type definition 29178). Release uses this
existing Object specialization, which accepts the derived GameObject, rather
than assuming a GameObject-specific Release instantiation (none is registered).

The mod now enumerates public static reflected methods on AddressableUtility,
selecting exact metadata tokens `0x06000022` (`LoadAssetAsync(String, Int32)`,
row `0x1445cac`) and `0x06000027` (`Release<T>(T)`, row `0x1445d4c`).
It resolves `MakeGenericMethod(Type[])` and five-argument `Invoke` on the
actual reflection object's class, not on the base MethodInfo class.
The latter overload is at `0xebcb6c` and takes Object, BindingFlags, Binder,
Object[], CultureInfo. Int32 arguments are boxed, and reflection objects and
argument arrays remain GC-rooted. Invocation exceptions include their actual
managed text in the existing latched failure log.

This also avoids the host's copied open-generic return-type wrapper.
The real reflected return type must be Task<GameObject>. Completion predicates
are resolved on the declaring `Task` base class: host signature lookup visits
declared methods only, so looking for these predicates on Task<GameObject>
would fail. Result remains gated on both completion and success.

`verify-map-root.mjs` now verifies the tokens, AOT specs, override slot,
exception constructor and literal against the pinned installed binaries.
No host DLL or host pin was changed.

## App/native readiness contract

The DLL reads `tactical-map.lease` beside itself. The app publishes it atomically
under the same lock as readiness revocation and heartbeat publication:

`1 targetCreationFileTime ownerPid ownerCreationFileTime mapEpoch mapId desiredGeneration enabled expiresTickCount64`

Target and owner creation identities prevent PID reuse. The consumer requires
a live owner, positive map/epoch/generation, and a future expiry no more than
3000 ms away. The producer renews every 750 ms with 2250 ms validity.
No file, invalid identity, stale file, or lost owner permits managed work.

Map changes, character selection, relogin, server transfer, link loss, and
forget revoke readiness. A fresh roleplay map or client ContextReady republishes
it. The module also checks the live ClientMapData ID against the authorized map.

Disabling publishes `enabled=0` while the map is still ready, authorizing cleanup
but not activation. Native code restores renderers, drains the pending prefab
load without waiting, releases it, and writes the exact record without expiry
to `tactical-map.restored`. The app waits for this generation-specific ACK before
unload. Off-map cleanup is deferred, not attempted under a revoked lease.

Bindings are resolved only after the first valid lease. Failed bindings are
latched, map probes are limited to twice a second, and overlay construction is
limited to eight cells per tick. The original map is hidden only after all
560 cells have built. F8 applies only to the foreground process.

These are source, binary, and test checks, not evidence of live visual success.
