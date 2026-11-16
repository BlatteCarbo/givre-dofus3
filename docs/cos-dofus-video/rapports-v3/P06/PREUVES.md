# P06 — Provenance, horloges, preuve et absence

Source historique N10 : `rapports-v2/N10/turbo_chronologie_anonymisee.json`, SHA256 **`153f24c529b4ddabcfc8b4e19d3f90c80790969d82d960600155bbba32aeb40d`** ; son log source privé **non recopié** porte SHA256 `4768ae7738df2098ba3901e61f6cc0199fb1a970a68b7baf7c95f3069d3d6e9f` (`N10/turbo_chronologie_anonymisee.json:2-5`). Extraction initiale N10 : `python docs/cos-dofus-video/rapports-v2/N10/extract_turbo.py`, retour **0**, `TURBO_TIMELINE_OK events=80` (`N10/PREUVES.md:3-12`) ; **P06 n'a pas rejoué cette extraction**. Le cas de +6,880/+6,950/+6,953s est directement situé aux lignes `35-74` de ce JSON, avec les métriques originales, qui ne sont pas nécessairement une même action sans `actionId`.

**PM par PM** : `rapports-v3/P03/jointure_n04_n10.json:85-326` relie par ordre chronologique les résumés N04/N10, au même SHA de log privé, sans exposer de PII. `N04/replays_anonymises.json:49-91` atteste trois changements de cellule mais aucun coût en PM ; `P03/jointure:85-325` place plusieurs demandes et fins de séquence dans le même intervalle. Ces colonnes ne portent ni `requestKind` ni `movePath` ni `actionId` appariés. `rapports-v2/N02/RAPPORT.md:9-16` est une **autre sélection historique** et ne prouve pas le coût 1 PM. Ce sont des indices à lier à des réponses autoritaires, pas une mesure de déplacement unitaire.

Source benchmark N03 : `rapports-v2/N03/RAPPORT.md:3-25`, `PREUVES.md`, `environment.json` et `measurements.jsonl`. L'étude N03 a exécuté cinq fixtures, 2 warmups +12 observations par cas (70 sorties), sous une **DLL compilée antérieure** à la modification du planner; ses latences ne mesurent pas le code source actuel. Aucun benchmark nouveau, aucune injection et aucune session en P06.

Source C# lue sur disque au 4/11 :

- `GivreClone.App/Services/LaunchService.Combat.Turn.cs:1093-1110` montre `Stopwatch.StartNew` autour de `CombatTurnPlanner.Choose`, `plan_ms,work,bounded`.
- `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:129-175` expose l'entrée `Choose`, `maxWork` et données de snapshot.
- `GivreClone.App/Services/LaunchService.Combat.cs:434-451,520-556` fournit `Stopwatch.GetTimestamp`, demande, refus, barrière et timeout.
- `GivreClone.App/Services/LaunchService.Packets.cs:202-215,1276-1299` fournit événements ACK, fin séquence propre/étrangère et action ID.
- `GivreClone.App/Services/NativeTurboRuntime.cs:88-105,390-445,450-479,950-966` décrit acceptation d'owner, bail timestampé, restauration et refus d'UNLOAD sans baseline.
- `GivreClone.App/Services/Combat/CombatJournal.cs:48-63` et `N10/PREUVES.md:15-25` : le résumé de combat importe `[turbo-metric]`, mais ne réunit pas par défaut le flux `[turbo-native]` dans le même timeline.

Empreintes de sources prises durant cette passe via `Get-FileHash` (code sortie 0) : `NativeTurboRuntime.cs` = `34f7e4f8691e1cbcd5e6e1ff5bd90af6bbede35e4a003df55e7f88eda2e1cf632`, `LaunchService.Packets.cs` = `9f335a79a6f0d53f8551fa2ffd217d661471f709da46e3d8146b09f2251ab57b`. Les fichiers restent susceptibles de changer sous d'autres travaux : ces empreintes ne garantissent aucune version déployée.

Rapport de validation historique et évolutif : `docs/validation/official-client-371317-20261103.md`. Pour retrouver sans décalage de lignes : `rg -n 'first-enemy-turn|timeScale activation|transition-only managed Turbo|Mean recorded fight duration' docs/validation/official-client-371317-20261103.md`; les extraits lus dans cette passe portent notamment `:949-955` (timings managed de bail, antérieurs à déploiement) et `:980-990` (25 combats serveur, pas rendu). Le fait qu'un ACK ennemi précède `timeScale` est transcrit antérieurement dans ce rapport, mais **les timestamps ne représentent pas une paire de traces natives synchronisées**.

**Origine et statut des champs** :

| Catégorie | Exemple | Statut |
| --- | --- | --- |
| Observé, log historique | `request_tx`, `sequence_end_self`, `ack_tx`, `barrier_closed` et valeurs N10 | `historical_journal_summary` |
| Code capable de mesurer | `plan_ms`, horodatage monotone `TurboNow`, bail UTC/monotonic | `read_code`, pas valeur actuelle |
| Mesuré autre build | N03 `median=64.898ms` cas ramifié | `historical_offline_benchmark_stale_dll` |
| À capter pour départager | `effectApplied`, `visualStart/end`, `nativeTimescaleFirstObserved`, `restoreConfirmed` | `not_observed` |
| Scénarios du protocole | `trace_schema.json` | `synthetic_not_executed` |

**Interdits de conclusion** : `ACK ≠ résultat serveur`; `lease published ≠ native activated`; `combat duration ≠ visible animation duration`; `planner plan_ms ≠ time since turn began`; `ancien binaire ≠ source courante`; `Socket≠MITM` sans cohortes et versions appariées.
