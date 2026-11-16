# Passage au développement — architecture, priorités et écart de preuve

Relecture **en lecture seule** du projet GivreClone du 4 novembre 2026. Dossier documentaire, aucune modification du moteur. Cette note établit où les nouvelles connaissances peuvent se rattacher et comment les tester ; elle ne prétend ni que les fonctionnalités sont déjà absentes en production ni que l'exécution des tests a été validée pendant cette mission.

## 1. Résultat principal

Les sources vidéo et guides 3.7 décrivent beaucoup de **dépendances temporelles** : le choix d'un sort à l'instant `t` dépend d'un état posé à `t-1`, d'un autre acteur ayant déjà joué et d'un événement serveur qui peut refuser, décaler ou modifier l'action. Le simulateur existant sait déjà représenter plusieurs **effets immédiats**, des états, des marques et des intentions d'équipe. Il doit faire la différence entre :

1. **Connaissance de la règle** : texte exact de sort, rang, variante, coût, portée, déclencheur, source de version, statut de preuve.
2. **Observation du combat** : combattant/source, ID d'invocation, effet, temps, cellule, tour, durée, ACK vs résultat réellement confirmé.
3. **Prédiction** : branche hypothétique `S_t → S_t+1`, avec cas d'échec, actions interdites et incertitudes.
4. **Décision** : action à tenter, score de danger, dépendance à des actions alliées effectivement intervenues.
5. **Réconciliation** : comparer événement serveur à la prédiction, invalider les segments devenus faux, demander une nouvelle décision sans rejouer un sort à issue inconnue.

**Aucune formule de dégâts de piège, mur, Téléfrag, portail, glyphe, résistance de boss ou tirage aléatoire ne doit être introduite uniquement depuis une transcription.**

## 2. État du code effectivement consulté

