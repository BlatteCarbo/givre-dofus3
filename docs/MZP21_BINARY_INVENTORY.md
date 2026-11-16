# MZP21 — inventaire binaire statique GivreClone, installation et référence détachée

**16 octobre 2026 — worker-2.** Inspection exclusivement statique des fichiers exécutables, DLL, métadonnées PE et manifestes de dépendances. Aucun lancement de GivreClone, de clone, de Dofus ou de moteur REA ; aucun accès à un processus, une session, un compte, un jeton, une capture, un dump ou au réseau. Les fichiers des autres workers n'ont pas été modifiés.

## R — résultat : identité du programme et portée de la version

Le **vrai exécutable installé** se trouve à `%USERPROFILE%\AppData\Local\Givre\app\GivreClone.exe` (et **pas** sous `%LOCALAPPDATA%\GivreClone`, qui ne contient ici que `browser`). Le fichier binaire détaché `offline-evidence/v131-app/verified-binaries/GivreClone.exe` a **exactement** sa taille et son SHA-256. Le ZIP `offline-evidence/v131-app/GivreDistribution-candidate-D66690B6.zip` a également le même SHA-256 que `%LOCALAPPDATA%\Givre\update\pkg.zip`. Ce sont des correspondances d'octets, pas des suppositions fondées sur le nom du produit.

| Élément | Taille (octets) | SHA-256 | Observation |
| --- | ---: | --- | --- |
| `app/GivreClone.exe` original et copie isolée | 54 083 584 | `21C9DE5BA4D15763E9DAF029FD7432E3574EA113C9D1F44175FA9E0590290758` | PE32+ x64, machine `0x8664`, sous-système GUI, pas de répertoire CLR |
| `app/GivreKama.dll` original et copie isolée | 12 551 168 | `4F94B008B13CED359D27B976DDF489CDD92E0F3BDEBF7E2BA29B7ED0F7D067B1` | PE32+ x64 natif |
| `app/GivreMcp.exe` original et copie isolée | 1 863 168 | `607BEEA1BF3D8D8D361D79C6A2609B1500F7BAB695E282D97F7AEB0E434A807F` | PE32+ x64 natif, sous-système console |
| `app/GivreMods.dll` original et copie isolée | 14 305 280 | `B8F95D30B178D4FD3A34B88DAA148F29D62D72371524067DF12CDD6CE06B0343` | PE32+ x64 natif |
| `app/WebAuth/GivreLauncher.WebAuth.exe` original et copie isolée | 12 666 880 | `19281A61CB097D15790DFEAF0AC332613A5DAF3DE0502F0AEC9C25864E003DC4` | PE32+ x64 natif, sous-système GUI |
| `app/WebAuth/GivreLauncher.WebAuth.dll` original et copie isolée | 76 288 | `31DDA6E03F6921A66D58BBE85110639D4BA367F94822B264A4E44B7E662BC1A6` | PE/CLI managé, machine COFF `0x14c` (ne signifie pas que cet assembly IL est limité à un processus x86) |
| `update/pkg.zip` original et archive isolée | 93 234 109 | `D66690B642204FC35B3EF2E4A3B9EF53483B41A447B0BFE3C04DFA2D38DB06FD` | 22 entrées ZIP, dont trois répertoires de runtime |
| `GivreUpdater.exe` installé, hors `app` | 12 423 680 | `073741A15B954130E60545DF6AA97641CA95E4379DB0B78D1843D356E81E462D` | Updater séparé, non lancé et non analysé en profondeur |
| Setup détaché `GivreClone-Setup-1.3.1.exe` | 226 304 | `4D4D6458A0B808173749EB98D9FC3B199748B707CEFD8C7319809769C1DF6E83` | PE natif x64, bootstrap de téléchargement ; ne contient pas le vrai `GivreClone.exe` |

