# Tactical current-source boundary

Parent reconciled the current native source with older build evidence on
2026-11-05. No native production edits or module promotion occurred.

`native/dofus3-native-host/mods/native-tactical/src/lib.rs:665` contains
`suppress_branch`: it enumerates renderers but only disables those present in
the supplied scenery association. Each disabled renderer records its previous
enabled state and a GC handle for restoration.

However, `suppress_map` at line 710 currently ignores its layers, logs
`construction association unavailable; original renderers preserved`, and
returns false. It is deliberately fail-closed; this is not a functional
tactical scenery implementation. Existing tests/builds and preservation of
original renderers cannot establish successful scenery suppression.

The adjacent TacticalAutostart README contains chronological candidate hashes
and prior tests. Those are not proof that the current association path works
or that the shipped DLL matches this source. NativeTurboRuntime still pins
the older shipped hash 9e9a7983360e51b1992e0971ef58a7ca442e5ed38a0a725302bc2a72ba618532.

Required next evidence is the per-assignment ownership association during map
construction, distinguishing static scenery from interactives, actors and UI,
then restoration and transition checks against the actual official client.
Do not replace the association with child indices or suppress all renderers:
the source notes that qii can remove pool entries before returning, so post-load
index pairing is not an ownership proof. The tracked preservation task stays
open, as do live visual acceptance and deployment.

The in-app IsoMapControl separately draws interactive markers at lines 484-505.
That mini-map rendering does not implement or validate native client scenery
suppression and should not be changed to address this native ownership gap.

## Follow-up lifecycle evidence

Read-only investigation `st_01a1add8` confirmed the hook APIs exist in
`native/dofus3-native-host/crates/mod-sdk/src/lib.rs:89-137`. The parent then
independently read `crates/mod-api/src/lib.rs:418-427` and
`crates/host/src/lifecycle.rs:37-73`: preparation explicitly delegates callback
quiescence and future-entry exclusion to the module, and sets `active=false`
before preparation. Returning busy retains the DLL but stops ordinary ticks.
Neither a module-local mutex nor a callback counter alone proves exclusion of
a callback paused before incrementing its counter. Hook removal must not be
treated as an independent drain of executing callbacks.

The investigation made no edits, installed no hook and ran no tests or game
operations. Its proposed evidence fixture would park callbacks with event
barriers before admission, in the body and before trampoline invocation, then
exercise teardown and explicit retry. That fixture is not implemented or run.
It would test a proposed entry barrier, not establish the separate positive
scenery classification or exclusive access to the game's construction lists.
Both contracts remain missing; current fail-closed suppression is unchanged.

## November 6 reconciliation

Read-only investigation `st_01a1ae4c` rechecked the current suppression,
provider exclusion and lifecycle boundaries. The parent independently read
`lifecycle.rs` and the API contract afterward. `Library::teardown` still marks
the module inactive before calling prepare, retains its DLL on a veto, and
removes hooks only after prepare succeeds. Its current tests cover veto/retry
and failed hook removal, not an entry barrier for a callback parked before
module admission. Therefore those passing historical tests do not close the
construction-hook lifetime gap.

A bounded host/module design investigation is now active as `st_01a1ae4e`.
It must choose an actual entry-exclusion mechanism and specify deterministic
barrier tests before implementation; it is not permission to add broader
scenery suppression. Complete positive renderer ownership, scene-access
exclusion, and live interactive preservation remain separate unmet contracts.

The design investigation completed without implementation. Its proposed
host-owned typed gateway must linearize callback admission and closure under
one synchronization boundary, holding a ticket until all module return paths
have returned to host code. A gateway paused before admission must never read
the closed module callback/context when resumed. This can protect module
callback state but cannot by itself reclaim the host gateway or trampoline.

Finite physical removal additionally requires a producer rendezvous: all
possible callers must acknowledge a safe point outside target, gateway and
trampoline, and remain excluded during removal. Neither the inspected host
nor the retained game-construction evidence establishes this contract. The
parent read `hook_create` and `hook_remove` directly: raw hooks target module
detours and removal disables then immediately frees the MinHook hook. Its
misleading comment equating disabled with quiescent was corrected; behavior
was not changed and no lifecycle safety improvement is claimed from prose.

