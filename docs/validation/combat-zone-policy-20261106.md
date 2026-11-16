# Origin-dependent combat zone policy

Defect proven from source: live planner snapshots froze ZoneOk/EnemyZoneOk at
the current caster cell into AllowedCells. The planner later simulated walking,
but a center rejected from the starting origin could not be reconsidered from
a legal destination.

Change: PlannerSpell carries an optional serializable PlannerZonePolicy with
zone geometry, minimum enemy hits, enemy-safety requirement and challenge-
forbidden enemy IDs. CastOn evaluates it from the simulated caster cell and
current board before resolution. Static AllowedCells keep walkability, refused
centers and initial occupancy constraints. Execution-time live gates remain.
Missing policy preserves legacy snapshot behavior.

Causal red: temporarily used the original caster cell in policy evaluation;
CombatPlannerResolverTests 56 passed, 1 failed, total 57. Restored n.Cell.
Focused green: resolver, performance and live regressions 67 passed, 0 failed.
Receipts: zone-policy-red.trx and zone-policy-focused.trx.

Boundary: replay of Bastionbot's 09:43 capture remains empty and predates this
policy; this change is not claimed to explain that defeat.

Full main suite: 4827 passed, 0 failed, 2 skipped, total 4829 (zone-policy-full.trx).
Guarded deployment replaced PID15304 with PID29684. DLL SHA256
9F06611923FDF634CE7428A1ECEDE50F331F6027208652589F2C0EB6D958B0AC.
Previously online sessions restored by explicit account requests: Bastionbot and
Sylvanbot on Mikhal, Shadowbot alone on Dakal. Connection requests accepted; in-game
confirmation and a live origin-dependent zone cast remain pending.

Zero-cast survey since 11:00: 13 of 88 combat journals contain at least one
turn with no cast. Active challenges in those fights: Statue (2), LigneDeMire
(2), Zombie (2), Dernier, Versatile, Focus, TueurAGages, Prudent, and none (2).

Case 11:54:27 (Bastionbot, Statue, defeat, 4 empty turns of 5) was captured on the
zone-policy build: the snapshot already carries ZonePolicy. Replay of turn 1:
12AP 0MP (Statue forbids moving), self cell 118, sole enemy cell 288, no
actions. With the standard MapPoint formula, 118=(10,2) and 288=(18,-2), so
distance 12, above every modeled damage range (max 6). These empty turns are
therefore consistent with a legal no-attack position, not the zone defect.
The actual weakness is challenge selection: Statue was accepted with a lived
success of 20/51 while the only enemy was out of reach and could approach.
Distance computed by hand from the formula, not by the tool.
