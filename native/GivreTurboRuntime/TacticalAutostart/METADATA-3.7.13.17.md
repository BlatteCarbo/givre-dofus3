# Dofus 3.7.13.17 tactical evidence

Read-only analysis on November 4, 2026. No native DLL promotion or live rendering
validation follows from these observations.

## Current binary identity

Installed directory: `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3`.

- Metadata version: 39.
- `global-metadata.dat` SHA-256:
  `ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`.
- Complete `GameAssembly.dll` SHA-256:
  `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591`.

The native hash was computed after explicitly seeking to byte zero. An earlier
hash of the stream's remaining suffix is not a file identity and is discarded.

## Metadata names retained

Existing Cpp2IL annotated assemblies under
`%USERPROFILE%/AppData/Local/Temp/givre-state-cpp2il-20261104/dll-addresses`
retain `MapController.dhcf : fpm`, concrete `epf`, `epf.doqa : ue`,
`ue.dgjn : MapController`, `ue.dgjp : ub` and the three layer pairs
`dgih/dgii`, `dgij/dgik`, `dgil/dgim`. The map-data backing field is instance;
the `GameObject` backing field is static. No replacement name for `epf` is needed.

`TacticalDebugCell.qxl(Int32)` and `qxk(wi)` remain present. The exact
AddressableUtility method tokens remain `0x06000022` for
`LoadAssetAsync<T>(String, Int32): Task<T>` and `0x06000027` for `Release<T>(T)`.
Declared signatures do not establish the behavior of the native method bodies.

## Current generic specifications

The metadata string heap begins at `0x203824`, methods at `0x855900` with
32-byte rows, and type definitions at `0x21d375c` with 82-byte rows.

Matching the exact method name and original-client token gives:

| Method | Metadata row | Method index | Declaring type index |
| --- | --- | --- | --- |
| LoadAssetAsync | `0x14435e0` | 390887 | 43898 |
| Release | `0x1443680` | 390892 | 43898 |

Type 43898 resolves to
`Ankama.AddressableUtilities.Runtime.AddressableUtility`.

A structurally coherent metadata-registration candidate begins at raw
`0x5fdff90`. Its paired field-offset/type-size counts are both 44465, not the
historical 44466. The method-spec table is VA `0x185dbe480`, count 137315;
generic instantiations are at VA `0x185d1f1f0`, count 33137.

Traversing method specs, their method-instantiation index, argument count and
IL2CPP CLASS argument yields:

| Specification | Method index | Instantiation | Argument type index | Resolved argument |
| --- | --- | --- | --- | --- |
| 124082 | 390887 | 1731 | 29143 | UnityEngine.GameObject |
| 124106 | 390892 | 904 | 29177 | UnityEngine.Object |

These are current registration records for the required closed signatures,
not a proof of successful invocation or an independent generic function pointer.
An initial 16-byte generic-function-table scan did not find these spec indices;
its layout and generic-sharing interpretation remain unresolved. Do not report
missing AOT code from that scan or reuse historical spec indices 124067/124091.

### Generic-sharing resolution

The absence of a separate function row for each closed signature is explained
by fully shared generics, not missing executable code. Resolving every function
row through its method spec finds these entries:

| Function row | Spec | Method | Argument | Pointer index | Executable VA |
| --- | --- | --- | --- | --- | --- |
| 20000 | 70573 | 390887 | __Il2CppFullySharedGenericType | 20001 | `0x18343b360` |
| 20007 | 70585 | 390892 | __Il2CppFullySharedGenericType | 20008 | `0x18343b8f0` |

Both use generic instantiation 24418 and type definition 44463,
`Unity.IL2CPP.Metadata.__Il2CppFullySharedGenericType` (IL2CPP VALUETYPE kind17).
The current code-registration structure ends at raw `0x5ab8780`; it points to
151 codegen modules at VA `0x1867fb000`, and 24772 generic method pointers at
VA `0x185f50930`. Following the two pointer indices above reaches executable
PE sections and nonempty method prologues. Their RVAs match the annotated
current LoadAssetAsync/Release addresses `0x343b360` and `0x343b8f0`.

The relevant upstream reader layouts are documented by Cpp2IL release
2022.1.0-pre-release.21:

- https://github.com/SamboyCoding/Cpp2IL/blob/2022.1.0-pre-release.21/LibCpp2IL/BinaryStructures/Il2CppGenericMethodIndices.cs
- https://github.com/SamboyCoding/Cpp2IL/blob/2022.1.0-pre-release.21/LibCpp2IL/BinaryStructures/Il2CppMethodSpec.cs
- https://github.com/SamboyCoding/Cpp2IL/blob/2022.1.0-pre-release.21/LibCpp2IL/BinaryStructures/Il2CppCodeRegistration.cs

This supersedes the unresolved table interpretation above. It establishes
closed signature records and executable shared implementations; actual
reflection construction, generic context and invocation remain live checks.

## Current native method observations

Bounded x64 disassembly at the annotated current RVAs confirms executable bodies:

