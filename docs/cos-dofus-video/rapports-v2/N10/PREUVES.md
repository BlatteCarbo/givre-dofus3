# N10 — Sources et protocole de chronologie

**Entrées privées en lecture seule** : résumés historiques `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/combat/YYYY-MM-DD/*.log`. L'export ne stocke **aucune ligne brute** ; la source sélectionnée est identifiable localement par `source_sha256` dans [turbo_chronologie_anonymisee.json](turbo_chronologie_anonymisee.json). Deux événements avec même type ne représentent pas forcément deux transactions uniques.

## Reproduction exécutée

```powershell
python docs/cos-dofus-video/rapports-v2/N10/extract_turbo.py
```

**Exit 0**, résultat `TURBO_TIMELINE_OK events=80...` daté 2026-11-04 ; JSON parsé ensuite par `ConvertFrom-Json`, vérification des sept catégories et des comptes. Voir [script](extract_turbo.py) : il choisit un log contenant au moins `request_tx`, `sequence_end_self`, `ack_tx` et `barrier_closed` et ordonne les lignes existantes, sans inventer d'événements. Une ligne `enemy_turn` n'a pas été trouvée dans l'export retenu, c'est une **absence d'instrumentation dans ce matériau**, pas une preuve que l'ennemi n'a pas joué.

## Origine des signaux instrumentés

| Code en lecture seule | Fait |
| --- | --- |
| `GivreClone.App/Services/LaunchService.Combat.cs:451` | `[turbo-metric] request_tx` émet `ack_to_request_ms` |
| `GivreClone.App/Services/LaunchService.Packets.cs:1291` | `sequence_end_self`, `request_to_sequence_end_ms` |
| `GivreClone.App/Services/LaunchService.Packets.cs:214` | `ack_tx` et `sequence_end_to_ack_ms` |
| `GivreClone.App/Services/LaunchService.Combat.cs:525-555` | seconde chance, `barrier_closed`, `request_to_ack_ms`, `sequence_end_to_ack_ms` ou non-fermeture |
| `GivreClone.App/Services/NativeTurboRuntime.cs:377-458` | bail natif séparé, publication journalisée en `[turbo-native]`; le replay de combat ne collecte pas ce flux par défaut |
| `GivreClone.App/Services/Combat/CombatJournal.cs:48-55` | Le réducteur de journal importe `[combat]`, `[combat-cast]`, `[turbo-metric]`, pas les `[turbo-native]` |
| `docs/codex/DIAGNOSTIC_COMBAT_20261023.md:3-11,23-31` | Audit historique des demandes/barrières, ACK recyclé, causes possibles ; ne prouve pas l'état du patch actuel |

**Aucun test de vitesse x8** ni événement live exécuté pour N10 ; seul l'export JSON d'un log ancien a été lancé. Le temps CPU, temps serveur, anims et chargement natif ne sont pas isolés par ce résumé.
