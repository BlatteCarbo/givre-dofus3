# MZP7 — PRIME / Reconstruction fournisseurs et intégration C# réelle

Date : 16 octobre 2026. Produit : GivreClone v1.3.1 **à reconstruire**, GivreShareWork et GivreRework 3.7.10.10.

## RESULT — ce qui est vrai

Cette passe a levé une partie réelle de l'obstacle de build. **Sans retirer
aucun `ProjectReference` ni masquer aucun dossier source de GivreClone** :

- Le véritable `GivreClone.Protocol/GivreClone.Protocol.csproj` compile en
  .NET 10 Release + Debug, contre la nouvelle bibliothèque réelle
  `../GivreProxy/GivreCore/GivreCore.csproj`.
- Le véritable `GivreClone.Engine/GivreClone.Engine.csproj` compile en Release
  + Debug avec de vraies implémentations reconstruites `GivreCore.Mitm` et
  `GivreCore.Injection`. Aucun de leurs chemins n'a été lancé sur un client.
- Le véritable `GivreClone.App/GivreClone.App.csproj` **ne compile toujours pas**.
  Après `dotnet restore`, Release affiche **194 diagnostics d'erreur C#**
  qui concernent les API non reconstruites de `GivreLauncher.Core` : 52
  `Ankama`, 42 `Launch`, 42 `Accounts`, 40 `Zaap`, 10
  `IShieldCodePrompt`, 4 `HandshakeRelay`, 2 `ZaapEmulator` et 2
  `GameLauncher`. Ce sont les **lignes diagnostics**, certaines sont
  dupliquées, et non 194 fonctionnalités distinctes.
- Le sous-ensemble `../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj`
  compile lui aussi avec un vrai transport SOCKS5 + HTTP CONNECT, mais
  **n'exécute pas l'authentification Ankama/Zaap/Launcher d'origine**.

L'exécutable indépendant MZP6 reste publiable avec Avalonia Pro ; il n'a
pas été rebaptisé « GivreClone complet ». Les nouvelles DLL ne prouvent
ni compatibilité de jeu, ni login, ni PID authentifié, ni ACK serveur.

## CHANGES — code réel et provenance

**Nouvelle reconstruction de code source par PRIME** (et NON décompilation
du vrai exécutable v1.3.1) :

- `../GivreProxy/GivreCore/GivreCore.csproj` et
  `Protobuf/{ProtoField,RawProtobuf}.cs` : vrais encodeurs et décodeurs wire,
  types originaux consommés par `EnvelopeBuilder`, `FightStatsReader`,
  `GameCodec` et `CaptureDecoder`. Champs inconnus conservés, budgets,
  framing générique. Documentation `../GivreProxy/GivreCore/PROVENANCE.md`.
- `../GivreProxy/GivreCore/Mitm/TcpProxyServer.cs` : vrai relayeur TCP
  duplex, mais **inactif sans route loopback TEST dédiée et autorité externe
  indépendante**. Interfaces propriétaires de session et vérification
  au fil de l'eau ; aucune route par défaut ni binding automatique 5658.
- `../GivreProxy/GivreCore/Injection/DllInjector.cs` : véritable surface
  native Win32, garde externe indépendant, naissance et SHA du processus
  TEST, vérification DLL, permit unique, absence de permit par défaut.
  **Seul le refus sans autorisation a été testé** ; ne jamais conclure à
  une injection prouvée, encore moins dans le GivreClone installé.
- `../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj` et
  `Proxy/ProxySettings.cs`, `Transport/{Socks5Forwarder,HttpConnectForwarder,TransportLog}.cs` :
  implémentations managées réelles alignées sur les signatures des consommateurs
  `ProxyResolver`, `SocketDialer`, `ProxyTester`, `TransportDiag`, RFC1928/1929,
  HTTP CONNECT, pas de fallback IP directe. Provenance
  `../GivreLauncher/GivreLauncher.Core/PROVENANCE.md`.
- `GivreClone.Engine/GameState.cs` : suppression du faux switch vide qui
  produisait CS1522 ; aucune promotion d'état carte/personnage/combat sans
  preuve de schéma et d'ingress indépendant.

Contributions effectives des agents récupérables :

- `GivreClone.Protocol/MZP7TypedProtobufContract.cs` (worker-1) : vrai codec
  de conversion MZP3/MZP4/MZP5 à un manifeste de types explicitement
  fourni, contraintes signed/packed/UTF-8/unknown, SHA de source et
  version attendue. Tests `tools/MZP7CoreContractHarness/`, **61 PASS**,
  détails dans `docs/MZP7_CORE_RECONSTRUCTION.md`.