| Emplacement lu | Ce que le code visible permet d'affirmer | Limite à éprouver pour les nouvelles missions |
| --- | --- | --- |
| `GivreClone.Protocol/FightMarks.cs` | `ObservedFightMark(Id,Author,Type,Cells,Active)` et mise à jour sur pose, retrait, activation ; un trigger ne sert pas implicitement de preuve de retrait | Ordre de trigger, type de piège/glyphe, durée de vie, éventuels effets multiples et relation au tour |
| `GivreClone.Protocol/MapFightReader.cs` | Décodeurs d'événements de marque et de positions échangées ; enregistrement de cases sous conditions de messages | Plusieurs déplacements imbriqués et pré/post-positions à départager sur des traces réelles |
| `GivreClone.App/Services/LaunchService.Packets.cs` (environ lignes 1092–1104) | Les événements de marque sont observés et journalisés ; commentaire de source « effets et déclenchements à confirmer avant toute décision d'évitement » | Un journal de mark ne constitue pas un calcul des dommages différés |
| `GivreClone.App/Services/Combat/SpellResolver.cs` | `SpellBoard` avec états/cellules/marks et booléen `ThroughPortal`; effets immédiats, `DeferredSpellEffect` et `UnmodeledSpellEffect` explicitement distincts | Portails sous forme de booléen insuffisants pour router plusieurs entrées/sorties ; effets différés enregistrés mais pas avancés par une horloge de combat dans le resolver pur |
| `SpellResolver.cs` (commentaires lignes 62–66) | Utilise moyenne des jets sans critique, retrait PA/PM sans calcul d'esquive, choix aléatoire refusés comme non modélisés | Ne pas scorer « -3 PM » comme -3 confirmés ; probabilités à versionner ou borner ; évaluer l'incertitude |
| `SpellResolver.cs` (lignes 297–313) | Création de summon dans la simulation par ID provisoire, avec vie générique faute de données par niveau ; marks ajoutés comme `SpellMark` | Bombes, poupées, tourelles et compagnons demandent leurs propres stats, durée, contrôlabilité, phases et propriété |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs` | Recherche bornée (`BeamWidth=8`, `MaxDepth=3`, `MaxWork=180000`) ; score de dégâts/soins/protection/contrôle, pénalité pour différé ou non modélisé | Les préparations de 2–4 tours ne sont pas automatiquement un « combo prouvé » dans une recherche limitée au tour ; tester le gain et le coût de calcul avec un horizon étendu |
| `CombatTurnPlanner.cs` (ligne ~439) | Les mouvements forcés dont la sécurité n'est pas certifiée sont rejetés si `ForcedMovementSafe` est faux | Un piège inconnu, un portail ou un glyphe non observé ne doit pas être ignoré dans un plan de placement |
| `GivreClone.App/Services/Combat/TeamIntentBoard.cs` | Réserve des kills, dommages prévus, soins/boucliers et cases de fin des alliés, séparés par combat/round ; vues de l'équipe | Les intentions sont des *prévisions* et leur publication n'est pas une confirmation que l'allié a réussi l'action |
| `GivreClone.App/Services/LaunchService.Combat.cs` (lignes ~165–200) | Partage l'intention entre alliés contrôlés et certains acteurs contrôlés ; publie sous garde d'identité `SelfActorId` et de fight | Vérifier timing de validité, épisode de contrôle de compagnon et réconciliation des intentions refusées |
| `GivreClone.Tests/CombatForcedMovesReplayTests.cs` et `FightMarksIntegrationTests.cs` | Noms de suites existantes pour marks, mouvements et comportements observés | Elles ne constituent pas des preuves qu'un sort DOFUS 3.7 précis a été reproduit tant qu'un replay externe daté ne les relie pas à un cas |

Ce tableau décrit uniquement les segments effectivement inspectés. Il ne mesure pas la couverture de tout le code, des appels réels, de la latence, ni d'un serveur.

## 3. Priorités avec nouveaux cas concrets

| Priorité | Mécanique à ajouter/prouver | Champs essentiels | Tests discriminants liés |
| --- | --- | --- | --- |
| P0 — autorité événementielle | Un sort/une marque/position n'existe qu'après action confirmée ; refus et `Unknown` distincts | `fightId, actorId, session, actionId, eventType, preState, postState, proof` | Une pose Sram refusée n'ajoute aucun piège ; un trigger seul ne supprime pas la marque ([M01](M01/PASSAGE_DEVELOPPEUR.md)) |
| P0 — géométrie et états | Occupation, LdV, cas de portage, ennemis invisibles, tacle, destination et marque sur trajectoire | `cell, path, blocker, carriedActor, carriedBy, visibility, certainty, moveKind` | Jet Panda sur case occupée vs libre ([M23](M23/PASSAGE_DEVELOPPEUR.md)) ; sortie d'un glyphe ([M09](M09/PASSAGE_DEVELOPPEUR.md)) |
| P0 — budget/cooldown par acteur | Le bénéficiaire d'un PA/PM/soin/bouclier doit être exact, soumis à son initiative et aux durées | `beneficiary, source, startTurn, expireTurn, effectType, resourceDelta` | Eniripsa buff avant/après Iop et remplacement d'Encouragement ([M15](M15/PASSAGE_DEVELOPPEUR.md)) |
| P0 — résistances/entrave | Esquive PA/PM, résistance de poussée, invulnérabilités et phase du boss | `rollOrObservedDelta, mpAfter, pushResistance, phase, triggerProof` | -5 PM annoncé vs 0 PM réellement retiré ([M16](M16/PASSAGE_DEVELOPPEUR.md)), mur vs case libre ([M10](M10/PASSAGE_DEVELOPPEUR.md)) |
| P1 — mécanique de marque | Réseau Sram, glyphes Féca, runes Huppermage, bombes Roublard | `markId, owner, kind, cells, active, placedAt, triggeredAt, removedAt` | Pièges ordonnés et bombe détruite avant mur ([M01](M01/RAPPORT.md), [M02](M02/RAPPORT.md)) |
| P1 — historique des déplacements | Téléfrag et consommations par cible, remboursements bornés | `actorId, previousCell, currentCell, historySource, stateId, apRefund[spell,turn]` | Destination occupée vs vide ; état consommé avant le second sort ([M03](M03/PASSAGE_DEVELOPPEUR.md)) |
| P1 — réseau de portails | Topologie et ordre des portails, entrées, sorties, égalités, ouvert/fermé | `portalId, owner, cell, creationOrder, open, distanceRule, tieRule` | Équidistance inversée, portail fermé et autre portail hors du champ ([M04](M04/PASSAGE_DEVELOPPEUR.md)) |
| P1 — cycle d'invocations | Bombes, tourelles, poupées, arbres, compagnons, tonneaux de boss | `summonId, template, owner, alive, cell, initiative, states, spellRanks` | Tourelle détruite entre évolution et frappe ([M20](M20/RAPPORT.md)), tonneau mort avant délock ([M11](M11/RAPPORT.md)) |
| P1 — effets de plusieurs tours | Charge Iop, Combo Roublard, modes Zobal, propagation Sadida/Huppermage | `startTurn, expiry, appliedTo, consumedBy, phaseTransition` | Colère seulement après premier lancement confirmé, et fenêtre perdue par Bond ([M14](M14/RAPPORT.md)) |
| P1 — boss/challenges | Réussite d'objectif sous contrainte de tour, attribution d'attaquant, cycle global, cibles spéciaux | `challengeId, actor→target, round, globalTurn, bossTurn, states, exceptions` | Duel premier attaquant vs tueur, Lignée en fin de tour, Gourlo tonneau adjacent, Founoroshi et portails ([M07](M07/RAPPORT.md), [M11](M11/RAPPORT.md), [M25](M25/RAPPORT.md)) |
| P2 — qualité tactique | Synergie : contrôle → cible atteignable → protection → dégâts, vs stratégie indépendante et initiative inversée | `actorPlan, preconditions, consequences, survivalScore, fallback, workBudget` | Quatuor Panda/Élio/Féca/Iop avec placeur avant/après finisseur ([M08](M08/RAPPORT.md)) |

## 4. Contrat de simulation proposé

```text
struct CombatFact {
    fightKey: string
    actorId: long
    source: serverEvent | versionedClient | guide | video | hypothesis
    observedAtTurn: int
    correlationId: optional
    certainty: confirmed | unknown | refused | contradictory
}
struct ActionCandidate {
    actorId, spellId, rank, variant, targetActorId?, targetCell?
    preconditions: knownStates, knownCells, lineOfSight, resources, cooldown
    effects: immediate, onStartTurn, onEndTurn, onCrossing, onDeath, onSummonTurn
    sideEffects: push, attract, swap, teleport, state, mark, summon
    evidenceRef, fallback
}
struct TurnPlan {
    fightKey, actorId, initialRevision
    candidates[], dependencies[requiresConfirmedEvent]
    ifRefusedOrUnknown: invalidateFutureSteps, reobserve, replan
}
```

Ce contrat est une **proposition** : il ne présume aucun nouveau message protocolaire, n'utilise pas de valeur de sort fictive et ne constitue pas une API déjà installée.

## 5. Exemple pas à pas : deux agents qui ne doivent pas se mentir

**Scénario** : Pandawa P joue avant Forgelance F ; P compte jeter le monstre E sur une cellule où F peut exploiter sa Lance. Voir [M23](M23/FICHE_PANDAWA.md) et [M24](M24/FICHE_FORGELANCE.md).

1. P choisit `carried(E)→throw(E,targetCell)` sur un état observé, enregistre **l'intention** de jet avec `targetCell` ;
2. P tente l'action. Tant que le serveur n'atteste pas le transfert de E, F ne doit pas compter sur la nouvelle case comme fait acquis ;
3. Si le jet réussit, observer E sur sa case, réviser la ligne de vue et la portée de F. Évaluer une action de Lance tenant compte de ses propres PA, cooldown et case libre ;
4. Si jet refusé, case occupée ou état de Saoul incompatible, F replanifie depuis la position initiale observée ; les PA prévus de P ne sont ni donnés à F ni supposés débités sans événement ;
5. Si l'initiative F→P, F choisit selon le plateau initial ou prépare le tour suivant ; il ne peut pas bénéficier rétroactivement d'une attaque de zone qui n'est pas encore ouverte.

**Variation négative** : une rune/piège/glyphe inconnu traverse la case jetée. Ne pas déduire une sécurité de l'absence de marque **affichée** sans source de visibilité; conserver le résultat d'un effet différé comme inconnu tant que sa transition n'est pas observée.

## 6. Comment passer d'un rapport à un test sans extrapoler

- **Étape A : preuve**. Choisir un identifiant de règle du rapport, son URL/version, son instant vidéo ou texte documenté, et les champs manquants. Seules les règles corroborées puis reliées à une trace du client méritent de devenir des oracles précis.
- **Étape B : état initial**. Construire une fixture avec positions et identités exactes, rangs de sorts, équipes, ressources, effets connus/absents, rounds et contraintes de challenge.
- **Étape C : transitions adversariales**. Exécuter au minimum P1 (préconditions vraies), N1 (case obstruée), N2 (état absent), N3 (initiative inversée), N4 (refus ou Unknown), N5 (invocation détruite/effet expiré).
- **Étape D : double vérification**. Observer la séquence de messages et l'état stable après chaque action, pas seulement la liste de sorts souhaitée. Conserver le fichier replay, le hash et le build exact.
- **Étape E : scorer**. Comparer au plan sans interaction, mesurer dégât, risque de mort, ressources résiduelles, succès de challenge et coût de recherche dans les mêmes conditions.

Cette recherche documentaire n'a **exécuté aucun test ni replay**, et aucun enchaînement n'est marqué `valide-test` ou `valide-live`. L'intégration réelle restera une mission distincte après preuves client et permission explicite de développement.

