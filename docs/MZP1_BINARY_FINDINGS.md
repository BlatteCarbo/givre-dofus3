# MZP1 — inventaire des binaires hors ligne et blocage EXE v1.3.1

**16/10/2026 — Audit statique du seul arbre autorisé `/givre`.** Aucun GivreClone actif, processus, dump `givre-live-mini.dmp`, profil, configuration, secret, capture ou répertoire d'installation n'a été ouvert ni copié. Aucun exécutable cible n'a été lancé.

## Résultat pour l'EXE GivreClone v1.3.1

**`GivreClone.exe` détaché introuvable dans les emplacements indexés et les recherches directes effectuées.** La mention antérieure de la fenêtre **GivreClone v1.3.1** est une observation de titre UI, sans hash de binaire. Elle ne peut pas être rapprochée des sorties locales. Le code `GivreShareWork/GivreClone.App/Services/LicenseService.cs:52` déclare par ailleurs `AppVersion = "1.2.0"` pour `client.version`, et n'atteste pas que le programme ouvert correspond à ces sources.

Le blocage est **réel et limité par la couverture annoncée**, plutôt qu'une absence universelle supposée :

| Périmètre offline | Dossiers | Entrées | EXE/DLL indexés | `GivreClone.exe` | Limites |
| --- | ---: | ---: | ---: | --- | --- |
| `/givre/GivreShareWork` | 257 | 1 805 | 957 | **0** | Couverture complète du sous-arbre autorisé dans les limites, exclusions explicites |
| `/givre/GivreAI` | 8 | 45 | 0 | **0** | Couverture ciblée complète |
| `/givre/GivreKnowledgeCrawler` | 17 | 10 564 | 0 | **0** | Couverture ciblée complète ; environnements Python exclus |
| `/givre/GivreSacrieurAutopilot` | 9 | 100 | 0 | **0** | Couverture ciblée complète |
| `/givre` — index général | 12 336 | **250 001** | **3 000** | **0 découvert avant limite** | **Partiel : limite 250k entrées et 3k fichiers, non exhaustif** |

Les valeurs locales reflètent un arbre de travail partagé pouvant changer pendant l'exécution d'autres workers. Fichier machine-readable local faisant foi pour la dernière passe GivreShareWork : `tools/MZP1_BinaryInventoryGivreShareWorkV2.json`. Pour les autres sous-arbres, utiliser les inventaires `tools/MZP1_BinaryInventoryGivreAI.json`, `tools/MZP1_BinaryInventoryGivreKnowledgeCrawler.json`, `tools/MZP1_BinaryInventoryGivreSacrieurAutopilot.json`. Deux inventaires généraux bornés (initial/prioritaire) sont conservés et portent explicitement `coverage_complete_within_limits=false`.

En plus de l'index borné, une recherche directe du **nom exact** via `rg --files -uuu -g 'GivreClone.exe'`, sous `/givre` avec exclusions `.git/obj/captures/assets`, n'a rendu aucun résultat. Une recherche plus large `rg --files -uuu -g '*GivreClone*'` a retrouvé les noms `.slnx`, `.csproj` et icônes dans GivreShareWork, sans EXE. Ces recherches n'inspectent que les noms des fichiers ; elles ne lisent pas les processus ni les installations. Elles ne couvrent pas les contenus compressés d'archives ZIP ou les dossiers exclus.

## Binaires source-only réellement vérifiés

Les fichiers ci-dessous sont des **sorties compilées locales**, de provenance distincte de l'EXE v1.3.1. Les empreintes SHA-256 sont calculées sur les octets de ces fichiers seulement. `clr_metadata_signature_valid` exige le `BSJB` du header CLR, et `clr_il_only` confirme l'indicateur IL-only. Ces indicateurs ne prouvent ni un comportement fonctionnel ni une association à la fenêtre GivreClone en cours.

