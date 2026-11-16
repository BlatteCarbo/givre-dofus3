# Utility beneficiary contract audit - 2026-11-05

## Immediate cancellation follow-up, 2026-11-06

Source investigation st_01a1b178 identified a narrower accounting discrepancy
than future ally usability: the resolver applies signed point rows in sequence,
but the planner rewards positive allied events without subtracting same-cast
removals. A +2 then -2 immediate cast leaves no point gain yet earns control.
Four real-planner regression cases cover AP/MP with explicit one/three-cast
budgets, requiring zero control and no action for a resource-only canceled gain.
Baseline is subscribed as bash_400, `ally-point-cancel-red.trx`; no executed
failure is claimed until its receipt arrives. Production scoring is unchanged
at this checkpoint. This does not infer universal stacking caps or an ally's
future inability to spend resources, nor establish an installed spell occurrence.

Baseline bash_400 completed with exit1:all four cases fail at the intended
control assertion, reporting AP12/AP36 and MP6/MP18 despite zero net gain.
The planner now groups signed point deltas by recipient and resource within
the cast, rewarding only a positive net result. Recipient filtering, resource
weights, native caps and resolver application order remain unchanged.
Related validation is subscribed as bash_401, `ally-point-cancel-green.trx`.
No green result or live acceptance is claimed before that receipt.

Targeted bash_401 completed with exit0:322 passed,0 failed,0 skipped in
`ally-point-cancel-green.trx`, including the four original canceled-gain cases.
The same theory now additionally covers partial same-resource cancellation and
cross-resource removal: losing MP must not cancel a real AP gain, and vice
versa. Full managed validation with those additional cases is subscribed as
bash_402, `ally-point-net-full.trx`; that receipt is pending. This is still not
live evidence or a model of later ally resource consumption.

The cross-resource fixture was tightened to start its ally with2AP/2MP instead
of zero. This ensures the opposite-resource removal actually executes rather
than clamping to zero, so an incorrect aggregate across PA and PM would fail.
That test-only edit postdates compilation of bash_402; the full run cannot be
claimed as validating those newest fixture bytes. A focused follow-up must
compile and execute the updated theory after bash_402 releases shared outputs.

Full bash_402 exited1:4858 passed,1 failed,2 existing nonexecuted cases,
4861 total. The only failure is
SmithObjectiveStoreTests.Restart_and_income_do_not_replenish_settled_allowance:
UnauthorizedAccessException in AtomicJson.Write at File.Move overwrite, reached
from Reserve. Its fixture uses a fresh GUID-named temp directory; no shared-path
collision or permission root cause is established. This path is unrelated to
the planner edit. No storage behavior, permission or failing assertion was
changed. A diagnostic including all SmithObjectiveStoreTests and the rebuilt
eight-case net-resource theory is subscribed as bash_403,
`ally-net-stock-diagnostic.trx`. An isolated pass would not erase the full failure.

Diagnostic bash_403 completed with exit0:16 passed,0 failed,0 skipped. This
includes all eight current net-resource cases with real opposite-resource loss
and all eight SmithObjectiveStoreTests. It confirms the storage invocation can
pass alone, not the cause of its earlier access denial or a green full suite.
No AtomicJson or storage-test source changed.

The symmetric enemy cases in the same theory reproduced the analogous gross
removal reward: bash_404 exited1,8 passed and4 failed. Canceled/partially
offset removals scored16,30,24,15 instead of net0,0,8,5. The planner now uses
one signed event aggregation grouped by recipient, resource and enemy flag:
positive net gains for nonself allies and negative net changes for enemies,
with the existing respective weights. Public resolver gross-event totals are
unchanged. Validation is subscribed as bash_405, `point-net-both-green.trx`.
This is same-cast net accounting only; cross-cast duration/expiry and future
ally spending remain unsupported, and no live spell occurrence is established.

Targeted bash_405 completed with exit0:330 passed,0 failed,0 skipped in
`point-net-both-green.trx`, including all12 net-resource cases. Full managed
validation of this new production change is subscribed as bash_406,
`point-net-both-full.trx`. Its result is pending. The earlier storage access
failure is retained separately rather than hidden by the isolated diagnostic.

