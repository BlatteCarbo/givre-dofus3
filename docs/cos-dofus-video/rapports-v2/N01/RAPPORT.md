# N01 — Extraction réelle des sorts et effets du client DOFUS 3.7.13.17

**Résultat : export complet des données sérialisées des quatre classes prioritaires — Sram, Roublard, Xélor, Éliotrope.** Travail réalisé le 04/11/2026 en lecture seule du client local, sans démarrer le jeu ni changer le dépôt Givre. La version n'est pas extrapolée d'un guide : le fichier installé `Dofus_Data/StreamingAssets/version` porte `Version=3.7.13.17` et `BuildDate=2026-11-03T14:09:52+00:00`. Son SHA-256 est `302ebf852253f99af779869ae5987a05e19e45e9113f52b30468ca1115a6753d`.

| Classe | `breedId` | Sorts uniques | Grades `SpellLevelData` | Entrées `SpellVariantData` |
| --- | ---: | ---: | ---: | ---: |
| Sram | 4 | 44 | 91 | 22 |
| Roublard | 13 | 44 | 91 | 22 |
| Xélor | 5 | 44 | 91 | 22 |
| Éliotrope | 16 | 44 | 91 | 22 |
| **Total** | | **176** | **364** | **88** |

**2 040 effets** au total : chaque `effects[]` et `criticalEffect[]` est conservé avec son `effectUid`, `baseEffectId`, `actionId`, cible, masque, élément, durée, délai, déclencheur et géométrie. Une entrée d'effet n'est **ni un identifiant de sort ni une confirmation de résolution serveur**.

## Fichiers livrés dans N01

- [SORTS_3.7.13.17.json](SORTS_3.7.13.17.json) : champs natifs complets des classes, des variantes, des sorts et de chacun de leurs grades. Les effets restent dans les deux tableaux originaux du grade ; **source de vérité d'export**.
- [SPELLS_3.7.13.17.csv](SPELLS_3.7.13.17.csv) : nom localisé, `spellId`, `nameId`, ordre, script, niveaux de référence.
- [LEVELS_3.7.13.17.csv](LEVELS_3.7.13.17.csv) : clé `spellId + grade`, `levelId`, PA, portées, `m_flags`, limites par tour/cible, refroidissements et `statesCriterion`.
- [EFFECTS_3.7.13.17.csv](EFFECTS_3.7.13.17.csv) : clé de **comparaison** `spellId + grade + branch + effectIndex`, `effectUid` et `actionId` séparés, masques/zones/triggers/effectElement/dés.
- [VARIANTS_3.7.13.17.csv](VARIANTS_3.7.13.17.csv) : `breedId`, `variantId`, paires de sort réellement extraites.
- [CLIENT_SOURCES.json](CLIENT_SOURCES.json) : tailles, SHA256, version, nombre d'enregistrements et vérifications `missingSpells=[]` / `danglingLevelIds=[]`.
- [extract_spells.py](extract_spells.py) : méthode reproductible de lecture seule ; [probe_unitypy.py](probe_unitypy.py) : diagnostic de format qui n'écrit rien.

## Méthode vérifiable

1. Contrôle de version exact depuis `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/version`, puis empreintes SHA256 des fichiers source.
2. Réutilisation du lecteur **UnityPy 1.25.4 déjà présent** sous `%USERPROFILE%/AppData/Local/Temp/senpi-unity-reader`, sans installation. Le bundle embarque une version Unity non interprétable ; fallback **6000.3.16f1**, issu de l'en-tête `globalgamemanagers` déjà documenté dans `native/GivreTurboRuntime/TacticalAutostart/METADATA-3.7.13.17.md` (lignes 172–175). Le fallback est une condition de **parsing**, pas une affirmation sur le build DOFUS.
3. Extraction `references.RefIds` de chaque `DataRoot`, contrôle strict de concordance entre les `objectsById.m_keys` et les identifiants `data.id` ; association `SpellVariantData.breedId + spellIds[]` → `SpellData.id` → `SpellLevelData.spellId + grade`.
4. Localisation française grâce au fichier natif `Content/I18n/fr.bin`. Le `BreedData.shortNameId` est une chaîne numérique et n'est **pas** `BreedData.nameId`. Les 4 labels de classe résolus permettent de vérifier les `breedId`, sans les supposer.
5. Écriture des JSON complets puis CSV projetés. Tous les 176 sorts sont présents, les 364 niveaux existent, et les `spellLevels[]` de chaque sort ne pointent vers aucun `levelId` manquant. Les valeurs vides sont conservées, pas déduites.

