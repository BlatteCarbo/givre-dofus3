# Fiabilité Discord — audit du 20 octobre 2026

Périmètre : `WebhookService`, notifications d'anomalies et persistance `BotSettings` / `BotSettingsStore`. Vérification **hors ligne** à l'aide d'un `HttpMessageHandler` simulé. Aucun webhook réel, aucun client Dofus, aucun compte utilisé.

## Problèmes vérifiés et corrections

| Cas | Comportement désormais appliqué |
| --- | --- |
| HTTP 204 / 2xx | Seule une réponse reçue avec statut 2xx constitue une confirmation d'envoi. |
| HTTP 429 | Un **seul** nouvel essai si `Retry-After` / `X-RateLimit-Reset-After` permet d'attendre au plus 2 secondes ; valeur décimale comprise. Si le serveur demande davantage, refus explicite sans court-circuiter la limite. |
| HTTP 3xx / 4xx / 5xx | Résultat négatif, code HTTP seul dans le diagnostic. Les redirections automatiques sont désactivées sur le client réel pour éviter une retransmission vers une autre origine. |
| Timeout / erreur réseau | Livraison **non confirmée** ; aucun faux ACK et aucune répétition automatique en cas d'issue ambiguë. |
| Entrée invalide | Refus HTTPS invalide, URL avec fragment, identifiants d'URL, message vide ou dépassant 2 000 caractères. Aucun appel HTTP. |
| Doublons d'anomalies | Identité de destination SHA-256 + ID serveur authentifié lorsqu'il est disponible (sinon nom ou PID) + sous-zone + ouverture/fermeture. Une requête en vol bloque le doublon. TTL 15 minutes uniquement après confirmation 2xx. |
| Mémoire | 512 entrées maximum, avec purge des confirmations expirées. À capacité pleine, refus observable ; une confirmation encore dans ses 15 minutes n'est plus évincée. |
| Fermeture d'anomalie | L'absence d'état lors d'une connexion ou reconnexion ne prouve plus une fermeture. Notification locale à l'expiration d'un timer effectivement connu ; message de fermeture explicite toujours pris en compte. |
| Persistance Discord | `DiscordWebhookProtected` chiffré DPAPI CurrentUser. Les propriétés URL en mémoire ne sont pas sérialisées. Migration des anciennes valeurs JSON et protection du backup `.bak`, y compris si le fichier principal était déjà migré, sans écraser son historique de paramètres. |
| Fuites de diagnostic | Aucun log du webhook, de `Exception.Message`, du corps HTTP, du chemin du fichier ou du contenu chiffré. |

## Tests et reproduction

```powershell
dotnet run --project AnomalyAlertReliabilityHarness/AnomalyAlertReliabilityHarness.csproj -c Release
dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore -clp:ErrorsOnly
```

Le harness lie directement les quatre sources de production (et `AtomicJson`), teste HTTP 204/429/500/307, exception, durée d'attente, épuisement de capacité, séparation des serveurs, clé DPAPI, migration et réouverture Windows. Les réponses HTTP sont **100 % simulées**.

## Limites et points de vigilance

- Le succès HTTP 2xx atteste l'acceptation par le point de terminaison ; il ne garantit ni lecture du message Discord ni livraison à un destinataire final.
- Une exception/expiration réseau peut survenir **après** l'acceptation côté serveur. Le service annonce donc une issue non confirmée sans second envoi automatique ; d'autres événements pourraient toutefois retenter après cet échec.
- La déduplication est volontairement en mémoire : un redémarrage de l'application réinitialise les 15 minutes. Le lien PID d'une anomalie suivie est également une donnée de session ; une reconnexion peut interrompre le suivi local jusqu'à un nouveau lancement d'expédition.
- Quand le délai `Retry-After` dépasse 2 secondes ou quand le 429 persiste au deuxième essai, l'alerte échoue visiblement. Il n'existe pas de file durable de reprise différée.
- Les anciennes archives `.bad-*` ou les copies de l'utilisateur peuvent encore conserver les données qui s'y trouvaient avant la migration ; elles ne sont pas altérées par cet audit. De même, une panne de disque/permissions peut empêcher la réécriture d'un `.bak`, signalée via le diagnostic.
- Les appels Lua historiques à `WebhookService.Post` restent sans attente par contrat ; les seules alertes d'anomalie et le test UI attendent `PostAsync` pour établir leur succès. Le service HTTPS générique n'impose pas un domaine Discord, afin de préserver les consommateurs existants.

**Aucune modification sur le tenant, les comptes, le client Dofus, le MCP ou les autres fichiers de workers. Aucun commit ni push.**
