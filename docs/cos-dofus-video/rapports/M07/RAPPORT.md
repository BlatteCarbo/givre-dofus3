# M07 — Challenges « Duel » et « De la même lignée » : acteur, cible et fin de tour

Recherche documentaire datée du **4 novembre 2026**. Cible du projet : DOFUS **3.7.13.17**. Règles publiées à l'occasion de la refonte **2.68 (2023)** ; **aucun replay de challenge ni lecture du texte in-game 3.7.13.17**. La mission couvre des **combattants alliés contrôlés** de toutes classes, leurs compagnons, des compositions à deux, trois ou quatre participants et deux ennemis au minimum. Les invocations, les dommages indirects et les exceptions doivent conserver l'état **`unknown`** jusqu'à preuve sur le serveur cible.

## 1. Règles à distinguer précisément

- **Duel (M07-R01)** : d'après la description publiée par [Dofus Pour Les Noobs — Les challenges, liste 2.68](https://www.dofuspourlesnoobs.com/les-challenges.html), dès qu'un **combattant allié A attaque un ennemi X**, aucun **autre combattant allié B** ne doit attaquer **X pendant toute la durée du combat**. L'état de la contrainte est donc une association persistante **ennemi X → premier attaquant A**. A peut, selon le sens littéral du texte, attaquer de nouveau X ; il peut également attaquer Y s'il n'existe pas d'autre attribution conflictuelle. La formulation **ne demande pas** à chaque allié de tuer un ennemi. Ce second objectif apparaît pour **« Chacun son monstre »**, challenge distinct.
- **De la même lignée (M07-R02)** : d'après [Dofus Pour Les Noobs, liste 2.68](https://www.dofuspourlesnoobs.com/les-challenges.html) et [Gamosaurus — Les challenges après refonte 2.68](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte), **chaque combattant allié doit terminer son tour en ligne avec au moins un autre combattant allié**. Le prédicat se vérifie **à la fin du tour du combattant courant**, sur les positions de ce moment-là ; il n'exige pas, au sens du texte, que tous les alliés soient sur une même ligne. Cette condition plus stricte appartient à **« En ligne et contre tous »**. La notion de « ligne » doit être calculée avec la géométrie réelle de DOFUS, pas des coordonnées d'écran isométrique.

**Statut :** les définitions précises ont le statut `corrobore-source` sur des publications tierces datées 2023. L'extension de ces règles à la version 3.7.13.17, les attaques indirectes et les exceptions sont `unknown`. Ce sont des spécifications d'analyse, ni `valide-test` ni `valide-live`.

## 2. Provenance vidéo : accès et limites

Les vidéos suivantes ont été **retrouvées**, avec métadonnées et transcription horodatée obtenues via MCP. Aucun horodatage ne prétend démontrer les deux challenges en gameplay : l'échange vidéo sert à établir les fonctions générales des challenges et la présence distincte des compagnons.

