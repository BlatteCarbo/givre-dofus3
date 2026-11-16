# Fiche M04 — sortie d'un sort projeté à travers plusieurs portails Eliotrope

## Identité

- **ID stable : ELI-M04-ROUTAGE-20261104.**
- **Sujet :** route d'entrée → portails intermédiaires → sortie ; projection du sort ; correction par axes/diagonales ; cas limite d'égalité et de fermeture.
- **Jeu :** DOFUS Unity (vidéo de novembre 2025, patch exact inconnu), recoupement guide DOFUS **3.5** (février 2026), référence cible locale annoncée **3.7.13.17** (à confirmer). Pas de transposition automatique à Retro/Touch.
- **Auteur/date de l'analyse :** worker documentaire M04, 4 novembre 2026.
- **Vidéo :** Yossat, « Dofus Unity - Tutoriel Astuces Redirection Eliotrope », https://www.youtube.com/watch?v=PTSHfNPspSc , publiée le 7 novembre 2025, durée de lecteur ~36:22.
- **Transcription :** get_available_languages expose fr/en (et ar, fr auto) ; get_timed_transcript demandé avec **lang=fr**, résultat 320 segments horodatés **en anglais** sur une page ; mode de traduction / origine manuelle non vérifiable. Intervalles exploités : 1:36–10:48, 11:51–16:50, 18:27–20:25, 24:05–26:07, 28:00–28:53 et exemples de combat.
- **Méthodes :** contenu vocal (segments MCP), recoupement Guidactik 3.5 et OpenDofusDB, guide Zekalfra ; **aucun pixel de plateau analysé** malgré tentative d'attachement/screenshot navigateur (BROWSER_NOT_DISPATCHED).

## Règles atomiques et preuves

