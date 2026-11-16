# GivreClone — SessionBinder et fermetures avant la première trame

13 novembre 2026 — correction ciblée, sans client ni socket réel.

## Interleaving initial

1. Une connexion TCP est créée mais aucune capture `MessageCaptured`
   n'est encore parvenue au binder.
2. La connexion est fermée et l'événement de fermeture d'ID exact appelle
   `SessionBinder.Forget(id)`.
3. Ancien comportement : `Forget` retourne immédiatement `false`
   en absence de session, **sans enregistrer l'ID comme fermé**.
4. La pompe de lecture, déjà suspendue entre lecture et callback,
   reprend et livre une capture tardive.
5. `OnCaptured` construit alors une nouvelle `BotSession` pour un
   transport pourtant fermé (« ghost session »).

Une variante pouvait apparaître au redémarrage du même proxy :
`Attach` effaçait toujours les IDs retirés alors que certaines
pompes de la génération précédente pouvaient terminer en retard.

## Changements

Dans `GivreClone.Engine/SessionBinder.cs` :

- `Forget(connectionId)` inscrit le tombstone pour toute fermeture d'ID
  **strictement positif**, même si aucune session n'existe encore ;
  l'ancien booléen de retour conserve son sens (une session a-t-elle
  réellement été retirée ?).
- `Detach` marque comme retirés les IDs des sessions encore enregistrées,
  avant d'invalider l'époque et de retirer les autorisations.
- `Attach` conserve les tombstones lors du réattachement du **même**
  objet proxy, dont `_nextId` est monotone au fil des `Stop/Start`.
  Il les réinitialise pour une instance de proxy **différente**, dont
  les IDs constituent un autre espace et peuvent légitimement recommencer.

Ces protections s'ajoutent aux gardes d'époque existants sur les délégués
de capture. Aucune inférence de fermeture par PID, délai, message texte ou
send failed n'a été ajoutée.

## Tests

Nouveau `GivreClone.Tests/SessionBinderClosedBeforeFirstFrameTests.cs`
avec quatre tests **en mémoire, sans démarrer TcpProxyServer** :

- fermeture avant la première capture, qui refuse la trame tardive ;
- conservation des IDs fermés et révoqués lors d'un redémarrage du
  même proxy, sans refuser une connexion réellement nouvelle ;
- réutilisation légitime d'un numéro par un **nouveau** proxy ;
- absence de tombstone pour un ID invalide.

Les tests de cycle `BehaviorSessionLifecycleSafetyTests`,
`BotHostConnectionCloseLifecycleTests` et la FIFO MZP1 doivent continuer
de passer. Lancer ensuite la régression complète hors Live.

## Limites non résolues

Le binder n'observe pas directement l'état des `Link` TCP ; il s'appuie
sur le contrat exact `ConnectionClosed` de la dépendance compilée.
L'ancien fallback `obj/local-deps` ne fournit pas cet événement.
Un événement de fermeture retardé provenant d'une ancienne **instance**
de proxy ne transporte pas la provenance du proxy dans `Forget(id)`.
L'ordre amont des deux pompes et la fenêtre dernier contrôle→callback
restent également des sujets distincts à traiter.

Aucune validation en jeu, authentification de client ou ACK positif
ne résulte de ce correctif.

## Validation

**Compilation et régression ciblée réellement exécutées** :
`SessionBinderClosedBeforeFirstFrameTests`,
`BehaviorSessionLifecycleSafetyTests`,
`BotHostConnectionCloseLifecycleTests`,
`DofusCaptureHostStartStopRaceTests`,
`MZP1OrderedPublicationTests` : **26/26 PASS**,
zéro ignoré, zéro échec, `exit 0`.
Journal : `docs/codex/validation-20261113-sessionbinder-close-targeted.log`.

**Régression finale réellement exécutée** sur le binaire recompilé,
séquentiellement et avec `exit 0` pour chacune des trois commandes :

- `GivreClone.Tests`, filtre `FullyQualifiedName!~Live` :
  **5 862 PASS / 2 ignorés / 0 échec**, total 5 864,
  `docs/codex/validation-20261113-sessionbinder-full-nonlive.log`.
- `GivreClone.PureTests` : **362/362 PASS**,
  `docs/codex/validation-20261113-sessionbinder-puretests.log`.
- `CombatShadowComparison.Harness` : **65 assertions hors jeu PASS**,
  `docs/codex/validation-20261113-sessionbinder-shadow.log`.

Cette suite contient les quatre nouveaux cas de ce correctif
en plus des 5 858 réussites du checkpoint précédent.
