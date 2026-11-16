# MZP2 — dépendances GivreCore / GivreLauncher.Core : preuves source et limite de reconstruction

**2026-10-16 — `SOURCE_ONLY / NOT_COMPILED / NOT_RUNTIME`.** Mission strictement hors ligne, sans accès au GivreClone actif, aux processus Dofus, aux configurations/secrets installés ni aux DLL en cours d'utilisation. Aucune répétition de l'inventaire des 170 ZIP de MZP1. Toutes les créations sont sous `tools/MZP2Dependency*` et ce document. Les `.csproj`, les sources existantes et leurs changements *dirty* n'ont pas été modifiés.

## 1. Résultat tranché : pas de projet de remplacement GivreCore légitime

Le code source réel de **GivreRelay** est disponible : `GivreRelayAgent/GivreRelayAgent.csproj` indique `<AssemblyName>GivreRelay</AssemblyName>`, `<OutputType>Exe</OutputType>` et cible `.NET 10`. Il livre l'agent de relais, pas le codec et le proxy de `GivreCore` : les sources `GivreRelayAgent/AgentLink.cs`, `HostPolicy.cs` et `Program.cs` implémentent sa connexion WebSocket, ses flux et sa politique d'hôtes. Côté App, `Services/Relay/GivreRelay.cs`, `GivreRelayClient.cs`, `RelaySocksBridge.cs`, `RelayStream.cs` (et autres fichiers Relay) implémentent le consommateur du service, mais eux-mêmes importent `GivreLauncher.Core.Proxy` et `GivreLauncher.Core.Transport`. **Ce code ne prouve pas l'existence des types `GivreCore.Mitm` / `GivreCore.Protobuf` / `GivreLauncher.Core.Ankama` / `Launch` / `Proxy` / `Transport` / `Zaap`.**

Le nom d'assembly *GivreRelay.dll* ne doit pas être confondu avec une preuve d'API `GivreCore`. Le code dans `tools/MZP0RecoveryHarness/MZP0GivreCoreStubs.cs` est un **stub de test**, explicitement exclu de notre inventaire : il ne peut pas devenir une dépendance de production. Les fichiers sources des projets manquants sont **toujours absents** aux chemins exigés :

| Consommateur | ProjectReference exact (voir `projectLinks` du JSON) | Disponibilité |
|---|---|---|
| `GivreClone.Protocol` | `../../GivreProxy/GivreCore/GivreCore.csproj` | Manquant |
| `GivreClone.Engine` | `../../GivreProxy/GivreCore/GivreCore.csproj` | Manquant |
| `GivreClone.App` | `../../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj` | Manquant |

Ce sont les références **des sources**, pas une rétro-déduction à partir de binaires. Les empreintes des `.csproj` figurent dans `tools/MZP2DependencyInventory.json`, sous `projects`. Il n'y a **pas assez de sources source-pinnées** pour implémenter et tester un `GivreCore` minimal sans inventer des comportements de parsing, de capture et d'authentification. **Aucun `tools/MZP2SourceRecovery` n'est donc créé.**

## 2. Inventaire automatique et vérifiable

```powershell
# Régénérer la photo source-only de ce workspace (modifie seulement un NOUVEAU fichier MZP2):
py -3 tools/MZP2DependencyInventory.py --write tools/MZP2DependencyInventory.json

# Vérifier la photo sans écrire : échoue si une source citée change :
py -3 tools/MZP2DependencyInventory.py --verify tools/MZP2DependencyInventory.json

# Tests négatifs/positifs de l'analyseur, hors réseau et sans GivreClone :
py -3 tools/MZP2DependencyTests.py
```

