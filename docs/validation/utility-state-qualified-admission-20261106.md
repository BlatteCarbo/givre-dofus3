# State-qualified resource-removal admission

## Enemy-category admission follow-up

Independent investigation st_01a1b142 identified another admission omission:
the resolver supports unstarred enemy categories I/J/H/L/M, but the rotation
gate recognized only A. The existing learned-removal theory now includes four
H/L cases. `enemy-category-removal-red.trx` reports 9 passed and 4 failed at
ShouldUsePlanner, exit1 (bash_373). The gate now recognizes those six exact
enemy category tokens, retaining immediate-modeled and planner admission checks.
Starred categories are not included. Two fallback cases also retain classic
rotation when an attack is unmodeled or a descriptor is missing.

Validation is subscribed as bash_375: the full UI suite runs first, followed by
AutoSpellRotationTests, SpellResolverTests and CombatTurnPlannerTests. Receipt
`enemy-category-removal-green.trx` is pending; no green result, installed spell
occurrence or live-server acceptance is claimed here.

The targeted follow-up bash_375 completed with exit0:315 passed,0 failed,
0 skipped in `enemy-category-removal-green.trx`. This includes the real planner
H/L cases and fallback regressions. It is targeted validation, not a new full
managed-suite result or evidence of an installed/live spell occurrence.

Full managed validation bash_386 completed with exit1:4847 passed,1 failed,
2 existing nonexecuted cases,4850 total. The sole failure was the Lua inventory
source-hash freshness check for GivreLuaHost.Combat.cs, modified earlier in
this thread. The existing generator was evaluated with its output intercepted
in memory; normalized comparison showed exactly one changed hash and no API
inventory changes (988 bindings,0 documented-but-absent). The JSON manifest was
synchronized with that generated hash via apply_patch. No test was weakened.
Focused LuaApiInventoryTests bash_388 then completed with exit0:4 passed,
0 failed,0 skipped (`lua-inventory-current-green.trx`). The prior full run
remains a failed receipt; the focused repair does not relabel it as green.

The new full run bash_389 completed with exit0:4848 passed,0 failed,
2 existing nonexecuted cases,4850 total. Receipt:
`enemy-category-removal-inventory-full.trx`. This establishes managed-suite
validation of the admission change and synchronized inventory, not live combat.

Parent independently read AutoSpellRotation, SpellEffectDescriptor.Targets,
SpellResolver.RowProblem and CombatTurnPlanner.IsAdmitted after investigation
st_01a1b0d8. The discovered rotation accepts immediate supported resource
removal with mask A,E97, but ShouldUsePlanner relied on the descriptor's
conservative Targets projection. That projection returns None for state
qualifiers even though the resolver supports and checks them.

The existing Learned_point_removal_reaches_the_planner_as_enemy_control
theory now covers actions1079,1080,168,169 with both A and A,E97. The enemy
has explicitly observed state97. Pre-fix bash_329 exited1: four original A
cases passed, all four qualified cases failed the planner-entry assertion.
Receipt: GivreClone.Tests/TestResults/state-removal-red.trx.

The narrow fix checks for an exact A category token at this admission gate,
still requiring CombatTurnPlanner.IsAdmitted and IsImmediateModeled. Unknown
tokens, deferred/unmodeled rows and actual state checks remain owned by their
existing admission/resolution contracts. The conservative Targets property
was not broadened, and no scoring or damage behavior was changed.

