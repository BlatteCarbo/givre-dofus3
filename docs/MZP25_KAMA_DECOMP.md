# MZP25 — analyse Ghidra du GivreKama.dll original détaché

**16 octobre 2026 — worker-1.** Objet : `offline-evidence/v131-app/verified-binaries/GivreKama.dll`, binaire original **copié et vérifié**, analysé statiquement. Écritures limitées à ce **nouveau** rapport et aux **nouveaux** fichiers `artifacts/MZP25Kama/*`. Aucune opération sur l'installation, la DLL active, les processus, comptes, sessions, sockets, réseau, hooks ou injection ; aucun code produit modifié.

## R — résultat fondé sur le fichier original

| Observation | Valeur ou résultat | Preuve |
| --- | --- | --- |
| Empreinte et taille | **12 551 168 octets**, SHA-256 **`4F94B008B13CED359D27B976DDF489CDD92E0F3BDEBF7E2BA29B7ED0F7D067B1`** | `Get-FileHash`, `static_pe.py`, `verify_static.py` ; concorde avec `docs/MZP21_BINARY_INVENTORY.md:9-14` |
| Format | PE32+ x64 `Machine=0x8664`, base d'image **`0x180000000`**, CLR directory **`0x0`** | Tables PE, `artifacts/MZP25Kama/kama_pe.json` |
| Entrée | **RVA `0xBF0C4E`**, VA **`0x180BF0C4E`** | `AddressOfEntryPoint`, octets désassemblés depuis le fichier exact |
| Sections | **12** ; `.text`, `.rdata`, `.data`, `.pdata`, `.detourc`, `.detourd`, `.fptable`, `.F9N` ont **`SizeOfRawData=0`** | En-têtes et offsets PE vérifiés |
| Corps accessible | `.7x[` RVA `0x78D000`, **12 548 608 octets bruts**, entropie **7,76149 bits/octet** | Entropie Shannon calculée sur données du fichier ; entropie haute ≠ identification précise d'un packer |
| Imports accessibles | 4 DLL : `WS2_32`, `IPHLPAPI`, `bcrypt`, `KERNEL32` ; les symboles **`connect`**, **`GetAdaptersAddresses`**, **`BCryptHash`** sont explicitement importés | Répertoire d'imports du binaire original, `kama_pe.json` |
| Export | Aucun export PE nommé détecté | Répertoire d'exports parsé (pas une conclusion sur les points d'entrée non exportés) |
| Autoanalyse Ghidra native | **Réussie** avec **Ghidra 12.1.4**, x86:LE:64:default:windows ; **1 462 fonctions estimées**, **9 noms autres que `FUN_*`** | `ghidra.log`, `kama_ghidra_functions.tsv`. Nombre de fonctions Ghidra ≠ nombre de routines produit démontrées |
| Décompilation bornée | **5 sorties `DECOMP_OK` réelles**, RVA `0xBF0C4E`, `0x78D000`, `0x1030`, `0x1090`, `0x1100` | `MZP25KamaHeadless.java`, `kama_ghidra_functions.tsv`, `verify_ghidra.py` ; décompilation de **fragments** seulement |

### Extraits authentiques Ghidra et leur portée

Le point d'entrée dans le corps fortement entropique est réellement identifié à
`RVA 0xBF0C4E` (`entry`, Ghidra indique **43 adresses** dans son corps).
Le décompilateur a produit cet extrait textuel **réel** (sortie `DECOMP_OK`,
SHA-256 du pseudocode complet
`63da40ad126bc29a0bd7abb8030ce94939a433b2db97f0cc4e8beeb331026cec`) :

```c
undefined4 entry(void)
{
  FUN_180c4a21d();
  syscall();
  FUN_180c7a0d2(0x180bf0c68);
  return _DAT_f8868a1f540be94c;
}
```

Ce pseudocode contient des valeurs et un enchaînement inhabituels : **il ne révèle aucune opération `connect`, `send`, détournement Detours ni construction de handshake**. Il représente uniquement ce que Ghidra a inféré à partir des bytes visibles au point d'entrée, potentiellement une couche de protection. Les octets originaux de l'entrée commencent notamment par `push rbp`, `movabs rbp, 0xa52faaa7fbae4fbc`, `pushfq`, puis plus loin `syscall` et `retf` (`kama_pe.json:entry_disassembly`). Ce sont des **instructions décodées sur ces octets**, pas une preuve d'exécution.

Ghidra a également produit une fonction `thunk_FUN_18078d005` à **RVA
`0x78D000`** (pseudocode de 55 lignes, SHA-256
`814b0c0cb8732eeab34d54ef5e6853b1a27cff7d6858930f9f9ec4cb903881cf`).
Les 20 premières lignes enregistrées comportent de multiples avertissements
`Removing unreachable block`; aucune signature métier ne ressort de cet
extrait. Cette RVA est dans la section entropique `.7x[`.

