# MZP7 — preuves bornées des fournisseurs GivreCore / GivreLauncher.Core

**16 octobre 2026 — SOURCE/BINARY ONLY.** Scanner et tests propres à
`tools/MZP7ProviderEvidence/`, sans installation, chargement de DLL,
désassemblage, lancement d'exécutable, connexion, socket, processus ou accès
aux comptes. Ne réexécute **pas** les 170 ZIP ni les 8 808 dossiers déjà
recensés par MZP1. Les sources, binaires et archives inspectés sont
explicitement sélectionnés dans des emplacements détachés sous `/givre`.

## Résultat utile pour PRIME

Le diagnostic comporte désormais un **fait nouveau constaté pendant cette
passe** : un projet `../GivreProxy/GivreCore/GivreCore.csproj` est apparu le
16/10/2026 à 07:52 (heure locale), **après** l'inventaire MZP1/MZP2.
Son contenu a changé au cours de ce contrôle : le SHA du premier état vu
était `74f394a9433cbae98ff5b1a4bfdcbc976f18da436e75dcdbb20442562d62c198`;
celui du snapshot livré est
`bc2ede98aceda8422163b3cb2a8356d1a3180733fc9d0d76d72e88c455c62053`.
Ce projet n'a **pas été créé ni modifié par ce lot**. Il est présent sous
un nom de chemin conforme à la référence MSBuild et déclare
`<AssemblyName>GivreCore</AssemblyName>`, mais la provenance externe des
sources reste **non établie** ; un chemin et un SHA sont seulement des faits
locaux.

Au moment du snapshot, le projet contient **cinq fichiers `.cs`**,
dont les déclarations `GivreCore.Protobuf.ProtoField`, `WireType`,
`RawProtobuf`, `GivreCore.Mitm.TcpProxyServer`, `Direction` et
`GivreCore.Injection.DllInjector`. Il manque encore le type sollicité côté
capture **`CapturedMessage`**. Son absence est détectée par le scanner.
Les signatures et comportements des autres noms de types ne sont pas
attestés par leur seule déclaration.
`../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj`
est toujours introuvable. Le nombre d'erreurs `CS0246` potentiellement
réductibles n'est **pas chiffrable sans compilation ciblée** du consommateur
contre les API réellement complètes ; **aucun build App n'a été fait**.

| Vérification | Observation du snapshot | Portée de la preuve |
| --- | --- | --- |
| `GivreClone.Protocol` → `../../GivreProxy/GivreCore/GivreCore.csproj` | Chemin désormais résolu | Lien de projet seulement |
| `GivreClone.Engine` → même `GivreCore` | Chemin désormais résolu | Lien de projet seulement |
| `GivreClone.App` → `../../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj` | Introuvable | Dépendance bloquante |
| GivreCore Protobuf (`ProtoField`, `WireType`, `RawProtobuf`) | Déclarations C# visibles, SHA par fichier | Surface partielle, ABI non vérifiée |
| GivreCore MITM (`TcpProxyServer`, `Direction`) et `Injection.DllInjector` | Déclarations C# visibles | Signatures, ABI et comportement non certifiés |
| GivreCore `CapturedMessage` | Déclaration attendue absente | `CS0246` encore possible |
| GivreLauncher (`HandshakeRelay`, `GameLauncher`, `ProxySettings`, `ZaapEmulator`) | Projet et types introuvables | `CS0246` encore possible |
| `../GivreAI/AfterArgentee_Kit_20261013.zip` | 10 entrées inspectées, aucun projet fournisseur | Noms des membres ZIP uniquement |
| `artifacts/MZP6-Source-Modules.zip` | 39 entrées inspectées, aucun projet fournisseur | Noms des membres ZIP uniquement |
| `../GivreKnowledgeCrawler`, `../GivreSacrieurAutopilot` | Aucun candidat en premier niveau | **Non exhaustif** dans ces dossiers |

Le scanner lie les observations au SHA-256 des trois `.csproj` consommateurs
et aux lignes de code consommatrices indexées par `(fichier, ligne, SHA)`.
`CharacterInfoReader`, `FightPrepReader` et `EnvelopeBuilder` requièrent
`GivreCore.Protobuf`; `BotHost`/`DofusCaptureHost` requièrent la capture MITM ;
`LaunchService` et `MitmTransport` requièrent l'API Launcher/Transport.
Les commentaires, chaînes C# et substituts de harness ne deviennent jamais
des définitions de fournisseur.

