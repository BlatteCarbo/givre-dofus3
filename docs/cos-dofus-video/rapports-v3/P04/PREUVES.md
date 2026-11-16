# P04 — Preuves, commande, limites

**Sources d'entrée (SHA256 vérifié par `Get-FileHash -Algorithm SHA256`, code retour 0)** :

- `rapports-v2/N01/EFFECTS_3.7.13.17.csv`, 2 040 lignes de données : `cbd685e8852966961a7d155c17d7a21dd23ec8e45cac70aa31be06300f89384b`.
- `rapports-v2/N01/LEVELS_3.7.13.17.csv`, 364 grades : `0fd23fb8da2acee41fd341995ea976c4aa7f0671d73fd070b2ee130d8426a484`.
- `rapports-v2/N01/SPELLS_3.7.13.17.csv` : `a9f42b1b99741bb3c6dd8f9a23950559871fa9b198ef4cf04e6d6fbc603c7a99`.

Origine client, build et empreintes officiels : `rapports-v2/N01/PREUVES.md:3-20`, `CLIENT_SOURCES.json`. Les exports couverts sont **quatre classes**, non la totalité du client. Ancienne version 3.7.12.12 : `rapports-v2/N07/PREUVES.md:3-15,34-49`; l'égalité des 2 040 effets dans les champs comparés est documentaire et ne valide pas le serveur.

Commande exécutée depuis la racine du dépôt (PowerShell) :

```powershell
python docs/cos-dofus-video/rapports-v3/P04/audit_crit.py
```

**Résultat réel : exit code 0**, `P04_AUDIT_OK {"grades_total": 364, "grades_with_critical_rows": 183, "grades_with_dice_changes_by_index": 180, "grades_with_value_changes_by_index": 3, "grades_without_critical_rows": 181} examples=12`. Sortie matérialisée dans [critiques_candidates.json](critiques_candidates.json), avec source SHA, douze `spellId/grade/effectUid` par branche, variations et probability de grade.

L'algorithme vérifie séparément le nombre de lignes et des **multiensembles** de champs structurels, afin que des changements d'ordinal ne deviennent pas de faux changements de cible ou durée. Les compteurs absents du résultat signifient zéro (`structural_multiset_differences=0`, `different_effect_counts=0`). Les effets sont joints par index **uniquement pour les changements de valeurs**, qui sont ensuite recontrôlés par groupe/structure; `effectUid` n'est pas une clé stable interbranche.

Code observé en lecture seule : `SpellResolver.cs:62-79` (moyennes et effets différés), `:111-122` (115/418), `:143-149` (soin), `:340-393` (dégâts, vol de vie, soin, bouclier), `SpellDescriptor.cs:61-75` (catégories), `DamageEstimator.cs:52-53` (borne interne). La présence des actions dans le code ne prouve ni leur traduction pour ce sort ni leur effet serveur.

**Classification** : fichier client = extrait; source C# = implémentation lue; fixture = entrée candidate offline produite; calcul d'espérance = proposition; critique effectif/animation/ordre serveur = **Unknown**. Aucun exécutable, processus, client, compte, session ou test de gameplay n'a été invoqué.
