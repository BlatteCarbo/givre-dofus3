# N03 — Preuves d'exécution, source et limites

## Commandes réellement exécutées

```powershell
$proj='docs/cos-dofus-video/rapports-v2/N03/bench/PlannerOfflineBench.csproj'
$local=(Join-Path $env:USERPROFILE '.nuget\packages')
dotnet restore $proj --ignore-failed-sources --source $local -v:q
dotnet run --project $proj -c Release --no-restore -- (Resolve-Path 'docs/cos-dofus-video/rapports-v2/N03').Path
```

Résultat de fin **exit 0**, sortie : `OFFLINE_BENCH_DONE cases=5 rows=70 ...\rapports-v2\N03` et `RUN_EXIT=0`. Les données brutes [measurements.jsonl](measurements.jsonl) ont été effectivement écrites, ainsi que [environment.json](environment.json). Le script est contenu entièrement dans [bench/Program.cs](bench/Program.cs), [bench/PlannerOfflineBench.csproj](bench/PlannerOfflineBench.csproj) ; ses seules entrées sont les DLL déjà compilées présentes localement. La compilation du **banc d'essai local** et son dossier `obj/bin` se trouvent sous N03, sans écraser les projets actifs.

## Version et contrôle d'empreinte

| Entrée | Preuve |
| --- | --- |
| Runtime mesuré | **.NET 10.0.13**, SDK appelant **10.0.402** |
| `GivreClone.Tests/bin/Release/net10.0/GivreClone.dll` | taille 35 146 240 o ; modif. 2026-11-03 13:49:09 ; SHA256 `6F1B03216893C5705300AB62E717B1264435134E563F5EE10184D96F084850BA` |
| `GivreClone.App/Services/Combat/CombatTurnPlanner.cs` | taille 42 395 o ; modif. 2026-11-04 09:36:02 ; SHA256 `7618468265FC2D32B6C186ADFB0E3917947A1C03B6C7C5E903344399BB142A3F` |
| Contrat de planificateur consulté | `CombatTurnPlanner.cs:61-71` plan, travail, budget ; `:570-629` recherche bornée et actions |
| Nombre de mesures | cinq fixtures × (2 warmups + 12 steady) = **70** |

La date de modification de la DLL précède la source, donc aucune équivalence exacte de build n'est revendiquée. Pour le contenu des fixtures et la collecte d'allocations sur le thread courant (pas allocation totale du processus), lire le code et les données, pas simplement le tableau arrondi.

**Aucun accès réseau/game/process de compte**, aucune source, suite xUnit ou configuration d'application modifiée. La compilation autonome a écrit dans N03 uniquement.
