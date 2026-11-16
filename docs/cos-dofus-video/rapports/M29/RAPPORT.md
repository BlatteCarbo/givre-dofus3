# M29 — Challenges avancés : le bon acteur, le bon ennemi et la bonne fin de tour

Étude documentaire 4 novembre 2026, complément à [M07 Duel / De la même lignée](../M07/RAPPORT.md). Source principale [Dofus Pour Les Noobs — Les challenges](https://www.dofuspourlesnoobs.com/les-challenges.html) (liste refondue à partir de 2.68), confrontée au [contexte 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Les formulations ci-dessous viennent d'une **source historique** : statut `corrobore-source` uniquement sur cette version ; le texte ingame 3.7.13.17, le rôle des compagnons et des invocations restent à vérifier.

## Quatre familles de contraintes

| Famille | Exemple décrit dans la source | Mémoire à conserver | Piège pour une team |
| --- | --- | --- | --- |
| Historique par cible | **Duel** : un ennemi ne doit pas être attaqué par des alliés différents | `enemyId → attackerActorId` (avec type d'attaque et source) | Personnage A puis compagnon B touchent X ; A/B distincts si le moteur du jeu les compte |
| Position finale du combattant | **De la même lignée** : finir son tour aligné avec au moins un allié | dernier état de position confirmé **à fin de son tour** | Pandawa lance son partenaire ensuite ; la position finale à examiner est celle du combattant à son propre tour |
| Position collective stricte | **En ligne et contre tous** : chaque allié sur la même ligne que tous les autres | tableau de positions, relation d'alignement | Confondre « au moins un » et « tous » invalide un plan |
| Ressource à la mort | **Infatigable** : lorsque l'allié tue un ennemi, il doit garder au moins la moitié de ses PA initiaux (arrondi inférieur) selon source | acteur tueur, `apAtTurnStart`, `apAtKill` | tuer après avoir utilisé tous ses PA fait échouer le challenge malgré un excellent DPS |
| Soin | **Sans-cœur** : pas de soins provenant d'autres alliés | source du heal et bénéficiaire | Eniripsa soigne un allié : plan valide pour survivre, illégal pour le challenge |
| Symétrie/placement | **Miroir** : quand un allié achève un ennemi, un autre allié doit être symétrique derrière la cible | cellule tueur, cible, troisième acteur, géométrie validée | tuer avant la mise en place du second combattant |
| Éléments et ordre de kill | Défis liés à élément, cible et ordre | historique de frappe et mort avec actionId/casterId | replanifier sur adversaire tué par dégâts indirects |

Le guide mentionne aussi **La diagonale du plein** : proche de De la même lignée, mais relation en diagonale. Le rapport M07 donne les sources, limites de comptage et tutoriels déjà transcrits.

## Exemple T1 et T2 : Duel et Infatigable en duo

Hypothèse de fixture : A=Crâ, B=Enutrof, ennemis X/Y, chacun **12 PA au début de son tour** (valeur artificielle de test). A inflige des dégâts à X ; l'historique enregistre `X→A`. B dispose d'une action sur X mais doit choisir Y pour conserver Duel. Sur Y, il peut tuer après avoir dépensé 8 PA ; il lui reste 4 PA, inférieur à la moitié de 12, donc l'attaque fatale violerait Infatigable **si ces challenges coexistent**. Un sort à 4 PA quand B a encore 8 PA laisserait 4 et n'aiderait pas non plus ; le plan doit examiner l'ordre précis et les ressources restantes. La compatibilité réelle des deux challenges simultanés doit être obtenue par l'interface du combat, pas supposée.

### Cas proposés, non exécutés

- **P01 Duel** : A touche X, B touche Y, jamais le même ennemi ; l'historique respecte l'unicité de l'attaquant par cible.
- **N01 Duel** : A touche X, un compagnon C contrôlé par le même compte touche X ; **attendu à vérifier** : si C est combattant distinct pour la règle, challenge raté. Ne pas convertir un doute en verdict.
- **P02 Lignée** : A finit son tour dans une ligne avec B, bien que C soit ailleurs ; règle « au moins un » satisfaite selon texte ancien.
- **N02 Lignée** : A est le seul sur sa ligne à la fin de son tour ; l'alignement ultérieur par B ne répare pas rétroactivement l'échec si la condition est immédiate.
- **N03 Infatigable** : tueur à 12 PA initiaux, 5 PA à la mort ; moitié arrondie inférieure = 6 PA, résultat incompatible selon la définition ancienne.
- **N04 Sans-cœur** : un soin allié autorisé tactiquement est interdit par la condition publiée ; préférer auto-soin ou protection selon la formulation.
- **N05 Miroir** : effet de kill à distance résolu avant le placement du second allié derrière la cible ; échoue si la position finale ne correspond pas.

## À propos des vidéos

[M07](../M07/RAPPORT.md) dispose du guide Ouatdefok 2024 et d'une vidéo de refonte 2023 **réellement transcrits** pour des principes généraux. La vidéo ne montre pas une validation de ces cas particuliers. Aucun nouveau transcript ni image pour M29 ; ne pas attribuer de parole horodatée imaginaire.

## Conséquence pour toutes les classes, équipes et compagnons

Les challenges s'appliquent aux actions **selon une identité** : lancer un sort, infliger des dégâts, soigner, tuer, terminer un tour. Le planificateur doit savoir si une invocation contrôlée ou un compagnon entre dans le groupe des combattants alliés. Sans confirmation de la version cible, marquer `Unknown` et conserver l'objectif de succès sans l'affirmer satisfait.

