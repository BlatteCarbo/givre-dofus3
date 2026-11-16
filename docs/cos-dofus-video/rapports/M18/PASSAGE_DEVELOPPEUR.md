# M18 — Handoff développeur (Osamodas)

**Objectif :** modéliser invocations indépendantes, boost Piqûre Motivante et capacité sans mélanger absence d'observation et absence d'effet.

## Trois exigences de priorité

1. État partagé minimal : `summonerId, summonId, templateId, cell, alive, states, ap/mp per actor, owner, initiative, capAndOccupancy, buffSourceAndExpiry`.
2. Transition pure par événement confirmé ; ne jamais appliquer de consommation, mouvement, gain PA/PM ou état après une simple intention envoyée.
3. Tests opposant précisément état légal, refus, délai, changement de cible et impact sur le plan de groupe.

## Règles à rapprocher du moteur

- **M18-R01** Une invocation possède son identifiant, ses PV, sa case, son propriétaire et son ordre de tour propres. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/osamodas/). Limite : Un acteur invoqué ne doit pas être confondu avec son invocateur pour l'identité d'une action.
- **M18-R02** Piqûre Motivante a des effets différents selon cible invocation/allié, décrits en 3.7.12. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/osamodas/). Limite : Les gains AP/MP doivent être attribués au bénéficiaire observé et à la bonne durée.
- **M18-R03** Les familles de créatures et leurs variantes ont des caractéristiques/actions distinctes. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/osamodas/). Limite : Une invocation générique sans template/rang ne suffit pas pour simuler ses actions.

## Contrat de données et tests

Entrées : état `S_t` réel, sorts rang/variante, actorId, cibleId, cellule, initiative, identifiants entités, événements confirmés. Sortie : `S_t+1` et actions légales recalculées, `Unknown` lorsque les observations nécessaires manquent. Les impacts différés restent en attente.

- **P01 :** Case libre et limite non atteinte ; l'invocation confirmée dispose de son propre ID et son attaque au tour suivant est attribuée à la créature.
- **N01 :** Même carte, mais plafond atteint ou case occupée ; aucune créature fantôme ne peut agir.
- **N02 :** Un monstre de zone détruit les deux invocations avant le prochain tour ; le plan doit recalculer les coûts de remplacement.
- **N03 :** inverse l'initiative dans `Osamodas + Ouginak : les entités peuvent influencer l'entourage de la cible, piste HUZ à vérifier` et vérifier que le bénéfice arrive au bon tour.
- **N04 :** refus/Unknown conserve l'état précédent et interdit tout replay aveugle.

**Intégration à inspecter avant changement** : `GivreClone.App/Services/Combat/SpellResolver.cs`, `CombatTurnPlanner.cs`, `TeamIntentBoard.cs` et lecteurs de messages `GivreClone.Protocol`. Ces noms proviennent d'une lecture locale ; aucun raccordement n'a été effectué.

## Preuves bloquantes

Rangs/coûts/PO exacts en client 3.7.13.17, captures de plateau, ordre d'événements et tests documentés. Aucun résultat `valide-test` ni `valide-live`.

