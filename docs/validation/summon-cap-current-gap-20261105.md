# Summon cap: current implementation and bounded remaining gaps

## Explicit living summon in a fighter appearance

Parent reproduction mon_P4CNJTKE28GG15W0 / bash_47 compiled and ran four
appearance cases: three passed, the self-owned living summon failed with
expected true / actual false, exit 1. The earlier attempt bash_46 was only a
test compilation error (nullable access on the ScriptFighter value type), not
a behavioral red.

FightStatsReader.ReadShownSummon now requires a positive summoned flag,
positive alive flag and a nonzero explicit summoner before reusing the actor
reader. LaunchService routes this observation through the existing OnSummoned
inside its in-fight gate, independently of numeric characteristic entries.
RequiredMapping includes the two additional literal dependencies. The test
table also rejects an actor whose summoned flag is false. Focused verification
mon_8R57KAJCW4Y3KXPE / bash_48 passed 57/57, no failures/skips, 9.6 seconds,
exit 0; report shown-summon-green.trx. This includes the five appearance cases,
existing summon dispatch/readers/capacity, visibility and mapping contracts.
Assertions were then strengthened to require roster presence for recognized
living summons and absence for the rejected inputs. Full current-source run
mon_YWSXP3DP2YTGQXF6 / bash_49 completed with exit 0: 4,759 total,
4,757 passed, zero failed and two existing skips, 179.0 seconds.
Report: GivreClone.Tests/TestResults/shown-summon-full.trx. The command used
Release, --no-restore and the existing isolated artifacts directory
%USERPROFILE%/AppData/Local/Temp/givre-mp-dispatch-20261105.

This is synthetic decoded-packet coverage, not an official reconnect capture.
Real server capacity acceptance remains unverified.

### Appearance before the first roster

Read-only investigation st_01a1ad35 identified that OnSummoned recorded owner
metadata but MutateFighters skipped positional insertion without an existing
roster. Parent independently read both handlers and extended the same theory
with an absent-roster case. mon_259Z8WA2VRJ2TPCY / bash_50 reproduced exactly
that case: six total, five passed, one failed at Assert.Contains, 5.0 seconds,
exit 1; report shown-no-roster-red.trx.

OnSummoned now uses the existing appearance-handler pattern: under
_fightersLock, start from an empty roster if absent and append only a missing
actor; publish FightersUpdated outside the lock. MutateFighters and authoritative
roster replacement semantics are unchanged. No position is invented when the
decoded actor has no disposition.

The first follow-up mon_SC91N8ZW87HYNF6C / bash_51 failed one of 54 cases:
the absent-roster actor was now present, but IsMySummon remained false before
the player itself was observed. Parent inspected the failure and SelfActorId:
that method deliberately requires the player to appear in the combat roster.
The test incorrectly expected ownership classification before that confirmation.
Production identity rules were left unchanged. The same case now first asserts
the summon is present, then dispatches a real EntitiesDispositionEvent containing
the player and summon at a new cell, and checks ownership/count and the new cell.
Validation mon_NDNQPZNQ7C6BXSEH / bash_52 passed 54/54, zero failed/skipped,
4.3 seconds, exit 0; report shown-roster-confirmed.trx. This covers all six
appearance rows plus existing summon reader/dispatch/capacity, controlled
combat and mark integration tests. The earlier full-suite result predates
this absent-roster insertion change and is not a full-suite claim for it.

Final combined run mon_C4SF73BY48CF6F1W / bash_53 completed with exit 0:
4,760 total, 4,758 passed, zero failed, two existing skips, 165.4 seconds.
Report: GivreClone.Tests/TestResults/shown-roster-final-full.trx. Command:
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-build
--no-restore --artifacts-path %USERPROFILE%/AppData/Local/Temp/givre-mp-dispatch-20261105
--logger "trx;LogFileName=shown-roster-final-full.trx".
This validates the current absent-roster insertion and confirmation regression
alongside the managed suite; it does not establish live reconnect or summon
acceptance by the official server.

## Implemented full-process cleanup follow-up

