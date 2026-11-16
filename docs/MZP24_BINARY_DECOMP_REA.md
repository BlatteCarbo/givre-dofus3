# MZP24 — REA / Ghidra / PE et désassemblage de la copie originale GivreMcp.exe

16 octobre 2026 — worker-1, audit **exclusivement hors ligne**, fichier cible
`offline-evidence/v131-app/verified-binaries/GivreMcp.exe`. Périmètre d'écriture :
uniquement ce nouveau rapport et les nouveaux fichiers de
`artifacts/MZP24REA/`. Aucun fichier MZP23, binaire installé, source produit,
processus GivreClone, compte, session, socket ni réseau inspecté ou modifié.

## R — résultat démontré et limites

Le spécimen **original détaché** mesure **1 863 168 octets** et possède le
SHA-256 **`607BEEA1BF3D8D8D361D79C6A2609B1500F7BAB695E282D97F7AEB0E434A807F`**.
Il correspond à l'identité MZP21
(`docs/MZP21_BINARY_INVENTORY.md:9-16`). PE32+ x64, base d'image
`0x140000000`, point d'entrée **RVA `0x115A80`** (VA `0x140115A80`),
6 sections, 12 descripteurs d'import et 1 export
`DotNetRuntimeDebugHeader` **RVA `0x196F20`**. Répertoire CLI/CLR :
`0x0`. Cela rend la décompilation .NET IL classique inapplicable à cet EXE
et représente un **indice NativeAOT** ; aucune classe C# source, TypeDef
managée ou méthode de service n'est pour autant récupérée automatiquement.

L'analyse de code x64 indépendante, avec Capstone borné aux intervalles
d'exception Windows `.pdata`, a couvert **6 335 plages de fonctions** et
**307 880 instructions**. Elle a identifié **185 références instructionnelles
exactes** vers **70 fonctions importées distinctes** (certains imports ne
sont jamais directement référencés par une instruction dans ce corpus).
Le point d'entrée appartient à l'intervalle `[0x115A80, 0x115A92)`.
Le compte MZP23 de **6 713 procédures Ghidra** est une observation
antérieure d'un autre moteur de détection, **pas** le nombre de fonctions
attestées par les seules entrées `.pdata`
(`docs/MZP23_REA_RESTART_AND_TEST_BASELINE_20261016.md:3-6`).

### Points natifs et marqueurs intéressants

| Nature | RVA / VA ou offset brut | Preuve effective |
| --- | --- | --- |
| Entrée PE | RVA `0x115A80` ; VA `0x140115A80` | `IMAGE_OPTIONAL_HEADER.AddressOfEntryPoint`, fonction `.pdata` `[0x115A80,0x115A92)` |
| Fonction candidate pour écriture de sortie | Fonction début `0xDAF70`; `call GetStdHandle` à `0xDAF8A`, `call WriteFile` à `0xDAFA7` | Décodage réel des opcodes x64 RIP-relative vers leurs slots IAT `KERNEL32.dll` et limites `.pdata` |
| Résolution d'API native | `GetProcAddress` depuis les fonctions `0xD5E50`, `0xDA740`, `0xDAB97`, `0xDACCB`, `0xDAFE0`, `0xDB0E0` | Sites d'appel import exacts, dont plusieurs depuis la même fonction ; ce sont des routines candidates runtime, pas des APIs MCP identifiées |
| Nom de module | `GivreMcp` : offset `0x11BF04`, RVA `0x11D304` ; `GivreMcp.exe` : offset `0x192BC2`, RVA `0x193FC2` | Octets source lisibles et conversion offset → RVA indépendamment vérifiés |
| Marqueur de namespace/assembly | `GivreClone.Mcp` : offset `0x1453B6`, RVA `0x1467B6` | Chaîne présente ; **n'atteste pas** la présence d'un TypeDef ni la signature d'une classe source |
| Diagnostic outils indisponibles | `Explains why GivreClone tools are unavailable and what the user must do. Calling it also retries the connection` : début à l'offset `0x142026`, RVA `0x143426` | Texte de description embarqué, compatible avec un outil de diagnostic/reconnexion ; sa procédure d'appel n'est pas démontrée |
| Diagnostic de reconnexion | `GivreClone is connected now: its tools are available. List the tools again` : offset `0x144CCB`, RVA `0x1460CB` | Texte embarqué, compatible avec un état de connexion restaurée ; aucune connexion réelle observée |
| Autre chaîne de configuration | `givremcp-ini` : offset `0x1495A6`, RVA `0x14A9A6` | Marqueur textuel ; le format, l'emplacement de configuration et l'usage effectif sont inconnus |
| Export runtime | `DotNetRuntimeDebugHeader`, RVA `0x196F20` | Export PE réel, pas une fonction métier ni une preuve de source récupérable |

