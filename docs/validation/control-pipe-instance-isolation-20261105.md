# Local control instance ambiguity

Read-only probe of the existing per-user control pipe first returned app
status with five bots and transport log 05-02-19; a subsequent list_bots
returned zero bots and app status then named log 18-17-24. No account command
was issued. These replies cannot be combined into one instance snapshot.

ControlPipeServer permits eight named-pipe server instances. The default name
depends only on the Windows user, not the validation run. LocalControlBridge
and ControlPipeClient already honor GIVRE_CONTROL_PIPE. Setting only
GIVRE_CLONE_RUN_NAME isolates data but does not isolate this endpoint.

For all subsequent parent-owned QA launches, set both variables before launch:
GIVRE_CLONE_RUN_NAME to the chosen QA profile and GIVRE_CONTROL_PIPE to a
distinct bounded name for that QA run. Set the same explicit pipe on its
observing client. Do not use the per-user default for commands while historical
QA processes remain attached. No production routing contract was changed.

Outstanding: retire historical owned QA listeners (one is elevated after reload),
verify a new isolated listener returns only its own state, and identify the live
instance independently before game commands. The legacy live PID14216 is not
an owned QA process to terminate. Existing profiles and account data are retained.

Isolated launch verified: PID26436/window5048408 on explicit pipe
GivreWork-parent-isolated-20261105 answers get_app_status with zero bots,
zero supervisors and its new transport log19-08-55. The launch used the verified
parent-reload-ui Release executable and parent-final-shell profile. This proves
the existing override reaches a distinct QA listener. Historical shared listeners
remain unresolved: background Alt+F4 on owned QA window3409182 returned Done
but window enumeration still showed it, so no closure was claimed. No commands
were issued to the live instance or ambiguous default endpoint.

CloseMainWindow on path-verified owned PID32336 returned True; subsequent
native enumeration confirmed window3409182 absent, while live PID14216 and
historical QA PID25428 remained. Unlike background Alt+F4, this supplies actual
closure evidence. A normal non-elevated CloseMainWindow request for historical
QA PID25428/window2949868 is pending; no forced termination or elevation used.

Historical elevated QA CloseMainWindow returned True but native enumeration
still showed window2949868. The default pipe then returned live status with5
bots and the following list_bots returned an empty list. Thus ambiguity persists
and successful close-request delivery is not closure. Stop default-pipe probes
for account targeting until this listener is retired or server identity is
bound per connection. No repeated-query selection of a desired reply is valid.
The explicit isolated QA endpoint remains usable for read-only/UI QA.

Later native enumeration still finds isolated PID26436/window5048408 and
elevated historical QA PID25428/window2949868. The former live window329234
is no longer enumerated; this is not proof that its process or pipe exited.
No shutdown or account command was sent. AX on the isolated window shows
the shell plus a protocol-download progress overlay; the map preparation
overlay is disabled. Its screenshot reports 1460x920 but the transport omits
the image ("model does not support images"), so compact-layout visual
acceptance is still unavailable. Do not infer a visual pass from AX or frame
dimensions, or target the ambiguous default endpoint based on window absence.

Correction to the overlay interpretation above: AX text alone did not prove
an active protocol wait. Source MainWindow.axaml binds that panel's
modalOpen class and hit testing to MappingLoadVisible, but leaves its static
text enabled in the accessibility tree when closed. On the isolated window,
pressing the quick-search header button opened the real search field;
setting its value to "comptes" and pressing Enter opened the Accounts page.
Native enumeration then found enabled Accounts actions, including the local
launcher import button (not invoked). Navigation is therefore responsive;
do not diagnose a stuck protocol download from those AX text nodes. This
proves search-to-Accounts navigation, not pixel layout or account execution.

## Automatic profile isolation correction

ControlPipeClient now resolves the endpoint once through the shared PipeName
property. Explicit GIVRE_CONTROL_PIPE retains precedence. Otherwise a
non-empty GIVRE_CLONE_RUN_NAME adds a bounded SHA-256-derived QA suffix to
the existing per-user pipe name. With no run name, the legacy endpoint is
unchanged. LocalControlBridge uses the same resolution as its client, so a
QA profile no longer joins the production/default endpoint merely because
its launcher omitted the explicit pipe variable. The observing client must
use the same run name or the explicit endpoint. Two processes deliberately
using the same profile are not isolated by this change.

LocalControlTests.Validation_profiles_route_to_separate_named_pipe_servers
starts two actual servers concurrently, sends framed status requests to each
profile endpoint and checks distinct owner responses. It also covers stable
naming, explicit override precedence and the unchanged null/empty-run default.
No process-global environment mutation or game command is required by the test.
Compilation and targeted LocalControlTests/McpControlSecurityTests are pending
under mon_5TYDX4TYYGG4TAEA, output root
`%USERPROFILE%/AppData/Local/Temp/givre-pipe-isolation-20261105`, logger
`pipe-isolation.trx`. Existing GUI processes still run their previous binaries;
the correction does not retroactively move their listeners.

First targeted run completed: 16 passed, zero failed/skipped, 24.8 seconds,
exit 0. The parent then deliberately replaced the run-name branch with the
legacy shared endpoint and inspected the mutated source. The focused
isolation regression failed as intended: Assert.NotEqual reported identical
pipe strings, one failed test, 16.7 seconds, exit 1, monitor
mon_7A4S1S4GCV1GC0SR / logger `pipe-sensitivity.trx` under
`%USERPROFILE%/AppData/Local/Temp/givre-pipe-sensitivity-20261105`.
The original run-name condition was restored exactly; no mutation is retained.
Restoration validation is running as mon_JKSXBXE98TQD8DPE with logger
`pipe-restored.trx` in the original isolation output root. A fresh application
launch with only the run-name variable remains necessary to verify bridge
wiring in the real GUI; the two-server test alone does not prove that launch.

Restored-source validation completed with exit 0: 16 passed, zero failures
or skips, 2.3 seconds. A fresh Release GUI build is registered as
mon_YVDRDS7X53Q9NKCX, output root
`%USERPROFILE%/AppData/Local/Temp/givre-pipe-gui-20261105`. No existing
process has been restarted or migrated, and the legacy endpoint remains
unsuitable for account commands while old listeners are ambiguous.

Real GUI verification completed: Release build exited 0 in 74.5 seconds
with 226 warnings, none suppressed. A fresh launch set only
GIVRE_CLONE_RUN_NAME=parent-auto-pipe-20261105 and explicitly removed
GIVRE_CONTROL_PIPE from its child environment. The first launcher command
failed PowerShell parsing before starting a process; the corrected encoded
command exited 0 and returned PID7800. Native enumeration independently
identified its non-elevated window6424172 beside the two older QA windows.

An independently calculated endpoint,
GivreWork-97A1F223CE35E55D-qa-FE88A3B17C2A6A54,
answered a framed get_app_status request: zero bots, zero supervisors,
transport log transport-2026-11-05_19-55-19.log, initialized/workerAlive true
and no last error. This proves the newly compiled GUI uses automatic profile
isolation without the explicit pipe override. No account operation was sent.
The old elevated/default listener remains untouched; its removal is not
needed to target this verified QA endpoint. It still prevents safely assuming
the legacy/default endpoint identifies a particular live instance.