## Exécution et limites de recherche

Depuis `/givre/GivreShareWork` :

```powershell
py -3 tools/MZP7ProviderEvidence/scanner.py
py -3 -m unittest discover -s tools/MZP7ProviderEvidence -p test_scanner.py -v
```

Le premier appel écrit un JSON sur la sortie standard. Pour un snapshot
supplémentaire, utiliser un nom **inédit** :

```powershell
py -3 tools/MZP7ProviderEvidence/scanner.py `
  --output tools/MZP7ProviderEvidence/verification-apres-restauration.json
```

La pièce livrée `tools/MZP7ProviderEvidence/MZP7EvidenceSnapshot.json`
contient la photo réellement obtenue, dont les empreintes des sources
et les références de projet. La recherche nominale se limite aux
deux `ProjectReference` attendus, trois dossiers détachés et deux ZIP
ciblés. Des ajouts concurrents exigent une nouvelle exécution : **ne pas
traiter la photo comme une preuve de fraîcheur permanente**.

Une recherche additionnelle ciblée peut être demandée explicitement :

```powershell
py -3 tools/MZP7ProviderEvidence/scanner.py `
  --candidate '..\GivreProxy\GivreCore\GivreCore.csproj' `
  --candidate '..\GivreLauncher\GivreLauncher.Core\GivreLauncher.Core.csproj' `
  --candidate 'DossierDetache\ArchiveAutorisee.zip'
```

**Bornes strictes :** racine autorisée, rejet des liens symboliques détectés,
un seul niveau dans les dossiers candidats, maximum 150 éléments par dossier,
350 `.cs` par projet et profondeur 4, 1 MiB par source, 24 MiB par DLL,
64 MiB/4096 entrées par ZIP, 1 MiB par membre et ratio de compression
au plus 100:1. Aucun ZIP n'est extrait sur disque. `.exe`, `.dmp`, fichiers
de comptes et secrets sont exclus ; aucun chemin hors racine autorisée
n'est accepté.

Une DLL `.NET` explicitement choisie est **lue sans être chargée** via ses
métadonnées `dnfile` si disponible : SHA, `AssemblyName`, `TypeDef` et `TypeRef`
des namespaces fournisseurs. `TypeRef` signifie **référence à un type**, pas
définition de ce type. L'étiquette `BINARY_MANAGED_METADATA` n'atteste ni
le corps des méthodes, ni la compatibilité de signature, ni la provenance
du binaire. Un binaire non managé ou invalide reste
`BINARY_BYTES_ONLY`. Une archive avec `.csproj` et `.cs` donne seulement
une preuve **de déclarations archivées**. Aucun de ces niveaux ne certifie
la parité avec GivreClone v1.3.1.

## Contrôles adversariaux et prochaines étapes

Validation du scanner le 16/10 : **13/13 tests PASS**. Sont notamment
testés les faux namespaces dans les commentaires/chaînes C#, les noms
d'assembly contradictoires, les faux fichiers DLL contenant des chaînes
`GivreCore`, la traversée `../` dans les ZIP, les taux de compression anormaux,
les liens symboliques et les limites de répertoires, la preuve Protobuf
partielle et le refus de transformer une source manquante en fournisseur
reconstruit. Le test est isolé dans des fichiers temporaires et n'ouvre
aucun client.

**Actions pour réduire concrètement CS0246 :** faire stabiliser la nouvelle
arborescence `GivreProxy/GivreCore` par son propriétaire puis vérifier les
signatures réelles `ProtoField`, `RawProtobuf`, `WireType` auprès des vrais
appels `EnvelopeBuilder` / `GameCodec` / `CharacterInfoReader`. Ajouter, si
leurs sources autorisées sont retrouvées, les composants `GivreCore.Mitm`
et `GivreCore.Injection`, ainsi que le projet `GivreLauncher.Core` et ses
dépendances transitives. Rejouer le scanner, vérifier les SHA des sources,
évaluer les vrais `ProjectReference`, puis compiler **hors ligne, en ciblant
les projets source** sous contrôle de PRIME. Tant que cela n'est pas fait,
`cs0246Resolved=false`, `actualRuntimeParity=false` et
`authenticatedSender=false` restent forcés dans le JSON.

Ne jamais confondre une implémentation indépendante `MZP3/MZP4/MZP5` ou
un stub de tests avec la restauration de GivreCore. Ce lot ne modifie
aucun consommateur, `.csproj`, binaire ni application.
