# M30 — Passage développeur : Protozorreur/Krobes en 3.7

**Portée** : spécification de recherche, sans code ni tests exécutés. **Sources** : [patch 3.7 publié 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; [guide Ventre de la Baleine actualisé](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) ; [recoupement 3.7 Dofuserie](https://www.dofuserie.com/mise-a-jour-3-7-dofus/). Client 3.7.13.17 présumé du kit, pas attesté. [Rapport](./RAPPORT.md) et [fiche de transitions](./FICHE_PROTOZORREUR_KROBES.md).

## Trois règles prioritaires

| Règle | Action requise | Niveau de preuve / alerte |
| --- | --- | --- |
| **M30-R02 et R03–R07** — un compteur **par UID de Krobe** | L'invulnérabilité ne disparaît pas : mort **conditionnelle** après **3** validations consécutives spécifiques à l'espèce. Échec → remise à 0 (guide). | **corrobore-source** patch + guide. Anciens guides 5 tours = **obsolete-pour-cible** ; phase d'évaluation encore à mesurer. |
| **M30-R08** — boss **6 tours Infection** | Générer glyphes violets sous personnages blessés, provoquer la **première attaque alignée** sur Protozorreur pour qu'il soit attiré dans glyphe, garder état Infection selon durée et renouvellements pendant six cycles. | **corrobore-source** 6 par patch, mécanisme de placement par guide. Vieux 10 tours = obsolète ; moment de mort et reset de compteur à valider en serveur. |
| **M30-R01/R09** — dégâts interceptés et Malamibe | Associer attaque sur Krobe invulnérable à une interception **par l'UID Malamibe vivant**, tenir compte de réinvocation/horloge de risque, de l'ordre d'initiative et d'une éventuelle mort d'acteur. | **corrobore-source** guide actuel. Ne pas additionner fictivement des dommages sur PV Krobe ni considérer un ACK d'attaque comme dommage reçu. |

## Contrat de simulation proposé

| Entité / événement | Champs à fournir réellement | Transition / garde |
| --- | --- | --- |
| Boss ou Krobe | monsterUid, species, side, alive, invulnerable, infectionState, conditionalCounter, phaseStamp, stateRemaining | Type d'espèce sélectionne **predicate** et nombre de validations : 3 Krobe, 6 boss. Indicateur UI restant = affichage, non preuve de décès. |
| État d'acteurs | playerUid, team, isCompanion, isSummon, alive, cell, initiativeOrder, currentActorUid | Bacterrib : tout adversaire hors 5PO à son début ; Verminocule : **personnage vivant** à contact ; vérifier companion/ invocations. |
| Journal de dégâts | sourceUid, targetUid, element, hitAccepted, healApplied, lifestealFlag, turnEpoch, order | Pataugerme : set distinct **terre/feu/eau/air** ; Tabacille : aucun **soin ordinaire** (vol de vie excepté par guide, à vérifier). Cible Krobe invulnérable → Malamibe si présente. |
| Journal déplacements/PM | targetUid, attemptedPmRemoval, actualPmDelta, oldCell, newCell, movementKind, sourceUid, collisionDamage, serverSequence | Virustine : tentative de retrait PM, échange, TP, poussée par joueur ou monstre allié annulent la validation ; dommage poussé sans déplacement = exception distincte à prouver. |
| Protoglyphe | glyphUid, cell, owner/sourceUid, creationCauseDamage, expiration, validForBossInfection | Positionner boss dans glyph via attraction de **première frappe** uniquement, suivre Infection et renouvellement avant expiration. |
| Déclencheur boss | firstHitOnBossThisTurn, alignedWithCaster, pathIncludesGlyph, actorUid, bossUid | Une frappe prématurée invalide le placement ultérieur **pour ce tour** si la première frappe a consommé le mécanisme. |
| Malamibe | minionUid, alive, summonedByUid, summonCycle, timeSinceSummon, nextTurnUid, redirectedDamage | Enregistrer naissance et mort de chaque nouvelle invocation, interception et échéance dangereuse (ancien guide : deux tours complets). |
| Confirmation serveur | session, commandId, actorUid, serverSource, receiptStatus, stateChanges, damageEvents, deathEvents | Refus/Unknown → recalcul depuis événements reçus, ne pas rejouer automatiquement un ordre dangereux. Mort conditionnelle est événement distinct des dommages. |

**Phases à ne pas inventer** : le guide valide Bacterrib/Verminocule à leur début de tour ; ne pas imposer cette même phase aux quatre éléments du Pataugerme, à l'absence de soin Tabacille ou à l'immobilité Virustine. Le patch fournit **nombre de tours**, pas ordonnancement complet des effets.

## Épreuves discriminantes proposées

| ID | Acteurs et timing | Résultat attendu | État |
| --- | --- | --- | --- |
| **P1 / N1** | Bacterrib à >5 PO pendant **3 BEGIN_TURN** vs adversaire à **5 PO** au deuxième. | P1 mort conditionnelle à 3 ; N1 compteur réinitialisé, pas de mort à 3 cycles calendaires. | proposé, non exécuté |
| **P2 / N2** | Pataugerme frappé **Terre/Feu/Eau/Air** avant chaque validation vs **Terre/Feu/Eau/Neutre**. | P2 valide ; N2 manque Air, aucun progrès. | proposé, non exécuté |
| **P3 / N3** | Verminocule avec personnage à contact au début du tour vs invocation seule. | P3 compte ; N3 ne compte pas dans modèle du guide ; compagnon = variante ouverte. | proposé, non exécuté |
| **N4** | Tabacille sans soin deux tours puis soin automatique d'un compagnon au troisième ; contrôle avec vol de vie uniquement. | Si soin automatique est admis, compteur reset ; vol de vie exception selon guide, à vérifier. | proposé, non exécuté |
| **N5** | Virustine : déplacement par autre Krobe, tentative retrait PM esquivée, puis collision sans déplacement. | Les deux premiers constituent des contre-exemples ; exception collision doit être tranchée par journaux serveur. | proposé, non exécuté |
| **P4 / N6** | Boss maintenu Infection **6** validations vs perte d'Infection entre la cinquième et sixième / première frappe avant alignement. | P4 mort à 6 ; N6 ne peut pas supposer même compteur ou nouvelle attraction. | proposé, non exécuté |
| **N7** | Malamibe vivant assez longtemps pour atteindre son échéance ; modifier initiative des deux frappeurs. | Danger d'équipe à la phase réelle, malgré compteur Krobe positif ; durée à certifier. | proposé, non exécuté |

**Compositions et initiative** : solo maintient une condition parmi les cinq ; duo place un personnage au contact avant tour Verminocule ; trio produit glyphe + aligne le boss + première frappe par acteurs distincts ; quatuor répartit éléments sur Pataugerme tout en assurant dégâts interceptés sur Malamibe. Le sort et les PA appartiennent toujours à leur lanceur. Déplacer le placeur après le frappeur change la légalité de la fenêtre sur Protozorreur ; un soigneur tardif peut casser Tabacille.

## Incertitudes bloquantes et prochaine preuve

1. Aucun tutoriel vidéo 2025/2026 Protozorreur authentifié avec ID et transcription MCP : **ne pas attribuer de timestamp ni d'observation vidéo**. Les sources sont textuelles, versionnées lorsque possible.
2. Seuil Bacterrib au voisinage **5/6/7 PO**, invocation/compagnon et phase d'évaluation doivent être mesurés par tests.
3. Déplacement Virustine, retrait PM tenté même esquivé, collision sans déplacement doivent produire des événements distinguables.
4. Échéance de la Malamibe et mort Protozorreur au sixième cycle : ordre exact des événements et compteur affiché ne suffisent pas.
5. Pour passer à **valide-test** : exécuter les fixtures P1/N1/P4/N6 avec état réel, UID, tour, position, état Infection, compteur et événement mort. Pour **valide-live** : replay complet autorisé et daté ; hors périmètre de cette mission.

**Priorité haute** : remplacer des temporisations 5/10 en 3.7 sans calculer les conditions expose le planificateur à des morts erronées ; la mécanique a des interactions non locales entre acteurs et phases de tour.