| Fichier relatif à `GivreShareWork` | Taille | PE / CLR observé | SHA-256 |
| --- | ---: | --- | --- |
| `GivreRelayAgent/bin/Debug/net10.0/GivreRelay.dll` | 50 688 | PE x86, CLR + BSJB + ILONLY | `34d78a62bf781f8a21017152b0f8e7ee98a5f870910b57b4a150796185ee5d70` |
| `GivreRelayAgent/bin/Release/net10.0/GivreRelay.dll` | 46 592 | PE x86, CLR + BSJB + ILONLY | `0224c4c6580e20796c8b5e85dcb71c337d8db9170410ea5a3af4a6535156917c` |
| `GivreRelayAgent/bin/Release/net10.0/GivreRelay.exe` | 162 304 | PE x64, sans répertoire CLR détecté (apphost possible) | `041ed0e369acac230c8d6340923894bd8f16d8c20a0c22e4aac70387ca681847` |
| `tools/GivreTurboLiveHarness/bin/Release/net10.0/GivreKama.dll` | 11 868 672 | PE x64, 12 sections, aucun CLR détecté | `e827c635316edfe8736fd6933bb546a82947abbfbd4d6d9a6448392ba666d375` |
| `tools/GivreTurboLiveHarness/bin/Release/net10.0/GivreTurboLiveHarness.dll` | 57 344 | PE x86, CLR + BSJB + ILONLY | `a18d4c358a6b3fb7e4ad36f1813491b4f410eb162ebed6d529d0d9f27e1de4dd` |

Les sorties Debug/Release du relais et l'EXE du harness possèdent aussi des entrées SHA/PE dans l'inventaire JSON complet. La mention **PE x86** sur les DLL `net10.0` reflète le champ COFF du fichier, sans preuve qu'il est limité à un processus 32 bits. L'absence de CLR dans un EXE peut caractériser un apphost .NET, un binaire natif ou un AOT : **aucun IL ne doit être inventé à partir de ce seul indice**.

## Outils MZP1 réellement livrés

**`tools/MZP1_BinaryIndex.py`** parcourt des dossiers à profondeur et volume bornés. Il exclut noms sensibles, dumps, captures, configurations, profils, `.git`, répertoires `obj`, environnements virtuels, assets et liens/jonctions. Il conserve les chemins relatifs + tailles des EXE/DLL, et calcule SHA-256 + métadonnées PE/CLR uniquement des binaires Givre nommés. Les dépassements portent `coverage_complete_within_limits=false`. La sortie JSON ne peut être créée que sous `tools/MZP1_Binary*.json` et refuse l'écrasement.

**`tools/MZP1_BinaryVerify.py`** accepte un `GivreClone.exe` explicitement fourni et détaché sous `/givre`. Il appelle le prévol **existant** `MZP0_OfflinePeAudit.inspect_pe`. Si et seulement si la classification est `managed_il_eligible`, il appelle le pipeline **existant** `MZP0_OfflineDecompile.reconstruct` et écrit une sortie isolée à nom basé sur le SHA du binaire. Le résultat de reconstruction contient les C# + projet et manifeste SHA lorsque l'IL est réellement récupérable. Pour un EXE natif/apphost/AOT, le résultat est `unsupported_native_or_aot`, sans récupération fictive. Sans EXE explicite, `blocked_no_detached_exe`.

**`tools/MZP1_BinaryTests.py`** valide de manière 100 % hors ligne un PE fictif nommé `GivreClone.exe`, l'absence d'EXE, les métadonnées d'une DLL, exclusions, parcours borné, liens symboliques, mauvais noms et la sélection du chemin IL vs natif. Aucun téléchargement ou lancement de la cible.

## Commandes de reproduction (PowerShell)

Depuis `/givre/GivreShareWork` :

```powershell
py -3 -B tools/MZP1_BinaryTests.py
py -3 -B tools/MZP1_BinaryIndex.py --focus GivreShareWork
py -3 -B tools/MZP1_BinaryVerify.py
```

Si une vraie copie autorisée et détachée est ensuite disponible sous, par exemple, `/givre/GivreShareWork/offline-input/GivreClone.exe` :

```powershell
py -3 -B tools/MZP1_BinaryVerify.py --input GivreShareWork/offline-input/GivreClone.exe
```

L'outil récupérera le projet avec `ilspycmd` seulement si CLR IL-only est **effectivement** identifié par les octets de l'EXE. Le nom `v1.3.1` doit rester **non attesté** tant que la version réelle de cette copie et la correspondance binaire/source ne sont pas établies indépendamment.

## Blocage transmis à PRIME

**Aucun `GivreClone.exe` détaché exploitable n'a été localisé dans le sous-arbre GivreShareWork complet ni par les recherches ciblées globales**. L'inventaire global borné reste non exhaustif ; si PRIME connaît un chemin de copie approuvé plus précis, il suffit de le passer à `MZP1_BinaryVerify.py --input`, sans toucher à l'installation en cours. La reconstruction du vrai v1.3.1 demeure **bloquée par l'absence d'un fichier hashé identifié**, et les binaires GivreRelay/GivreKama retrouvés n'y suppléent pas.
