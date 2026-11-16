# MZP0 — reconstruction statique d'un EXE détaché (sources C# + preuves)

**Statut au 16 octobre 2026 : pipeline codé et testé sur fixtures PE entièrement hors ligne ; aucun `GivreClone.exe` détaché vérifié n'a été fourni.** Le GivreClone actuellement ouvert et son installation restent exclus. Les fichiers source disponibles dans `GivreShareWork` déclarent `LicenseService.AppVersion = "1.2.0"`, tandis que le titre de la fenêtre observée passivement par PRIME indiquait `v1.3.1` : ces libellés ne relient aucun binaire par hash.

## Fichiers et fonctionnement

- `tools/MZP0_OfflineDecompile.py` : entrée de commande, garde-fous et orchestration de la reconstruction C#.
- `tools/MZP0_OfflineDecompileTests.py` : tests unitaires sur **PE fictifs** et faux `ilspycmd` injecté ; aucun code binaire d'un tiers ne sert de fixture.
- `tools/MZP0_OfflinePeAudit.py` : prévol existant, **lecture seule**, qui valide en-têtes MZ/PE, sections, répertoire CLR, signature `BSJB`, drapeau `ILONLY`, architecture, taille et SHA-256.

Le pipeline attend un fichier détaché **nommé `GivreClone.exe` dans l'arbre `/givre` autorisé**, sélectionné explicitement. Il refuse les EXE externes, le répertoire d'installation du GivreClone lancé, les liens symboliques/jonctions et les chemins d'évasion. Il refuse également une destination préexistante, même vide. Le parent de la destination doit exister ; son dernier segment doit commencer par `MZP0_OfflineDecompile_` et le chemin doit être **relatif à `/givre`**.

| Prévol statique | Action | Résultat annoncé |
| --- | --- | --- |
| PE avec CLR, métadonnées `BSJB` et `ILONLY` vérifiés | Requiert `ilspycmd` installé, exécute la CLI de décompilation sur le fichier **en données**, avec `-p -o` | `managed_source_recovered` seulement si `.cs` **et** `.csproj` créés et tous les fichiers inventoriés |
| PE natif, apphost, NativeAOT possible, CLR sans ILONLY ou métadonnées non vérifiées | Ne lance aucun outil de décompilation | `unsupported_native_or_aot`, diagnostic de méthode sans génération de faux C# |
| PE invalide, chemins dangereux, sortie existante | Rejette l'opération | `rejected`, sans écrasement |
| CLR IL mais `ilspycmd` absent | Bloque sans installer d'outil | `blocked_ilspycmd_missing` |
| ILSpy échoue, artefact change, sortie incomplète | Échec avec purge de l'espace temporaire créé par cette invocation | `rejected`, aucun résultat final publié |

`native_or_aot_requires_static_recovery` est un **classement conservateur** : l'absence de métadonnées IL exploitables ne permet pas de conclure à NativeAOT, à VMProtect ni à un langage source précis. Un exécutable `.NET` peut aussi être un **apphost natif** dont le vrai IL se trouve dans une DLL séparée ; ce pipeline porte volontairement sur **l'EXE explicitement fourni**, sans rechercher automatiquement d'autres fichiers privés.

## Utilisation sur Windows

Depuis PowerShell, avec `%USERPROFILE%\Videos\GIVRE\GivreShareWork` comme dossier courant, une fois qu'une **copie autorisée distincte du processus actif** est disponible, par exemple sous `%USERPROFILE%\Videos\GIVRE\GivreShareWork\offline-input\GivreClone.exe` :

```powershell
py -3 -B tools/MZP0_OfflinePeAudit.py --input "%USERPROFILE%\Videos\GIVRE\GivreShareWork\offline-input\GivreClone.exe"

py -3 -B tools/MZP0_OfflineDecompile.py `
  --input "%USERPROFILE%\Videos\GIVRE\GivreShareWork\offline-input\GivreClone.exe" `
  --output "GivreShareWork/MZP0_OfflineDecompile_reference_01" `
  --timeout-seconds 600
