# GivreClone — provenance d'une fermeture TCP après changement de proxy

13 novembre 2026. Mission de sécurité hors jeu, conduite après l'audit
lecture seule du chemin `TcpProxyServer.ConnectionClosed` →
`BotOrchestrator` → `SessionBinder.Forget`.

## Interleaving problématique

`ConnectionClosed` transmet seulement un `int connectionId`.
`BotOrchestrator` peut conserver un callback provenant d'un proxy A,
alors que le binder a été détaché de A puis rattaché à un proxy B.
Contrairement à un redémarrage du **même** proxy (IDs croissants),
B crée un **nouvel espace** d'IDs et peut attribuer à son premier lien
le même numéro qu'A. Une notification tardive `Closed(A, 1)`,
si réduite à `Forget(1)`, pouvait alors supprimer le lien B(1) ou
le bloquer avant sa première capture.

Ce risque est conditionnel au changement de l'instance proxy : le
`BotHost` standard conserve le même objet proxy. Il ne prouve pas
une exploitation en production, mais la fonction publique d'attachement
le permet et la confiance ne doit pas reposer sur l'absence supposée
d'un callback tardif.

## Correctif

- `SessionBinder.ForgetFrom(TcpProxyServer source, int id)` vérifie
  sous le verrou de collection que le proxy source est bien celui
  actuellement attaché (ou, pendant l'intervalle détaché, le dernier
  proxy détaché). Sinon il refuse **avant** toute mutation de
  tombstone, de session ou de PID.
- `SessionBinder.Forget(int id)` reste le port explicite historique
  pour les callers possédant déjà leur autorité de fermeture ; les
  callbacks réels du transport ne doivent pas l'utiliser.
- `BotOrchestrator.NotifyConnectionClosed(int id)` conserve
  désormais la provenance de son proxy propriétaire en appelant
  `Binder.ForgetFrom(_proxy, id)`.
- `GivreClone.Tests/SessionBinderCloseProvenanceTests.cs` exerce
  deux proxys A/B de même ID sans ouvrir de listener, rejette
  `Closed(A)` après `Attach(B)`, vérifie qu'un vrai `Closed(B)`
  agit normalement, et confirme la conservation d'une fermeture
  d'A reçue pendant son intervalle détaché avant son propre restart.

## Limites

Cette correction traite la **provenance de l'instance** pour le
callback de close. Elle ne crée aucun marqueur de génération de lien
sur `CapturedMessage`, n'attend pas les tâches `AcceptLoopAsync` /
`PumpAsync` au `Stop`, et ne linéarise pas le dernier
`MessageCaptured` et `ConnectionClosed`.

Un ancien pump peut encore publier tardivement avant que son
tombstone exact soit observé ; une future correction doit s'appuyer
sur une vraie génération du transport ou sur un drain démontrable,
sans déduire des ACK de sa présence.

## Validation

Compilation et tests ciblés incluant `SessionBinderCloseProvenanceTests`,
`SessionBinderClosedBeforeFirstFrameTests`, les régressions TCP et
le garde fiscal HDV partagé : **34/34 PASS, 0 échec, exit 0**.
Journal : `docs/codex/validation-20261113-hdv-tcp-provenance-targeted.log`.
Les tests restent synthétiques et hors jeu.

**Régression finale** : **5 870 PASS / 2 ignorés / 0 échec**
pour `GivreClone.Tests` hors Live, **362/362 PureTests**
et **65 assertions shadow PASS**, toutes `exit 0`.
Journaux `docs/codex/validation-20261113-hdv-tcp-provenance-*.log`.
Ce ne sont pas des preuves de fonctionnement sur serveur live.
