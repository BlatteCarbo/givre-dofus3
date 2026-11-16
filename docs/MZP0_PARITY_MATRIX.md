# MZP0 — matrice de parité sourcée, GivreClone ↔ GivreRework

**Audit du 16/10/2026, strictement hors ligne.** Source : `tools/MZP0_SourceParityInventory.json`, générée par `tools/MZP0_SourceParity.py`. Les constats portent exclusivement sur les fichiers source locaux autorisés. Aucun processus, compte, configuration, dump, session de jeu ou EXE installé n'a été ouvert, modifié ou copié.

## Provenance du binaire de référence

| Référence | Identité / version | Hash SHA-256 | Preuve disponible | Conséquence |
| --- | --- | --- | --- | --- |
| **GivreClone en cours** | Titre de fenêtre rapporté : **v1.3.1**, observation passive PRIME | **Inconnu** | `running_window_only`, sans accès au chemin installé ni aux octets | Aucune caractéristique de cet EXE n'est certifiée par le présent audit |
| **GivreClone.exe de build offline** | `GivreClone.App/GivreClone.App.csproj:10` définit `<AssemblyName>GivreClone</AssemblyName>` | **Aucun EXE trouvé sous `GivreClone.App/bin/`** | `no_offline_exe` | Aucun lien binaire ↔ source n'est prouvé |
| **Version déclarée dans les sources** | `GivreClone.App/Services/LicenseService.cs:52` : `AppVersion = "1.2.0"` transmis comme `client.version` à l'API de licence | SHA du fichier source dans le manifeste | `source_only` | Le libellé **1.2.0 diffère du titre de fenêtre 1.3.1** ; ils ne prouvent pas le même binaire |
| **Source locale GivreShareWork** | Projet Avalonia/.NET 10, snapshot de travail modifié | Hash **par fichier source** dans le JSON | `source_only` | Preuve de présence d'une déclaration, pas équivalence comportementale avec v1.3.1 |
| **Source GivreRework** | P0 Dofus 3.7.10.10, Avalonia/.NET 10 | Hash **par fichier source** dans le JSON | `source_only` | Les portes gameplay et ACK restent non validées |

Si une **copie détachée et autorisée** de `GivreClone.exe` est placée ultérieurement dans un répertoire de build offline contrôlé, relancer le scanner : son format PE et son SHA seront enregistrés comme **identité de fichier**, sans déclarer les fonctionnalités présentes dans l'EXE. La version **v1.3.1** observée dans une fenêtre ne garantit ni la version ni le hash d'un binaire copié. Une analyse fonctionnelle du binaire exigerait ensuite des preuves propres à chaque fonctionnalité.

Le `README.md:68-74` de la copie GivreShareWork déclare explicitement des éléments exclus. L'audit les confirme sur disque : **projet frère `../GivreProxy/GivreCore/GivreCore.csproj` absent, dossiers `data/` et `server-mapping/` absents**. `GivreClone.slnx:7` référence ce projet manquant ; le pipeline MITM réel et les mappings nécessaires à un binaire pleinement opérant ne sont donc pas démontrés dans cette copie. Le fichier `GivreClone.Protocol/GameCodec.cs:8-19` annonce expressément une branche type-safe figée à 3.6.10.11 : ne pas l'utiliser comme preuve de compatibilité 3.7.10.10.

## Parité vérifiable par famille

Légende : `S` = déclaration source localisée et hashée ; `—` = aucune **ancre sélectionnée** dans ce périmètre. Chaque rang est `exe_confirmed=false`. Un `S/S` indique des composants aux finalités proches, **pas** la parité du fonctionnement. Le manifeste JSON associe à chaque preuve le **chemin, la ligne exacte, le symbole et le SHA-256**, et inventorie séparément les modules applicatifs.

