# M33 — Passage développeur : 5–8 places, convocations et contraintes de challenge

**Mission documentaire 2026-11-04 ; 3 fichiers sous M33 uniquement.** L'objectif est de supprimer les fausses équivalences `nombre de cases occupées = joueurs`, `invocation = compagnon` et `effet confirmé = ACK de lancement`. Aucun code ni règle serveur inventée.

## Trois règles prioritaires

| ID | Modélisation à réaliser en priorité | Preuve et limite |
| --- | --- | --- |
| **M33-R01/R02/R03 — places vs entités** | Maintenir `combatSlots` (joueurs et compagnons admis) séparé de `liveUnits` (incluant summons). Un compagnon occupe une place de combattant contrôlée ; Sac/poupée sont des unités d'invocation liées à leur propriétaire. **Le donjon modulaire peut dépendre des places initiales, pas de chaque apparition de summon**. | [DPLN compagnons mis à jour 02/03/2026](https://www.dofuspourlesnoobs.com/les-compagnons.html) ; [devblog historique donjons modulaires](https://dofus.jeuxonline.info/actualite/35775/devblog-donjons-modulaires). Plafonds précis de l'instance 3.7 et échelle de monstres **à confirmer**. |
| **M33-R05/R06/R07 — challenge et attribution** | Conserver `challengeRule`, `targetId`, `effectiveActorId`, `ownerId`, tour/phase et `serverStatus`. *Duel* : ne pas frapper le même ennemi par une **seconde attribution interdite**. *De la même lignée* : vérification **en fin du tour de l'acteur concerné**, aligné avec un allié éligible. Compagnon et invocation requièrent un filtre propre. | [DPLN challenges](https://www.dofuspourlesnoobs.com/les-challenges.html) pour Lignée et échec visible ; [Tofus historique Duel](https://www.tofus.fr/fiches/challenge) pour assimilation des **invocations** à invocateur. Attribution **compagnon/3.7 non prouvée**, statut doit rester Unknown avant retour serveur. |
| **M33-R04/R08/R09 — temps, zones, replanification** | Recalculer cellule, LoS, prochain tour, dégâts/buffs et disponibilité après chaque mouvement/spawn/mort ; en 3.7, le **+15 s PvM s'applique aux personnages, pas aux invocations** (Songes exclus). Les glyphes à aura, début et fin de tour se résolvent à **leur phase documentée**. | [DPLN 3.7, 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [Grimoire Enutrof](https://dofus-grimoire.fr/classes/enutrof/) et [Sadida](https://dofus-grimoire.fr/classes/sadida/). Timer de compagnon et ordre serveur de plusieurs effets = Unknown. |

## Données et machines à états nécessaires

**Groupe (places)** :
- `combatMode` = donjon / quête / arène / Songe / raid / autre ; `maxSlots` et `admittedSlots` issus d'une source 3.7 du contenu ou d'un événement autoritatif, non fixés partout à 8.
- `serverMode` = serveur classique/monocompte et règles d'éligibilité (source officielle [Ankama monocompte, 15/04/2026](https://support.ankama.com/hc/en-us/articles/115013728988--DOFUS-How-does-single-account-mode-work)) ; une équipe de plusieurs **joueurs** ne signifie pas plusieurs comptes autorisés à une seule personne.
- `rosterSlots[]` = `slotId`, `unitId`, `kind=player|companion`, `controllerPlayerId`, `teamId`, `alive`, `initiative`.
- `encounterEnemies[]` et configuration de difficulté **observées au démarrage** ; jamais « +1 monstre » à chaque spawn d'invocation. Un combat de quête peut interdire les compagnons ; ne pas supposer admission.

