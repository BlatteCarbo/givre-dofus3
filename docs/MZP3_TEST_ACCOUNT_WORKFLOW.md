# MZP3 — Profils de TEST persistants, exclusivement hors ligne

**Livré : service Win/.NET 10 de vrais fichiers de test isolés + ports MZP2 injectables séparément ; aucun vrai compte, aucun mot de passe, token, client Dofus, hook ou session GivreClone n'est ouvert.** L'application Avalonia peut compiler et utiliser le module offline indépendamment du moteur réseau.

## Périmètre / séparation de dépendances

| Fichier neuf | But | Dépend de MZP2/GivreCore ? |
| --- | --- | --- |
| `GivreClone.App/Services/MZP3TestAccountService.cs` | Profils, sélection, CRUD, JSON versionné, DACL privée, journal de sauvegarde et diagnostics UI | **Non**, source autonome compilable |
| `GivreClone.App/Services/MZP3TestAccountService.Admission.cs` | Préparation du **type réel** `MZP2TestClientAdmissionRequest`, avec permit et deux autorités reçus de l'extérieur | **Oui**, adapter partiel séparé |
| `tools/MZP3TestAccountsHarness/MZP3TestAccountsHarness.csproj` | Tests de la vraie source offline et de vraies E/S dans un dossier de TEST à GUID aléatoire | **Non** |
| `tools/MZP3TestAccountsHarness/MZP3AdmissionLinkHarness.csproj` | Tests de compilation / liaison réelle de l'adaptateur avec les sources MZP2/MZP1 et refus live | **Oui** ; `BotHost`/`GivreCore` substitués uniquement dans ce harness |

Ne pas modifier `AccountManager`, `BotHostService`, `MZP2OwnedHostAdmission`, le client, les `.csproj` existants ni le stockage réel de GivreClone. L'adaptateur MZP3 ne démarre pas d'hôte et n'invente aucune identité ou preuve d'ACK.

## Parcours UI de test entièrement opérationnel

```csharp
using var ui = MZP3TestAccountService.Open("test-run-2026-001", explicitOperatorApproval: true);
var a = ui.CreateProfile("Client Test Local");
ui.SelectProfile(a.Id);
ui.SetOfflineCandidate(a.Id, "Serveur Candidat", "Personnage Candidat", explicitOfflineSelection: true);
ui.Save();

var accounts = ui.Profiles;       // copies immuables, pas de tokens
var selected = ui.SelectedProfileId;
var diagnostics = ui.Diagnostics; // état, décompte, dirty, candidat, pas de chemins / alias
ui.Reload();                      // refuse de perdre des modifications non sauvegardées
ui.Stop();                        // libère son verrou; refuse si dirty
```

Autres opérations de l'interface : `RenameProfile(id,alias)`, `ClearOfflineCandidate(id)`, `RemoveProfile(id)`, puis `Save()`. Un candidat serveur/personnage **ne certifie jamais** qu'il existe, que l'utilisateur en a la propriété ou qu'il est connecté ; aucune résolution réseau n'est appelée. `MayPrepareMZP2` signale seulement la sélection hors ligne sauvegardée, **pas** la permission de démarrer le jeu. `Stop()` exige `Save()` préalable ; `Dispose()` libère le verrou sans sauvegarde implicite, même si une fermeture forcée intervient.

## Stockage et garanties vérifiées

Chaque test-run utilise obligatoirement un identifiant court `[A-Za-z0-9][A-Za-z0-9_-]{3,39}`. Il est résolu uniquement sous :

`%LOCALAPPDATA%/GivreReplica/TestRuns/<test-run-id>/`

Le dossier privé contient `mzp3-test-profiles.json` (uniquement alias, identifiant local GUID, candidats serveur/personnage, sélection offline et révision), `mzp3-test-profiles.json.lck` (verrou à ouverture exclusive) et, seulement pendant l'écriture, `mzp3-test-profiles.json.tmp`. MZP3 **ne lit ni ne crée** `accounts.json`, chemin réservé à l'hôte de test MZP2. `recovery/` est un futur répertoire de journal MZP2, et non une base de comptes MZP3.

Le dossier privé est créé avec un DACL Windows protégé, propriétaire = SID utilisateur courant, permissions héritables accordées uniquement à cet utilisateur ; les dossiers déjà existants doivent conserver cette politique. Les fichiers sont validés avant chaque lecture/écriture ; les liens symboliques/reparse points sur le chemin de test, les fichiers ou les répertoires sont refusés. Le module exige Windows pour ne pas prétendre à la même politique sur Linux/macOS ; aucun ajustement de droits n'est fait à un dossier de production.

