# Blocage intermittent de la suite xUnit — 24 octobre 2026

Base : `e920c41`, branche `codex-flaky-20261024`, worktree
`GivreWork-Flaky-20261024`.

## Reproduction et cause

Après compilation de l'assembly App nécessaire au harnais MCP (sans lancer
l'application), la première répétition de la suite complète a bloqué. Le
collecteur `--blame-hang --blame-hang-timeout 60s` a arrêté le testhost et produit
une séquence avec un seul test non terminé :

`LocalHandshakeRelayTests.Relays_both_directions_feeds_the_decoder_tap_and_injects_toward_the_server`.

Une instrumentation temporaire a borné à cinq secondes la lecture de l'injection
sur `s2`. Elle a reproduit le défaut dès son premier passage ciblé :
`s1.Available=3, s2.Available=0`. Les seuls trois octets destinés à une injection
étaient `INJ` : ils arrivaient sur la première socket, pendant que le test lisait
la seconde sans échéance.

`AcceptTcpClientAsync` côté faux serveur se termine avant que la continuation de
`LocalHandshakeRelay.ServeAsync` enregistre le tunnel avec `_seen.TryAdd` et
`_gameTunnel`. Le test ouvrait immédiatement la connexion suivante ; les deux
continuations pouvaient enregistrer les tunnels dans l'ordre inverse. Le faux
serveur `s1` devenait alors la cible de l'injection dite « jeu ».

## Correctifs

- Attendre `OnGameTunnelOpened` et vérifier le rôle connexion/jeu avant d'ouvrir
  le tunnel suivant, dans les deux tests utilisant deux tunnels.
- Remplacer les lectures TCP supposées atomiques par `ReadExactlyAsync` et le
  délai fixe du tap par des signaux asynchrones ; tolérer les segments TCP.
- Pour les refus, attendre la fermeture effective du client et vérifier l'absence
  de connexion amont. Les cinq acceptations abandonnées par `WhenAny` pouvaient
  absorber la connexion témoin suivante ; elles ont été supprimées.
- Donner aux échanges relay/Zaap/TLS/pipes une échéance explicite de dix secondes
  et transmettre le jeton aux vraies E/S. Borner aussi l'arrêt des tâches agent
  après annulation (cinq secondes) et la destruction du serveur pipe (dix secondes).
- Les listeners du relais, TLS et gateway utilisent déjà le port éphémère `0`.
  Les tests Zaap gardent l'allocation éphémère existante imposée par l'API externe.

Aucun changement de production, aucun nouveau test désactivé. Le correctif
PacketBus sur thread dédié existait déjà dans la base ; aucun nouveau blocage
PacketBus n'a été observé.

## Validation

- Compilation Release des tests et de l'assembly App ; App jamais exécutée.
- 79 tests ciblés réussis.
- Répétitions complètes après correctif : 30 passages sans aucun blocage (le
  `--blame-hang` à 60 s ne s'est jamais déclenché) ; 29 réussis, 1 échoué
  (itération 3, voir « Limites »). Durée de 18 à 138 s selon la charge.

Commande de répétition (une seule instance à la fois) :

```powershell
dotnet test GivreClone.Tests -c Release --no-build --no-restore `
  --blame-hang --blame-hang-timeout 60s --blame-hang-dump-type mini `
  --results-directory TestResults/flaky/after-N --logger 'trx;LogFileName=results.trx'
```

Les compilations suivantes ont utilisé `-p:BuildProjectReferences=false` pour ne
pas reconstruire les projets voisins référencés par ce dépôt. La toute première
commande standard avait encore évalué ces références et affiché leurs sorties
`GivreProxy/GivreCore/bin/Release` et `GivreLauncher/GivreLauncher.Core/bin/Release`.
Aucun fichier source de ces dépendances n'a été édité.

Preuves locales non versionnées : `TestResults/flaky/before-1.log`,
`before-1/**/Sequence_*.xml`, dump associé, `before-1-stacks.txt`,
`diagnostic-1.log`, `targeted.log`, `after-summary.csv` et les TRX de répétition.

## Limites

Une campagne finie ne prouve pas l'absence de toute course. Les échéances font
échouer les tests concernés si la machine ne progresse plus. Les tests de
protocole gardent leurs deux exclusions préexistantes et leurs conditions sur
les données privées. Le cas DNS négatif `nope.invalid` utilise le résolveur de
l'OS avec une échéance de trente secondes ; les dialogues TCP/TLS/WebSocket
testés ici restent sur loopback. Aucun compte de jeu ni service de jeu contacté.

L'itération 3 s'est terminée sans blocage mais avec un échec distinct dans
`LocalControlTests.Console_wait_observes_new_lines_without_blocking_other_reads` :
`RegexMatchTimeoutException` dans `ConsoleJournal.Read`, sur le motif `READY`.
Le filtre de production impose 50 ms de temps réel et la machine a été mesurée
à 100 % de CPU. Ce budget peut expirer sous charge même sur une petite entrée.
Le délai de production n'a pas été modifié : ce défaut borné n'est pas la cause
du blocage étudié, et ne doit pas être confondu avec un hang ni masqué par une
exclusion du test.
