# M21 — Handoff développeur (Sadida)

**Objectif :** modéliser Arbres, Poupées, Infecté et propagation sans mélanger absence d'observation et absence d'effet.

## Trois exigences de priorité

1. État partagé minimal : `tree[owner,id,cell,leafy,alive,expiry], doll[owner,id,cell,alive], targetInfected[actorId,source,expiry], range, zone`.
2. Transition pure par événement confirmé ; ne jamais appliquer de consommation, mouvement, gain PA/PM ou état après une simple intention envoyée.
3. Tests opposant précisément état légal, refus, délai, changement de cible et impact sur le plan de groupe.

## Règles à rapprocher du moteur

- **M21-R01** Les Arbres et Poupées sont des entités/cases du terrain, avec propriétaire, capacité et état vivant. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/sadida/). Limite : Si l'arbre meurt entre pose et déclenchement, sa zone ne doit plus être attendue.
- **M21-R02** Infecté est un état par cible avec source et expiration ; certains sorts peuvent en appliquer/propager les effets. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/sadida/). Limite : Ne pas considérer tous les monstres d'un groupe comme infectés par simple proximité.
- **M21-R03** L'effet indirect sur plusieurs ennemis dépend des règles propres du sort et de l'état de chacun. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/sadida/). Limite : L'infection n'autorise pas tout sort de dégâts à toucher automatiquement toutes les cibles.

## Contrat de données et tests

Entrées : état `S_t` réel, sorts rang/variante, actorId, cibleId, cellule, initiative, identifiants entités, événements confirmés. Sortie : `S_t+1` et actions légales recalculées, `Unknown` lorsque les observations nécessaires manquent. Les impacts différés restent en attente.

- **P01 :** Deux cibles E1/E2 portent Infecté confirmé ; le sort à propagation prévu dans la version 3.7 est légal, la résolution sur chacune est consignée séparément.
- **N01 :** E2 n'est plus Infecté ou l'Arbre propagateur est mort ; E2 ne reçoit pas de dégâts fantômes.
- **N02 :** L'ennemi élimine les poupées/arbres en zone, ou le combat se termine avant la mise en place.
- **N03 :** inverse l'initiative dans `Sadida + Huppermage : contrôle et soutien durable (VID-HUPPER-DUO-01)` et vérifier que le bénéfice arrive au bon tour.
- **N04 :** refus/Unknown conserve l'état précédent et interdit tout replay aveugle.

**Intégration à inspecter avant changement** : `GivreClone.App/Services/Combat/SpellResolver.cs`, `CombatTurnPlanner.cs`, `TeamIntentBoard.cs` et lecteurs de messages `GivreClone.Protocol`. Ces noms proviennent d'une lecture locale ; aucun raccordement n'a été effectué.

## Preuves bloquantes

Rangs/coûts/PO exacts en client 3.7.13.17, captures de plateau, ordre d'événements et tests documentés. Aucun résultat `valide-test` ni `valide-live`.

