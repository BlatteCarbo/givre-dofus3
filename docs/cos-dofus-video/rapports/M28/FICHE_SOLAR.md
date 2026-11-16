# Fiche M28 — Solar : automate de phase donjon / Songes 3.7

## Identité / preuve

- **ID stable : M28-SOLAR-CYCLE-20261104**. Auteur : recherche M28, 4 novembre 2026. Cible : DOFUS Unity **3.7** ; référence locale 3.7.13.17 **non vérifiée sur client**.
- **Modes** : Tour de Solar (donjon), Songes infinis avec Solar (3.7), et séparément Expédition de guilde (Audace/Bravoure). Le changement de l'horloge de Solar en Songes ne s'applique pas au donjon selon les notes 3.7.
- **Version 3.7 :** [Dofus pour les Noobs — MAJ 3.7, 4 octobre 2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) (S1) ; [Tour de Solar, dernière MAJ 5 octobre 2026](https://www.dofuspourlesnoobs.com/tour-de-solar.html) (S2).
- **Guide additionnel 2026** : [Dofuserie Tour de Solar](https://www.dofuserie.com/donjons/tour-de-solar/) (S3), actualisé octobre 2026 ; [Dofus Grimoire boss Solar](https://dofus-grimoire.fr/boss/?q=Solar) (S4), synthétique.
- **Vrai tutoriel 2026** : [Deplai — FARM 100 DONJONS SOLAR ... TEAM SADI 3.6](https://www.youtube.com/watch?v=3JEP1fidMkw), **15 juin 2026**, **9 min**, chapitre de description « SALLE BOSS » [03:15](https://www.youtube.com/watch?v=3JEP1fidMkw&t=195s). get_video_info/get_available_languages OK, uniquement en auto déclaré ; get_timed_transcript(lang=fr) **refusé par YouTube (IP bloquée)**. **Aucune parole ou image du plateau inspectée**, 3.6 et non Songes 3.7. Méthodes : recoupement pages 3.7 et métadonnées MCP.

## Registre des règles atomiques

| ID | Assertion précise | Preuve et statut |
| --- | --- | --- |
| SOL-M28-01 | Quatre états du boss Aurore, Zénith, Crépuscule, Nadir. Sans avance forcée, le guide du donjon donne **trois tours** pour les trois premiers, Nadir à partir du **tour 10**. | [S2 §Principe](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source en donjon |
| SOL-M28-02 | Aurore / Zénith / Crépuscule : Solar invulnérable. Nadir : Solar vulnérable. | [S2](https://www.dofuspourlesnoobs.com/tour-de-solar.html), [S4](https://dofus-grimoire.fr/boss/?q=Solar) ; corrobore-source |
| SOL-M28-03 | **Songes infinis en 3.7** : changement d'état **au tour global** et non plus au tour de Solar. | [S1 §Solar](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; corrobore-source 3.7 |
| SOL-M28-04 | **Donjon Tour de Solar** : changement au **tour du boss** dans la mécanique décrite ; la modification 3.7 est expressément réservée aux Songes. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [S2](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source |
| SOL-M28-05 | Sur le boss, Nécronyx permet un saut d'état **au prochain moment de transition** (S2 dit prochain tour de Solar dans le donjon) ; ne saute qu'un état par effet. | [S2 §Principe](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source donjon, déclencheur précis Songes **unknown** |
| SOL-M28-06 | En 3.7, **Nécronyx ne fait plus passer le tour** au personnage lanceur. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) et [S2](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source 3.7 |
| SOL-M28-07 | Les **monstres ressuscités** ne sont plus invulnérables 1 tour après la résurrection en 3.7. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; corrobore-source, distinct de l'invu naturelle de Solar |
| SOL-M28-08 | La mort **de Solar** sans Nécronyx actif provoque selon S2 la mort de l'équipe ; le dernier coup nécessite simultanément Nadir + état Necro confirmé. | [S2 §Attention](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source, ordre précis de sanction à vérifier |
| SOL-M28-09 | Pyrozomb : renvoi de dégâts près d'une cible touchée, dégâts de mort, résurrection par ennemi suivant ; Nécronyx sur une cible supprime ces mécanismes durant la fenêtre indiquée. | [S2 §État spécial Pyrozomb](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source |
| SOL-M28-10 | Nécronyx est décrit utilisable **tous les 2 tours** et sur Solar/Calciné fait subir au lanceur **+20 % érosion 3 tours**. | [S2](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source du guide, cooldown exact individuel à vérifier |
| SOL-M28-11 | Glyphe périphérique au début du tour d'un personnage : en Aurore −300 puissance, Zénith +20 % érosion, Crépuscule −2 PA, Nadir −4 PO, selon état. | [S2 §Principe](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source, LoS/cellules unknown |
| SOL-M28-12 | Natures des phases dans donjon : Aurore soins et +1 PA à l'équipe ; Zénith +200 puissance sur les monstres et +2 PA aux personnages ; Crépuscule −1 PA personnages ; Nadir −2 PA personnages. | [S2](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source, durée selon événement réel |
| SOL-M28-13 | Audace et Bravoure ont des mécanismes **d'Expédition de guilde** particuliers, jamais présumés en Songes : Audace donne à un changement d'état le bonus de l'état **précédent** ; Bravoure crée des glyphes autour des personnages/invocations. | [S2 §Expéditions](https://www.dofuspourlesnoobs.com/tour-de-solar.html) ; corrobore-source pour mode expédition |
| SOL-M28-14 | Ancien modèle « changement au tour de Solar **en Songes** », et « Nécronyx termine immédiatement le tour du lanceur », sont obsolètes depuis 3.7. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; obsolete-pour-cible |

## Etat initial et machines distinctes

**Entités** : Solar S, monstres M1/M2 et leurs états Pyrozomb, personnages A/B/C/D, invocations éventuelles I, cellules et glyphes du bord. Pas de numéro de cellule ni niveau inventé. Le scénario commence avec S vivant en **Aurore**, invulnérable, sans Nécronyx et tous les joueurs disposant de leurs propres PA/PM, PV, sorts et relances enregistrés. Il faut lire les rangs/PA exacts de Nécronyx depuis le client cible ; S2 documente la relance, pas les champs complets de chaque rang.

**Automate de phase** :

- Etat courant dans {Aurore, Zénith, Crépuscule, Nadir, Unknown}, avec un compteur des ticks de la phase, lastConfirmedPhaseEvent, pendingNecro, sourceActorId de Necro, actorTurnId, globalRoundId et solarTurnId.
- Mode **DONJON** : phase évolue à partir d'un événement de **tour de Solar**. Le guide décrit un pas naturel par blocs de trois tours jusqu'à Nadir ; Nécronyx confirmé peut avancer la transition au prochain tour de Solar. Le round global est **enregistré**, mais ne doit pas devenir le déclencheur de la phase de ce mode.
- Mode **SONGES_3_7** : phase évolue à partir de l'événement de **tour global** plutôt que de SolarTurnStarted. Quand GlobalRoundBoundary est confirmé, interroger la phase réelle et mettre à jour ; ne pas deviner si c'est à la dernière initiative ou première activation. La durée exacte de Nadir, le déclenchement Necro au sein de ce round et l'ordre d'émission des états sont **unknown sans replay Songes 3.7**.
- Mode **EXPEDITION** : héritage de la phase de donjon + effet du modificateur Audace/Bravoure. Même lorsqu'un effet se déclenche « au début du tour de Solar », le bonus d'Audace pendant un **changement de phase** est associé à la phase **précédente** (S2), et pas à la nouvelle étiquette affichée.

**Conditions sur la fenêtre de kill** :
- phase courante = Nadir **confirmée** ;
- effet Nécronyx actif sur Solar **confirmé au moment du coup final**, et non simplement envoyé ;
- kill doit survenir avant le retour éventuel en Aurore ou expiration de Necro ;
- équipe encore capable d'attaquer avec ressources réelles ; les dégâts de Pyrozomb et effets de renvoi peuvent frapper les alliés proches.

## Séquence documentée et chronologie des acteurs

| Etape | Acteur | Action et cible | Précondition et coût | Transition / preuve |
| --- | --- | --- | --- | --- |
| 0 | Moteur | Charger phase Aurore et mode donjon ou Songes | Révision d'état confirmée, Solar vivant | Solar invulnérable S2 ; deux horloges distinctes S1 |
| 1 | A | Préparer : placement/contrôle pendant les phases 1–3 | PA/PM de A (inconnus en fixture) | Aucune phase forcée par position/attaque ordinaire |
| 2 | A ou B | Nécronyx sur Solar, si saut de phase souhaité | Sort acquis, relance et cible légales, état serveur confirmé ; coût PA unknown | PendingNecro documenté ; **ne termine plus le tour A/B en 3.7** S1 |
| 3 | Horloge | Tour boss (DONJON) ou round global (SONGES) | Événement de phase reçu | Saut de phase lié à Nécronyx si appliqué, **un état** seulement S2 |
| 4 | A | Début fenêtre Nadir | Nadir explicitement confirmé | Solar vulnérable ; état Pyrozomb toujours à respecter S2 |
| 5 | B | Nécronyx sur Solar pour coup fatal | B joue avant finisseur, sort utilisable, serveur confirme le sort | Solar reçoit Necro ; renvoi Pyrozomb neutralisé selon S2 |
| 6 | C/D | Attaques dont coup fatal Solar | Toujours Nadir et Necro actif, PA/PO/LoS confirmés | Tuer Solar sans wipe attendue seulement si toutes les gardes sont maintenues S2 |
| N1 | C | Coup fatal en Nadir sans Necro | Solar vulnérable mais pas Necro | Danger de wipe de l'équipe S2 : plan à refuser |
| N2 | Horloge | Round global Songes progresse entre B et C | Événement reçu, phase modifiée | Calcul de C invalidé ; recontrôler Nadir, indépendamment du tour propre de Solar S1 |

## Solo, duo, trio, quatuor

**Solo** : A temporise les trois premiers états, sauve Nécronyx jusqu'à fenêtre Nadir, le lance puis frappe avant transition suivante, si PA/sort/dégâts le permettent. Ne pas inventer que Nécronyx coûte zéro PA ni qu'il termine son tour.

**Duo** : A contrôle les monstres / met Necro, B finit Solar ; ordre A→B indispensable à cette séquence. Si B agit avant A, remplacer la stratégie (B ne peut se prévaloir d'un Necro futur). Tenir compte de Moribombe et de la résurrection, mais Nécro sur chaque monstre est une ressource à relance.

**Trio** : A gère Pyrozomb, B possède Nécronyx avant la fenêtre, C prépare un burst après B. Si un ennemi ressuscite, ne pas lui réattribuer l'invulnérabilité supprimée 3.7, mais conserver ses autres contraintes d'état et la possibilité de renaissance.

**Quatuor** : A tank, B Necro premier en initiative, C/D frappeurs ; en Songes, même si Solar n'a pas pris son tour, **l'événement global** peut avoir fait bouger sa phase et ruiner la fenêtre. Réévaluer avant chaque acteur, pas « trois tours Solar » appliqué globalement.

## Tests proposés, zéro exécution

| ID | Fixture / action | Observable attendu |
| --- | --- | --- |
| P01 — donjon naturel | Tour de Solar normal, aucun Necro ; passage Aurore→Zénith | Transition selon tour du boss et trois tours Aurore, avec état réellement reçu |
| N01 — Songes global | Solar ne rejoue pas, un round global se termine en Songes 3.7 | Événement de phase lié au **round global**, pas au SolarTurnStarted ; test nécessite une capture Songes |
| N02 — donjon pas Songes | Un round global change mais pas le tour propre du Solar donjon | Ne pas avancer d'office la phase ; transition liée à son tour confirmé |
| N03 — hors Nadir | C attaque en Zénith sans transition Nadir | Invu Solar ; aucune projection de dégâts de boss |
| P02 — Nadir et Necro | B pose Necro confirmé avant C finisseur ; S Nadir | Fenêtre de kill légal du point de vue de la mécanique, sous PV/dégâts disponibles |
| N04 — Nadir sans Necro | Solar Nadir, C veut tuer et Necro absent | Plan dangereux (wipe d'équipe décrit), refuser de certifier une victoire sûre |
| N05 — effet de tour 3.7 | A lance Necro et conserve PA pour une attaque éligible | Le tour ne s'arrête **plus** automatiquement au lancer de Necro |
| N06 — résurrection 3.7 | M1 meurt puis ressuscite, pas d'autre invu | Ne pas ajouter « invulnérable 1 tour » de l'ancien patch |
| N07 — Nadir expiré | Solar passe Aurore avant coup final, malgré Necro posé précédemment | Solar redevient protégé, le kill plan doit être invalidé |
| N08 — Audace / Bravoure | Changement d'état en expédition, puis invocation sur glyphe de Bravoure | Bonus Audace de l'état **précédent** et glyphes Bravoure spécifiques ; pas en Songes par défaut |

## Risques, timing et reprise

Ne pas confondre **invulnérabilité de Solar hors Nadir** (liée à la phase), l'ancienne **invulnérabilité post-résurrection supprimée en 3.7**, et des invulnérabilités auxiliaires d'autres sorts. Ne pas dériver la phase de l'animation ; suivre l'état effectif. Les risques majeurs sont le wipe de l'équipe si le kill Solar n'est pas sous Necro, un renvoi Pyrozomb mal anticipé, et un ciblage au mauvais round en Songes.

Preuves encore nécessaires : replay **Songes 3.7.13.17** avec round global qui bascule alors que Solar n'a pas rejoué ; effet de Nécronyx et instant de consommation, vitesse de phase en Songes, bouclage Nadir, événement de mort d'un ressuscité, glyphes/risques associés. Le tutoriel vidéo réel ne livre pas de transcript MCP ; uniquement métadonnées et chapitre **3:15 de description**, pas de paroles ni preuve de plateau. Statuts : corrobore-source, hypothese, obsolete-pour-cible ; ni observe-video, ni declare-video, ni valide-test/live.
