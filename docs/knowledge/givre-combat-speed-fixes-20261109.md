# Vitesse de combat et robustesse d'expédition — mesures du 09/11/2026

Build déployé vers `%USERPROFILE%/AppData/Local/Temp/givre-live-20261107c/app/`
(`GivreClone.dll` 35 568 128 o). Suite : **5 245 réussis, 0 échec, 2 skips volontaires**.

## Mesures en jeu, avant / après

| Indicateur | Avant (audit 514 combats) | Après (build 09:43) |
|---|---|---|
| Inter-tour ennemi moyen | 3 900–4 066 ms | **1 784 ms** (p90 4 182, med 1 426) |
| `plan_ms` max | 23 362 ms | **347 ms**, 0 occurrence > 1 000 ms |
| `BARRIERE NON FERMEE` | 293 (Archerbot) | **2** |
| Secondes par tour | 6–7 s | **1,2–2,3 s** (Bastionbot 1,60 ; Shadowbot 2,30 ; Archerbot 1,17) |
| Part de log des dumps de diagnostic | 57,3 % (8,5 Mo) | **0 %** |

## Deux bugs trouvés pendant la validation

### 1. `action_failed` en combat tuait toute l'expédition
`ExpeditionScript.cs` : `attempt(fight, ...)` renvoyait `false`, et `move()` faisait
`return false` pour toute raison autre que `no_matching_group` → le script s'arrêtait.
Observé sur Shadowbot (« attempts=3 reason=action_failed », puis « script terminé »), à
rebours de la consigne « même après 10 000 défaites il continue ».
Corrigé : le combat indisponible journalise et passe à la carte suivante. Le test
`ExpeditionRouteRegressionTests.Persistent_fight_failure_stops_without_skipping_the_intended_map`
épinglait l'ancien arrêt ; renommé en `..._keeps_the_expedition_running_...`.
Scripts déjà enregistrés de Bastionbot et Shadowbot patchés en place dans `planning.json`
(sauvegarde `planning.json.avant-fix-actionfailed-20261109`).

### 2. Dump de 56 Ko sérialisé dans le chemin critique du tour
`LaunchService.Combat.Turn.cs` : un plan vide déclenchait `JsonSerializer.Serialize`
de tout l'état de combat (fighters, cells, spells) découpé en lignes de 3 500 car.
Or un plan vide **après au moins une action** est la fin de tour normale : le dump
tombait à chaque fin de tour, 2 495 lignes / 8,5 Mo sur un log de 14,5 Mo.
Conditionné à `decision == 0 && !_actedThisTurn` : seul le tour réellement inactif
est tracé.

### 3. Bans temporaires de 5 min : cadence d'accusés, pas les relances seules
Bastionbot a pris deux `IDENTIFICATION REFUSÉE : BANNED` (09:47 puis 10:42), chaque fois
juste après un `FightEndEvent` pendant la transition de carte. Mesure du trafic sortant :
**85-87 `GameActionAcknowledgementRequest` par seconde** plus 16-20 `FightTurnReadyRequest`,
soit des pics à 110 trames/s, contre 2 600 trames/min reçues pour Shadowbot au même moment.

Deux causes cumulées, les deux corrigées :
- `AdaptiveReadyPolicy` donnait une gigue **par message** (10-21 ms étrangers, 15-35 ms les
  nôtres) sans aucune limite de **débit soutenu**, et sur deux compteurs séparés alors que le
  serveur compte le total. Ajout de `SustainedAckSpacingMs` (30/50/80 ms selon le backoff)
  avec un créneau partagé : nos accusés gardent leur gigue courte (la barrière Turbo les
  attend) mais consomment un créneau ; les accusés étrangers cèdent et sont **abandonnés**
  au-delà de `MaxAckDeferralMs` (900 ms), ce que fait déjà le vrai client (0/242 accusés).
