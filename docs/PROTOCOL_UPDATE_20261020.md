# Détection des mises à jour Dofus et revue du protocole — 20/10/2026

## Ce que le module mesure

`GivreClone.App/Services/ProtocolUpdates/ProtocolUpdateProbe.cs` inspecte **hors jeu, en lecture seule** les quatre fichiers sous la racine de l'exécutable sélectionné par l'utilisateur :

| Source installée | Résultat |
|---|---|
| `Dofus.exe` | SHA-256 complet du fichier |
| `GameAssembly.dll` | SHA-256 complet du fichier |
| `Dofus_Data/il2cpp_data/Metadata/global-metadata.dat` | SHA-256 complet du fichier |
| `Dofus_Data/StreamingAssets/version` | SHA-256 des octets et extraction de `Version=x.x.x.x` |

Le marqueur officiel contient aussi `BuildDate=` et `ConfigUrl=` sur les lignes suivantes : elles sont tolérées sans interprétation ni journalisation. Une deuxième déclaration `Version=` invalide l'identité. Le marqueur est borné à 4 Kio, les fichiers doivent être de vrais fichiers non redirigés, les lectures de binaires sont séquentielles avec partage lecture seule, et une annulation explicite est possible. Il n'y a aucun client ou processus ouvert par la sonde, aucune connexion, aucun compte, et aucune installation/mise à jour.