Les trois fonctions Ghidra trouvées dans `.text` à **RVA `0x1030`,
`0x1090`, `0x1100`** ont chacune une taille analysée de **1 adresse**.
Leur décompilation se réduit à une alerte puis à ce corps synthétique
(exemple réel à `0x1030`) :

```c
/* WARNING: Control flow encountered bad instruction data */
void FUN_180001030(void)
{
  /* WARNING: Bad instruction - Truncating control flow here */
  halt_baddata();
}
```

Le journal signale pour ces RVA :
`Disassembly not permitted within uninitialized memory block`.
C'est cohérent avec les sections code de taille brute **zéro** dans le PE.
En conséquence, ces `FUN_*` ne sont **pas** des versions récupérées des
fonctions C++ originales. Le mot `DECOMP_OK` signifie seulement que Ghidra
a rendu un texte, même lorsque celui-ci contient `halt_baddata`.

**Diagnostic binaire précis :** le fichier porte des signes forts de
protection/emballage du code original (sections standards non présentes
en octets bruts, gros bloc entropique, point d'entrée peu intelligible,
nombreuses fonctions illisibles dans la mémoire non initialisée).
La **technique de protection précise reste non identifiée**. On ne peut
ni reconstruire le `HookConnect` original, ni démontrer le flux du
handshake, ni attester la présence d'un mécanisme précis de falsification
d'empreinte depuis ces seuls octets. Aucune déprotection/dévirtualisation
ou analyse mémoire de processus n'a été tentée.

## Méthode exacte, commandes et sorties

Répertoire de travail :
`%USERPROFILE%\Videos\GIVRE\GivreShareWork`. Le script Python lit
uniquement les bytes détachés, vérifie SHA et taille **avant** toute
interprétation, puis extrait PE/sections/imports/entry dans un JSON nouveau.

```powershell
Get-FileHash -LiteralPath '.\offline-evidence\v131-app\verified-binaries\GivreKama.dll' -Algorithm SHA256
py -3 -B '.\artifacts\MZP25Kama\static_pe.py'
py -3 -B '.\artifacts\MZP25Kama\verify_static.py'
py -3 -B '.\artifacts\MZP25Kama\verify_ghidra.py'
```

Ghidra installé :
`%USERPROFILE%\AppData\Local\Programs\REA-Tools\ghidra_12.1.4_PUBLIC`.
Ghidra headless a été **réellement exécuté** en x64 sur le seul fichier
original **détaché**, avec projet MZP25 dédié :

```powershell
$art = (Resolve-Path '.\artifacts\MZP25Kama').Path
$exe = (Resolve-Path '.\offline-evidence\v131-app\verified-binaries\GivreKama.dll').Path
$env:JAVA_HOME = '%USERPROFILE%\AppData\Local\Programs\REA-Tools\jdk-25.0.4.1+1'
$env:PATH = $env:JAVA_HOME + '\bin;' + $env:PATH
$headless = '%USERPROFILE%\AppData\Local\Programs\REA-Tools\ghidra_12.1.4_PUBLIC\support\analyzeHeadless.bat'
& $headless $art MZP25KamaProject -import $exe -scriptPath $art -postScript MZP25KamaHeadless.java (Join-Path $art 'kama_ghidra_functions.tsv') -analysisTimeoutPerFile 120 -deleteProject
```

La sortie `ghidra.log` confirme **`Analysis succeeded for file`**
et **`MZP25_KAMA_COMPLETE total=1462 candidate=5`**.
Le script Java écrit les RVA, noms attribués par Ghidra, tailles, véritables
extraits bornés de pseudocode et leur empreinte SHA-256 ; il **masque les
littéraux** sensibles. Le rapport `kama_ghidra_functions.tsv` se termine
par `MZP25_KAMA_COMPLETE`; les 5 sorties sont vérifiées par
`verify_ghidra.py`. Le log mentionne des problèmes d'instructions
dans les sections non initialisées, mais **pas** de `REPORT SCRIPT ERROR`.
Il n'existe aucune observation dynamique dans ce lot.

## Comparaison strictement limitée aux sources du clone

Fichiers consultés **en lecture seule** :
`native/GivreKama/GivreKama.cpp:1-270` et
`native/GivreKama/README.md:1-22`. Le code source clone porte SHA-256
`0F88D6B31252200DD1C541BB47AA2370A8930F78D623CDD22CA07EB041CEBB8F`.
Son DLL compilé `native/GivreKama/GivreKama.dll` mesure **180 736 octets**
et a SHA-256
`C53189FCCFE0964BD5A74AA2CF04153527F455A2AE5F2092A49F3BF1CD009A5D`.
Cette DLL clone est **différente** de l'original (12 551 168 octets,
SHA distinct). Les symboles définis par le code source clone ne doivent
pas être attribués au binaire original sans preuves indépendantes.

