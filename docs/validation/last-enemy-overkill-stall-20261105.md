# Last-enemy overkill scoring stall

## Observed incident

Official-client combat journal
`userdata/combat/2026-11-05/220205_pid8664_victoire.log` records 111
turns in 21.2 seconds, 21 injected spells and 105 turns without a spell.
The target remains at 30/9013 life from turn 7. Turn 111 triggers the existing
110-turn pause. The victory label and zero counted problems do not establish
a healthy session.

Parent independently read the transport log and reconstructed all 23 JSON
fragments of the turn-8 empty-plan snapshot. It has 14 AP, 5 MP, a visible,
confirmed enemy at cell 297, self at 284, and 19 admitted spells. Target HP
gates are unrestricted. The empty plan scores 223.804 position points before
two uncertainty points, despite only 30 enemy life remaining.

The speculative next-turn score used uncapped single-cast damage. Finishing
the enemy removes that speculative score, so waiting could beat the actual
kill. The correction caps that damage estimate at remaining life plus shield,
before applying the existing half-weight and distance factor.

## Reproduction and checks

`CombatTurnPlannerTests.Finishing_the_last_enemy_beats_saving_overkill_damage_for_next_turn`
uses the existing planner boundary: one adjacent enemy at 30/9013 life, a
500-damage attack, no movement and no enemy reply. It asserts the chosen cast
and exactly 30 scored damage. No production test seam or mock was added.

- Before correction, mon_XQHFPKZ0439JFQJW / bash_83 failed the one test with
  `Assert.Single() Failure: The collection was empty`, exit 1,
  `last-enemy-red.trx`.
- First related verification, mon_XD0WXF963EMPDSWE / bash_84, did not run
  tests: MSB3027/MSB3021 reported that the running GivreClone process 8212
  locked the output apphost. This is not a test failure or a green result.
- Verification ran with independent output artifacts in
  `Temp/givre-last-enemy-20261105`, mon_DZY9QTBQ5KCJR4X4 / bash_85,
  `last-enemy-isolated-green.trx`: 194 passed, zero failed or skipped,
  12.9 seconds, exit 0. Restore was enabled for this new artifact path.
- Integrated verification mon_5Q2MGG64512TCAK8 / bash_86 passed 4766 of
  4768 tests, zero failures and two existing skips, 177.3 seconds, exit 0,
  `last-enemy-full.trx`, using the same independent artifact directory.

This minimized regression is not a complete replay of all installed spell
subcasts or the captured search budget. No post-correction live fight has
been performed. The C# language server still crashes on access to the existing
`artifacts/kourial-tools/pypdf` directory; compilation supplies diagnostics.

## Disconnect evidence

Parent read transport lines 10883-10901 of
`userdata/logs/transport-2026-11-05_21-53-15.log` independently of the read-only
child investigation. Enemy death is logged at 22:02:25.828, result issue=2 at
22:02:26.345, and game-server connection loss at 22:02:26.590 local time.
The relevant sequence contains no explicit sanction code, reason or duration.
The user reports a temporary ban; these logs establish the disconnect but
not its server-side cause. Application injection suspension is not itself
proof of an account sanction.

The parent stopped a subsequently observed reconnect and confirmed the bot
offline with pid 0. No reconnect or combat was initiated during this diagnosis.

## Direct review and loaded binary follow-up

The requested read-only scoring reviewer st_01a1ada1 failed with zero tool
calls because its designated Argon provider timed out. No independent review
was delivered. Parent reviewed `NextTurnPotential`, its caller in `Score`,
the regression and the logged-state replay directly instead.

The cap applies to each candidate's immediate damage before the existing
half-weight and distance attenuation. It prevents an empty aggressive plan
from earning damage beyond that target's remaining life and shield; it does
not change admission, AP cost, targeting or server acknowledgements. No new
finding was identified in this one-line correction. Residual risks are the
existing mean-damage approximation, finite search budget and callbacks omitted
by the logged-state replay.

The regression now also includes a 1/1802-life target with 150 expected reply
damage. Both rows passed within the 195-test focused run (bash_90, exit 0,
`last-enemy-one-hp.trx`). The full logged Shadowbot turn-99 replay chose the lethal
3-AP spell 12902 (damage 1, finish reward 190) at the live 60000-work budget;
see `sylvanbot-miner-live-20261105.md` for the replay boundary.

Process 22616 loaded the corrected Release app after the old live process
closed normally. Its Sylvanbot punching-bag diagnostic reproduced a separate
resolver exception, so that session does not establish healthy end-to-end
combat or live acceptance of the coup-fatal correction.
