# Tactical hierarchy diagnostic

## Follow-up after PID 16248 capture

The live `native-host.log` starting at line 30 reports module 5.0.5 loaded
successfully and `actual_map=54165809`. The controller is a childless
`MapController` under `MapRoot_54165809`, whose direct children are
`MapController`, `MapAnimatedElements`, `MapParticles`, and
`PostProcess(Clone)`. Both controller and root have identity transforms.
The renderer sample instead contains meshes under a separate top-level
`MapBackground`; the warning still reports `map_54165809` absent and an
empty static `parentTiles`. The controller ancestor is therefore not a
proven replacement for the visual root. Sampled renderer names do not
establish ownership, and identity transforms do not establish cell units.

The follow-up increment keeps root admission, cell placement, suppression,
fresh-map leases and restoration unchanged. It adds read-only evidence:

- Explicit transforms and ancestor names for the named `MapBackground`,
  static root, and acquired tactical prefab, without admitting any of them.
- Up to 48 declared method signatures each for the controller and its map
  service, including return and first four parameter types. No unknown game
  method is invoked to guess a coordinate conversion.
- A bounded field graph seeded by the controller, its service, map data,
  tactical prefab component, and cell data at indices 0, 14, and 559 (only
  when the existing collection reports exactly 560 entries). Cell indices
  are samples, not assumed cell IDs; field output includes the actual IDs.
- At most 12 objects, depth 2, three declaring classes per object, 48 fields
  per class and 512 fields total. Reference objects queued for inspection
  are GC rooted for the capture. Field values use the host's boxing API,
  never a guessed field buffer size. Scalars include Int32, UInt32, Single,
  Vector2 and Vector3. Unity references include their name, transform and
  ancestry. Capture-local object indices expose shared service references;
  `same_transform_as_named_background` explicitly compares Unity references
  with the named candidate, rather than inferring equality from names.

The existing lease identity and live-owner check is repeated before each
field or method record, in addition to the existing capture checks. The
capture remains deduplicated by diagnosis and map identity. This is not a
visual fix and has not established an owned background root or cell frame.

### Next authorized live validation

The live-host owner should unload the old tactical module using the normal
restoration path, load the tested release artifact, and provide the existing
fresh enabled map-ready lease. This source increment does not change any
pin, deployment configuration or live process. Capture the complete next
`Native Tactical diagnostic: BEGIN` through `END` block, including
`FieldObject`, `MethodMetadata`, candidate transforms and `Fields END`.
Do not judge visual completion from the build or diagnostic load.

Required evidence before admitting a new root is a reference path from the
current controller/map service to the background and the map-ID ownership
of that service, checked again after a map transition. Required cell-frame
evidence is a concrete conversion contract or cell positions for known IDs
in that owned transform space, together with the prefab transform. If the
bounded field graph or method sample is truncated before those facts,
report the exact truncation and unresolved class/field rather than hiding
all renderers or parenting a guessed grid. After any eventual visual fix,
verify placement and decor suppression in the game, transition maps, and
disable tactical mode to verify exact renderer restoration.

This is evidence collection, not a tactical visibility fix. The existing
static-root and named-root admission checks, placement, suppression, lease
revocation and restoration paths remain unchanged.

## Capture trigger and output

After an authorized load, the module must receive its existing fresh,
enabled, live-owner map-ready lease and acquire the tactical prefab. A map
resolution failure is probed by the existing 500 ms map-probe path. When its
diagnosis changes (or the map lease identity changes and resets the gate),
the module logs the existing warning and attempts one read-only capture.
Heartbeat expiry renewal alone does not reset the gate. Unchanged failures
do not recapture every frame or every probe. Revocation followed by a new
valid lease resets the gate through the existing map-change handling.

Capture requires one MapController, a readable correctly typed map service,
and an actual ClientMapData ID matching both the current lease and the state's
expected map. It runs only if `map_snapshot` still fails. Permission is reread
before capture and between bounded ancestor/renderer operations; revocation
aborts the capture. No capture is attempted on load without a lease, on
disabled leases, or against a different actual map ID.

Output uses the existing host logger. For the current client session it is:

```text
%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/NativeTurbo/18668/NativeMods/native-host.log
```

Every evidence line starts with `Native Tactical diagnostic:`. `BEGIN`
includes the lease identity token, actual map ID and capture limits. It is
followed by MapController ancestor descriptions and renderer samples, then
`END` with enumeration counts, or `ABORT lease revoked`. Unsupported optional
values are explicitly `unavailable`; an absent active-state getter produces
an explicit renderer-path-unavailable line instead of treating unknown state
as active. No separate capture file or mailbox command is required.

## Evidence bounds and interpretation

- Up to eight MapController transform ancestors, including its own transform.
- Up to 12 direct child names per described transform; total child count is
  also recorded, so a limited list is distinguishable from an empty object.
- Local position, quaternion rotation and scale; optional world position,
  quaternion rotation and lossy scale on each described transform.
- The first 128 Renderer references are sampled through the existing host
  find-objects API, with at most 24 active-in-hierarchy, enabled renderer paths
  emitted. Each path includes up to eight ancestors, a truncation flag and
  the renderer transform description. Enumeration ordering is not guaranteed.
- Object names are limited to 96 Unicode characters and control characters
  are replaced, preventing names from injecting extra log lines.

The host's find-objects API bounds copies to the provided output capacity but
internally enumerates Unity objects; this source-only change cannot bound that
underlying host operation's runtime. The diagnostic does not call visibility,
shared-material, setter, instantiation, destruction or renderer-toggle APIs.
World getters and active-state lookup are optional only within the diagnostic;
no previously required binding has been weakened.

The renderer sample is scene evidence, not proof of map ownership. Missing
terrain in a limited sample is not proof it is absent. Use the MapController
relationship, map ID, ancestor paths and transforms to determine whether a
later discovery change is justified. No discovered object is admitted as a
root by this diagnostic.

The parent session owns live loading/capture after verifying the built module
and current session. No live load or screenshot was performed for this change.

## Build evidence

`cargo test -p dofus-native-tactical && cargo build -p dofus-native-tactical --release`
exited 0: ten tests passed, zero failed or ignored; release build succeeded.
The two diagnostic tests cover changed-failure deduplication/reset and bounded,
single-line names. They do not simulate the Unity hierarchy or prove that the
optional getters exist in the live client. The tracked-source whitespace check
also exited 0.

Built artifact, relative to `native/dofus3-native-host`:
`target/release/dofus_native_tactical.dll`.

SHA-256:
`6d13faa2a8a552dd4df3c3b5ab46f23788ca69692a8a1a3af2fd989cc79bd88f`.

No version or catalog pins were changed. Verify this artifact identity before
parent-controlled loading; a later rebuild may replace the file.
