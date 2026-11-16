# MZP24 — REA / inventaire binaire hors ligne de `GivreKama.dll`

**16 octobre 2026, worker-2. Scope : analyse statique uniquement.** Aucun lancement de l'original, du clone, de Ghidra ou de Dofus ; aucune ouverture de processus, de socket, de réseau, de compte, de mémoire ou de secrets. Seule la copie détachée `offline-evidence/v131-app/verified-binaries/GivreKama.dll` est l'objet binaire original. Les fichiers sources et la DLL reconstruite sous `native/GivreKama` servent uniquement de comparatifs. `GivreMods.dll` est reportée à un lot distinct.

## R — Résultats démontrés par les octets et REA

**Choix du lot :** `GivreKama.dll` = **12 551 168 octets**, contre `GivreMods.dll` = **14 305 280 octets** (tailles observées dans `verified-binaries`). L'original isolé a pour SHA-256 **`4F94B008B13CED359D27B976DDF489CDD92E0F3BDEBF7E2BA29B7ED0F7D067B1`**. Le SHA attendu est vérifié avant tout inventaire ; voir `artifacts/MZP24Binary/scan_givrekama.py`.

L'outil REA **6.5.0** installé dans un cache local préexistant a été appelé directement via `node <chemin-cache>/scripts/rea.mjs`, sans exécuter l'objet analysé ni demander au gestionnaire npm de télécharger quoi que ce soit. Les deux analyses ont terminé avec **exit 0** :

| Opération REA et preuve | Résultat exact | Artefact produit |
| --- | --- | --- |
| `inspect-pe-resources` ; provider `rea-pe-resources`, Evidence `ev_c252b79fcfe676406e09560b94efdf7be8894f536322e3b35140bcbdc922480a` | PE32+ ; machine 34404 (`0x8664` x64) ; **1 seule ressource**, `RT_MANIFEST` (type 24), identifiant 2, langue 1033, 381 o, offset fichier **12 550 232**, SHA-256 `4BB79DCEA0A901F7D9EAC5AA05728AE92ACB42E0CB22E5DD14134F4421A3D8DF`. Couverture `complete` au sens du parseur de ressources ; aucune icône. | `artifacts/MZP24Binary/GivreKama_rea_pe_resources.json` |
| `inspect-managed-artifact` ; provider `rea-dotnet-static`, Evidence `ev_779e0110a5f6e4cc6860b5bc3b3411699ccf50b6ef085d732dccbe557a0e08e9` | Classification **`not-managed`**, CLI data directory absent, pas de module/assembly .NET, pas de TypeDef/MethodDef/membres CIL, pas de décompilation IL applicable. PE32+ x64, **12 sections**. | `artifacts/MZP24Binary/GivreKama_rea_managed_admission.json` |

Ces observations REA ont été recoupées avec `pefile` en Python en lecture seule. Le rapport reproductible `GivreKama_pe_comparison.json` porte le hash exact, les en-têtes, sections, imports symboliques/IAT, ressources et une comparaison de la compilation locale. **Les quatre importations API réellement déclarées par le PE original sont :**

| Bibliothèque importée | Symbole importé dans le PE original | Niveau de preuve |
| --- | --- | --- |
| `WS2_32.dll` | `connect` | Table d'import PE vérifiée |
| `IPHLPAPI.DLL` | `GetAdaptersAddresses` | Table d'import PE vérifiée |
| `bcrypt.dll` | `BCryptHash` | Table d'import PE vérifiée |
| `KERNEL32.dll` | `WriteConsoleW` | Table d'import PE vérifiée |

**Aucun export PE**, répertoire CLR nul, répertoire d'import différé nul et table Authenticode (`IMAGE_DIRECTORY_ENTRY_SECURITY`) de taille nulle. La présence de `connect`, `GetAdaptersAddresses` et `BCryptHash` est compatible avec un module interceptant des fonctions système et traitant des identifiants, mais **ne prouve ni la pose effective d'un hook, ni une redirection, ni une falsification d'adresse réseau, ni l'envoi d'un handshake**. Le fait que `send` soit absent des imports PE originaux ne permet pas d'affirmer qu'aucun envoi n'est effectué : un binaire protégé peut résoudre ses fonctions autrement ou masquer son code.

