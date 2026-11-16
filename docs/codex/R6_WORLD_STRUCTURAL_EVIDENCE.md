# Round 6 — world HUD / CombatInstant structural evidence

Scope: offline Dofus metadata and existing source artifacts only, 18 October 2026. Verdict: **BLOCKED_STRUCTURAL_PROOF** for WorldIdOverlay and CombatInstant. No native mod implemented because no exact attributed hover/map/movement method or ABI was established. ItemIdOverlay and NativeTurboRuntime.cs are untouched by this work. No client attachment, injection, gameplay action, network access, dotnet build or git commit.

## Inputs and provenance

- Local Dofus metadata: `Dofus_Data/il2cpp_data/Metadata/global-metadata.dat`, 43,791,420 bytes, header version **39**, SHA-256 `b04b4cee266b0a9f0aa6023b15e389e3c0dec6f054947288d8511416dbcf8a36`, verified this pass. Header version 39 is not interpreted as a guessed record layout.
- Local `GameAssembly.dll`: SHA-256 `e08da74cfcac126a4d0929bf76d79b7c4f45b902ece082041b55d2715db9bf13`, verified against `artifacts/ghidra-dofus-long-20261018/run.ps1`. Build attribution is the existing R6 artifact/bundle claim, not a new gameplay certification.
- `artifacts/ghidra-dofus-long-20261018/summary.json`: 308,283 functions; `continuation-summary.json`: 313,735 functions. Both explicitly report analysis timeout. They are separate analysis snapshots, not method-signature proof.
- `navigation-probe.txt`: `MATCHING_DEFINED_STRINGS: 0`, `CANDIDATE_FUNCTIONS: 0`. Three sample decompilations test the database only; they do not identify navigation or hover methods. No reanalysis or binary dump was launched.

## Exact literal evidence, with file offsets

Generated report: `artifacts/R6-world-structure/metadata-candidates.json`. All **453** matching occurrences emitted with cap 1024, `truncated=false`. Topic counts: hover 182, map 153, animation 119; an occurrence can belong to several topics, so these counts are not additive. They include UI/framework false positives.

| Identifier | Metadata file offset | What remains unknown |
| --- | --- | --- |
| `mapId` | `0x22629e` | Declaring type, field vs parameter, type and active map semantics |
| `entityId` | `0x22863c` | Owning record and instance/template identity |
| `EntityMovementParam` | `0x2294c1` | Type definition, members and movement consumers |
| `Core.Rendering.Entity.CoreAnimatedEntity.MovementBehaviors` | `0x2294d5` | Namespace/type attribution and method mapping |
| `m_cellIdColorOnHover` | `0x230994` | UI field ownership; not proof of a hover event |
| `OnMouseEnter` / `OnMouseOver` | `0x230b86` / `0x230b93` | Declaring classes, parameters, native entries and cell relation |
| `OnPointerEnter` | `0x2d30b9` | UI/framework vs world callback and exact event type |
| `SetAnimation` | `0x2f9e78` | Owning class, overloads and native mapping |
| `MapPoint` | `0x300c45` | Owning assembly, layout and coordinate/cell semantics |
| `InputChecker` | `0x31a486` | Attributed definition and fight-state member |
| `Animator2D` | `0x665ad1` | Definition, overloads and target-build mapping |

These are NUL-delimited identifier occurrences, not parsed IL2CPP method records, native addresses or callable signatures. Neighboring strings do not prove a declaring-class relationship. Obfuscated names can be missed. No absence verdict follows from a literal search.

## Source evidence and specific gaps

`native/CombatSkipper/src/lib.rs:455` requests `Ankama.Animator2D.dll / Ankama.Animations / Animator2D`. Its resolver requests instance `System.Void(System.Single)` and up to two address-distinct entries for instance `System.Void(System.String,System.Boolean,System.Boolean,System.Int32)`. The method names in diagnostic labels are not name constraints: the resolver receives `None` for the name. This source shows the intended matching contract, not that any particular target method is Run/SetAnimation, nor that movement completion is safe to skip. No CombatInstant hook can be derived from it.

`native/dofus3-native-host/mods/native-tactical/src/lib.rs:304` requests `Core.dll / Core.Rendering / MapRenderer`. Lines 366–367 choose the **first** field of type `System.Int64` as `map_id_field`. There is no uniqueness or semantic proof in that selection. Metadata/root candidates are gathered separately by type. Do not reuse this selection as an authoritative map-ID resolver for the HUD. This worker leaves the tactical implementation unchanged.

`tools/R6-FightClassProbe.js` is runtime Frida tooling and was only read, never executed. It filters fight/movement classes, so it is not a world-hover inventory. It enumerates at most 72 methods per class and prints at most 36 matching rows, while its final `truncated` reflects only the class cap; output completeness is therefore not established. It does not emit native addresses, declaring-type ownership, storage flags or field layouts. A unique-looking line from that output cannot establish uniqueness or an ABI. No MethodInfo first-word pointer assumption was added here.

## Offline probe and reproduction

New `tools/R6-WorldMetadataProbe.ps1` reads only the explicitly supplied metadata file. It caps the input at 64 MiB, verifies SHA-256 against the pinned R6 fingerprint by default, checks the metadata magic, and inventories NUL-delimited ASCII identifiers with exact byte offsets. It emits no arbitrary content, instance data or method arguments. Candidate output is capped (default 256, maximum 1024), with complete matching counts and explicit truncation. The generic header version is reported, never used to guess a record schema. All proof/authorization flags are always false. A different expected fingerprint can be supplied for synthetic fixtures or an explicitly reviewed artifact; matching it alone does not attribute a Dofus build.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-R6-WorldMetadataProbe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/R6-WorldMetadataProbe.ps1 -MetadataPath '%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\Dofus_Data\il2cpp_data\Metadata\global-metadata.dat' -MaxCandidates 1024
```

Validation: **6/6** synthetic behavioral checks passed in PowerShell 7 and Windows PowerShell 5.1: adjacent identifiers and exact offsets, hover classification, no promotion, honest cap/counts, wrong-hash rejection, wrong-magic rejection. Local metadata execution produced the 453-occurrence report above. These tests validate the offline inventory only.

## Required next evidence, without live work in this task

Obtain an existing attributed offline metadata/type/native-method export supporting header 39, tied to both hashes above. Require assembly + namespace + class + inheritance, full method/field records, flags, exact types, field offsets and documented metadata-to-native mapping. Preserve duplicate overloads and shared generic native addresses; explicitly mark incomplete enumeration. Then review hover enter/leave, map-change lifecycle, cell/entity ID provenance, and movement completion/cancellation in attributed call sites. A missing export, unknown layout, ambiguous candidate or semantic gap keeps both modules blocked. `native/WorldIdOverlay/README.md` captures these gates for integration.
