# Audit telemetrique de 514 combats (2026-11-08)

Extraction automatisee sur 514 combats complets (`transport-2026-11-08_17-04-40.log` -> `transport-2026-11-08_23-45-56.log`) couvrant Bastionbot (Sacri 199, Mikhal), Shadowbot (Sram 189, Dakal), Sylvanbot (Mineur 159, Mikhal) et Archerbot (Cra 58, Mikhal).

## 1. Les 4 goulets d'etranglement mesures

1. **Trou noir de 2,4 a 3,3 s sur chaque tour ennemi (65% du temps de combat)** :
   - Inter-tour ennemi moyen : `3 900 ms` a `4 066 ms` (p90 `6 604 ms`).
   - Cause prouvee dans la trace : avec seulement 2 relances `FightTurnReadyRequest` a 150 ms, la couverture s'arretait a `t = 335 ms`, laissant le serveur attendre son timeout d'inactivite (~3,3 s) sur le tiers des tours qui depassent 400 ms.
   - Fix : `AdaptiveReadyPolicy` (8 relances progressives a gigue couvrant ~1,8 s, coupure immediate sur `FightTurnEvent`, backoff automatique sur rejet codes 170-177).

2. **Explosion hors-budget de `CombatTurnPlanner.Choose` jusqu'a 23 362 ms** :
   - 151 occurrences `plan_ms > 1000 ms` dans un seul fichier de log, pic a `23,36 s` sur un seul appel.
   - Cause : `if (budget.Used >= budget.Limit) break;` ne sortait que de la boucle interne `foreach (var cast in Casts(origin))`, continuant a parcourir toutes les cases `origins` et tous les noeuds `beam` apres epuisement du budget.
   - Fix : propagation du garde de budget a `Casts(Node)`, `origins`, `beam` et `Finish(Node)`.

3. **Sorts de profil etrangers lances dans le vide (293 `BARRIERE NON FERMEE` sur Archerbot)** :
   - Archerbot (Cra 58) executait un profil contenant `[13528, 14396]` (sorts non appris par un Cra). Le serveur ignorait chaque lancer en silence : 250 ms de timeout barriere par essai, 0 degat inflige en 12 tours, 0% de victoire contre les protecteurs de ressources.
   - Fix : filtrage de `profile.Spells` contre `LearnedSpells(pid)` dans `LaunchService.Combat.Turn.cs` et repli automatique sur `AutoSpellRotation.Build` quand aucun sort actif du profil n'est appris par le personnage.

4. **Winrate Pandala selon PV de depart et taille de groupe (`Akwadala`)** :
   - 44 combats sur 45 demarraient a moins de 60% de PV (moyenne 46% PV, faute de `MIN_LIFE_PERCENT` dans le script genere d'expedition).
   - Winrate mesure par taille de groupe :
     - 1 mob : **91%** (10W / 1L)
     - 2 mobs : **56%** (5W / 4L)
     - 3 mobs : **43%** (6W / 8L)
     - 4 mobs : **9%** (1W / 10L)
   - Recommandation utilisateur : passer `maxMonsters` de 4 a 2-3 et activer une regeneration inter-combat >= 75% PV sur Pandala solo.
