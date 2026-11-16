# Round 6 — extraction UID HDV sans action de vente

L'outil `tools/R6-Hdv-Operator-Test.ps1` inspecte en **lecture seule** les 20 segments locaux `transport-*.log` les plus récents. Seules six formes de diagnostic `HdvUidDiagnostic` sont acceptées, par expression régulière **ancrée sur la ligne entière** : UID sac, UID lot ou `?`, tentative d'injection, disposition, nom d'événement et PID. Le script ne copie pas de ligne de journal hors de ces six formats, pour exclure prix, tokens et comptes. Il n'appelle ni le jeu ni le relais.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/R6-Hdv-Operator-Test.ps1 -PidFilter 12345
```

Le CSV est écrit dans `artifacts/R6-hdv-operator/uid-only-review.csv`. Le chemin source d'un journal n'est jamais recopié, seulement son nom de segment. Un CSV vide signifie **aucune preuve**, y compris si un fichier plus ancien avait des données : le script écrase le rapport précédent.

Le déroulé de test humain avec un seul objet banal, UID sac → lot → reprix et retrait vérifié est documenté dans `docs/codex/R5_HDV_LIVE_TEST_PROTOCOL.md`. En particulier `inject=OK` ne prouve pas un ACK serveur, et le reprix actuel n'a pas de corrélation réseau stricte. Aucune opération HDV automatique n'est prévue dans ce script.

Les commandes `HdvUidDiagnostic` de Round 5 sont conservées en lecture seule pendant cette passe concurrente. Une vue interactive dédiant le verdict `NeedsReview` à l'opérateur reste à implémenter après intégration des fichiers Round 5 ; elle devra traiter `?` comme l'absence de preuve plutôt qu'un UID valide.
