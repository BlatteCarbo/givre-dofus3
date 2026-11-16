# MZP4 Launcher — prévol d'isolation pour comptes de TEST (App)

**Livré le 16/10/2026 : `SOURCE_COMPILED / OFFLINE_TESTED 35/35`, statut `CONFIGURATION_CANDIDATE_ONLY`.** Aucun client Dofus/GivreClone existant ni processus n'a été consulté, aucun accès réseau, aucun fichier de compte ou secret n'a été ouvert. Ce lot n'accorde aucun droit de démarrer un hôte ni d'injecter une trame.

## Implémentation

Nouveaux fichiers **exclusivement** :

| Chemin | Fonction |
| --- | --- |
| `GivreClone.App/Services/MZP4LauncherPreflight.cs` | Contrat produit App pour valider une sélection de test, un port dédié, le dossier local autorisé et ses permissions **sans écrire sur le disque** |
| `tools/MZP4LauncherHarness/MZP4LauncherHarness.csproj` | Compile directement la **vraie source** du service App sous .NET 10, warnings as errors, sans remplacement ni faux `GivreLauncher.Core` |
| `tools/MZP4LauncherHarness/Program.cs` | Vérifie en dossier temporaire de test réel Windows le prévol positif, les ACL et les cas négatifs |
| `docs/MZP4_LAUNCHER_BOUNDARY.md` | Contrat d'intégration, validations et limites |

### Sources authentiques examinées en lecture seule

- `LaunchService.cs` : `LaunchAsync` utilise `GameLauncher`, authentification, hook et relais MITM (port **5658**), plus un `ZaapEmulator(26666)` ; ce service agit réellement sur des processus et des réseaux. Le prévol ne l'appelle jamais.
- `Transport/IGameTransport.cs` et `Transport/MitmTransport.cs` : `Inject(pid, frame)` et hooks d'observation `OnData`/`OnClosed` appartiennent au transport **live**, absent de MZP4.
- `Relay/GivreRelay.cs`, `Relay/GivreRelayClient.cs`, `GivreRelayAgent/Program.cs`, `GivreRelayAgent/HostPolicy.cs` : pont de partage, WebSocket, gestion d'agent, politiques d'hôtes, codes et flux réels ; aucun de ces services n'est instancié.
- `MZP2OwnedHostAdmission.cs`, `BotHostService.cs` : `MZP2IsolatedTestHostConfiguration(TestRelayPort, TestAccountStorePath, ExplicitOperatorApproval)`, ports **1024..65535** hors **5657/5658**, stockage sous `%LOCALAPPDATA%/GivreReplica/TestRuns`, validation additionnelle du vrai propriétaire de session.
- `MZP3TestAccountService.cs` et `.Admission.cs` : run ID ASCII **4..40** (`lettre/chiffre`, `-`, `_`), chemin `TestRuns/<runId>/accounts.json`, journal sous `TestRuns/<runId>/recovery`, dossier de run à ACL protégée limitée à l'utilisateur Windows, candidat de sélection explicite.

La lecture de ces sources vérifie des **contrats de forme**. Le statut runtime réel MZP2 reste bloqué : `MZP2DedicatedBotHostPort.CanBindProtectedEngine` retourne actuellement `false` (pas de preuve host/sink/identité/réponse indépendante).

## API concrète, destinée au futur raccord App

```csharp
using GivreClone.App.Services;

// Sélection déjà enregistrée dans le magasin MZP3 de TEST :
var selected = new MZP4LauncherSelection(
    TestRunId: testRunId,
    TestRelayPort: operatorPort,
    TestAccountStorePath: selectedTestAccountStorePath,
    JournalDirectory: selectedRecoveryDirectory,
    ClientVersionCandidate: "3.7.10.10",
    ExplicitOperatorApproval: explicitApproval);

var check = MZP4LauncherPreflight.Evaluate(selected);
if (!check.IsConfigurationCandidate)
{
    // Afficher check.Refusal (enum), sans jamais exposer chemins, login ou secret.
    return;
}

var candidate = check.Candidate!;
// candidate.TestRelayPort et candidate.TestAccountStorePath sont conformes au
// contrat STRUCTUREL MZP2IsolatedTestHostConfiguration. Une vérification MZP2
// séparée (permit indépendant, host-owned sink, identité, ACK et reprise du
// journal) reste obligatoirement requise à l'instant du démarrage.
```

