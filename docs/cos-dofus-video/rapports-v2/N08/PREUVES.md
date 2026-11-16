# N08 — Registre des preuves en lecture seule

**Source unique** : worktree Windows `fix-f01-f16-final-20261024`, lecture le 04/11/2026. Références relatives à sa racine, numéros de lignes constatés et non extraits de logs de combat.

| Référence | Ce qui est lisible | Ce qui reste non démontré |
| --- | --- | --- |
| `GivreClone.App/Services/Combat/SpellResolver.cs:10-16` | `SpellFighter` contient `States`, `StatesKnown`, `AbsentStates` | ID, sémantique exacte et portée de `Insoignable` |
| `GivreClone.App/Services/Combat/SpellResolver.cs:62-66` | Documentation explicite : jets moyens, esquive non modélisée, buffs/durées/expiration/hooks à simuler ailleurs | Déroulement de tour complet |
| `GivreClone.App/Services/Combat/SpellResolver.cs:143-150` | `Heal` : plafonne au manque de PV et émet `HealingResolved` ; pas de garde `Insoignable` dans cette fonction | Effets éventuels appliqués hors du résolveur pur |
| `GivreClone.App/Services/Combat/SpellResolver.cs:370-393` | Soins (81/108/143/407/1109...), shields (1020/1039/1040) effectivement résolus ; état appliqué séparément en 403–411 | Règles serveur combinées, dispel, expiration |
| `GivreClone.App/Services/Combat/SpellResolver.cs:113-120,414-420` | `StatDelta` couvre le correctif d'actions 115/118/138/418 ; le buff de stat modifie l'acteur effectivement ciblé | Aucun besoin de « re-corriger » ce routage |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:451-460` | Vérification des cibles affectées, filtre heal/shield selon besoin de PV et exposition | Statut Insoignable et durée de bouclier |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:479-484` | Chaque PA donné à un allié peut obtenir 6 points de contrôle, PM 3 points, par `PointsResolved` ; absence de coût d'opportunité du tour du bénéficiaire dans ce calcul | Valeur offensive réelle du buff |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:499-516` | Soins plafonnés avec réservations ; shield plafonné au danger estimé ; pas d'expiration dans ce score | Menace future mesurée dans un vrai combat |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:549-568` | Un follow-up est calculé pour le **caster** via `CastOn`, pas pour le tour indépendant du bénéficiaire | Ordre d'initiative et expiration |
| `GivreClone.Tests/CombatTurnPlannerTests.cs:350-392` | Tests `Team_protects_exposed_ally` / `Resource_only_rotation_boosts_an_ally_but_not_an_idle_caster` ; boost d'allié attendu sur action 111/120/128 | Ce test ne couvre pas l'absence de sort utile au prochain tour |
| `GivreClone.Tests/CombatTurnPlannerTests.cs:395-468` | Healer protégé vs non protégé, soins réservés, shield promis et danger initial testés | Bouclier expiré, `Insoignable`, initiative |
| `GivreClone.Tests/CombatTurnPlannerTests.cs:823-841` | Pourcentage de soin calculé sur vie max de la cible déjà couvert | Pas une nouvelle lacune |

**Recherche de texte** : le scan ciblé des implémentations/tests de `SpellResolver` et `CombatTurnPlanner` n'a pas révélé de garde littérale `Insoignable` ; cela ne démontre pas l'absence d'une vérification ailleurs dans l'application. Aucune compilation ni exécution de test pendant N08.