```

L'outil utilise le `ilspycmd` déjà présent dans `PATH` et **n'installe rien**. Le contrôle du 16/10/2026 a identifié `ilspycmd: 11.1.0.9782` sur le poste Windows. Pour vérifier un autre environnement :

```powershell
ilspycmd --version
ilspycmd --help
```

S'il manque, l'installation est une décision opérateur explicite, par exemple via la documentation officielle de l'outil [`ilspycmd` sur NuGet](https://www.nuget.org/packages/ilspycmd) puis, **facultativement**, `dotnet tool install --global ilspycmd`. Ne pas effectuer cette installation automatiquement dans le pipeline. La version de l'outil utilisée est enregistrée dans le manifeste.

**Structure en cas de réussite :**

```text
GivreShareWork/
  MZP0_OfflineDecompile_reference_01/
    manifest.json
    source/
      GivreClone.csproj
      ... sources C# décompilées par ILSpy ...
```

Le nom réel du `.csproj` et la disposition des classes dépendent d'ILSpy. Le dossier de sortie est créé en staging privé sous le parent autorisé, validé, puis publié en une seule opération de renommage vers un répertoire neuf. Les options d'ILSpy sont `-p -o` et l'EXE **est passé comme argument d'entrée au décompilateur** ; l'EXE lui-même n'est jamais démarré.

## Manifeste et preuves

Le `manifest.json` de chaque reconstruction réussie comporte :

- **Entrée** : chemin relatif sous `/givre`, SHA-256, taille, architecture, format PE et indicateurs `CLR`, `BSJB`, `ILONLY`.
- **Runtime** : version du schéma en-tête CLI, cible `.NET` déclarée **`unknown`** si elle ne peut pas être démontrée, système hôte, version Python, version exacte d'`ilspycmd` et commande normalisée.
- **Sorties** : chemin relatif du projet récupéré, **ensemble complet des fichiers** (`source/`), chacun avec chemin, taille et SHA-256, nombre total de fichiers.
- **Limites** : `exe_executed=false`, `process_or_installed_exe_touched=false` et `feature_equivalence_certified=false`.

Le manifeste n'est pas inclus dans son propre ensemble de SHA, pour éviter une empreinte autoréférente. Les fichiers sont inventoriés en ordre stable. Contrôles : au plus 20 000 fichiers, 128 Mio par fichier, 1 Gio en tout. Toute modification de l'EXE source **pendant** la reconstruction annule le résultat ; tout fichier ou dossier généré sous forme de lien/jonction est rejeté. Les journaux détaillés et éventuels contenus privés de l'outil de décompilation ne sont pas copiés dans le manifeste.

Le code décompilé est **un point de départ pour l'analyse statique et la compilation**, pas une reproduction certifiée de la version observée. Pour juger la fidélité, réviser signatures/types, dépendances, XAML, accès aux ressources, appels natifs et tests fonctionnels, puis comparer à l'EXE **correspondant réellement au hash**. Une compilation de sortie peut échouer si ILSpy n'a pas récupéré certaines ressources, dépendances ou comportements NativeAOT ; aucune preuve gameplay/live n'est issue de ce pipeline.

## Vérification du pipeline sans EXE tiers

```powershell
py -3 -B tools/MZP0_OfflineDecompileTests.py
py -3 -B tools/MZP0_OfflinePeAuditTests.py
```

Ces tests construisent des en-têtes PE minimaux synthétiques, simulent l'outil ILSpy par une fonction de test et examinent les arguments invoqués, les manifestes SHA, le repli natif, les destinations existantes, traversées `..`, liens symboliques, modifications de l'entrée, erreurs d'outil, absence de `.csproj` et nettoyage de staging. **Ce succès valide les contrats du pipeline**, mais ne remplace pas une décompilation du véritable EXE détaché ni la compilation de ses sources.

**Prochain apport attendu pour la parité EXE :** une copie offline autorisée de `GivreClone.exe` dans `/givre` avec provenance du fichier initial, afin d'exécuter le prévol, décider s'il contient du CLR IL, récupérer réellement le projet lorsqu'il est managé, puis examiner les écarts de fonctionnalités. Tant que ce fichier n'est pas présent, `exe_confirmed` reste faux dans la matrice MZP0.
