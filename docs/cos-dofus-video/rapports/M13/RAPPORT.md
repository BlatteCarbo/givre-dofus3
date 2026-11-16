# M13 — Crâ : portée, ligne de vue et recul (recherche documentaire)

**État au 04/11/2026 :** dossier de conception pour une fenêtre tactique concrète (tir → contrôle PM → recul). Les chiffres de sorts proviennent de références **tierces 3.7/3.7.12**, pas d'une extraction du client local **3.7.13.17**. Aucun combat, code ou test exécuté. Périmètre utilisateur global : les 19 classes et compositions solo, duo, trio, quatuor ou plus ; le cas étudié ici concerne Crâ, sans prétendre couvrir toutes les mécaniques de toutes les classes.

## Vidéos réelles identifiées et preuve effectivement accessible

| Ref | Titre / chaîne, date, durée | Méthode et passages | Ce qui est prouvé |
| --- | --- | --- | --- |
| V13-1 | XAWPIYA, [*DOFUS : Les MEILLEURS DUOS avec le Crâ en 2026 ! (Guide Complet)*](https://www.youtube.com/watch?v=5yqgq2xnWF8), **19 juin 2026**, **7 min** | MCP `get_video_info` et `get_available_languages` OK, `fr` automatique proposé ; `get_timed_transcript(lang=fr)` **échec** `Could not retrieve a transcript... YouTube is blocking requests from your IP` ; **aucun intervalle visionné** | Description du créateur : partenaires **Osamodas, Énutrof, Sacrieur**, contrôle du terrain, placement, dégâts à distance ; **pas** de preuve d'une rotation ou valeur de sort |
| V13-2 | GOATESQUE - DOFUS, [*JOUER LE NOUVEAU CRA AIR MAJ 3.6 : TOUT SAVOIR [DOFUS UNITY]*](https://www.youtube.com/watch?v=Xb3p31R1-J0), **6 juillet 2026**, **6 min** | Métadonnées et langues OK ; transcription `fr` **même erreur de blocage IP** ; images non inspectées | Vidéo 3.6 authentifiée, pertinente pour constater son **antériorité** à la refonte 3.7 ; **aucune règle de sorts attribuée à ses paroles** |
| V13-3 | XAWPIYA, [*DOFUS : Les MEILLEURES TEAMS de 4 en 2026 ! (Guide Complet)*](https://www.youtube.com/watch?v=VCVjlE8pVcI), **8 juin 2026**, **9 min** | Métadonnées/langues OK ; transcription automatique `fr` **294 segments, `next_cursor=null`**, récupérée intégralement. Intervalles analysés **00:50–02:00**, **02:25–03:05**, **04:00–05:10**, **07:00–07:45** | [02:37–02:50](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=157s) rôle dégâts longue portée du Crâ ; [04:26–05:00](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=266s) décrit **Pandawa place/tank, Énutrof ralentit PM, Crâ tire, Féca protège** ; [07:38](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=458s) voies Air/Feu/Terre en progression. **Paroles uniquement** (`declare-video`) ; ce ne sont pas des positions observées ni les PA du plateau. |
| V13-4 | Tenmalexis, [*La MEILLEURE TEAM de 4 sur DOFUS en 2025 ?!*](https://www.youtube.com/watch?v=YFlyjdSo4Ek), **2 mai 2025**, **16 min**, 3.1 annoncée | Transcription FR accessible **225 fragments / curseur nul** ; [08:07–09:10](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=487s), [11:25–12:05](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=685s) | Trio **Panda/Énu/Crâ**, quatrième soutien flexible ; Panda tacler, Énu contrôler les PM, Crâ dégâts. Ancienne version, `declare-video`. |

**Images** : aucune image à l'instant vidéo vérifiée. Le navigateur CoS avait renvoyé `BROWSER_NOT_DISPATCHED` sur l'essai de lecture visuelle, et les vignettes/sites ne prouvent pas les tours. Les échecs de transcript des guides individuels ne sont pas compensés par des citations horodatées inventées.

## Recoupement versionné déterminant

- **S13-1** : [Dofus Grimoire — Crâ, page affichant `MAJ 3.7.12`](https://dofus-grimoire.fr/classes/cra/), consultée **04/11/2026**. Rang 3 : **Flèche de Recul 3 PA, 1–8 PO modifiable, repousse 2 cases**, 3/tour, 2/cible ; **Flèche Cinglante 2 PA, 1–8 PO, retire 2 PM** (réussite effective du retrait à vérifier selon résistance) ; **Flèche Persécutrice 4 PA, 3–10 PO**, effet retardé lorsque cible hors LdV ; **Flèche Harcelante 3 PA, 1–10 PO, sans LdV**. Les coûts/rangs doivent correspondre à la variante choisie et à la cible.
- **S13-2** : même page, **Acuité Absolue 2 PA**, relance **4 tours**, ignore la LdV pour les sorts du Crâ **mais augmente la portée minimale de ses sorts offensifs** ; variantes **Sentinelle/Acuité** exclusives. **Tir de Repli 1 PA, en ligne, recule le lanceur de 2 cases, donne +2 PO** (rang haut), relancer selon limites de rang/cible ; variante Vendetta exclusive. Des cases libres sont nécessaires au recul réel.
- **S13-3** : [Back from AFK — Crâ 3.6 → 3.7](https://backfromafk.com/classes/cra/depuis-3-6/), données présentées **3.7, octobre 2026** : **Flèche de Recul portée maximale 6 → 8**, Cinglante 7 → 8 ; bon contrôle des différences de version. [Changements depuis 3.3](https://backfromafk.com/classes/cra/) indique une refonte importante et des sorts nouveaux/remplacés depuis sept. 2025. Ces tableaux sont indépendants de la vidéo ; ils ne disent pas que les versions 3.7.12 et 3.7.13.17 sont identiques.

## Règles atomiques retenues

| ID | Affirmation | Preuve / statut | Inconnues |
| --- | --- | --- | --- |
| **M13-R01** | La portée géométrique et la ligne de vue sont des gardes distinctes. Un sort « sans ligne de vue » reste contraint par sa PO min/max et ses autres conditions. | `corrobore-source` S13-1/S13-2 ; `hypothese` conformité exacte client local | LdV du plateau, PM et bonus/malus PO au moment du jet, obstables dynamiques |
| **M13-R02** | Rang 3 : recul de **2 cases** sur Flèche de Recul **si déplacement légal**, pour **3 PA** ; portée max **8** dans le guide 3.7, et évolution 6→8 documentée. | `corrobore-source` S13-1/S13-3 | Collision/ordre avec les dégâts, états enraciné/indéplaçable, résistance de poussée, challenge |
| **M13-R03** | Budget solo 12 PA hypothétique : **Cinglante 2 + Persécutrice 4 + Harcelante 3 + Recul 3 = 12 PA** ; déplacer l'ennemi après les tirs modifie le prochain état, non les effets déjà résolus. | Coûts `corrobore-source` S13-1 ; ordre et valeur `hypothese` fixture | Retrait effectif PM, dommages, survivabilité E, Ligne de vue après tir, choix des variantes |
| **M13-R04** | Acuité 2 PA lève la LdV, **augmente la portée minimale**, et entre en compétition PA avec les quatre tirs du R03 ; décision conditionnée par distance. | `corrobore-source` S13-2 ; validation 3.7.13.17 en attente | Modificateurs PO exacts, état de relance, adversaires sur cases adjacentes |
| **M13-R05** | L'équipe 2026 conseille Panda/Énu/Crâ/Féca en rôles distincts ; ne pas partager PA/PM ou attribuer un retrait Énu au Crâ. | `declare-video` V13-3 [04:26](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=266s), V13-4 [08:07](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=487s) | Identités de sorts et coûts exacts des alliés **non établis par les vidéos** |

## Séquence discriminante : solo avec et sans pilier

**Fixture libre, non extraite d'un combat** : Crâ C=(0,0), adversaire E=(5,0), 12 PA/6 PM chacun du côté de C (E ressources inconnues), rang 3 et variantes Cinglante/Persécutrice/Harcelante/Recul disponibles, E mobile, aucun obstacle, LdV vraie, PO non modifiée, cibles vivantes. Toute position est **relative**, pas un cellId.

Plan A (à ciel ouvert) : 1. **Cinglante** E, 2 PA → C 10 PA/6 PM, retrait PM **tenté** ; 2. **Persécutrice** E, 4 PA → C 6 PA/6 PM, état de dégâts retardés conditionnel ; 3. **Harcelante** E, 3 PA → C 3 PA/6 PM, même si LdV change ; 4. **Recul** E, 3 PA → C **0 PA/6 PM**, E **attendu (7,0)** si 2 cases libres et effet confirmé. L'augmentation de distance peut réduire la menace, mais E peut encore agir. Les PV/PM de E sont **à actualiser d'après résultats**, le -2 PM ne doit pas être présumé réussi.

Plan B (pilier opaque à (2,0), LdV fausse) : sans Acuité, **Harcelante 3 PA** est une option explicitement sans LdV, alors que les autres casts du plan A ne peuvent pas bénéficier d'une LdV fictive. Avec **Acuité 2 PA**, la portée minimale de tous sorts offensifs Crâ devient plus élevée : depuis C→E distance 5, plusieurs actions peuvent devenir légales hors LdV si le nouveau minimum le permet. Exemple conditionnel **Acuité 2 → Cinglante 2 → Harcelante 3 → Recul 3** = **10 PA**, 2 restants ; E(5,0)→(7,0) si aucun obstacle **après** E et si Recul se lance réellement. Ne pas supposer qu'Acuité lève l'exigence de **case visable**, ni que le pilier interrompt le recul vers la droite.

**Risque** : Flèche de Recul peut repousser E dans la zone d'un allié ou dans un obstacle et générer une collision à résoudre avec ses paramètres de jeu ; Acuité peut rendre impossible un tir à courte distance à cause de l'augmentation du minimum. **Repli** du Crâ est une autre direction tactique, non interchangeable avec pousser E, et demande une cellule d'arrivée libre. Tout refus/Unknown annule le suffixe utilisant la nouvelle position.

## Extensions solo / duo / trio / quatre ou davantage

| Composition et ordre de tours hypothétique | Ce qui change | Données obligatoires |
| --- | --- | --- |
| **Solo** C | C combine retrait, dégâts et recul selon Plan A/B ; si E menace le corps à corps, garder option Repli ou déplacement en sécurité. | 12 PA/6 PM C fixture, menace E, LdV et chemin réel, résistance retrait |
| **Duo** **Énu → Crâ** (ou **Sacrieur → Crâ**), références V13-1 | Énu peut viser ralentissement avant que C calcule distance ; Sacrieur peut placer une cible **avant** la flèche. Si allié joue **après** C, C doit tirer sur l'état initial. | Identité acteur allié, ses **propres** PA/PM inconnus, effets confirmés avant début C, cellule de E |
| **Trio** **Panda → Énu → Crâ**, décrit en V13-4 | Panda fixe/place, Énu tente retrait PM, puis C répartit attaques et reculs selon nouveau placement ; le recul C peut casser tacle Panda, donc comparer les deux ordres/recalculer. | Positions et états du Panda, succès retrait, LoS et impacts de poussée sur alliés |
| **Quatuor** **Panda → Énu → Féca → Crâ**, V13-3 | Féca protège une cible **avant** les attaques ennemies ; C joue dégâts/contrôle. Si C joue avant Panda, son recul peut défaire le futur placement. | Buff reçu par **quel acteur**, tour de protection, PA/PM individuels, états et délais |
| **5+ personnages** | Ne pas généraliser une séquence rigide ; évaluer pour chaque initiative les cibles alliées pouvant obstruer LdV et les zones où recul est nuisible. | Graphe de dépendance actions, contraintes challenge et chemins |

Les recommandations de groupe ci-dessus ne sont **pas** une rotation de 4 joueurs démontrée en vidéo ; toutes valeurs de ressources des autres classes restent `unknown`.

## Tests et passage au développeur

Les scénarios détaillés et vérifiables se trouvent dans [FICHE_CRA_PORTÉE_LDV_RECUL.md](FICHE_CRA_PORTÉE_LDV_RECUL.md) et [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md). Il faut distinguer : un **cast refusé** (pas d'effet certifié), un **cast confirmé avec déplacement** (positions à jour), et un **ACK/timeout sans état** (`Unknown`; pas de recul simulé comme factuel). `valide-test` et `valide-live` **non attribués**.

**Reprise concrète :** transcriptions V13-1/V13-2 bloquées à l'appel, prochaine tentative seulement si accès renouvelé ; V13-3 et V13-4 intégrales récupérées. Examiner visuellement V13-3 [04:26](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=266s) avec lecteur/capture disponible ; contrôler dans le client 3.7.13.17 la portée et la LdV des variantes, coûts/rangs, seuil min d'Acuité et ordre de résolution des collisions. Pas de preuve visuelle inventée.
