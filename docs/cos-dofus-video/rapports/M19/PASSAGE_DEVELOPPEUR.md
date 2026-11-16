# M19 — Handoff développeur (Huppermage)

**Objectif :** modéliser états élémentaires, combinaisons de deux éléments et runes sans mélanger absence d'observation et absence d'effet.

## Trois exigences de priorité

1. État partagé minimal : `targetId→elementState, elementHistory, rune[owner,cell,element,duration], cooldowns, ap/mp, targetPosition, comboEffectVersion`.
2. Transition pure par événement confirmé ; ne jamais appliquer de consommation, mouvement, gain PA/PM ou état après une simple intention envoyée.
3. Tests opposant précisément état légal, refus, délai, changement de cible et impact sur le plan de groupe.

## Règles à rapprocher du moteur

- **M19-R01** Une cible reçoit un état élémentaire lié à l'action réellement résolue ; la combinaison ne se déclenche qu'à la succession conforme de deux éléments. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/huppermage/). Limite : Conserver par cible l'état précédent, pas un drapeau global Huppermage.
- **M19-R02** Le couple d'éléments détermine la catégorie d'effet ; les six couples doivent être discriminés. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/huppermage/). Limite : Ne pas attribuer un retrait PM universel à toute combinaison.
- **M19-R03** Les runes sont des ressources de terrain distinctes des états élémentaires sur combattants. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/huppermage/). Limite : Une rune expirée, déplacée ou consommée ne doit plus participer à l'effet.

## Contrat de données et tests

Entrées : état `S_t` réel, sorts rang/variante, actorId, cibleId, cellule, initiative, identifiants entités, événements confirmés. Sortie : `S_t+1` et actions légales recalculées, `Unknown` lorsque les observations nécessaires manquent. Les impacts différés restent en attente.

- **P01 :** Deux effets de deux éléments différents sont confirmés sur la même cible avant expiration ; une combinaison spécifique est attendue selon le tableau du patch.
- **N01 :** Même séquence avec première application refusée ou cible changée : aucun effet de combinaison ne peut être inventé.
- **N02 :** La cible disparaît/est désenvoûtée avant le deuxième sort ou un ennemi tue la tourelle de l'allié durant l'installation du terrain.
- **N03 :** inverse l'initiative dans `Huppermage + Steamer : contrôle et tourelles ; +Sadida : endurance et propagation ; +Zobal : protection` et vérifier que le bénéfice arrive au bon tour.
- **N04 :** refus/Unknown conserve l'état précédent et interdit tout replay aveugle.

**Intégration à inspecter avant changement** : `GivreClone.App/Services/Combat/SpellResolver.cs`, `CombatTurnPlanner.cs`, `TeamIntentBoard.cs` et lecteurs de messages `GivreClone.Protocol`. Ces noms proviennent d'une lecture locale ; aucun raccordement n'a été effectué.

## Preuves bloquantes

Rangs/coûts/PO exacts en client 3.7.13.17, captures de plateau, ordre d'événements et tests documentés. Aucun résultat `valide-test` ni `valide-live`.