The parent reproduced the omission through public Forget and FightersDetail:
mon_R96AHYKTZKP6AXQ9 / bash_7 exited 1, one failed test, because the old
roster remained. The regression also covers fight ID, open turn, cancellation
of an automatic turn, another client's isolation and actor-ID reuse without
inheriting summon ownership.

LaunchService.Forget now removes fight ID/open turn and calls the existing
PurgeFightState after removing the in-fight flag. No new reset mechanism,
events, ownership counter or timing fallback was introduced. Existing
idempotent cleanup remains in place.

Fresh isolated Release related verification mon_J5TWQP6Z13SA1MPN / bash_8:
exit 0, 39 passed, zero failed/skipped, 897 ms. Filter covers
SummonOwnershipIntegrationTests, SummonDispatchIntegrationTests,
CombatSummonCapacityTests, FightMarksIntegrationTests and ControlledCombat.
Artifacts: %USERPROFILE%/AppData/Local/Temp/givre-forget-combat-green-20261105;
TRX filename forget-green.trx. LSP daemon was unreachable after reload;
compilation/tests succeeded, existing unrelated warnings were not suppressed.

Gemini 3.8 Flash High independently reviewed the omission read-only before
the production fix. Parent verified the relevant source and actual red/green.
This resolves the full-Forget cleanup omission below, not the remaining
decoded-appearance, reconnect ownership or real-server summon-cap proof gaps.

2026-11-05. Proposal only, based on current source and focused tests; no tests,
builds, native analysis, game actions or git commands run for this review.

**Decision:** reuse the existing owner/roster count and cap gates; no expiry
counter or state-effect ledger is needed. The normal confirmed-death path is
implemented and has self/companion dispatch coverage. There is a concrete
full-PID cleanup omission to address, plus narrower integration/live-proof gaps.

## Existing contract and evidence

Full-suite restoration verified: mon_XG2QFJST17ZBA22R/bash_23 exited0,
4742 total,4740 passed,zero failed,2 existing skipped,183.5s.
Report: GivreClone.Tests/TestResults/summon-final-suite-r2.trx.
This replaces the earlier red global run after correcting its two delivery
contracts. It certifies managed regression only, not live combat or visual QA.

Full managed regression mon_M2AVHQA4JXNAGQQW/bash_18 exited1:
4742 total,4738 passed,2 failed,2 skipped,228.2s. The actual TRX
GivreClone.Tests/TestResults/summon-final-suite.trx identifies precisely
MappingContractTests.RequiredMapping_manifest_matches_the_code and
LuaApiInventoryTests.Inventory_covers_the_current_binding_sources.
No failing combat assertion was reported by this run.

The raw reader added four literal catalog lookups missing from RequiredMapping:
jrl.fwot/fwou/fwov and jsq.fwvz. Added these sorted entries without changing
the contract test. mon_7M804GBG0DPA5ZKS/bash_21 then passed93/93,exit0,
8.5s (MappingContractTests and RawStateMessageReaderTests), raw-mapping-final.trx.

For the Lua failure, executing the existing inventory generator in memory with
its output write removed produced exactly the existing JSON except three
sourceHashes. All988 API entries, zero documentedButAbsent and203 undocumented
entries stayed identical. The three changed files Bindings/Combat/Travel are
listed in the accepted expedition-failure-policy evidence. Updated only their
three hashes using apply_patch; no binding code or test weakened. Related Lua
inventory/expedition verification mon_TVY55XFSTBBVQY0B/bash_22 passed124/124,
zero failed/skipped,25.7s,exit0 (lua-inventory-final.trx). This is not yet a green full-suite
claim. C# diagnostics remain unavailable; JSON LSP biome is not installed.

Final bounded acceptance: extended the same dispatch theory with configured
classic player rotation (AutoSpells=false, IsSummon=true, MaxSummons=2).
mon_XW0ZQSY91807FNZ6/bash_15 passed25/25. Controlled companions explicitly force
automatic rotation in production, so no fictitious classic-companion case exists.