Full bash_406 completed with exit 0. Full XML readback of
`point-net-both-full.trx` reports 4865 total,4863 executed and passed,0 failed
and2 nonexecuted cases. This validates the current net-resource implementation
against the full managed suite. It does not establish the cause of the earlier
intermittent storage access denial, nor live-game behavior.

## State-filtered learned grants

Parent verified the reported utility-routing gap in AutoSpellRotation:
ShouldUsePlanner relied on the narrow Targets projection for allied boosts,
which returns None for a supported extra state token such as a,E97. The
existing point-change theory now includes AP and MP grants to an observed ally
with state97. bash_407 exited1:13 existing removal cases passed and2 grant
cases failed at ShouldUsePlanner (false). These are synthetic supported masks,
not evidence that an installed learned spell uses this exact row.

The routing check now recognizes recipient category tokens directly, like the
existing enemy-removal branch. Admission, immediate-modeled-row checks and
unknown-damage fallback remain unchanged; SpellResolver still enforces the
observed state conditions. Live planner allowed-target filtering applies user
recipient configuration only to configured spells, not these auto additions.
bash_408 is subscribed for rotation/planner/resolver validation with receipt
state-grant-routing-green.trx; result pending. No deployment or live validation
is claimed, and the broad utility task stays open.

The targeted bash_408 completed exit0 with332 passed,0 failed,0 skipped.
Both state-filtered grant cases now route to the planner and produce the
expected allied net resource reward (12 for AP,6 for MP); the13 removal
controls remain green. The existing unknown-attack/null fallback theory also
passes. Full validation of the same compiled payload is subscribed as bash_409,
state-grant-routing-full.trx; pending. Application compilation retains225
warnings and test compilation30 warnings, not suppressed.

Full bash_409 completed with exit0. Complete XML counter readback from
state-grant-routing-full.trx reports4867 total,4865 executed and passed,
0 failed and2 nonexecuted. This includes the new conditional grant routing
and existing managed contracts. No installed-spell occurrence, deployment or
live use is implied by this full offline result.

## Finding

No bounded production correction is established for the remaining goal
`Corriger les sorts utilitaires sans beneficiaire`. No production or test
source was changed by this audit. The existing fixed allied AP/MP reward
is a resource heuristic, not proof that an ally can spend those points.
The current snapshot cannot establish that stronger claim.

Read the existing CombatTurnPlannerTests first, including
`Resource_only_rotation_boosts_an_ally_but_not_an_idle_caster`,
`Unspent_self_resource_gain_does_not_replace_an_available_attack`, and
`Placement_reserves_end_cells_only_for_allies_still_alive_on_the_board`.
Programming/testing references read locally: `.agents/skills/implement/SKILL.md`
and `.agents/skills/tdd/{SKILL.md,tests.md,mocking.md}` under `C:/Users/A`.
Also read the installed
`%USERPROFILE%/.omo/binary-runtime/5.1.57/plugin/skills/programming/SKILL.md`
and its `references/testing/{README.md,junk-classes.md}`. Their authoring
gate requires an independently established behavioral contract and a
credible regression; neither permits asserting invented ally incapacity.
The requested existing real `CombatTurnPlanner.Choose` boundary is the test
seam; no new production seam, mock planner, or speculative ally capability
was introduced. No failing regression is claimed.

## Existing bounds

- `CombatTurnPlanner.cs:11-26`: PlannerFighter includes observed life,
  visibility, position certainty, states and optional TurnAp. CanTarget
  requires visible, confirmed position; it does not describe turn utility.
- `CombatTurnPlanner.cs:470-476`: Eligible requires positive simulated life,
  targetable position (except self), currently visible resolver fighter,
  AllowedTargets membership, configured target HP bounds and remaining
  per-target casts.
- `CombatTurnPlanner.cs:514-524`: CastOn maps resolved event recipients back
  to the snapshot and rejects ineligible non-self recipients. Its exception
  for modeled friendly damage does not allow a resource-only gain through.
- `CombatTurnPlanner.cs:549-552`: allied point reward requires an actual
  positive PointsResolved event on an actor other than the caster.
