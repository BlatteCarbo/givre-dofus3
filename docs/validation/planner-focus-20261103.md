# Focus challenge search transition

bash_834 reproduced the missing Focus transition: the constrained chain
dealt 80 instead of 40 by attacking two living enemies. The unconstrained
control and the case where the first enemy dies were not reported failing.

The planner snapshot now carries the active Focus rule and existing fight
target, copied under the challenge-state lock. Each search branch processes
resolved enemy damage events in order. A living focused enemy prevents
damage to a different enemy; its death releases the branch lock. Healing,
buffs and other events without enemy damage do not establish that lock.

bash_835 exited 0. planner-focus.trx records 113 executed, 113 passed, zero
failed or not executed, across planner, challenge catalog and finishing-policy
tests. Compilation retained the existing CS8826 partial-method warnings.
This is offline coverage, not a live combat result.

bash_836 subsequently exited 0. planner-focus-initial.trx records seven
executed and passed, zero failures or unexecuted cases. The existing theory
now asserts exact spell sequences and covers a living initial target, a
target killed during the branch, an already-dead initial target and an
absent initial target, alongside the unconstrained control. This confirms
initial lock inheritance and stale-lock release in offline search.

Existing live AllowedTargets and AllowedCells remain conservative: a target
excluded by the observed fight's Focus restriction is not reintroduced merely
because the search predicts a kill. Execution must observe that kill before
rebuilding those restrictions. This change prevents illegal branch scoring
and supports switching in initially unrestricted snapshots; it does not yet
prove full live kill-then-switch planning, server-designated variants, or
the game's precise simultaneous area-damage ordering.
