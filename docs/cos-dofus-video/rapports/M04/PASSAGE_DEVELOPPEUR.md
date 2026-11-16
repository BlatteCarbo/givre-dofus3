# M04 — Passage au développeur GivreClone

**Objet strict :** implémenter ultérieurement une prédiction fiable de la **route d'un tir Eliotrope à travers plusieurs portails**, sensible à l'équidistance, aux portails fermés et aux obstacles. Ce document livre une **spécification de recherche**, pas du code branché, ni un test passé. Rapport d'origine : [RAPPORT.md](./RAPPORT.md) ; mécanique : [FICHE_ELIOTROPE.md](./FICHE_ELIOTROPE.md).

## Trois règles prioritaires et leurs preuves

**P1 — L'entrée est ciblée, la chaîne suit ensuite le plus proche encore admissible.** Guidactik, guide versionné DOFUS 3.5 du 17/02/2026, https://guidactik.com/dofus/les-meilleurs-stuff-eliotrope-sur-dofus-unity/ , sections « Comment lancer ... » et « Comment savoir ... ». Le guide Zekalfra https://eliotrope.net/ corrobore la distinction entre portail d'entrée explicitement visé et route choisie par le réseau. Modèle candidat : à partir du portail d'entrée ouvert, sélectionner le portail ouvert non parcouru dont la **distance client** est minimale, puis répéter jusqu'au dernier. Une image d'un raccourci de Yossat n'est pas une preuve de la métrique exacte.