**Entités dynamiques (y compris summons)** :
- `unitId` exact (pas d'ID inventé), `kind=player|companion|summon|enemy`, `ownerUnitId`, `controllerPlayerId`, `summonSpellId`, rang/variante, `cellId`, `hp/alive`, `status`, `canBeTargeted`, `canBlockCell` si confirmé.
- `occupancy[cellId]` recalculée par réception de déplacement/spawn/mort ; `reservedCells` seulement une intention avec expiration, jamais vérité serveur ; zones/pièges/glyphes conservés séparément.
- `turnOrder[]`, `turnEpoch`, `actorTurnStart/end`, timer effectif de chaque catégorie ; `timeBudget` provenant du combat, **pas un simple +15 s à tout**.
- Buff `sourceActorId`, `targetId`, durée/phase, attribution de dommages/soins ; ne pas fusionner l'entité summon avec son owner dans les HP/PA.

**Challenge** :
- `challengeId`, `ruleVersion`, `state=Active|Failed|Succeeded|Unknown`, `serverEvidence`, `scopedTargets`, `attackerAttributionByTarget`, `eligibilityByUnitKind` (versionnée, peut être Unknown).
- `Duel` garde une table cible→acteur autorisé attribué ; tester invocation contrôlée et compagnon séparément. `DeLaMemeLignee` contrôle la géométrie à **la fin du tour** du combattant éligible et exige un **autre** allié éligible en ligne ; la catégorie compagnon/summon reste **non démontrée**.
- `serverStatus=Failed` avec acteur indiqué dans le chat/documentation peut confirmer l'échec, mais ne pas construire un identifiant protocolaire arbitraire ; `Succeeded` doit attendre l'issue vérifiée du combat.

## Round concret à six places (proposé, non exécuté)

**Roster** P=Pandawa, E=Enutrof, F=Féca, C=Crâ, K1 et K2 compagnons contrôlés respectivement par P et C ; 6 places. `SacId` invoqué durant le round devient 7e entité alliée **vivante**, sans 7e place de groupe. Ennemi M1 adjacent à P, ennemis M2–M6 non placés sans carte. P Sobre, case de jet Z et case d'invocation W distinctes libres initialement, ordre `P→K1→E→M1→F→C→K2→...`, conditions légales des sorts/rangs à vérifier.

1. **P** Karcham M1 **1 PA porter +1 PA jeter** vers Z, selon [Grimoire Panda 3.7.12](https://dofus-grimoire.fr/classes/pandawa/). **Confirmer** la position de M1, revalider Z/W et les lignes avant K1.
2. **K1** agit avec une action de son deck *réel* ; aucun nom ni coût de sort compagnon non attesté. Si challenge Duel actif, vérifier `attribution(K1, M1)` avant toute frappe de K1. Son tour ne consomme aucun PA de P.
3. **E** : poser Sac Animé en W **2 PA** sous capacité/case libre, puis Maladresse M1 **1 PA** si cible valide ; confirmer spawn **et** retrait PM effectif. SacId est `kind=summon owner=E`.
4. **M1** : adversaire peut frapper et tuer Sac/K2 avant leur prochain tour ; sur `DeathConfirmed`, retirer unité/cellule, actualiser timeline et contraintes challenge.
5. **F** : protection légale P (Barricade **2 PA** si pertinente contre une menace mêlée **future**, ne protège pas contre M1 déjà joué) ; **C** : attaque depuis sa ligne de vue actuelle ; **K2** : action propre seulement si encore vivant.
6. **Fin de tour de chaque acteur** : évaluer `DeLaMemeLignee` avec des alliés éligibles et de vraies cellules ; s'il n'y a que Sac en ligne, **ne pas promettre la réussite**. Finaliser le bilan Duel uniquement avec preuves de participation et statut du challenge.

Dériver les variantes **5 = 4P+1K**, **7 = 4P+3K**, **8 = 4P+4K** en ajoutant les agents et leurs tours ; valider d'abord capacité et nombre de monstres **dans le combat réel**. Une attaque, le mouvement d'une poupée ou un nouveau tour d'un compagnon sont de nouveaux événements et invalident les snapshots antérieurs.

## Contre-tests à prioriser

| ID | Injection unique | Résultat attendu / incertitude discriminée |
| --- | --- | --- |
| **N33-01 Occupation** | Le Panda jette M1 sur W, W était réservée à Sac E. | Ne pas tenter spawn sur case occupée ; replanifier autre cellule ou refuser ; jamais superposer des entités par planification locale. |
| **N33-02 Mort** | M1 tue K2 avant le tour annoncé de K2. | Annuler l'attaque de K2, libérer sa cellule selon reçu, invalider les buffs et engagements de challenge qui dépendaient de K2. |
| **N33-03 Duel compagnon** | P frappe M1, puis K1 compagnon contrôlé par P frappe M1. | **Unknown côté 3.7** : ne pas automatiquement transférer l'attribution P→K1 comme pour invocation historique ; observer événement du challenge. |
| **N33-04 Duel invocation** | P frappe M1, puis sa propre invocation frappe M1. | Ancien guide assimilation owner ; mesurer règle **actuelle** et ne pas confondre avec N33-03. |
| **N33-05 Lignée summon** | P finit tour aligné uniquement sur Sac, autre combattant joueur/compagnon hors ligne. | Le filtre d'éligibilité est non prouvé : ne pas faire passer `Unknown` à `Succeeded` sans preuve. |
| **N33-06 Timer** | Tour de personnage P puis tour de Sac dans combat PvM 3.7. | +15 s documentées pour personnages, pas summons ; mesurer compagnon à part. |

## Risque, validation et transfert
- **Priorité haute** : R01/R02 (place/occupancy), R05/R06 (challenge), puis R04/R09 (temps/expiration). Une erreur de place peut bloquer toutes les unités ; une erreur Duel peut annuler un challenge de groupe ; une erreur de durée peut dégrader tout le plan.
- **Source vidéo** : [HUZ 2025](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1885s) FR automatique **horodatée** lisible sur des préférences pour groupes de 6–8, pas une démonstration du système de challenge. [HUZ 2024 teams 6/8](https://www.youtube.com/watch?v=87ot3paBr1o) métadonnées/langues/chapi­tres de description seulement, transcription bloquée IP, version anciennne. Guide [Huzounet 2026](https://huzounet.fr/guides/les-teams-de-8) contient référence **2.73**, donc prudence.
- **Validation indispensable** : lire le texte des challenges dans le client cible 3.7.13.17 ; rejouer un combat 4P+compagnons avec logs expurgés et horodatés ; isoler acteur, contrôleur, propriétaire et victime ; mesurer version de progression du donjon et états de challenge. Réponses `Unknown/Refused` si absence d'effet confirmé ; éviter tout replay automatique.
- **Aucun test exécuté ni combat lancé** ; pas de `valide-test`/`valide-live`. Aucun fichier source, Git, config du bot ou `SYNTHESE.md` modifié. Rapport principal [RAPPORT.md](RAPPORT.md), fiche détaillée [FICHE_EQUIPES_ETENDUES.md](FICHE_EQUIPES_ETENDUES.md).