| ID | Titre, chaîne, publication, durée MCP | Sous-titres réellement récupérés et passage pertinent | Ce que la vidéo soutient, et ce qu'elle ne soutient pas |
| --- | --- | --- | --- |
| M07-V1 | [DOFUS 3 : GUIDE DES COMBATS POUR LES NOOBS](https://www.youtube.com/watch?v=ne-6NC0HF3Q), **Ouatdefok**, **21/12/2024**, durée retournée **46 minutes** | FR **auto**, transcription horodatée **1362 segments**, `next_cursor=null`. Passages retenus : [14:27–17:02](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=867s), [21:10–22:13](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=1270s), [29:20–29:29](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=1760s), [42:58–43:08](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=2578s) | Décrit sélection XP/drop/challenges, placement initial du compagnon contrôlé et nécessité de suivre « qui tape quelle cible » pour Duel. **Ne montre aucune validation de Duel avec compagnon ni De la même lignée.** |
| M07-V2 | [NEWS - MAJ 2.68 : Suppression des Idoles et Refonte Eniripsa](https://www.youtube.com/watch?v=BbcElOsyEO8), **Laniyelle**, **16/06/2023**, durée retournée **4 minutes** | FR **auto**, transcription horodatée **122 segments**, `next_cursor=null`. [01:20–01:34](https://www.youtube.com/watch?v=BbcElOsyEO8&t=80s) | Énonce que des challenges sont ajoutés/modifiés et que l'initiateur choisit XP/drop. **Aucune règle de Duel/De la même lignée n'est prononcée.** Le lien vers le fil de bêta officiel est dans la description : [forum 2.68](https://www.dofus.com/fr/forum/1883-bugs-beta-2-68/2392598-beta-2-68-challenge-idoles) ; lecture de son contenu non établie. |

Les outils `get_video_info`, `get_available_languages`, `get_timed_transcript(lang=fr)` ont effectivement renvoyé les informations ci-dessus. Les sous-titres sont générés automatiquement et contiennent des erreurs de transcription (« partage / duel »). **Aucune frame du plateau examinée, aucun événement serveur inspecté.** Les liens `&t=` correspondent aux positions réelles des segments MCP ; ils ne sont pas des heures de mur.

Une recherche YouTube ciblée sur **« De la même lignée »** n'a pas fourni de démonstration clairement pertinente et transcrite dans le périmètre. Ce **manque de vidéo directe** est conservé comme lacune, et la règle provient des pages 2.68 uniquement.

## 3. Références externes et compatibilité

| Source exacte | Date/version exploitable | Valeur et limite |
| --- | --- | --- |
| [Dofus Pour Les Noobs — Les challenges](https://www.dofuspourlesnoobs.com/les-challenges.html) | Présentation de la refonte **2.68 (2023)**, page signalée datée **29/06/2023** | Description nominale des deux challenges, et distinction **Duel** vs **Chacun son monstre**, **De la même lignée** vs **En ligne et contre tous** ; texte ancien pour le build 3.7.13.17 |
| [Gamosaurus — Tous les challenges après la refonte 2.68](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) | Article de la refonte **2.68 (2023)** | Confirme « De la même lignée » avec **au moins un autre allié** ; ne détaille ni compagnons ni invocations |
| [Dofus Wiki Fandom — Fighting Challenge](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) | Date de version exacte non trouvée, résultat de moteur de recherche consulté le **04/11/2026** | Extrait indexé : « allied fighters » inclut **players and Sidekicks** ; **summons** généralement exclus, leurs dégâts/poisons imputés à l'invocateur. Page détaillée **inaccessible lors du chargement direct (402)** ; règles `unknown` en 3.7 |
| [Dofus Pour Les Noobs — Les compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html) | Site tiers, date/version exacte de la section non garantie | Compagnon contrôlé et occupant une place de combattant ; ne prouve pas à lui seul la participation à chaque challenge |
| [Dofus Pour Les Noobs — MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) | Publication **04/10/2026**, version 3.7 | Contexte de version actualisé ; **aucune citation de cette page ne prouve ici la persistance exacte** de Duel ou De la même lignée |
| [Fil bêta officiel DOFUS 2.68, lié par M07-V2](https://www.dofus.com/fr/forum/1883-bugs-beta-2-68/2392598-beta-2-68-challenge-idoles) | Fil ancien 2.68 cité dans description vidéo | Lien connu, contenu non exploité ; ne pas attribuer de confirmation officielle |

Le texte de **Duel** emploie « attaquer », pas « infliger des dégâts » ; **déclencheurs exacts** à confirmer : tentative refusée, frappe directe ratée, dommage d'un piège, poison, glyphe, attaque d'invocation, dommage de poussée, dégâts nuls, frappe mortelle, coup de grâce. Une convention de comptage générique serait une hypothèse. Le texte de **De la même lignée** emploie « combattants alliés », pas « personnages » ; pour un **compagnon Korbax**, l'extrait Dofus Wiki favorise son comptage séparé du personnage contrôleur, mais sans protocole ou capture 3.7, **statut `unknown`** pour les exceptions (mort, dernier tour, invocations, seuls survivants, carte en diagonale).

## 4. Règles atomiques avec statut et preuve

| ID | Proposition précise | Statut | Source et restriction |
| --- | --- | --- | --- |
| **M07-R01** | Duel : un ennemi attaqué par A n'est plus attaquable par un autre combattant allié sans risquer l'échec, même aux tours ultérieurs | **corrobore-source**, 3.7 **unknown** | [Dofus Pour Les Noobs — « Duel »](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| **M07-R02** | De la même lignée : à **chaque fin de tour** d'un combattant allié, il doit être en ligne avec **au moins un** autre combattant allié | **corrobore-source**, 3.7 **unknown** | [Dofus Pour Les Noobs](https://www.dofuspourlesnoobs.com/les-challenges.html), [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) |
| **M07-R03** | Duel requiert une mémoire **par ennemi** de l'identité du combattant premier attaquant ; la mémoire doit persister pendant le combat | **hypothese de formalisation** directement dérivée de R01 | [Dofus Pour Les Noobs — Duel](https://www.dofuspourlesnoobs.com/les-challenges.html), aucune trame prouvée |
| **M07-R04** | Lignée se vérifie contre les positions à l'**instant de fin du tour du joueur courant** et ne nécessite pas un alignement simultané de tous les alliés entre eux | **corrobore-source** pour « chaque fin de tour / au moins un », **hypothese** pour l'instant serveur précis | [Gamosaurus](https://www.gamosaurus.com/jeux/dofus/dofus-2-68-tous-les-challenges-apres-la-refonte) ; distinguer [En ligne et contre tous](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| **M07-R05** | Le compagnon est un combattant distinct du contrôleur ; l'extrait tiers suggère qu'un Sidekick est un `allied fighter` dans les challenges | **corrobore-source** à titre général, **unknown 3.7.13.17** pour le challenge concret | [Compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html), [Dofus Wiki Fandom, extrait seulement](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) |
| **M07-R06** | Invocations, pièges, glyphes, poisons, poussées : leur **attribution à l'acteur** et leur caractère d'attaque en Duel ne sont pas entièrement prouvés | **unknown**, modèle conservateur **hypothese** | [Fandom, extrait non ouvert](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) attribue les dégâts indirects à l'invocateur ; non validé version cible |
| **M07-R07** | Un échec de challenge est commun à l'équipe et peut être indiqué dans le journal avec l'auteur ; le bonus est perdu | **corrobore-source** | [Dofus Pour Les Noobs — « Les challenges, c'est quoi ? »](https://www.dofuspourlesnoobs.com/les-challenges.html) ; ID de résultat serveur non connu |
| **M07-R08** | Le dernier tour de combat pourrait posséder une exception « De la même lignée » selon l'ancienne fiche anglophone | **unknown**, hypothèse concurrente | [Fandom, extrait « Along the Same Lines / Does not fail on the last turn »](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) ; page inaccessible 402 ; ne pas présumer applicable en 3.7 |
| **M07-R09** | Le contrôle des alliés en lignée doit considérer toutes classes et toutes compositions, avec un compagnon comme candidat au moins provisoire | **hypothese de couverture**, politique prudente | [Dofus Pour Les Noobs](https://www.dofuspourlesnoobs.com/les-challenges.html) et [Fandom, extrait seulement](https://dofuswiki.fandom.com/wiki/Fighting_Challenge) |
| **M07-R10** | « Duel » n'implique **pas** le succès « Partage » ou « Chacun son monstre » qui exigent chacun un ennemi tué par allié | **corrobore-source** | [Dofus Pour Les Noobs, entrées séparées](https://www.dofuspourlesnoobs.com/les-challenges.html) |
| **M07-R11** | Un allié déplacé par un ennemi avant son tour doit être réévalué à la fin de son propre tour ; l'initiative et chaque fin de tour importent | **hypothese d'algorithme** dérivée de R02 | [Dofus Pour Les Noobs, Lignée](https://www.dofuspourlesnoobs.com/les-challenges.html) |

**Statuts exclus :** `observe-video` (absence d'observation des cases), `valide-test` et `valide-live` (aucune exécution). Il n'y a pas de probabilité arbitraire.

## 5. Modèle d'état nécessaire

Un **DuelGuard** doit distinguer `controllerAccountId`, `fighterId`, `summonerFighterId` et `targetFighterId`. Au premier événement de type « attaque qui compte » **confirmé**, `duelOwner[targetFighterId] = attackerFighterId`. Si un autre `attackerFighterId` attaque cette cible : violation présumée. Une action sans attaque confirmée ne remplit pas la carte. L'**identité de l'acteur** d'un poison, piège ou invocation reste `unknown` sans preuve versionnée ; utiliser `Unknown/NeedsValidation` plutôt que fusionner tous les acteurs sous le même compte.

Un **LigneeGuard** doit recevoir, à `TurnEnded(fighterId)`, les positions des alliés **présents et éligibles**. Sa condition proposée : `exists other ally j : sameLine(position[i], position[j])`. Le prédicat `sameLine` utilise la géométrie **officielle des cases**, pas l'égalité naïve de pixels d'écran. Le moment des mises à jour de position, les états morts, les invocations et la dernière action doivent être validés. Une mauvaise position pour un allié en fin de tour fait échouer le challenge global selon la description publiée 2.68.

**État global** : `Inactive / Selected / Active / FailedConfirmed / SuccessConfirmed / Unknown`. Garder `selectedChallengeIds`, texte localisé/versionné, `turnSequence`, état d'identité combat/session/époque, buffs et positions réels et événements d'attaque/dégât. Une croix rouge visible ou notification serveur confirmée peut clore le challenge, mais la prédiction locale ne devient pas confirmation serveur.

## 6. Matrices discriminantes à reproduire

Le fixture utilise **A = joueur**, **K = Korbax**, **X/Y = deux monstres**, tous vivants et de même combat, avec leurs tours distincts. Les coordonnées pour la lignée sont abstraites sur une grille à axes orthogonaux ; elles représentent les **lignes de lancer du moteur** après conversion d'une carte réelle. Aucune cellule effective client n'a été inventée.

| Cas | Séquence ou position au contrôle | Hypothèse de résultat | Règle | Statut |
| --- | --- | --- | --- | --- |
| **Duel P01** | A attaque X (confirmé), K attaque Y (confirmé), A attaque X à nouveau | Deux cibles possèdent un attaquant stable, challenge **préservé selon le texte 2.68** | R01/R03/R05 | **propose, non exécuté** ; compter K en 3.7 **unknown** |
| **Duel N01** | A attaque X, puis K attaque **X** | Challenge **échoué selon le texte 2.68 si K compte comme combattant allié indépendant** ; le plan prudent rejette l'action | R01/R05 | **propose, non exécuté**, attribution compagnon à confirmer |
| **Duel D01** | A pose un poison sur X, puis K pousse X dans un mur ; pas de coup direct | Résultat **unknown** : identifier ce qui constitue « attaquer », acteur des dégâts et ordre des effets | R06 | **propose, non exécuté** |
| **Lignée P01** | Fin de tour de A en (0,0), K en (2,0) ; fin de tour de K après maintien/retour en (2,0) | Chacun partage au moins une ligne avec l'autre, donc **condition texte respectée** | R02/R04 | **propose, non exécuté** |
| **Lignée N01** | Fin de tour de A en (0,0), K en (2,1), aucun autre allié | Aucun alignement pour A, challenge **échoué selon texte**, sous réserve du vrai prédicat de cellule | R02 | **propose, non exécuté** |
| **Lignée P02 — trio** | A=(0,0), K=(2,0), B=(2,3), chacun finit son tour en ces positions | A ↔ K, K ↔ A/B, B ↔ K ; **tous ont un partenaire**, sans ligne commune unique | R02/R04 | **propose, non exécuté** |
| **Lignée D02 — quatuor** | A=(0,0), B=(2,0), C=(2,3), K=(5,3) | Tous ont **au moins un partenaire** ; discrimine la règle exacte de « En ligne et contre tous » | R02/R04 | **propose, non exécuté** |
| **Lignée N02 — allié bougé** | A comptait finir aligné avec K ; un ennemi déplace K avant la fin de tour d'A, et plus aucun partenaire | La position réellement **confirmée au moment de la fin d'A** prime ; sans alignement, échec présumé | R11 | **propose, non exécuté** |
| **Lignée D03 — mort/fin combat** | Seul A survivant et fin de tour décisif ; ou K mort avant fin d'A | Résultat **unknown** (règles de dernière action, morts, alliés éligibles non établies) | R08/R09 | **propose, non exécuté** |

## 7. Risques, limites et reprise

- **Comptage du compagnon :** l'extrait Fandom indique explicitement les Sidekicks comme combattants alliés ; son accès direct renvoie HTTP 402. La fiche Dofus Pour Les Noobs confirme leur statut de combattant mais pas chaque exception. **Ne jamais transférer l'attaque de Korbax au personnage propriétaire par défaut**.
- **Comptage des invocations :** la terminologie « combattants alliés » ne suffit pas à déterminer toutes les exclusions ; différence historique entre `allié` et `combattant allié`. Garder une catégorie de fighter.
- **Effets indirects et mort pendant un tour :** résultats de Duel et de Lignée non déduits automatiquement des données actuelles. Prendre une règle prudente pour le planificateur et attendre le statut serveur.
- **Données versionnées :** exiger texte challenge et identifiant réel 3.7.13.17 + éventuel changement post-2.68 ; les deux vidéos datées renseignent le contexte, pas ce build.
- **Preuve suivante :** enregistrer deux combats (avec challenge actif) sur le client cible : (a) un joueur frappe X puis Korbax frappe X/Y, (b) A et Korbax alignés/non alignés à leurs **fins de tours**. Capturer logs de choix du challenge, entrée combat, casts et dégâts, changement de position, fin de tour et succès/échec serveur. Aucun compte/jeu n'a été utilisé dans la mission.

**Reprise :** deux transcriptions MCP V1/V2 lues et reçues intégralement (`next_cursor=null`) ; aucune page de transcript en attente. Vérifier la capture vidéo à [V1 42:58–43:08](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=2578s) et rechercher une **démonstration directe de De la même lignée**, absente de ce corpus. Aucun passage n'est classé `observe-video`.