Le scanner compare la version installée avec la chaîne `build` de `data/bundle-build.json` (à fournir au scanner depuis le lecteur de l'application existant) et, si disponible, avec un `ProtocolTrustedBaseline` fourni par un composant distinct. La baseline contient les **quatre empreintes** et un `EvidenceId` non vide ; `IsTrusted` doit être vrai seulement lorsqu'une chaîne de publication/recette l'a effectivement approuvée. Ce module ne transforme jamais automatiquement une lecture du client local en baseline de confiance.

### États et déduction autorisée

| `Status` | Critère | Réponse |
|---|---|---|
| `VERSION_CHANGED` | `Version=` différente du bundle connu ou du build d'une baseline fiable | `NeedsReview=true`, revue recommandée |
| `HASH_CHANGED` | Version identique, au moins un des quatre SHA différent de la baseline fiable | `NeedsReview=true`, revue recommandée |
| `UNKNOWN` | Fichier absent/illisible, marqueur invalide, bundle sans version ou baseline fiable absente | `NeedsReview=true`, diagnostic explicite |
| `UNCHANGED` | Version égale au bundle et à la baseline fiable, les quatre SHA identiques | Dérive locale non détectée ; aucun lancement auto |

`CanRun` signifie uniquement que **l'identité locale est suffisamment complète pour construire un plan de revue indexeur**, jamais « on peut démarrer le bot ». Si le bundle et le marqueur concordent sans baseline SHA, le résultat demeure `UNKNOWN` et `CanRun=true` pour permettre à l'utilisateur de vérifier le protocole. Lorsque des fichiers manquent, `CanRun=false`. Les propriétés `ProtoChangedProven`, `GameplayCertified` et `CanAutoUpgrade` restent toujours `false`. Aucun hash de binaire ne prouve à lui seul des changements de codes de messages, d'ACK ou de sérialisation wire.

## Lecture optionnelle des snapshots Proto Studio

`ProtocolSchemaEvidenceProbe.Inspect(indexerRoot, referenceVersion, targetVersion, installedHashes)` lit :

```
DofusReverseIndexer/local-output/historical-extractions-v3/<version>/
  manifest.json
  dofus-<version>.proto
```

Pour chacun des deux candidats, le lecteur exige `format=dri-native-il2cpp-candidate-v3`, version exacte, `review_only=true`, `certified=false`, `protocol_promotion=false`, tailles et compteurs cohérents, empreintes sources au bon format, et SHA-256 réel du `.proto` égal au manifeste. Les fichiers sont bornés et les points de redirection refusés. `SchemaUnchangedProven=true` signifie **identité SHA-256 entre ces deux fichiers candidats locaux vérifiés**, uniquement. `CurrentClientBound=true` est ajouté si les trois SHA de binaires enregistrés dans le manifeste cible correspondent aux trois binaires installés lus par le scanner ; l'absence de lien est signalée séparément. Les divergences donnent `SchemaChangedCandidate=true` sans affirmer une modification du protocole wire. Ni identité de `.proto`, ni changement de SHA ne constituent une certification des identifiants réseau, des round-trips ou des ACK serveur.

Dans le dossier de l'indexeur consulté le 20/10/2026, les manifestes `3.7.10.10` et `3.7.11.11` présentent tous deux `proto_sha256=bf3799e516ee52d968432db5651fa662a23308d94ac8314862189e59dbd41651`, `2260` messages et `6493` champs. **Ce sont des candidats d'extraction**, explicitement `certified=false`. Le scanner revérifie les octets avant d'afficher une équivalence, et ne réutilise aucune conclusion basée seulement sur le texte du manifeste.

Sur `ProtocolUpdateReport`, l'appel `report.WithLocalSchemaEvidence(indexerRoot, "3.7.10.10")` ajoute `SchemaEvidence` et expose `SchemaUnchangedProven`. Il n'écrase jamais `Status` ni `ProtoChangedProven`.

## CLI indexeur épinglée et absence d'auto-exécution

Le README et `src/dri/autopipeline.py` du dépôt `%USERPROFILE%\Videos\GIVRE\DofusReverseIndexer` confirment : la commande **sans `--execute`** fait un plan hors ligne, avec une version explicite ; `--execute` peut faire de l'acquisition Cytrus et des extractions, et doit rester absent de ce flux. Le dossier `GivreActive` est une entrée **en lecture seule** et l'output est séparé de l'indexeur et de GivreActive.

`ProtocolIndexerPlanner.BuildReviewOnly(report, workspaceRoot, approvedPythonExe, approvedPythonSha256, userConfirmed, destinationAppDirectory)` fabrique uniquement les arguments fixes :

```
python.exe -m dri rework-auto-update --givre-root %USERPROFILE%\Videos\GIVRE\GivreActive --output-root %USERPROFILE%\Videos\GIVRE\GivreWork\artifacts\protocol-review --version 3.7.11.11
```

`workspaceRoot` pointe sur le dossier parent `GIVRE`. La destination produite est soit `GivreWork`, soit `GivreWork-Integration-20261020` ; aucun dossier choisi arbitrairement n'est accepté. Si les deux sont disponibles, utiliser le paramètre `destinationAppDirectory` pour viser explicitement le worktree de recette. La commande préparée est inspectable dans `.Executable`, `.Arguments`, `.WorkingDirectory`, `.EnvironmentVariables`. L'environnement fixe `PYTHONPATH=<indexer>/src` et `PYTHONDONTWRITEBYTECODE=1`. Les arguments sont dans une liste, sans interprétation par un shell, et `--execute` est absent par construction.

Sans `userConfirmed=true`, le plan affiche ses arguments mais `.CanRun=false`. L'appelant doit fournir un `python.exe` local existant, non redirigé, avec le SHA-256 **préalablement approuvé** de cet interpréteur ; une différence de hash bloque l'exécution. L'approbation ne vaut que pour la revue hors ligne et doit être donnée via un bouton clairement libellé. Le bouton d'audit automatique à l'ouverture doit se limiter à `Inspect`, qui ne lance rien.

`ProtocolIndexerRunner.RunReviewOnlyAsync(...)` est l'option d'exécution **sur clic explicite seulement**. Il reconstruit et revalide le plan, utilise `UseShellExecute=false`, capture la sortie bornée, limite la durée à trois minutes et accepte comme succès uniquement une sortie JSON `status=planned`, `mode=plan`, `review_only=true`, `runtime_certified=false`, `auto_promote=false`, `givre_runtime_modified=false`. Il ne déclenche aucun upgrade, téléchargement, lancement Dofus, injection ni activation de codec. Le comportement de l'indexeur doit être revalidé si ses scripts évoluent ; son arbre contient des fichiers locaux non commités dans la version lue le 20/10/2026.

## Intégration UI réalisée dans GivreWork

`MainViewModel.ProtocolAudit.cs` et la barre de titre `MainWindow.axaml` lancent un contrôle **local asynchrone** à chaque démarrage : marqueur version du client officiel, quatre SHA-256, build du bundle, puis paire de snapshots Proto Studio la plus récente antérieure au client installé. La bannière protocole est visible pour rendre l'état vérifiable même si les numéros de version correspondent. L'ancien bouton « Revérifier » utilise désormais la même sonde : l'ancien code qui dépendait exclusivement de `manifest.cytrus` pouvait indiquer « version inconnue » alors que `StreamingAssets/version` était présent.

`ProtocolObservedDrift` maintient séparément une **observation locale non certifiée** dans `%LOCALAPPDATA%/GivreClone/ProtocolObservations` (identifiant de chemin haché, version, quatre SHA). Première observation : un repère est créé, sans prouver la compatibilité. Aux démarrages suivants : un changement de version **ou d'empreinte à version identique** produit un avertissement. En cas de changement, l'ancien repère n'est pas écrasé automatiquement ; un acteur local pouvant modifier ce fichier, **jamais** de décision de protocole ou d'autorisation gameplay à partir de lui. Tests négatifs et multi-démarrage : `ProtocolObservedDriftTests`.

Les boutons de la bannière ont des responsabilités distinctes :

- **Vérifier** : lecture locale, sans réseau ni client lancé.
- **Analyse avec l'indexeur** : lancement explicite du plan `rework-auto-update --version <installée>` **sans** `--execute`, sans acquisition ; pas de promotion.
- **Extraire / comparer** : second clic distinct qui ajoute `--execute` au plan épinglé, **peut télécharger des fichiers via Cytrus et analyser en caches isolés**, exige au moins 3 Gio libres, limite l'exécution à 25 minutes et ne remplace jamais un bundle Givre. Cette option n'est **pas** appelée au démarrage. Elle est plus large que `ProtocolIndexerRunner.RunReviewOnlyAsync`, lequel reste strictement limité au plan sans téléchargement. Un hash Python calculé au clic vérifie la stabilité du fichier entre sélection et exécution mais ne constitue **pas** une approbation externe préalable.

La destination du plan est confinée à `GivreWork` ou au worktree `GivreWork-Integration-20261020`, selon le dossier d'exécution, jamais à `GivreActive` ou à la racine du jeu. La sortie visible est bornée, les erreurs sont classifiées sans impression des arguments sensibles (qui n'en contiennent pas). L'opérateur doit examiner les rapports et les preuves wire/ACK avant de promouvoir manuellement un quelconque codec, dans un autre processus de validation.