Le JSON suit un contrat fermé `MZP3StoreEnvelope { Body:{ FormatVersion=1, Revision, SelectedProfileId, Profiles }, Sha256 }` : limite 128 KiB, maximum 64 profils, GUID/alias distincts, uniquement caractères autorisés dans les libellés et aucun champ de credential. Les champs inconnus (`password`, `token`, etc.), JSON tronqué, digest SHA-256 incohérent, version future/legacy non prise en charge et données invalides sont **refusés sans migration silencieuse**. SHA-256 est un détecteur de corruption accidentelle, **pas** une preuve cryptographique d'origine en cas de modification volontaire de l'ensemble du JSON. Il n'y a ni chiffrement, ni mot de passe, précisément parce que cette base ne doit contenir aucun secret.

La sauvegarde écrit un nouveau `.tmp` dans le même dossier (CreateNew, `Flush(true)`), valide les permissions, puis remplace atomiquement le JSON cible par renommage, sans écrire directement dans le fichier existant. Le verrou FileShare.None empêche un second propriétaire local ; les modifications hors bande pendant une session sont détectées par un hash du fichier lu initialement. Un `.tmp` orphelin, une base manquante après création précédente ou une corruption conduit au refus `InterruptedWrite` / `MissingOrCorruptStore` plutôt qu'à la génération automatique d'une base vierge. Les erreurs de stockage et sécurité se traduisent en catégories (`MZP3Failure`), sans exposer noms et chemins dans les diagnostics.

**Limites :** l'ACL seule ne protège pas contre un administrateur local ou un processus arbitraire tournant sous le même SID. Comme pour toute vérification puis ouverture de chemin Windows, une course native de substitution de répertoire reste à examiner lors d'une future revue de sécurité Windows approfondie. Ce module ne prouve pas de données de gameplay, n'implémente pas l'authentification Ankama et ne s'interface avec aucun GivreClone en cours d'exécution.

## Préparation typée de la demande MZP2, jamais admission automatique

Le fichier `.Admission.cs` est inclus dans GivreClone.App seulement. Il ajoute la méthode :

```csharp
MZP2TestClientAdmissionRequest PrepareMZP2Request(
    MZP1OwnedClientPermit externallyVerifiedPermit,
    MZP2TrustedEnginePorts independentlyVerifiedPorts,
    int testRelayPort,
    bool explicitOperatorApproval);
```

Elle demande un profil sélectionné, un candidat serveur/personnage **explicitement choisi**, une sauvegarde à jour et un port opérateur compris entre 1024 et 65535 mais différent de 5657/5658. Le chemin `accounts.json` du test-run et le chemin `recovery/` sont renvoyés dans les **véritables types de MZP2**. Le permit, le catalogue, la source d'identité, le sink et le vérificateur de réponse positive doivent être **fournis indépendamment** ; MZP3 ne les crée jamais. La demande résultante doit encore réussir la validation MZP2 (dont le binding de transport de l'hôte). Un alias ne vaut jamais autorisation de jeu.

## Validation de bout en bout sans session live

```powershell
dotnet test tools/MZP3TestAccountsHarness/MZP3TestAccountsHarness.csproj -c Debug
dotnet test tools/MZP3TestAccountsHarness/MZP3TestAccountsHarness.csproj -c Release

dotnet test tools/MZP3TestAccountsHarness/MZP3AdmissionLinkHarness.csproj -c Debug
dotnet test tools/MZP3TestAccountsHarness/MZP3AdmissionLinkHarness.csproj -c Release
```

Les tests de disque créent et suppriment exclusivement leurs **propres** sous-dossiers `mzp3-<GUID>` sous `TestRuns`. Ils exercent les parcours UI, la fermeture/reprise, sélection/candidats, contrôles DACL, corruption/version, exclusivité de lease, symlink, sauvegarde atomique et no-op idempotent. Ils n'ouvrent jamais `%APPDATA%/GivreClone/accounts.json` et n'interrogent aucune session/procédure active. L'harness de liaison utilise exclusivement des doublures de types manquants sous `tools/` et prouve que les classes MZP2 acceptent la demande tout en restant bloquées sans attestation réelle.
