# Bastionbot repeated swap investigation

Parent fully read074752_pid1073741825_defaite journal (filename uses accented
defaite) in the live profile combat/2026-11-06 directory:179 lines.
Outcome0 is accompanied by explicit death of actor33202504034 at+4.981s;
this is an actual defeat, not merely a neutral outcome interpretation.
Lichangoro+Fangshu+Kurookin remained alive after two own turns.

Turn1 cast12733 on342 swapped self327->342 and enemy342->327; a second cast
of12733 on327 restored both starting positions. Two later casts followed.
Turn2 used four casts; no walking request occurred in either turn. This
proves an immediately reversed placement sequence, not that this sequence
alone caused death or that12733 has no other useful effect.

Parent stopped only Bastionbot's script through stop_script; acceptance was
followed by get_bot_state confirming scriptRunning=false,outside combat,
1637/3063HP,map206307842(20,-29). The account stayed connected.

Current Turn.cs1161 intentionally discards the remaining plan after each cast
to reconcile the real board. Therefore two consecutive live choices need
not be the two steps of one simulated plan. Current planner control scoring
adds AP/MP removal and ally point grants, not a raw movement-event bonus.
The initial hypothesis of cumulative swap control credit is not established.
Next evidence needed: the installed12733 descriptor, its recursive effects,
and the legal continuation selected before and after its confirmed swap.
No spell blacklist or network timing change is justified by this trace alone.

## Installed descriptor evidence

bash_243 completed exit0 using the existing SpellCatalogInspect tool and real
GameNameResolver. Bundle SHA256:
525048D9A4406720BA79FB6A23826B9AC5E40D4A87DCD3C452E1A0CCC27B2A30.
All three grades cost2AP,range1-2,three casts/turn,two/target. Effects in order:
action8 mask a,A,*e7 followed by action98 mask A,immediate damage.
Damage ranges by grade:9-11,11-13,14-17. Thus the spell is not merely a
placement utility; reversing positions may still deal damage. The earlier
description of these casts as useless is not established without damage and
alternative-action comparison. No blacklist is warranted.

Resolver recalculates each effect's zone and targets after previous movement
(SpellResolver.cs309-315). Whether damage follows the moved target versus the
original center needs an independently established game contract before a fix.

The resident bastionbotResumeSource is an older Frigost harvesting script with
no custom fight turn handler, but the fresh stopped state names a different
saved multi-profession script. The resident source cannot establish which
turn-driving mode produced the Pandala trace. Turn.cs937 excludes scriptDriven
turns from the planner. Need the actually selected script/transport evidence
before attributing this live sequence to planner search or classical rotation.