| Famille | Givre | Rework | Écart fonctionnel à résoudre |
| --- | :---: | :---: | --- |
| Catalogue des messages | S | S | Vérifier les mappings et tags réels pour 3.7.10.10 |
| Forge des requêtes | S | S | Rework expose une intention Lua, l'envoi gameplay n'est pas confirmé |
| Acquisition entrante | S | S | Givre dépend de GivreCore ; Rework lit un corpus privé offline |
| Cadrage incrémental | — | S | Framing Givre dans dépendance absente ; Rework a un framer borné source |
| Corrélation UID | S | S | L'UID Rework n'est pas un `serverAck` |
| Bus par session | S | — | Reconstituer abonnement/attente/timeout dans le chemin Rework attesté |
| État de jeu | S | S | Givre `GameState.Apply` ; Rework réducteur d'observations encore hors transport |
| Envoi d'actions | S | — | `ActionSender` de Givre existe, mais dépend d'un sink MITM externe |
| Combat | S | S | `LaunchService.Combat` vs simulateur offline ; aucun combat Rework certifié |
| Navigation | S | S | Comparer cheminement et déplacements réellement acceptés |
| Banque/échanges | S | S | Rework prépare des échanges offline, sans banque live prouvée |
| HDV/économie | S | S | Cotations Rework ≠ achat/vente HDV en jeu |
| Artisanat | S | S | Planification offline Rework ≠ fabrication authentifiée |
| Runtime Lua | S | S | `GivreLuaHost` vs moteur Lua borné offline |
| Enregistrement de routes | S | S | Origine des observations Rework à authentifier |
| Multi-comptes | S | — | `AccountManager` existe côté Givre ; aucun équivalent live certifié |
| Orchestration/H24 | S | S | `BotOrchestrator` vs `OfflineSupervisor` : robustesse H24 non testée |
| Mod natif/turbo | S | — | `NativeTurboRuntime` côté Givre ; preuve native Rework absente |
| UI Avalonia | S | S | Comparaison des parcours, bindings et actions encore nécessaire |
| Différentiel privé | — | S | Aucun des deux vrais décodeurs ne partage encore une capture authentifiée |
| Console P0 | — | S | Interface candidate opérationnelle offline ; source session live manquante |

**Comptage calculé : 21 familles**, dont **14 `both_source`**, **4 `givre_source_only`** et **3 `rework_source_only`**. Le scanner produit aussi **120 entrées Givre d'inventaire de modules** : 33 `LaunchService`, 24 `GivreLuaHost`, 1 `RoadRecorder`, 21 définitions de vues `.axaml` et 41 `ViewModels`. Ces 120 entrées sont des fichiers, pas 120 fonctionnalités prouvées dans l'EXE. Une famille sans ancre Rework ne signifie pas absence de toute implémentation analogue ailleurs : c'est un **gap à vérifier**.

### Chaîne réellement lisible dans les sources

La source Givre organise un pipeline cohérent : `GivreClone.Engine/DofusCaptureHost.cs` pointe vers GivreCore, `BotSession.cs:26-53` relie un `PacketBus`, `GameState`, `ActionSender` et `BehaviorHost`; `PacketBus.cs:37-88` prévoit diffusion/attente avec timeout ; `ActionSender.cs:41-60` forme une requête puis transmet au `IFrameSink`. Le retour `SendOutcome.Sent` signifie explicitement « frame injectée » dans la source : il **ne constitue pas un accusé de réception serveur**. Une parité fidèle doit conserver cette distinction.

La source Rework dispose déjà de `P0PinnedBranchCatalog`, `P0LegacyCompatibleStreamFramer`, `P0CandidateCorrelationTracker`, `P0SameBytesDifferential` et `P0PrivateCapturePreview`, puis de `P0SessionViewModel`/`P0OperatorWindow`. `src/GivreRework.App/Services/RuntimeWireBridgeContracts.cs:21-24` interdit toujours envoi gameplay, Lua autonome et ACK fictifs. Le handoff `../GivreRework/HANDOFF.md:33-34` mentionne explicitement **G0 non validé**, le manque de source de trame vraie authentifiée et le besoin de deux décodeurs réels sur les mêmes octets. Les tests offline et un aperçu UI ne lèvent pas ces blocages.

## Trois choix d'intégration, par ordre de priorité

