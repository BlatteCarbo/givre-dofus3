# GivreClone combat AI v2 - 2026-11-02

Worktree %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024 (no commits without user authorization).

## Built
- SpellResolver (Services/Combat/SpellResolver.cs): pure simulation of real spell effects. It covers zones, target masks, life steal, sub-casts 1160/792/2960 and more, states, AP/MP, shields, summons and marks. On installed data, immediate damage is modeled for 814/862 class spells (94%); before, about 0 spells were plannable.
- CombatTurnPlanner now runs on the resolver. TeamIntentBoard lets our bots in one fight share their intents. Accounts gained an uptime display and a disconnect history.
- Follow-up fix: when approach is prohibited, the planner can still retreat to clear line of sight before attacking. The focused movement/planner tests and full suite passed (3,992 passed, 0 failed, 2 pre-existing skipped) on 2026-11-02. This is automated test evidence only; no live-combat validation was reported.
- JondoEmu 683ab49 (Temp/givre-jondo-lab-20261102) is used as a semantic reference only. It has no license, so its code is not copied.

## Live QA recipe
- Control pipe: \\.\pipe\GivreWork-97A1F223CE35E55D (int32 LE + JSON). Use exact account ids.
  - Valrianess (Eniripsa 37): 00fe352d06bf491fb5abcc2029ce84d7
  - Sylvanbot (Sadida 157): 4bc58e7f9db345aaa2e7dfdaf9ae1726
  - Bastionbot (Sacrieur 195, account ArcherbotLeRetour): c4a95aedecaf4a82803e6c64eb33ca38
- Restart flow: build Release with -o Temp/givre-live-20261102/outN, kill the old PID, Start-Process the new exe.
- Party: Lua team:invite("Sylvanbot") on the leader. Read the party id from the log line '[équipe] invitation REÇUE ... -> party N', then team:accept(N) on the follower.
- App team: run_supervisor with teams.create/setCombatJoin/form.
- Never run two dotnet builds in parallel on the same worktree: the shared obj/ref corrupts ('Image is too small').

## Dofensive bestiary (user partner, authorized scraping 2026-11-02)
- The SPA calls a public API. First call /api/security/token with the anonymous value hard-coded in the site's ApiStore-*.js; this sets a cookie.
  - Then GET /api/dofus2/bestiary/monsters/preview?lang=fr (5204 monsters), /monsters/{id}, /spells/{id}.
  - Error 1001 means the token must be refreshed.
  - Node fetch fails on this host; curl and urllib work.
- Local copy: %LOCALAPPDATA%\GivreReplica\dofensive.
  - scrape.py: resumable cache, about 8 req/s.
  - compile_threat.py writes monster-threat.json as {monster:{grade:[ap,mp,reach,turnDamage]}}.
  - Turn damage is computed as (min+max)/2 × (100+element stat)/100 + flat, with a knapsack over AP and MaxCastPerTurn.
  - Sanity check: Sanglacier 2845 grade 1 = 10 AP, 454 damage per turn.
- App side: Combat/MonsterThreatCatalog plus LaunchService.MonsterThreat. Monsters are keyed by _fightMonsters (GenericId, Grade).
  - The planner uses AttackRange, MovePoints and ExpectedDamage, taking the max of the bestiary and the observed hit.

## Dofensive follow-up (2026-11-03)
- The user explicitly confirmed they are partnered with Dofensive and authorized scraping all monster spells/ranges for combat AI use (source: 2026-11-02 user message).
- The 2026-11-03 continuation reported extending the threat catalog with observed per-enemy actual hit damage, reset between fights, and preserving conservative estimates before an enemy has hit. Full suite reported 4078 passed, 2 skipped, 0 failed, including three catalog tests.
- The bestiary scrape later completed all 5204 monsters with zero failures and began retrieving 8487 monster spells; the latest captured progress was 3000/8487, so spell retrieval and the full generated threat summary remain unconfirmed. `compile_threat.py` initially failed on a grade with no casts (`max()` on an empty iterable); it was fixed to clamp negative AP to zero, and calculation was independently checked against a real Sanglacier spell. Do not treat the catalog as complete until retrieval and summary generation finish.
- Full-suite status during the 2026-11-03 continuation was unresolved: 4078 passed, 2 skipped at one checkpoint, but a later run found the installed-data Sanglacier regression expectation failing because all five installed grades had reach and damage set to zero. The engine retains fallback estimates; the test must not invent or assert unavailable real damage. A final full-suite result was not captured.
- Separate Sram/Roublard target-mask work propagated observed monster template and summoner ID to the planner/resolver; focused checks reported 68 passing, including cleanup between fights. It does not model bomb explosions or trap activation. State-mask planning now distinguishes unknown states from confirmed absence (101 tests reported); simulated effects can establish presence, but server-state observation is still not reliably connected. Protocol state fields remain obfuscated, so do not substitute an assumed older schema. No live verification was reported.

## Regressions caught by checks
- LiveCombatRegressionTests cast-refusal wake-up failed only under the full suite.
  - Cause: NoteTurboCastRejection resolved the info text (which can lazily load the game data, taking seconds) BEFORE pulsing the barrier.
  - Fix: pulse first, log text after, resolve only when names.IsReady.
- A follow-up child (st_01a19d38) ended before verifying. It left NativeTeamFollowTests not compiling (?? between List and array) and a stale RequiredMapping manifest.
  - Always rerun build + the full suite yourself after a child.

## Live results
- Solo v2: Valrianess beat 4 monsters (zone hit + life steal), finishing at 62/274 HP.
- Team: Bastionbot + Sylvanbot won in 5 turns / 38 s and auto-join worked. Defects found:
  - silent cast refusals (TextInformation only, 1.2 s barrier wait each);
  - replanning at 180k work for every single-cell move (1.2-1.9 s per step);
  - team view never read by Sylvanbot;
  - ally left unprotected (86 HP).
- User feedback: the fight is too slow. The user wants the game's native auto-follow (PartyFollowMemberRequest ijo, FightAutoJoin ikg, FightAutoReady iko), not the follower walking onto the leader's cell.

## Tactical visual tracing status (2026-11-03)

The in-progress investigation identified `ub.bzv` as processing three distinct map-decoration layers, with `ub.qih` and `uj.qjy` creating/configuring graphical objects. Constructors and call-site scans did not yet establish which branches are safe to hide without affecting interactive objects or overlays. A direct call-site scan found a jump to `ub.qih` and pointer references to both factories; this remains tracing evidence, not a validated visual fix. Keep this work explicitly open until the owning storage/branch is identified and verified in-game. No active client modification was made during this investigation.