**Précaution fonctionnelle :** les imports `WaitNamedPipeW`, `ReadFile`,
`WriteFile`, `GetStdHandle`, `GetProcAddress` et les chaînes MCP
constituent des pistes de transport/IPC, mais une table d'import, une chaîne
ou un nom Ghidra ne prouvent pas l'établissement d'un canal, sa propriété, un
appel d'outil ou une réponse. Dans les 6 335 intervalles `.pdata` inspectés,
l'analyseur **n'a pas** trouvé de référence RIP *directe* ciblant les chaînes
MCP choisies dans une fenêtre [-40,+8] autour de leur RVA. Ce constat n'exclut
pas les références indirectes, les pointeurs vers objets NativeAOT, les
fonctions feuilles absentes de `.pdata` ou les usages runtime.

## Méthode et commandes réellement exécutées

Sous PowerShell, depuis
`%USERPROFILE%\Videos\GIVRE\GivreShareWork` :

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\offline-evidence\v131-app\verified-binaries\GivreMcp.exe'
py -3 -B '.\artifacts\MZP24REA\inspect_mcp_pe.py'
py -3 -B '.\artifacts\MZP24REA\mcp_capstone_xrefs.py'
py -3 -B '.\artifacts\MZP24REA\mcp_import_callers.py'
py -3 -B '.\artifacts\MZP24REA\verify_static.py'
```

Le script PE utilise `pefile` pour la table DOS/PE, les sections,
`AddressOfEntryPoint`, l'IAT, l'export et la conversion
`get_rva_from_offset`. Les chaînes ASCII/UTF-16 filtrées sont enregistrées
avec offsets, SHA et type ; celles évoquant secrets, tokens, URLs ou comptes
sont exclues du rapport. Capstone lit le code original **uniquement par
intervalles `.pdata`** (triplets begin/end/unwind), recalcule les cibles des
opérandes `[RIP+disp32]` et confronte les adresses aux slots d'import exacts.
Les fichiers JSON contiennent la preuve machine des adresses de fonction,
opcodes et limites de couverture. Aucun hook, injection, dump mémoire,
désérialisation de session, fichier de profil ou appel réseau n'est utilisé.

REA CLI disponible via cache npm local : `rea-agents@6.5.0`, entrée exacte
`%USERPROFILE%\AppData\Local\npm-cache\_npx\9b9809298fe40d2c\node_modules\rea-agents\scripts\rea.mjs`.
Invocation effectuée sur **le même binaire détaché** :

```powershell
$cli = '%USERPROFILE%\AppData\Local\npm-cache\_npx\9b9809298fe40d2c\node_modules\rea-agents\scripts\rea.mjs'
$exe = (Resolve-Path '.\offline-evidence\v131-app\verified-binaries\GivreMcp.exe').Path
$snap = (Join-Path (Resolve-Path '.\artifacts\MZP24REA').Path 'GivreMcp_snapshot.json')
node $cli analyze $exe --provider ghidra --json --snapshot $snap
```

**Statut exact de CETTE tentative REA : échec**, sortie structurée dans
`artifacts/MZP24REA/rea_overview.json`, `code=provider_timeout`,
`operation=list_documents`, `timeout_ms=330000`, échec avant retour
`binary_overview`. Le log montre que Ghidra 12.1.4 a importé le vrai PE,
mais **ne donne aucun dossier de fonctions ou pseudocode nouveau** à cette
étape. L'aperçu REA **réussi précédemment** est conservé sous
`artifacts/MZP23REAEvidence/GivreMcp_ghidra_overview.json` (SHA sujet
identique, base `0x140000000`, 6 713 procédures, 690 chaînes définies,
Evidence `ev_6c0789a44e9ad1b1fa7aa9736a1c922c0e4027fc6028da87d0a6d3a39c1c5d7a`).
Il reste distinct de l'exécution MZP24 et n'a pas été écrasé.

Ghidra headless installé sous
`%USERPROFILE%\AppData\Local\Programs\REA-Tools\ghidra_12.1.4_PUBLIC\support\analyzeHeadless.bat`,
JDK `REA-Tools\jdk-25.0.4.1+1`. Premier passage direct sur un **nouveau
projet MZP24** : import/autoanalyse du PE réussis, mais son script
`MZP24GhidraEvidence.py` ne s'est pas exécuté car le mode Windows headless
n'a pas activé PyGhidra :
`Ghidra was not started with PyGhidra. Python is not available`.
Le code Java `MZP24GhidraJava.java` sous `artifacts/MZP24REA/` vise
les mêmes fonctions sans dépendance PyGhidra. Pour le lancer :

```powershell
$art = (Resolve-Path '.\artifacts\MZP24REA').Path
$env:JAVA_HOME = '%USERPROFILE%\AppData\Local\Programs\REA-Tools\jdk-25.0.4.1+1'
$exe = (Resolve-Path '.\offline-evidence\v131-app\verified-binaries\GivreMcp.exe').Path
& '%USERPROFILE%\AppData\Local\Programs\REA-Tools\ghidra_12.1.4_PUBLIC\support\analyzeHeadless.bat' $art MZP24JavaProject -import $exe -scriptPath $art -postScript MZP24GhidraJava.java (Join-Path $art 'mcp_ghidra_java_evidence.tsv') -analysisTimeoutPerFile 180 -deleteProject
```

La preuve d'exécution du script Java dépend de la présence d'un fichier
`mcp_ghidra_java_evidence.tsv` se terminant par
`MZP24_GHIDRA_JAVA_COMPLETE` et de l'absence d'erreur
`REPORT SCRIPT ERROR` dans `ghidra_java.log`. Un code de sortie
`analyzeHeadless=0` **à lui seul** est insuffisant (le premier passage
Python a renvoyé 0 malgré l'échec du post-script).

**État de remise à PRIME : Java Ghidra PENDING.** La commande du second
passage headless a été lancée avec projet différent, log
`artifacts/MZP24REA/ghidra_java.log`. Au moment de la dernière vérification,
elle était encore occupée et aucun fichier de sortie certifié par le marqueur
`MZP24_GHIDRA_JAVA_COMPLETE` n'avait été lu. Ce lot se termine sur les preuves
PE, Capstone et REA déjà acquises ; aucun résultat de décompilation Java n'est
déclaré disponible, et une sortie différée éventuelle devra être relue et
validée avant promotion.

## C — changements effectivement livrés

- `docs/MZP24_BINARY_DECOMP_REA.md` — ce rapport nouveau.
- `artifacts/MZP24REA/inspect_mcp_pe.py`, `mcp_pe_static.json` — PE, SHA, imports/exports et chaînes filtrées.
- `artifacts/MZP24REA/mcp_capstone_xrefs.py`, `mcp_static_function_xrefs.json` — recherche stricte des pointeurs RIP vers marqueurs applicatifs.
- `artifacts/MZP24REA/mcp_import_callers.py`, `mcp_import_callers.json` — références exactes vers IAT et RVA des fonctions de `.pdata`.
- `artifacts/MZP24REA/verify_static.py`, `verification.log` — tests indépendants des rapports contre les octets immuables.
- `artifacts/MZP24REA/rea_overview.json`, `rea_overview.stderr.txt`, `ghidra_headless.log`, `ghidra_headless.stderr.txt` — sorties réelles de REA et Ghidra.
- `artifacts/MZP24REA/MZP24GhidraEvidence.py` et `MZP24GhidraJava.java` — scripts de collecte bornée, le premier bloqué par l'absence de PyGhidra dans ce mode.
- `artifacts/MZP24REA/ghidra_java.log` et `ghidra_java.stderr.txt` — deuxième passage headless lancé, résultat non certifié au transfert.

## V — validations exécutées

- **PASS** : SHA/longueur indépendants de la copie ; identité identique
  dans les trois rapports JSON, architecture/base/entry/CLR et nombre
  de sections/imports/exports contrôlés par `verify_static.py`.
- **PASS** : vérification **des octets source réels** et offsets/RVA de
  chaque chaîne sélectionnée (54 sélections filtrées, 30 matchs masqués
  par les filtres) ; en particulier `GivreClone.Mcp` à RVA `0x1467B6`.
- **PASS** : **185/185 références IAT** redésassemblées depuis leurs
  adresses `instruction_rva`, mnémonique, opérande RIP et import exact
  contrôlés ; **6 335** intervalles valides `.pdata`. Le journal se
  termine par `MZP24_VERIFY_STATIC_COMPLETE`.
- **Observations REA/Ghidra qualifiées** : REA CLI MZP24 a échoué
  (timeout), alors que l'import Ghidra direct a réussi et que son script
  Python a explicitement échoué ; l'essai Java est **PENDING**, et aucun
  résultat de décompilation n'est revendiqué à ce stade.

## B — blocages et piste utile pour PRIME

Le binaire expose des marqueurs de liaison MCP/GivreClone et un ensemble
de routines natives/Windows documentées ci-dessus. **L'extraction de
classes C# exactes, de handlers JSON-RPC, de signatures métier et de
pseudocode de ces fonctions reste non certifiée** tant que le post-script
Java n'a pas produit un dossier fonction attesté. Un nom de chaîne
`GivreClone.Mcp` n'est pas une définition de classe et la présence de
`WaitNamedPipeW` dans les imports ne prouve pas qu'un named pipe soit
ouvert par l'application. Le clone ne doit pas se déclarer compatible
MCP ni relié à GivreClone sur cette seule base.

Pour avancer ensuite : joindre les éventuels RVA et hashes de
décompilation Java aux points `0xDAF70` (écriture I/O native) et
`0x115A80` (entrée PE), puis vérifier à part la sémantique des
messages et handlers. Ces tests sont statiques, indépendants des
trois clients Givre originaux, et aucune preuve réseau, licence,
compte, gameplay ou ACK serveur n'en découle.