**P2 — Équidistance : départage horaire, susceptible de renverser la sortie.** Yossat [4:04](https://www.youtube.com/watch?v=PTSHfNPspSc&t=243s)–[5:06](https://www.youtube.com/watch?v=PTSHfNPspSc&t=306s), puis [10:19](https://www.youtube.com/watch?v=PTSHfNPspSc&t=619s)–[10:48](https://www.youtube.com/watch?v=PTSHfNPspSc&t=648s), recoupé par Guidactik §« même distance ». L'auteur décrit une zone dans laquelle deux portails diagonaux sont équidistants d'un troisième, puis une mauvaise sortie corrigée en décalant la pose d'une case. Une règle booléenne « portails en diagonale => sortie fixe » serait fausse. Il faut étalonner le départage horaire exact sur le client avant une simulation déterministe.

**P3 — Tout changement de disponibilité, âge ou terrain impose de revalider chemin ET sort.** Yossat [16:28](https://www.youtube.com/watch?v=PTSHfNPspSc&t=988s)–[16:50](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1010s) (portail oublié), [18:27](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1107s)–[19:13](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1153s) (Portail 1/Errance), [25:45](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1545s)–[26:07](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1567s) (fermeture des deux portails annoncée avec Sillage) et [28:01](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1681s)–[28:53](https://www.youtube.com/watch?v=PTSHfNPspSc&t=1733s) (Neutral). OpenDofusDB https://openddb.dev/spells/14582-neutral confirme un effet de désactivation pendant un tour et un coût de 1 PA remboursé dans ses données actuelles. Réduire le jeu de portails ouverts avant de recalculer ; **séparer** refus par LoS/occupation et suppression logique d'un portail.

## Contrat fonctionnel à proposer au simulateur

**Entrées de calcul, toutes avec révision d'état :**

- Portails : id stable, cellId, coordonnées de grille transformées, propriétaire/équipe si pertinent, ordre de création (distinct de l'ordre de route), statut ouvert/fermé, effet/durée et déclencheur de réouverture, provenance serveur et version d'observation.
- Lanceur et sort : actorId, cellule d'origine, spellId/variantId/rang, cible initiale **portail d'entrée** ou cellule, caractéristiques exactes de portée et de LoS, effet porté par le portail, PA avant lancer et ordres d'actions.
- Carte : passabilité, occupation temporaire par combattants/invocations, opacité pour ligne de vue, règles de ciblage de cellule et topologie de la grille. Ne pas utiliser une image de carte à la place des cellId client.

**Sortie du calcul :**

- Portail d'entrée validé, liste des identifiants parcourus dans l'ordre, dernière sortie, vecteur lancer→entrée, case de projection théorique, statut du cast (possible / impossible / **Unknown**), raisons détaillées (portail fermé, portée, LoS, cellule, sort incompatible, métrique horaire inconnue), empreinte de la révision d'état.
- Aucune conclusion « attaque réussie » ou « dégâts appliqués » ne peut provenir de cet objet ; requérir résultat autoritatif du serveur et réconciliation acteur/PA/position/états.

**Règles de transitions :**

1. Recevoir une pose confirmée ; ajouter/remplacer l'identité de portail et recalculer numéros d'âge et réseau. La vidéo décrit la disparition de l'ancien « Portail 1 » : vérifier le plafond/variant courant sur client cible.
2. Sur Neutral, Sillage ou déplacement par portail confirmé, appliquer **seulement** les ouvertures/fermetures effectivement prévues par la version du sort ; conserver chaque id et son échéance. Un portail fermé peut rester visible et ultérieurement se rouvrir.
3. À chaque événement de réouverture de tour, résurrection/disparition/occupation de cellule ou mutation du réseau, invalider la projection mise en cache et relancer la validation de trajectoire.
4. Avant émission du sort, recontrôler PA, portée, LoS d'entrée et de sortie, occupation/cellules, variante, disponibilité de tous les portails, ordre de parcours et changement d'acteur. Un échec/Timeout/Unknown interdit de supposer la transition réalisée.

**Algorithme de recherche candidat, sous réserve de calibration :**

    etat := snapshot_autoritatif()
    entree := portail_explicitement_cible_et_ouvert(etat)
    candidats := tous_portails_ouverts(etat) \ {entree}
    route := [entree]
    tant_que candidats non vides :
        m := minimum_de_distance_client(portail_courant, candidats)
        proches := {p parmi candidats | distance_client(p, courant) == m}
        prochain := unique(proches) OU departage_horaire_client(proches, courant)
        ajouter(route, prochain) ; enlever(candidats, prochain)
        courant := prochain
    sortie := dernier(route)
    cellule_projetee := projeter_vecteur(lanceur, entree, sortie)
    legalite := verifier_sort_et_deux_segments_LOS_et_terrain(...)
    retourner(route, sortie, cellule_projetee, legalite, revision)

Ne pas fixer distance_client à Manhattan ni le départage à un simple tri des angles tant que des échantillons déterminants du client 3.7 ne l'ont pas confirmé. Aucun chemin si moins de deux portails ouverts lorsque la règle de traversée exige une paire. Le contenu de candidats doit être calibré pour les portails accessibles à **cet acteur** et **ce sort**, sans supposer qu'un portail fermé est simplement un obstacle de LoS.

## Cas positifs / négatifs discriminants

| Test proposé | Fixture et actions | Observable attendu | Pourquoi |
| --- | --- | --- | --- |
| **P01 trois portails** | E=(0,0), A=(1,3), B=(4,2) ouverts ; caster=(-2,0), entrée E, terrain vide, sort projectible dont portée/LoS conviennent ; métrique d'essai L1 | Route candidate E→A→B ; vecteur (+2,0) ; sortie B ; case cible (6,2). Le client devra confirmer. | Ne permet pas de confondre portail le plus proche du lanceur et du portail courant. |
| **N01 égalité puis rupture** | E=(0,0), A=(2,2), B=(2,-2), candidate L1 : deux distances 4 ; second essai B=(3,-2), distance 5 | Cas égal : choix exact à **mesurer** sur le client ; après décalage : A est prochain, indépendamment de l'âge. | Distingue distance stricte, départage horaire et ordre de création. |
| **N02 occultation de sortie** | Reprendre P01 ; occulter cellule (5,2) et exiger LoS entre sortie B et cible (6,2) | Chemin géométrique prédit inchangé ; tir **invalide** si le moteur de LoS confirme l'obstruction. | Distingue sélection de portail et légalité du sort. |
| **N03 fermeture Neutral** | Reprendre P01 ; fermer B avec une action effectivement confirmée | Réseau admissible E,A ; sortie A, projection (3,3) ; ancienne cible (6,2) n'est plus déduite. | Distingue portail fermé et portail visible encore présent. |
| **N04 réactivation / tour** | Neutral sur B ; passage d'un événement de tour où l'effet expire | À la réception de l'événement de réouverture, route à recalculer ; ne rien rouvrir sur simple délai mural. | Empêche une route stale au tour adverse. |
| **N05 blocage de case** | Invocateur bouche la case d'un portail ; comparer entrée d'un personnage et projection d'un sort | Résultats à **mesurer séparément** et à encoder selon type d'action ; pas de présupposition d'échec commun. | « Bloqué par une pelle » de Yossat ne suffit pas à établir une règle de LoS. |
| **N06 Sillage** | Réseau ouvert avant Sillage ; lancer légal, puis seconde attaque envisagée sans nouvelle pose | Sur confirmation des fermetures d'entrée/sortie annoncées, invalider le réseau et le second tir. | Vérifie l'ordre des effets successifs. |
| **N07 cinquième portail** | Quatre portails ouverts, puis nouvelle pose, révision d'âge/ID client | Ancien portail supprimé si règle confirmée et route recomputée ; numéro de pose ne devient jamais rang de route. | Écarte le tri naïf par âge. |
| **N08 refus d'action** | Tentative de pose ou fermeture refusée/unknown, ancienne route préparée | Conserver état confirmé, interdire mise à jour optimiste durable ; relecture serveur avant le plan suivant. | Empêche de transformer une commande envoyée en effet accepté. |

**Exécution : aucun de ces tests n'a été lancé ; statut « proposé, non exécuté », aucun rapport de réussite, aucun nombre de tests PASS/FAIL.** Les coordonnées ne sont pas celles de Yossat ; ce sont des **fixtures de grille abstraite**. Les configurations réelles de LoS, variantes et PA doivent être fournies avant un replay valide.

## Ordre d'intégration suggéré et blocages

1. **Priorité haute :** représentation d'état par identités stables, disponibilités et révisions ; parcours + cible projetée déterministes si et seulement si la distance et l'égalité sont calibrées. Bénéfice direct : éviter les attaques sur sortie erronée.
2. **Priorité haute :** validation indépendante de LoS/obstacles/occupations, avec cause d'échec précise. Bénéfice : ne pas interpréter un tir impossible comme un autre routage.
3. **Priorité moyenne :** générateurs de candidats à pose sur les axes/diagonales exposés par [2:00](https://www.youtube.com/watch?v=PTSHfNPspSc&t=120s) et [6:27](https://www.youtube.com/watch?v=PTSHfNPspSc&t=387s). Toujours valider chaque candidat par le calcul complet ; aucune « zone noire » estimée depuis des sous-titres.
4. **Priorité haute :** révision des plans après Neutral/Sillage/Errance et après refus ; valider la chronologie des effets avec la définition des sorts de la **version cible**.
5. **Révision obligatoire avant fusion :** compatibilité 3.7.13.17 (la source vidéo est de 2025 ; Guidactik précise 3.5), distance de grille, définition horaire exact, collisions/LoS y compris coins, effet Sillage et moment de réouverture, observables serveur et données de simulation.

Une capture de vidéo n'a pas été inspectée au niveau des pixels et la vidéo de clarification https://www.youtube.com/watch?v=EtmnyszY8Ck&t=101s n'a pas livré de transcript. Pour valider les cas d'égalité, récupérer des snapshots de placement réels et comparer le simulateur à la sortie affichée / à la réponse serveur dans un environnement **ultérieurement autorisé**. Jusqu'alors, retourner **Unknown** si la règle de tie-break ou de collision est indéterminée.

**Statuts probatoires :** declare-video, corrobore-source et hypothese uniquement. Pas de observe-video, valide-test, valide-live.
