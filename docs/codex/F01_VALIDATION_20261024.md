# F01 — terminé le 24 octobre 2026

Périmètre : diagnostic et commandes MCP. Référence source : codex-dakal-20261024 ; corrections initiales 5703149, complétées par le commit contenant ce rapport. Ce statut ne clôture pas les fonctionnalités F02 à F16 ni leurs validations MITM.

## Corrections

- read_log privilégie le fichier actuellement ouvert de cette instance ; historique explicite conservé. Les métadonnées Windows périmées avaient conduit à un diagnostic erroné de journal vide.
- get_app_status expose activité de l'écrivain, file, erreur et dernier flush sans prendre le verrou disque.
- L'identifiant du compte et connectionStatus restent accessibles hors ligne.
- Les réponses communes d'ordre distinguent accepted/refused et portent completionConfirmed=false : une acceptation ne certifie pas une réponse serveur. Les états et consoles donnent l'issue observée.
- Défaut reproduit pendant F01 : l'erreur Lua apparaissait dans la console mais scriptRunning restait parfois vrai, car le dernier log précédait la fin de la tâche. L'hôte signale maintenant sa fin effective ; l'UI et la télémétrie relisent la tâche courante pour ne pas arrêter fictivement un script de remplacement.

## Preuves réelles, instance privée Dakal

Application PID 6092, stockage dakal-validation-20261024, canal GivreWork-Dakal-20261024. Aucun autre compte piloté ; aucun farm relancé.

1. Connexion Socket de Rangerbot confirmée par status=ingame et la carte 153879299, au-delà du retour accepted.
2. Refus de script hors ligne : ok=false, status=refused, reason=bot hors ligne. Connexion avec serveur explicite non pris en charge : refus précis, compte toujours offline. Aucun faux succès.
3. Script volontairement fautif accepté puis erreur finale F01_EXPECTED_RUNTIME_FAILURE_FINAL dans la console du compte, catégorie script/niveau danger ; scriptRunning=false après terminaison.
4. Script normal : marqueur F01_NORMAL_COMPLETION puis script terminé ; scriptRunning=false.
5. Déconnexion : état offline, PID 0, identifiant du compte conservé. reconnect_bot accepté puis entrée réelle en jeu sur le même personnage, nouveau PID Socket 1073741827 au lieu de 1073741825.
6. Après reconnexion : marqueur F01_AFTER_RECONNECT puis script terminé dans la console (curseurs 6 à 8), scriptRunning=false. read_log retrouve les traces fraîches sous le nouveau PID dans transport-2026-10-24_10-29-08.log.

## Rotation réelle et régression de fin des scripts

Commande reproductible : dotnet run --project tools/F01DiagnosticHarness -c Release.

Le harness utilise les classes compilées de l'application dans un TestRuns unique sans compte. Il écrit plus de 25 Mio via le véritable TransportDiag, avec son thread disque : rotation effective, premier marqueur PID 101 conservé dans l'ancien segment, dernier marqueur PID 202 dans le nouveau, lecture du segment courant même après falsification de la date de l'ancien, historique explicite lisible, file vide et aucune erreur disque. Aucun seuil de production abaissé.

Dernière preuve locale : TestRuns/f01-rotation-b309a129d7544b7fb3d9ee209d025ef6/userdata/logs. Seuls des marqueurs synthétiques sont écrits.

Le harness contrôle également le véritable signal RunFinished sur des tâches contrôlées : la fin de l'ancien run laisse le nouveau actif, puis la fin du nouveau rend Running faux. Aucun service de jeu initialisé pour ce contrôle.

## Validation

- Build application Release : zéro erreur, 206 avertissements existants.
- Build MCP Release : réussi après restauration des dépendances.
- Suite complète : 2615 réussis, 2 ignorés, zéro échec (26 s).
- Harness F01 : rotation et cycle des tâches réussis.
- Clés fr/en/es : aucun doublon ; git diff --check réussi.

État final : personnage connecté, aucun script actif. F01 terminé pour le diagnostic MCP ; cela ne certifie pas l'exécution des opérations métier encore inscrites dans F02–F16.