L'analyseur couvre exclusivement les `.cs` de `GivreClone.App`, `GivreClone.Engine`, `GivreClone.Protocol` et `GivreRelayAgent` (sans `bin/`, `obj/`, symlinks et autres sorties). Il capture **imports**, qualifications explicites, alias, emplacements `(chemin, ligne)`, SHA-256 des fichiers consommateurs et **SHA-256 de chaque ligne d'exemple**. Les `ProjectReference` sont lus depuis le XML des quatre vrais `.csproj`. Il ne lit ni `.dll` ni dump ou ZIP.

Observation sur la photo du 16/10 : **510 entrées lexicales** (`using`/qualifications) dans **100 fichiers** ; 9 fichiers sources de relais référencés par empreinte ; 0 déclaration de fournisseur `namespace GivreCore`/`namespace GivreLauncher.Core` dans les quatre projets retenus ; aucune référence externe importée dans `GivreRelayAgent`. Le `verify` compare intégralement le JSON déterministe au nouveau calcul : si un autre worker modifie une source, **le verdict `STALE_SOURCE_OR_INVENTORY` est attendu** et il faudra revalider la nouvelle version ; ne pas déclarer la photo toujours fraîche.

**Limite importante :** le parseur est **lexical**, pas Roslyn. Un import ou un mot dans une expression peut être détecté sans que la liaison de type C# ou le comportement du fournisseur soient prouvés ; les exemples sont donc des **sites d'appel candidats justifiés**, pas un contrat ABI vérifié. Les nombres incluent des mentions de types, pas des appels tous distincts ni des méthodes nécessairement opérationnelles.

## 3. Les cinq surfaces les plus bloquantes, classées par impact produit

| Priorité / surface | Références lexicales ancrées | Fonction exigée et preuve source |
|---|---:|---|
| **P1 — `GivreCore.Protobuf`** : `ProtoField`, `WireType`, `RawProtobuf` | **422** mentions dans 41 fichiers | Encodage/décodage de trames. `GivreClone.Protocol/EnvelopeBuilder.cs:71–95` construit une liste de champs et invoque `RawProtobuf.Encode`; `GivreClone.App/Services/CaptureDecoder.cs:41–56` invoque `ConsumeFrames`, `Parse`, `FindTypeUrls`. Sans implémentation exacte, les payloads et le framing restent non fiables. |
| **P2 — `GivreCore.Mitm`** : `TcpProxyServer`, `CapturedMessage`, `Direction` | **38** mentions dans 12 fichiers | `GivreClone.Engine/BotHost.cs` construit le proxy ; `SessionBinder.cs` consomme `MessageCaptured`, `ConnectionId`, `ProcessId`. `FrameSink.cs:15–20` exige `Replay(connectionId, direction, bytes)`. Aucune implémentation démontrée des identités, événements et rejoueurs. |
| **P3 — `GivreLauncher.Core.Transport.HandshakeRelay`** | **3** mentions dans 2 fichiers | `GivreClone.App/Services/LaunchService.cs:497–524` demande un constructeur PID→proxy, `knownPid`, `GameProxyForPid`, `OnGameTunnelOpened` ; `Services/Transport/MitmTransport.cs:12–32` exige `Inject`, `OnData`, `OnClosed`, `Start`. Le `RelaySocksBridge` interne à l'App n'est **pas** ce handshake proxy. |
| **P4 — `GivreLauncher.Core.Launch.GameLauncher`** | **5** mentions dans 3 fichiers | `LaunchService.cs:52,533,538` nécessite `ResolveDofusPath()` et `Launch(...)`; `AccountsViewModel.Launch.cs:31,120` vérifie le chemin. Le lancement exige aussi la vraie gestion `ZaapEmulator` et l'auth `GivreLauncher.Core.Ankama` — ne pas simuler un PID ou un token. |
| **P5 — `GivreLauncher.Core.Proxy`** : `ProxySettings`, `ProxyKind` | **44** mentions dans 13 fichiers | `LaunchService.cs:21–32`, `Services/Relay/GivreRelay.cs:143–153` et `SocketDialer.cs:29–64` dépendent du typage et de la politique de routage SOCKS5/HTTP. Un simple record copié n'atteste ni la politique anti-fuite ni les forwarders `Transport`. |

