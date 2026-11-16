# N02 — Chaîne de preuve et manques

**Commande effectivement lancée** :

```powershell
python docs/cos-dofus-video/rapports-v2/N02/scan_incidents.py
```

**Code retour 0** et sortie `INCIDENT_SCAN_OK files=3348 prudent=411 companion=37 empty=True moves=True buff=False`. Le JSON [incidents_anonymises.json](incidents_anonymises.json) a ensuite été parsé en PowerShell, avec **deux sélectionneurs qui pointent sur le même log** : `prudent_companion_no_cast` et `consecutive_companion_movements`. Ils ne doivent donc **pas** être comptés comme deux incidents indépendants.

| Champ anonymisé | Valeur relevée |
| --- | --- |
| Date du journal | 2026-11-04 |
| SHA256 source | `c855496fbf462fd2d219ecc4993c0fdf45b12071a11a1932c72d6259c5eefc70` |
| Démarrage compagnon | ligne 83, **t = +7,734 s** |
| Dernière observation de la fenêtre | **t = +7,871 s** |
| Lignes de cast correspondant au motif de l'analyseur | **0** |
| Lignes de déplacement correspondant au motif | **3** |
| Événement explicite PA=11 / PM=6 reconnu dans cette fenêtre | **0** |
| Résultat serveur individualisé avec identifiant de commande | **Non lu** |

**Attention** : le résultat est « zéro occurrence de **motifs de cast** » sur un résumé de journal, pas zéro appel serveur garanti. Les résultats peuvent changer si le moteur utilise d'autres formulations. Les noms, IDs longs, PID et texte original ne sont pas reproduits.

### Provenance du modèle de journal

- `GivreClone.App/Services/Combat/CombatJournal.cs:48-77` ingère les sources `[combat]` et `[turbo-metric]`, tronque la fenêtre en cas d'inactivité, classe et enregistre les lignes.
- `CombatJournal.cs:89-101,104-121` différencie sort, mouvement, refus, tour ; tout message non reconnu peut tomber dans `Info`.
- `CombatJournal.cs:124-163` résume les tours sans casts et les déplacements refusés : **inférences depuis le texte**, pas preuve des effets.
- `docs/COMBAT_REPLAY.md:5-24` admet explicitement que la raison de certaines décisions n'est pas journalisée.
- `docs/codex/DIAGNOSTIC_COMBAT_20261023.md:23-31` identifie des chemins de timeout/barrière Turbo possibles, historiques : **pas la cause établie du compagnon ici**.

**Champs indispensables non obtenus** : `actorUid`, `ownerUid`, `controlledBy`, `round/epoch`, PA/PM observés à l'ouverture du tour, sorts/conditions triés, plan choisi et alternatif, paquet de commande cast, accusé et **résultat serveur** ; variations PM par marche ; cible et utilité conditionnelle des buffs.