- `qxl`, RVA `0x25d75d0`, stores its Int32 argument at object offset `0x68`,
  derives a row-parity Boolean at `0x70`, and later passes the stored cell id
  minus `0x230` (560) to a renderer-related call. It also invokes other helpers;
  their semantic identities have not been independently resolved here.
- `qxk`, RVA `0x25d7410`, branches on enum values 1, 2 and 3. Value 1 uses a
  zero vector and a false Boolean on the object at `0x30`. Value 2 reads the
  serialized reference at `0x28`; value 3 reads the reference at `0x20` and
  selects one of two vectors using the parity Boolean at `0x70`.

The references at `0x20`/`0x28` agree with the current metadata offsets of
`m_spriteCellMov`/`m_spriteCellLos`. Calls at VA `0x1853b7d10`,
`0x18538d7d0` and `0x18538dea0` have not yet been mapped independently to Unity
method names, so this is branch/data-flow evidence, not a full semantic proof.
The tactical source separately overwrites sprite and renderer visibility after
calling `qxk`; do not diagnose its output from the enum branch alone.

Further PE resolution found the current `UnityEngine.CoreModule.dll` codegen
module at raw `0x637ea10`, with 7482 method slots at VA `0x1870a95e0`.
The four target pointers occupy the following original-client token slots:

| Native VA | Token in CoreModule | Matching current Unity declaration |
| --- | --- | --- |
| `0x1853b7d10` | `0x060006fe` | Renderer.set_enabled |
| `0x18538d7d0` | `0x060001bc` | SpriteRenderer.set_color |
| `0x18538dea0` | `0x060001b2` | SpriteRenderer.set_sprite |
| `0x1853b8770` | `0x0600070d` | Renderer.set_sortingOrder |

Metadata tokens collide across assemblies. The codegen-module name scopes the
native token; the matching Renderer/SpriteRenderer declarations were found in
the current type table, rather than selecting the first global token match.
Together with the native argument flow, this identifies qxk's operations as
renderer visibility, color and sprite changes, and qxl's final operation as
sorting order `cellId - 560`. The metadata39 image row is 36 bytes; its type
start/count at offsets 8/10 are UInt16, not UInt32. The CoreModule range is
`[28714, 29787)`, containing SpriteRenderer type 28854 and Renderer type 28958.
The next image row resolves to Core.Localization.dll, confirming row alignment.
This independently resolves the assembly ambiguity. It does not prove prefab
colors or live alignment.

## Remaining evidence

The installed `Dofus_Data/StreamingAssets/aa/catalog.bin` is 909718 bytes,
SHA-256 `b60cdf25e31b65e2bfcbe4c1b1d50e2021569f2aaddb8ba69c7868ab9555225b`.
It contains ASCII `tacticalCell` at `0x2f279` and `tacticalCell.prefab` at
`0x2f289`. These strings establish presence in this catalog, not successful
address resolution, bundle availability or the serialized sprite references.
Read-only asset investigation st_01a1a63d is resolving that remaining boundary;
no installed game file or project source change is part of that investigation.

### Prefab resolution confirmed

Read-only asset investigation st_01a1a63d resolved key-table entry `0x5074`
through key object `0x9321e`, location set `0x9322a` and location `0x2f2a4`.
The BundledAssetProvider location has internal id
`Assets/Content/Prefabs/Debug/tacticalCell.prefab`. Its dependency set includes
`aa/StandaloneWindows64/worldassets_assets_all.bundle`, 2456646 bytes, SHA-256
`07e21cdc2e80bfdef928d573c1444fc2193c337793d386e4bdc2b21f4349bbf1`.
That bundle's container resolves the exact path to GameObject
7901019122066836993 named tacticalCell, serialized file
`CAB-f024a410046b32364568659a0d298b41`.

The prefab Transform is unparented, scale `(48,48,1)`, identity quaternion and
position approximately `(-150.8,270.7,0)`. SpriteRenderer starts enabled and white.
The MonoBehaviour's script resolves in dofus_unity_monoscripts.bundle to
`Core.Rendering.Sprites.Interactive.TacticalDebugCell`, assembly Core.
Its movement sprite reference is file4/path3038464113058975085, matching the
renderer initial sprite; file4 is CAB-9f1c2aa15d4897e53cf6114f5101cd5a in
uiassets_assets_.bundle. The sprite is named tacticalCell. Its LOS sprite is
local path28327881472226010, named tacticalLos. Both sprites have rect
180x192, pixels-per-unit100 and pivot `(0.5,0.5)`.

UnityPy1.25.4 was isolated under Temp/senpi-unity-reader. Bundle headers report
0.0.0; reader fallback6000.3.16f1 comes from the installed globalgamemanagers
header. Sprite texture pixels and runtime colors were not evaluated. These
serialized asset facts do not prove scene alignment or restoration.

Verify current generic execution and reflection dispatch, tactical-cell prefab
and sprites, live controller/owner/layer identities, all 560 cells, visible
alignment, actor/UI preservation, restoration, reactivation and map transitions.
The historical verifier remains pinned to 3.7.12.12 and was not relaxed.
