# Active class tactics request: Eliotrope self-support portals, Sram invisibility, Cra threat-aware kite, Sadida utilities; current code restriction discovered

User currently requests implementing useful nonoffensive/class-specific tactics for Eliotrope, Sadida, Sram, Cra and other classes, plus measured Dragoeuf farm and previous reliability/economy follow-through. Examples are tactical intent, not verified game rules: inspect cooldown/invisibility break/recast rules before promising kill then invisibility.

Active goal and todo registered. Notepad: %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/.omo/evidence/givre-tactics-20261112/notepad.md. Tier HEAVY. No source implementation yet.


Current increment actual evidence: portal-support-red.txt 1fail1pass (injuredself no cast); after planner support-target change portal-support-focused.txt5pass, actualRaygrade3extension7pass. Adding real production ZonePolicy exposed another failure (portal-support-zone-red.txt6pass1fail). RunFightTurn synthetic support caller3pass1fail: states omitted in PlannerFighter, so Heal refuses unknown incurable76 absence. Added conservative observed-present/explicitly-removed-state wiring with StatesKnown=false; never mark all states known from a partial boost. Changes still under test, not deployed/live-proven. New combined monitor pending. Keep exact regressions in notepad.

Catalogue actual: Cabale14617grade3level174 gives200power2MP via portal vs100power1MP direct; action406 unsupported prevents safe admission until modeled. Neutral action1183duration1. Invisibility12913minCastInterval3 allgrades; no evidence kills reset cooldown. Child class/state reports claimed artifact writes but lead reads ENOENT; do not treat as verification.

Inspected CombatTurnPlanner.cs 606–640: PortalShotSpell requires HasModeledEnemyDamage; PortalShots additionally rejects !Enemy targets. Thus current production path excludes pure support portal casts and self-return healing even if resolver supports their effects. PortalPlacementValue at642–675 scores only enemy-damage shooters with remaining current AP and small firstportal future heuristic. Need evaluate real rules and risk before replacing.

Live read list_bots: Eliobot176, Shadowbot199, Sylvanbot161, Archerbot76 scripts running. Read-only set_spell_variant requires arguments {bot:accountId,spellId:0}, not accountId key. Kams now enumerates all22classspell pairs incl Cicatrisation, Neutral, Distribution, Cabale. Enumeration alone not proof grade/effect admission or live cast.

Existing other-classes read-only explore task st_01a1cedd; lead owns shared planner, no concurrent writers. Previous delivery green5374pass2skip is baseline, not proof new tactics. Preserve no commits, no unauthorized economic QA spending, Shadowbot regen and Turbo20 cap, separate server accounts.