- `CombatTurnPlanner.cs:598-606`: single-target centers and target checks use
  Eligible. Area casts still run the affected-recipient checks above.
- `SpellResolver.cs:305-309`: every effect's recipients are selected from
  fighters that are Alive with positive current Life, applying the real mask
  and zone. A dead actor cannot receive a points event. An absent snapshot
  actor is not in that list, even if a stale TeamIntent references it.
- `SpellResolver.cs:408-414`: AP/MP events follow the resolved recipient and
  actual resource delta. Subsequent effect rows re-evaluate living targets.
- `LaunchService.Combat.Turn.cs:989-1087`: the runtime adapter copies observed
  fighter facts, profile target gates and resource limits into the snapshot;
  it supplies no allied spell rotation or future resource-spending plan.

Dead, absent, hidden/unconfirmed and configured-ineligible sole recipients
are therefore bounded by existing source contracts. This is source evidence,
not a new executable negative-recipient matrix or live-server observation.
Mixed utility/damage casts retain their existing friendly-fire semantics.

## Unsupported usability claims

`TeamIntentBoard.cs:7-10` exposes damage/kill targets, planned healing and
shielding, an end cell, round and timestamp. It has no AP/MP reservation,
ally learned spells, spell costs/cooldowns, action capacity, remaining turn
order, resource expiry, or confirmed later execution. An empty target list
or missing intent is not proof that the ally has no useful action. Previous
round positional hints are explicitly admitted by TeamIntentView.

Null Characteristics or TurnAp, zero ExpectedDamage, zero current AP/MP,
and lack of a modeled attack are not evidence that a living ally cannot use
a gain. Damage prevention alone does not rule out healing, shielding,
summoning or other utility. A geometrically isolated ally likewise has no
known spell repertoire or future movement/target state in this contract.
No such condition can safely replace the existing allied resource reward
without a new capability contract or a scoring policy decision, both outside
this task. Duplicate allied AP/MP grants and resource use after the caster's
turn remain unsupported by this audit.

## Validation

The existing related suite was started through an asynchronous monitor,
without restore or application/network actions:

```text
cd %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~AutoSpellRotationTests|FullyQualifiedName~CombatPlannerResolverTests|FullyQualifiedName~SpellResolverTests" --nologo -v minimal -p:BuildInParallel=false -m:1 --logger "trx;LogFileName=utility-beneficiary-existing.trx" --results-directory "%USERPROFILE%/AppData/Local/Temp/givre-utility-beneficiary-20261105"
```

Completed monitor `mon_Q0N8ZKSWME00F4DX` / process `bash_8`: exit 0.
304 executed, 304 passed, zero failed, skipped, aborted or pending; console
test duration 30.4 seconds. The TRX at
`%USERPROFILE%/AppData/Local/Temp/givre-utility-beneficiary-20261105/utility-beneficiary-existing.trx`
records start `2026-11-05T12:15:46.1937425+01:00` and finish
`2026-11-05T12:16:15.7214861+01:00`. Compilation emitted existing C#,
Avalonia and xUnit warnings; none were suppressed or corrected in this
documentation-only task. No baseline-red/green fix is claimed because there
is no proven bounded bug to correct. No live ally action or server resource
use was tested. Parent verification is independent.

Parent verification completed: mon_CH8VKMJEF7YGR8D4 / bash_69 exited 0,
304 passed, zero failed or skipped, reported test duration 10 s. The parent
read this report and the actual planner recipient gates, resource scoring,
runtime AllowedTargets adapter and neighboring support tests. Its command
used the independently compiled Debug artifacts, not the child's Release
binary:

```text
dotnet test "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/GivreClone.Tests.csproj" --no-build --artifacts-path "%USERPROFILE%/AppData/Local/Temp/givre-parent-self-visibility-20261105" --filter "FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~AutoSpellRotationTests|FullyQualifiedName~CombatPlannerResolverTests|FullyQualifiedName~SpellResolverTests" --verbosity minimal --tl:off
```

These existing tests and source contracts do not close the broader utility
beneficiary goal or prove allied future resource consumption.