**Structure du PE original :** 12 sections : `.text`, `.rdata`, `.data`, `.pdata`, `.detourc`, `.detourd`, `.fptable`, `.F9N` (toutes à 0 octet brut), `.U;W` (512 o), `.7x[` (**12 548 608 o bruts**), `.rsrc` (512 o), `.reloc` (512 o). Le point d'entrée est à **RVA `0xBF0C4E`** ; la section `.7x[` commence à `0x78D000` et son entropie **sur les premiers 64 KiB uniquement** est ~**7,0692 bits/octet**. Les noms atypiques, les sections standards virtuelles à 0 octet brut et le gros bloc `.7x[` sont des indices compatibles avec une protection/empaquetage, **sans identification d'un logiciel de protection précis**. Les noms `.detourc` et `.detourd` constituent des indices de liens avec Detours ; leur présence ne prouve pas l'activation runtime des hooks.

**Limite du fournisseur Ghidra/REA :** le provider Windows Ghidra documenté pour cette version cible les *applications* PE natives, avec une admission différente pour les DLL. Aucune analyse Ghidra profonde, décompilation de fonctions internes ou unpacking n'a été exécuté dans MZP24. La sortie REA ressources et la classification gérée sont des preuves de **structure**, pas de fonctionnalités. Aucune chaîne binaire, payload de ressource ou clé n'a été exporté.

## Correspondance contrôlée avec les interfaces du clone

