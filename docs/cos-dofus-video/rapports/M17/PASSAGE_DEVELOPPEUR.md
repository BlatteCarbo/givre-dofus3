# M17 — Handoff développeur (Ecaflip)

**Objectif :** modéliser main de cartes, Pile ou Face et effets conditionnels sans mélanger absence d'observation et absence d'effet.

## Trois exigences de priorité

1. État partagé minimal : `main: [rank, suit, origin], max cards, activeSide(Pile/Face/Unknown), actionConfirmed, buffs, target, apRemaining, pendingEffects`.
2. Transition pure par événement confirmé ; ne jamais appliquer de consommation, mouvement, gain PA/PM ou état après une simple intention envoyée.
3. Tests opposant précisément état légal, refus, délai, changement de cible et impact sur le plan de groupe.

## Règles à rapprocher du moteur

- **M17-R01** Chaque sort élémentaire concerné peut alimenter une Main avec une carte d'élément et de rang associés au coût ; limite annoncée de quatre cartes. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/ecaflip/). Limite : Ne jamais générer une carte sur une action refusée ou sur un sort hors règle du passif.
- **M17-R02** Pile ou Face modifie les conditions des sorts Lapement, Mésaventure, Destin d'Ecaflip et Esprit Félin selon le guide 3.7.12. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/ecaflip/). Limite : Ce statut ne prouve pas le tirage exact, le détail des états ni le changement d'un nouveau patch.
- **M17-R03** Rekop dépend de la combinaison actuelle de la Main et la défausse, selon la base communautaire 3.7.12. Preuve : [guide classe 3.7.12](https://dofus-grimoire.fr/classes/ecaflip/). Limite : La valeur d'une main ne peut pas être ramenée à son nombre de cartes.

## Contrat de données et tests

Entrées : état `S_t` réel, sorts rang/variante, actorId, cibleId, cellule, initiative, identifiants entités, événements confirmés. Sortie : `S_t+1` et actions légales recalculées, `Unknown` lorsque les observations nécessaires manquent. Les impacts différés restent en attente.

- **P01 :** Cible éligible ; le sort élémentaire est confirmé puis la Main s'enrichit. L'action de consommation est légale et retire les cartes attendues d'après le serveur.
- **N01 :** Même plateau, mais la première demande de sort échoue : le moteur n'ajoute pas de carte et ne prévoit pas une Main gagnante fictive.
- **N02 :** Un combat court se termine avant d'accumuler une Main utile ; une issue de hasard inconnue fait préférer une décision robuste plutôt qu'un plan unique.
- **N03 :** inverse l'initiative dans `Écaflip + Crâ : allié distance profite d'une fenêtre de soutien, à évaluer selon durée des boosts` et vérifier que le bénéfice arrive au bon tour.
- **N04 :** refus/Unknown conserve l'état précédent et interdit tout replay aveugle.

**Intégration à inspecter avant changement** : `GivreClone.App/Services/Combat/SpellResolver.cs`, `CombatTurnPlanner.cs`, `TeamIntentBoard.cs` et lecteurs de messages `GivreClone.Protocol`. Ces noms proviennent d'une lecture locale ; aucun raccordement n'a été effectué.

## Preuves bloquantes

Rangs/coûts/PO exacts en client 3.7.13.17, captures de plateau, ordre d'événements et tests documentés. Aucun résultat `valide-test` ni `valide-live`.

