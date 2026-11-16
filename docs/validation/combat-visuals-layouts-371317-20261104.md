# Combat visuals instance-layout audit: 3.7.13.17

## Result

The requested numerical offsets still occur in the current registered layouts.
There is no demonstrated numerical field-offset mismatch in this sample. The
service links are declared as interfaces, not the concrete classes tested by the
native code; those concrete classes implement the corresponding interfaces.
This is compatible static evidence, not proof of the live object graph.

The animation-step fields are verified for `gpy : gpa`. The raw `step` argument
at the shipped hook RVA `0x20853E0` is not independently attributed to a current
managed owner here. Consequently those three reads/writes are **conditionally
verified for gpy, unresolved for the actual hooked step**. No guessed rename or
new hook address is proposed.

Only this report was created. No production edits, pin edits, git operations,
installs, runtime loading, injection, desktop operations, or process changes were
performed. Native images were inspected as file-backed data. This was not a
repeat of the 762-signature audit.

## Inputs and method

Source inspection was limited to:

- `native/GivreCombatVisuals/vendor/dofus-enhanced/src/animations/animations.cpp`
- `native/GivreCombatVisuals/vendor/dofus-enhanced/src/appearance/appearance-refresh.cpp`

Registration evidence came from
`docs/validation/combat-visuals-compositor-371317-20261104.md`. The prior signature
report supplied the installed-client path. Existing local Il2CppInspectorRedux
metadata structure definitions were read to interpret records; no tool was built
or installed. Existing Python `pefile` and `capstone` decoded selected native
instructions. Annotated DLLs were located but were not required for the result.

The actual files read were under
`%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3`:

| File | Bytes | Recomputed SHA256 |
| --- | ---: | --- |
| GameAssembly.dll | 125827584 | ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591 |
| Dofus_Data/il2cpp_data/Metadata/global-metadata.dat | 43768684 | ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af |

These equal the compositor report's inputs. The release label is supplied by the
task; the hashes identify the inspected content.

The audit reused the confirmed image base `0x180000000`, registered types table
`0x18608C8E0`, field-offset pointer table `0x186A3B570`, and Core.dll method
pointer table `0x1868EAD30`. All VAs were mapped through PE sections to raw bytes.
Metadata v39 section 19 has 44465 type rows of 82 bytes; section 11 has 206220
field rows of 12 bytes. FieldStart is type+26, FieldCount is type+62. Each field
record stores name index at +0 and full four-byte Il2CppType index at +4. The
registered field offset is the signed four-byte entry at the declaring type's
field table plus declared ordinal times four. Inherited fields use the base
type's table, not an ordinal in the derived type.

Il2CppType kind is flags bits 16..23 at +8; Class is `0x12`, Boolean `0x02`,
Int64 `0x0A`. Class data identifies the metadata type definition. ParentIndex
is type+16, not +14. InterfacesStart is +46 and InterfacesCount +70, resolved
through metadata section 16. These positions produced the inheritance and
interface relationships below.

## Requested targets

All row and ordinal numbers below are zero-based. Field names are exact current
metadata strings, not inferred semantic renames.

| Native use / expectation | Declaring owner (type index) | Current field | Field row / ordinal | Registered offset / type | Assessment |
| --- | --- | --- | --- | --- | --- |
| AnimatedEntity ID read | CoreAnimated.Animated (38936) | fhtc | 186608 / 61 | 0x1C8 / Int64 | Verified inherited layout; matches int64_t read |
| entity+0x310 | Core.Rendering.Entity.CoreAnimatedEntity.AnimatedEntity (997) | dhhm | 6912 / 16 | 0x310 / EntityLook (38959) | Verified layout; this offset is not directly read in either scoped source file |
| fight+0x18 expected fyb | fxc (8421) | dvzu | 47036 / 0 | 0x18 / frv interface (8253) | Offset verified; fyb implements frv; live concrete value unresolved |
| state+0x58 expected evv | fyb (8454) | <dwiw>k__BackingField | 47307 / 9 | 0x58 / fso interface (8272) | Offset verified; evv implements fso; live concrete value unresolved |
| state+0x68 expected fxw | fyb (8454) | <dwiy>k__BackingField | 47309 / 11 | 0x68 / frw interface (8254) | Offset verified; fxw implements frw; live concrete value unresolved |
| rendered+0x150 expected ebm | fxw (8446) | <dwfl>k__BackingField | 47192 / 21 | 0x150 / ebm (3209) | Verified declared class and offset |
| step+0x78 entity reference | gpy (9014) | <eagz>k__BackingField | 50382 / 9 | 0x78 / CoreAnimated.Animated (38936) | Verified for gpy; subtype is not guaranteed, matching the source's separate AnimatedEntity assignability test |
| step+0x1D Boolean check | gpa (8972), inherited by gpy | <eacr>k__BackingField | 50216 / 3 | 0x1D / Boolean | Verified for gpy ancestry; completion-state meaning not established by field name alone |
| step+0x71 Boolean write | gpy (9014) | eagy | 50381 / 8 | 0x71 / Boolean | Verified for gpy; native body corroboration below; disposal/completion semantics not fully proven |