**Version distante ajoutée le 20/10 :** `OfficialDofusReleaseProbe.CheckAsync` lit uniquement `https://cytrus.cdn.ankama.com/cytrus.json`, limite la réponse à 64 Kio et 7 secondes, refuse les redirections et extrait strictement `games.dofus.platforms.windows.dofus3`. Ce contrôle HTTPS fonctionne au démarrage et à la demande, sans lancer le CLI ni télécharger des binaires. L'état « nouvelle version publiée » reste **distinct** de « nouvelle version installée » : par exemple, le CDN annonce 3.7.12.12 tandis que l'installation locale est en 3.7.11.11. Aucune validation des codes/protos pour 3.7.12.12 n'est déduite automatiquement. Si le réseau échoue, état distant UNKNOWN et fonctionnement local conservé. Tests purs `OfficialDofusReleaseProbeTests`.

L'égalité de deux `.proto` extraits est une preuve de fichiers identiques, pas une certification du gameplay.

## Tests et état

Tests déterministes `ProtocolUpdateProbeTests.cs` et `ProtocolUpdateIndexerTests.cs` : fixtures factices sans lancement Dofus ni Python, absence de baseline fiable, priorité des écarts de version, dérive SHA, marqueur officiel multiligne, doublons/invalidités, fichiers manquants, commande CLI fixe, demande de confirmation, binaire Python approuvé par hash, refus de destination arbitraire, validation réelle des `.proto` et refus des fichiers falsifiés. Le projet Tests lie les sources de l'App à partir du répertoire `ProtocolUpdates/*.cs` ; cette modification `.csproj` appartient au prime.

Ce lot ne crée pas de nouvelle baseline promotionnée. Il ne certifie aucune compatibilité wire ni aucun succès gameplay. Il prépare la décision et l'audit opérateur, avec preuve locale explicite et absence de modification de GivreActive.
