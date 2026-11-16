# N01 — Provenance et preuves locales

**Date de vérification : 04/11/2026.** Toutes les sources du client ci-dessous sont des fichiers en **lecture seule**, sous `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/`.

| Fichier source relatif à l'installation | Octets | SHA-256 réel |
| --- | ---: | --- |
| `Dofus_Data/StreamingAssets/version` | 113 | `302ebf852253f99af779869ae5987a05e19e45e9113f52b30468ca1115a6753d` |
| `Dofus_Data/StreamingAssets/Content/Data/data_assets_breedsdataroot.asset.bundle` | 3950 | `3d5332b763b7319e616f3220f2d1a23ff9cf9db2a5ccb6236715973e6ced2f0f` |
| `Dofus_Data/StreamingAssets/Content/Data/data_assets_spellsdataroot.asset.bundle` | 320298 | `96c830cd1add89cb0af1c613886a7f193fd38706d1cd27e51556bfad9b330e26` |
| `Dofus_Data/StreamingAssets/Content/Data/data_assets_spelllevelsdataroot.asset.bundle` | 1097950 | `525048d9a4406720ba79fb6a23826b9ac5e40d4a87dcd3c452e1a0ccc27b2a30` |
| `Dofus_Data/StreamingAssets/Content/Data/data_assets_spellvariantsdataroot.asset.bundle` | 4216 | `9cff1a5aafd46d3ebcc3a2a4d6c4f793d1c27427a9c35e8f8cd092ec0e8c170a` |
| `Dofus_Data/StreamingAssets/Content/I18n/fr.bin` | 28047670 | `40fb0c50526fdf084b2b8e16fd32f874a7ed633edc430f821f050b70dc7bb0ad` |

**Recoupement de l'identité du binaire complet** : `GameAssembly.dll`, 125827584 octets, SHA-256 `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591` ; `Dofus_Data/il2cpp_data/Metadata/global-metadata.dat`, 43768684 octets, SHA-256 `ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`. Les deux empreintes viennent de commandes `Get-FileHash -Algorithm SHA256` exécutées sur les fichiers installés, code retour 0, et concordent avec `native/GivreTurboRuntime/TacticalAutostart/METADATA-3.7.13.17.md:8-17` déjà présent dans le dépôt source (lu seulement).

Ces empreintes sont dans [CLIENT_SOURCES.json](CLIENT_SOURCES.json), qui porte en outre les nombres d'enregistrements **complets des bundles** (`SpellsDataRoot=17006`, `SpellLevelsDataRoot=34318`, `SpellVariantsDataRoot=431` et `BreedsDataRoot=19`). Les **176/364/2 040/88** sont le **sous-ensemble** des quatre classes ciblées. Ne pas confondre ces deux périmètres.

## Exécution réelle et résultat

Lecture de la version : `Version=3.7.13.17`, `BuildDate=2026-11-03T14:09:52+00:00`. Lecteur utilisé `UnityPy=1.25.4` présent avant l'enquête sous `Temp/senpi-unity-reader`. Le fallback de lecteur `6000.3.16f1` est requis parce que le bundle encode `0.0.0`; celui-ci ne modifie pas le fichier et ne prétend pas que le jeu affiche cette version Unity.

```powershell
python docs/cos-dofus-video/rapports-v2/N01/extract_spells.py
```

**Code retour 0**, extrait de la sortie réelle :

```json
{"version":"3.7.13.17","buildDate":"2026-11-03T14:09:52+00:00","totalSpells":176,"totalLevels":364,"totalEffects":2040,"totalVariants":88}
```

Bilan par classe : `breedId` 4/13/5/16 ; 44 sorts, 91 grades, 22 variantes chacune. `missingSpells=[]` et `danglingLevelIds=[]` pour chaque classe. Le script valide la concordance `objectsById.key` avec `references.RefIds[].data.id`, puis les associations `breedId → spellId → SpellLevelData.spellId+grade`. Aucune ligne synthétique n'est substituée à une absence.

