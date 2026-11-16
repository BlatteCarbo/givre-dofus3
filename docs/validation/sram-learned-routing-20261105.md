# Sram learned routing: no friendly eligibility loss established

## Decision

Do not change `AutoSpellRotation.Build` to classify action150 as support.
The proposed causal chain stops at the actual learned adapter:
`TargetKind=Enemy` does not restrict targets for an automatically appended
spell. No production or test source was changed in this investigation.

This is a source-traced no-bug result for the suspected routing defect,
not proof that Sram combat behavior is complete. No failing actual routing
regression was established, so there is no red/green fix receipt.

## Current exported descriptor and conversion

Evidence is the existing current export
`docs/cos-dofus-video/rapports-v2/N01/SORTS_3.7.13.17.json:5463-5750`,
corroborated by the installed-data investigation in
`sram-reveal-contract-20261105.md`. No large fixture was added.

| Grade | Level record | AP | Range | MP diceNum |
| ---: | ---: | ---: | --- | ---: |
| 1 | 40916 | 2 | 0-4 | 1 |
| 2 | 40917 | 2 | 0-5 | 2 |
| 3 | 40918 | 2 | 0-6 | 2 |

All grades have flags512, interval3, initialCooldown0, globalCooldown1,
zero per-turn/per-target caps, and an empty states criterion. Each has:

1. action150, mask `a,A`, duration1, zero dice/value;
2. action128, mask `a`, duration2, diceNum1/2/2, diceSide0, value0.

Both effects have element-1, point shape80, size1, minSize0, delay0,
random0, and trigger `I`.

`GameNameResolver.ReadSpellDamage` (`GameNameResolver.cs:590-702`) retains
both effects, raw dice/trigger/zone fields and durations; computes fixed
Min/Max0 for150 and1/2/2 for128; and uses `Classify`, yielding Unknown
for150 and Buff for128. It marks the descriptor uncertain for the nonzero
globalCooldown. `GetSpellDescriptor` at462 returns the exact grade only.
This trace does not execute the Unity bundle parser or prove which catalog
version a running process selected.

`CombatTurnPlanner.IsAdmitted` (`CombatTurnPlanner.cs:88-91`) does not use
the descriptor's strict `CanPlan`/`Uncertain` gate. Both actions are immediate
modeled rows in `SpellResolver`, so this descriptor is admitted in the
`RequireDescriptors:true` path used by non-script automatic combat.
`ShouldUsePlanner` already recognizes the allied immediate128 row. Unknown
action150 classification is not an admission failure here.

The descriptor-level `Targets` intersection is Self|Ally, because the128
mask is allied-only. That intersection is not used to restrict planner cast
centers. Resolver selection is per effect, so the legal enemy invisibility
possibility is not lost to that intersection either.

## Exact routing and execution path

1. `LaunchService.Combat.Turn.cs:514-522` builds from learned exact-grade
   descriptors with `RequireDescriptors:true`. `Build` excludes every spell
   already listed in the profile, including disabled entries. It appends12913
   with `OnMe=false`, `SelfBuff=false`, `TargetKind=Enemy`, and PerTurn99
   because action150 fails its all-support predicate. Existing profile objects
   retain their original settings (`AutoSpellRotation.cs:39-84`).
2. At `Turn.cs:1039-1047`, `configured` is computed from **profile.Spells**,
   not the generated rotation. For appended12913 it is false. The OnMe/Ally/
   Enemy narrowing occurs only under `if (configured && ...)`. The following
   return admits every friendly fighter, and enemies passing `TargetOk`.
   Default generated HP gates pass (`LaunchService.Combat.cs:890-894`).
   Previous-use exclusion, server caps and other ordinary legality gates
   still apply; this is not an assertion that every fighter is always legal.
3. `CombatTurnPlanner.cs:470-477` checks AllowedTargets, life, target cap,
   HP gates, and current visibility. The caster is allowed to target itself
   even while invisible; other directly targeted fighters must be visible.
   Cast centers are enumerated from those eligible fighters at600-611.
   Neither generated TargetKind nor descriptor.Targets is consulted here.
4. `SpellResolver.cs:223-245` selects `a` as allied, including self, and `A`
   as enemy, combining category tokens with OR. The point zone affects the
   selected cell only. At408-421 action128 grants MP only where its allied
   mask matches, while150 hides whichever selected fighter its `a,A` mask
   matches. Enemy invisibility therefore remains a modeled legal possibility
   without granting the enemy allied MP.
5. `Turn.cs:1165-1185` revalidates selected casts. Friendly action targets
   are not reclassified through cfg.TargetKind/OnMe; enemy-specific target
   checks apply only to enemies. After acknowledgment,1200-1215 accounts for
   immediate self MP with `ExpectedSelfPointGain`, records the cast, and
   marks nondamaging support use. A friendly invisibility cast is not marked
   as an enemy hit by this path.

For **manual** profile entries, configured is true: OnMe restricts to self;
Ally restricts to nonself friendlies; Enemy restricts to enemies. Those are
intentional profile settings, not learned eligibility loss. A support-only
rotation with an unmodeled attack can still use fallback rather than the
planner; that separate boundary is covered by existing gate tests and was
not broadened here.

## Verification receipt and limits

Fresh LSP requests for both `AutoSpellRotation.cs` and
`AutoSpellRotationTests.cs` each returned:
`Timed out waiting for fresh diagnostics ... within 3000ms`.
No clean diagnostics or fresh compilation is claimed.

An asynchronous `dotnet vstest` baseline run completed with exit code0:
**86 executed, 86 passed, 0 failed, 0 skipped/notExecuted**. Exact TRX:
`%USERPROFILE%/AppData/Local/Temp/givre-sram-routing-20261105/existing-routing-baseline.trx`.
It selected all `AutoSpellRotationTests` plus:

- `SpellResolverTests.Sram_invisibility_immediate_row_is_modeled_separately_from_ally_mp`;
- `CombatTurnPlannerTests.Invisible_caster_can_still_target_its_own_cell`;
- `CombatTurnPlannerTests.Target_hidden_by_a_cast_is_not_available_to_a_later_targeted_cast`.

The assembly was the existing isolated
`Temp/givre-parent-self-visibility-20261105/bin/GivreClone.Tests/debug/GivreClone.Tests.dll`,
modified2026-11-05T11:09:19.443Z. It was **not freshly built** in this task.
The rotation source timestamp observed was2026-11-05T09:03:09.222Z; timestamp
ordering alone is not proof of source/assembly identity. Results are baseline
evidence, not a new current-source build receipt. No shared output, full UI
build, account, network operation, or git operation was used.

The existing Sram resolver test proves enemy invisibility without enemy MP;
the existing generated MP regression proves that self MP can enable movement
and an attack. They do **not** execute the inline LaunchService adapter or
provide a faithful all-three-grade12913 ally/self routing regression. Copying
that inline predicate into a new test would not protect the production adapter,
and adding a production seam solely for a no-bug hypothesis is unjustified.
This missing automated adapter coverage remains explicit; source tracing is
the evidence for the no-loss conclusion.

No defensive invisibility valuation, reveal hook, expiry ordering, duration
guess, cooldown-reset implementation, or generic class profile was added.
Choosing a useful friendly cast is separate from preserving eligibility;
legal eligibility does not promise that the present scoring will choose it.
