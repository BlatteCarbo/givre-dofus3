# N04 — Origines et preuves de reproduction

- **Corpus existant** : `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/combat/`, sous-dossiers datés. **3 347 fichiers** observés lors du contrôle d'inventaire le 04/11/2026 ; cette quantité ne veut dire ni 3 347 replays valides ni 3 347 combats uniques.
- **Schéma du résumé** : `GivreClone.App/Services/Combat/CombatJournal.cs:12-20,30-77,89-101,124-147`. Capture des lignes `[combat]` et `[turbo-metric]`, classement et clôture du combat. Les lignes résumées peuvent être des inférences.
- **Stockage** : `GivreClone.App/Services/Combat/CombatJournalService.cs:10-26,34-44,59-68` ; `docs/COMBAT_REPLAY.md:5-24` décrit les catégories et limites. **Ne pas confondre** `CombatJournal` et un dump Proto brut.
- **Source identifiable sans fuite** : les deux SHA256 `source_sha256` sont dans [replays_anonymises.json](replays_anonymises.json). Ils désignent des sources différentes, les temps `t_sec` et `line` sont conservés à l'intérieur de chaque combat ; l'export ne révèle pas les chemins complets, PID, personnages, comptes ni le texte brut.
- **Tests antérieurs distincts** : `GivreClone.Tests/CombatForcedMovesReplayTests.cs:9-16` avoue explicitement des paquets **synthétiques** construits depuis `fields.json`, qui ne sont pas des replays octet pour octet. L'export N04 vient de journaux historiques, mais demeure un **résumé d'événements**, non un flux brut de paquets.

## Validation exécutée

Le code [validate_replays.py](validate_replays.py) vérifie schéma, deux séquences, numérotation contiguë, temps ordonnés, alias `actor_N`, absence de champs `raw/text/pid`, catégories de provenance et quelques indicateurs de secrets. Résultat **VALIDATION_OK replays=2 historical=2**, **exit 0**.

Cette vérification est **structurelle** et ne garantit ni une suppression exhaustive de toutes formes inédites de données personnelles, ni une preuve serveur. Le JSON ne contient cependant pas de texte original et les seuls champs pseudonymisés sont construits explicitement depuis le parseur.

## Absences importantes

`raw_packet_bytes`, `authoritative_server_receipt_per_event`, `actor_account_id`, `full_stable_entity_id`, `exact_companion_owner`, `complete_initial_fight_state` sont inscrits explicitement dans `not_present_or_unproven`. Pour les événements de refus, l'absence d'une position confirmée doit conserver `Unknown` jusqu'à une vraie observation ultérieure. Ne pas inférer qu'une tentative a réussi.
