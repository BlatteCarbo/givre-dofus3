# GivreClone — adaptateur de marques pour les preuves de combos (hors jeu)

13 novembre 2026, client de référence **3.7.13.17**.

## Extension livrée par l'agent combat

Dans `GivreClone.App/Services/Combat/ClassMechanics.cs`, la nouvelle méthode
`ReadConfirmedFightMarkObservation` lit exclusivement les champs déjà décodés
par `MapFightReader` et `FightStatsReader` sur un
`GameActionFightEvent` entrant, à condition que l'appelant établisse :

- la provenance serveur, l'appartenance au combat courant, son FightId/génération ;
- un tour inclus dans la fenêtre et un ordre de capture valide ;
- un registre de marques de la même génération, nécessaire pour attribuer une activation à son auteur ;
- l'identité de la marque, l'auteur et la source cohérents sur la pose.

Observations possibles : **MarkPlaced**, **MarkActivated**, **MarkRemoved**,
**MarkDeactivated**. Le dernier type invalide une preuve qui dépendait de la marque.

Le protocole de ces événements ne fournit pas de corrélation de cast/ACK,
de spellId ou de cible suffisante ; les champs correspondants restent nuls.
L'adaptateur ne fabrique ni `Cast`, ni `WallActivated`, ni `DetonationConfirmed`,
ni Téléfrag. Le champ `mark_type=4` seul ne suffit pas à prouver un portail
Éliotrope (confusion possible avec une rune Huppermage).

La nouvelle suite `GivreClone.Tests/CombatComboObservationsIngressTests.cs`
comprend 13 cas synthétiques attendus : contrats positifs/négatifs de lecture,
provenance, génération, contrôle source/auteur, refus des faux ACKs et
invalidation par une désactivation.

## Limites impératives

Ce code fournit un **adaptateur de lecture testable** ; aucun hook runtime n'a été
ajouté à `LaunchService.Packets` pendant cette mission. Une observation marquée
`ServerConfirmed` n'est fiable que si le futur appelant fournit une admission
serveur authentifiée, une époque courante et un ordre causal prouvés. Les
fixtures construites en mémoire ne prouvent pas que le serveur actuel envoie
ces séquences en combat.

Pour obtenir une vraie tactique Roublard/Éliotrope, il faut encore raccorder
ce chemin à l'ingress gardé, attribuer les casts/effets avec corrélation indépendante,
suivre les murs/portails actifs et calculer des séquences PA/PM légales.
Le simple catalogue statique 3112→13455 ne prouve aucune explosion.

## Validation

Le prime a compilé les changements et exécuté un ensemble de régressions
comportant `CombatComboObservationsIngressTests`,
`DofusCaptureHostStartStopRaceTests`, `ClassMechanicsVerifiedTacticsTests`
et `BotHostConnectionCloseLifecycleTests` : **41/41 PASS**, zéro ignoré,
zéro échec, `exit 0`.
Journal : `docs/codex/validation-20261113-combo-capture-targeted.log`.
Ces preuves sont **hors jeu**, synthétiques et ne constituent pas une
preuve de hook runtime authentifié ou de combo réussi en serveur.
