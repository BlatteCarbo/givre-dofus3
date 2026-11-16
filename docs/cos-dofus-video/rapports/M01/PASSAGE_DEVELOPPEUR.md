# M01 — Passage au développeur : réseau Sram Terre

**Statut : spécification documentaire uniquement, 04/11/2026.** Aucun code, test, combat, marque ou message serveur n'a été modifié, exécuté ou observé ici. Version cible de référence : **DOFUS 3.7.13.17** ; les données de sort détaillées utilisées sont issues d'une fiche **3.7.12**. La [fiche détaillée](FICHE_SRAM.md) et le [rapport de provenance](RAPPORT.md) conservent les limites.

## Les trois règles à intégrer en premier

| Priorité / ID | Règle | Preuve exacte | Condition de promotion |
| --- | --- | --- | --- |
| **1 — M01-R01/R04** | Une **transition de position confirmée** dans la zone d'une marque la rend éligible, même après une poussée ; plusieurs zones peuvent couvrir la même case | [Shurikn 04:08–05:32](https://www.youtube.com/watch?v=2aFjInvshkE&t=248s), transcript FR automatique, **declare-video** ; [Grimoire Sram 3.7.12, « Un piège, c'est une zone »](https://dofus-grimoire.fr/classes/sram/), **corrobore-source** | Confirmer sur une fixture 3.7.13.17 entrée à pied / forcée et liste de marques réellement activées |
| **2 — M01-R02/R06** | Conserver l'**ordre de pose** et gérer les effets sélectionnés simultanément / en attente ; un déplacement peut déclencher une nouvelle marque avant la fin de l'ensemble courant | [Pchen 01:01–03:27](https://www.youtube.com/watch?v=hQExxOlRS-o&t=61s), **declare-video** ; [GOATESQUE 07:18–07:56](https://www.youtube.com/watch?v=sGhxghCtkhY&t=438s) (ancienne version) ; [Grimoire, « mise en attente »](https://dofus-grimoire.fr/classes/sram/), **corrobore-source** | Capturer l'ordre exact sur deux zones concurrentes et vérifier si l'effet différé s'applique depuis la position courante |
| **3 — M01-R03/R05** | Le déplacement du **Répulsif (2 cases)** doit être découpé par étapes : tout piège rencontré peut **interrompre** l'impulsion et générer de nouveaux dégâts, notamment Mortel Terre | [Pchen 08:57–10:08](https://www.youtube.com/watch?v=hQExxOlRS-o&t=537s), [GOATESQUE 08:32–10:49](https://www.youtube.com/watch?v=sGhxghCtkhY&t=512s), **declare-video** ; [Grimoire, Pièges Répulsif/Mortel 3.7.12](https://dofus-grimoire.fr/classes/sram/), **corrobore-source** | Fixture libre, fixture avec piège intermédiaire et obstacle, comparaison à un replay serveur |

## Entrées requises du simulateur

- **Combat :** ID, session/époque, version du jeu, ordre de tours, acteurs et équipe ; positions et états confirmés ; obstacles, occupations, ligne de vue, accès aux cases ; PA/PM/PO et effets d'entrave.
- **Marque :** ID serveur si disponible, propriétaire, type et version du sort, rang/variante, cellule centrale, géométrie d'activation et d'effet, priorité/ordre de pose, durée, état de vie et consommation ; ne pas confondre zone de pose et zone d'effet.
- **Déplacement :** acteur source, entité déplacée, direction, distance demandée, chemin confirmé case par case si fourni, origine (marche, Peur, attraction, autre), résistances et interactions avec obstacles ; absence d'une confirmation = **Unknown**.
- **Effets :** événement de marque activée, cible(s), déplacement, dommages par cible, retrait de marque ; aucune inférence d'un serveur ACK vers un dégât ou une destruction.

### Modèle d'état proposé

1. **Pose demandée** : aucune marque n'est ajoutée sur simple requête. **Pose confirmée** : allouer une identité stable, ordre monotone par combat/époque, case, zone, propriétaire. **Refus / Unknown** : préserver la distinction ; sans confirmation, ne pas déclarer la marque active.
2. **Entrée confirmée** sur une cellule : calculer les marques éligibles. En cas de recouvrement, conserver **l'ordre de pose** et les actions en attente ; la priorité du serveur reste à établir pour 3.7.13.17.
3. **Résoudre une marque** : effets immédiats, puis chaque déplacement réellement produit ; à chaque case effectivement traversée, examiner les nouvelles activations. L'arrivée sur un piège intermédiaire peut suspendre/couper l'impulsion en cours.
4. **Consommation / disparition** : seule la notification de retrait ou une autre preuve autoritative permet de passer l'état à retiré ; conserver le lien entre l'activation et la marque, et éviter double-comptage sur rafraîchissements. Une disparition dans l'UI ne prouve pas son ordre exact par rapport aux dommages.
5. **Interruption / échec** : obstacle, absence de déplacement, mort, refus d'action, nouvel epoch ou données inconnues invalidant la trajectoire ; arrêter le plan optimiste, recalculer à partir du dernier état confirmé.

Ne pas coder de formule de dégâts Terre/Air à partir des chiffres des vidéos. Ne pas inventer d'identifiants de sorts, de paquets, de cast, de marks ni de temps de résolution. La présence d'un sort « Sram » dans un profil générique ne suffit pas à représenter les réseaux.

## Fixtures discriminantes et observations attendues

**Positif P01 — « Le Doublé », terrain libre.** Préparer M1 Mortel en E=(0,0), R Répulsif en (-1,0) (zone en croix comprenant E), M2 Mortel en (2,0), dans cet ordre. Cible C en (0,-1) ; mouvement **confirmé** d'une case vers E. Aucun obstacle en (1,0) et (2,0). **Prédiction documentaire :** M1 et R sont éligibles sur E, R peut amener C en (2,0), puis M2 est éligible ; deux applications Terre distinctes et effet Air de R à vérifier en ordre exact. **Oracle :** positions réelles, transitions, dégâts, ID et retraits de chaque marque dans le replay. Les 8 PA de pose (3+2+3) sont ceux de [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/sram/) et n'incluent pas la poussée d'entrée. **Statut : propose, non exécuté.**

**Négatif N01 — obstacle.** Reprendre P01 mais rendre (1,0) bloquante. Après entrée E, ne jamais inventer l'arrivée en (2,0) ; M2 ne doit pas être déclaré déclenché sans preuve d'entrée dans sa case. Vérifier séparément la présence d'un éventuel effet de collision et le retrait de R. **Statut : propose, non exécuté.**

**Discriminant D01 — inverser la pose.** R avant M1, même zones et déplacement. Capturer l'ordre de résolution, les effets différés et les positions au moment des dommages : les références prévoient une influence de l'ordre, mais **aucune sortie détaillée n'est déclarée validée**. **Statut : propose, non exécuté.**

**Discriminant D02 — interrompre par piège.** Remplacer la cellule libre (1,0) par un troisième Mortel posé légalement sur un autre tour si nécessaire. Vérifier si ce piège suspend la poussée et quel événement éventuel reprend ensuite. **Statut : propose, non exécuté.**

## Risques d'implémentation concrets

- Le moteur pourrait **additionner** toutes les impulsions à partir d'E et faire « téléporter » C directement à (2,0) malgré un piège actif en (1,0). N01 et D02 distinguent ce bug.
- Un tri par ID, par cellule ou par type de piège pourrait remplacer par erreur l'**ordre de pose**. D01 le détecte.
- Retirer la marque **avant** confirmation de résolution ou créditer un piège sur simple ACK ferait perdre une action encore incertaine et produirait de faux dégâts. Journaliser activation, résultat et retrait indépendamment.
- Réutiliser les coûts/limitations de 2025 sous 3.7 peut refuser un tour pourtant permis : [Back from AFK](https://backfromafk.com/classes/sram/depuis-3-0/) signale notamment le passage du Répulsif de 1 à 2 lancers par tour entre 3.0 et 3.7.
- Les pièges pouvant toucher/repousser aussi des **alliés**, la simulation doit représenter toutes les entités dans les zones ; leur effet précis est indiqué par [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/sram/) mais demande un test multi-entité versionné.

## Inconnues bloquantes et prochaines preuves

1. **Priorité du serveur** lorsqu'une entrée active plusieurs zones, éventuel report d'effet et position d'application des dommages. Capturer une séquence A1–A5 complète avec trames anonymisées / replay 3.7.13.17.
2. **Moment du retrait** de M1/R/M2, instant du calcul des dégâts Air du Répulsif, interactions avec obstacle, immobilisation, disparition de cible ou changement d'epoch. Ne pas affirmer ces timings à partir de l'animation.
3. **IDs et types protocolaires** des poses/marks/impulsions : lire le dictionnaire protocolaire de la version cible et faire correspondre uniquement les identifiants réellement vus.
4. **Preconditions exactes** : niveau, variantes de sorts, ligne/portée de la pose, cumul par tour, identité des cibles, ordre d'initiative ; le schéma relatif doit être instancié sur une vraie grille avant exécution.
5. **Preuve visuelle** : inspecter V1 [08:57–10:08](https://www.youtube.com/watch?v=hQExxOlRS-o&t=537s), puis V2 [04:08–05:32](https://www.youtube.com/watch?v=2aFjInvshkE&t=248s) et confronter les trajectoires visibles aux déclarations. Aucun de ces passages n'a été inspecté en images dans cette mission.

**Ordre de validation conseillé :** tests offline à oracle enregistré sur P01/N01, puis D01/D02, puis exécution encadrée sur fixture versionnée si autorisée séparément. À ce jour tous les cas sont **propose, non exécuté** ; aucun statut valide-test ni valide-live n'a été attribué.
