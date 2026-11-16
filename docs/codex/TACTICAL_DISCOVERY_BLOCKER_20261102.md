# Tactical renderer discovery: missing runtime evidence

## Scope and result

Read-only source and existing-log investigation on November 2, 2026. No
tactical source was changed, no DLL was loaded, and no client process was
stopped. The actual module directory is
`native/dofus3-native-host/mods/native-tactical`, not `mods/tactical`.
No `AGENTS.md` was found at the repository root, its ancestors, or under
`native`. The recovery notes were read before examining tactical source.

The available evidence does not justify a renderer-discovery correction.
In particular, replacing a root name or searching every renderer would not
establish either map ownership or the transform used to place tactical cells.

## Observed failure and source mechanism

The existing host log is:
`%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/NativeTurbo/18668/NativeMods/native-host.log`.

Its lines 25-30 confirm that the tactical Unity object/transform/renderer
bindings resolved and the tactical prefab was acquired. Line 31 reports:

```text
map_54165809 not found; static root: L0 'parentTiles' [0]: .
```

`native-tactical/src/lib.rs` establishes the following chain:

- `map_snapshot` reads the unique MapController's service, its ClientMapData,
  and the static GameObject field. It requires the data ID to equal the lease
  map ID before considering either root.
- `has_map_visual_branch` admits only a root with a direct
  `MergedBackground`, `Layer_0`, or `MergedForeground` transform branch.
  Those names are also understood by the existing suppression path.
- If the static root fails that check, `GameObject.Find("map_<id>")` is the
  current fallback. Existing runtime evidence shows that fallback fails.
- `child_names` already uses Transform child count, GetChild and get_parent
  to report up to five hierarchy levels. The single L0 result shows no parent
  was returned for the empty static root. Walking farther upward from that
  object therefore has no evidenced route to the active visual root.
- `begin_overlay` and `copy_transform` copy the selected normal root's parent,
  local position, local rotation and local scale. Root selection determines
  tactical placement as well as renderer suppression.
- `process_build` revalidates the map snapshot and suppresses only known
  branches after all 560 cells build. `restore_renderers` retains enabled
  states and uses Unity's destroyed-object check before restoration.

The current guard is consequently an evidence-based refusal to build against
an empty root, not proof that all renderers should be hidden. The inspector
load failures in `RECOVERY_COMBAT_20261102.md` produced no renderer capture.

## Exact evidence needed before a correction

A read-only capture from the active client, under the existing valid map-ready
lease, must establish:

1. The current MapController GameObject path, transform parent/child hierarchy,
   and relevant local/world transforms, alongside its ClientMapData ID.
2. The active map terrain/background/foreground renderer GameObjects and their
   full transform ancestor paths, distinguishing them from fighters, UI,
   effects, and other maps. Renderer enabled/active state is sufficient for
   discovery; visibility and shared materials are not required.
3. The root or explicit branch relationship tying those visual objects to that
   MapController or its map service, including the frame in which the existing
   tactical cell coordinates belong.

The tactical module already demonstrates usable Component.get_gameObject,
GameObject.get_transform, Transform.Find/get_parent and
GameObject.GetComponentsInChildren(Type, Boolean) bindings. Its diagnostic
helper uses Transform.get_childCount/GetChild and Object.get_name. A reduced
read-only inspector can use those operations without requiring
Renderer.get_isVisible or Renderer.get_sharedMaterials. Capturing names alone
does not prove that a candidate is a map-owned suppression target.

No live capture was attempted because live loading was outside this task's
authorization. Once captured, select only an evidenced map-owned root or
branch set; preserve the lease ID check, revocation behavior, deferred cleanup,
per-renderer restoration and suppression-after-build ordering. A subsequent
authorized live check must verify visible grid placement, preserved actors/UI,
disable restoration, and map-change cleanup. Pure Rust tests and a release
build cannot establish those visual properties.

## Validation

From `native/dofus3-native-host`, the command
`cargo test -p dofus-native-tactical && cargo build -p dofus-native-tactical --release`
completed with exit code 0. All eight existing tests passed, with zero failed
or ignored tests; the release build completed successfully. This validates the
unchanged tactical source baseline, not a renderer-discovery fix.

The diagnostic document's whitespace check completed with exit code 0. No
new tests were added for this prose-only artifact. There was no live visual
verification, sanitizer run, or managed renderer hierarchy capture.