`AnimatedEntity` directly inherits `CoreAnimated.Animated`, which inherits
`System.Object`. `gpy` directly inherits `gpa`, which inherits `System.Object`.
`frv`, `fso`, and `frw` have interface type flags `0xA1`. Thus calling the
interface-typed fields mismatches merely because their declared names differ
from `fyb`, `evv`, or `fxw` would be incorrect. Conversely, interface compatibility
does not prove that these exact concrete instances populate the current fight.

`animations.cpp` does not itself check the fight object's class before reading
+0x18; this report attributes that field to `fxc` because the scoped appearance
code explicitly resolves and checks `fxc` for the same module fight reference.
An object of unknown class is not assigned an `fxc` layout by this audit.

## Other raw fields in the two scoped sources

The following completes coverage of raw managed instance offsets in
`appearance-refresh.cpp`; repeated reads in `Owners::Current`, `Root`, and
`RefreshCombatAppearance` refer to the same layouts.

| Native use | Declaring owner | Exact field | Row / ordinal | Current offset / type |
| --- | --- | --- | --- | --- |
| rendered services | exx (7575), inherited by fxw | drmi | 42815 / 0 | 0x20 / fsm (8270) |
| creature services | exf (7547) | drig | 42633 / 0 | 0x10 / fsm (8270) |
| root entity self-reference | Animated (38936) | <fhsz>k__BackingField | 186605 / 58 | 0x1A8 / Animated (38936) |
| entity services | AnimatedEntity (997) | dhiv | 6947 / 51 | 0x398 / fsm (8270) |
| non-null entity look | Animated (38936) | fhst | 186598 / 51 | 0x170 / EntityLook (38959) |
| non-null animator | Animated (38936) | fhro | 186567 / 20 | 0xA8 / Animator2D (39214) |
| non-null transform | Animated (38936) | fhtf | 186611 / 64 | 0x1E0 / Transform (29223) |

The services fields have the same declared class and offsets expected by the
source. Static metadata does not prove equality of their runtime values, nor
that the root field points to self. The source checks those relationships.
The complete rendered ancestry is `fxw -> exx -> gov -> gow -> System.Object`.
`fxc` also inherits `gow`; the fight context field is declared in `fxc` itself.

## Registered field-table evidence

| Declaring owner | Type index | Field-table VA |
| --- | ---: | --- |
| AnimatedEntity | 997 | 0x18593D370 |
| Animated | 38936 | 0x18597A0C0 |
| fxc | 8421 | 0x18599E190 |
| fyb | 8454 | 0x18599FE10 |
| fxw | 8446 | 0x18599EBE0 |
| gpy | 9014 | 0x1859A9E70 |
| gpa | 8972 | 0x18599AAB0 |
| exx | 7575 | 0x18599D070 |
| exf | 7547 | 0x18599C270 |

For example, Animated ordinal 61 reads offset 456 (`0x1C8`) from
`0x18597A0C0 + 61*4`; gpy ordinal 8 reads 113 (`0x71`) from
`0x1859A9E70 + 8*4`. This is registered native layout evidence, not managed DLL
sequential-layout estimation.

## Animation-step attribution and corroboration

The scoped source explicitly comments on `gpy.bmat`, and the compositor report
already identifies current `gpy..ctor` row 81025, token `0x06013C82`, RVA
`0x2056210`. The same token-slot calculation maps `gpy.bmat` row 81024,
zero parameters, token `0x06013C81`, to RVA `0x20565A0`.

Selected current file-backed x64 instructions corroborate the layout:

| RVA | Instruction |
| --- | --- |
| 0x205626D | mov qword ptr [rsi + 0x78], rbp (constructor) |
| 0x2056610 | mov rcx, qword ptr [rsi + 0x78] (bmat) |
| 0x2056A03 | cmp byte ptr [rsi + 0x1d], r12b (bmat) |
| 0x2056AA5 | mov byte ptr [rsi + 0x71], 1 (bmat) |

The supplied source's hook is installed directly at old RVA `0x20853E0`, and
completion is resolved directly at old RVA `0x205E360`. This audit did not map
either old entry to a current managed method or prove its ABI. The current
`gpy.bmat` mapping does not resolve the identity of that hook argument. The
source's comments about old locations `0x2075C45`, `0x20758C8`, and `0x2075C49`
are not current instruction evidence. No claim is made that replacing addresses
with the values above would produce a correct hook.

## Exact limitations and completion boundary

All nine requested offset targets are addressed: six have directly mapped
layouts (including interface-qualified service links), and three step targets
have verified gpy layouts with unresolved hooked-owner attribution. The extra
raw fields from the scoped appearance file are also mapped. `entity+0x310` is
included as requested even though neither scoped file directly dereferences it;
its consumer and validation logic outside those files were not audited.

No numerical mismatch was observed in these tables. Remaining unresolved facts
are live service concrete identity/value equality, self/root relationships,
field semantic meaning beyond exact names/types and selected instructions,
actual current owner at the shipped animation hook, hook/completion ABI, and
completion/disposal ordering. No runtime behavior or compatibility approval
follows from a stable layout. No application tests or native build were run:
this deliverable changes prose only, and execution would not resolve the static
owner/ABI limitations within the requested read-only boundary.