**Limite de version :** les ressources Windows de `GivreClone.exe` installé/copié et du compagnon WebAuth annoncent `FileVersion=1.0.0.0` et `ProductVersion=1.0.0`. Le nom commercial **1.3.1** est celui du setup et de l'interface rapportée, mais aucun champ de version dans ce PE ne l'atteste cryptographiquement. Le hash établit l'identité **avec les octets installés le 16 octobre**, pas une nomenclature de release officielle. Voir `docs/MZP16_SETUP_131_FINDINGS_20261016.md` et `docs/MZP17_REAL_131_PACKAGE_AND_WEBAUTH_20261016.md`.

## Architecture et résultats PE reproductibles

Les cinq binaires natifs du tableau présentent un `IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR` nul : ILSpy ne peut pas en extraire directement des corps C# IL. L'export `DotNetRuntimeDebugHeader` existe dans `GivreClone.exe` (39 exports, 9 sections) et `GivreMcp.exe` (1 export, 6 sections) : c'est un **indice de NativeAOT**, à qualifier avec leurs headers natifs, mais pas une preuve que le corps protégé est récupérable. L'EXE principal a une section `.g0z` d'environ 53,97 Mo et des sections standards à zéro octet brut, d'après `offline-evidence/v131-app/MZP17-native-classification.json` ; les sections des DLL `GivreKama` et `GivreMods` montrent une disposition atypique comparable. Ce sont des **candidats protégés/empaquetés** sans identification ferme d'un packer. Pour ces fichiers, Ghidra peut seulement analyser statiquement ce qui est réellement visible dans le PE, sans garantie d'obtenir le programme déprotégé.

Les répertoires d'imports PE accessibles ont livré les noms de DLL suivants (aucune résolution ou chargement dynamique n'a été pratiqué) :

| Module | Imports statiques principaux (noms de DLL) | Détail |
| --- | --- | --- |
| `GivreClone.exe` | `ADVAPI32`, `bcrypt`, `CRYPT32`, `IPHLPAPI`, `KERNEL32`, `ncrypt`, `ole32`, `OLEAUT32`, `Secur32`, `VERSION`, `WS2_32`, API sets CRT | 18 descripteurs d'import ; aucune entrée de delay import relevée |
| `GivreKama.dll` | `WS2_32`, `IPHLPAPI`, `bcrypt`, `KERNEL32` | 4 descripteurs ; rôle de hook/capture décrit par les sources de reconstruction, fonctionnement natif non certifié par ces imports |
| `GivreMcp.exe` | `ADVAPI32`, `bcrypt`, `KERNEL32`, `ole32`, `Secur32`, API sets CRT | 12 descripteurs |
| `GivreMods.dll` | `USER32`, `KERNEL32` | 2 descripteurs |
| `GivreLauncher.WebAuth.exe` | `SHELL32`, `ADVAPI32`, `KERNEL32`, `USER32`, API sets CRT | 12 descripteurs ; bootstrap natif |
| `GivreLauncher.WebAuth.dll` | `mscoree.dll` | CLR managé, source IL décompilable avec `ilspycmd` |

Les imports de l'EXE principal sont des noms observés dans sa table PE : ils ne prouvent ni les appels réseau effectués, ni les bibliothèques éventuellement chargées par d'autres mécanismes.

## Dépendances livrées avec l'application

Le paquet contient, en plus des modules principaux, ces bibliothèques **avec SHA-256 relevés en lecture seule sur l'installation** :

