# Fiche mécanisme — Gourlo et ses tonneaux

**ID** M11-GOURLO-01 · cible DOFUS 3.7.13.17 à confirmer · source communautaire 3.7.12 Grimoire et fiches OpenDofusDB · **statut** `corrobore-source` pour la description de résistance et de mécanique, `hypothese` pour sa chronologie exacte sur client cible.

## Règles atomiques et preuves

| Règle | Énoncé | Preuve | Limite |
| --- | --- | --- | --- |
| M11-R01 | Gourlo a une résistance élevée partout (200 % selon bases citées) | [OpenDofusDB](https://openddb.dev/monsters/1051-gourlo-le-terrible), [Grimoire](https://dofus-grimoire.fr/boss/) | Niveau/rang et version exacte |
| M11-R02 | Un tonneau voisin de Gourlo à son tour réduit une résistance dans l'élément lié au tonneau, durée annoncée de 3 tours | [Grimoire](https://dofus-grimoire.fr/boss/), [Dofus pour les Noobs](https://www.dofuspourlesnoobs.com/les-succes-speciaux.html) | Déclencheur exact et valeur interne inconnus |
| M11-R03 | Le succès « Tonneaux truands » requiert que chaque ennemi ait subi au moins un effet de tonneau | [Dofuserie](https://www.dofuserie.com/donjons/cale-de-l-arche-d-otomai/), [Dofus pour les Noobs](https://www.dofuspourlesnoobs.com/les-succes-speciaux.html) | Attribution et conditions selon version à vérifier |
| M11-R04 | Le succès « Premier » demande de tuer Gourlo avant les autres ; peut être incompatible avec une autre trajectoire optimisée | [Dofuserie](https://www.dofuserie.com/donjons/cale-de-l-arche-d-otomai/) | Comportement de challenge et compatibilité à tester |

## État initial fictif, à transformer en replay

Boss E en case `cE`, tonneau T en `cT`, placeur P en `cP`, finisseur D en `cD`, obstacle B. Le boss a une résistance `rE[element]` observée, son état d'invulnérabilité « effective » restant à qualifier. L'ordre `P→T→D` sert à l'exercice et ne représente pas un ordre vu en vidéo.

## Séquence conditionnelle

1. P vise une case à distance de proximité autorisée de E, vérifie son sort, sa PO, les obstacles et sa propre survie.
2. Si le déplacement est **confirmé**, actualiser `cE`. Sinon ne jamais supposer la nouvelle case.
3. T joue ; observer un effet dont **T est l'auteur** et E la cible. Mettre à jour la résistance du bon élément et sa durée seulement après confirmation.
4. D sélectionne un sort de cet élément si la réduction est effective et la cible accessible. Évaluer la meilleure attaque selon le challenge actif.
5. Au tour suivant, réévaluer l'état ; ne pas déduire que l'effet persiste parce qu'il était actif au tour précédent.

## Scénario négatif

Après l'étape 1, E se déplace hors de la zone avant le tour de T, ou T est détruit. Le solveur doit annuler les attaques préconditionnées par la baisse de résistance et conserver l'état d'E effectivement connu.

## Combinaisons interclasses

- **Pandawa + attaquant à élément adapté** : piste de placement en vue du tour du tonneau ; risque si le boss ou T ne peuvent être déplacés.
- **Féca + placeur + attaquant** : piste de protection lors d'une fenêtre où l'équipe attend ; bonus seulement sur les membres menacés.
- **Duo sans placeur** : peut dépendre de la mobilité naturelle d'E et de T ; ne pas annoncer impossible ou obligatoire sans replay.

**Aucune validation live ni capture vidéo.** Données de combat exactes à produire avant toute implémentation.

