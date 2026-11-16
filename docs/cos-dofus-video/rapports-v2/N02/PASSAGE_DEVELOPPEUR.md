# N02 — Protocole pour départager les causes sans faire de jeu live

## Cas compagnon : contrôle et tour vide

En partant du résumé historique daté 2026-11-04 (SHA présent dans [PREUVES.md](PREUVES.md)), remonter hors jeu aux captures/protocoles déjà archivés pour établir un journal unifié `combatEpoch, round, actorUid, ownerUid, selectedActor, ap, mp, legalSpells, plannedActions, send, ack, confirmedServerResult, endTurn`. Inclure un **contre-cas** où le compagnon a un sort légal et une cible atteignable, puis le même état avec cible hors ligne de vue. Ne pas supposer que `Prudent` identifie l'acteur : c'est un mot retrouvé à l'échelle du journal.

## Cas marche segmentée

Pour chacun des **trois** mouvements de la fenêtre sélectionnée, rapprocher cellules avant/après, commandes de mouvement, ressource PM avant/après, ordre serveur et vérification de tacle. Si chaque commande couvre un seul PM malgré un trajet intégral légal, comparer hors jeu avec une **action de mouvement groupée** et l'invariant de sécurité (cellule interdite, pose de glyphe, collision, interruption). Ne pas optimiser le nombre de mouvements au détriment des confirmations.

## Buff utile/inutile

Le log analysé ne prouve pas de buff perdu. Construire hors jeu une fixture avec `S→B` et un sort de B qui **exploite** le bonus, puis `B→S` ou une cible hors portée où ce bonus expire inutilisé. Ne compter un bénéfice qu'après la disponibilité réelle d'une action du bénéficiaire. Reprendre les tests et les observations du [N08](../N08/RAPPORT.md) : routage d'action 115/118/138/418 déjà corrigé, donc ne pas refaire ce patch.

**Aucun correctif ni test de combat exécuté**, aucune configuration, compte, processus ou Git modifié. Le prochain essai doit être un replay pur avec source, commande, code de sortie et invariants discriminants.