| ID | Affirmation précise | Statut | Citation précise | Justification / borne |
| --- | --- | --- | --- | --- |
| ELI-M04-01 | Le sort est dirigé vers un portail choisi comme entrée ; la sortie est une conséquence du réseau. | corrobore-source | [Guidactik, section vecteur](https://guidactik.com/dofus/les-meilleurs-stuff-eliotrope-sur-dofus-unity/), [guide Zekalfra « You choose the entry portal »](https://eliotrope.net/) | Ne pas choisir l'entrée automatiquement par proximité du lanceur. |
| ELI-M04-02 | Chaque portail rejoint le prochain plus proche encore admissible jusqu'à la sortie finale. | corrobore-source | [Guidactik, section « portail le plus proche »](https://guidactik.com/dofus/les-meilleurs-stuff-eliotrope-sur-dofus-unity/), [Zekalfra, « The network chooses the route »](https://eliotrope.net/) | Distance et traitement exact des statuts non spécifiés côté serveur. |
| ELI-M04-03 | En cas d'équidistance minimale, le sens horaire départage. | declare-video, corrobore-source | [4:57](https://www.youtube.com/watch?v=PTSHfNPspSc&t=297s), [10:33](https://www.youtube.com/watch?v=PTSHfNPspSc&t=633s), [Guidactik § même distance](https://guidactik.com/dofus/les-meilleurs-stuff-eliotrope-sur-dofus-unity/) | Non prouvé : convention d'angle exact et éventuels ex aequo angulaires. |
| ELI-M04-04 | L'écart du lanceur à l'entrée se reporte sur la sortie pour viser une case. | corrobore-source | [Guidactik § vecteurs](https://guidactik.com/dofus/les-meilleurs-stuff-eliotrope-sur-dofus-unity/), [Zekalfra § Copy the spell direction](https://eliotrope.net/) | Validité du sort, portée, LoS et obstacles doivent ensuite être vérifiés. |
| ELI-M04-05 | Tracer des axes qui se croisent, compter moins de cases puis projeter une ligne guide un placement de portail. | declare-video | [1:36](https://www.youtube.com/watch?v=PTSHfNPspSc&t=96s)–[3:59](https://www.youtube.com/watch?v=PTSHfNPspSc&t=239s) | Cas où l'auteur compte 13 cases ; pas de géométrie écran certifiée. |
| ELI-M04-06 | Les portails diagonaux sont équidistants **depuis certaines cases** de zone ; hors zone, le résultat change. | declare-video | [4:12](https://www.youtube.com/watch?v=PTSHfNPspSc&t=252s)–[5:52](https://www.youtube.com/watch?v=PTSHfNPspSc&t=352s) | La condition « diagonal » seule est insuffisante. |
| ELI-M04-07 | Une intersection de diagonales et une droite de projection produisent une autre ligne de placements, parfois avec deux cases voisines d'intersection. | declare-video | [6:27](https://www.youtube.com/watch?v=PTSHfNPspSc&t=387s)–[9:29](https://www.youtube.com/watch?v=PTSHfNPspSc&t=569s) | Aucune coordonnée de cette ligne n'a été inspectée visuellement. |
| ELI-M04-08 | Une équidistance peut envoyer le tir vers la mauvaise sortie ; décaler le portail d'une case modifie parfois le résultat. | declare-video | [10:19](https://www.youtube.com/watch?v=PTSHfNPspSc&t=619s)–[10:48](https://www.youtube.com/watch?v=PTSHfNPspSc&t=648s) | Confirmer le « avant/après » avec une capture ou un replay. |
| ELI-M04-09 | Des portails hors champ mental du joueur modifient la route ; il faut tous les prendre en compte. | declare-video | [16:28](https://www.youtube.com/watch?v=PTSHfNPspSc&t=988s)–[16:50](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1010s) | Le transcript décrit un tir raté ; aucune image vérifiée de la cause. |
| ELI-M04-10 | L'ancienneté détermine quel portail peut disparaître après nouvelle pose, indépendamment du choix de sortie. | declare-video, corrobore-source | [18:27](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1107s)–[19:13](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1153s), [Zekalfra § Portal order](https://eliotrope.net/) | Id identité stable et numéro courant ne doivent pas être confondus. |
| ELI-M04-11 | Neutral désactive un portail pendant un tour avec remboursement de 1 PA selon catalogue ; un portail fermé ne doit pas être considéré ouvert. | declare-video, corrobore-source | [28:01](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1681s)–[28:53](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1733s), [OpenDofusDB Neutral](https://openddb.dev/spells/14582-neutral) | Instant précis de réactivation à contrôler par événements de tour. |
| ELI-M04-12 | Selon Yossat, Sillage ferme les portails d'entrée et de sortie qu'il utilise ; préparer une nouvelle route avant de le lancer. | declare-video | [25:45](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1545s)–[26:07](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1567s) | Effet dépendant du sort/variant/version ; fiche sort indépendante requise. |
| ELI-M04-13 | La ligne de vue du segment de tir peut être bloquée sans changer le calcul de proximité entre portails. | corrobore-source, hypothese de séparation algorithmique | [Zekalfra § entry/LoS](https://eliotrope.net/), [Guidactik § vecteurs](https://guidactik.com/dofus/les-meilleurs-stuff-eliotrope-sur-dofus-unity/) | Modèle de collision exact non vérifié ; ne pas fusionner « bloqué » et « fermé ». |
| ELI-M04-14 | Le joueur peut employer un portail comme obstacle de passage et des invocations peuvent gêner un usage du portail. | declare-video | [24:18](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1458s)–[24:43](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1483s), [29:35](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1775s), [31:19](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1879s) | État de l'unité / cellule exacte et différence déplacement-sort non observés. |

Tous les éléments de la table sont des **déclarations vidéo** ou des **recoupements communautaires** ; aucun n'a reçu le statut observe-video, valide-test ou valide-live.

## État initial reproductible (fixture proposée)

- **Acteur :** 1 Eliotrope allié L, avec un sort projectible non nommé, dont coût, rang et préconditions doivent être chargés d'une source client avant exécution ; ennemi témoin T immobile. Aucun compagnon ni ordre de tours utile dans la fixture géométrique pure.
- **Portails, grille 2D relative candidate :** E=(0,0), A=(1,3), B=(4,2), tous ouverts ; ordre de placement explicite E plus ancien, A puis B, à ne pas confondre avec la route. Lanceur L=(-2,0), cible théorique T=(6,2).
- **Distance d'essai :** d(P,Q)=|Px-Qx|+|Py-Qy| ; ceci est une **hypothèse calibrable** sur la numérotation logique des cellules, pas un extrait vidéo. d(E,A)=4, d(E,B)=6, d(A,B)=4.
- **Terrain :** vide et sans ligne de vue obstruée dans le cas positif. Pour le négatif LoS, mur occultant en (5,2). Pour le négatif fermeture, B indisponible.
- **PA/PM/PO :** non chiffrés pour le sort d'attaque ; vérifier légalité du lancer de L vers E et de B vers T, rayon et portée. Un portail placé par le sort Portail coûte 1 PA dans le catalogue S3, mais ce n'est **pas** une preuve des dépenses dans l'exemple de Yossat.
- **Tour, états, variantes :** pas de Neutral/Sillage dans le cas positif ; dans les négatifs, transition de fermeture explicite et durée issue de l'événement de jeu. Sillage et Errance non supposés équipés à la fois.

## Séquence et transitions

| Étape | Acteur | Action / cellule | Préconditions | Coût | Effet immédiat et état suivant | Effet différé / ordre | Preuve |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | État | Charger E,A,B ouverts, L et T | Trois portails connus, révision d'état fixée | aucun | Ensemble de portails ouvert {E,A,B} | Aucune action serveur | fixture hypothétique |
| 1 | Simulateur | Choisir E comme entrée ciblée | Portail E ouvert et sort légal depuis L | calcul | E est début du chemin | Respecter la vraie cible du lanceur | ELI-M04-01 |
| 2 | Simulateur | Calculer prochain depuis E | Candidats A,B ouverts | calcul | A choisi (4 contre 6 sous métrique d'essai) | Marquer E parcouru | ELI-M04-02 |
| 3 | Simulateur | Calculer depuis A | B reste ouvert/non parcouru | calcul | Route E→A→B ; sortie B | Pas de priorité de numéros | ELI-M04-02 |
| 4 | Simulateur | Projeter vecteur L→E = (+2,0) sur B | Sort compatible, cases et LoS valides | calcul | Tprévu=B+(2,0)=(6,2) | Aucun dégât déduit du routage seul | ELI-M04-04 |
| 5 | Eliotrope | Émettre le sort vers E, sous autorisation du plan | PA, portée, LoS, variante et état réels confirmés | coût du sort **inconnu** | **Prédiction** de frappe T ; pas une réussite confirmée | Exiger acceptation/résultat serveur et réconciliation des ressources | non exécuté |
| N1 | Eliotrope | Activer Neutral sur B, puis tenter le même tir | Cible et PO de Neutral légales ; B encore ouvert avant l'action | Neutral 1 PA puis +1 PA d'après S3 | B devient fermé pour un tour selon S3 ; projection à recalculer | Réactivation par événement effectif ; ne pas la deviner à l'horloge locale | ELI-M04-11 |
| N2 | Simulateur | Recalculer après N1 | E,A ouverts, B fermé | calcul | Route E→A ; sortie A ; projection nouvelle (3,3) | Tir prévu sur (6,2) désormais invalide | ELI-M04-11 |
| N3 | Eliotrope | Essayer une cible via B avec mur entre B et T | E,A,B ouverts et LoS exigée | aucun si refus avant émission | Chaîne géométrique inchangée ; cible T non légale | Le refus ne prouve pas un autre portail choisi | ELI-M04-13 |

**Remarque :** les étapes 0–4 et N2 sont des calculs attendus **sous une métrique non encore validée**. L'étape 5 et les étapes négatives sont des scénarios futurs, aucun lancement n'a eu lieu.

## Intérêt tactique et limites

- **Objectif :** établir une ligne de frappe vers une cible éloignée sans marcher jusqu'à elle ; générer une alternative de placement plus rentable que le simple placement aléatoire de deux portails.
- **Coût du calcul :** au plus quelques portails selon la source pédagogique ; un calcul exhaustif simple est préférable au raccourci « diagonale » lorsqu'un obstacle ou une égalité peut tromper l'heuristique.
- **Bénéficiaire :** l'Eliotrope qui tire, ou un allié qui projette un sort ; ne pas attribuer les PA d'un allié au lanceur. Les multiplicateurs de dégâts ne sont pas établis par une mesure de cette vidéo.
- **Préparation :** un placement nouveau modifie la file d'ancienneté, puis la route ; Sillage/Neutral peuvent désactiver un sous-ensemble ; la disponibilité doit être réévaluée à chaque événement.
- **Risques :** un ennemi peut tirer parti d'un réseau ouvert, le portail le plus ancien peut disparaître, la LoS de sortie peut être bloquée et une diagonale involontaire peut déclencher un mauvais départage. Yossat recommande de considérer l'exposition et parfois de se repositionner plutôt que d'attaquer [21:13](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1273s)–[21:55](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1315s).
- **Repli :** si la nouvelle route ne permet pas le tir, n'émettre aucune attaque sur une cible supposée ; privilégier placement/sécurité ou Neutral lorsqu'il est légal et tactiquement avantageux.
- **Dégâts/boosts :** pas de valeur de dégâts prédictive validée par S1 ; les 800 observés verbalement dans le combat [34:10](https://www.youtube.com/watch?v=PTSHfNPspSc&t=2050s) ne prouvent ni formule ni répétabilité.

## Scénarios de tests proposés (non exécutés)

| Cas | Entrée / actions | Résultat observable attendu | Distingue | Statut |
| --- | --- | --- | --- | --- |
| T-M04-01 positif | E=(0,0), A=(1,3), B=(4,2), L=(-2,0) ; lancer un sort compatible vers E, aucun obstacle | Route théorique E→A→B, sortie B, T=(6,2), sous métrique candidate et conditions de sort confirmées | Sortie déterministe vs simple proximité de la cible | proposé, non exécuté |
| T-M04-02 égalité | E=(0,0), A=(2,2), B=(2,-2), puis déplacer B=(3,-2) dans une deuxième fixture | Avant : choix dicté par une **référence horaire à calibrer** ; après : A plus proche strictement et doit être choisi d'abord | départage horaire vs distance vs ancienneté | proposé, non exécuté |
| T-M04-03 négatif LoS | Même E,A,B que T01, mur en (5,2), sort nécessitant LoS | Route théorique inchangée, tir sur T refusé ; refaire sans obstacle | obstacle de projectile vs algorithme de route | proposé, non exécuté |
| T-M04-04 négatif fermé | T01, Neutral sur B **confirmé** avant le calcul suivant | B indisponible ; sortie A et destination théorique (3,3), jusqu'à événement de réouverture | fermeture vs portal encore présent | proposé, non exécuté |
| T-M04-05 âge | Quatre portails d'identités stables puis pose d'un cinquième ; comparer numéros et ordre de route | Ancien portail supprimé si la règle d'âge est confirmée ; route recomputée, jamais triée par âge | ordre de création vs ordre de parcours | proposé, non exécuté |
| T-M04-06 Sillage | Deux portails utilisés par Sillage puis seconde attaque sans reconstruire | Portails annoncés fermés ; seconde projection requiert recomputation / rejet | effets secondaires et ordre des sous-actions | proposé, non exécuté |
| T-M04-07 obstacle de marche | Portail disponible pour projection mais case de passage occupée par invocation ; tester déplacement et sort séparément | Déplacement et sort peuvent avoir des préconditions différentes ; **résultat exact à établir** | obstruction de cellule vs LoS vs fermeture | proposé, non exécuté |

## Passage au développeur

- **Données** : identifiant stable de portail, cellule, âge, ouvert/fermé et durée effective, propriétaire, révision du tour ; lanceur, entrée explicitement ciblée, sort/variante, portée/LoS, obstacles et occupation.
- **Transitions** : pose/replace → mise à jour âge/portails ; Neutral/Sillage/déplacement → fermetures ; ouverture en événement de tour ; recalcul de route et projection après **chaque mutation confirmée**.
- **Calcul possible avec ces seules preuves** : squelette de routage ; tests d'équivalence géométrique sous métrique candidate ; invalidation conservatrice du plan.
- **Preuves encore nécessaires** : vraie distance sur coordonnées de cellule du client cible, départage horaire exact, étendue LoS et blocages, sort Sillage dans 3.7, signaux de confirmation/réouverture.
- **Priorité** : haute pour l'IA de combat Eliotrope ; une erreur de destination peut gaspiller un sort, révéler le personnage ou offrir un portail à l'adversaire. Test offline via fixtures puis validation live séparée uniquement si autorisée ultérieurement.

## Reprise

Dernière source principale entièrement paginée (320 snippets, next_cursor=null), extrait pertinent final 35:12 ; aucun écran de carte validé. Complément S5 du 25 janvier 2026 intéressant à [1:41](https://www.youtube.com/watch?v=EtmnyszY8Ck&t=101s), mais transcript MCP inaccessible. Capturer les cellules de [4:36](https://www.youtube.com/watch?v=PTSHfNPspSc&t=276s), [6:27](https://www.youtube.com/watch?v=PTSHfNPspSc&t=387s) et [10:33](https://www.youtube.com/watch?v=PTSHfNPspSc&t=633s), puis comparer au calcul client 3.7.13.17. 
