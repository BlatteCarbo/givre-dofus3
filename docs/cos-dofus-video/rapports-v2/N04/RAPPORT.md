# N04 — Replays minimaux anonymisés, issus des journaux existants

**4 novembre 2026 — deux séquences réelles de journaux, extraction et vérification offline réussies.** Les sources sont les résumés de combats déjà présents dans `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/combat/` ; aucune lecture de session ou commande jeu.

## Livraison et constat

Le programme [extract_replays.py](extract_replays.py) a parcouru les anciens journaux **en lecture seule**, choisi deux résumés distincts contenant un tour explicitement attribué à un **COMPAGNON** et un cas distinct de **refus de déplacement rapporté**, puis enregistré leurs événements utiles dans [replays_anonymises.json](replays_anonymises.json). Les **deux séquences proviennent de journaux existants**, pas d'une fixture inventée ; elles contiennent **11 événements chacune** dans l'extraction actuelle.

| Replay | Origine | Événements préservés | Limite |
| --- | --- | --- | --- |
| 1 | `historical_journal_summary` daté 2026-11-04, SHA dans JSON | 2 rapports de contrôle serveur, 7 déplacements de position observés, 1 démarrage de tour compagnon, 1 fin de tour | Ne contient ni paquets bruts, ni état initial complet, ni propriétaire exact du compagnon |
| 2 — négatif distinct | `historical_journal_summary` daté 2026-11-04, autre SHA | 3 positions vues, 2 rapports de contrôle serveur, 3 déplacements, 1 refus de déplacement rapporté, 1 démarrage de tour compagnon, 1 fin de tour | Refus **décrit par le journal**, pas confirmation de refus signée par un paquet brut ; ne pas réutiliser une cellule projetée comme déplacement réussi |

**Anonymisation stricte** : aucune ligne brute, nom de personnage, PID, compte, adresse, identifiant acteur original ou chemin source complet n'est écrit dans les données. Les identités par combat sont réduites à `actor_1`, `actor_2`… ; l'ordre relatif est préservé. Les cellules restent des coordonnées tactiques et ne permettent pas d'identifier le compte. Chaque événement conserve `seq`, `line` dans le journal d'origine, `t_sec`, `kind`, `actor` pseudonymisé et `evidence` indiquant **commande**, **résumé d'observation**, ou **refus rapporté**. L'empreinte SHA256 du journal permet un recoupement privé sans distribuer son contenu.

**Deux niveaux de preuve distincts** : une ligne `position_change_seen` est l'observation **du journal de combat**, pas l'original du paquet serveur ; un `server_control_reported` est une phrase du journal, pas une confirmation cryptographique. L'état initial reste `unknown_until_first_seen`; aucun événement non capté n'a été imputé. Les captures brutes/protobuf et le transfert exact du contrôle seraient nécessaires à un replay protocolaire complet.

## Contrôle exécuté

Les deux commandes suivantes ont abouti avec **exit 0** :

```powershell
python docs/cos-dofus-video/rapports-v2/N04/extract_replays.py
python docs/cos-dofus-video/rapports-v2/N04/validate_replays.py
```

Sorties exactes : `EXTRACT_OK replays=2 kinds=historical_journal_summary,historical_journal_summary first_events=11`, puis `VALIDATION_OK replays=2 historical=2`. Le JSON produit est d'environ **7 Ko** ; aucune anonymisation de journal brut hors de cet export n'a été faite.

La comparaison à une trace d'événement serveur demande encore une **lecture des paquets source** (sans connexion en jeu) et l'alignement `send → ACK → vrai résultat → changement de tour`. Voir [PREUVES.md](PREUVES.md) et [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md).