1. **P0 transport et réception authentifiés.** Reprendre la **structure des contrats** `DofusCaptureHost → BotSession.Dispatch → PacketBus → GameState` dans des interfaces propres à Rework, à partir des sources autorisées, avec provenance PID-naissance/session/génération/version, framing et diagnostic `unknown`. Raccorder uniquement une source appartenant à la session autorisée quand PRIME en dispose. Critère de sortie : même corpus privé bit à bit dans deux vrais décodeurs, comparaison sans divergence inexpliquée, cartes/combat observés, sans convertir un `Sent` local en ACK serveur.
2. **Parité des comportements, après G0.** Constituer une table de contrats/actions/tests des services source `LaunchService.Movement`, `Combat`, `Bank`, `Hdv`, `Craft`, `Interactives` et des bindings `GivreLuaHost.*`. Intégrer d'abord les réducteurs de session, barrières de combat, navigation, puis Lua et économie. Tester les transitions d'état, timeouts, retries non idempotents et reprise avec captures privées/fixtures expurgées. Ne pas copier les assets ni réutiliser le `GameCodec` figé.
3. **Parité produit/UX et exploitation H24.** Comparer les **21 vues** et **41 ViewModels** inventoriés à la navigation Avalonia Rework, puis identifier les commandes réellement connectées aux ports d'état et les états incertains actionnables. Reprendre les modes d'opération, historiques, journal, diagnostics et reprise durable sans s'attribuer des fonctionnalités de l'EXE tant que son propre audit binaire offline manque. Mesurer ensuite les parcours UI et une exécution H24 authentifiée pour obtenir G5-G8.

## Reproduire ou invalider cet inventaire

Exécuter depuis `GivreShareWork`, avec Python 3 standard, **sans lancer .NET, EXE, bot, client ou réseau** :

```powershell
py -3 -B tools/MZP0_SourceParity.py
py -3 -B tools/MZP0_SourceParityTests.py
py -3 -B tools/MZP0_SourceParity.py --check
py -3 -B tools/MZP0_OfflinePeAuditTests.py
```

Le scanner est déterministe : pas d'horodatages, de chemins absolus ni de morceaux de code dans le JSON. `--check` compare le fichier enregistré **octet par octet** au résultat recalculé ; tout changement de source hashée force une régénération et une revue. Il n'inspecte que des fichiers `.cs`/`.axaml` ciblés et d'éventuelles copies `GivreClone.exe` sous `GivreClone.App/bin/`; les processus actifs, chemins d'installation, comptes, données privées, dumps et binaires tiers sont exclus. Un faux PE, un fichier corrompu, des ancres absentes et un manifeste obsolète sont couverts par tests négatifs. Le scan n'est **pas** une décompilation ni une preuve d'équivalence de comportement de l'EXE.

### Tri préalable d'une copie EXE détachée

`tools/MZP0_OfflinePeAudit.py` accepte seulement un fichier **nommé `GivreClone.exe` sous l'arbre `/givre` autorisé**, sélectionné explicitement via `--input` ; la copie n'existe pas dans ce dépôt au présent audit. La commande suivante ne devient utilisable **qu'après** mise à disposition d'une copie offline indépendante :

```powershell
py -3 -B tools/MZP0_OfflinePeAudit.py --input "<copie-offline-sous-GIVRE>\GivreClone.exe"
```

Le rapport JSON stdout contient uniquement identité de fichier, taille, SHA-256, architecture, en-têtes PE, sections, indice CLR et disponibilité locale éventuelle de `ilspycmd`. `managed_il_eligible` exige un header CLI cohérent, une signature de métadonnées `BSJB` et le drapeau IL-only ; sinon `native_or_aot_requires_static_recovery` reste un **choix de méthode**, jamais une preuve que le binaire est NativeAOT/VMProtect. Aucun fichier source n'est reconstruit par ce prévol. Le script refuse chemins hors racine, liens symboliques, noms inattendus, PE tronqués, sections invalides, RVA CLR sans backing et dépassements. Il n'exécute aucun programme et n'installe aucun outil. Si `ilspycmd` est déjà disponible et la copie admissible, une analyse IL ultérieure pourra être organisée localement dans un répertoire de travail nouveau et isolé ; en son absence ou si le binaire est natif, poursuivre l'analyse statique hors processus à partir du spécimen détaché.
