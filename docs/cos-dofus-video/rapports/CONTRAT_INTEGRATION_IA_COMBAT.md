# Contrat de passage des tutoriels vers l'IA de combat GivreClone

**Audit documentaire en lecture seule — 4 novembre 2026.** Référence Git observée : `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`, **worktree non propre** : cette référence seule ne décrit pas les modifications non committées. Empreintes SHA-256 des fichiers de code **lus** : `SpellResolver.cs` = `0D31722C41F62B1ED175A533EC73B1790391E3BE60C25F045AEB6E05536CD2DF`, `CombatTurnPlanner.cs` = `7618468265FC2D32B6C186ADFB0E3917947A1C03B6C7C5E903344399BB142A3F`, `TeamIntentBoard.cs` = `9CB089A6965B7A15C0DF6C26295EF4918D96559A072EA6DCF27E24BEA3926576`, `FightMarks.cs` = `60B92F5CE836B63BA6C1B3FEF950E95B727814F6BC1785923A2F0DB4DF992C70`, `MapFightReader.cs` = `53495B20CA67F53CA8D3E878E6744C1838E7C2F98F6C2E88ADC57A2E7168D6FB`.

**Nature du livrable :** proposition de contrat technique et scénarios de tests fondés sur la lecture des portions indiquées. **Aucune modification de code, aucun build, aucun test xUnit ni action de jeu effectué au titre de cette documentation.** Pour toute intégration future, réinspecter les fichiers et leurs appels au moment d'intervenir : ils sont activement modifiés dans le projet.

## 1. Carte exacte des composants consultés

| Composant | Ce que la lecture démontre | Frontière clairement énoncée par le code | Lien vers dossiers |
| --- | --- | --- | --- |
| `GivreClone.Protocol/MapFightReader.cs` | Lecteurs de mort, slide, téléportation/porté/jet, permutation de positions, pose/désactivation/retrait de marques, disposition d'entités | Certains champs dépendent du mapping du client et le code documente plusieurs anciens formats corrigés ; un événement lu n'est pas une prédiction d'effet | [M01](M01/RAPPORT.md), [M03](M03/RAPPORT.md), [M05](M05/RAPPORT.md), [M09](M09/RAPPORT.md) |
| `GivreClone.Protocol/FightMarks.cs` | Maintient les marques ID/auteur/type/cases/actif ; activation d'une marque inconnue **ne crée pas** de marque ; retrait uniquement sur événement `ReadUnmark` | Le modèle consulté ne définit pas ici l'effet de chaque piège/glyphe ou sa date d'expiration ; il ne faut pas assimiler trigger à un retrait | [M01](M01/RAPPORT.md), [M09](M09/RAPPORT.md) |
| `GivreClone.App/Services/Combat/SpellResolver.cs` | Simulation pure d'effets immédiats ; événements `DamageResolved`, `HealingResolved`, `ShieldResolved`, `PointsResolved`, `StateResolved`, `MovementResolved`, `SummonResolved`, `MarkResolved` | Commentaires en tête : jets évalués à moyenne sans critique ; perte PA/PM sans esquive ; lignes différées conservées sans exécution ; buff durée/expiration et hooks exigent simulateur de tours ; lignes aléatoires et conditionnelles classées `Unmodeled`. Lors d'une poussée bloquée, dommages de collision non modélisés (`Walk`) | [M02](M02/RAPPORT.md), [M03](M03/RAPPORT.md), [M10](M10/RAPPORT.md), [M15](M15/RAPPORT.md), [M16](M16/RAPPORT.md) |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs` | Recherche bornée déterministe, déplacement avant sorts, jusqu'à trois casts, puis marche de sécurité, score incluant dégâts/soins/boucliers/contrôle/risque | `ForcedMovementSafe=false` par défaut, la sécurité du mouvement suppose l'absence établie de marks/portails/états bloquants ; les effets différés incomplets ne donnent pas de résultat certain | [M04](M04/RAPPORT.md), [M08](M08/RAPPORT.md) |
| `GivreClone.App/Services/Combat/TeamIntentBoard.cs` | Intentions de cible à dommages prévus, kill réservé, soutien heal/shield, cellule de fin ; publication ordonnée par round/horodatage | Une intention est une prévision et pas une résolution serveur. Les structures consultées ne portent pas ici l'historique complet acteur→ennemi du challenge Duel, ni les conditions de fin de tour de Lignée | [M07](M07/RAPPORT.md), [M08](M08/RAPPORT.md) |

Ne pas conclure qu'une fonctionnalité est absente de **tout le projet** à partir d'un seul fichier ; ce tableau ne concerne que le code consulté. La conception devra inspecter les appelants de `TeamIntentBoard.Publish/Read`, `MapFightReader`, les journaux de combat, les détails des sorts et les tests existants avant de changer quoi que ce soit.

## 2. Schéma d'état canonique à proposer

```text
CombatObservedState
  fightKey + sessionId + epoch + clientBuild
  round + currentActorId + turnOrder + nextTurn
  mapCells + walkability + los + obstacles
  actors[id] = team, ownerId?, classOrTemplate, cell?, positionCertainty,
               alive?, life, maxLife, shield, ap, mp, initiative,
               knownStates, absentStates, buffs[duration,origin], cooldowns
  marks[id] = kind, author, cells, active?, duration?, triggerRule?
  portals[id] = author, cell, open?, creationOrder?
  summons[id] = ownerId, templateId, cell, alive?, turn
  bossPhase = phaseId?, globalTurn, bossTurn, pendingFlags
  challenges[id] = params, perActorTargetHistory, lastTurnChecks
  evidence = lastServerEventIds, knownUnknownFields, patchVersion
