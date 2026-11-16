# Tactical interactive preservation: remaining evidence

The current candidate does not implement safe scenery suppression. Parent
inspection of native-tactical/src/lib.rs confirms suppress_map returns false
when the construction association is unavailable. The completed-cell branch
then calls disable_current_map, marks failure and returns. Constructing 560
overlay sprites is not successful tactical activation.

The existing suppress_branch only disables renderer identities explicitly in
its scenery allowlist, records their previous enabled state and roots them
before writing. Removing that membership check or suppressing a whole layer
would defeat the interactive-preservation contract.

The parent checked the original trial evidence in
official-client-371317-20261103.md: the diagnostic candidate classified zero
batches and zero eligible renderers. Post-load staging was empty, not proof
that all scenery was safe. The earlier user observation recorded interactive
objects disappearing; disabling restored ordinary scenery. Neither trial
establishes a safe ownership filter.

A read-only investigation (st_01a1aae7) identified construction-time association
as the next evidence boundary. Its findings are an investigation, not a code
delivery. It changed no files and ran no build or tests. No candidate was
loaded or account operated during this follow-up.

Required before implementation can safely suppress original scenery:

- Capture complete source membership for the actual surviving renderer at a
  proven exclusive assignment boundary. qii may compact the pool, so an index
  zip after load is invalid.
- Establish synchronization covering the pool and batch mutations, and validate
  the concrete generic assignment binding and hidden ABI on the target client.
- Preserve HDV, service and door landmarks as well as registered harvestables;
  absence from an interactive registry is not evidence of ordinary scenery.
- Support hook disable/re-enable without reclaiming live callback code or
  permanently vetoing the host lifecycle. Late enable without captured ownership
  must remain a capture miss.
- Exercise visible grid, landmarks, restoration, re-enable and map transition
  against the intended candidate. Unit tests and ownership of a layer do not
  establish those live properties.

Until that evidence exists, retaining all original renderers and refusing
activation remains the correct current behavior. The interactive-preservation
todo stays open. This note does not claim the broad tactical goal complete.

## Parent follow-up on construction capture

The read-only follow-up st_01a1ab50 changed no files and ran no tests. The
parent independently opened the SDK hook/generic methods, host teardown,
actual suppress_branch/suppress_map, and the retained ABI and compacting-pool
evidence. The basic hook facility exists: mod-sdk/src/lib.rs provides
create/enable/disable/remove_hook and inflate_generic_method. Missing a hook
API is therefore not the blocker.

The concrete association boundary remains ub.qil<T> entry, which receives
the selected MapElement and its actual batch. The retained static ABI report
records ten Windows x64 native arguments including the incoming hidden context;
that context must be forwarded unchanged. It does not verify successful
concrete merged/isometric generic resolution on the deployed host. Captured
identities would still need reconciliation at a proven enclosing completion,
not pairing compacted pool indices at qii exit.

Host teardown sets active=false before preparation; a preparation veto retains
the DLL but does not keep normal ticks running. Hook removal is not by itself
a callback quiescence proof. No inspected contract establishes exclusion for
foreign/reentrant construction, source-list reads, reassignment and teardown.
Likewise, no positive classification yet proves an ordinary-scene batch lacks
HDV, service or door landmarks. This follow-up therefore does not justify
installing a capture hook or weakening the explicit scenery allowlist. The
actual suppress_map still logs unavailable association and returns false.

## Executed temple-door source classification, 2026-11-06

`tools/TacticalDoorProbe` is a read-only offline reader restricted to map
241445377 and cell475 in mapdata_assets_world_921.bundle. The first build
failed because DirectoryInfos is a List, not an array; Count fixed that
compiler error. The corrected run bash_398 exited0 against the installed bundle
SHA256 f68744fa58bb9fb5c2fd3be3489af27b50f483f2c484d66564b036873115c5df.
It found exactly one target asset, file index0, pathId7033127060226027223.
All root source collections were traversed, including mapAnimatedElements.

Cell475 contains six separate singleton middleground merged groups, indices
122-127. Their member RIDs are2413726569231328180-2413726569231328185.
The first five resolve to ClientMapElement; the sixth resolves to
ClientInteractiveMapElement, gfx806735, interactiveId541737, displayBehaviour0.
That last group's materialIndex is4, uniqueMaterialInstance1, displayOrder5,
isStagingTarget0 and stagingId empty. Selected group references have zero
unresolved RIDs. Registry duplicate-definition count and source-group repeated
membership count are both zero in this map.

This establishes a concrete interactive source group at the documented door
action cell, not a mixed group. Five base-class groups share the same cell:
cell identity alone cannot distinguish scenery from the interactive source.
The classification does not prove that base-class members are safe scenery,
nor establish a link from the classified source to a surviving runtime renderer.
No runtime hook, suppression, client launch or account operation occurred.
The explicit renderer allowlist and fail-closed activation remain unchanged.
