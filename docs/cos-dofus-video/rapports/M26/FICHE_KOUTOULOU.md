# M26 — Fiche de mécanique Folie, échanges et réduction alliée

**Version** : guide Koutoulou incluant correctif 3.7 ; sous-patch cible annoncé 3.7.13.17 non certifié. Sources [MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) et [Temple de Koutoulou](https://www.dofuspourlesnoobs.com/temple-de-koutoulou.html). Statuts `corrobore-source` et `hypothese`, aucun test/live.

## Machine à états par personnage

```text
F(actor) = {
  folie: entier observé ou Unknown,
  position: cellule confirmée ou Unknown,
  statuses: {Pesanteur, Enraciné, Indéplaçable, ...},
  visitedInCurrentTurn: []  // positions avant/après permutations
  repeatedSwaps: (otherActorId -> nombre observé),
  seenByBossAtEndTurn: bool/Unknown
}
Boss = { id, position, turn, visibleFighters, ownSwapCooldown, allies, fightPhase }
```

**Événements à distinguer** :
1. `StartTurn(actor)` : une décroissance de Folie n'est validée que si une variation d'état est observée, même si le guide décrit cette règle.
2. `DirectDamage(actor, target, effectLine)` : déplacement forcé possible pour le lanceur ; identifier **l'entité la plus proche avant chaque ligne**, pas après le sort entier. Si l'entité ciblée est alliée, une réduction de Folie sur la cible est possible depuis 3.7.
3. `Swap(source, partner, from, to)` : actualiser **les deux positions** et le compteur de répétition du couple.
4. `EndTurn(actor)` : vérifier si l'allié finit en ligne de vue boss et si un gain de Folie apparaît.
5. `BossStartTurn(boss)` : exécuter seulement les téléportations effectivement émises par le boss, sous règles propres à ses sorts.

## Séquence contrôlée positive — tour de sauvetage proposé

**État initial inventé :** A1 Féca, A2 Iop, boss B et autre monstre M. A2 se trouve en Folie élevée mais **en deçà du niveau fatal** ; A1 peut viser A2 avec un sort de dommage de faible intensité selon sa propre variante. PA/PM, résistances et ligne de vue sont à extraire du client réel. A2 possède une case sûre à finir après ses déplacements.

| Ordre | Acteur | Action proposée | Précondition | Transition à observer |
| --- | --- | --- | --- | --- |
| 1 | A1 | Attaque admissible sur A2 | Folie A2 connue ; sort visant allié réellement autorisé ; risque PV acceptable | Réduction de Folie sur A2 et dommages à A2 **indépendants** |
| 2 | A1 | Retraite hors ligne de vue B | Destination libre, PM connus | Case A1 confirmée, LdV recalculée |
| 3 | A2 | Attaque ou repositionnement après action A1 | Folie mise à jour avant sa décision, boss accessible ou menace à éviter | Si une attaque directe, vérifier le swap lié à chaque ligne |
| 4 | B | Tour de boss | Ligne de vue et états sur A1/A2 connus | Téléportations et dommages réellement confirmés, nouvelles cases |

La réussite du scénario est **conditionnelle** à la validation des événements de Folie et à l'état de menace ; ce tableau n'est pas une transcription de tour de jeu.

## Deux contre-scénarios cruciaux

**N1 : A2 ne peut pas être ciblé par le sort d'A1 ou l'action est refusée.** Aucun niveau de Folie ne doit être décrémenté fictivement. Si A1 devait ensuite soigner A2, vérifier la disponibilité du soin et de la cible.

**N2 : A1 frappe directement le boss en plusieurs lignes à la place d'A2.** Le premier effet peut déplacer A1 et changer le partenaire le plus proche ; les effets suivants sont recalculés. Ne jamais conserver une position « avant sort » pendant toutes les lignes.

## Scénarios additionnels

- **Initiative inversée A2→A1** : le soin de Folie arrive après l'exposition de A2 au boss ; calculer séparément la survie de chaque tour.
- **Invocation comme obstacle** : si une invocation bloque la LdV au début du tour du boss, vérifier qu'elle est encore vivante et correctement positionnée avant de compter sur sa présence.
- **Pesanteur** : empêche une capacité de téléportation du boss selon le guide, mais ne garantit pas d'empêcher le déplacement forcé déclenché par les dommages directs du joueur.
- **Folie dix** : ne pas continuer le tour sur l'hypothèse qu'un soin PV compensera un état fatal ; établir le seuil exact du patch et sa transition.
- **Challenge Intouchable** : une attaque sur allié peut constituer une violation selon le texte/attribution en vigueur ; ne pas privilégier la réduction de Folie sans revoir l'objectif.

## Sources et développement

Références M26-R01 à R08 du [rapport](RAPPORT.md). Recommandation : traiter Folie comme un état **actor-scoped**, avec modifications serveur événementielles et risques à l'horizon de tour. Vérifier par replay un cas avec deux attaques directes, un dommage sur allié et un début de tour. Ce replay n'a pas été réalisé dans cette recherche.

