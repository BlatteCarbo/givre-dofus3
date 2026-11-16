# M31 — Tour de Bethel : Monolithe, Dagon, résurrections et changement de camp (DOFUS 3.7)

**Recherche du 04/11/2026, sans jeu ni test.** Référence locale du kit : client 3.7.13.17, version non certifiée sur manifeste. Les mécanismes sont documentés par le patch 3.7 et des guides de donjon ; **aucune observation de combat ou de transcript YouTube M31 obtenue**, aucun résultat valide-test ou valide-live.

## Règle centrale modifiée au patch 3.7

Le **Dagon passé dans le camp des joueurs peut mourir sans clôturer le combat** : depuis la 3.7, **Bethel recrée un Monolithe et un Dagon dans son propre camp** ; il faut de nouveau **tuer le Monolithe** pour reprendre un Dagon allié. Source explicite [Présentation MAJ 3.7, 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Les anciens guides qui décrivent une seule phase Dagon allié sans boucle ne couvrent pas cette transition.

Autres mises à jour du même patch : **Nécronyx ne termine plus le tour de l'acteur qui le lance** ; les monstres **ressuscités ne sont plus invulnérables pendant un tour** ; le **Monolithe ne réduit plus les dégâts des poisons/pièges** ; son **bouclier ne dure qu'un tour** ; le bonus PM et PO d'**Aquazomb** devient désenvoûtable. Aucune de ces modifications n'est facultative lors d'un replay 3.7.

## Sources et provenance

| ID | Source exacte | Force et limites |
| --- | --- | --- |
| **M31-S1** | [DofusPourLesNoobs, patch 3.7, 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) | Texte explicite récent : retour en phase ennemie si Dagon allié meurt, bouclier un tour, Nécronyx ne passe plus le tour, ressuscité vulnérable. **Source prioritaire** pour différences de version. |
| **M31-S2** | [DofusPourLesNoobs, Tour de Bethel](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), dernière mise à jour **05/10/2026** | Guides des phases, Monolithe mêlée, Dagon allié contre Bethel, nécessité **Nécronyx** au décès Bethel, risque Aquazomb, chronologie T1/T4, éloignement en fin de tour. |
| **M31-S3** | [Dofuserie, Tour de Bethel](https://www.dofuserie.com/donjons/tour-de-bethel/) | Bon déroulé et contrôle du Dagon, mais son passage « **Nécronyx fait passer le tour** » est désormais **obsolete-pour-cible** ; l'ancienne invulnérabilité des ressuscités est elle aussi obsolète. |
| **M31-S4** | [Dofus Portals, Tour de Bethel](https://dofus-portals.fr/donjons/tour-de-bethel/) | Reprise des stratégies de Monolithe au contact, Dagon allié en mêlée de Bethel et Nécronyx au moment fatal. Conditions de contrôle/placement exactes à vérifier. |
| **M31-S5** | [DofusPourLesNoobs, état Aquazomb et Nécronyx](https://www.dofuspourlesnoobs.com/tour-de-bethel.html) | Décrit un **monstre tué ressuscité par le suivant**, et Nécronyx utilisé avant la mort pour neutraliser les tours de la cible ressuscitée. La résurrection ne donne **plus** 1 tour invulnérable en 3.7. |

**Vidéos** : recherches ciblées « Tour de Bethel Dagon DOFUS 2025 2026 YouTube » n'ont fourni aucune URL vidéo spécifique vérifiable pour documenter cet enchaînement. Aucun ID de vidéo, auteur, date ou transcript M31 **inventé** ; les règles ici sont des textes de guides, pas des déclarations vidéo ou observations de case. Une récupération YouTube sera la première étape de reprise si un lien fourni ou nouvellement localisé fonctionne.

## Règles atomiques et statut

| ID | Règle | Statut / preuve | Inconnues |
| --- | --- | --- | --- |
| **M31-R01** | Bethel et Dagon ennemi sont invulnérables dans la phase initiale ; **Monolithe ne peut être blessé qu'en mêlée**. | **corrobore-source** [S2](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [S3](https://www.dofuserie.com/donjons/tour-de-bethel/). | Type précis de « invulnérable à distance », exceptions et identité des sorts à lire client. |
| **M31-R02** | **Mort du Monolithe** → un Dagon est attribué/invoqué du **côté joueurs** ; celui-ci peut blesser Bethel en mêlée (Souffle Chaud d'après guide). | **corrobore-source** [S2](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [S4](https://dofus-portals.fr/donjons/tour-de-bethel/). | Le contrôle du Dagon, la priorité de cible, son UID de respawn et le moment dans la file d'initiative ne sont pas prouvés en serveur. |
| **M31-R03** | **Mort du Dagon allié pendant vie Bethel** → **pas de fin de combat** ; Bethel recrée **Monolithe et Dagon ennemis**, retour à la première phase. | **corrobore-source** [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) explicite version 3.7. | Ordre exact des deux invocations/UID et phase de leurs premiers tours à vérifier. |
| **M31-R04** | **Bethel doit être dans l'état Nécronyx au moment de sa mort**, sinon le groupe perd le combat. | **corrobore-source** [S2](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [S4](https://dofus-portals.fr/donjons/tour-de-bethel/). | Mort simultanée d'un autre monstre et expiration de Nécronyx dans le même lot d'événements non décrites. |
| **M31-R05** | **Nécronyx ne fait plus passer le tour du joueur** depuis **3.7**, et Bethel **retire son état au bout de trois tours** (exemple guide pose au tour 1 du lanceur puis disparition au tour 4 de ce même lanceur). | **corrobore-source** [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [S2](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). | Procès-verbal serveur exact du déclenchement et clé de tour du caster encore nécessaires. |
| **M31-R06** | En **3.7**, **les monstres ressuscités n'ont plus un tour d'invulnérabilité**. Sous Aquazomb, une cible tuée peut ressusciter au prochain tour de monstre allié ; Nécronyx avant mort empêche ses tours efficaces selon guide. | **corrobore-source** [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [S2](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). | Résurrection UID, délais et états à confirmer. |
| **M31-R07** | **Bouclier Monolithe** désormais **un seul tour** ; poisons/pièges **non réduits** ; bonus PM/PO Aquazomb désenvoûtable. | **corrobore-source** [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). | Valeur bouclier selon combat/rang, application aux autres monstres et ordre du désenvoûtement. |
| **M31-R08** | Garder **Dagon ennemi** repoussé lui applique **Inébranlable un tour** et empêche l'attraction ; quand le Monolithe tombe, placer **Dagon allié** à portée mêlée de Bethel. | **corrobore-source** [S2](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [S4](https://dofus-portals.fr/donjons/tour-de-bethel/). | Durées exactes, LoS, états immunité et phase de sortie à relever. |
| **M31-R09** | À partir du **tour 2**, terminer le tour d'un personnage à **plus de 6 PO** de tout monstre comptant pour la règle d'Aquazomb confère des bonus aux monstres ; invocation/Monolithe/ressuscité peuvent être exclus du comptage selon guide. | **corrobore-source** [S2](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). | Inventaire exact de quelles entités comptent selon cas et sous-patch. |
| **M31-R10** | Ancien conseil de **passer le tour en lançant Nécronyx** ou de traiter chaque ressuscité comme invulnérable un tour est faux en 3.7. | **contradictoire**, **obsolete-pour-cible** [S3](https://www.dofuserie.com/donjons/tour-de-bethel/) vs [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). | Ne pas importer le script de combat d'un guide ancien sans garde de version. |

## Machine à états fonctionnelle

- **PHASE_ENNEMIE (S0)** : boss vivant invulnérable aux joueurs ; Monolithe mE vivant dans camp ennemi, Dagon dE camp ennemi ; focus Monolithe **en mêlée**. Événements de Nécronyx et Aquazomb parallèles. Tant que Monolithe vivant, aucun Dagon joueur fiable.
- **TRANSITION MORT_MONOLITHE (S0→S1)** : le serveur enregistre death(mE), puis une invocation/changement de côté donne **Dagon ami** dA (nouvel UID et propriétaire/contrôleur à enregistrer, et ennemi dE à réconcilier). **Ne pas supposer** que dE change seulement le champ side sans disparition/recréation, malgré expression « passe dans votre camp ».
- **PHASE_DAGON_ALLIÉ (S1)** : dA vivant, contrôler/positionner près de Bethel, faire lancer **Souffle Chaud** contre Bethel ; les joueurs assurent soin/protection/placement et Nécronyx.
- **TRANSITION MORT_DAGON_ALLIÉ (S1→S0)** : Bethel encore vivant, dA meurt → Bethel ressuscite/recrée **Monolithe et Dagon ennemis**, nouvelle phase. Conserver un **numéro de cycle** et de nouveaux UID.
- **TRANSITION MORT_BETHEL (S1→S2)** : dA inflige le coup fatal et Bethel possède **Nécronyx actif à ce même instant** → état **BOSS_MORT**, il reste éventuellement d'autres monstres à éliminer. Sans Nécronyx : **DÉFAITE** selon guide, même si points de vie atteignent zéro.
- **AQUAZOMB** : sous-machine pour chaque monstre ordinaire ; death(uid) → RESURRECTION_PENDING → RESURRECTED sous conditions du sort ; en **3.7 ressuscité directement vulnérable** (aucune grâce d'un tour), état Nécronyx éventuel réduit la capacité de jouer ; ne pas confondre cette résurrection avec la transition particulière dA→mE/dE.

## Plans solo / duo / trio / quatuor et dépendance initiative

- **Solo théorique** : un personnage avec Nécronyx doit être capable d'approcher un Monolithe en mêlée, de survivre au Dagon ennemi et de rapprocher Dagon allié du boss. **Faisabilité non démontrée**, aucun plan de classe unique certifié.
- **Duo** : placeur/tank → frappeur Monolithe : si tank pousse Dagon ennemi **avant son tour**, état Inébranlable peut empêcher l'attraction ; frappeur termine Monolithe puis Dagon allié apparaît. **Ordre inverse** frappeur tue Monolithe avant que tank soit placé, risque changement de contrôleur Dagon/placement défavorable. Vérifier initiative Dagon nouvellement apparu.
- **Trio** : tank immobilise Bethel et réapplique Nécronyx, placeur gère Dagon, frappeur mêlée détruit Monolithe ; en S1 garder dA en mêlée de Bethel et protéger dA. Si soigneur joue seulement **après dA**, un soin tardif ne protège pas dA du coup ennemi ayant eu lieu avant.
- **Quatuor** : tank/placement + frappeur mêlée + soigneur/protecteur + débuff/contrôle : gérer bouclier Monolithe durée un tour, timer Nécronyx 3 tours, fin de tour ≤6 PO d'un monstre admissible pour éviter buff, puis assurer au moment du coup fatal **état Nécronyx confirmé**. Si un allié provoque mort de dA avant le coup final, passer par **un nouveau cycle S0** ; pas de « succès supposé ».

**Tactique ≠ fait serveur** : les acteurs, sorts exacts, PA, portée, dégâts, résistances et cases ne sont pas inventés ; les scénarios sont des hypothèses de coordination à valider.

## Scénarios discriminants essentiels

| Cas | Entrées / action | Sortie attendue | Discrimination |
| --- | --- | --- | --- |
| **P1** | S0, frappeur **en mêlée** détruit Monolithe, Bethel vivant. | Passage S1, Dagon allié disponible, nouvel état/UID. | R01/R02 |
| **N1** | S0, attaque **à distance seulement** sur Monolithe. | Invulnérabilité distance : pas de mort, pas de Dagon allié. | R01 |
| **P2** | S1, Dagon allié vivant en mêlée attaque Bethel jusqu'à mort, **Nécronyx actif** au coup fatal. | Boss mort normalement (puis nettoyer autres monstres). | R02/R04 |
| **N2** | S1, même coup fatal mais **Nécronyx absent ou expiré**. | Combat perdu selon guide, malgré dommage final. | R04/R05 |
| **N3** | S1, dA meurt avant Bethel. | **3.7 : retour S0**, résurrection/recréation Monolithe et Dagon **ennemis**, combat en cours. | R03 ; différence pré-3.7 |
| **N4** | Nécronyx sur Bethel T1, préparer fatal T4 sans le renouveler. | L'état peut être expiré au tour T4 du même lanceur ; plan fatal doit le recontrôler. | R05, délai initiative |
| **P3 / N5** | Aquazomb ressuscite monstre, recommencer attaque immédiatement vs supposer invulnérabilité un tour. | **3.7 : pas de bouclier d'invulnérabilité dû à la résurrection**, mais autres protections restent possibles. | R06 |
| **N6** | Deux cycles de bouclier Monolithe à un tour puis poison/piège. | Ni durée 2 tours, ni réduction poison/piège ancienne ; valider les événements. | R07 |
| **N7** | Tour 2+, personnage termine >6PO des monstres admissibles. | Buff Aquazomb des ennemis ; doit pouvoir être débuff pour PM/PO selon patch. | R09 |
| **N8** | Modifier initiative : placeur avant/après tueur Monolithe ; Dagon ami avant/après Bethel. | Contrôleur, cellules, première fenêtre d'attaque de dA **à relever** ; aucun ordre supposé. | R02/R03 et initiative |

## Limites et reprise

- Aucun vidéo/metadata/timed transcript authentifié pour M31 ; descriptions et phases sont sourcées **web**, pas « declare-video ».
- La page Dofuserie juxtapose des bonnes tactiques et une ancienne phrase « Nécronyx fait passer tour ». Le patch **prime** pour cette règle ; les autres anciennes valeurs demandent recoupement.
- Source serveur exacte des événements de résurrection, du nouveau Dagon allié et de sa propriété encore manquante ; décès de Dagon allié pendant phase intermédiaire non instrumenté.
- Prochaine preuve : replay **3.7.13.17** d'un Monolithe tué, Dagon allié obtenu puis mort, recréation Monolithe/Dagon ennemis, second cycle et coup fatal Nécronyx, avec UID, source, équipe, phase, initiative, effets. **Aucun test exécuté**.

**Livrables liés** : [FICHE_BETHEL_DAGON.md](./FICHE_BETHEL_DAGON.md) et [PASSAGE_DEVELOPPEUR.md](./PASSAGE_DEVELOPPEUR.md).
