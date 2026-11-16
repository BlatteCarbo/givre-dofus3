# Native tactical owned-layer visibility fix

## Evidence and admission

The live `NativeMods/native-host.log` for PID 20192 on map 54165809 confirms
`MapController -> epf.doqa -> ue`, a matching `ue.dgjn` controller backlink,
and `ue.dgjp -> ub`. The pairs `dgih/dgii`, `dgij/dgik`, `dgil/dgim` hold
GameObject/Transform references for MapBackground (34 children), MapMiddleground
(489), and MapForeground (6). Each stored Transform equals its GameObject's actual
Transform. All three branches are separate top-level roots with identity frames.
Static `ub.qik` evidence (token 0x0600222f, native 0x181f8c470) selects the same
three Transforms for layers 0, 1, 2. Existing cell positioning constants are retained.

Discovery requires exact instance-field names and declared reference types via
the host field API, runtime types, live Unity objects, matching Transform
references, distinct top-level roots, and the fresh enabled lease's actual map ID.
No name lookup or renderer sample admits a root. `parentTiles` and `map_<id>` are
not ownership sources. Diagnostic name lookup remains read-only.

## Lifecycle and suppression

The overlay copies the verified background's parent and local frame, making it
a sibling rather than a descendant of a suppressed branch. Three GC roots retain
the admitted GameObjects. Discovery is repeated while following the map and just
before suppression; any changed layer identity, controller or map invalidates the
overlay and restores original renderer enabled states before rebuilding.

Only Renderers within the three admitted branches are suppressed, after all 560
cells build. A renderer is modified only after its restoration GC root succeeds.
Normal cleanup frees layer and renderer roots; unload frees all retained handles
without managed calls after lease revocation. The existing owner-lease and ACK
rules are preserved. Actors, UI, MapAnimatedElements, MapParticles, postprocess,
and the unproven `dgin/dgio` MapElementsPool branch are never selected.

One success log per completed construction identifies the verified map, bounded
layer names and cell 0's read-back local position, sprite reference match and
renderer enabled state. Missing reliable read-back values remain explicit.

## Verification and remaining live gates

The existing Rust test module covers exact typed field signature rejection and
all three retained layer replacement slots, missing references and reordering.
Existing tests are retained. Requested commands use the isolated target directory
`%USERPROFILE%/AppData/Local/Temp/givre-owned-root-fix-20261102`:

```
cargo test -p dofus-native-tactical --target-dir %USERPROFILE%/AppData/Local/Temp/givre-owned-root-fix-20261102
cargo build -p dofus-native-tactical --release --target-dir %USERPROFILE%/AppData/Local/Temp/givre-owned-root-fix-20261102
```

The final task report records actual exits and DLL SHA256. Native IL2CPP/Unity
FFI cannot be exercised by pure Rust tests or Miri. The subsequent live capture
on map 54165809 verified loading, a visible aligned grid, and F8 restoration;
see `RECOVERY_COMBAT_20261102.md`. Re-enabling and map-transition behavior remain
unverified. No shipped DLL or catalog pin has been replaced.

### Additional lease boundary verification

On November 2, three additional tests in `src/lease.rs` extended the local
coverage without changing production behavior or touching the game:

- Each identity component independently invalidates the retained map binding:
  target process start, owner PID, owner process start, epoch and map ID.
  Both comparison directions and the acknowledgement token are checked.
- Expiry is exclusive at the exact deadline; the maximum allowed TTL and the
  final live millisecond remain valid even near `u64::MAX`, without arithmetic
  overflow.
- Overlong padded records, an owner PID exceeding `u32`, an expiry exceeding
  `u64` and a negative map ID cannot grant scene access.

The current native tactical test suite passed **17 tests, 0 failed, 0 ignored**,
exit 0, using:

```
cargo test --manifest-path %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/native/dofus3-native-host/Cargo.toml -p dofus-native-tactical --target-dir %USERPROFILE%/AppData/Local/Temp/givre-tactical-lease-20261102
```

These are lease parsing/comparison tests, not an execution of Unity cleanup.
The module remains limited to lease ownership and parsing (191 nonblank,
non-comment lines after this addition); no new FFI, dependencies or production
helpers were introduced. Live transition validation still requires Bastionbot to
be available without concurrent user input.
