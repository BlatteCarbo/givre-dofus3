# M33 — Passage développeur : état et coordination 5–8

**Sources :** [Compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html), [M06](../M06/RAPPORT.md), [M07](../M07/RAPPORT.md), [M08](../M08/RAPPORT.md). Statut des interactions groupe `hypothese` de conception ; aucun replay à huit combattants obtenu.

**Trois invariants :** (1) pas de double occupation finale confirmée ; (2) chaque coût PA/PM et chaque buff est compté par bénéficiaire et tour ; (3) aucun acteur/kill/soin attribué à une invocation ou un compagnon sans la source de l'événement.

**Schéma à proposer** : `FightKey, epoch, round, OrderedActors, CurrentCells, IntentVersion, PlannedEndCells, ConfirmedMoves, KillAssignments, ChallengeState, SummonOwnerMap, BossPhase, UnknownFields`. Conserver la différence entre cellule réservée par une intention et cellule réellement occupée.

**Tests à ajouter après validation des règles** : P1 séquence P/N/C/F/K avec 5 ressources distinctes ; N1 deux intentions pour même case ; N2 Eni boost avant/après finisseur ; N3 compagnon attaque ennemi assigné par un autre acteur sous Duel ; N4 tourelle détruite mais toujours prévue ; N5 boss change de phase entre deux acteurs ; N6 sortie de portail change pendant l'initiative. Tous proposés, zéro exécuté.

**Intégration** : `TeamIntentBoard` consulté stocke intentions et cellules de fin, les réducteurs serveur doivent rester source de vérité. Examiner les callsites et conserver `Unknown` pour les comptes et modes non pris en charge. Ce dossier ne modifie pas le code.

