# M22 — Handoff développeur (Ouginak)

**Objectif :** modéliser Proie, Rage et contrôle du corps-à-corps sans mélanger absence d'observation et absence d'effet.

## Trois exigences de priorité

1. État partagé minimal : `preyId, preyState, rageCounter, transformedState, adjacentFightersAndSummons, legalAttackRange, resists, selfLifeAndRisk`.
2. Transition pure par événement confirmé ; ne jamais appliquer de consommation, mouvement, gain PA/PM ou état après une simple intention envoyée.
3. Tests opposant précisément état légal, refus, délai, changement de cible et impact sur le plan de groupe.

## Règles à rapprocher du moteur

- **M22-R01** La cible Proie est une identité/état spécifique qui peut influencer les dégâts et les soins perçus par les alliés. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/ouginak/). Limite : Un changement de Proie doit invalider les calculs établis sur l'ancienne cible.
- **M22-R02** Rage et les états dérivés se modifient selon des événements de coups décrits par la source 3.7.12. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/ouginak/). Limite : Le compteur ne doit pas croître sur une attaque refusée ou une cible hors portée.
- **M22-R03** La transformation change les options de déplacement/portée et le profil de risque. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/ouginak/). Limite : Une routine de corps-à-corps ne doit pas se déclencher sur une cible qui s'est téléportée.

## Contrat de données et tests

Entrées : état `S_t` réel, sorts rang/variante, actorId, cibleId, cellule, initiative, identifiants entités, événements confirmés. Sortie : `S_t+1` et actions légales recalculées, `Unknown` lorsque les observations nécessaires manquent. Les impacts différés restent en attente.

- **P01 :** Proie E reste identifiée et à portée ; le bonus conditionnel éventuel est enregistré uniquement si l'état d'attaque et de Proie est confirmé.
- **N01 :** Proie E se déplace hors de portée avant le tour Ouginak ; le bot abandonne son plan de mêlée au lieu de lancer à travers la carte.
- **N02 :** Un monstre à longue portée fuit ; la création d'invocations autour de la cible gêne l'allié ou augmente les dégâts de zone ennemis.
- **N03 :** inverse l'initiative dans `Ouginak + Osamodas : idées de placement des créatures autour de la proie discutées dans VID-DUO19-01` et vérifier que le bénéfice arrive au bon tour.
- **N04 :** refus/Unknown conserve l'état précédent et interdit tout replay aveugle.

**Intégration à inspecter avant changement** : `GivreClone.App/Services/Combat/SpellResolver.cs`, `CombatTurnPlanner.cs`, `TeamIntentBoard.cs` et lecteurs de messages `GivreClone.Protocol`. Ces noms proviennent d'une lecture locale ; aucun raccordement n'a été effectué.

## Preuves bloquantes

Rangs/coûts/PO exacts en client 3.7.13.17, captures de plateau, ordre d'événements et tests documentés. Aucun résultat `valide-test` ni `valide-live`.

