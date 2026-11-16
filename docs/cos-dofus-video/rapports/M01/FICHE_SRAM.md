# Fiche M01-SRAM-RESEAU-TERRE-001 — Mortel → Répulsif → Mortel

## Identité

- **Identifiant stable :** M01-SRAM-RESEAU-TERRE-001.
- **Question :** comment l'entrée forcée sur deux zones de pièges déclenche les dégâts Terre et une impulsion, puis un second piège ; comment un obstacle coupe la chaîne ?
- **Personnages :** un Sram orienté Terre et une cible ennemie ; option d'un placeur allié ; aucun compagnon requis pour le fixture minimal.
- **Jeu :** DOFUS Unity, V1/V2 de 2025 titrées 3.0 / DOFUS 3 ; V3 de 2023 historiquement ancienne. Sources de sort 3.7.12 ; **version cible 3.7.13.17**. La compatibilité moteur exacte entre les versions **n'est pas validée**.
- **Analyse :** recherche documentaire du 04/11/2026.
- **Vidéos :** [V1 Pchen, 26/05/2025, 12 min](https://www.youtube.com/watch?v=hQExxOlRS-o) ; [V2 Shurikn, 14/01/2025, 22 min](https://www.youtube.com/watch?v=2aFjInvshkE) ; [V3 GOATESQUE, 09/01/2023, 32 min](https://www.youtube.com/watch?v=sGhxghCtkhY). Métadonnées retournées par outil MCP.
- **Transcriptions :** sous-titres **français automatiques**, horodatés ; V1 00:28–03:29 et 07:55–10:08 ; V2 04:08–05:32 ; V3 06:00–06:54. Les 3 outils de transcription renvoient un curseur final nul. Noms de sorts parfois mal transcrits.
- **Méthodes :** interrogation MCP métadonnées/langues/transcriptions ; recoupement Grimoire 3.7.12, Back from AFK 3.7 et article 3.7 de Dofus Pour Les Noobs. **Aucune image de vidéo inspectée** et aucune observation en combat.

## Règles atomiques et preuves

| ID | Affirmation précise | Statut | Provenance / limite |
| --- | --- | --- | --- |
| M01-R01 | Entrer par déplacement forcé dans une zone déclenche potentiellement le piège | declare-video, corrobore-source | [V2 04:08–05:32](https://www.youtube.com/watch?v=2aFjInvshkE&t=248s), [Grimoire Sram](https://dofus-grimoire.fr/classes/sram/) |
| M01-R02 | L'ordre de pose influence les impulsions sur zone commune | declare-video, corrobore-source | [V1 01:01–03:27](https://www.youtube.com/watch?v=hQExxOlRS-o&t=61s), [V3 07:18–07:56](https://www.youtube.com/watch?v=sGhxghCtkhY&t=438s), [Grimoire](https://dofus-grimoire.fr/classes/sram/) |
| M01-R03 | Le Répulsif pousse de deux cases, mais une entrée de piège intermédiaire peut interrompre ce trajet | declare-video, corrobore-source | [V1 08:57–10:08](https://www.youtube.com/watch?v=hQExxOlRS-o&t=537s), [V3 08:32–10:49](https://www.youtube.com/watch?v=sGhxghCtkhY&t=512s) ; valeurs 3.7.12 [Grimoire](https://dofus-grimoire.fr/classes/sram/) |
| M01-R04 | Les zones se chevauchent bien que les centres de marques occupent des cases distinctes | corrobore-source | [Grimoire, réseaux](https://dofus-grimoire.fr/classes/sram/) |
| M01-R05 | Le Mortel déclenché occasionne des dégâts Terre | declare-video, corrobore-source | [V2 04:08–04:34](https://www.youtube.com/watch?v=2aFjInvshkE&t=248s), [Grimoire, Mortel](https://dofus-grimoire.fr/classes/sram/) |
| M01-R06 | Une file ordonnée doit gérer des effets activés simultanément et éventuellement différés | corrobore-source ; hypothese sur la règle exacte du moteur | [Grimoire, « mise en attente »](https://dofus-grimoire.fr/classes/sram/) ; en attente d'un replay serveur |
| M01-R08 | Le piège finit consommé ; l'instant précis de retrait par rapport aux dégâts et déplacements n'est pas connu | hypothese sur l'ordre de retrait | [Grimoire, réseaux](https://dofus-grimoire.fr/classes/sram/) ; absence de journal d'événements |

## État initial reproductible (fixture de simulation proposé)

Toutes les coordonnées ci-dessous sont **relatives et artificielles**. Plan orthogonal X vers la droite, Y vers le bas. Les identifiants M1, R et M2 sont internes au fixture et **ne sont pas des IDs de sorts ou de marques du client**.

| Objet | Centre / position | Zone de déclenchement considérée | Rôle |
| --- | --- | --- | --- |
| M1 : Piège Mortel | E = (0,0) | cellule (0,0) | dégât Terre d'entrée |
| R : Piège Répulsif | (-1,0) | croix à distance orthogonale 1, comprenant E | poussée de E vers +X |
| M2 : Piège Mortel | (2,0) | cellule (2,0) | dégât Terre sur destination |
| Cible C | (0,-1) | aucune au départ | cible à forcer sur E |
| Sram / placeur | position calculée pour pose légale et poussée de C vers +Y | doit respecter portée, ligne, cellules libres et risque d'exposition | acteur des sorts |

**Ordre de pose :** 1. M1 sur E ; 2. R sur (-1,0) ; 3. M2 sur (2,0). Coût de référence **3 + 2 + 3 = 8 PA** selon [Grimoire, version 3.7.12](https://dofus-grimoire.fr/classes/sram/). Ces coûts n'incluent pas la poussée d'entrée (Peur ou allié), et les deux Mortels exigent une même ligne de lancer ou un repositionnement légal. Les références 3.7.12 indiquent 2 lancers de Mortel et 2 de Répulsif par tour ; **les cast et marque IDs, positions exactes du Sram, niveaux/rangs et durée restent à extraire du client cible**.

**Préconditions communes :** cible non morte, position C connue, E/(1,0)/(2,0) libres avant exécution, aucune marque parasite dans le trajet, déplacement d'entrée déjà légalement confirmé. Distance/PO, variantes équipées, immunités, résistance, états de déplacement, initiative, durée des marques, présence d'alliés et montants de PV doivent être renseignés pour transformer cet exercice en test réel. Aucune formule de dégâts n'est figée.

## Séquence et transitions (hypothèse testable)

| Étape | Acteur | Action / cible | Précondition et coût | Effet immédiat / attendu | Effet différé / ordre | Preuve |
| --- | --- | --- | --- | --- | --- | --- |
| P1 | Sram | Poser M1 sur E | Sort Mortel disponible, cible en ligne, case libre ; **3 PA** version 3.7.12 | événement de pose confirmé ; marque M1 armée | aucune attaque immédiate | [Grimoire, Mortel](https://dofus-grimoire.fr/classes/sram/) |
| P2 | Sram | Poser R sur (-1,0) | Répulsif disponible et case de pose libre ; **2 PA** version 3.7.12 | marque R armée, zone croix couvre E | numéro de pose de R supérieur à M1 | [Grimoire, Répulsif](https://dofus-grimoire.fr/classes/sram/) |
| P3 | Sram | Poser M2 sur (2,0) | 2e Mortel autorisé et cast en ligne ; **3 PA** version 3.7.12 | marque M2 armée | aucune attaque immédiate | [Grimoire, « Le Doublé »](https://dofus-grimoire.fr/classes/sram/) |
| A1 | Placeur | Amener C de (0,-1) sur E, direction +Y | capacité / coût du placeur **inconnus** : l'entrée est une injection événementielle dans le fixture | C arrive E ; M1 et R sont éligibles | activer l'ensemble selon le moteur versionné | [V2 04:28–05:32](https://www.youtube.com/watch?v=2aFjInvshkE&t=268s) |
| A2 | Moteur | Résoudre M1 | M1 éligible ; son ordre de pose précède R | dégâts Terre de M1 ; montant inconnu | retrait de M1 **à positionner par preuve** | [Grimoire, « Le Doublé »](https://dofus-grimoire.fr/classes/sram/) |
| A3 | Moteur | Résoudre R | R éligible, C en E, trajet libre | déplacement forcé de C vers (1,0) puis (2,0) sous réserve d'absence d'arrêt | à (2,0) M2 devient éligible ; dégâts Air de R et leur timing exact à vérifier | [V1 01:01–03:27](https://www.youtube.com/watch?v=hQExxOlRS-o&t=61s), [Grimoire, Répulsif](https://dofus-grimoire.fr/classes/sram/) |
| A4 | Moteur | Résoudre M2 | arrivée effective sur (2,0) confirmée | dégâts Terre M2 ; montant inconnu | retrait de M2 **à positionner par preuve** | [Grimoire, « Le Doublé »](https://dofus-grimoire.fr/classes/sram/) |
| A5 | Moteur | Clore la chaîne | tous événements confirmés et file vide | état spatial observé : C = (2,0) **si aucune interruption** | états des marques M1/R/M2 contrôlés via messages réels | hypothese de fixture, non valide-test |

Le moment exact où R inflige ses dégâts Air et où chaque marque est supprimée **n'est pas fixé** par cette séquence pédagogique. Il faut utiliser l'oracle d'événements pour départager « pousse puis frappe », « frappe puis pousse » et effets différés. Pour tout événement serveur inconnu, conserver l'état **Unknown** plutôt que d'acter une position ou une destruction fictive.

### Machine à états recommandée (spécification provisoire)

**Marque individuelle :** Absent → PoseDemandee → Armee (après confirmation effective) → Eligible (entrée dans zone) → ResolutionEnCours / EnAttente → Consommee (retrait confirmé). Possibilités additionnelles : PoseRefusee (pas de marque) et Unknown (absence ou contradiction de confirmation). Les états « Eligible », « ResolutionEnCours » et « EnAttente » sont des concepts d'analyse, pas des types de paquets établis. Identité de marque = combat/session/époque + serveur/owner + identifiant serveur de marque si existant ; ne pas dédupliquer sur la seule cellule.

**Chaîne :** après chaque déplacement **confirmé**, chercher les marques encore pertinentes, conserver l'ordre de pose, empiler/ordonner les déclenchements concurrents, appliquer un nouvel événement de déplacement, interrompre si un obstacle/une autre marque coupe l'impulsion, puis reprendre les effets en attente selon les preuves. Ne pas compter deux fois la même marque ; ne pas supprimer rétroactivement une marque dont le retrait n'est pas confirmé.

## Intérêt tactique et limites

La préparation (8 PA de pose dans l'exemple, plus un placement à payer sur le même tour ou le suivant) peut faire traverser **deux sources de dégâts Terre** à une seule cible, éventuellement avec dégâts Air du Répulsif. Le bénéficiaire est le Sram/équipe par concentration des dégâts ; il n'y a aucun boost de caractéristiques implicite. L'allié qui place peut faciliter la chaîne, mais son ordre d'initiative et ses PA restent séparés. Il faut comparer à l'attaque directe, à la prise de risque, à la possibilité que le monstre contourne le réseau et aux contraintes de challenges.

Les vidéos mentionnent Concentration de Chakra et des variantes ; **aucun bonus de ce sort n'est ajouté au fixture**. Les montants affichés en vidéo dépendent du stuff, des résistances et de buffs ; il serait erroné de les transformer en formule générale. Le placement par Méprise avec Double/Comploteur nécessite un second fixture : [V2 08:10–10:40](https://www.youtube.com/watch?v=2aFjInvshkE&t=490s).

## Scénarios de test proposés (tous non exécutés)

| Cas | Entrées et actions | Résultat observable attendu / oracle | Règle distinguée | Statut |
| --- | --- | --- | --- | --- |
| **POSITIF P01** | M1 en (0,0), R en (-1,0), M2 en (2,0) dans cet ordre ; C arrive de (0,-1) vers E ; (1,0) et (2,0) libres | Après entrée confirmée : activation de M1/R, déplacement vers (2,0) et entrée sur M2 ; deux occurrences de dommage Terre distinctes **si** combat permet de survivre ; nombre/ordre des suppressions à capturer | M01-R01/R02/R03/R05 | **propose, non exécuté**, artefact absent |
| **NÉGATIF N01 — obstacle** | Même fixture, mais obstacle infranchissable en (1,0) ; forcer entrée E | C ne traverse pas l'obstacle et n'entre pas sur (2,0) ; M2 ne doit pas être crédité du déclenchement ; état exact de R et dégâts collision à relever | M01-R03/R09 ; interdit de sauter le chemin | **propose, non exécuté**, artefact absent |
| **NÉGATIF N02 — mauvaise entrée** | Même pièges, mais pousser C vers (0,1) sans traverser E | Pas d'activation du Mortel M1 situé sur E ; ne pas déclencher toute la chaîne par simple proximité | M01-R01/R04 | **propose, non exécuté**, artefact absent |
| **DISCRIMINANT D01 — ordre inverse** | R posé avant M1, même positions, même entrée ; contrôler tous les événements | Ne **pas** préjuger du résultat : comparer priorité, destination, événements en attente, dégâts et suppressions avec P01 | M01-R02/R06 | **propose, non exécuté**, artefact absent |
| **DISCRIMINANT D02 — coupure par marque** | Ajouter un Mortel intermédiaire en (1,0) au trajet de R, budget/temps ajustés ; M2 reste en (2,0) | Relever que l'entrée en (1,0) entraîne un événement de piège et peut interrompre le déplacement vers (2,0) ; décider ensuite par replay si R reprend ou non | M01-R03/R06 | **propose, non exécuté**, artefact absent |
| **DISCRIMINANT D03 — cible meurt** | PV de C juste sous dommage de M1 | Ne pas injecter artificiellement le déplacement R ni des dégâts M2 après confirmation de mort ; vérifier si les autres marques ont été consommées côté serveur | M01-R06/R08 | **propose, non exécuté**, artefact absent |

Le statut « négatif » vise un réseau manqué, pas une preuve que le serveur refuse le lancer. Un **test local simulé** ne pourra être marqué valide-test qu'une fois réellement exécuté, avec journal, version et assertions. La **validation en jeu** doit rester distincte.

## Passage au développeur

Construire un état de combat possédant marques identifiées, ordre de pose, position par entité, géométrie/obstacles, zones, objets de déplacement en cours et file d'effets. Consommer les événements serveur pour arbitrer la vérité. Le simulateur peut énumérer un ensemble de trajectoires candidates et rejeter celles où la case d'arrivée n'est pas atteinte ; il **ne peut pas affirmer** la chronologie exacte des dégâts/retraits ni les formules de cette version sans preuve complémentaire. Priorité : P01/N01, puis D01/D02 ; le placement Méprise/Double fera une fiche séparée après vérification.

## Reprise

Derniers passages exploités : V1 [10:08](https://www.youtube.com/watch?v=hQExxOlRS-o&t=608s), V2 [11:00](https://www.youtube.com/watch?v=2aFjInvshkE&t=660s), V3 [18:00](https://www.youtube.com/watch?v=sGhxghCtkhY&t=1080s) plus [20:20–24:40](https://www.youtube.com/watch?v=sGhxghCtkhY&t=1220s). Transcriptions MCP entièrement accessibles, sans pages restantes. À obtenir : captures image à ces moments et replay d'événements serveur 3.7.13.17 ; aucun autre lien fourni en attente.