| Fichier relatif à `app/` | Taille | SHA-256 |
| --- | ---: | --- |
| `aspnetcorev2_inprocess.dll` | 392 488 | `3A0131399A65DD63D50F325B62DFDB891DDACC80A1A9180897AF159651FCB5A5` |
| `av_libglesv2.dll` | 5 394 096 | `53191A77FE783CD757CA7767077C2A64A662E7043777A5B4AB74980D4A0B73E3` |
| `libHarfBuzzSharp.dll` | 1 816 088 | `145ADE963EBA427027D5E1381DB5D51A0EF9E98E9FEF9EFBFA6074CA20DA246F` |
| `libSkiaSharp.dll` | 11 628 896 | `7DEC3BA900AB353491E6446F0083739924C6F8DD668832E2F09D38EBFFDBBE1C` |
| `WebAuth/Microsoft.Web.WebView2.Core.dll` | 567 896 | `616157863B1CD1F1330B4914DD013ADEF10A4CE8CD819598FD6BD4037C2C1646` |
| `WebAuth/Microsoft.Web.WebView2.WinForms.dll` | 38 360 | `A2DDFDE024F6A248576A69CE9A54B5407AD915EE8FEC7DFF3A089A60948AC0AE` |
| `WebAuth/Microsoft.Web.WebView2.Wpf.dll` | 51 160 | `2FE5FBCE16F19324A41AE40EB81FEF4E48A853969185F24AB7902350FEABF536` |
| `WebAuth/runtimes/win-x64/native/WebView2Loader.dll` | 165 336 | `1CBCA1698175936A93D0C4D4148ED355E1B91415C6A4DB6D762F017DE3B6EB9A` |
| `WebAuth/runtimes/win-x86/native/WebView2Loader.dll` | 116 184 | `DD4C6797FF04625934A14AA7B22D76B847C8B6BB1CCCD7C587EE6FEC6B636121` |
| `WebAuth/runtimes/win-arm64/native/WebView2Loader.dll` | 140 368 | `D17168D459F16EAE2F944D31B2ECD6714B3447101F6BB531D162445D2BBB1C19` |

L'archive contient aussi `WebAuth/GivreLauncher.WebAuth.deps.json` et `WebAuth/GivreLauncher.WebAuth.runtimeconfig.json`. Lecture **des seules clés de dépendances**, depuis le ZIP détaché : cible `.NETCoreApp,Version=v10.0`, bibliothèques `GivreLauncher.WebAuth/1.0.0` et `Microsoft.Web.WebView2/1.0.2478.35`, frameworks requis `Microsoft.NETCore.App 10.0.0` et `Microsoft.WindowsDesktop.App 10.0.0`. La présence des DLL WebView2 et des loaders n'atteste pas la présence ni le fonctionnement du runtime Edge WebView2 sur chaque machine. Il n'y a pas de `GivreClone.deps.json` dans le ZIP d'origine, cohérent avec une publication du binaire principal native/NativeAOT candidate ; les imports ne suffisent pas à identifier toute sa pile logicielle.

## Comparaison avec la reconstruction locale

La copie reconstruite **`artifacts/MZP19LocalBuild/GivreClone.exe`** mesure seulement **274 432 octets**, SHA-256 `7236F7F3E63E3CED98697339C5BD87E5A4B0015A9141263922A3D8FF988DE6FF`. C'est un PE x64 sans répertoire CLR dans l'EXE hôte. Il est accompagné d'un **`GivreClone.dll` managé séparé**, 27 462 656 octets, SHA-256 `47E92CC7D3C770C3B4759118C59DAD8D6862E510B52228B79A7C9A997BBAB764`. Les dépendances reconstruites incluent `GivreClone.Engine.dll` (SHA `12F8242257BA9CE32DFA3383F96D7261D2B2021197C440F549CCD7B8689C16EF`), `GivreClone.Protocol.dll` (`C184450FD3EE2C85A81900278251A7CBAFDD2E2B075677643F23C09959A7BCB6`) et `GivreCore.dll` (`A22491FBBA33DE2BC1296A0AB8BAFA56F18FB65E5FB237070F61F3531C723253`). Ces identités démontrent que **le clone compilé et l'original installé sont des distributions distinctes** ; elles ne permettent ni de compter leurs fonctions identiques, ni de conclure à une parité gameplay.

## Outils d'analyse statique et REA sur ce poste

