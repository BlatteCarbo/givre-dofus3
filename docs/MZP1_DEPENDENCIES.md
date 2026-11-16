# MZP1 — restauration des dépendances par sources authentiques (offline)

Date de l'audit : 2026-10-16. Périmètre **source-only** : `/givre`, sans accès aux processus, installations, comptes, sessions, secrets ou clients Dofus/GivreClone. Ne prouve **ni** la parité EXE v1.3.1 **ni** une connexion runtime.

## Blocage constaté dans les sources réelles

| Projet compilé | Dépendance source relative inscrite dans le `.csproj` | État sous `/givre` |
|---|---|---|
| `GivreClone.App` | `../../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj` | **Absent** |
| `GivreClone.Engine` | `../../GivreProxy/GivreCore/GivreCore.csproj` | **Absent** |
| `GivreClone.Protocol` | `../../GivreProxy/GivreCore/GivreCore.csproj` | **Absent** |

`GivreClone.slnx` déclare également `../GivreProxy/GivreCore/GivreCore.csproj`, sans livrer le projet. Les répertoires `GivreLauncher`, `GivreProxy` et `GivreSuite` sont absents à la racine locale du workspace. L'absence d'un `.csproj` de référence ne peut pas être corrigée à partir d'un binaire, d'une DLL empruntée ou d'un faux projet vide.

### Inventaire

Commande vérifiée :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/MZP1DependencyAudit.ps1 -AsJson
```

Résultat de la dernière passe : **8 808 dossiers** parcourus, **170 archives ZIP inspectées**, **0 illisible, 0 écartée pour taille, scan non tronqué** ; zéro fichier `GivreCore.csproj` / `GivreLauncher.Core.csproj` identifié, zéro ZIP contenant ces chemins de projet avec des sources C# reconnues. Les caches, `obj`, `bin`, `.git`, `.venv`, `site-packages`, `node_modules`, `dist`, `artifacts` et jonctions sont exclus : cela protège les installations actives et évite une recherche massive dans les sorties générées. Les chemins d'archives ne sont jamais extraits ; seuls les noms du répertoire central ZIP sont utilisés. Le scanner expose explicitement `scanTruncated`, `archivesInspected`, `archiveUnreadable` et `archiveSkippedOverSize`; toute archive non inspectable invalide une conclusion d'exhaustivité. Le scanner ne prétend donc pas prouver qu'aucune copie n'existe ailleurs ou dans un cache exclu.

### Empreintes des **déclarations source locales**, non des applications runtime

Empreintes SHA-256 établies par `Get-FileHash` au moment du contrôle :

| Fichier | SHA-256 |
|---|---|
| `GivreClone.App/GivreClone.App.csproj` | `9f3623f9b9fd861e778279c8f9c238515c57a9720ded70e1e6b7049946e04788` |
| `GivreClone.Engine/GivreClone.Engine.csproj` | `0df55fddaabffab4bcb9f669bbb01820e8daa932eeafdb8db48108caa3a89020` |
| `GivreClone.Protocol/GivreClone.Protocol.csproj` | `b7fc01f7a6daf3ec5b297843c93308f9c4580fb303bc141dabead1d9be848312` |

**Important :** `GivreClone.App.csproj` comporte déjà une modification *dirty* préexistante liée au runtime natif de combat ; elle n'est **pas** un changement MZP1. Les scripts ne la corrigent pas, ne la réinitialisent pas et ne la remplacent pas.

## Mécanisme de restauration opt-in (non actif tant que les sources manquent)

Quatre éléments réutilisables ont été créés :

- `tools/MZP1DependencyAudit.ps1` — inventaire non destructif des vrais `.csproj` et des projets éventuellement présents dans les ZIP.
- `tools/MZP1DependencyBuild.ps1` — contrôleur *fail-closed*, diagnostic par défaut ; vérifie les deux vrais projets, leurs noms, la présence de `.cs` et les SHA-256 requis avant tout appel au build.
- `tools/MZP1DependencyRecovery/ProjectLinks.targets` — redirection MSBuild **temporaire**, importée seulement par `-p:DirectoryBuildTargetsPath=...` et activée par `MZP1DependencyRecovery=true`. Remplace les trois anciennes références `ProjectReference` à l'évaluation, en conservant le reste des projets d'origine inchangés.
- `tools/MZP1DependencyTests.ps1` — vérifications hors ligne du refus de build, du périmètre, des scans incomplets et des 3 substitutions MSBuild **en évaluation seulement** ; aucun projet factice n'est créé.

### Exécution lorsque les sources autorisées auront été restaurées

1. Vérifier hors ligne que les deux arbres source correspondent réellement aux projets recherchés, qu'ils proviennent de copies autorisées et contiennent leurs dépendances réelles. Ne pas assimiler un nom de fichier ou un SHA calculé localement à la provenance d'un EXE.
2. Épingler séparément les SHA-256 des vrais `GivreCore.csproj` et `GivreLauncher.Core.csproj`. Un fichier source ne sera jamais recréé à partir d'un `.dll` ou d'une référence seule.
3. Exécuter une évaluation **sans compilation**, puis, si les restaurations NuGet locales nécessaires existent déjà, un build **sans réseau et sans lancer l'application** :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/MZP1DependencyBuild.ps1 `
  -Mode Evaluate `
  -CoreProject 'C:\PATH_APPROUVE\GivreCore.csproj' -CoreSha256 'SHA256_64_CARACTERES' `
  -LauncherProject 'C:\PATH_APPROUVE\GivreLauncher.Core.csproj' -LauncherSha256 'SHA256_64_CARACTERES'

