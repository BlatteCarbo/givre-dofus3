# Living summon capacity: parent verification

Parent read CombatSummonCapacityTests.cs, SummonOwnershipIntegrationTests.cs,
the planner admission and post-resolution capacity checks, the live dispatch
capacity guard and CountMySummons in LaunchService.Combat.cs.

The planner counts living fighters owned by the current caster and applies both
the observed characteristic and configured spell ceiling. Its tests exercise
shared capacity across spells, another actor's summons, a full ceiling with a
regular attack still available, and a new plan after removal of a dead summon.

The integration test uses the actual service, summon observations and decoded
server Death event. It switches to a controlled companion, checks that ownership
follows that actor, injects the decoded death and checks that exactly one slot is
freed while the surviving summon remains. This is an offline protocol harness,
not a real-server combat.

Parent command:

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --artifacts-path %USERPROFILE%/AppData/Local/Temp/givre-parent-journal-r4-final-20261105 --no-build --filter "FullyQualifiedName~CombatSummonCapacity|FullyQualifiedName~SummonOwnershipIntegration" --verbosity minimal
```

Monitor mon_ZJ9M04WQQZQK7WPC / bash_54 completed with exit 0:
13 passed, zero failed or skipped, 6.9 s. This uses the current debug test output
produced by the R4 build; no summon source or tests were changed for this check.

The broader living-summon todo remains open until replacement is observed in
real combat. These tests do not prove that a pure summon is tactically valued,
that summon-specific class mechanics are modeled, or that the server accepts
the replacement cast. No app, account or game was launched by this check.
