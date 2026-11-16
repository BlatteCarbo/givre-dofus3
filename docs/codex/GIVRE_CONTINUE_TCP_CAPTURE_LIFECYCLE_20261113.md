# GivreClone — arrêt/démarrage atomiques de la capture TCP

13 novembre 2026 — tests isolés, sans connexion au client de jeu.

## Problème

`DofusCaptureHost.Start` lançait le listener proxy avant de marquer
`_running = true`. Un `Stop` concurrent dans cet intervalle pouvait
retourner prématurément, laissant un listener actif. De même, si un
observateur `TcpProxyServer.Log` levait une exception après l'ouverture
du listener, `Start` quittait sans armement de `_running`, et `Stop`
ne pouvait plus arrêter le proxy.

## Correctif

Dans `GivreClone.Engine/DofusCaptureHost.cs` :

- cycle explicite `Stopped → Starting → Running → Stopping` et garde locale ;
- requête d'arrêt concurrent pendant `Starting` enregistrée et suivie
  jusqu'au rollback, sans verrou conservé sur les callbacks externes ;
- arrêt réentrant dans un callback de démarrage non bloquant (sinon deadlock) ;
- `Start` en échec exécute toujours le rollback `_proxy.Stop()` même si
  l'ouverture du listener a précédé l'exception ;
- diagnostics `Log` isolés, afin qu'un observateur en erreur ne bloque
  plus le cycle ; verrou d'injection conservé pour les actions déjà admises.

Les **quatre tests** ajoutés dans
`GivreClone.Tests/DofusCaptureHostStartStopRaceTests.cs` couvrent :
exception d'observateur proxy juste après ouverture listener, concurrence
`Start/Stop` sous barrière, arrêt réentrant et exception de log host.
Ils utilisent une liaison **loopback locale temporaire** avec autorité
refusant les clients, sans upstream DOFUS, sans injection ni vrai PID.

## Risques restant ouverts

Ce correctif de cycle du host ne règle **pas** :

- `SessionBinder.Forget(id)` qui n'enregistre pas de tombstone si aucune
  session n'avait encore été créée ; un callback retardé peut recréer un
  fantôme après fermeture ;
- `Attach` qui nettoie des tombstones alors que les anciennes pompes
  du proxy peuvent encore publier après `Stop/Start` ;
- le fallback `obj/local-deps/GivreCore` sans événement
  `ConnectionClosed`, ainsi que la réflexion qui n'impose pas ce hook ;
- une éventuelle contention de locks et des callbacks externes non bornés.

Ces points exigent une correction indépendante avec des tests d'entrelacement
déterministes. Ne pas prétendre que le proxy entier est exempt de courses.

## Validation

Le build ciblé a réussi et **41/41 tests** (observations combos,
capture TCP et régressions associées) sont passés avec `exit 0` :
`docs/codex/validation-20261113-combo-capture-targeted.log`.
Ces tests sont exclusivement hors jeu.
