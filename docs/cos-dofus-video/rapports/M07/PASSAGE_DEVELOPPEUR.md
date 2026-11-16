# M07 — Passage développeur : Duel / De la même lignée, personnages et compagnons

**Date : 04/11/2026.** Spécification documentaire, **aucun test exécuté**. La source normative disponible ici est une description tierce de **2.68 (2023)** ; le build cible est **3.7.13.17** et ses exceptions sont `unknown`. Les deux transcriptions vidéo MCP servent de contexte général, pas de preuve spécifique des critères sur compagnon. Voir [rapport](RAPPORT.md) et [fiche](FICHE_CHALLENGES.md).

## Trois règles prioritaires et leurs preuves exactes

| Priorité | Règle à évaluer | Provenance / niveau de certitude | Preuve à obtenir ensuite |
| --- | --- | --- | --- |
| **1 — M07-R01/R03 Duel** | Conserver **un premier attaquant par cible ennemie** sur toute la vie du combat. Interdire au plan local qu'un autre combattant attaque une cible déjà assignée. | **corrobore-source** [Dofus Pour Les Noobs — Duel, 2.68](https://www.dofuspourlesnoobs.com/les-challenges.html) ; [Ouatdefok 42:58–43:08](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=2578s), **declare-video** sur l'importance du suivi de cible uniquement | Replay avec attaque A→X puis K→X et A→X puis K→Y, état serveur du challenge après chacune |
| **2 — M07-R02/R04 Lignée** | À la **fin de chaque tour allié**, ce combattant doit être **sur une ligne avec au moins un autre combattant allié**. N'exiger ni contact ni alignement de **tous** les alliés. | **corrobore-source** [Dofus Pour Les Noobs — De la même lignée](https://www.dofuspourlesnoobs.com/les-challenges.html), [Gamosaurus 2.68](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) ; aucune vidéo directe | Rejouer deux placements aligné/non aligné avec `TurnEnded` confirmé ; lire indicateur de challenge |
| **3 — M07-R05/R06** | Identifier chaque **combattant** séparément de son **contrôleur** et de son éventuel **invocateur** ; propager `unknown` pour le statut et les dégâts indirects non étayés. | **corrobore-source** [Dofus Pour Les Noobs compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html) ; extrait [Fandom « players and Sidekicks », « summons usually exempt »](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) **non consultable en page pleine (402)** ; vidéo [Ouatdefok 21:10–22:13](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=1270s) démontre seulement le placement initial contrôlé | Duel avec Korbax distinct et invocation à l'origine d'un dommage indirect ; comparer attribution serveur, ne pas inférer le statut de l'ID de compte |

## Données et logique à fournir au simulateur

**Combat et challenge** : `session`, `epoch`, `combatId`, clientBuild, challengeId réel, texte/version, `Selected/Active/FailedConfirmed/SuccessConfirmed/Unknown`. Les ID numériques ne sont pas inventés. **Combattant** : `fighterId, teamId, controlledBy, companionOwner?, summonerId?, classOrCompanionKind, alive, position, turnOrder, currentTurn, resources`. **Actions** : `actorFighterId, targetFighterId/cellId, spell/action source, damageSource, castResult, eventOrder, appliedEffects, sourceOwner`. **Carte** : coordonnées de grille normalisées et un prédicat `inSameLine` défini par l'API de géométrie réelle, ainsi que les obstacles et la visibilité si pertinents.

Le registre de Duel proposé (à validation serveur) est `Map<enemyFighterId, allowedAlliedAttackerFighterId>`. Une attaque confirmée par A sur X sans entrée antérieure attribue X à A. Une nouvelle attaque confirmée de **B** sur X lorsque B≠A prédit `unsafe`. Une deuxième attaque de A sur X reste `safe` sous le texte. Une action de type `attack` ambigu, où l'acteur d'un poison/renvoi/invocation n'est pas connu, fait `unknown` ; **ne pas transformer cela en succès**. L'événement d'échec serveur, distinct, est la source d'autorité.

Pour la lignée, sur `TurnEnded(A)` confirmé, prendre les positions **actuelles** des alliés éligibles ; `safe` si existe un combattant B distinct de A tel que `inSameLine(A.position,B.position)`; `unsafe` si liste complète connue et aucun B aligné ; `unknown` si positions, éligibilité, dernière phase ou règle de version manquent. Un autre combattant sur la même ligne **n'a pas besoin d'avoir joué** ; s'il meurt ou est déplacé, réévaluer seulement au bon événement. Conditions spécifiques au tour final, revives, invocations et personnages sous état spécial **unknown**.