Sensitivity proof: changed only the summons-ingress name condition to a
nonmatching sentinel, confirmed the mutated source, then ran all three dispatch
rows. mon_80P3096J37DWVGBX/bash_16 exited1 with3/3 failures at the first capped
turn: Assert.Single saw two emitted frames instead of only turn finish. This
proves the keeper detects loss of decoded appearance registration. Restored the
exact condition to GameActionFightEvent; no mutation retained. Fresh compiled
restoration mon_QSXD9KTS3HJTG72Y/bash_17 passed25/25, zero skipped,6.6s,exit0;
TRX summon-wire-restored.trx in the same isolated artifacts directory below.
The classic configured-branch proof gap is now closed offline. Pure-summon
utility, actual server acceptance and reconnect stream remain unverified.

Parent encoded-appearance integration accepted: SummonDispatchIntegrationTests
now creates catalog-numbered synthetic wire messages, parses them with
RawProtobuf.Parse and delivers through actual OnDecoded. Direct OnSummoned
fixture calls were removed. Player and controlled-companion cases retain the
real dispatch observations: blocked at cap, other-owner death stays blocked,
own decoded death admits a replacement cast attempt. Message source deliberately
differs from the explicit summoner, so source fallback cannot mask ownership loss.

Independent Release runs: mon_R829FKGWHK2HNZ83/bash_13, 24 passed, exit0;
strengthened source/owner variant mon_CKR3G5R0J2PX928N/bash_14, 24 passed,
zero failed/skipped, 6.5s, exit0. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-summon-wire-20261105;
TRX summon-wire-owner.trx. Filter: SummonDispatchIntegrationTests,
SummonReaderTests, SummonOwnershipIntegrationTests, CombatSummonCapacityTests.
Compilation is verified; C# LSP failed scanning an inaccessible pre-existing
artifacts/kourial-tools/pypdf directory. Permissions were not changed.

This supersedes the direct-OnSummoned description in the historical table.
The fixture still uses a synthetic summon-plus-damage descriptor and rejects
socket sends. It does not prove a real spell's server acceptance, the classic
configured branch or reconnect reconstruction. Gemini's read-only return did
not change files or execute tests; all edits and run evidence above are parent-owned.

Parent reconnect follow-up: the inspected FightFighterShowEvent handler upserts
stats, team and position but never maps FightCharacteristics.summoner into
_summonOwners. ReadShowFighter returns null when parsed characteristic entries
are empty, despite an identity/ownership envelope potentially being present.
This is a concrete missing show-only ownership path, not evidence that the
official reconnect stream omits GameActionFightEvent summons. Obtain an actual
reconnect stream or a bounded show-only reproduction before claiming the
normal reconnect count correct or changing cap semantics. Do not erase unknown
ownership by interpreting the absence of an owner entry as confirmed zero.

| Behavior | Current source | Existing test evidence (read, not rerun) |
|---|---|---|
| Effective cap | `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:81`: minimum of observed MaxSummonedCreatures (zero allowed) and configured MaxSummons (configured0 means1); missing characteristic retains configured cap. | `CombatSummonCapacityTests.Planning_respects_caster_and_profile_capacity_in_every_branch`, `Missing_caster_characteristic_keeps_configured_summons_available`, `Different_summon_spells_share_the_same_living_capacity` |
| Current living owner's count | `LaunchService.Combat.cs:736-765`: OnSummoned records explicit summoner, else nonzero server action source; CountMySummons intersects ownership with current `_fighters`, comparing to SelfActorId. `:941` selects controlled companion first; `LaunchService.Look.cs:22` and Combat.cs:785 use that actor's characteristics/stats. | `SummonOwnershipIntegrationTests.Summon_capacity_and_script_ownership_follow_the_actor_not_the_observation_turn` |
| Planner and final execution gates | `LaunchService.Combat.Turn.cs:1003,1058` supplies ownership/profile cap; `:1170-1174` rechecks observed count before send. Classic summon branch also gates at `:1412-1418`. Planner counts living owned simulated fighters before/after resolution (`CombatTurnPlanner.cs:443,491`). | `CombatSummonCapacityTests` covers cap branches, other owners, death, and regular damage at cap. |
| Own death frees slot; other owner's death does not | Server GameActionFightEvent summon ingress at `LaunchService.Packets.cs:1017-1019`; confirmed Death at `:985-995` removes target from roster. Ownership latch may remain, but intersection stops counting it. HP drift alone does not remove a slot (`LaunchService.Combat.Turn.cs:982-987`). | `SummonDispatchIntegrationTests.Automatic_dispatch_reopens_only_for_the_active_actors_decoded_summon_death(false/true)` runs real RunFightTurn for player/companion: blocked at2, other-owner death still blocked, own decoded death admits replacement. |
| End/disconnect | EndFight calls PurgeFightState (`LaunchService.Combat.cs:230-231`); purge clears roster, owners and controlled context (`:245-274`). ForgetSession calls purge (`LaunchService.GameLink.cs:440-447`). | No summon-specific end/disconnect reset assertion found in the inspected focused tests. |

