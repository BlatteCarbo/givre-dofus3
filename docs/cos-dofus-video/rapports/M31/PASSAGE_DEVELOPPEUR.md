# M31 — Passage développeur : Bethel, Dagon, Monolithe et Nécronyx 3.7

**Spécification documentaire au 04/11/2026.** Aucun code, test, combat ou Git. Référence patchée par [DOFUS 3.7, publié 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), détails par [guide Tour de Bethel actualisé 05/10/2026](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). Version client 3.7.13.17 connue du kit mais non confirmée. Voir [RAPPORT.md](./RAPPORT.md) et [FICHE_BETHEL_DAGON.md](./FICHE_BETHEL_DAGON.md).

## Trois invariants prioritaires

| Priorité | Comportement nécessaire | Source et statut | Erreur à empêcher |
| --- | --- | --- | --- |
| **1 — phase et camp** | Début : Monolithe et Dagon ennemis. **Mort Monolithe → Dagon allié** apte à frapper Bethel. **Mort Dagon allié tandis que Bethel vit → nouveau Monolithe et Dagon ennemis, combat continue**. Compter les cycles, conserver chaque UID et camp, ne pas supposer une mutation in-place. | M31-R01,R02,R03 ; **corrobore-source** [patch 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [guide boss](https://www.dofuspourlesnoobs.com/tour-de-bethel.html). | Déclarer défaite/victoire à la mort du Dagon ami ; confondre Dagon ami / ennemi même nom. |
| **2 — mortalité conditionnelle** | Bethel est blessé dans la phase alliée par l'action **Souffle Chaud** du Dagon ami ; **Nécronyx doit être actif sur Bethel au moment exact de death(Bethel)** sinon défaite. Bethel retire l'état après **3 tours** selon guide lié au lanceur, et le sort ne **termine plus le tour du lanceur en 3.7**. | M31-R02,R04,R05 ; **corrobore-source** [guide du donjon](https://www.dofuspourlesnoobs.com/tour-de-bethel.html), [patch 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). | Créditer un tour passé à Nécronyx, supposer un état encore actif car posé trois tours calendaires plus tôt. |
| **3 — résurrection et bouclier versionnés** | **Aquazomb** peut ressusciter un monstre ; **en 3.7 il ne gagne plus une invulnérabilité automatique d'un tour** ; bouclier du Monolithe dure **un seul tour**, poisons/pièges ne sont plus réduits, bonus PM/PO Aquazomb peuvent être débuff. | M31-R06,R07 ; **corrobore-source** [patch 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). | Programmer une année de comportement obsolète d'un guide antérieur ; bloquer à tort l'attaque sur un ressuscité. |

**État des preuves :** source web versionnée seulement ; aucun tutoriel vidéo Bethel 2025/2026 identifié avec URL et transcript MCP fiables, donc aucun timestamp de parole ni plateau certifié. Les affirmations de contrôle précis du Dagon sont des hypothèses jusqu'à observation du serveur. Ne pas attribuer valide-test ou valide-live.

## Contrat des états et événements

| Domaine | Données nécessaires | Garde/transition à modéliser |
| --- | --- | --- |
| **BossPhase** | fightId, phase ENEMY_MONOLITH/ALLIED_DAGON/BOSS_DEAD/FAILED, cycleIndex, reasonEventId | M31-R03 provoque **ALLIED_DAGON→ENEMY_MONOLITH** et cycleIndex+1 si dA meurt et B vit. Ne pas terminer sur ACK de mort dA. |
| **BossEntities** | bethelUid, monolithEnemyUid, dagonEnemyUid, dagonAllyUid, ownerUid/controllerUid, team, alive, spawnEpoch | Comparer l'UID avant/après chaque réinvocation. Ne jamais piloter un ennemi avec l'ancien identifiant d'allié. |
| **TurnClock** | round, actorUid, initiativeIndex, turnEpoch, spellSourceUid, expirationPhase, serverSequence | Nécronyx selon cycle de **celui qui le pose**, exemple T1→T4 ; vérifier expiration avant ou après action du lanceur. |
| **NécronyxState** | targetUid, sourceUid, appliedTurn, remaining, active, expirationEvent | Avant **chaque coup fatal potentiel** de dA sur B : état actif confirmé. Appliquer ce sort n'interrompt **pas** toute la suite du tour de P en 3.7. |
| **MonolithDefense** | shieldSourceUid, shieldAmountFromServer, ttl, damageFilter, sourceOfDamage | Durée **1 tour**, disparition/désenvoûtement ; poison/piège plus réduit par Monolithe depuis patch. |
| **AquazombState** | sourceUid, targetUid, resurrectionPending, newlyResurrectedUid, noInvulnGrace, pmPoBuffStatus | Nouvelle instance ressuscitée doit être réconciliée avec son camp ; ne pas introduire l'ancienne immunité automatique de 1 tour. |
| **MovementAndPosition** | casterUid, targetUid, oldCell, newCell, push/teleport/drag, legalLoS, old/new distance, serverSequence | Maintenir Monolithe frappable à mêlée ; Dagon ennemi repoussé pour Inébranlable ; Dagon ami à mêlée Bethel pour Souffle Chaud ; finir à distance compatible pour éviter buffs. |
| **ServerReceipt** | session/epoch, commandId, actorUid, accepted/refused/unknown, summonEvents, teamChanges, castEvents, deathEvents, statusSnapshots | Les changements de phase suivent **seulement** preuves serveur ; refus/Unknown → suspendre plan et relire combat, jamais rejouer aveuglément un sort ou conclure. |

**Modèle textuel des transitions** :

- S0 ENEMY_MONOLITH : boss vivant, mE/dE ennemis ; Monolithe vulnérable uniquement contact.
- S0 + death(mE) : attendre événements invoquant/attribuant dA → S1 ALLIED_DAGON, conserver UID, équipe et contrôleur.
- S1 + death(dA) + B.alive=true : patch 3.7 → invoquer/réconcilier nouveaux mE et dE ennemis → S0 avec un nouveau cycle.
- S1 + death(B) + B.Nécronyx=true : → BOSS_DEAD, poursuivre élimination des mobs restants.
- S1 + death(B) + B.Nécronyx=false : → FAILED selon guide ; éviter de déduire du seul total de PV.
- En parallèle, death(mob ordinary) + Aquazomb peut déclencher **résurrection d'une autre entité** ; pas de bonus d'invulnérabilité 1 tour ajouté par l'ancien moteur.
- En parallèle, timeout de bouclier mE (1 tour) et Nécronyx B (3 tours selon guide) ont des **horloges distinctes**.

## Tests à proposer avant tout correctif

| ID | Préconditions et actions | Résultat observable attendu | Règle | Statut |
| --- | --- | --- | --- | --- |
| **P1** | Monolithe tué **en mêlée** après attaque acceptée. | Nouvel UID/camp Dagon allié, S0→S1 ; Bethel toujours vivant. | R01/R02 | **proposé, non exécuté** |
| **N1** | Monolithe attaqué uniquement **à distance**. | Aucun death(mE) par ces dégâts, dA non créé. | R01 | proposé, non exécuté |
| **P2** | Dagon allié frappe Bethel en mêlée ; coup fatal avec **Nécronyx confirmé**. | death(B) → BOSS_DEAD, combat pas perdu pour absence de Nécronyx. | R02/R04 | proposé, non exécuté |
| **N2** | Même coup fatal mais sans Nécronyx ou état expiré. | Échec général selon guide, ne pas annoncer victoire. | R04/R05 | proposé, non exécuté |
| **N3** | Dagon allié tué pendant que Bethel reste vivant. | **Aucun combat terminé** ; monolithe et Dagon ennemis réapparaissent, S1→S0, cycle+1. | R03, 3.7 | proposé, non exécuté |
| **N4** | Nécronyx appliqué T1 puis T4 du **même lanceur** ; tester si fatal entre début et remise à jour état. | Expiration à la phase réellement mesurée ; ne pas conserver buff sans événement. | R05 | proposé, non exécuté |
| **P3/N5** | Monstre ordinaire ressuscité par Aquazomb, frapper immédiatement / simuler immunité 1 tour ancienne. | **Pas d'invulnérabilité automatique liée à la résurrection** en 3.7 ; éventuelles autres protections isolées. | R06 | proposé, non exécuté |
| **N6** | Monolithe gagne bouclier, souffre poison/piège, attendre un tour. | Bouclier ne reste **qu'un tour**, pas de réduction spécifique poison/piège. | R07 | proposé, non exécuté |
| **N7** | Dagon ennemi repoussé avant/après son tour, comparaison Inébranlable. | État d'un tour peut empêcher attraction ; dépend de l'ordre. | R08 | proposé, non exécuté |
| **N8** | Personnage termine >6PO de tout monstre admissible, en tour ≥2 ; contrôler comparaison ≤6PO. | Bonus Aquazomb en cas de sortie, débuff PM/PO possible depuis 3.7. | R09 | proposé, non exécuté |
| **N9** | Alterner initiative du placeur/tueur Monolithe et celle de dA/Bethel après transition. | Différences d'action et de premier tour dA mesurées, **aucun contrôleur ou créneau inventé**. | R02 et coordination | proposé, non exécuté |

## Synergies et initiative au-delà de la seule classe

- **Solo** : faisabilité non démontrée. Si personnage ne peut pas simultanément maintenir Nécronyx, survivre, atteindre mêlée Monolithe et placer Dagon allié, plan irréalisable ; préserver état de preuve.
- **Duo** : placeur avant le frappeur peut éloigner dE et placer un combattant en mêlée de mE ; si le frappeur agit **avant**, un décès précoce de mE modifie l'UID/contrôleur de dA à un moment imprévu.
- **Trio** : tank maintient Nécronyx, frappeur tue Monolithe, soigneur protège dA **avant** la fenêtre des ennemis. Si dA meurt, replanifier le cycle entier, ne pas supposer une nouvelle mort du boss immédiatement possible.
- **Quatuor** : placeur/tank, dégâts mêlée, soin/protection, désenvoûteur/contrôle peuvent exploiter le bouclier d'un tour et Nécronyx 3 tours ; tous doivent recalculer distance en fin de tour et garder dA en mêlée Bethel. Aucune attaque d'un allié ne devient l'attaque du Dagon sans UID réel.
- **Compagnons/invocations** : équipe, contrôleur, ordres de tour et distances restent des variables d'entrée ; pas de règle spéciale attribuée sans source.

## Blocages et niveau de clôture

**Non prouvés** : UID après changement de camp, priorité de contrôle de dA, ordre exact de résurrection lors de death(dA), phase d'expiration Nécronyx, ordre des dégâts simultanés/poisons, événements d'Aquazomb. **À demander** : replay 3.7.13.17 montrant deux cycles Monolithe→Dagon ami→mort Dagon→réapparition ennemis, puis fatal Bethel avec et sans Nécronyx.

**Livré** : spécification et tests **proposés seulement**, pas une mécanique validée par serveur. Les valeurs d'ancienne version « Nécronyx passe tour » et « ressuscité invulnérable 1 tour » sont explicitement **obsolete-pour-cible**.
