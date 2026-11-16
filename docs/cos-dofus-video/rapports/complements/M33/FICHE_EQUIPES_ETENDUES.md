# Fiche M33 — 5 à 8 places, entités et challenges Duel / De la même lignée

## Identité
- **ID** `M33-TEAM5TO8-SLOTS-SUMMONS-CHALLENGES-v1`, branche `M33-CHALLENGE-OWNER-ATTRIBUTION-v1`.
- **Date** : 2026-11-04 ; DOFUS 3 ; cible visée 3.7.13.17 mentionnée dans kit, **pas de combat réel ni protocole vérifié**.
- **Source vidéo 2025** : [HUZ REPLAY 05/12/2025 — Duos de classes](https://www.youtube.com/watch?v=X9gMQA5rhVw), 37 min, `get_video_info` et `get_available_languages` OK, `get_timed_transcript(lang=fr)` **OK : 1 203 segments**, [31:25–33:35](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1885s) vu dans la transcription. **Avis** : Sadida sur groupes 6–8, et certaines interactions de bouclier du Zobal.
- **Vidéo complémentaire historique** : [HUZ REPLAY 07/11/2024 — Teams de 6 & 8](https://www.youtube.com/watch?v=87ot3paBr1o), 27 min. `get_video_info` et `get_available_languages` OK, `get_timed_transcript` **bloqué IP YouTube**, aucun passage oral ; chapitres de description **02:54 / 13:33 / 16:40** seulement. La page [Huzounet actualisée 30/09/2026](https://huzounet.fr/guides/les-teams-de-8) l'intègre, mais ses exemples sont libellés **2.73**.
- **Sources normatives documentaires (non serveur)** : [DPLN compagnons 02/03/2026](https://www.dofuspourlesnoobs.com/les-compagnons.html), [DPLN challenges](https://www.dofuspourlesnoobs.com/les-challenges.html), [DPLN mise à jour 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [Gamosaurus compagnons ancien 2.67](https://www.gamosaurus.com/jeux/dofus/dofus-guide-des-compagnons-incarnations-apres-la-2-67-refonte), [Grimoire Enu 3.7.12](https://dofus-grimoire.fr/classes/enutrof/), [Grimoire Sadida 3.7.12](https://dofus-grimoire.fr/classes/sadida/), [Tofus Duel non versionné](https://www.tofus.fr/fiches/challenge).
- **Statuts** : `declare-video` uniquement pour paroles transcrites HUZ 2025 ; `avis-source` pour composition équipe de 8 Huzounet 2026 ; `corrobore-source` pour textes des règles de guides ; `hypothese` pour toutes sémantiques d'algorithme challenge non confirmées serveur 3.7 ; pas d'`observe-video`.

## Invariants proposés et preuves exactes

| ID | Affirmation / règle candidate | Preuve / statut | Limites et source d'autorité |
| --- | --- | --- | --- |
| **M33-R01** | Le compagnon est un combattant **dans une place de groupe** et est contrôlé par un joueur. Un joueur peut équiper un signe de compagnon ; les règles anciennes citent un maximum 8 places et quatre compagnons. | [DPLN compagnons actualisé 02/03/2026](https://www.dofuspourlesnoobs.com/les-compagnons.html) `corrobore-source`; [Gamosaurus 2.67](https://www.gamosaurus.com/jeux/dofus/dofus-guide-des-compagnons-incarnations-apres-la-2-67-refonte) `hypothese` pour plafonds | **Revalider** pour jeu/instance 3.7 et les combats spéciaux ; les 8 entités visibles ne prouvent pas 8 places. |
| **M33-R02** | La poupée Sadida et le Sac Animé Enu sont des **invocations supplémentaires en combat**, avec propriétaire, statut vivant, cellule ; un échec de pose ou leur mort change l'occupation. | [Grimoire Sadida 3.7.12](https://dofus-grimoire.fr/classes/sadida/) et [Enutrof 3.7.12](https://dofus-grimoire.fr/classes/enutrof/) `corrobore-source` | Types d'invocations, cases traversables, ordre de mort, limite d'invocation et sorts précis à lire par instance. |
| **M33-R03** | Le nombre de monstres en **donjon modulaire** est historiquement calculé selon **4–8 places**, mini 4 pour groupes inférieurs. | [Devblog historique donjons modulaires](https://dofus.jeuxonline.info/actualite/35775/devblog-donjons-modulaires), `corrobore-source-historique` | Donjon/quête/raid/Songes 3.7 peuvent déroger ; jamais attribuer un monstre supplémentaire à chaque **invocation**. |
| **M33-R04** | En 3.7 les **personnages** ont 15 s de plus en PvM, **pas les invocations**, hors Songes. | [DPLN 3.7 rubrique Temps de tour](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) `corrobore-source` | Le traitement des **compagnons** n'est pas explicité dans cette phrase ; délai serveur/IA et temps UI inconnus. |
| **M33-R05** | De la même lignée demande à **chaque combattant allié** de terminer son tour **en ligne** avec au moins un autre combattant allié. | [DPLN challenges](https://www.dofuspourlesnoobs.com/les-challenges.html) `corrobore-source` | Applicabilité des compagnons et invocations au texte « combattant allié » **Unknown**. |
| **M33-R06** | Duel, selon un guide non versionné, attribue à une entité attaquée un combattant autorisé ; une invocation est décrite comme assimilée à **son invocateur**. | [Tofus challenge Duel](https://www.tofus.fr/fiches/challenge), `corrobore-source-historique` + `hypothese` en 3.7 | Ne jamais appliquer cette assimilation à un **compagnon**, qui prend sa propre place. Il faut des preuves exactes 3.7 et un log. |
| **M33-R07** | Le challenge échoué affiche une croix rouge et peut associer une identité fautive dans le chat ; le résultat du combat prime sur l'oracle local. | [DPLN challenges](https://www.dofuspourlesnoobs.com/les-challenges.html) `corrobore-source` | Source de vérité événementielle du protocole encore non identifiée ; ne pas inventer IDs. |
| **M33-R08** | Les marques 3.7 ont des familles et phases : aura, début de tour, fin de tour pour les glyphes ; elles peuvent modifier une décision d'occupation/timing. | [DPLN MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) `corrobore-source` | Durée exacte/valeur de chaque sort dépend du deck ; ne pas réactiver un glyphe simplement à tout mouvement. |
| **M33-R09** | Le build 3.7 peut modifier le plafond d'invocation (+1 Dresseur / +2 Dompteur sous condition). | [DPLN MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) `corrobore-source` | Le cap final n'est pas `nombre de trophées` ; lire équipement/états/limitations de chaque sort et classe. |
| **M33-R10** | Choix de team **farm** versus **polyvalente** sont des objectifs distincts ; faire plus de damage en zone peut préférer 8 combattants, mais aucune formule de profit n'est prouvée. | [Huzounet guide mis à jour 30/09/2026](https://huzounet.fr/guides/les-teams-de-8), `avis-source` | Pas de taux de drop chiffré, ni validation sur 3.7 ; provenance de l'exemple 2.73 à conserver. |

## Modèle de personnes, entités et capacité

**Trois catégories distinctes**, avec variables `unitId` et `ownerId` indépendantes :

1. `player` : joueur P, E, C, F, contrôleur direct, occupe une place, budget propre et participation aux challenges selon règle.
2. `companion` : compagnon K1–K4, **son propre combattant**, contrôlé par un joueur propriétaire, occupe une place sous règles S33-R01 ; 6 sorts et niveau selon [DPLN](https://www.dofuspourlesnoobs.com/les-compagnons.html). Les sorts d'un K déterminé sont **inconnus sans son nom, rang et deck**. Ne pas assimiler ses attaques aux attaques du joueur sans preuve.
3. `summon` : Sac Animé, poupée, arbre ou tourelle selon classe ; `ownerUnitId`, `summonId`, contrôlable ou non, cellule, état de vie, durée, statut ; **aucun slot de groupe ajouté**. Son éventuelle participation aux challenges est spécifique.

**Capacité visée par cette étude** : 5 = 4 joueurs+1 compagnon ; 6 = 4+2 ; 7=4+3 ; 8=4+4. Il existe aussi 5–8 joueurs sans compagnon ou d'autres répartitions permises. **Ce tableau est une famille de fixtures à vérifier dans l'instance** (limite par contenu et configuration), pas une garantie de création de groupe. À l'intérieur d'une même rencontre, les 4 joueurs + 2 compagnons + 1 Sac = **6 places mais 7 entités alliées visibles**, sous acceptation de l'invocation. Les groupes d'ennemis sont fournis par le combat réel : une invocation ne doit pas déclencher le redimensionnement du donjon.

## État initial détaillé et ordre d'initiative

- **Instance P33-06** (6 places, combat ordinaire proposé) : joueurs `P` Pandawa, `E` Enutrof, `F` Féca, `C` Crâ ; compagnons `K1(owner=P)`, `K2(owner=C)` ; six monstres fictifs M1…M6. Pas d'invocation initiale.
- **Cellules relatives non `cellId`** : `P=(0,0)`, `M1=(1,0)`, `E=(0,2)`, `F=(-1,0)`, `C=(-3,0)`, `K1=(-2,1)`, `K2=(-2,-1)`, `W=(1,2)` candidat Sac libre, `Z=(2,1)` candidat jet Pandawa libre. Les autres monstres M2–M6 sont **hors zone immédiate** et leurs cellules réelles doivent être capturées, non fabriquées comme contraintes de route.
- **Timeline illustratrice** : `P → K1 → E → M1 → F → C → K2 → M2 → … → M6`, intercalée ; n'est **pas** un ordre prescrit pour les classes ou compagnons. `initiative` réelle doit être lue en ouverture et après changements. Tous les `unitId` vivants à T1.
- **Ressources et variantes** : Karcham P, 1 PA porter +1 PA jeter avec P Sobre si légal ; Sac Animé E, 2 PA (données [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/enutrof/)), Maladresse E, 1 PA (jusqu'à -2 PM tentés), Barricade F 2 PA ; sorts C/K1/K2 **non spécifiés**, budgets par unité issus du serveur. Challengers `Duel` ou `De la même lignée` lus du texte actuel de la rencontre, pas tous les deux toujours disponibles.
- **Contraintes** : W/Z libres à T1, portée/LoS légales à confirmer, état des alliés/ennemis, capacité d'invocation E et Deck, zone de Sac et glyphes inconnues ; aucune case n'est considérée libre après un déplacement tant qu'il n'est pas confirmé.

## Séquence round T1 à plusieurs acteurs

| Ordre | Acteur | Action/cible et coût attesté | Conditions | Effet à confirmer et risque |
| --- | --- | --- | --- | --- |
| 1 | P | Porter M1 avec Karcham **1 PA**, jeter M1 sur Z **1 PA** | P Sobre, M1 adjacent, Z libre, sorts et portée légaux | M1 migre de (1,0) à Z, change LoS de C/K1 ; si refus, conservation ancien état et replanifier. |
| 2 | K1 | Déplacement légal ou sort issu de sa **liste réelle** ; coût **inconnu** | Turn K1 vivant, peut viser la bonne cible et respecte challenge | Pas de « soutien générique » attribué à Korbax ou autre compagnon ; vérifier cible, dégâts et propriétaire. |
| 3 | E | Sac Animé en W **2 PA** puis Maladresse M1 **1 PA** | W désormais libre, quota invocation, sort connu ; M1 vivant et ciblable | Ajouter `SacId` seulement après spawn confirmé ; PM réellement perdus ≠ 2 garantis. |
| 4 | M1 | Déplacement / attaque selon IA et PM restants | M1 vivant | Peut tuer Sac, F ou K2 avant leurs tours ; retirer PV, effets et places. |
| 5 | F | Barricade sur P **2 PA** si prochaine menace mêlée ; sinon action légale adaptée | F vivant, P vivant, buff applicable | Ne protège pas rétrospectivement de M1 déjà joué ; si F est mort, annuler ses futurs lancers. |
| 6 | C | Attaque avec sort du deck réel, cible révalidée | LoS, rang, PA, cible vivante, règle Duel | Si challenge Duel, attention à la paire `actorAttribution,target`; ne pas attaquer par erreur M1 attribué à K1. |
| 7 | K2 | Sort réel ou déplacement légal | K2 vivant, terrain et challenge toujours compatibles | Une cible tuée par C rend invalide l'attaque prévue. À la fin du tour, vérifier lignée si catégorie applicable. |
| 8 | M2–M6 | Tours adverses | Priorité serveur | Actualiser toute position, état de groupe, challenge et invocations restantes. |

Au **fin de tour** de chaque combattant admissible, le moteur vérifie la contrainte *De la même lignée* **au moment exact**, et non à la fin du cycle entier. Le challenge *Duel* requiert une mémoire persistante `targetId→actorAttribution`, avec un statut spécial **Unknown** si la catégorie de l'acteur n'a pas été démontrée. Aucun verdict « gagné » avant validation de combat.

## Scénarios comparatifs et contre-exemples

| Cas | Fixture modifiée (une variable si possible) | Actions | Attendu discriminant, pas résultat inventé | Statut |
| --- | --- | --- | --- | --- |
| **P33-5/6/7/8** | Respectivement 4P+1/2/3/4 compagnons ; aucun/1 Sac invoqué | Entrée combat/ajout d'un compagnon et spawn | Compter `slots` ≠ `visibleEntities`, distinguer capacité refusée d'un spawn d'invocation ; nombre ennemi **observé** par instance | proposé non exécuté |
| **P33-ROUND** | Six places, W et Z libres, K1/K2 vivants ; Duel ou Lignée explicitement choisi | Round ci-dessus | Mise à jour des positions, owner et turn order, statut challenge par événement, aucune attaque imaginaire de compagnon | proposé non exécuté |
| **N33-01 COLLISION** | P jette M1 **sur W** ; E garde action « invoquer Sac sur W » | P puis E | Recalcul W occupée ; E choisit une nouvelle case légale **avant** lancer ou abandonne ; aucun Sac doublé ni PA inventé | proposé non exécuté |
| **N33-02 MORT** | M1 tue K2/Sac **avant le tour de K2** | M1 puis K2 planifié | K2 mort n'agit pas, statut d'occupation libéré, effets liés à K2 vérifiés ; bilan challenge reclassé, sans ressusciter | proposé non exécuté |
| **N33-03 DUEL OWNER** | P attaque M1, ensuite compagnon K1 contrôlé par P attaque M1 | Deux attaques ciblées séparées | Attribution compagnon **distincte ou non** selon preuve serveur réelle ; ne pas assimiler K1 à P comme une invocation par réflexe. Oracle 3.7 **Unknown** | proposé non exécuté |
| **N33-04 DUEL INVOC** | P attaque M1 puis invocation propriétaire P attaque M1 | Deux attaques séparées | Source historique Tofus assimile l'invocation au propriétaire ; **vérification indispensable** du résultat et de la règle en 3.7 | proposé non exécuté |
| **N33-05 LIGNEE** | P finit tour aligné uniquement avec **Sac**, pas avec un joueur/compagnon | Fin tour P | Tester si le Sac est un allié **éligible** ; ne pas compter automatiquement le Sac, journaliser résultat de challenge | proposé non exécuté |
| **N33-06 TIMING** | État 3.7, tour invocation et tour joueur tous deux sous contrainte de durée | Mesurer budget affiché/réel des deux | Le bonus +15 s concerne **personnages PvM**, **pas les invocations** ; temps compagnon et animation restent inconnus | proposé non exécuté |

## Solo/duo/trio/quatuor comme sous-équipes fonctionnelles
Une team 8 peut être **incubatrice** (source Huzounet 2026) : sélectionner une cellule de plan `P+Enu+Crâ+Féca` ou `Sadida+Hupper+Steamer+Zobal` lorsque des défis demandent une coordination à quatre. En situation où **5–8** jouent réellement, conserver le tour des autres et leurs actions dans le même calendrier : ils peuvent bloquer les lignes, tuer la cible d'un combo en préparation, laisser un challenge échouer ou fournir protection. **Les actions des quatre autres ne sont pas des pauses invisibles.** Les sous-équipes sont des **plans partiels**, pas des groupes isolés avec leur propre timeline.

## Risques, formule, sortie et inconnues
- **Risques** : en combat à 6–8, le donjon peut proposer d'autres monstres ; les invocations et compagnons peuvent obstruer chemins/LoS, être taclé/mourir/agir avant un joueur ; challenge initialement assigné à une cible éliminée ; effets de zone/poison sur une cible préattribuée ; ennemis qui attaquent pendant les préparations.
- **Formules inconnues** : coefficients de loot et succès de challenges, damage par zone, bonus invocations/compagnon, attribution globale de Duel, règle 3.7 exacte d'éligibilité des invocations à Lignée, nombre des monstres dans chaque donjon. **Ne pas extrapoler** à partir d'une vidéo ou du guide 2.7.
- **Sortie développeur** : voir [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md). États attendus : capacité slots, identités, owners, occupations, timeline, cellules, challenge current status, cause des effets. `Unknown` si absence d'ACK serveur **n'est pas** `Success`.
- **Reprise** : capturer un replay autorisé et expurgé de groupe réel 5/6/7/8, la présentation des compagnons et invocations, timeline, événements de challenge, compteur de monstres, tour K1, tour Sac, fin de tour lignée, Duel joueur→invocation→compagnon. Aucun test ni live réalisé.