Focused planner/resolver/automatic-rotation validation bash_330 completed with
361passed and4failed. The admission assertion now passes; the four qualified
cases instead fail because the plan is empty. The fixture left caster state
knowledge unspecified. The resolver evaluates the condition for each living
candidate before the final category/zone decision, records unobserved state
on the caster, and the planner rejects utility-only casts with unresolved rows.
The fixture now explicitly observes the caster's empty state set as well as
enemy state97; no production uncertainty guard was weakened. Follow-up is
subscribed as bash_331, completed with exit0:365passed,0failed,0skipped
in51.7seconds. Receipt:
`GivreClone.Tests/TestResults/state-removal-observed-green.trx`.
This includes the four qualified cases, with an actual planned cast and the
expected AP/MP control score, alongside the original simple-mask cases.
The full managed suite is now subscribed as bash_332 using the same Release
artifacts with --no-build --no-restore. That run exited1 before executing tests:
testhost could not find AssetRipper.TextureDecoder1.3.0 from the dependencies
manifest. This is an aborted run, not a failing combat assertion or a passing
full suite. A fresh restore/build and full test run uses the separate
`%USERPROFILE%/AppData/Local/Temp/givre-state-removal-full-20261106` artifact
directory, subscribed as bash_333. It completed with exit1:4839passed,
1failed,2existing skips,4842total in6minutes13seconds. The sole failure is
`MZP0PacketBusTests.Publish_commits_waiter_before_slow_subscriber_even_if_cancel_runs`,
whose unlabelled Assert.True fails (the subscriber-entry wait); it is outside
the modified automatic-rotation path. This is not a green full-suite receipt.
Source uses Task.Run for publication and blocks on a3second entry event before
cancelling. A focused diagnostic is subscribed as bash_334; no PacketBus
production or test behavior was changed to turn this run green. Scheduling
contention is a hypothesis, not a proven cause of this occurrence.
The isolated diagnostic bash_334 completed with exit0:1passed,0failed in14ms,
receipt `state-removal-packetbus-isolated.trx`. That confirms this invocation
can pass alone but does not erase the failed full-suite result or prove a
scheduling root cause. No unchanged full-suite retry was started.
No deployment, installed spell occurrence or live
server acceptance is claimed. This does not close learned utility completeness
or allied future resource-consumption tasks. The language server still crashes
on the unrelated inaccessible artifacts/kourial-tools/pypdf directory;
compiler/test results must establish validation instead.

Installed-data follow-up: the existing SpellCatalogInspect tool accepts explicit
spell/grade pairs, not a predicate query across the catalog. No arbitrary spell
ID was substituted to claim an installed occurrence. The installed Sram12913
utility receipt covers MP gain/invisibility, a different action and mask; it
does not validate this resource-removal admission change. Installed occurrence
and live acceptance remain open rather than inferred from synthetic fixtures.

## Out-of-zone uncertainty follow-up

The previously documented caster-state fixture workaround exposed a distinct
resolver defect: Mask evaluated unknown conditions before rejecting a fighter
outside the effect zone. The existing theory adds one case with enemy state97
observed but caster states unknown. bash_341 exited1:8passed,1failed; only the
unknown-caster case fails with an empty plan. The entry gate assertion passes.

SpellResolver.Mask now rejects fighters outside the zone before evaluating
state/breed conditions, retaining the exact uppercase-C global-caster exception.
It moves an existing geometric requirement earlier; it does not assume any
unknown state or suppress uncertainty on an actual in-zone recipient. Category
evaluation inside the zone is unchanged. Related validation is subscribed as
bash_342; it completed with exit0:366passed,0failed,0skipped in21seconds.
Receipt `GivreClone.Tests/TestResults/mask-zone-green.trx` includes the new
unknown-outside-zone case and existing global-caster/resolver cases. Full-suite
validation after this new production change bash_343 completed with exit0:
4841passed,0failed,2existing skips,4843total in6minutes8seconds. Receipt:
`GivreClone.Tests/TestResults/mask-zone-full.trx`. PacketBus also passed in
this run; that does not establish a fix for its earlier timing-dependent
failure. No live deployment occurred.

## Bounded installed-catalog candidate search

Read-only investigation st_01a1b0fb surveyed an existing cache of196spell rows,
413grade descriptors across breeds1-20. The matching cached candidate is
Panique13781 grades1/2: action1080 uses mask A,E516. Both descriptors also
contain action754, outside the currently modeled immediate utility contract,
so they are not evidence that the admission change enables a complete installed
spell. No action754 row was discarded to manufacture admission. The exact
installed inspection already launched by that investigation remains pending;
catalog hashes and current fields must come from that result. This finite cache
survey does not establish the absence of other candidates in the full catalog.