Commande effectivement réussie, PowerShell (dans le worktree du worker) :

```powershell
python docs/cos-dofus-video/rapports-v2/N01/extract_spells.py
```

**Code retour : 0.** Sortie structurée : version `3.7.13.17`, 176 sorts, 364 niveaux, 2 040 effets, 88 variantes. La première tentative avait échoué faute de la bonne clé de classe `shortNameId` ; cette erreur a été corrigée et le résultat actuel provient du rerun réussi. Détails d'empreinte dans [PREUVES.md](PREUVES.md).

## Trois contrôles croisés concrets

| Sort de l'export 3.7.13.17 | Champs réellement relevés, grade 1 | Référence externe / limite |
| --- | --- | --- |
| Sram **Piège Répulsif** : `spellId=12914` | `apCost=2`, `minRange=1`, `range=5`, `maxCastPerTurn=2` ; l'effet normal `actionId=400` n'est pas un dégât certifié | [Dofus Grimoire, Sram, version déclarée 3.7.12, 20/10/2026](https://dofus-grimoire.fr/classes/sram/) : 2 PA, 2 lancers/tour. **Portée grade 1 du site non vérifiée** |
| Sram **Piège Mortel** : `spellId=12921` | `apCost=3`, `minRange=1`, `range=7`, `maxCastPerTurn=2`, effet normal `actionId=400` | [Dofus Grimoire, Sram 3.7.12](https://dofus-grimoire.fr/classes/sram/) : 3 PA, 2 poses/tour, « Le Doublé » de 2 Mortels+1 Répulsif coût 8 PA. **Pas de preuve de cycle de la marque** |
| Roublard **Explobombe** : `spellId=13444` | `apCost=2` de base, PO 1–6, `maxCastPerTurn=2`, `maxCastPerTarget=1`, effets `actionId=1008/406/951` | [Dofus Grimoire, Roublard 3.7.12](https://dofus-grimoire.fr/classes/roublard/) : caractéristiques de pose cohérentes, mais le **coût dynamique** lié aux bombes déjà présentes doit être appliqué en plus du champ de base |

Ces recoupements portent sur **des champs**, et non sur une identité exhaustive des règles entre 3.7.12 et 3.7.13.17. L'évolution exacte des grades et des effets fait l'objet de [N07](../N07/RAPPORT.md).

## Limites qui intéressent le développeur

- `effectUid` = identifiant d'instance de l'effet sérialisé ; `actionId` = type d'action ; `spellId` = sort, `levelId` = entrée de grade. Le parser ne confond aucun de ces identifiants.
- Le code existant `GivreClone.App/Services/GameNameResolver.cs` (lignes 120–136, 321–356, 374–449 et 556–705) connaît les champs, mais son `SpellZone()` choisit une zone représentative. Le JSON N01 conserve **chaque zone par effet et par branche** : indispensable pour bombes, portails, pièges, effets concurrents.
- `m_flags`, `targetMask`, `statesCriterion`, `triggers` et `actionId` ne doivent pas être interprétés avec une table inventée. Le bit de contrainte et la cible effective doivent être prouvés par la définition du client et l'oracle de serveur.
- L'extraction n'a pas lancé le jeu, n'a observé aucun cast serveur et ne prouve pas les bonus, collisions, cumul de marques ni l'ordre d'explosion. La classe et ses 44 sorts sont **documentés au niveau de données**, pas validés de bout en bout.
- Les « 44 sorts par classe » sont les `spellId` reliés à chaque `breedId` par **`SpellVariantsDataRoot`**, y compris les variantes. Ce n'est **pas** un inventaire certifié des sorts internes d'invocations, des scripts automatiques, des pièges ou des bombes détachés de cette liste.
- Ces 4 classes couvrent la mission N01 et ne signifient pas « 19 classes extraites ». Aucune configuration, code ou processus lié au jeu n'a été modifié.

**État final de N01 :** export livré/reproductible et quatre classes couvertes en données ; les tests de comportement et la comparaison de versions restent des travaux séparés. Voir [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md).

