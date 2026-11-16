# Observed summon grade contract - 2026-11-05

## Result and evidence limit

Read-only implementation-enabling investigation. No code, tests, builds, git operations, or live actions were performed. The smallest observed adapter already has a decoder and observation store: `FightMonsterReader` -> `FightMonster.Grade` -> `_fightMonsters[pid][ActorId]` -> the existing `monster` local in planner construction. `LaunchService.Combat.Turn.cs:1000-1008` supplies template and owner but does not supply `PlannerFighter.MonsterGrade`.

The repository catalog explicitly exposes **grade separately from level**. It is not necessary to guess grade from level or template. However, no decompiled `MonsterFighter.cs` was located under `C:/Users/A`, and no clear game proto was located in the worktree. Consequently the sources inspected prove the named grade scalar and its exact transport path, not that every real observed summon carries a positive ordinal in `1..n`, nor that a particular ordinal matches a particular monster-data array index. Synthetic fixtures are not passive wire captures. Do not claim those stronger semantics or observed integration completion from this report.

`data/bundle-build.json` declares `3.7.11.11` and describes passive replay as a structural candidate, not an ACK or send certificate. This report makes no claim about a later patch or an active session's exact mapping. `ProtocolCatalog.cs:41-70,154-160` loads field mappings from `fields.json` and resolves wire field numbers by name; its clear beta proto is not an authoritative wire-number fallback. Runtime `MappingCatalog.cs` can use the streamed catalog rather than these repository files. Keep the adapter catalog-driven.

## Actual catalog and decoder chain

Sources: `data/fields.json:176-185,2703-2712,5205-5208,5232-5236,7393-7399`; `server-mapping/fields.json:5205-5208,5232-5236` agrees on the monster grade/level fields. These are actual checked-in entries, not inferred tags:

- `ActorPositionInformation`: `actor_id`, `disposition`, `actor_information`.
- `ActorInformation`: `fighter` (also `look` and the separate `role_play_actor`).
- `FightFighterInformation`: `ai_fighter`, `stats`, `spawn_information`, `character_id`, plus other variants.
- `AIFighterInformation.monster_fighter_information`.
- `MonsterFighter`: `creature_grade`, `creature_level`, `monster_gid` are three distinct fields.
- `MonsterInGroupInformation`: `grade`, `level`, `gid`, `look` are distinct fields.
- `SummonContextInformation`: `characteristics`, `summons`, `look`, `spawn_information`, plus other fields. No named monster grade/gid field exists directly on this context in the checked catalog; opaque `fvjl` is not evidence of one.

`GivreClone.Protocol/FightMonsterReader.cs:9-58` already defines `FightMonster(ActorId, GenericId, Grade, Appearance, Summoner)` and follows:

```text
ActorPositionInformation.actor_id                         -> FightMonster.ActorId
ActorPositionInformation.actor_information
  ActorInformation.fighter
    FightFighterInformation.ai_fighter
      AIFighterInformation.monster_fighter_information
        MonsterFighter.monster_gid                       -> GenericId
        MonsterFighter.creature_grade                    -> Grade
```

The scalar reader requires a varint, uses the catalog resolver, and returns zero if the mapped field or message is absent. `creature_level` is not substituted. A negative actor can yield an observation with template/grade zero even without the monster submessage; a positive actor lacking that submessage is ignored. A missing mapping is not permission to copy a numeric tag from this report.

Packet entry paths already handled by that reader:

1. `FightFighterShowEvent.information` or `FightFighterRefreshEvent.information` -> actor position above.
2. `GameActionFightEvent.summons` -> `Summons.summons_by_actor` -> repeated `SummonsByActor.summons` -> actor position above.
3. `GameActionFightEvent.summons` -> `Summons.summons_by_context_information` -> repeated `SummonsByContextInformation.summons` -> repeated `SummonContextInformation.summons` -> `SpawnInformation.position` -> actor position above. Grade is available only if that position actually contains the fighter/monster chain. Shared context look and characteristics do not supply a grade. A bare position remains grade zero.

The reader does not gate these paths on action IDs 181 or 1008. `FightStatsReader.cs:276-336` separately decodes summon position/team/owner into `SummonedFighter`; its lack of a grade member does not block the existing parallel `FightMonsterReader` path.

## Existing observation, identity, and minimal adapter

`LaunchService.Packets.cs:958` calls `ObserveTreasureFighters` on server packets while in a fight. Despite its treasure-oriented name, `LaunchService.Combat.Treasure.cs:14,22-30` stores every decoded `FightMonster` in `_fightMonsters[pid][monster.ActorId]`. `LaunchService.Combat.Turn.cs:968-974` retrieves the observation using the current roster entry's `f.ActorId`, before constructing `PlannerFighter`. `CombatTurnPlanner.cs:11-18` already declares optional `int MonsterGrade = 0`; its resolver projection preserves `f.MonsterGrade` at line 201.

Minimal proposed production change, for the parent to decide and implement: add `MonsterGrade: monster?.Grade ?? 0` beside `MonsterTemplate: monster?.GenericId ?? 0` in this existing planner construction. No new dictionary, decoder, `SummonedFighter` positional argument, or owner-dependent grade lookup is needed. This carries a decoded scalar unchanged and preserves zero as unknown. It does not fill information absent from a grouped packet.

Identity invariants:

- Read grade and template from the same `(pid, f.ActorId)` observation. Never select a monster by cell, negative-ID ordering, template equality, summoner, character ID, or the currently controlled actor.
- `_summonOwners[pid][summonActor]` is owner identity, not a monster-grade key. `OnSummoned` (`LaunchService.Combat.cs:736-757`) uses explicit `sm.Summoner`, then nonzero action source, and stores ownership against `sm.Id`. Keep this unchanged. `CountMySummons` compares owner to current `SelfActorId`; grade must not follow that changing view.
- Planner `SummonerId` already prefers `_summonOwners[f.ActorId]`, then `monster.Summoner`. Preserve this precedence. The grade adapter must not change ally/team or ownership classification.
- `PurgeFightState` (`LaunchService.Combat.cs:245-268`) removes `_fightMonsters`, `_summonOwners`, and roster state per pid. Death removes the actor from `_fighters` (`LaunchService.Packets.cs:983-996`), so planner projection no longer sees it. The inspected death handler does not remove its `_fightMonsters` entry; same-fight actor-ID reuse would require separate evidence/handling, not a guessed ownership alias.
- Existing observation updates replace the whole `FightMonster` record. A subsequent sparse observation can therefore replace a known grade with zero. The minimal adapter preserves this conservative behavior rather than retaining an unproven old grade. Do not broaden this task into an observation-merging policy without evidence.

`LaunchService.LotF.cs:110-112` already consumes the same observation's `(GenericId, Grade)` for `MonsterThreatCatalog.Find`. That catalog has a fallback to another grade (`MonsterThreatCatalog.cs:27-39`); the fallback threat is not an observed grade and must never be fed back into `PlannerFighter.MonsterGrade`.

## Map actor extraction is a separate source

`MapActorsReader.cs:15-16,76-78,96-108` defines `MonsterMember(GenericId, Level)` and follows `ActorInformation.role_play_actor` -> `RolePlayActor.monster_group_actor` -> `MonsterGroupActor.identification` -> `MonsterGroupStaticInformation.main_creature` / repeated `underlings` -> `MonsterInGroupInformation.gid` and `.level`. It does not read the catalog's `.grade`. A map group actor ID identifies a group, not an individual future fight actor. Do not pair these members with combat IDs by index or infer a summon grade from map-level data. No MapActorsReader change is needed for the minimal existing fight observation adapter.

## Existing fixtures and what they do not prove

Read, not run:

- `FightMonsterReaderTests.Show_reads_monster_id_grade_and_structural_look`: synthetic show shape with `monster_gid=1234`, `creature_grade=2`; exercises the exact decoder chain. The same `Position` helper can exercise refresh by using `FightFighterRefreshEvent.information`; no refresh assertion currently exists in this file.
- `FightMonsterReaderTests.Four_actor_summons_can_share_id_and_grade_but_differ_by_skin`: repeated by-actor shape, grade and template on each actor, source-owner fallback. Confirms same template/grade does not identify an actor.
- `FightMonsterReaderTests.Context_summons_use_shared_look_and_summoner_without_requiring_monster_gid`: context shape with bare position and shared look, source fallback. It currently asserts identity/appearance/owner, not grade zero. Nested `Position` from the same fixture can exercise a context position containing monster data, but that is a proposed synthetic case, not an existing captured shape.
- `SummonReaderTests.Ally_summon_of_any_action_type_carries_its_team_and_summoner`, `Every_summon_is_read_not_only_the_first`, `Grouped_summons_share_their_summoner_and_keep_their_own_team`, `Missing_spawn_or_summoner_stays_unknown_and_other_actions_read_nothing`: encoded synthetic by-actor/context fixtures using catalog fields, including action 181/1008, omitted team zero and absent metadata. Their source explicitly says no attested network payload. These cover `FightStatsReader`, not planner grade projection.
- `SummonOwnershipIntegrationTests.Summon_capacity_and_script_ownership_follow_the_actor_not_the_observation_turn`: real service instance with player and controlled fighter summons, context switch, and decoded death. Suitable neighboring integration evidence for keeping grade keyed by actor rather than active owner, but it currently has no grade assertion.
- `SummonDispatchIntegrationTests.Automatic_dispatch_reopens_only_for_the_active_actors_decoded_summon_death`: player/companion variants and other-owner death versus own death; useful existing control-context harness, not a grade or passive-wire test.
- `MonsterGradeStatsTests.Monster_grade_preserves_elemental_resistances_and_weaknesses` asks game DB stats for level 130. That level-based API test is not evidence that observed `creature_grade` equals level or that the grade wire value has a proved ordinal domain.

The parent can validate the small adapter with an integration fixture that supplies distinct positive grades to two different actor IDs owned by player/companion, switches control, and observes each projected grade unchanged; include absent grade -> zero and a bare context spawn -> zero. Such tests would verify dataflow, not manufacture missing live semantics.

## Exact remaining wire evidence

For the stronger claim that real summon grade is ordinal `1..n` aligned with monster-grade data, obtain one of: the actual family 3.7 decompiled producer/consumer assigning or indexing `MonsterFighter.creature_grade`, or an attributable passive decoded capture paired with authoritative monster grade definitions. For grouped summons lacking nested monster information, obtain the actual decoded payload/schema semantics for the opaque context field before treating it as grade-bearing. Neither level, appearance, action effect `DiceSide`, owner grade, nor template is a substitute for that missing observation.

The accepted simulation preservation work is outside this read-only track. This report proposes only exposing the already decoded observed scalar; observed integration remains for the parent to implement and verify.
