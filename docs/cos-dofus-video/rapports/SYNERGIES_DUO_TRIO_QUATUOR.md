# Compositions et interactions — solo, duos, trios, équipes de quatre et plus

Ce document sépare **recommandation de créateur** (`declare-video`), **architecture de team déduite** (`hypothese`) et **efficacité prouvée** (`valide-test` / `valide-live`, aucune à ce stade). Les noms de classes ne suffisent jamais : préciser sorts disponibles au niveau choisi, équipement, ordre d'initiative, restrictions du combat et version.

## Question préalable à toute composition

Un groupe efficace doit répondre à ces questions : **qui maintient la cible en position ? qui inflige les dégâts ? qui garde les alliés vivants ? qui annule les capacités dangereuses du monstre ? qui ouvre la ligne de vue ? qui bénéficie du boost ? qui gère l'échec ?** Dans un combat court à tuer une cible immédiatement, la priorité peut différer d'un combat long qui exige maintien de position et survie.

### Modèle d'une synergie reproductible

État `S_t = {plateau, combattants, initiative, PA/PM par combattant, PV, états, relances, invocations, marques, actions confirmées, objectifs, contraintes}`.

Pour chaque action prévue : `préconditions → demande → résultat serveur → transition observée → replanification éventuelle`. Les PA/PM ne se transfèrent pas entre acteurs. Un plan de groupe est **conditionnel** à la survie et au jeu effectif des alliés précédents ; un déplacement, une invocation morte ou une case bloquée peut l'invalider. Score utile : dégâts nets, tour de victoire, risque de mort, réussite des challenges, temps de préparation, stabilité en cas de refus et coût de calcul.

## Duos avec source horodatée

| Duo | Intention stratégique | Faiblesse / contre-exemple | Source et statut |
| --- | --- | --- | --- |
| Huppermage + Steamer | Contrôler les axes et les espaces avec tourelles et effets runiques ; préparation coordonnée | Un boss à éliminer immédiatement pénalise la préparation longue ; tourelle placée sur une mauvaise case | [Xawpiya 01:05–03:15](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=65s), `declare-video` |
| Huppermage + Sadida | Endurance, invocations, entrave et soutien progressif | Débordement au premier tour ou impératif de tuer une cible avant un seuil | [Xawpiya 03:15–05:15](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=195s), `declare-video` |
| Huppermage + Zobal | Boucliers, polyvalence distance/mêlée, alternance de tempo | Désenvoûtement, très faible fenêtre pour préparer, dépendance au maintien des états | [Xawpiya 05:15–07:05](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=315s), `declare-video` |
| Sram + Féca | Contrôle et entrave donnant du temps au réseau de pièges | Ennemis peu sensibles au retrait ou capables de contourner la zone, temps de mise en place | [HUZ 12:28](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=748s), `declare-video` |
| Sram + Roublard | Placement et immobilisation pour préparer une zone dangereuse puis y envoyer une cible | Détonation/activation avant la cible, bombe détruite, tour d'attente trop risqué | [HUZ 11:40–11:59](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=700s), `declare-video` |
| Sram + Xélor | Cible retenue à un emplacement exploitable pour des échanges/téléfrags | Cible déplacée, historique incomplet, ordre des tours défavorable | [HUZ 12:00–12:20](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=720s), `declare-video` |
| Forgelance + Pandawa | Placement des ennemis pour zones du Forgelance, avec alternative tank/dégâts selon niveau | Mauvais regroupement, zone qui met l'allié en danger, initiative inversée | [HUZ 16:05–16:40](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=965s), `declare-video` |