La version compilée depuis le projet source `native/GivreKama/GivreKama.dll` a **180 736 o**, SHA-256 **`C53189FCCFE0964BD5A74AA2CF04153527F455A2AE5F2092A49F3BF1CD009A5D`**. Elle diffère byte pour byte du binaire original (12 551 168 o ; 12 sections et seulement **4 symboles** importés dans l'original contre **8 sections et 97 symboles** importés dans la version source construite). La source C++ du clone `native/GivreKama/GivreKama.cpp` était `0F88D6B31252200DD1C541BB47AA2370A8930F78D623CDD22CA07EB041CEBB8F` pendant l'inventaire. **Aucune identité source/binaire historique ne peut être déduite.**

| Fonction déclarée dans les sources locales | Source vérifiable | Soutien de l'original statique | Ce qui reste à démontrer |
| --- | --- | --- | --- |
| Redirection de `connect` pour la capture MITM | `native/GivreKama/GivreKama.cpp:44-91`, `:230-245` | Import `connect` et sections `.detour*` ; **compatibilité seulement** | Hook actif, conditions réelles de sélection de connexion, retour d'erreur, transfert intégral et destination réellement propre |
| Transmission d'un en-tête de 26 octets lié à la connexion/PID | `native/GivreKama/GivreKama.cpp:49-57`, `:69-87` | **Aucune preuve dans les imports/ressources** | Sérialisation correcte, limites et réponse du relais pour un client TEST |
| Hook optionnel des adaptateurs réseau | `native/GivreKama/GivreKama.cpp:162-204`, `:206-250` | `GetAdaptersAddresses` + `BCryptHash` ; **indices, pas comportement certifié** | Désactivation effective lorsque la fonction n'est pas demandée ; aucune altération du système hôte |
| Injection et contrôle de présence du module | `GivreClone.Engine/DofusCaptureHost.cs:37-54`, `:70-109`; `GivreClone.App/Services/NativeModules.cs:6-28`, `LaunchService.cs:667-681` | DLL native x64, aucune preuve de chargement | Identité unique PID + naissance + chemin + SHA ; preuve que le hook a réellement pris effet après présence du module |
| Relais et ownership de session | `GivreClone.Engine/BotHost.cs:22-40`, `../GivreProxy/GivreCore/Mitm/TcpProxyServer.cs:16-35`, `:77-111` | **Pas une fonction du binaire prouvée** : contrat C# reconstruit | Association par connexion vérifiée indépendamment, sans confondre PID contenu dans une trame et propriété de l'OS |
| Lancement historique et compatibilité du port | `GivreClone.App/Services/LaunchService.cs:533-577`, `:606-632`, `../GivreProxy/GivreCore/Mitm/TcpProxyServer.cs:77-109` | Le chemin historique source mentionne `5658`, l'original PE ne livre aucune preuve de port | Les reconstructions récentes exigent un relais TEST indépendant et interdisent de s'approprier `5657/5658/26666` |

Le contrat C++ de `HookConnect` a deux fragilités visibles **dans le code source reconstruit seulement** : `send(...)` est appelé sans vérifier que `RealConnect` a réussi (`GivreKama.cpp:85-87`), ni gérer explicitement un envoi partiel ; la structure `sockaddr` est modifiée en place à partir d'un pointeur déclaré `const` (`:59-83`). Cela motive des tests de non-régression dans un harnais purement synthétique. Ce ne sont pas des vulnérabilités attribuables à l'original protégé sans preuve de code identique.

Autre écart de contrat important : le `TcpProxyServer` reconstruit ne déclare aucune lecture du handshake natif de 26 octets dans son chemin de lecture `AcceptLoopAsync` / `PumpAsync` (`../GivreProxy/GivreCore/Mitm/TcpProxyServer.cs:114-225`) : il commence par la vérification externe de propriétaire, puis la consommation de frames Protobuf. Le protocole de handshake appartient à l'ancien exemple C++/HandshakeRelay et **n'est pas implémenté, ou démontré, par la présence du seul `TcpProxyServer`**. Le contrat de production doit expliciter lequel de ces deux modes est utilisé pour un client TEST, sans ajouter de mécanisme de confiance basé sur un PID non authentifié.

## Features précises proposées pour le clone (hors implémentation MZP24)

| Priorité / feature contrôlable | Critères d'acceptation **hors réseau, avant activation réelle** | Précondition runtime ultérieure |
| --- | --- | --- |
| **P0 — Sélecteur de DLL avec identité vérifiée** | Sur fixtures, refuser DLL au SHA inattendu, architecture non x64, fichier absent, lien de réanalyse, ou version de contrat inconnue ; afficher raison spécifique ; autoriser uniquement le binaire **construit et sélectionné pour le TEST**, jamais basculer silencieusement sur le binaire de l'original. | Permit réel de la DLL et du client TEST, générés par une autorité indépendante (`DllInjector.cs:13-28`, `:49-102`). |
| **P0 — Contrat de routage de connexion sans ambiguïté** | Tester en mémoire les adresses ciblées et non ciblées, le refus sur connect échoué, l'absence d'envoi partiel non traité, les longueurs incohérentes, l'annulation et le repli contrôlé. Vérifier par assertion que le mode test n'utilise jamais les ports originaux. | Tests de relais exclusivement possédé par le clone, sans session Givre originale ni port 5658. |
| **P0 — Contrat de handshake versionné** | Un harnais byte-array vérifie taille 26, ordre/tailles des champs, interprétation d'adresse, endianness déclarée, entrée fragmentée, dédoublonnage et rejet de payload mal formé. Un PID déclaré dans le payload **ne suffit jamais** à autoriser une connexion. | `ITestProxyRouteAuthority` authentifie indépendamment la connexion source (`TcpProxyServer.cs:16-35`, `:125-137`). |
| **P0 — Confirmation réelle de hook et transport** | Tests de statuts distincts : « DLL absente », « chargée mais hook non confirmé », « relais propre prêt », « source attestée », « capture vérifiée », « réponse positive serveur ». La seule présence en liste de modules ne peut pas faire passer le dernier niveau. | Capture et ACK obtenus du client TEST autorisé, corrélés à génération et connexion. |
| **P1 — Contrôles optionnels et observabilité bornée** | Option désactivée par défaut ; tests purement mémoire sur `GetAdaptersAddresses` simulé pour ne jamais modifier une entrée lorsque l'option est désactivée, aucune journalisation de MAC réelle, file d'événements bornée et arrêt idempotent. | Tests uniquement dans une instance TEST détenue, avec données synthétiques et sans intervention sur l'identité réseau de l'original. |
| **P1 — Compatibilité de la chaîne d'actions** | Tests `ProxyFrameSink` : sens C2S/S2C, identifiant connexion, fragments protobuf, écriture atomique et refus de rejouer après révocation ; jamais confondre `Sent` avec ACK serveur. | Un `IFrameSink` lié au propriétaire de connexion, cartographie du protocole et réponse serveur authentifiée. |

Ce sont des **propositions testables** déduites des sources et indices disponibles. Elles ne doivent pas être libellées « features de GivreClone original récupérées » avant analyse ou vérification fonctionnelle indépendante. `GivreMods.dll` (animation/tactique) est un autre module : `ModControl.cs:8-32` et `LaunchService.cs:699-716` en documentent l'interface reconstruite, mais **aucune fonction de `GivreMods` n'a été étudiée ici**.

## C — Modifications effectuées

Nouveaux fichiers **uniquement** dans les deux zones attribuées :

- `docs/MZP24_MODS_KAMA_REA.md` : conclusions, matrice de parité et propositions de tests.
- `artifacts/MZP24Binary/GivreKama_rea_pe_resources.json` : preuve REA des ressources.
- `artifacts/MZP24Binary/GivreKama_rea_managed_admission.json` : preuve REA de non-éligibilité .NET.
- `artifacts/MZP24Binary/scan_givrekama.py` : scanner PE statique borné, hash épinglé et invariant de 4 imports/1 ressource ; utilise `pefile`.
- `artifacts/MZP24Binary/GivreKama_pe_comparison.json` : comparaison structurelle original vs module C++ de reconstruction, ressources + symboles d'import + SHA.

## V — Validation

Commandes utilisées avec l'archive originale **détachée** comme entrée, sans réseau :

```powershell
$rea='%USERPROFILE%\AppData\Local\npm-cache\_npx\9b9809298fe40d2c\node_modules\rea-agents\scripts\rea.mjs'
$dll='%USERPROFILE%\Videos\GIVRE\GivreShareWork\offline-evidence\v131-app\verified-binaries\GivreKama.dll'
node $rea inspect-pe-resources $dll --format json
node $rea inspect-managed-artifact $dll --format json
py -3 -B artifacts\MZP24Binary\scan_givrekama.py
```

Résultats : **REA ressources exit 0, REA managed exit 0**, `PASS_STATIC_PINNED_PE_ONLY`, 4 symboles PE, 1 ressource, hash original exact, pas de metadata CLI. Les JSON ont été générés sous `artifacts/MZP24Binary/`, en lecture du seul original détaché et de la référence source locale ; aucune exécution des DLL et aucun test réseau.

## B — Blocages et prochaines preuves

1. **Protection/empaquetage** : 12 sections atypiques et corps brut dominé par `.7x[` ; ni l'import minimal ni l'absence d'IL ne permettent de reconstruire à la lettre l'implémentation originale. Aucun RVA de fonction métier certifié, aucun pseudo-code propre et aucune liste exhaustive de hooks originaux.
2. **Admissibilité Ghidra Windows DLL** : le fournisseur MCP documenté ne couvre pas ce rôle comme l'analyse d'un EXE natif ; les commandes REA de cette passe ne constituent pas une décompilation Ghidra. Une analyse Ghidra headless autonome de DLL détachée pourrait être envisagée dans un lot avec temps et stockage dédiés, et mesurer exactement ce qui reste lisible.
3. **Parité à prouver** : les 4 imports suggèrent des zones fonctionnelles communes à la reconstruction, mais les deux DLL diffèrent fortement en taille/structure/hash. Les contrats de handshake, redirection, conservation des connexions, post-connexion et réponse serveur restent hypothétiques pour le binaire original.
4. **Exécution TEST** : les autorités d'identité de processus et de connexion, la recette Launcher 2/Zaap, un relais TEST réellement détenu, une trame décodée et un ACK serveur corrélé demeurent prérequis. Ce worker n'a ni construit ni exécuté ces étapes.

**FINISH MZP24 worker-2 :** preuve statique de `GivreKama.dll` terminée, sans modification de l'installation ou des sessions originales.
