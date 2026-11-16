# M01 — Sram terre : ordre de résolution d'un réseau de pièges

Analyse documentaire du 4 novembre 2026. **Périmètre borné :** un réseau Terre avec Piège Mortel, Piège Répulsif, entrée par déplacement forcé et arrêt d'impulsion sur un piège intermédiaire. L'ensemble des règles du Sram, les formules de dégâts et toutes les interactions de marques ne sont pas couverts.

## 1. Réponse à la question M01

Un réseau n'est pas une somme de dégâts indépendants : il faut conserver les marques posées, leurs zones de déclenchement, leur **ordre de pose**, les positions courantes des combattants et les événements de déplacement. Une entité déplacée de force **dans la zone de déclenchement** d'un piège peut le déclencher ; une poussée de deux cases peut être interrompue à la première case où un nouveau piège se déclenche. Plusieurs pièges dont les zones couvrent une même case doivent être ordonnés ; les déplacements produits au cours de la résolution peuvent déclencher d'autres pièges avant que tous les effets initialement sélectionnés aient fini de se résoudre. L'ordre fin des événements internes au serveur et la date exacte de retrait d'une marque par rapport au calcul des dégâts **restent à confirmer sur la version cible**.

Preuves principales : [Pchen, 00:28–03:29](https://www.youtube.com/watch?v=hQExxOlRS-o&t=28s), [Pchen, 08:57–10:08](https://www.youtube.com/watch?v=hQExxOlRS-o&t=537s), [GOATESQUE, 06:00–06:54](https://www.youtube.com/watch?v=sGhxghCtkhY&t=360s), [GOATESQUE, 08:32–10:49](https://www.youtube.com/watch?v=sGhxghCtkhY&t=512s). Ce sont des paroles extraites de sous-titres français automatiques : elles appuient le **modèle fonctionnel**, non la chronologie exacte des trames serveur.

**Trois notions à séparer :**

1. **Pose / enregistrement** : le sort autorisé ajoute une marque identifiable avec position, propriétaire, type, ordre de pose, zone et effets. Les PA sont débités après confirmation de la pose ; aucune activation par un tiers n'est déduite du seul ACK de demande de lancer.
2. **Activation / sélection de déclenchement** : une transition spatiale peut rendre une ou plusieurs marques éligibles. Ce terme est utilisé ici comme **concept du simulateur** ; il ne représente pas un message protocolaire dont le nom serait établi.
3. **Déclenchement / effets / retrait** : le moteur applique les dégâts et déplacements réellement confirmés, puis retire la marque selon la confirmation serveur. Une marque peut être sélectionnée alors qu'un effet antérieur fait déjà bouger la cible ; son ordre de consommation exact est une question de preuve. L'affichage de disparition d'une marque ne suffit pas à établir l'ordre des messages ou des effets.

## 2. Journal des vidéos et accès réel

Au plus trois tutoriels étudiés. Aucune vidéo n'a été fournie initialement ; recherche effectuée le 4 novembre 2026. Les métadonnées et transcriptions proviennent des outils MCP vidéo, pas de captures du plateau.

| ID | Vidéo et métadonnées vérifiées par MCP | Version explicitement indiquée | Langue et accès | Intervalles effectivement retenus |
| --- | --- | --- | --- | --- |
| V1 | [Pchen — SRAM RESEAUX LES BASES NIVEAU 1 - DOFUS 3.0](https://www.youtube.com/watch?v=hQExxOlRS-o), publication **26/05/2025** (MCP), **12 min** | 3.0 dans le titre, build exact inconnu | FR, sous-titres **automatiques**, transcription horodatée MCP, 349 segments, dernier curseur nul | [00:28–03:29](https://www.youtube.com/watch?v=hQExxOlRS-o&t=28s), [03:40–07:30](https://www.youtube.com/watch?v=hQExxOlRS-o&t=220s), [07:55–11:30](https://www.youtube.com/watch?v=hQExxOlRS-o&t=475s) |
| V2 | [Shurikn — Guide Sram & réseaux de pièges sur DOFUS 3 unity - Partie 2](https://www.youtube.com/watch?v=2aFjInvshkE), publication **14/01/2025** (MCP), **22 min** | DOFUS 3 / Unity, build exact inconnu | FR, sous-titres **automatiques**, transcription horodatée MCP, 748 segments, dernier curseur nul | [02:13–11:00](https://www.youtube.com/watch?v=2aFjInvshkE&t=133s) et [16:41–20:00](https://www.youtube.com/watch?v=2aFjInvshkE&t=1001s) |
| V3 | [GOATESQUE - DOFUS — TUTORIEL COMPLET SRAM RÉSEAUX DE PIÈGES](https://www.youtube.com/watch?v=sGhxghCtkhY), publication **09/01/2023** (MCP), **32 min** | Ancienne version, patch exact inconnu ; antérieure à DOFUS Unity | FR, sous-titres **automatiques**, transcription horodatée MCP, 894 segments, dernier curseur nul | [05:45–18:00](https://www.youtube.com/watch?v=sGhxghCtkhY&t=345s), [20:20–24:40](https://www.youtube.com/watch?v=sGhxghCtkhY&t=1220s) |

**Limite d'accès :** l'information vidéo, les langues et les transcriptions horodatées fonctionnent. Les passages n'ont pas fait l'objet d'une lecture d'images de la vidéo ni d'un replay serveur. Par conséquent **aucune règle n'est classée observe-video, valide-test ou valide-live**. Les coordonnées montrées oralement comme « ici », « là », « en haut » ne permettent pas de reconstituer les cases exactes à partir des seules paroles. La fiche construit donc un *fixture proposé* explicite distinct d'une observation.

Les sous-titres déforment certains noms : « inidieux / incidieux » ≈ Piège Insidieux, « répu » ≈ Piège Répulsif, « sournoi » ≈ Piège Sournois, « fangu / fin jeu » ≈ Piège Fangeux ou Funeste selon le contexte. Ne pas convertir ces segments directement en identifiants de sorts.

## 3. Réconciliation avec les références récentes

| Source | Version, date, ce qu'elle appuie | Précaution |
| --- | --- | --- |
| [Dofus Grimoire : Sram, sorts et réseaux](https://dofus-grimoire.fr/classes/sram/) | Fiches déclarées **3.7.12**, correctif du **20/10/2026**. Piège Mortel : 3 PA, 2 poses/tour, ciblage en ligne et sans ligne de vue ; Piège Répulsif : 2 PA, 2 poses/tour, zone en croix 1 et poussée 2 ; modèle « Le Doublé » à 8 PA, deux Mortels et un Répulsif | Site tiers, fiche **3.7.12** ; n'établit pas le comportement exact en **3.7.13.17** ni le protocole |
| [Back from AFK : évolutions Sram de 3.0 à 3.7](https://backfromafk.com/classes/sram/depuis-3-0/) | Données déclarées **3.7, octobre 2026**. Piège Répulsif passe de **1 à 2 lancers par tour** depuis 3.0 ; autres sorts modifiés | Confirme que les valeurs 2025 ne sont pas transposables sans relecture versionnée |
| [Dofus Pour Les Noobs : mise à jour 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) | Article du **04/10/2026** : pièges catégorisés comme **marques au sol** ; affichage du nom, lanceur, durée et effets | Décrit surtout la représentation UI, **pas** l'ordre des activations/dégâts/disparitions |
| [OpenDofusDB : journal 3.7.13.17](https://openddb.dev/changelog) | Extraction du **03/11/2026**, version **3.7.13.17**, confirme l'existence du nouveau correctif | Le journal de cette page ne prouve aucun changement ou invariant particulier des pièges |
| [Eclypsia : Apprendre à jouer Sram](https://www.eclypsia.com/home/dofus/guides/dofus-apprendre-a-jouer-sram-71668.html) | Exemple historique de pose ordonnée et d'envoi par Peur | Ancien guide, **obsolete-pour-cible** pour les valeurs de sorts ; illustration conceptuelle uniquement |

**Conflits et portée :** V1 et V2 parlent de DOFUS 3.0 début 2025 ; la référence actuelle connue du projet est **3.7.13.17** (également mentionnée dans le kit source). La limite « 1 Répulsif par tour » qui aurait pu figurer dans un ancien plan n'est plus sûre ; Back from AFK et Grimoire indiquent **2** en 3.7. Les vidéos 2023 ne fixent ni coût, ni portée, ni cumul, ni seuil de dégâts pour 2026. Le simulacre « un seul piège par case » et le recouvrement des zones sont décrits par Grimoire ; l'interaction exacte entre pièges **simultanément éligibles** reste à tracer.

## 4. Règles atomiques, avec provenance et statut

| ID | Proposition précise | Statut de preuve | Preuve et limite |
| --- | --- | --- | --- |
| M01-R01 | Le **déplacement forcé** d'un combattant qui entre dans la zone de déclenchement d'un piège peut ouvrir le réseau | **declare-video**, **corrobore-source** | [V2, 04:08–05:32](https://www.youtube.com/watch?v=2aFjInvshkE&t=248s) : entrée par Peur / placement ; [Grimoire, « Un piège, c'est une zone »](https://dofus-grimoire.fr/classes/sram/) |
| M01-R02 | **L'ordre de pose** des pièges de déplacement doit être conservé pour prédire leur ordre d'impulsions lorsqu'ils couvrent une même entrée | **declare-video**, **corrobore-source** | [V1, 01:01–03:27](https://www.youtube.com/watch?v=hQExxOlRS-o&t=61s) : Sournois, Répulsif, Insidieux ; [V3, 07:18–07:56](https://www.youtube.com/watch?v=sGhxghCtkhY&t=438s) ; [Grimoire, « Réseaux de pièges »](https://dofus-grimoire.fr/classes/sram/) |
| M01-R03 | Un Répulsif exerce **une impulsion de deux cases** depuis le centre (en version consultée) ; une autre marque rencontrée pendant le trajet peut **couper** cette impulsion | **declare-video**, **corrobore-source** | [V1, 08:57–09:45](https://www.youtube.com/watch?v=hQExxOlRS-o&t=537s) ; [V3, 06:30–06:54](https://www.youtube.com/watch?v=sGhxghCtkhY&t=390s) ; [Grimoire, Piège Répulsif](https://dofus-grimoire.fr/classes/sram/), **3.7.12**. À confirmer contre une vraie fixture 3.7.13.17 |
| M01-R04 | Sur une même case d'entrée, **plusieurs zones peuvent se chevaucher** sans que deux marques soient posées sur la même case | **corrobore-source** | [Grimoire, « Réseaux de pièges »](https://dofus-grimoire.fr/classes/sram/), **3.7.12** |
| M01-R05 | **Piège Mortel** inflige des dommages Terre ; la présence d'un Mortel sur l'arrivée d'une poussée produit une étape de dégâts supplémentaire si la cible y entre | **declare-video**, **corrobore-source** | [V2, 04:08–04:34](https://www.youtube.com/watch?v=2aFjInvshkE&t=248s) ; [V1, 04:14–04:34](https://www.youtube.com/watch?v=hQExxOlRS-o&t=254s) ; [Grimoire, « Le Doublé »](https://dofus-grimoire.fr/classes/sram/) |
| M01-R06 | La sélection de plusieurs pièges sur une case et leur **résolution imbriquée/en attente** exigent une politique de file d'événements ; Grimoire décrit l'ordre de pose avec une reprise ultérieure des autres effets | **corrobore-source** uniquement | [Grimoire, règles « mise en attente » et « ordre de pose »](https://dofus-grimoire.fr/classes/sram/). **Non établi par un log serveur** : priorité, snapshot de zone et effet sur une cible ayant quitté la zone restent des hypothèses |
| M01-R07 | Les marques au sol « piège » sont représentées avec **propriétaire/lanceur, durée et effets** dans la lisibilité 3.7 | **corrobore-source** | [Dofus Pour Les Noobs, mise à jour 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; il s'agit d'UI, pas d'un contrat de trame |
| M01-R08 | Lors d'un piège déclenché, **la marque est à terme consommée/retirée** ; son point exact de retrait et la séquence de notifications restent inconnus | **hypothese** pour la chronologie, **corrobore-source** pour l'emploi de pièges consommables | [Grimoire, « Réseaux de pièges »](https://dofus-grimoire.fr/classes/sram/) ; **aucune preuve** de l'instant exact côté serveur |
| M01-R09 | Un **obstacle / déplacement impossible** peut empêcher l'accès au second Mortel même si l'entrée a déclenché le premier | **hypothese** pour la configuration exacte, **declare-video** pour le principe de coupure de chaîne | [V3, 08:32–10:49](https://www.youtube.com/watch?v=sGhxghCtkhY&t=512s), [V1, 08:57–10:08](https://www.youtube.com/watch?v=hQExxOlRS-o&t=537s) ; collision murale et dommages de poussée à mesurer séparément |
| M01-R10 | Déclencher un réseau est distinct de **placer la cible devant l'entrée** : Peur, Méprise, Double et/ou aide alliée modifient le coût et la faisabilité | **declare-video** | [V2, 04:28–05:32](https://www.youtube.com/watch?v=2aFjInvshkE&t=268s), [V2, 08:10–10:40](https://www.youtube.com/watch?v=2aFjInvshkE&t=490s). Les variantes de sorts dépendent de la configuration exacte |

Note sur « observe-video » : **aucune attribution** car aucun flux d'images de ces séquences n'a été inspecté. « Corrobore-source » désigne une page documentée, et non une preuve officielle de l'algorithme moteur.

## 5. Chronologie de travail et situations discriminantes

Le fixture de la [fiche](FICHE_SRAM.md) utilise l'entrée **E = (0,0)**, un Mortel **M1** en E, un Répulsif **R** en **(-1,0)** dont la croix couvre E, et un second Mortel **M2** en **(2,0)**. La cible part de **(0,-1)** et un déplacement forcé d'une case vers le bas l'amène en E. Sur terrain libre, le modèle prédit : entrée E → dégâts M1 → impulsion de R vers +X (deux cellules) → entrée sur M2 → dégâts M2 → position finale (2,0). Ces coordonnées sont **inventées pour le test**, pas reprises de la vidéo ; les valeurs de dégâts et les identifiants protocolaires sont volontairement inconnus. Il faut valider que les placements choisis satisfont les portées **depuis la position réelle du lanceur**, notamment la condition « en ligne » des Mortels.

**Contre-exemple structurel :** placer un obstacle dur sur **(1,0)**. La cible ne peut pas parvenir par poussée classique sur **(2,0)** ; l'activation de M2 ne doit jamais être simulée simplement parce que R a été déclenché. La collision, la règle d'arrêt, l'éventuel recul et les éventuels dommages de poussée nécessitent une oracle serveur ; l'assertion minimale porte seulement sur l'absence d'entrée dans la case de M2.

**Contre-exemple d'ordre :** inverse la chronologie de pose des mêmes marques couvrant E et compare *toute* la suite des transitions. On attend une différence seulement si l'algorithme de résolution réellement versionné dépend de cet ordre ; ne codons **aucune séquence attendue exhaustive** sans replay. Ce test est particulièrement utile pour décider si les effets en attente résolvent sur la position courante ou sur la position d'activation.

## 6. Limites et demande de preuve suivante

- Confirmer avec replay de **DOFUS 3.7.13.17** les identités de marques, séquences et priorités lors d'une double couverture, ainsi que la position au moment des dégâts.
- Vérifier si un piège posé sur une case déjà couverte par une **zone** est autorisé (le modèle Grimoire distingue « même cellule de pose » et « chevauchement de zones »).
- Relever les messages et leurs identifiants réels pour : demande de pose, confirmation, ajout de marque, déplacement, début/fin de déclenchement, dégâts, retrait de marque. Aucun nom d'événement ni ID de sort n'est établi ici.
- Vérifier obstacle, état Indéplaçable / enracinement, mort d'une cible pendant la chaîne, arrivée par téléportation, nombre de cibles et effets sur alliés. Ce sont des axes de test, **pas des invariants acquis**.
- Réexaminer les règles si le client 3.7.13.17 ou une version ultérieure diffère des pages 3.7.12. Les descriptions UI de 3.7 n'établissent pas la fidélité du simulateur.

État de reprise : les trois vidéos disposent de transcriptions récupérées intégralement côté MCP (curseur de pagination nul) ; les segments utiles sont référencés ci-dessus. Aucune page de transcription supplémentaire n'est en attente. La prochaine opération utile est une inspection vidéo de **V1 08:57–10:08**, puis une fixture de messages serveur anonymisée sur la même situation. Aucun essai en jeu n'a été réalisé.
