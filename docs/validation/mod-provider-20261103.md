# Visual mod provider - November 3, 2026

The combat panel diagnostic now identifies Dofus Enhanced as the configured
provider when DofusEnhancedEnabled is true. This branch precedes standalone
module availability, so a missing standalone module no longer obscures the
configured provider. The message explicitly does not certify live activation.

NativeTurboRuntime.PrepareForEnhancedAsync first publishes disabled standalone
state, reconciles it, verifies process identity, then rejects any still-loaded
historical, visual-options, skipper, speedhack or tactical module. The existing
EnhancedOwnsHooks check also accounts for a loaded Miniatures module whose
startup did not fail. These are source observations, not a live conflict test.

Validation: 33 existing policy, native catalog and tactical token tests passed,
zero failures/skips, exit 0. The App build after the diagnostic change passed,
exit 0, with 226 warnings; they were not suppressed. The LSP remains unable
to enumerate artifacts/kourial-tools/pypdf, so compilation is the available
diagnostic, not a clean language-server claim.

The current Release executable was launched in the existing isolated offline
run. Its new main window PID is 680, native window handle 2950538. Navigation
through the real command palette to Bots > Combat succeeded. The accessible
surface reports Aucun bot en jeu, and the diagnostic panel is not available
without a selected bot. No live account was launched merely to expose this
message. The provider task remains open pending actual rendered diagnostic
and conflict verification. No screenshot appearance verdict is claimed: the
capture tool delivered an image attachment that this model could not inspect.

## Offline provider diagnostic capture

The Release RecoveredUiHarness --provider-capture run now exits 0 with
`PASS provider diagnostic capture and standalone exclusion; no client or network`.
It locates the production TextBlock in the compiled view's logical tree (an
inactive tab is not in its visual tree), binds the actual ViewModel, and
checks vertical text fit. With permissions and module availability held
constant, standalone controls are available without Enhanced and unavailable
with Enhanced. CanCombatVisualOptions now enforces that exclusion.

The fresh PNG is at
`%USERPROFILE%/AppData/Local/Temp/ulw-givre-100-20261101/provider-diagnostic.png`.
Neither the primary model nor the delegated visual assessor could receive
image pixels; no contrast, clipping or nonblank visual verdict is claimed.
The binding/layout assertions are evidence only for the isolated control,
not the full combat panel or live native-hook coexistence. The provider task
therefore remains open. The former QA window was closed before this run;
the process probe found no Dofus or GivreClone process to exercise live.

## Native lease coverage and remaining live checks

`cargo test -p dofus-native-tactical --offline` in native/dofus3-native-host
completed with code 0: 17 passed, zero failed/ignored. The real file-consumer
test verifies a live owner and immediate revocation; parser tests verify
expiry boundaries, target identity, owner/map changes and disabled cleanup
permission without activation. This is stronger than the one producer-token
test but does not execute Unity renderer restoration.

Source review confirms publication requires a positive ready map and owned
process identity, and revocation shares its lock with heartbeat publication.
On a retained-layer mismatch, follow_map_changes calls disable_current_map
with restoration enabled. That source path and passing lease tests do not
establish that a visible map is restored after a live transition. The live
activation/revocation tasks remain open for that observation.

## Live attempt: server requires a newer client

The current Release application (PID 19532) launched only the existing Bastionbot
QA account in MITM through its configured server-selection route. Dofus PID
7964 opened, but remained connecting with mapId 0 and no character observation.
Its actual NativeTurbo staging directory contained no tactical-map.lease.
The native host log recorded zero startup mods, then combat-visuals and item-ID
modules loaded through control. This establishes an off-map absence of the
lease only; tactical activation was not requested or proven by that listing.

Text extraction from a fresh Dofus window capture reported the explicit server
refusal: installed version 3.7.12.12 is invalid; version 3.7.13.13 is required.
The message says a maintenance may delay its availability in Ankama Launcher.
No connection success or map restoration is claimed. A compatible client
update and revalidation of native client hashes/signatures are prerequisites
for continuing this live scenario.

Basic text extraction of the isolated diagnostic PNG also recovered the
Enhanced provider/exclusion message. This does not change the outstanding
visual layout/contrast assessment or live coexistence verification.

## Post-maintenance retry

After the user reported maintenance finished, the current Debug application
started as PID 10680, using the existing givre-app-offline run. Its local
control bridge answered list_bots. connect_bot accepted the existing MITM test
account with AutoConnectType=0 (server-selection route); the acknowledgement
explicitly had completionConfirmed=false. Dofus PID 21528 opened from the
official installed path, but the native window title still reported
3.7.12.12 and the bot snapshot remained connecting with mapId=0.

This is not proof the required 3.7.13.13 update was locally applied. A question
is pending for the user to apply/check the launcher update; disconnect_bot
was requested under bash_796 so the owned test client does not lock its files.
No bypass of the server version check or live combat success is claimed.