- `AdaptiveBurstRetries` à 8 relances (mon correctif d'inter-tour) ajoutait 16 requêtes/s.
  Ramené à 5 (3 en backoff modéré) : ~1,1 s de couverture, le timeout serveur étant à ~3,3 s.

Résultat sur 10 min de jeu continu avec 4 comptes : **0 ban, 0 fermeture serveur en jeu**
(la seule fermeture du log est Sylvanbot *avant acceptation du login*, sans lien avec le débit),
accusés propres descendus de 85/s à 25/s de pic, et vitesse de combat préservée
(Bastionbot 2,52 s/tour, Shadowbot 2,29 s/tour, Sylvanbot 1,13 s/tour).

## Vague du 09/11 après-midi : winrate 49,5 % -> 96,3 %

Mesuré sur 54 combats réels (12:21 -> 12:31, 47 430 lignes) après 4 correctifs et 1 réglage.
Quatre agents Gemini ont produit les analyses (`analysis-{winrate,refus175,pathfinding,moverefused}-20261109.md`) ;
chaque chiffre décisif a été recontrôlé par une mesure indépendante avant d'être utilisé.

| Indicateur | Avant | Après |
|---|---|---|
| Victoires | 200/404 = **49,5 %** | 52/54 = **96,3 %** (Bastionbot 100 %, Shadowbot 94 %) |
| Refus serveur | 136,8 / 100k lignes | **27,4 / 100k** (-80 %) |
| Bans temporaires | 2 | **0** |
| Secondes par tour | 6-7 s à l'origine | 2,14 s (Bastionbot), 2,84 s (Shadowbot) |

### Le réglage qui rapporte le plus : maxMonsters 4 -> 2
Winrate par taille de groupe engagée, mesuré sur 404 combats : 1 mob **95 %**, 2 mobs **55 %**,
3 mobs **18 %**, 4 mobs **0 % sur 12 combats**. Les groupes de 3+ concentraient **129 des 204
défaites (63 %)** pour seulement 25 victoires. `config:setMaxMonsters(2)` sur Bastionbot et Shadowbot
(sauvegarde `planning.json.avant-cap2mobs-20261109`). Après : 1 mob 33V/0D, 2 mobs 15V/2D.

### Les 3 correctifs de code
1. **Cases marquées exclues du choix de cible** (`MarkedCells` + filtre `AllowedCells`) :
   Shadowbot (Sram) posait un piège puis rechoisissait la même case dans le même tour -> 365 refus
   « Il y a un piège sur cette case » (message 229). Couverture inconnue = ensemble vide, pour
   ne jamais bloquer le tour. Test ajouté dans `FightMarksIntegrationTests`.
2. **Portée minimale contrôlée sur le chemin « sur soi »** : le code lançait sur sa propre case
   sans vérifier `MinRange` (le commentaire disait « aucun ciblage ni portée »). 149 refus
   « portée de 1 à 1 et vous visez à 0 » (message 171), paramètres serveur `[1 | 1 | 0]`.
3. **Mémoire des refus 175** (`_spellRefusedAtTurn`, 4 points de refus branchés, purge en fin de
   combat) : la rotation classique relançait le sort refusé au tour suivant. 147 refus, dont
   40 relances dans le même tour (médiane 58 ms après l'essai précédent).

### Correction d'une régression que j'avais introduite
La cadence d'accusés à 30 ms **coûtait de la vitesse** : les accusés étrangers partaient avec
165 ms de retard moyen et le tour ennemi coûtait 258 ms par séquence contre 218 ms sans cadence
(+18 %). Ramenée à 18 ms (35 / 60 en backoff) : plafond ~55/s, toujours très loin des 85-87/s
qui déclenchaient les bans.

Deux chiffres annoncés plus tôt dans la session étaient faux car mesurés sur 16 intervalles :
l'inter-tour ennemi n'est jamais descendu à 1 784 ms (3 673 ms sur 638 intervalles), et le
passage de 8 à 5 relances Ready n'a rien changé (3 778 -> 3 564 ms, dans le bruit).

## Dette technique à surveiller
- `GameNameResolver` : `EnsureLoaded`/`EnsureCombatLoaded` rechargeaient les bundles
  Unity par instance. Deux caches statiques (`s_combatCache`, `s_fullCache`, clés =
  dossier Content + chemin I18n) partagent les tables ; c'est ce qui a débloqué les
  7 classes de tests qui dépassaient le `--blame-hang-timeout`.
- `maxMonsters=4` sur Akwadala reste le réglage de l'utilisateur alors que l'audit
  mesure 9 % de victoires à 4 mobs (contre 43 % à 3). Non modifié : c'est son choix.
- `bot-settings.json` est réécrit à chaud par l'application ; toute édition manuelle
  de la régénération y serait écrasée. Passer par l'UI ou le pipe de contrôle.
- Le LSP `csharp-ls` plante systématiquement sur
  `artifacts/kourial-tools/pypdf` (`UnauthorizedAccessException`), sans effet sur le build.

## Pilotage des bots sans l'UI
Le pipe de contrôle `GivreWork-<SHA256(DOMAINE\user)[..16]>` accepte `list_bots`,
`read_bot_console`, `run_lua_script`, `stop_script`, `travel_to`, `connect_bot`…
Trame = en-tête 4 octets little-endian + JSON `{tool, arguments}`.
Quand deux comptes partagent un nom d'affichage, `bot` doit recevoir l'`accountId`
exact (sinon « Ambiguous bot identity »).
