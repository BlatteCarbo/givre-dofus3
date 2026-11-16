# Targeted owned-branch diagnostic, 2026-11-02

This increment adds read-only evidence before the existing broad field graph.
It runs only through the existing deduplicated failure capture, after the same
enabled lease, unique controller, actual map ID, expected map ID and missing
supported-root checks. Deployment and live capture remain with the lead.

## Static inputs, not live ownership proof

The pinned static investigation recorded at the end of
`RECOVERY_COMBAT_20261102.md` identifies `epf.doqa` as `ue`, with
`ue.dgjn` a MapController backlink, `ue.dgjp` an `ub` reference and
`ue.dgjm` a GameObject. `ub.qik` at `0x181f8c470` returns stored Transforms
`dgii`, `dgik`, `dgim` for layers 0, 1, 2 respectively. The fourth pair has
no proven selector. Runtime owner identity and scene roles remain unproven.

## Output labels

Every line uses the existing `Native Tactical diagnostic:` prefix.

- `OwnedBranches BEGIN` / `OwnedBranches END` delimit the targeted capture.
- `OwnedBranches accessor.doqa` reports the runtime class; traversal requires
  exactly `ue`.
- `OwnedBranches ue.dgjn` reports its class, availability and
  `same_reference_as_current_controller`.
- `OwnedBranches ue.dgjp` reports the runtime class; branch traversal requires
  exactly `ub`.
- `OwnedBranches ue.dgjm` describes the separate GameObject candidate.
- `OwnedBranches ub.dgih/dgii layer=0`, `ub.dgij/dgik layer=1`,
  `ub.dgil/dgim layer=2`, `ub.dgin/dgio layer=unproven` report
  `game_object_transform_equals_stored_transform`, then describe each
  GameObject and stored Transform separately, including runtime classes.
- Candidate descriptions include `same_transform_as_named_background`,
  `named_background_in_ancestry` and `ancestry_truncated`, plus the existing
  bounded names, children, local/world transforms and ancestry output.

Equality values are `Some(true)`, `Some(false)` or `None` (unavailable).
The ancestry relation is only within the existing eight-node limit; a false
value with truncation is not proof that no relationship exists farther up.
Missing fields, null references, signature mismatches and unsupported runtime
types produce unavailable evidence without retries outside the existing
capture deduplication. Named MapBackground remains an optional candidate.

## Host reference contract and limits

The inspected host bridge `crates/host/native/unity_bridge.cpp` returns raw
IL2CPP object references from `dnh_unity_runtime_invoke`,
`dnh_unity_field_get_value` and `dnh_unity_find_objects`. Metadata class,
field and method handles are UnityResolve wrappers; those wrapper identities
are not used for object equality. Non-null raw object references can be
compared directly for the requested controller backlink and Transform
reference equality. This does not compare Unity native instance IDs or claim
that distinct managed wrappers represent distinct native engine objects.
No unresolved host wrapper-reference conversion is needed for these reads.

New reads resolve exact instance field names and declared reference types,
then use the host field API with a DnhHandle output slot. They never fall
back to unknown property getters or directly dereference pinned offsets.
Unity objects are runtime-type checked before transform inspection.
Lease revocation is checked before the targeted capture and between its
bounded candidate descriptions. The existing 8 ancestors, 12 children,
12 broad objects, 512 fields, 48 fields/class, 3 classes, depth 2,
128 renderer samples and 24 renderer paths are unchanged. Targeted work is
fixed at one ue GameObject and four GameObject/Transform pairs.

No discovered reference admits a root, changes suppression, calls a setter
or unknown client method, instantiates or destroys scene objects, or changes
any lease/map gate. No live client actions, deployment or pin updates occur.

Existing diagnostic and tactical tests were read before editing. No Rust/FFI
skill files were available in the searched skill directories; the actual
SDK and C++ bridge contracts were inspected directly.
