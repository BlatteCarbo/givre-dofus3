# M20 — Handoff développeur (Steamer)

**Objectif :** modéliser tourelles, états d'Évolution et attaques autonomes sans mélanger absence d'observation et absence d'effet.

## Trois exigences de priorité

1. État partagé minimal : `turretId, template, evolutionLevel, alive, cell, summonTurns, cooldown, targetRangeAndLos, ownerAndTeam, apGainEvents`.
2. Transition pure par événement confirmé ; ne jamais appliquer de consommation, mouvement, gain PA/PM ou état après une simple intention envoyée.
3. Tests opposant précisément état légal, refus, délai, changement de cible et impact sur le plan de groupe.

## Règles à rapprocher du moteur

- **M20-R01** Chaque tourelle est une invocation persistante qui possède identité, position, état d'évolution et actions propres. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/steamer/). Limite : La tourelle ne peut pas agir une fois morte ; sa nouvelle invocation recommence avec un nouvel état.
- **M20-R02** Évolution change le jeu d'actions ou les bonus de la tourelle selon son palier. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/steamer/). Limite : Le palier ne se déduit pas du nombre de tours écoulés si la montée a été refusée.
- **M20-R03** Des sorts du Steamer peuvent interagir avec la présence/position de tourelles pour générer PA, soin ou contrôle. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/steamer/). Limite : Le gain de PA dépend des conditions exactes du sort et doit être confirmé.

## Contrat de données et tests

Entrées : état `S_t` réel, sorts rang/variante, actorId, cibleId, cellule, initiative, identifiants entités, événements confirmés. Sortie : `S_t+1` et actions légales recalculées, `Unknown` lorsque les observations nécessaires manquent. Les impacts différés restent en attente.

- **P01 :** Tourelle confirmée vivante, évolution confirmée, cible dans sa portée légale ; les effets sur cette cible sont attribués à l'acteur/tourelle correspondant.
- **N01 :** Tourelle détruite entre pose et évolution ; un ancien niveau d'Évolution et les dégâts attendus n'existent plus.
- **N02 :** L'adversaire force un déplacement de la tourelle ou se met hors de ligne ; les PA d'investissement ne sont pas récupérés avant la fin du combat.
- **N03 :** inverse l'initiative dans `Steamer + Huppermage : duo documenté avec contrôle progressif dans VID-HUPPER-DUO-01` et vérifier que le bénéfice arrive au bon tour.
- **N04 :** refus/Unknown conserve l'état précédent et interdit tout replay aveugle.

**Intégration à inspecter avant changement** : `GivreClone.App/Services/Combat/SpellResolver.cs`, `CombatTurnPlanner.cs`, `TeamIntentBoard.cs` et lecteurs de messages `GivreClone.Protocol`. Ces noms proviennent d'une lecture locale ; aucun raccordement n'a été effectué.

## Preuves bloquantes

Rangs/coûts/PO exacts en client 3.7.13.17, captures de plateau, ordre d'événements et tests documentés. Aucun résultat `valide-test` ni `valide-live`.

