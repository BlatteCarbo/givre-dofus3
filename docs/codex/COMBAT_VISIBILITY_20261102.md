# Combat visibility observation, 2026-11-02

## Contract and supported observations

The catalog-driven `FightVisibilityReader` reads the initial state through
`FightFighterShowEvent.information -> ActorPositionInformation.actor_information -> ActorInformation.fighter -> FightFighterInformation.stats -> FightCharacteristics.invisibility_state`.
It reads changes through `GameActionFightEvent.invisibility -> Invisibility.target_id/invisibility_state`.
Actor IDs retain signed 64-bit protobuf semantics. A present state message with an omitted scalar records protobuf default zero. Missing actor/message or a wrong state wire type does not create an observation. Initial state reading does not depend on the characteristics list containing entries.

`FightVisibilityReducer` keeps the raw state, semantic visibility, and position certainty separately. Missing actors remain Unknown. `InvisibleDetected` updates position certainty to LastKnown, never Visible or Confirmed. Packet ingress uses the relocation reader's returned kind `InvisibleDetected`, not its snake-case catalog field name. Death removes the actor; fight start creates a new reducer; existing fight purge removes the reducer.

The immutable planner snapshot carries visibility and position certainty. Actor-targeted expansion and displacement follow-up ranking require Visible plus Confirmed. Uncertain enemies retain conservative threat exposure rather than making their last-known location a safe assumption. Live snapshots explicitly use Unknown for missing non-self observations; the self actor uses its known position. Synthetic planner callers retain the existing visible/confirmed constructor defaults.

Managed combat target selection filters uncertain enemies even without challenge restrictions. Area hit-count checks do not count uncertain enemy cells as confirmed hits. Cast dispatch rechecks occupants against the live reducer, including legacy choices made before a visibility event. Last-known positions remain usable as conservative movement obstacles; this change does not claim a complete hidden-actor spatial model.

## Parent follow-up: reference mapping and position confirmation

The initial child unit was not deployable: every value remained Unknown,
disabling all non-self attacks. Parent found an explicit reference schema in
BillBot's `DBDofusUnity/datas/protos/non_obf/game/common.proto:183-187`:
FightInvisibilityState defines INVISIBLE=0, DETECTED=1, VISIBLE=2.
The reader still resolves field numbers from the current catalog.
This is reference-schema evidence, not an independently decoded current
client enum. Live validation against observed fighters remains required.

Parent maps these values and retains Unknown for other integers.
A visibility transition alone leaves position LastKnown. Initial fighter
data with position, server dispositions, accepted movement and forced
relocation can confirm a visible actor's cell. InvisibleDetected cannot.
Revelation after hiding requires a fresh position observation.
Added reducer transition coverage and required mapping entries.
Post-change full validation and deployment are pending.

## Initial child investigation: numeric semantics unresolved

Installed client metadata version 39 was checked against SHA-256
`ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7c67db6`.
The parent supplied GameAssembly SHA-256
`70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663`.
Ordinary bounded static inspection used the table layout in
`native/GivreTurboRuntime/TacticalAutostart/verify-map-root.mjs` and
`inspect-map-services.mjs`. The 44,466 type names and field names did not identify a named combat visibility enum/value table. Unrelated UI visibility types, StatId.Invisibility and ActionId.CharacterMakeInvisible do not establish this field's integer meanings.

The shipped field catalog establishes field numbers, not integer value meanings:
FightCharacteristics.invisibility_state #1; Invisibility.invisibility_state #1 and target_id #2; InvisibleDetected.cell #1 and target_id #3.
No verified link from these state fields to enum literals or client state-switch semantics was recovered. Obfuscated names may still contain that information; its absence is not asserted. The REA resource constraint was retained inventory budget, not missing metadata, and this ordinary inspection is not labeled REA Evidence.

The initial child version left every raw state Unknown. The parent follow-up
above supersedes that behavior using the explicit reference schema; it does
not claim Sram completion. ForcedMovementSafe remains false.

## Verification

Deterministic tests cover catalog initial/change paths, empty characteristic entries, signed actor IDs, omitted scalar zero, raw unknown values, detection without revelation, death/reset reducer behavior, and planner rejection of uncertain attack cells. Existing planner and placement-team tests are included as neighbors.

Real application build: `dotnet build GivreClone.App/GivreClone.App.csproj --artifacts-path givre-visibility-app --verbosity minimal`, exit 0, 224 existing project warnings, no errors. Warnings include partial-method parameter names, nullability, Windows-only APIs and obsolete Avalonia Watermark properties. No warnings were suppressed.

Final targeted command: `dotnet test GivreClone.Tests/GivreClone.Tests.csproj --artifacts-path givre-visibility-artifacts --filter "FullyQualifiedName~FightVisibilityTests|FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~FightPlacementTeamsTests" --verbosity minimal --logger "trx;LogFileName=visibility.trx" --results-directory givre-visibility-artifacts/results`.

Final targeted result: exit 0, 47 executed, 47 passed, 0 failed, 0 skipped. Durable counters are in `givre-visibility-artifacts/results/visibility.trx`. The final test command rebuilt the real application reference after the detection correction. The separate application build also exited 0. `git diff --check` for the tracked ingress/combat changes exited 0. The parent's prior full-suite baseline was 3966 passed plus 2 skipped; it is not claimed as a post-change run. No shared application restart, live game action, new dependency or commit was performed.

## Exact edited paths for this unit

- `GivreClone.Protocol/FightVisibility.cs` (new)
- `GivreClone.App/Services/LaunchService.Packets.cs`
- `GivreClone.App/Services/LaunchService.Combat.cs`
- `GivreClone.App/Services/LaunchService.Combat.Turn.cs`
- `GivreClone.App/Services/Combat/CombatTurnPlanner.cs` (existing parent work preserved)
- `GivreClone.Tests/FightVisibilityTests.cs` (new)
- `GivreClone.Tests/CombatTurnPlannerTests.cs` (existing parent work preserved)
- `docs/codex/COMBAT_VISIBILITY_20261102.md` (new)

No recovery document, class ledger, native source, project link, or other parent's edits were changed by this unit.
