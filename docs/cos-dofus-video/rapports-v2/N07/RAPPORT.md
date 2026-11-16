# N07 — Comparaison des données de sorts DOFUS 3.7.12.12 ↔ 3.7.13.17

**Résultat complet vérifié le 04/11/2026 : zéro différence de champ** sur les quatre classes Sram, Roublard, Xélor et Éliotrope, entre l'extraction publique 3.7.12.12 et les fichiers installés du client 3.7.13.17. La troisième source historique (`spelllevelsdata.json`, **200736289 octets**) a finalement été obtenue et son SHA-256 vérifié. Le comparateur a parcouru toutes les tables, **code retour 0**.

| Table / type | Enregistrements 3.7.12.12 | Enregistrements 3.7.13.17 | Changements de champs |
| --- | ---: | ---: | ---: |
| Variantes des quatre classes | 88 | 88 | **0** |
| Sorts liés aux variantes | 176 | 176 | **0** |
| Grades `SpellLevelData` | 364 | 364 | **0** |
| Effets normaux et critiques | 2040 | 2040 | **0** |

Les enregistrements sont rapprochés par `breedId+variantId`, `spellId`, `spellId+grade` et, pour les effets, `spellId+grade+branch+effectUid`. **Cela ne démontre aucune invariance des scripts d'exécution, des règles serveur ou du comportement du moteur en combat.** Le manifeste [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json) rend chaque conclusion auditables : toutes les catégories portent désormais `compared` et tous les SHA des archives anciennes sont `verified`.

## Périmètre de données et versions réelles

| Côté | Origine | Authentification possible et limite |
| --- | --- | --- |
| **Nouveau, 3.7.13.17** | Client installé `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3` ; version `Version=3.7.13.17`, build daté **03/11/2026 14:09:52 UTC** | [N01/CLIENT_SOURCES.json](../N01/CLIENT_SOURCES.json) : 4 bundles + I18n + version avec tailles et SHA256 ; source locale effective. |
| **Ancien, 3.7.12.12** | Release publique [ledouxm/dofus-sqlite v6.0_3.7.12.12](https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.7.12.12), publiée le **20/10/2026 à 11:15:37 UTC** | Hashes SHA256 publiés par GitHub API, comparés aux fichiers téléchargés. Cela valide **les octets de cette release tierce**, pas une signature d'Ankama ni l'installation 3.7.12.12. |

Les tables utilisées sont les racines sérialisées `SpellVariantsDataRoot`, `SpellsDataRoot` et `SpellLevelsDataRoot`. L'export récent est celui de N01 avec les quatre classes validées. Pour l'ancien, les tables JSON GitHub contiennent les objets `references.RefIds[].data` et des wrappers `{"Array":[...]}` ; ils sont normalisés sans interpréter les effets.

### Résultat machine et documentation

- [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json) : versions, dates, trois SHA256 vérifiés, décomptes et `status=compared` pour **les quatre catégories**.
- [DIFF_3.7.12.12_VS_3.7.13.17.csv](DIFF_3.7.12.12_VS_3.7.13.17.csv) : colonnes `entity,class,stableKey,change,field,old,new` ; **seul l'en-tête**, résultat réel des **0 différences** après vérification de toutes les tables.
- [ANCIENNE_BASE_CIBLEE.json](ANCIENNE_BASE_CIBLEE.json) : 88 variantes, 176 sorts et 364 grades anciens sélectionnés pour les quatre classes.
- [compare_spell_versions.py](compare_spell_versions.py) : lecture en streaming du fichier ancien volumineux, contrôle de la taille/du SHA-256 **avant** usage, tri déterministe des clés, diff récursif par champ.
- Anciennes données sources brutes **toutes hachées et vérifiées** : `spellvariantsdata.3.7.12.12.json` (214736 octets), `spellsdata.3.7.12.12.json` (39766555) et `spelllevelsdata.3.7.12.12.json` (200736289).

## Critères d'identité et interprétation

| Niveau | Clé stable de comparaison | Diff autorisé |
| --- | --- | --- |
| Variante | `breedId, variantId` | ajout/suppression de paire, changement de `spellIds[]` |
| Sort | `spellId` (ID `SpellData.id`) | tous champs sérialisés, y compris scripts et noms numériques ; **un nom affiché ne sert jamais de clé** |
| Grade | `spellId,grade` (un `SpellLevelData.id` supplémentaire subsiste comme champ) | PA, portée, masques/flags, lancers, cooldown, états, autres champs. Sous-champs `effects` et `criticalEffect` exclus pour ne pas compter deux fois |
| Effet | `spellId,grade,normal|critical,effectUid` ; repli explicite `slot:index` si UID absent | `__exportOrdinal` est conservé pour détecter le réordonnancement ; `baseEffectId`, `actionId`, `targetMask`, zones, durée, délai, dés, trigger et tout champ. Un UID peut être renuméroté selon version : suppression/ajout d'UID ne prouve pas un changement de comportement |

Pour chaque clé, le diff recense `record_added/removed`, `field_added/removed`, `element_added/removed`, `value_changed`. Un changement d'`actionId` serait une modification de la donnée sérialisée ; **son impact de gameplay exige une preuve de moteur**. Une différence `nameId` n'est pas un changement mécanique établi.

## Exécution de la comparaison

Commande exécutée (dossier du worker, aucun Git) :

```powershell
python docs/cos-dofus-video/rapports-v2/N07/compare_spell_versions.py
```

**Exécution finale : code retour 0 ; 88 variantes, 176 sorts, 364 grades et 2040 effets comparés ; zéro différence de champ dans les quatre catégories ; 0 ligne de changement écrite.** Une première exécution pendant le téléchargement signalait correctement `blocked` pour les niveaux/effets ; elle a été remplacée par le résultat complet après vérification du fichier. Le script n'interprète jamais une archive tronquée comme une source inchangée.

## Conclusion exploitable et limites

Le nouveau client **présente des enregistrements identiques** sur les variantes, métadonnées de sorts, grades, coûts, portées, contraintes et listes d'effets sérialisés **dans ce périmètre**. Il s'agit d'une comparaison entre une release publique tierce et une extraction locale réelle, avec chacun de leurs octets source certifiés par empreinte. Ne pas extrapoler aux autres 15 classes, aux compagnons, à la vie des marques, aux scripts natifs, à l'interpréteur de sorts ou au serveur.

Le diff complet exige les mêmes noms de champs et sources d'effets pour les deux versions, contrôlés par hash. L'ancien fichier est une extraction d'un tiers, et non des octets client d'Ankama sur cette machine. Toutes les sorties et commandes de cette mission ont été exécutées **hors jeu**, sans modification de code source ni manipulation de processus.