**Règle de coordination** : deux personnages, Korbax et d'autres alliés peuvent partager un contrôleur, mais **jamais leurs PA/PM, états, ordre de tour ou propriété d'attaque**. Ces champs déterminent le plan faisable. Dans le cas « partenaire déjà fini son tour », ne pas simuler rétroactivement une réorientation. Les transitions sont `Request → Acknowledgment → Applied effects → TurnEnded → challenge-status`, avec ACK ≠ résultat serveur.

## Tests discriminants à enregistrer

| Cas | Préconditions et stimulus | Attendu documentaire / oracle | Risque détecté | Statut |
| --- | --- | --- | --- | --- |
| **D-P01** | A et K allies ; X/Y ennemis ; `A→X, K→Y, A→X` en attaques confirmées | `duelOwner[X]=A`, `duelOwner[Y]=K`; aucune collision selon texte 2.68 ; confirmer côté serveur | Algorithme par contrôleur/compte ou reset à chaque tour | **propose, non exécuté** |
| **D-N01** | Même instance, `A→X, K→X` | prédiction `unsafe` si K est distinct dans challenge ; oracle échec challenge et acteur responsable, caractère Sidekick **unknown en 3.7** | Monstre frappé par 2 combattants, attribution erronée | **propose, non exécuté** |
| **D-D01** | A applique poison sur X ; K fait une poussée sans dégâts puis une collision ; invocation frappe X | **unknown** avant oracle ; consigner origine de l'attaque, déclenchement, zéro dégâts et summoner | Compter uniquement les dégâts directs / faux propriétaire | **propose, non exécuté** |
| **L-P01** | A=(0,0), K=(2,0), `TurnEnded(A)` puis `TurnEnded(K)` sans changement | chaque combattant a un autre en ligne → `safe` sur prédicat abstrait | Ignorer Korbax dans la liste d'alliés | **propose, non exécuté** |
| **L-N01** | A=(0,0), K=(2,1), fin de tour A, aucun autre allié | `unsafe` documentaire ; confirmer si exceptions serveur | Tester ligne à la mauvaise phase ou confondre diagonale/ligne | **propose, non exécuté** |
| **L-D02 trio/quatuor** | trio A=(0,0), K=(2,0), B=(2,3) ; quatuor A=(0,0), B=(2,0), C=(2,3), K=(5,3) | chaque combattant partage **au moins une** ligne avec un autre, alors qu'ils ne sont pas tous sur une même ligne | Confondre « même lignée » avec « En ligne et contre tous » | **propose, non exécuté** |
| **L-N03 interruption** | A a un partenaire initialement aligné, ennemi l'en déplace avant `TurnEnded(A)` | recalcul positions confirmées ; s'il ne reste aucun partenaire `unsafe` ; séquence serveur fait foi | Prévoir ligne sur ancienne position | **propose, non exécuté** |
| **L-D03 fin combat** | Korbax meurt et A termine/achève dernier monstre seul | **unknown**, cas spécial dernier tour mentionné dans résultat de recherche Fandom mais non vérifié en 3.7 | Déclarer faussement échec/succès | **propose, non exécuté** |

Le développeur doit choisir un modèle à trois résultats (`safe/unsafe/unknown`) pour la **prédiction**, et **garder séparé** le statut de challenge communiqué par le serveur. Aucun calcul de chance ou de bonus arbitraire. Une erreur in-game ne doit pas être masquée par une prédiction de succès.

## Ce qui reste bloquant

1. Exactitude des textes et identifiants de Duel / De la même lignée **dans le client 3.7.13.17**, et liste des types de combattants éligibles ; l'infobulle in-game ou un export client versionné sont requis.
2. Acteur effectif des dommages indirects et événements comptant comme **attaque** pour Duel (invocation, poison, glyphe, piège, poussée, collision, 0 dégât, attaque ratée).
3. Exceptions de fin de combat, décès, tour du compagnon et effet de déplacement **après** l'action mais **avant** la fin de tour en Lignée.
4. Correspondance `fighterId` / `challengeId` / `spellId` / signaux du protocole serveur, sans imaginer des champs qui n'existent pas.
5. **Preuve directe de « De la même lignée »** : aucune vidéo trouvée dans ce lot montrant ce challenge précis. [Ouatdefok, 21/12/2024](https://www.youtube.com/watch?v=ne-6NC0HF3Q) et [Laniyelle, 16/06/2023](https://www.youtube.com/watch?v=BbcElOsyEO8) sont des tutoriels de contexte avec transcriptions horodatées FR automatiques, pas une validation de l'algorithme.

**Priorité d'intégration :** d'abord les structures génériques d'identité et de challenge (impact toutes classes et compositions), ensuite le registre Duel, puis Lignée sur les vrais événements de fin de tour. Avant toute promotion `valide-test` ou `valide-live`, fournir un replay versionné, résultat attendu et obtenu, et artefact réel. Aucun test, partie ou modification de code n'a été exécuté pour M07.