Do not add an unused admission counter and claim this gap closed. The next
production prerequisite is evidence of a single non-reentrant construction
producer safe point, or cooperative park-and-acknowledge by every producer.
Only after that boundary is established should a versioned safe-hook capability
span host, mod-api and SDK; existing V1-V8 layouts must remain compatible.
Positive scenery ownership and exclusive scene access remain necessary even
after callback lifetime is solved. No native hook or renderer write was enabled.

## November 6 tool availability follow-up

Parent independently re-read both tactical boundary notes after read-only
investigation st_01a1aef3. It confirmed no new ownership capture implementation
was delivered. The retained official-client note still identifies per-assignment
ub.qil<T> as the candidate association boundary; qii-exit index pairing remains
invalid. No module was promoted and no hook was installed.

REA tools were absent from the current catalog search. Local diagnostic
bash_201 exited127 because `rea` was not found in the shell PATH. This is a
launcher availability result, not a provider failure or a permission refusal.
The documented version-pinned diagnostic `npx -y rea-agents@6.8.0 doctor --json`
is subscribed as bash_202. No registration or client configuration was changed.

The version-pinned diagnostic completed with exit1, but its provider inspection
explicitly reports Ghidra available:true at C:/Tools/ghidra_12.1.2_PUBLIC,
version12.1.2, working headless/decompiler and64-bit JDK21.0.12.1. Missing
Hopper, IDA, JADX and Kuna and stale unrelated editor registrations explain why
the aggregate doctor exit is not a Ghidra failure. No missing provider was
installed. CLI help is being inspected before selecting native analysis;
availability alone does not establish renderer ownership or producer affinity.

The first decompile invocation was rejected before analysis because JSON and
token-window output cannot be combined. Corrected invocation bash_206 reached
Ghidra but returned provider_timeout at its330000ms startup deadline. Target
SHA256 was ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591.
The result explicitly declares incomplete output and no confirmed out-of-memory
error. Therefore no new pseudocode, construction affinity or ownership proof
is claimed. Its remediation permits a larger REA_GHIDRA_STARTUP_TIMEOUT_MS on
a future attempt, after checking retained cleanup. No unchanged retry or
provider-setting modification was performed at this point.

Cleanup inspection bash_209 failed before execution because the outer shell
expanded PowerShell variables. Corrected read-only inspection bash_210 exited0
with no matching java.exe/decompile.exe processes for that attempt's exact
temporary identifier. No process was killed. A new attempt bash_211 uses
REA_GHIDRA_STARTUP_TIMEOUT_MS=1200000 in that command's environment only,
as permitted by the returned remediation, with a1500000ms monitor deadline.
This repeats import with a materially larger startup budget; it changes no
persistent provider or editor configuration. Native analysis remains pending.

The larger-budget attempt bash_211 also returned provider_timeout at1200000ms.
Retained output reaches `ANALYZING all address and memory` but contains no
completed function result. A direct read of its reported temporary ghidra.log
path returned ENOENT after exit; no retained project or usable pseudocode is
claimed. Repeating the same whole-image import with still more time is not the
next step: the analysis strategy must narrow expensive analyzers or reuse an
authenticated existing database. No third import was started. The serialized
full managed test subscription bash_215 remains responsible for validation
after the identified Java process exits.

## Reduced-analyzer attempt

mon_18DSGMHZW2DPHGSZ / bash_218 also exited1 with provider_timeout. This
attempt changed analysis strategy rather than only extending time: startup
budget600000ms, with Decompiler Parameter ID, Stack, Windows x86 PE RTTI
Analyzer, Scalar Operand References, ASCII Strings and Function ID disabled.
The returned REA pre-script receipt explicitly confirms status disabled for
all six analyzer names. Thus the override was accepted, but did not produce
the requested qii pseudocode within the budget. No function result or native
ownership/producer-affinity proof was obtained, and no hook was enabled.
Do not repeat this same reduced whole-image import unchanged. The remaining
approach needs a narrower function-level analysis or an existing authenticated
database, not a claim that accepted analyzer options solved tactical rendering.
