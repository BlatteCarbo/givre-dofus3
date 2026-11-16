# Atlas des 19 classes — catalogue couvert, vérification empirique ouverte

**Périmètre** : les 19 classes de DOFUS 3, selon [OpenDofusDB](https://openddb.dev/) et [Dofus Grimoire 3.7.12](https://dofus-grimoire.fr/classes/). Version cible technique à contrôler avec les données locales : client 3.7.13.17 mentionné dans le code. Une classe peut être indexée sans que son sort et ses valeurs soient étudiés ; ce document rend cette différence visible.

| Classe / fiche | Rôle à explorer | Question à prouver | Maturité au 04/11 |
| --- | --- | --- | --- |
| [Crâ](classes/cra.md) | Tir à distance, contrôle de portée, placement et dégâts de zone | Comparer les cases atteignables à portée effective et le coût de conserver une distance de sécurité. | [M13](M13/RAPPORT.md) : source 3.7 et tutos |
| [Ecaflip](classes/ecaflip.md) | Dégâts adaptatifs, aléas et soutien conditionnel | Suivre les états, probabilités ou tirages de sorts qui changent le plan légal et la variance de résultat. | [M17](M17/RAPPORT.md) : fiche 3.7.12, vidéo spécifique à obtenir |
| [Éliotrope](classes/eliotrope.md) | Réseau de portails, projection géométrique, repositionnement | Prédire le chemin réel de redirection et la sortie selon portails disponibles, distance et règles de départage. | [M04](M04/RAPPORT.md) : 320 segments tutoriel |
| [Eniripsa](classes/eniripsa.md) | Soins, bonus, soutien et dégâts d'appoint | Faire correspondre chaque sort au bénéficiaire effectif et à l'attaque/au dommage futur qu'il modifie. | [M15](M15/RAPPORT.md) : enquête livrée, vidéo dédiée bloquée |
| [Enutrof](classes/enutrof.md) | Entrave, contrôle des déplacements, utilité et pression à distance | Évaluer les effets de retrait à partir de leur résultat réel, des capacités d'esquive et des tours des ennemis. | [M16](M16/RAPPORT.md) : enquête livrée, vidéo dédiée bloquée |
| [Féca](classes/feca.md) | Protection, glyphes, altérations et maîtrise de zone | Modéliser séparément création, activation, traversée, début/fin de tour et expiration d'un glyphe. | [M09](M09/RAPPORT.md) : glyphes et événements |
| [Forgelance](classes/forgelance.md) | Zones d'attaque, lance et placement dynamique | Suivre la présence et la position de la lance ainsi que les zones de sorts rattachées à son état. | [M24](M24/RAPPORT.md) : Lance, états, zones et cas négatifs, guide 3.7.12 |
| [Huppermage](classes/huppermage.md) | Interactions élémentaires, runes et états composites | Formaliser la succession des éléments et effets sans déduire une fusion à partir du seul nom du sort. | [M19](M19/RAPPORT.md) : six états/couples, 3.7.12 |
| [Iop](classes/iop.md) | Dégâts concentrés, préparation et mobilité offensive | Distinguer bonus préparatoires, fenêtres d'attaque, accès mêlée et risque d'exposition après dégâts. | [M14](M14/RAPPORT.md) : Colère et boost |
| [Osamodas](classes/osamodas.md) | Invocations, contrôle des alliés et effets indirects | Traiter chaque invocation comme une entité distincte avec propriétaire, IA, durée, tour et limites propres. | [M18](M18/RAPPORT.md) : invocations 3.7.12 |
| [Ouginak](classes/ouginak.md) | Traque de cible, mobilité et pression rapprochée | Suivre la désignation de proie, les états spécifiques et l'occupation des cases autour de la cible. | [M22](M22/RAPPORT.md) : Proie, Rage, duo HUZ |
| [Pandawa](classes/pandawa.md) | Placement, portage, protection et création de zones | Gérer précisément les états porté/porteur, la cible du lancer, les cases de dépose et la confirmation d'échange. | [M23](M23/RAPPORT.md) : portage/jet/Sobre-Saoul ; [M08](M08/RAPPORT.md) quatuor |
| [Roublard](classes/roublard.md) | Bombes, murs, charges et dégâts différés | Simuler la vie des bombes, leur position, les liens du mur, l'âge/état et les déclenchements sur plusieurs tours. | [M02](M02/RAPPORT.md) : tutoriels et mur |
| [Sacrieur](classes/sacrieur.md) | Attirance, permutation, exposition et dommages rapprochés | Suivre PV réels, modifications temporaires des dégâts, déplacements forcés, positions alliées et valeur de survie. | [M05](M05/RAPPORT.md) : placement et attaque |
| [Sadida](classes/sadida.md) | Entrave, arbres et poupées, propagation conditionnelle | Séparer marques de propagation, états sur cibles, invocations/arbres vivants et événements de déclenchement. | [M21](M21/RAPPORT.md) : Infection 3.7.12 |
| [Sram](classes/sram.md) | Pièges, invisibilité, déplacements forcés et dégâts déclenchés | Gérer pose, activation, déclenchement et retrait séparément ; position invisible et cible présumée restent incertaines. | [M01](M01/RAPPORT.md) : réseau et vidéos |
| [Steamer](classes/steamer.md) | Tourelles, évolutions, couloirs de tir et soutien | Modéliser une tourelle comme entité durable, avec mode, niveau/évolution, cible et portée propres. | [M20](M20/RAPPORT.md) : tourelles 3.7.12 |
| [Xélor](classes/xelor.md) | Téléfrags, échanges de position et séquences de déplacements | Suivre positions d'origine, tentatives, swaps réellement effectués, états créés/consommés et événements de téléfrag. | [M03](M03/RAPPORT.md) : Téléfrag et vidéos |
| [Zobal](classes/zobal.md) | Masques, boucliers et alternance d'offense/défense | Suivre les états de masque, leur exclusivité éventuelle et les cibles couvertes par les boucliers. | [M12](M12/RAPPORT.md) : masques, Plastron |

## Lecture du catalogue

Chaque fiche comporte : spécificité, variables minimales pour un simulateur, stratégie solo, duo proposé, trio, quatuor, micro-séquence hypothétique, test positif et négatif, interactions communes et sources. Les propositions servent à décider **quoi lire et vérifier** ; aucune n'est un contrat de comportement prêt pour l'IA de production.

## Programme de complétion par classe

1. Lister les sorts disponibles au niveau choisi (variante, rang, PA, PO, zones, restrictions, relances) depuis une source versionnée.
2. Identifier 1–3 tutoriels pertinents de DOFUS 3, récupérer infos, langues, transcription complète, puis images aux moments où la géométrie est décisive.
3. Isoler au moins **deux mécaniques de classe indépendantes** et, par mécanique, une règle atomique avec lien à l'instant et source recoupée.
4. Reconstituer un tour avec état initial et transitions; contraster une variante qui échoue malgré des dégâts supposés identiques.
5. Décrire une ligne solo, un duo, un trio, un quatuor et leur dépendance à l'initiative ; comparer les bénéfices à des actions indépendantes.
6. Identifier un ennemi/un boss qui change fondamentalement la décision et un challenge qui invalide un combo.
7. Relever le point d'intégration dans les réducteurs/planificateurs, tester un replay hors ligne, puis demander une validation en environnement réel séparée.

## Schéma de classification des rôles

Les rôles ne sont **pas des étiquettes exclusives**. Un acteur peut contrôler, protéger et frapper avec des coûts différents ; on évalue son utilité pour le prochain tour. Construire un graphe **acteur → transition → bénéficiaire** :
- `placement → cible dans zone → dégâts supplémentaires`
- `retrait PM réel → attaque ennemie inaccessible → PV sauvegardés`
- `protection → allié survit → lancement décisif au tour suivant`
- `état temporaire → sort conditionnel légal → gain net`
- `invocation → blocage de cellule / attaque / soin → score net après exposition`

Pour chaque arc, conserver la règle avec provenance et les conditions où elle cesse d'être vraie.

## Missions détaillées et niveaux de preuve

Les **19 classes** disposent maintenant d'une fiche de cadrage et d'au moins **une mission mécanique dédiée** parmi M01–M24. Les missions M25–M31 explorent les boss/challenges ; M32/M33 comparent les formats d'équipe. L'existence d'un dossier ne certifie ni la liste de tous les sorts ni leur effet dans le client 3.7.13.17. Pour accéder aux tests proposés et aux limites de preuve, lire le `RAPPORT.md` puis `FICHE_*.md` et `PASSAGE_DEVELOPPEUR.md`. Le statut précis est dans [SYNTHESE.md](SYNTHESE.md) et les situations pratiques sont dans [ATELIER_19_CLASSES_CAS.md](ATELIER_19_CLASSES_CAS.md).

