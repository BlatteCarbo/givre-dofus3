# Bastionbot: zero-cast defeat at09:43

Observed094337_pid1073741827_défaite.log:6.0seconds,four own turns,
zero casts and one confirmed movement. Turn1 starts2721/3026HP,12AP5MP;
turn2 has12AP6MP,turn3 also12AP6MP,turn4 has13AP5MP. This is not
evidence of entering combat at critically low life or lacking action points.
Enemies Ino-Naru/Fangshu remain full3100/3300HP. TueurAGages constrains
target to actor-1. Actor dies after turn4.

Raw transport near line29560 retains planner decisions missing from the old
replay:turn1 chooses328->259 for position73.25,damage0,work60000,
bounded=True. Server confirms in32ms. Subsequent turn1 replan is empty,
still12AP,MP0 in snapshot,position73.25,work60000,bounded=True.
Three of20 spells are excluded:12762/12743 resolver coverage,14011 caster
condition.17 spells admitted;their existence alone does not establish a
legal positive-damage cast under challenge/geometry restrictions.

Read-only process probe confirms live PID7132 and original window6751846,
running givre-caster-side-effect-20261106. QA12692/35456 are separate.
The earlier absent window inventory did not mean the live process exited.

Saved ExpeditionLast snapshot before any deployment:
`78|942|1|2|||0|0|95|1|0|0|0|1|1|1|1|0`.
No script restart or preset modification performed.

Replay investigation assigned st_01a1b00d. No root cause/fix claimed until
current-source replay distinguishes bounded search,geometry and constraints.

Parent execution exposed a tooling limitation:the full session contains several
captures with the same pid/turn,so the original replay aborts on duplicate
offset0. Added optional `--at` to select a capture by its first fragment time,
then retain subsequent contiguous offsets until the next offset0. Filtering
each line by identical time was rejected after JSON truncation at10500:
fragments can cross milliseconds. Original duplicate/missing-offset checks
remain. Current run uses turn2,budget60000,`--at 09:43:43.033`;result pending.

Current-source execution now succeeds:turn2 remains empty at60000 and180000
work,both exhausted,score-644. Turn3 at60000 is also empty,score-644.
Turn4 at60000 remains empty,13AP5MP,score-1099.1667,incoming1148.
Each process exits0. Tripling the budget does not establish a useful fix.
Replay boundary remains logged fields only,without live SpellLookup or omitted
state-damage catalogs;these are reproduced symptoms,not complete server replay.

Endpoint turn1 also reproduced at60000,exit0:12AP0MP,no actions,
incoming0,position73.25,total73.25,budget exhausted. Thus all four captured
empty decisions reproduce;the pre-movement turn1 state remains uncaptured.
Source review identifies a testable search-order concern:movement origins are
sorted using Nearest over all living enemies,while Eligible obeys per-spell
AllowedTargets. Under TueurAGages,the nearest enemy can be forbidden. This
is a hypothesis only;changing ordering requires an independent regression
that shows a legal productive action is actually missed,not merely a larger
score or abandoning the challenge. No production search change made.

Single-spell counterfactual12755 on turn2 terminates with no actions at38260
work,BudgetExhausted=false,score-644,exit0. This weakens the hypothesis that
budget exhaustion alone explains the missing attack:that spell has no chosen
productive branch even when its isolated search finishes. Original target/cell
constraints preserved. Second isolated direct attack12728 now running.

All14 damage spells tested individually returned empty.13 finished below
60000work;12727 alone exhausted60000. No missed legal attack demonstrated
by this isolation.12755 has AllowedTargets[-1],line cast,range0..6,LOS,
and AllowedCells including443. Source investigation independently proves
AllowedCells freezes orientation and caster-zone safety before simulated
walking. A node-local serializable zone policy is needed to preserve safety
without rejecting later legal origins. Implementation assigned st_01a1b04b;
its relation to this captured defeat remains unproven.
