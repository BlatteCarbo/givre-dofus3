# M16 — Enutrof DOFUS 3 : retrait PM, esquive, initiative et survie

**Date :** 2026-11-04. **Cas central :** Enutrof Eau à 12 PA contre une menace de mêlée à 6 PM. Séquence Orpaillage→Obsolescence→Pelle Aurifère→Maladresse ; deux contres : retrait totalement/partiellement esquivé, ennemi qui agit avant l'Enutrof. Branche de défense avec Sac Animé.

## Vidéos identifiées et protocole d'accès

| ID | Source | Métadonnées MCP vérifiées | Transcription / limite |
| --- | --- | --- | --- |
| V16-A | [Yusonara — Enutrof dans Dofus : Guide de Classe 🌀💡](https://www.youtube.com/watch?v=rKGGoHvxevI) | Vidéo authentique, 2025-05-29, 7 min, guide des voies élémentaires, rôles d'équipe et synergies ; FR automatique annoncé par `get_available_languages` | `get_timed_transcript(lang=fr)` : **échec « YouTube is blocking requests from your IP »**. Page ouverte dans navigateur externe, description développée, clic sur « Afficher la transcription » ; panneau toujours en **loading** et 0 segment. Aucun passage horodaté récupéré |
| V16-B | [GOATESQUE — ENUTROF DOFUS UNITY - 9 MINUTES POUR TOUT COMPRENDRE (TUTO DOFUS 3)](https://www.youtube.com/watch?v=GGbqdb_Ynv0) | Vidéo authentique, 2024-11-26, 10 min, chapitres **00:00 intro, 03:19 Air, 04:35 Eau, 05:55 Terre, 07:18 Feu**, via **description horodatée fournie par l'auteur** ; FR automatique annoncé | `get_timed_transcript(lang=fr)` : même erreur YouTube IP ; chapitres ne sont **pas** transcription ni preuve de règle en 3.7 |

**Reprise précise MCP :** aucun segment reçu, `next_cursor` non disponible, aucun intervalle oral examiné. Si l'accès revient, interroger V16-A en `fr` en entier et au minimum V16-B 04:35–05:55 ; corriger alors les règles avec des paroles réellement attestées et les versions. Aucun `declare-video` de règles de sorts, aucun `observe-video`. Les deux vidéos précèdent la mise à jour 3.7 (oct. 2026) : elles servent de **repères historiques de gameplay**, jamais de validation des valeurs actuelles.

## Sources 3.7 recoupées

| ID | Page / version | Données utilisables | Qualification |
| --- | --- | --- | --- |
| S16-1 | [Dofus Grimoire — Enutrof](https://dofus-grimoire.fr/classes/enutrof/) ; version affichée **3.7.12**, correctif 20 octobre 2026 | Maladresse 1 PA (retrait jusqu'à 2 PM), Pelle Aurifère 4 PA (jusqu'à 3 PM), Obsolescence 3 PA (-20 Esquive PM), Orpaillage 4 PA (état à déclencheurs jusqu'à 4), Sac Animé 2 PA, tours de 12 PA | Source communautaire utilisant données DofusDB ; `corrobore-source` pour 3.7.12 |
| S16-2 | [Back from AFK — Enutrof, 3.6→3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/), finale **3.7 octobre 2026** | 2 sorts modifiés depuis juin : Pelle de Fortune donne maintenant +10 Retrait PM, +10 Retrait PA, +40 Puissance, +10 Fuite, durée 2 tours ; Ruée vers l'Or clarifie la condition de **fin de tour du bénéficiaire** | Diff versionné, non test serveur |
| S16-3 | [Dofus pour les Noobs — Retrait et Esquive](https://www.dofuspourlesnoobs.com/retrait-et-esquive.html) | Explique un retrait opposé à l'esquive cible et la réduction de chances lorsque les PA/PM ont déjà été retirés ; formule ancienne sagesse/équipement | Guide sans date/version 3.7 démontrée : `hypothese` pour probabilités, arrondis et formule, **ne pas coder la formule comme oracle** |
| S16-4 | [Back from AFK — Enutrof 3.5→3.7](https://backfromafk.com/classes/enutrof/depuis-3-5/) | Diff signale modifications intermédiaires, dont Orpaillage, Boîte de Pandore et autres sorts | Met en évidence l'écart entre tutoriel 2025 et règles 3.7 |

## Règles atomiques documentées

| ID | Règle exploitable | Preuve et statut | Ce qui reste inconnu |
| --- | --- | --- | --- |
| **M16-R01** | Maladresse **1 PA**, portée haute jusqu'à 12 PO : **jusqu'à -2 PM tentés**, compatible avec la voie Eau ; plusieurs lancers par tour sur cibles différentes selon exemple de la fiche. | [S16-1, Maladresse](https://dofus-grimoire.fr/classes/enutrof/) — `corrobore-source` | Esquive cible, plafond réel de PM perdus, lanceurs/rangs, règles 1/cible |
| **M16-R02** | Pelle Aurifère **4 PA**, Eau, jusqu'à **-3 PM tentés** sur cible, dégâts Eau indépendants. | [S16-1, Pelle Aurifère](https://dofus-grimoire.fr/classes/enutrof/) — `corrobore-source` | PM perdus réellement, jets, déclencheurs par événement |
| **M16-R03** | Obsolescence **3 PA**, Eau, applique **-20 Esquive PM** ; ordre à viser avant les tentatives de retrait. | [S16-1, Obsolescence](https://dofus-grimoire.fr/classes/enutrof/) — `corrobore-source` | Application/expiration du malus, proportion du gain réel de succès |
| **M16-R04** | Orpaillage **4 PA** donne un état dont les dommages annexes réagissent aux tentatives de retrait PM, **quatre déclenchements maximum** selon fiche et relance de trois tours. | [S16-1, rotation Eau](https://dofus-grimoire.fr/classes/enutrof/) — `corrobore-source` | Déclenchement sur **tentative esquivée** ou seulement sur retrait réussi : à vérifier ; cible(s) et reset des charges |
| **M16-R05** | Sac Animé **2 PA** : invocation qui intercepte des dommages destinés aux alliés dans sa zone ; une attaque alliée sur objet animé peut soigner ce même allié selon passif. | [S16-1, Sac Animé / Trésor d'Enutrof](https://dofus-grimoire.fr/classes/enutrof/) — `corrobore-source` | Aire, ordre d'interception, PV du Sac, cible, dégâts collatéraux et provenance |
| **M16-R06** | Ruée vers l'Or donne des PM à **un allié**, bonus conditionnel de Puissance/soin évalué si le bénéficiaire termine son tour au contact de l'ennemi/invocation selon le cas. | [S16-2, diff 3.6→3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/) — `corrobore-source` | Phase suivante précise, identité d'invocation, état de fin de tour |
| **M16-R07** | Pelle de Fortune gagne notamment **+10 Retrait PM pendant 2 tours** dans la 3.7. | [S16-2, diff 3.6→3.7](https://backfromafk.com/classes/enutrof/depuis-3-6/) — `corrobore-source` | À qui s'applique exactement le bonus, réapplication, statut et phase. Variante de Retraite Anticipée : deck exclusif |
| **M16-R08** | Plus le retrait déjà appliqué a entamé le PM de la cible, plus le calcul peut rendre l'entrave supplémentaire difficile selon un guide historique. | [S16-3](https://www.dofuspourlesnoobs.com/retrait-et-esquive.html) — `hypothese` pour 3.7 | Formule de probabilité, bornes, taux arrondis, référence officielle courante |

## Conclusions de conception
Le moteur doit stocker **tentative de retrait** et **retrait effectif** séparément, par sort/targetId. Afficher « jusqu'à -5 PM » n'autorise pas à supprimer automatiquement cinq PM. La cible peut conserver 1 à 6 PM (ou autre nombre selon buffs/états), et doit être reconsidérée comme dangereuse si elle peut atteindre un allié, lancer à distance ou se téléporter malgré les pertes. Une stratégie d'entrave efficace mais coûteuse en PA laisse moins de ressources pour le **Sac Animé**, dont l'interception est une branche défensive, avec coût et coordonnées propres.

**Initiative :** E=Enutrof avant X menace ; retirer des PM avant que X agisse peut réduire sa capacité à atteindre D. Si X joue avant E, l'entrave ne protège pas rétroactivement D des dégâts déjà reçus. La protection dépend aussi du séquencement d'un placeur et d'un soigneur alliés ; leurs ressources restent indépendantes.

**Duos/trios/quatuors :**
- Solo Enutrof : prioriser distance et état de menace, Sac Animé si interception nécessaire ; jamais assurer « aucune attaque subie » du seul affichage retrait PM.
- Duo Enutrof–Crâ : entrave avant tour ennemi facilite le tir à distance si cible toujours atteignable ; bénéfice dépend portée/obstacles.
- Duo Enutrof–Iop/Sacrieur : si entrave totale maintient une cible hors mêlée, elle peut aussi priver le frappeur de l'accès ; Ruée vers l'Or sert à résoudre ce compromis **avec effet fin de tour** validé.
- Duo Enutrof–Eniripsa : Enutrof réduit les déplacements adverses, Eniripsa soigne la cible exposée ; quand le retrait est esquivé, plan de soin doit être recalculé avant l'attaque ennemie.
- Trio Enutrof–placeur–DPS : ordre `Enu→placeur→DPS` aide à contrôler puis concentrer les dégâts ; ordre inverse décale l'intérêt du placement.
- Quatuor Enutrof–tank–placeur–DPS : l'entrave limite l'approche, mais le tank doit parfois attirer/occuper. Les PM bonus Ruée vont à une identité précise, non à l'ensemble de l'équipe.

## Deux contres prioritaires
1. **Esquive :** X démarre à 6 PM et à forte Esquive PM, Maladresse/Pelle Aurifère tentent -5, mais le serveur valide seulement -1 ou 0. Le bot **doit conserver les PM réels** et calculer la menace de X, pas forcer X=1 PM.
2. **Initiative :** X agit et frappe D **avant** le tour de l'Enutrof ; une Maladresse réussie ensuite n'efface pas les dégâts du tour de X. Priorité au soin/protection pour le prochain cycle, si disponibles.
3. **Cible :** X1 à 6 PM près du DPS, X2 loin ou immunisé ; Orpaillage/Obsolescence sur X2 puis Maladresse sur X1 peut perdre la synergie, et un retrait appliqué sur un invocateur sans déplacer son invocation ne neutralise pas cette dernière.

## À vérifier avant intégration
Aucune formule probabiliste fiable version 3.7 ni journal serveur n'a été reçu. Obtenir transcriptions horodatées (aujourd'hui bloquées), fiches des sorts depuis le client réel, réels `effectId`, lignes d'acceptation/refus et résultats `-N PM`, états résistants/inesquivables, charges Orpaillage, interception du Sac et expiration de Pelle de Fortune. Aucun test ni jeu exécuté ; `valide-test` et `valide-live` exclus.