```

Champ `?` signifie **indisponible tant qu'aucun événement ou export versionné ne le documente**. Le snapshot du simulateur et l'observation réseau doivent rester conceptuellement séparés. Une hypothèse sur la position cachée d'une cible ne devient pas une `positionCertainty=Confirmed` en raison d'un score d'IA élevé.

### Priorité de confiance d'une transition

```text
Intention planifiée (non exécutée)
  → Cast envoyé / accusé de transport (seulement transmission)
  → Résultat serveur confirmé ou observation cohérente
  → Nouvelle version d'état combat
  → Réévaluer les préconditions des autres actions
```

Sur refus : conserver l'état observé et produire un nouvel objectif. Sur timeout/Unknown : interdire tout rejeu destructeur sans preuve que le premier effet n'a pas eu lieu. Un tour d'équipe est une **chaîne de décisions séquentielles**, même si le plan global est calculé en amont.

## 3. Machines à états à introduire par domaine

| Domaine | État transitoire | Transition qui demande preuve | Échecs discriminants |
| --- | --- | --- | --- |
| Sram M01 | pose → actif → trigger → retrait (par ID) | `ReadMark`, `ReadMarkActivation`, `ReadUnmark`, déplacement confirmé | trigger sans unmark, ordre de pièges inversé, déplacement stoppé |
| Roublard M02 | bombe vivante → âgée/boostée → mur → détonation/destruction | vie/position des bombes, création/disparition réelle du mur | bombe détruite avant le tour, mauvaise cible dans mur |
| Xélor M03 | positions avant → permutation → Téléfrag → consommation | événement d'échange et application d'état, remboursement confirmé | case vide/occupée, ordre inversé, état consommé |
| Éliotrope M04 | portails posés → routage connu → ouverts/fermés → sortie | événements de portail, ordre de création, géométrie exacte | portail supplémentaire modifie la sortie, fermeture inconnue |
| Féca M09 | glyphe posé → aura ou début/fin de tour → effet → retrait | type glyphe, acteur occupant, tour global/acteur, unmark | téléportation à travers vs marche, déclenchement mauvais tour |
| Poussée M10 | mouvement demandé → déplacements réellement possibles → collision | slide et case d'arrivée, obstacle, résistance poussée | mur vs libre, rés. poussée différente, cible indéplaçable |
| Iop M14 | premier lancer Colère confirmé → relance en attente → fenêtre chargée → second lancer | sortId, rang, round du Iop, disponibilité et états cible | tour 4 refusé, initiative placeur après Iop |
| Eniripsa M15 | Encouragement cible → remplacé/expiré, soin et Insoignable | cible réelle, source/expiration, ordre des buffs | deux encouragements incompatibles, cible ne peut être soignée |
| Enutrof M16 | retrait tenté → réellement subi ou esquivé | variation des PM du monstre | -5 PM prédit malgré esquive, boss qui joue avant Enu |
| Zobal M12 | masque actif → changement → sorts accessibles, bouclier posé/expiré | état de masque, cible et effet de bouclier | variantes incompatibles, invocation ne reçoit pas la valeur annoncée |
| Boss M11/M25+ | phase N → trigger confirmé → phase N+1 → expiration/invulnérabilité | round global/boss, cible, état, source d'effet | tonneau mort, phase ancienne 3.6, challenge incompatible |

Le code de recherche de coups ne doit pas compter comme « vérifié » un effet représenté dans `DeferredSpellEffect` ou `UnmodeledSpellEffect`. Il peut simuler une borne d'utilité ou une action prudente, mais il faut l'indiquer et mesurer l'incertitude.

## 4. Grille de tests priorisée pour un développeur

| ID | Pourquoi ce test | Fixture minimale | Oracle / résultat à vérifier |
| --- | --- | --- | --- |
| T01 | Sécurité du réseau Sram | deux marques A/B, une cible, poussée, puis trigger sans unmark | A et B restent présentes tant que retrait non confirmé |
| T02 | Mur Roublard dérivé d'objets vivants | deux bombes, mur actif, une bombe meurt | aucun effet du mur fantôme après mort |
| T03 | Téléfrag Xélor et position confirmée | deux acteurs, case libre puis case occupée | états différents et aucun PA inventé sur cas refusé |
| T04 | Redirection Éliotrope | trois portails, deux équidistants, ajout d'un quatrième | sortie déterminée par règle prouvée ou `Unknown` |
| T05 | Glyphe Féca début/fin/aura | même occupant, arrivée par marche/téléportation, changement de tour | événements distincts, pas de double déclenchement |
| T06 | Poussée et collision | mur en bout de ligne vs terrain libre, résistance poussée différente | position fiable, effet collision classé Unmodeled tant que formule inconnue |
| T07 | Colère Iop chargée | premier lancer accepté vs refusé, T1→T4 | charge/cooldown lié à l'effet réel |
| T08 | Soutien Eniripsa | D reçoit Mot Stimulant, P reçoit Vivifiant, inversion de leurs tours | chaque bénéficiaire conserve uniquement son effet légitime |
| T09 | Entrave Enutrof | retrait confirmé vs esquivé, même déplacement futur ennemi | l'IA prévoit encore l'attaque ennemie si esquive |
| T10 | Challenge Duel | A attaque X, B attaque Y, B tente X | plan B→X interdit si et seulement si règle/rang actuel établis ; compagnon compté séparément si prouvé |
| T11 | Challenge De la même lignée | A finit son tour aligné avec B puis non aligné, cases DOFUS | contrôle exactement en fin de tour, pas après déplacement ultérieur |
| T12 | Délock Gourlo | tonneau adjacent sur son tour vs mort avant tour | fenêtre de résistance disponible seulement après effet |
| T13 | Phase Torkélonia/Solar | tour global ≠ tour boss, phase initiale contrôlée | changement de phase au bon compteur de version |
| T14 | Équipe à 4 et refus | placeur avant frappeur, puis swap des initiatives | l'attaque antérieure ne reçoit jamais un placement futur |
| T15 | Équipe 5–8, conflits | deux acteurs réservent la même cellule, soin sur cible morte | arbitrage sans double réservation et sans soin fantôme |

Ces tests sont des **spécifications proposées**. Un test n'est réussi qu'après fixture, exécution, assertion et sortie enregistrée. Ne pas créer de chiffres universels avec un jet vidéo ou des coûts de tutoriels sur ancienne version.

## 5. Ordre de travail justifié par le risque

**P0 — Cohérence de l'état et sécurité :** événements de pose/activation/retrait, mouvements sur cases 0 et 559, refus/Unknown et identités/époques, échec de challenge, portages, réponses de boss. Cela évite d'agir depuis un faux plateau.

**P1 — Résolution tactique réelle :** déclencheurs différés, collisions, états et consommations, invocations et Tourelles, portails, buffs ciblés et durées, esquive PA/PM. Un plan complexe sans ces notions est incapable de représenter certaines classes.

**P2 — Optimisation des synergies :** préconditions d'un combo, score net vs action simple, initiative multi-acteurs, objectifs/challenges, coût des préparations, adversaire et boss ; recherche bornée et mesure de performances.

**P3 — Couverture empirique :** exposer la version de chaque sort, case et équipement, fournir un replay par mécanique, puis élargir les trios et quatuors. Les 5 016 combinaisons indexées dans les CSV donnent un **carnet de travail**, pas des attentes de tests automatiquement satisfaites.

## 6. Risques de version et preuve restant à obtenir

La [mise à jour 3.7 du 6 octobre 2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) a modifié glyphes, classes, résistances et boss ; les tutoriels 2024–2025 et les données Grimoire **3.7.12** n'établissent pas une équivalence avec **3.7.13.17** (patch de début novembre). Pour chaque règle qui va entrer dans le code, joindre idéalement : (a) snapshot de sort client/rang/variante, (b) trace réseau expurgée du cas positif, (c) cas négatif, (d) test de régression, (e) note de version. Les dates de récupération de vidéo et les messages `IP_BLOCKED` ne remplacent pas ces preuves.

**Décision actuelle :** ce dossier est suffisamment structuré pour **spécifier** une intégration incrémentale, mais **aucune implémentation ni conformité combat réel** n'est prouvée par le laboratoire documentaire.