1. **PE/COFF borné** : `pefile` (Python) est disponible et a permis de vérifier `Machine`, `Subsystem`, points d'entrée, nombre de sections, RVA CLR, noms de DLL importées, nombre d'exports. Les scripts déjà présents `tools/MZP0_OfflinePeAudit.py` et `tools/MZP17NativeBinaryTriage.py` fournissent des contrôles PE à empreintes. Attention : le second écrit son JSON par défaut et **n'a donc pas été exécuté** dans ce lot, limité à un seul nouveau document.
2. **Assemblies managées** : `ilspycmd.exe` existe sous `%USERPROFILE%\.dotnet\tools\`. Le compagnon `WebAuth/GivreLauncher.WebAuth.dll` est éligible à l'analyse IL et dispose déjà d'une reconstruction dans `offline-evidence/v131-app/decompiled-WebAuth-IL`. `GivreClone.exe` et `GivreMcp.exe` ne disposent pas de metadata CLR exploitable de la même manière.
3. **Ghidra headless** : `%USERPROFILE%\AppData\Local\Programs\REA-Tools\ghidra_12.1.4_PUBLIC\support\analyzeHeadless.bat` est présent ; `Ghidra/application.properties` indique **12.1.4**, `application.java.min=21`. Java et `javac` sont présents dans `...\REA-Tools\jdk-25.0.4.1+1\bin\`. C'est une possibilité d'analyse future sur **la copie détachée**, après mise en place d'un répertoire de projet isolé ; **Ghidra n'a pas été exécuté ici**.
4. **REA (repo source)** : `%USERPROFILE%\Videos\GIVRE\ReaReference\package.json` annonce `rea-agents` **6.0.0**, avec points d'entrée `scripts/rea.mjs` et commandes telles que `rea providers --json`, `rea doctor --provider ghidra --json`, `rea analyze <fichier> --provider ghidra --json`. D'après `docs/windows-ghidra-p0.md`, le fournisseur Ghidra Windows expérimental prend en charge les applications PE natives x86/x64 en lecture seule et refuse les DLL managées/les cibles incompatibles. Le fournisseur IL/CLI est distinct. **État réellement observé :** `rea` n'est pas une commande accessible dans `PATH`, `AppData/Roaming/npm/rea.cmd` et le module natif `ReaReference/native/windows/build/rea-windows-x64.node` n'existent pas ; ni `ReaReference/dist/cli/index.js`. Le checkout source **n'est pas un fournisseur REA CLI opérationnel prouvé**. Installer/compiler/configurer REA est une étape séparée à valider avant de prétendre lancer Ghidra via ce CLI.
5. **Limite des autres fournisseurs REA** : la documentation cite Hopper et IDA en plus de Ghidra, mais aucune installation ou disponibilité opérationnelle Hopper/IDA n'a été établie ici. Les capacités CLI déclarées dans des documents ne prouvent pas que leur serveur MCP fonctionne actuellement. Aucune décompilation native profonde ni contrôle `rea doctor` n'a été lancé pendant cette investigation.

## C / V / B — modifications, vérification et blocages

**C —** un seul fichier nouveau : `docs/MZP21_BINARY_INVENTORY.md`. Aucun binaire, source d'application, outil, package, configuration, compte ou installation modifié.

**V —** empreintes SHA-256 calculées avec `Get-FileHash` pour six modules dans **les deux emplacements** installé et détaché : **6/6 correspondances** ; archive ZIP installée/détachée : **1/1 correspondance**. Classification de **7 PE détachés** avec `pefile` (machines, sous-systèmes, import directories, CLR et export AOT), liste de **22 membres** de l'archive, métadonnées de dépendances WebAuth lues dans ce ZIP, empreintes des **10 DLL de dépendances** installées et comparaison du clone MZP19. Contrôle par `Test-Path` des binaires Ghidra/JDK/REA sans exécution de moteur. Aucun résultat ne certifie un lancement, une licence, un client Dofus, une session connectée ou un ACK serveur.

**B —** l'origine de la dénomination commerciale exacte v1.3.1 reste distincte de la `FileVersion=1.0.0.0` ; la protection du binaire principal peut masquer le code pour Ghidra ; `rea` n'est pas prêt depuis ce checkout. Pour approfondir, lancer un analyseur *sur la copie isolée* avec des sorties dédiées et une preuve de hash, en limitant l'analyse aux ressources et fonctions réellement présentes. La comparaison statique n'établit aucune équivalence comportementale entre le binaire original et le clone local ; il faut des tests fonctionnels séparés sous contrôle de PRIME.