Tests above are in `GivreClone.Tests/`. `SummonReaderTests` separately checks
encoded protobuf summons, grouped ownership, multiple summons, omitted cells
and unknown ownership. The dispatch test initializes summons by calling
OnSummoned directly, sends synthetic decoded Death, and observes a rejected
socket-send attempt using a synthetic summon-plus-damage spell. It does not
prove decoded summon-appearance wiring or server acceptance of a real summon.

## Actual gap versus proof gap; minimum proposed next change

1. **Concrete cleanup omission:** `LaunchService.cs:469-534` Forget removes
   in-fight/identity/script state but neither calls PurgeFightState nor clears
   `_fighters`/`_summonOwners`/controlled combat. ResetScriptFightState
   (`LaunchService.Combat.Script.cs:77`) clears summon-ID tags, not these stores;
   ForgetGameLink (`LaunchService.GameLink.cs:465`) does not purge combat.
   Thus Forget alone leaves stale ownership/roster state. Propose reusing the
   existing combat purge at the full-forget boundary, preserving callback
   ordering, with a focused test seeding both owners, Forget, and same-PID reuse.
   This is a source cleanup defect; an observed live over/undercount from it is
   not claimed. FightStarting (`LaunchService.Packets.cs:840-871`) also does
   not itself clear owners/roster; verify fresh-fight behavior when prior end
   was missed rather than assuming fight start repairs stale state.
2. **Minimum missing deterministic integration:** extend the existing dispatch
   owner test through actual encoded summon payload -> ReadSummons/OnDecoded,
   instead of direct OnSummoned; assert cap blocking and decoded own/other
   Death behavior for self/companion. Add explicit EndFight/ForgetSession/full
   Forget reset and fresh-fight/reused-ID cases. Test the classic IsSummon
   dispatch branch separately; current real-dispatch test selects the planner.
   These gaps warrant focused tests, not replacing the existing count mechanism.
3. **Live proof only, parent-owned:** real summon-only spell, known profile and
   observed capacity2; obtain two server-confirmed owned living summons, show
   a third is not sent; another owner's death must not reopen the slot; own
   server-confirmed death/expiry must admit exactly one replacement. Repeat
   controlled companion with different player capacity, then end/disconnect/
   reconnect and confirm no stale count. Record decoded appearance/owner/death,
   active actor/cap, emitted cast and server result. Pure-summon usefulness and
   confirmation-before-next-cast timing are not proved by the synthetic damage
   fixture; no outstanding timing bug is established by this bounded review.

Reconnect/show-only ownership reconstruction is also unproved: the inspected
FightFighterShow handler (`LaunchService.Packets.cs:1305-1335`) adds stats/team/
roster but does not populate `_summonOwners`. Confirm whether the real reconnect
stream replays summons before admitting counts; missing ownership must not be
presented as complete zero. No duration arithmetic or per-turn expiry counter
should replace confirmed roster removal. Beyond the cleanup omission, current
evidence does not establish another normal-path cap bug; do not broaden fixes
until the bounded integration test or parent's live scenario demonstrates one.
