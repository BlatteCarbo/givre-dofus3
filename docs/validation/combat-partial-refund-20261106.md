# Incomplete immediate utility admission

Gemini read-only investigation st_01a1af55 identified a concrete use of
unsupported immediate activation as a cheap resource generator. Parent read
IsAdmitted, HasModeledEnemyDamage and the existing resolver/planner test owner.
The existing unknown-side-row damage contract was preserved unchanged.

## Causal proof

The new two-row theory uses a synthetic1AP spell refunding2AP to its caster
plus unknown action1009, and a7AP attack with6AP initially available. No real
Detonateur refund amount or actual bomb statistics are inferred from this
fixture. bash_226 / mon_VKY2D9J70N65NHT9 compiled against unchanged production:
exit1,1 failed and1 passed,279ms. Immediate activation failed Assert.Empty with
the concrete actions cast20(1AP),cast10(7AP). Delayed activation control passed.

## Correction and checks

IsAdmitted now requires all immediate utility rows to be statically modeled,
unless the descriptor has immediate modeled enemy damage. The latter preserves
the established attack-with-unknown-side-row contract and uncertainty score.
Deferred rows remain deferred, not executed. No bomb explosion, wall, scaling
or lifecycle semantics were invented, and the resolver diagnostics are intact.

bash_227 / mon_G8K32RKQNZCWGRBG passed302/302, zero failed/skipped,1m25s,exit0.
Coverage:CombatPlannerResolverTests,AutoSpellRotationTests,
SpellModifierRealDataTests,CombatTurnPlannerTests. This includes the causal
regression, its delayed control, the original unknown-side-row damage keeper
and installed Sram invisibility utility cases.

Full managed validation bash_228 / mon_6XPZYQS6VA5NHTB3 completed exit0:
4809 total,4807 passed,zero failed,2 existing skips,3m43s. It used the same
isolated build, --no-build --no-restore and partial-refund-full.trx. This also
includes the preceding caster-side-effect fix. No live deployment or official
combat acceptance is claimed. LSP remains unavailable
because its workspace enumeration encounters the existing denied pypdf path;
no access permissions were changed.