À l'intérieur de chaque groupe, les attributs JSON `requiredNamespace`, `symbols`, `examples` (ligne + `lineSha256`), `files` et `count` permettent à PRIME de vérifier précisément les sites. Les surfaces non classées dans le top cinq restent dans `references` et `referencingFiles` (notamment `GivreCore.Injection.DllInjector`, `GivreLauncher.Core.Ankama`, `Zaap`, `TransportLog` et l'alias `Accounts.Account`). Les 5 groupes couvrent les **goulots** du produit ; ce n'est pas la liste complète des signatures et méthodes requises.

## 4. Chemin de build productif, une fois les vraies sources obtenues

1. Réacquérir de manière autorisée les **vrais** dossiers `GivreProxy/GivreCore` et `GivreLauncher/GivreLauncher.Core`, avec leurs projets, `.cs`, fichiers de build et références transitives. Identifier commit/source/licence et SHA-256 **indépendamment** des noms des fichiers. Ne rien emprunter au GivreClone installé/running.
2. Vérifier les APIs P1 et P2 contre les usages de `GivreClone.Protocol`/`Engine` : `RawProtobuf.{Encode,Parse,ConsumeFrames,FindTypeUrls,TryReadVarint}`, champs/membres de `ProtoField`, `TcpProxyServer.{Start,Stop,Replay,MessageCaptured}`, et provenance réelle de `CapturedMessage`. Restaurer aussi le flux Injecteur/Proxy si réellement sourcé ; aucun repli en faux ACK.
3. Vérifier P3/P4/P5 avec la véritable implémentation de `GivreLauncher.Core` et les dépendances de `Ankama`, `Zaap`, HTTP/SOCKS, et les mécanismes d'identité de session ; les interfaces reconstruisibles *en principe* depuis les appels restent **non implémentées** sans sources vérifiables.
4. Lancer la relecture des empreintes avec `MZP2DependencyInventory.py --verify` (réémettre le JSON seulement après revue d'éventuels nouveaux fichiers concurrents). Utiliser le raccord temporaire de **MZP1** `tools/MZP1DependencyBuild.ps1 -Mode Evaluate`, en passant `-CoreProject`, `-LauncherProject` et leurs deux SHA-256. Cela évalue la redirection des vrais `ProjectReference` sans modifier le produit.
5. Seulement après cette évaluation, exécuter `tools/MZP1DependencyBuild.ps1 -Mode Build` sur **les vrais sources épinglées**. La commande utilise `dotnet build GivreClone.App/GivreClone.App.csproj -c Debug --no-restore` : nécessite les assets NuGet déjà présents ; elle ne lance ni l'EXE ni aucun client. Ensuite seulement : tests purement hors ligne de framing, identité, anti-replay, PID, absence d'ACK inventé. Les validations live resteraient distinctes et demanderaient une preuve de session autorisée.

**État atteint ici :** `CALLSITE_INVENTORY_VERIFIED`, **pas** `DEPENDENCY_REBUILT`, `APP_COMPILED`, `GIVREBOT_1_3_1_PARITY` ni `ACK_VERIFIED`. Les deux projets sources demeurent le blocage principal. Ne pas faire passer `GivreRelay` pour `GivreCore` ou `GivreLauncher.Core`.

## 5. Validation et traçabilité

`py -3 tools/MZP2DependencyTests.py` : **11/11 PASS**, tests en répertoires de fixture éphémères couvrant absence de source, imports, alias, bruit lexical non importé, commentaires, faux fournisseur, exclusion du stub MZP0, identification relay, drift SHA, stabilité JSON et `ProjectReference` réel. `py -3 tools/MZP2DependencyInventory.py --verify ...` : **PASS** à la génération. Ces suites **n'exécutent pas** de code du bot ou des DLL ; aucun build d'une dépendance manquante n'est annoncé.
