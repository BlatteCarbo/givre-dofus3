# N07 — Sources, empreintes et preuve du diff

**Périmètre :** Sram (breedId 4), Roublard (13), Xélor (5), Éliotrope (16). **Ancien :** release tierce [v6.0_3.7.12.12](https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.7.12.12), publiée `2026-10-20T11:15:37Z`. **Nouveau :** client installé 3.7.13.17 daté `2026-11-03T14:09:52+00:00`; voir [N01/CLIENT_SOURCES.json](../N01/CLIENT_SOURCES.json).

## Empreintes des trois fichiers publiés

Les tailles et SHA256 de référence sont des attributs de la **release GitHub**, contrôlés par [API publique de la release](https://api.github.com/repos/ledouxm/dofus-sqlite/releases/tags/v6.0_3.7.12.12). Le statut local réel, y compris toute source incomplète, est sauvegardé dans [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json).

| Source ancienne téléchargeable | Taille déclarée (octets) | SHA256 attendu de release |
| --- | ---: | --- |
| [spellvariantsdata.json](https://github.com/ledouxm/dofus-sqlite/releases/download/v6.0_3.7.12.12/spellvariantsdata.json) | 214736 | `c0e3b8b48bcf8dbe06349019c5169f0b41e7ffa4c5d5eaa24f92a304dcc17029` |
| [spellsdata.json](https://github.com/ledouxm/dofus-sqlite/releases/download/v6.0_3.7.12.12/spellsdata.json) | 39766555 | `a44a36651903652bd26c5acb83f7aed78e1d18e29c5b742151474458d2b37a0f` |
| [spelllevelsdata.json](https://github.com/ledouxm/dofus-sqlite/releases/download/v6.0_3.7.12.12/spelllevelsdata.json) | 200736289 | `55301a2dc6580a8389ef09f7f861a8ed5618e818aa3715a1dac746b340775dbe` |

**Vérifications effectivement réussies sur les TROIS fichiers :** `spellvariantsdata.json` 214736 octets / SHA `c0e3b...`, `spellsdata.json` 39766555 / `a44a36...`, `spelllevelsdata.json` **200736289** / `55301a2dc6580a8389ef09f7f861a8ed5618e818aa3715a1dac746b340775dbe`. Toutes les tailles et tous les digests correspondent à la release GitHub : `oldAssets.variant/spell/level.status=verified` dans le manifeste. Le dernier diff a comparé exactement **88 variantes, 176 sorts, 364 grades et 2040 effets** de chacun des deux jeux de données, avec **zéro changement de champ dans chaque catégorie**.

Le nouveau client est manifesté dans [N01/CLIENT_SOURCES.json](../N01/CLIENT_SOURCES.json), notamment :

- `Dofus_Data/StreamingAssets/version` SHA256 `302ebf852253f99af779869ae5987a05e19e45e9113f52b30468ca1115a6753d`.
- `spellsdataroot` SHA256 `96c830cd1add89cb0af1c613886a7f193fd38706d1cd27e51556bfad9b330e26`.
- `spelllevelsdataroot` SHA256 `525048d9a4406720ba79fb6a23826b9ac5e40d4a87dcd3c452e1a0ccc27b2a30`.
- `spellvariantsdataroot` SHA256 `9cff1a5aafd46d3ebcc3a2a4d6c4f793d1c27427a9c35e8f8cd092ec0e8c170a`.

Les SHA256 ancien/nouveau **ne sont pas comparables directement**, car l'un porte sur JSON exporté et l'autre sur Unity bundles. Seules les lignes structurées et leurs identifiants sont comparées.

## Méthode indépendante et arrêt explicite

1. Télécharger les **fichiers de publication exacts**, dans N07 exclusivement, sans appel au jeu ni au processus.
2. Lire octets et SHA256 ; si un seul des deux diffère, tableau concerné `blocked`. **Un fichier de 50 Mo au lieu de 200 Mo est incomplet même si un préfixe paraît parseable.**
3. Déballer les `{"Array":[...]}` de l'ancien extracteur et parcourir en streaming les `references.RefIds[].data` (particulièrement les 200 Mo de `SpellLevelData`). Les fichiers du client actuel sont déjà normalisés par UnityPy via N01.
4. Retenir les IDs de sorts apparaissant dans **l'union des variantes anciennes et nouvelles** des quatre classes. Comparer variantes par `breedId+variantId`, sort par `spellId`, niveau par `spellId+grade`, effet par `spellId+grade+branch+effectUid` ; repli sur `slot:index` uniquement si UID manquant. L'ordinal est conservé comme champ, pas comme ID du sort ; une renumérotation d'UID exige un examen manuel.
5. Faire le diff récursif des champs en conservant valeurs avant/après **en JSON canonique**, avec statut machine. Déduire d'une table partielle seulement `blocked`, jamais « unchanged ».

```powershell
python docs/cos-dofus-video/rapports-v2/N07/compare_spell_versions.py
```

**Exécution finale réelle : code retour 0**. Extrait machine de [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json) :

```json
{"variant":{"status":"compared","oldRecords":88,"newRecords":88,"differences":0},
 "spell":{"status":"compared","oldRecords":176,"newRecords":176,"differences":0},
 "level":{"status":"compared","oldRecords":364,"newRecords":364,"differences":0},
 "effect":{"status":"compared","oldRecords":2040,"newRecords":2040,"differences":0}}
```

La première exécution intermédiaire a détecté `blocked` parce que le téléchargement était incomplet ; elle n'est **pas** la preuve finale. Les trois contrôles de hash sont maintenant positifs.

Le dernier état chiffré se lit dans [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json). Puisque **toutes** les catégories y portent `compared`, le [DIFF CSV](DIFF_3.7.12.12_VS_3.7.13.17.csv) réduit à son en-tête constitue bien la preuve d'**absence de changements sérialisés dans ce sous-ensemble**, à condition de reproduire les hashes d'entrée. Le moteur peut diverger sans que ces tables diffèrent ; aucune exécution en jeu n'a été réalisée.