Autres duos à instruire depuis les sections HUZ et [catalogue Xawpiya](https://xawpiya.fr/videos/) : Crâ/Enutrof, Iop/Pandawa, Roublard/Pandawa, Éliotrope/Iop, Ouginak/Osamodas, Féca/Eniripsa, Sadida/Osamodas. **Ils sont ici des axes d'étude**, sans validation de combinaison précise.

## Trios : exemples de plans dérivés, à tester

| Trio candidat | Ordre fonctionnel à comparer | Hypothèse à falsifier | Échec et repli |
| --- | --- | --- | --- |
| Pandawa + Enutrof + Crâ | Placeur → entraveur → dégâts distance | Regrouper/isoler puis ralentir augmente les tirs effectivement possibles et diminue les attaques ennemies | Si une cible est impossible à déplacer, l'Enutrof privilégie contrôle direct ; le Crâ garde distance |
| Pandawa + Eniripsa + Iop | Placeur → soutien → finisseur | Un placement et un renforcement sécurisent l'approche et l'attaque décisive | Si Iop joue avant placeur, préférer une action indépendante, puis préparer le prochain tour |
| Huppermage + Steamer + Féca | Déploiement tourelle/runes → protection/contrôle | La protection compense le tour de préparation et réduit le risque de perte de structure | Si la tourelle est détruite, repasser au plan dégâts/éloignement |
| Sram + Roublard + Enutrof | Ralentir/isoler → préparer pièges et bombes → déplacer/activer | Le contrôle collectif prolonge la fenêtre de préparation | Si le monstre ne peut entrer dans la zone, éviter tout lancer présumé de dégâts différés |

Ces propositions sont **`hypothese`** : elles ne sont pas des combats documentés et n'imposent pas un ordre d'initiative fixe ; comparer les permutations `A-B-C`, `B-A-C` et la cible hors de portée.

## Quatuors recommandés dans un tutoriel réel

| Composition | Rôles et logique décrits | Moment | Statut |
| --- | --- | --- | --- |
| Pandawa + Enutrof + Crâ + Féca | Placement/tank, entrave, dégâts distance, protection et dégâts supplémentaires | [Xawpiya 04:25–05:05](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=265s) | `declare-video` |
| Pandawa + Eniripsa + Crâ + Féca | Version « confort » avec soin et soutien plutôt qu'entrave Enutrof | [Xawpiya 05:05–05:36](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=305s) | `declare-video` |
| Pandawa + Sadida + Enutrof + Féca | Jeu de zone, invocations et ralentissement, structure de contrôle | [Xawpiya 05:36–06:08](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=336s) | `declare-video` |
| Sram + Féca + Sadida + Huppermage | Alternative sans Pandawa, contrôle et polyvalence ; capacité de placement attribuée au Sram dans le commentaire | [Xawpiya 06:08–06:47](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=368s) | `declare-video` |

**Attention** : le créateur est une source de conseil et non une mesure comparative de DPS ou de réussite. L'appellation « meilleure » du titre n'est pas une vérité de simulation. Pour reproduire, enregistrer les quatre feuilles de personnage (niveau, voies/variantes, inventaire, initiative), la carte, les résistances et les objectifs.

### Exemple pas à pas : quatuor Pandawa/Enutrof/Crâ/Féca

1. **Photographie initiale** : ennemi E1 à `cE1`, E2 à `cE2`, Panda `cP`, Enu `cN`, Crâ `cC`, Féca `cF` ; zones d'obstacle connues, initiative `P→N→C→F` *supposée pour l'exercice*. PA/PM et sorts inconnus tant que le niveau et le profil ne sont pas fixés.
2. **P** : examiner si le déplacement de E1 sur une case `c'` est légal et avantage les deux prochains acteurs. Sur confirmation serveur seulement, actualiser sa case ; sur refus, recalculer depuis E1 inchangé.
3. **N** : choisir entrave ciblée uniquement si la réduction de mobilité évite un danger identifié. Enregistrer effet réel, durée et esquive ; aucune perte de PM présumée avant la réponse.
4. **C** : sélectionner la meilleure cible accessible selon ligne de vue, élément, résistances, ressources restantes et contraintes du challenge ; ne pas annoncer de dégâts sans simulation versionnée.
5. **F** : protéger l'allié effectivement menacé ou exploiter une zone accessible ; vérifier les effets différés et l'évolution des glyphes aux changements de tour.
6. **Tour ennemi** : simuler une réponse plausible, observer les changements et replanifier. Résultat attendu d'un bon modèle : aucune action n'est construite sur une case déplacée seulement « souhaitée ».

**Tests discriminants à écrire** : (a) le Panda place E1 et la ligne de tir du Crâ s'ouvre ; (b) le déplacement Panda est refusé, Crâ ne tire pas au travers du mur ; (c) Enu joue après Crâ et le plan n'anticipe pas le retrait PM ; (d) E1 devient invisible/sa position incertaine, donc aucune cible directe inventée ; (e) challenge interdisant une action pourtant rentable.

## Équipes de cinq à huit : rôle de la taille

Une équipe de cinq à huit ne se construit pas en ajoutant des dégâts sans limite. Mettre à jour le nombre réel d'ennemis, leurs PV/résistances, l'initiative individuelle, les contraintes de donjon et l'occupation des cellules. Les invocations et compagnons sont des acteurs distincts pour les interactions et les challenges. Prévoir des conflits de placement (deux placeurs veulent la même case), de ressources (boosts qui bénéficient seulement au prochain allié), d'effets (deux états se remplacent) et de tempo (préparations de durées incompatibles).

### Comparateur de compositions à renseigner

| Critère observable | Solo | Duo | Trio | Quatre | 5–8 |
| --- | --- | --- | --- | --- | --- |
| Prérequis de scénario | 1 acteur / 1 tour | 2 initiatives et ressources séparées | 3 ordres possibles et repli | 4 objectifs et rôles | Collisions, occupation, phases boss |
| Difficulté de coordination | Faible à forte selon classe | Dépendance partenaire | Synchronisation / complémentarité | Protection + dégâts + contrôle | Explosion combinatoire, besoin de recherche bornée |
| Métrique réelle | Réussite, tours, PV restants | Gain vs 2 actions seules | Gain marginal du 3e | Gain vs 2 duos | Gain marginal vs coût et risque |
| Donnée indispensable | Sorts/version | Confirmation inter-acteurs | Initiatives et états | Plan conditionnel d'équipe | Planification séquentielle et conflits |

**À implémenter une fois les règles prouvées** : un moteur de préconditions et transitions commun, un planificateur à budget borné, un tableau d'intentions par équipe et une revalidation systématique après chaque événement autoritaire.

