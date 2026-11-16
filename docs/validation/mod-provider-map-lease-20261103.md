# Visual provider and tactical map lease verification

Release --no-restore run filtered to CombatVisualModPolicyTests,
TacticalMapLeaseTests and MitmTurboSourceTests: exit 0, 22 passed, zero
failed or ignored, duration 1.5 seconds. This is offline evidence only.

## Provider conflict

BotsViewModel.CombatVisualMods.cs disables standalone visual controls when
DofusEnhancedEnabled is configured and displays the configured provider.
NativeTurboRuntime.RequestState and EnableAsync independently check
EnhancedOwnsHooks. PrepareForEnhancedAsync requests standalone teardown and
refuses the switch if a competing native module remains loaded.

The identified status discrepancy was that EnhancedOwnsHooks also recognizes
a loaded DofusMiniatures.dll when Enhanced startup has not failed, even when
DofusEnhancedEnabled is false. The view-model gate and provider description
only inspected the configuration flag. In this case the runtime refused
standalone activation while the UI could still offer it. The view-model gate
and refresh status now call the same EnhancedOwnsHooks method as RequestState
and EnableAsync; its visibility changed from private to internal for these
production callers. No second provider predicate or cached provider state
was added. The status now describes Enhanced as configured or loaded.
Compilation and the existing 22-test filter completed after this change:
exit 0, 22 passed, zero failed or ignored, duration 11.0 seconds.
Those tests do not exercise the loaded-provider UI case; real-surface
verification remains outstanding and the provider task remains open.

## Map lease

PublishNativeMapReady excludes Socket and suspended links before publishing.
Publication binds the map to the current Dofus process start time and gives
a changed map a new epoch. RefreshTacticalLeaseLocked requires matching
desired state, process identity, ownership and map readiness. Publication,
revocation and heartbeat share TacticalMapGate. A superseded heartbeat checks
its generation under that lock before touching its successor's lease.
The lease records a 2250 ms expiry; heartbeat interval is 750 ms.

MarkCharacterLoading revokes readiness. Additional revocation calls exist
in combat-map, game-link and packet handling. These call sites alone do not
prove that every live map transition restores the renderer.

TacticalMapLeaseTests verifies invariant-culture token formatting and changed
generation/epoch tokens, not lease publication, revocation or native renderer
restoration. MitmTurboSourceTests includes source-text and pinned-binary
checks; those are not behavioral map-transition tests. Activation after map
and restoration after revocation remain open for stronger verification.

No client was started, attached or injected for this verification.

## Isolated application observation

An application-only QA instance was opened using the current Release build
and GIVRE_CLONE_RUN_NAME=qa-mod-provider-20261103. No account was added and
no Dofus client was launched. The Windows accessibility tree identified
GivreClone v1.3.3.11, the language chooser, and first-run map extraction
(2300/17722 at observation). French was selected. A subsequent accessibility
button lookup timed out after 60 seconds; the QA process tree was stopped
through its owned bash session. The provider panel was not reached, so no
visual/provider PASS follows from this attempt. The tool's screenshot was
not visually available to the model and is not claimed as inspected evidence.
The first launch command had failed to set its environment variable because
of shell quoting; that process tree was stopped before the corrected launch.

A second application-only attempt reused the same isolated run name with the
current Release output. Windows AX observed PID 16984, window 2885506,
the language chooser and extraction at 7951/17722. Selecting French and
continuing extraction in the background succeeded through AX. The local
navigation search received `mods`, but no Mods destination was exposed by
the subsequent control lookup. A later AX tree reported `Aucun bot en jeu`
and the combat profile controls; it did not establish a selected graphical
client or a provider conflict. The owned QA process tree was stopped via
bash_656. No account, game client, injection or provider switch was started.
Screenshots again were unavailable to the model, so neither appearance nor
the loaded-provider UI case is marked PASS. The provider task remains open.

The native consumer was subsequently located at
native/dofus3-native-host/mods/native-tactical/src/lease.rs. Its existing
tests cover exact expiry, TTL bounds, changed ownership/map identity,
disabled cleanup tokens and actual file removal with a live owner process.
The cargo test run (`cargo test -p dofus-native-tactical --lib -- --nocapture`)
completed with exit 0: 17 passed, zero failed or ignored, duration 0.03 seconds
after a 0.67-second build. These consumer
checks still cannot establish renderer restoration or managed publication
on every game-map transition.

Current Debug regression check under bash_740 ran CombatVisualModPolicyTests,
MZP25NativeModPolicyTests and TacticalMapLeaseTests with --no-build --no-restore:
15 passed, zero failed or skipped, exit 0, test duration 2.0 seconds (2.6 seconds
for the command). These are overlapping policy/token checks, not a new native
renderer or loaded-provider UI scenario. The existing pending live checks above
remain pending. Reading provider-diagnostic.png again confirmed the current
model cannot receive image pixels; no visual verdict was added.

## Revocation is suspension, not immediate restoration

DNM_Tick returns immediately when lease::read fails: it clears last_lease
and performs no scene cleanup while off-map. Thus expiry or deletion revokes
scene access, but does not itself restore already suppressed renderers.
On the next valid lease, changed is true and disable_current_map(state, true)
runs before following the new map. It destroys the retained overlay and
restores each retained renderer's recorded was_enabled value only if Unity's
Object.op_Implicit reports that the object is still alive, then frees roots.
A valid disabled lease permits cleanup and writes the matching restored ACK
after pending prefab work drains. Unexpected unload only frees roots.

Consequently, the passing lease-file deletion test proves immediate loss of
permission, not immediate visual restoration. Whether suspension leaves a
visible overlay during a particular loading/selection screen, and whether
all surviving layers restore correctly on return, still requires a renderer
harness or a real client transition. No claim of immediate restoration after
revocation is made.