- `tools/MZP7ProviderEvidence/` et
  `docs/MZP7_PROVIDER_EVIDENCE.md` (worker-8) : scanner et cas adversariaux
  **13 PASS**. Son premier snapshot est **historique** ; de nouveaux fichiers
  `GivreCore` et `GivreLauncher` ont été ajoutés PAR PRIME durant l'audit.
  `MZP7EvidenceAfterReconstruction.json` recalculé depuis les nouvelles sources.
- `GivreRework/src/GivreRework.Infrastructure/Protocol/Differential/MZP7ReplayLeaseAdmission.cs`
  (worker-5) : correction des trois vrais défauts du contrat MZP6 (double
  replay, reçus expirés et trou de séquence). Attestation indépendante et
  claim atomique d'usage unique *requis* ; aucun test live, aucune autorité
  production créée. `tests/MZP7P0Harness`, **52 PASS**.

## VALIDATION — niveaux de preuve

| Module/build/test | Niveau et résultat |
| --- | --- |
| `GivreCore.csproj` Release | **Compile** (0 erreur / 0 avertissement) |
| `GivreClone.Protocol.csproj` Debug+Release | **Compile** |
| `GivreClone.Engine.csproj` Debug+Release | **Compile** ; états non attestés restent inconnus |
| `GivreLauncher.Core.csproj` Release | **Compile** sous-ensemble proxy |
| `GivreCoreProtobufContractHarness` | **31 PASS** offline avec vrai `EnvelopeBuilder` |
| `GivreLauncherTransportHarness` | **18 PASS** offline SOCKS5/HTTP CONNECT sur streams mémoire |
| `MZP7CoreContractHarness` | **61 PASS** offline sur vrais MZP3/MZP4/MZP5 |
| `MZP7ProviderEvidence` | **13 PASS** Python sécurité/scanner |
| `Givre MZP7P0Harness` | **52 PASS** offline, 3 refus adversariaux nouveaux |
| `Givre MZP6P0Harness` | **57 PASS** offline régression |
| `GivreRework.DesktopHost` Release | **Compile** (0 erreur / 0 avertissement) |
| `MZP6WorkbenchUiHarness` | **21 PASS** Headless après restore standard explicite |
| `GivreClone.App` Release après restore | **ÉCHEC** ; 194 lignes d'erreurs C# Launcher |

Les autres tests MZP1–MZP6 antérieurs restent conservés. Certains
`dotnet run` exécutés contre les projets MSBuild de harness de tests ne
présentent aucun compteur d'assertions : ils ne sont pas comptabilisés
comme nouveaux succès sans sortie de validation.

La compilation des modules et les contrôles structuraux ne certifient
pas le handshake Dofus. Aucun TCP de jeu, injection, login, processus,
compte, secret, dump ou prise de contrôle du GivreClone original n'a été
utilisé dans cette passe.

## BLOCKERS / prochaine action d'ingénierie substantielle

1. **Fournisseur historique `GivreLauncher.Core` non reconstruit complètement** :
   récupérer des sources autorisées attestables ou implémenter légitimement
   les réelles API `AnkamaAuth`, `ShieldCertificate`, `ZaapKeydataReader`,
   `GameLauncher`, `ZaapEmulator` et `HandshakeRelay`, sans contourner
   authentification/licence. Le fichier `.csproj` nouveau ne remplace
   actuellement que le sous-ensemble route proxy/log.
2. Confronter ensuite le protocole **3.7.10.10** à des octets privés et une
   provenance vérifiée, identifier les binaires clients d'une instance de
   TEST distincte sous permit indépendant, puis seulement tester route MITM,
   ACK serveur et état du personnage. Ne pas forger de preuve de session.
3. **Parité v1.3.1 non certifiée** : aucune copie détachée SHA-vérifiée du
   véritable EXE n'a permis de décompiler cette version pendant ce lot.
   Le nouveau `GivreCore` est **reconstructed source by PRIME**, pas un
   fournisseur extrait ni équivalent binaire démontré.
4. **Givre G0 et G1–G8 NON VALIDÉS** : gate MZP7 s'applique uniquement à
   des reçus R115 fraîchement émis (<30s) ; un corpus archivé exige une
   autre chaîne de garde. Deux ports de décodeurs indépendants exacts
   3.7.10.10 et preuve réelle manquent.
5. UI/Avalonia : l'ancien WinExe MZP6 publié avec Avalonia Pro est
   réellement utilisable pour **TEST offline**, pas pour l'ensemble
   Comptes → Combat → Banque → HDV. L'App GivreClone originale reste bloquée.

Prochaine priorité P0 : **terminer `GivreLauncher.Core` à partir des vrais
contrats et preuves**, compiler enfin `GivreClone.App`, puis rattacher la
chaîne authenticité/protocole au vrai host et seulement ensuite ouvrir
les tests d'intégration sur un client TEST autorisé.
