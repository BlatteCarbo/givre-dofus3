# Global-caster utility with an empty cast center

Read-only investigation st_01a1ad3e identified candidate enumeration in
CombatTurnPlanner.Casts as a distinct beneficiary gap. Parent read the actual
planner, neighboring tests and SpellResolver.Mask before applying the fix.

The synthetic supported descriptor is a point heal with mask C, range one,
NeedFreeCell, 80 healing and a caster at 20/100 life. C selects the caster
globally even outside the cast zone; the cast center must nevertheless be
empty. Previously only fighter positions were considered for point effects
other than teleport/summon, so all candidates failed occupancy checks.

Parent reproduction mon_XWMQ2NTTPFSCGCMM / bash_54 failed the new
CombatTurnPlannerTests.Global_caster_heal_can_use_an_empty_cast_cell with
Assert.Single: the action collection was empty. One failed, exit 1, 4.1 seconds;
report empty-beneficiary-red.trx. This is not a captured learned game spell.

The owner change adds board cells to candidate centers when NeedFreeCell is
set. CanCast retains occupancy, range, line-of-sight and allowed-cell checks.
The separate teleport/summon free restriction is unchanged. The regression
expects spell 20 at the single reachable empty cell and 200 weighted healing
(80 actual healing with the existing low-life survival weighting).

Focused validation mon_J024PM2VA79A7VRC / bash_55 completed with exit 0:
282 passed, zero failed/skipped, 14.4 seconds, report empty-beneficiary-green.trx,
covering CombatTurnPlannerTests, SpellResolverTests, AutoSpellRotationTests and
CombatUtilityDispatchIntegrationTests. No live
gameplay, class completeness or full utility-beneficiary acceptance is claimed.

Full combined verification mon_AAV75SD6E132JVHC / bash_56 completed with exit 0:
4,761 total, 4,759 passed, zero failed, two existing skips, 166.9 seconds.
Report: GivreClone.Tests/TestResults/empty-beneficiary-full.trx. The Release
command used --no-build --no-restore and artifacts directory
%USERPROFILE%/AppData/Local/Temp/givre-mp-dispatch-20261105.

## Applicability boundary

Read-only catalog investigation st_01a1ad46 reported no current learned
class/companion spell uniquely exercising this new enumeration branch.
Its reported installed-data examples Ivresse 12777 and Balise Tactique 32467
already receive empty-center candidates through summon/area effects. The
parent inspected the installed-data test seam in SpellModifierRealDataTests
and the automatic-admission implementation, but has not independently replayed
that catalog extraction. Those IDs are investigation leads, not additional
verified regression evidence. The passing synthetic test establishes the
descriptor contract only; it must not be reported as a reproduced current
learned-spell incident or completion of the broad beneficiary task.

The regression is now a two-row theory named
Global_caster_heal_on_empty_cell_requires_an_allowed_beneficiary. Its negative
row removes allowed targets and requires no actions and zero healing; the
positive row retains the original expected cell and healing. Production was
unchanged by this test extension. mon_AXSWTCWWB9BP8Z3G / bash_57 passed 233/233,
zero failed/skipped, 10.2 seconds, exit 0, empty-beneficiary-allowed.trx. The
earlier full result predates this extra negative test row, not the owner fix.

## Installed-client integration in progress

SpellModifierRealDataTests now loads Ivresse 12777 grade 1 through the real
GameNameResolver and uses the application assembly types throughout automatic
rotation, planner and resolver. Parent read the entire delivered test and
corrected its selection of action 1109: there are two heal rows, so the global
caster row must be selected by its C mask as well as action ID. The caster is
adjacent to the only empty cast center and the selected heal has point shape;
CombatZones.GetCells therefore cannot include the caster geometrically.

The child's initial Debug run was not accepted as verification. Parent observed
its process exit before starting independent isolated Release verification
mon_5S6VZSHXZ2FKV8XN / bash_61, report ivresse-parent.trx. It passed 5/5,
zero failed/skipped, 90.3 seconds, exit 0. Parent read the TRX and confirmed
the Ivresse case itself ran and passed (24.79 seconds), not only its neighbors.
No live cast has been sent. This test exercises real data integration, not a
production incident uniquely caused by the new NeedFreeCell enumeration branch.

The initial sensitivity command mon_SWKP56KMHX90GWAF / bash_62 returned exit 0
without running tests because the fresh artifacts directory had no restored
test SDK assets. That command is not verification. With restore enabled,
mon_MQXA03S5F7JHBBYV / bash_63 compiled the temporary globalCaster=false mutation
and failed the Ivresse case with an empty action/event collection: one failed,
exit 1, 23.7 seconds; ivresse-sensitivity.trx in separate artifacts
givre-ivresse-sensitivity-20261105. Parent read the actual failing TRX.
The original tokens.Contains("C") expression has been restored exactly.
Restored combined verification mon_1HXJZ9KHEPCMAE5S / bash_64 passed 288/288,
zero failed/skipped, 77.4 seconds, exit 0, report ivresse-restored.trx.
This covers real installed-data tests, planner, automatic rotation, resolver and
utility dispatch together. No mutation remains in the source.