| Surface du clone (source uniquement) | Ancre de code | Élément original vérifié | Tests proposés, uniquement sur fixtures locales |
| --- | --- | --- | --- |
| Détournement conditionnel de `connect` et relais | `GivreKama.cpp:46-91` | `WS2_32!connect` importé, mais fonction de détournement originale illisible | Comparer décision de redirection sur familles/adresses/ports synthétiques, et passage inchangé pour connexions hors périmètre |
| Handshake packé | `GivreKama.cpp:49-57,69-86` : 16 + 4 + 2 + 4 = **26 octets** | Aucune séquence de construction du message établie dans l'original | Encodage/décodage strict en mémoire ; tailles exactes, endianness, champs limites et rejets de trames tronquées |
| Adaptateurs/MAC optionnels | `GivreKama.cpp:162-204` | `IPHLPAPI!GetAdaptersAddresses` importé, logique originale inaccessible | Fixture d'adaptateurs 6 octets/longueurs autres, gestion d'appel de dimensionnement, déterminisme du résultat |
| Graine et hachage | `GivreKama.cpp:136-146,206-228` | `bcrypt!BCryptHash` importé, appel métier non prouvé | SHA-256 de graines fictives, absence de graine et échecs simulés, aucune donnée de compte |
| Transactions indépendantes | `GivreKama.cpp:230-267` | Aucun corps `DetourAttach`/transaction prouvé dans le fichier original | Test de fonctions abstraites avec doubles de test : échec de la branche facultative ne modifie pas le résultat primaire |
| Journal optionnel | `GivreKama.cpp:100-133,206-210` | Aucun flux de logs original attesté | Tests de flag désactivé par défaut, formats neutres, taille maximale et non-persistance des secrets |

Les propositions de tests portent sur le **contrat autonome du clone**,
et ne prétendent pas certifier sa parité exacte avec les octets protégés.
Elles n'exigent pas d'exécuter `Dofus.exe`, `GivreClone.exe`, une DLL
injectée, un proxy ou une session réseau.

## C — modifications et artefacts

**Un seul document nouveau**, ce fichier, et fichiers **exclusivement**
sous `artifacts/MZP25Kama/` :

- `static_pe.py`, `kama_pe.json` : identification PE, 12 sections, imports, entrée et désassemblage Capstone borné.
- `MZP25KamaHeadless.java`, `kama_ghidra_functions.tsv`, `ghidra.log`, `ghidra.stderr.txt` : autoanalyse native et extraits décompilés réels.
- `verify_static.py`, `verification.log`, `kama_source_comparison.json` : assertions sur les octets d'origine et distinction binaire/source clone.
- `verify_ghidra.py`, `ghidra_verification.log` : attestation que le post-script Ghidra a achevé 5 extraits, leurs adresses et avertissements.
- Éventuels fichiers de projet temporaire Ghidra `MZP25KamaProject*` sont des sorties du seul analyseur MZP25, sans lien avec les projets voisins.

## V — validation exécutée

`verify_static.py` : **PASS**, conclusion
`MZP25_STATIC_VERIFIED`. Empreintes/longueurs, en-têtes, RVA d'entrée,
12 offsets et tailles de sections, imports, octets exacts de chaque instruction
retenue, distinction original/clone : revérifiés sur le fichier original.
`verify_ghidra.py` : **PASS**,
`MZP25_GHIDRA_VALIDATED five genuine decompiler outputs; business functions unproven`.
Il vérifie l'exécution Java et les sections du log, le marqueur final, les cinq
`DECOMP_OK` aux bonnes RVA, `syscall()` à l'entrée et les
`halt_baddata()` aux trois RVA non initialisées. **Ce sont des tests
statiques**, pas des tests de hooks ou gameplay.

## B — limites finales et prochaines obligations

Le PE original est utilisable pour **empreinte exacte, étude des imports,
sections, entrée et fragments natifs du code visible**. Le désassemblage
de plusieurs zones `.text` est impossible par manque de bytes dans l'image
brute. La décompilation réussie des fragments ne prouve ni un véritable
`HookConnect`, ni le fonctionnement d'un proxy, ni un handshake accepté,
ni un appel `GetAdaptersAddresses` détourné, ni une MAC modifiée.
La couche de protection éventuelle et l'identité de son produit ne sont
pas certifiées. Il faudrait un artefact d'origine non empaqueté ou des
symboles indépendants pour confronter des fonctions métier exactes.

**Statut de transfert :** analyse Ghidra réelle **réussie mais non
exploitable pour reconstruire les fonctions métier protégées** ; cinq
extraits bornés et vérifiables, comparaisons de fonctionnalités limitées aux
sources du clone. Aucun accès au Givre original en fonctionnement.