### Conditions de succès, volontairement strictes

1. Approbation opérateur explicite, run ID conforme à MZP3, version candidate exacte `3.7.10.10`.
2. Port de test explicite entre **1024 et 65535**, hors 5657, 5658 et **26666** (ce dernier protège aussi le vrai Zaap de `LaunchService`). **La disponibilité réseau du port n'est pas interrogée** : seul le choix du numéro est vérifié.
3. Chemin de compte exactement égal à `%LOCALAPPDATA%\GivreReplica\TestRuns\<runId>\accounts.json` et dossier de journal exactement `%LOCALAPPDATA%\GivreReplica\TestRuns\<runId>\recovery`. Rejet des chemins relatifs, UNC, autres dossiers, `.`/`..`, noms de fichiers non attendus, flux NTFS alternatifs et substitutions silencieuses.
4. Arborescence `LocalAppData / GivreReplica / TestRuns / <runId>` existante, sans reparse points (symlinks/jonctions). Le dossier `<runId>` doit avoir un propriétaire Windows égal à l'utilisateur courant et une DACL protégée, sans autorisation positive pour d'autres comptes.
5. Dossier `recovery` déjà présent, non reparse et à permissions privées correspondant à l'utilisateur courant. Si la préparation MZP3 n'a pas encore créé `recovery`, l'état est **`JournalDirectoryMissing`** : création requise par un workflow de propriétaire autorisé, jamais par ce prévol.

**Isolation des lectures :** le prévol vérifie seulement le chemin calculé et les métadonnées des **dossiers dédiés** ; il ne lit, ne crée, ne charge, ne parcourt ni `accounts.json`, ni les journaux, ni les configs de l'application d'origine, ni l'installation Dofus. Il n'énumère aucun répertoire arbitraire fourni par un utilisateur : les entrées doivent d'abord correspondre aux chemins exacts dérivés de `runId`.

`MZP4LauncherPreflightResult.Refusal` donne un motif stable pour les diagnostics UI et `Candidate` (nullable) ne contient que port, chemins dédiés, runId et version candidate. `MayStartHost`, `MayLaunchClient`, `MayInject`, `Authenticated`, `PortAvailabilityVerified`, `GrantsRuntimeAdmission`, `OwnedClientVerified` et `ServerAckVerified` restent toujours **false**, même si `IsConfigurationCandidate` vaut true. Tout résultat est invalidable par des changements ultérieurs du système de fichiers (TOCTOU) : MZP2/BotHostService devront revérifier le chemin et le port avant toute utilisation.

## Validation reproductible

```powershell
Set-Location %USERPROFILE%\Videos\GIVRE\GivreShareWork
dotnet restore tools/MZP4LauncherHarness/MZP4LauncherHarness.csproj --ignore-failed-sources
dotnet run --project tools/MZP4LauncherHarness/MZP4LauncherHarness.csproj -c Release --no-restore -m:1
```

**Résultat observé : 35/35 PASS** sur Windows/.NET 10. Le harness compile la source `MZP4LauncherPreflight.cs` non modifiée et crée **uniquement** sous `%TEMP%` un dossier `MZP4LauncherHarness-<guid>` avec un run ACL privée. Il teste le succès candidat et les rejets (absence approbation/version/run, ports réservés et limites, store absent/hors racine/renommé, traversal, alternate stream, journal arbitraire, chemin système/UNC, run manquant, run à ACL héritée/non protégée, recovery absent, racines alternatives). Le harness vérifie son chemin temporaire avant nettoyage et n'accède à aucun client, compte réel, service ni réseau.

### Limites et action PRIME

La prévalidation ne décide pas si le **port est libre**, si le **processus Dofus appartient à l'opérateur**, si la **version client** déclarée correspond à un binaire, si une **source de capture est authentifiée**, ou si un **ACK serveur** a eu lieu. Même la construction publique d'un `MZP4LauncherCandidate` ne constitue aucune permission. Pour aller plus loin, PRIME devra le raccorder à la sélection MZP3 et à la vérification indépendante MZP2, puis satisfaire les vraies autorités de session/transport et la validité des chemins **au point d'utilisation**. Ce lot ne modifie pas `LaunchService`, `GivreRelay`, `MZP2/MZP3`, `AccountsView`, `MZP4TestAccountsPanel`, `Givre`, `MZP4ProtocolAdapter` ni aucun `.csproj` App.