# Seulement quand l'évaluation vérifie les vrais projets et leurs dépendances :
powershell -NoProfile -ExecutionPolicy Bypass -File tools/MZP1DependencyBuild.ps1 `
  -Mode Build `
  -CoreProject 'C:\PATH_APPROUVE\GivreCore.csproj' -CoreSha256 'SHA256_64_CARACTERES' `
  -LauncherProject 'C:\PATH_APPROUVE\GivreLauncher.Core.csproj' -LauncherSha256 'SHA256_64_CARACTERES'
```

Le `Build` invoque seulement `dotnet build GivreClone.App.csproj -c Debug --no-restore` avec import temporaire, sans `dotnet run`, sans client, sans auto-injection et sans NuGet réseau. La présence d'autres dépendances non résolues reste un blocage distinct. Ne jamais utiliser de stub/proxy fictif et annoncer le résultat comme un build produit.

## Niveaux de preuve

`SOURCE_MISSING` : état établi pour les deux projets au terme de cet inventaire. `MSBUILD_REDIRECTION_EVALUATED` : l'option de redirection peut être testée sans construire d'application via `dotnet msbuild -getItem:ProjectReference`, **même avec des chemins de diagnostic non existants**, et doit être distinguée d'une compilation réelle. `COMPILED_WITH_REAL_SOURCES` : **NON ATTEINT**. `GIVREBOT_EXE_1_3_1_PARITY` : **NON VÉRIFIÉ**. `SESSION/ACK` : **NON VÉRIFIÉS**.

Contrôle automatisé :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/MZP1DependencyTests.ps1
```

Résultat observé : **7/7 PASS** (refus sans sources, chemin hors workspace, scan tronqué, build sans pin refusé, substitution effective pour Protocol/Engine/App). Le test d'évaluation utilise uniquement des **chaînes de chemins inexistants** `DIAGNOSTIC_ONLY` et ne crée ni `.csproj`, ni stub, ni DLL ; le véritable build est bloqué.

## Blocage global de compilation connu

La tentative antérieure :

```powershell
dotnet build GivreClone.App/GivreClone.App.csproj -c Debug --no-restore `
  -p:BuildProjectReferences=false -p:CopyBuildOutput=false -v:q
```

avait renvoyé `MSB9008` (`GivreLauncher.Core.csproj` introuvable) et `CS0006` (fichiers de référence `GivreClone.Engine.dll`, `GivreClone.Protocol.dll` introuvables). `BuildProjectReferences=false` explique pourquoi les deux `.dll` ne sont pas régénérées. Une nouvelle tentative plus représentative **avec références projet activées** (`dotnet build GivreClone.App/GivreClone.App.csproj -c Debug --no-restore -p:CopyBuildOutput=false -v:q`) échoue avec **171 erreurs**, en commençant par `MSB9008` sur `GivreProxy/GivreCore/GivreCore.csproj`, puis `CS0246` sur les namespaces `GivreCore` et types `ProtoField` dans `GivreClone.Protocol`. La cause racine révélée par les `.csproj` reste **GivreCore + GivreLauncher.Core absents**. Il ne faut pas remplacer les références par des binaires arbitraires. L'essai `MZP1DependencyBuild.ps1 -Mode Diagnose` retourne volontairement `BLOCKED` avant build et **ne modifie aucun des projets existants**.

## Prochaine action sûre pour PRIME

Obtenir l'arbre source autorisé `GivreProxy/GivreCore` et `GivreLauncher/GivreLauncher.Core` ou des archives ZIP originales contenant les vrais projets et leurs `.cs`, puis refaire l'inventaire et l'évaluation MSBuild. Ne rien tenter sur le GivreClone lancé, ne pas lire les secrets/config installés, et ne pas prétendre à la parité v1.3.1 en raison d'une simple compilation.