## Lignes vérifiables, extraites du fichier source actuel

| Origine du CSV | Clé et champs de preuve | Ce que cela établit |
| --- | --- | --- |
| `LEVELS_3.7.13.17.csv` | Sram, `spellId=12914`, grade 1, `apCost=2, minRange=1, range=5, maxCastPerTurn=2` | Valeurs sérialisées du **grade 1** de Piège Répulsif ; guide comparatif [Grimoire Sram 3.7.12](https://dofus-grimoire.fr/classes/sram/) pour PA/lancers |
| `LEVELS_3.7.13.17.csv` | Sram, `spellId=12921`, grade 1, `apCost=3, minRange=1, range=7, maxCastPerTurn=2` | Valeurs sérialisées du **grade 1** de Piège Mortel ; [Grimoire Sram](https://dofus-grimoire.fr/classes/sram/) pour PA/lancers |
| `LEVELS_3.7.13.17.csv` | Roublard, `spellId=13444`, grade 1, `apCost=2, minRange=1, range=6, maxCastPerTurn=2, maxCastPerTarget=1` | Champs de base de l'Explobombe ; [Grimoire Roublard 3.7.12](https://dofus-grimoire.fr/classes/roublard/) recoupe ce sous-ensemble |
| `EFFECTS_3.7.13.17.csv` | `12914 / grade1 / normal / index0: effectUid=208149, actionId=400` | `effectUid` identifie l'entrée ; `actionId` est une catégorie d'action, non le `spellId` |
| `EFFECTS_3.7.13.17.csv` | `12921 / grade1 / normal / index0: effectUid=208146, actionId=400` | Même catégorie d'action dans deux sorts différents : preuve qu'`actionId != spellId` |
| `EFFECTS_3.7.13.17.csv` | `13444 / grade1 / normal / index0..3: actionId=1008,406,951,951` | L'Explobombe porte plusieurs effets distincts ; ne pas réduire un sort à un seul `actionId` |

Les chiffres ci-dessus sont des **faits de fichier installé** et des jointures déterministes du parser, pas des essais de jeu. Les sources externes donnent un contrôle indépendant ponctuel ; elles ne garantissent pas la compatibilité de toutes les valeurs du client cible.

## Correspondance avec le lecteur existant, sans modification

Le code source du projet SOURCE a été inspecté en lecture seule :

- `GivreClone.App/Services/GameNameResolver.cs:120-136` : charge `spelllevelsdataroot`, dégâts, zones, critères d'état.
- `GivreClone.App/Services/GameNameResolver.cs:321-356` : `SpellVariantData.breedId`, `spellIds`.
- `GivreClone.App/Services/GameNameResolver.cs:374-449` : `SpellLevelData.spellId + grade`, `apCost, range, minRange, m_flags`.
- `GivreClone.App/Services/GameNameResolver.cs:556-705` : `effects`, `criticalEffect`, champs `actionId, effectElement, diceNum, diceSide, targetMask, zoneDescr, statesCriterion, delay, triggers`.
- `GivreClone.App/Services/LotM/GameDb.Spells.cs:6-24` : règles des grades, relances et niveau minimum.
- `native/GivreTurboRuntime/TacticalAutostart/METADATA-3.7.13.17.md:172-175` : lecteur UnityPy 1.25.4 et fallback déjà observés.

**Reproduction** : disposer du **même client installé avec hashes identiques**, du même lecteur UnityPy 1.25.4, et exécuter le script dans le répertoire isolé. Une autre version du client doit être refusée par le script via `--expected-version` ; pour comparer des versions utiliser N07, ne pas réétiqueter l'export.

**Inconnues :** la signification des actions dynamiques de bombes/poussées et les conditions exhaustives d'application des effets relèvent de l'interpréteur du jeu et du serveur. Aucun cast, trace réseau ou joueur n'a été ouvert.

