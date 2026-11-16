# Progression de quêtes : tracker passif à preuves serveur (20/10/2026)

Ce lot ajoute `QuestProgressionTracker` (source pure et tests xUnit) : plans de jalons ordonnés, progression par personnage/session, distinction entre objectif prêt, revue manuelle, attente de détails serveur et validation réellement observée.

**Pas de nouvelle automatisation gameplay** : le tracker ne forge/envoie aucun message Dofus et ne lance pas de script. Road Creator v4 et `QuestReader` existent déjà ; l'adaptateur de branchement sur les événements runtime **reste à faire**, tout comme les essais en jeu et l'interface de suivi. Ce lot fournit le contrat sûr pour y parvenir.

## Contrat des événements

- Un événement admis doit être **entrant depuis le serveur**, lié à une **identité de personnage vérifiée**, avec une **session explicite** et une **séquence strictement croissante**.
- `FullSnapshot` remplace l'état par les quêtes terminées et les étapes/objectifs présents. Une requête d'information envoyée ou un retour d'injection ne compte pas.
- `ObjectiveValidated` ne porte pas l'étape dans le protocole : la confirmation n'est acceptée que si l'étape est déjà connue grâce à des détails serveur valides.
- Un changement de session efface les confirmations précédentes ; un changement de personnage sous la même session est refusé.
- Une nouvelle étape ne réutilise pas les validations de l'étape antérieure pour ses propres objectifs; les preuves d'objectifs antérieurs restent visibles dans la session. Une validation de quête complète doit être un événement serveur explicite.
- Plan invalide, objectifs dupliqués et instantané incohérent sont refusés avant toute mutation.

## Suite pour connecter au jeu

1. Dans l'adaptateur `LaunchService.LotI`, construire `QuestServerEvidence` uniquement après décodeur validé et vérification de `player_id == CharacterId(pid)`. Pour les événements sans `player_id`, conserver le contexte sûr du relais et l'identité active, ou ne rien émettre.
2. Associer une session immuable à une paire connexion/personnage (pas au seul PID Windows) et incrémenter une séquence monotone.
3. Brancher l'affichage en lecture seule et le Road Creator, en exigeant une confirmation côté serveur avant de passer au jalon suivant.
4. En cas de refus d'identité, de mapping inconnu ou de reconnexion incomplète, exposer UNKNOWN et suspendre la reprise.

## Validation

`QuestProgressionTrackerTests` couvre identité, changement de session, snapshot, séquence, paquet non serveur, détails absents, progression ordonnée et validation de quête. Les tests sont **hors jeu** et n'établissent pas de réussite de quête réelle.
