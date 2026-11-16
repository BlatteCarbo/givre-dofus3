# MZP2 — Provenance native du GivreClone v1.3.1, sans contournement

**Audit hors ligne daté du 16 octobre 2026.** Périmètre de modifications : nouveaux `tools/MZP2Native*` et présent document uniquement. L'installation GivreClone active, ses processus, ses comptes, ses configurations, ses dumps/captures et les mécanismes de licence/protection n'ont pas été approchés. Aucune tentative de recopier l'EXE installé, de le dépaqueter, de neutraliser des vérifications ou de forcer une décompilation n'a été effectuée.

## 1. Référence déjà attestée par PRIME, distincte de nos observations MZP2

Selon `docs/MZP1_PRIME_HANDOFF_20261016.md:15-31`, PRIME a observé **en lecture seule** les métadonnées du fichier installé :

| Propriété | Observation préexistante | Provenance / qualification |
| --- | --- | --- |
| Référence SHA-256 | `21C9DE5BA4D15763E9DAF029FD7432E3574EA113C9D1F44175FA9E0590290758` | Rapport MZP1 PRIME, **non recalculé** par ce worker depuis le fichier installé |
| Taille | **54 083 584 octets** | Rapport MZP1 PRIME |
| COFF | PE x64, machine `0x8664` | Rapport MZP1 PRIME |
| CLR | Répertoire CLR RVA=0 / size=0 | Rapport MZP1 PRIME ; **aucune IL managée garantie dans cet EXE** |
| Section `.g0z` | **53 967 360 octets**, entropie annoncée **7,952 bits/octet** | Rapport MZP1 PRIME, **pas une preuve de NativeAOT ni d'un packer déterminé** |
| Fenêtre produit | titre rapporté `v1.3.1` | Observation UI, pas une donnée de version certifiée par l'EXE détaché |

**Interprétation :** ce PE peut être natif, NativeAOT, empaqueté/protégé ou combiner plusieurs techniques. L'absence de CLR et une section haute entropie ne permettent **ni de choisir entre elles**, ni d'attribuer un produit tiers de protection. Nous ne transformons jamais une section native en pseudo-C# et n'utilisons pas `ilspycmd` pour cet EXE.

## 2. Nouvelle recherche MZP2 de copies détachées

`tools/MZP2NativeProvenance.py` a été conçu pour **les sous-arbres de /givre non couverts par la recherche nominative ciblée MZP1**, sans rescanner l'installation ou les sous-arbres Givre déjà couverts. Il parcourt une liste blanche de **20** sous-arbres de références, outils ou travaux de reconstruction : `analysis`, `artifacts`, `DocraftResearch_20261010`, `DoFarmCurrentAnalysis`, `dofusmasters-bot`, les dossiers `DofusMulti*`, `DofusReverseIndexer`, `DofusSpriteRendererWork`, `ExoFastResearch_20261010`, `ReaReference`, `retro-survival-autobot`, `GivreCompanionReference`, `GivreRework`, `GivreUnity`, `_safety` et trois `_tmp_*` de références autorisées.

Le scanner écarte `active`/`live`, installations, `AppData`, `obj`, `.git`, environnements virtuels, secrets, profils, comptes, dumps, captures, caches, assets, archives et toute jonction/lien symbolique. Aucun fichier non EXE n'est analysé. Les EXE de taille différente de **54 083 584 octets** sont exclus sans lecture de leur contenu ; même un EXE renommé doit avoir **la taille et les 64 chiffres du SHA-256 exact** avant de faire l'objet d'un quelconque profil PE. Aucune archive ZIP ou ancien dump n'est ouvert.

### Bilan de la passe MZP2 bornée (dernière validation)

| Mesure | Constat |
| --- | --- |
| Sous-arbres retenus | **20** |
| Couverture achevée dans les quotas | **18/20** |
| Répertoires parcourus | **8 181** |
| Entrées de système de fichiers considérées | **101 907** |
| Noms de fichiers EXE repérés | **474** |
| EXE de taille égale à la référence | **0** |
| Copies au SHA-256 exact | **0** |
| Inspection approfondie de l'EXE d'origine | **0 : jamais tenté** |

Les deux sous-arbres **partiellement couverts** sont `GivreRework` (budget de **4 000 dossiers**, et profondeurs limites) et `GivreUnity` (**80 000 entrées**, et profondeurs limites). Cela **interdit toute affirmation d'absence complète** sur /givre ou sur ces deux dossiers. Les exclusions de confidentialité restent volontaires et ne sont pas des preuves d'absence dans les répertoires exclus.

**Statut de la preuve : `blocked_no_matching_detached_exe`.** Aucun profil du vrai PE 54 Mo n'a été produit dans cette passe. Les caractéristiques `.g0z` restent exclusivement celles rapportées précédemment par PRIME.

Résultat machine-readable **du dernier parcours borné** : `tools/MZP2NativeEvidenceBounded.json`, comprenant les 20 rapports de sous-arbre, limites, matches SHA (vide), statut final, provenance du hash et matrice de dérive. La matrice correspondante est également isolée dans `tools/MZP2NativeVersionDriftBounded.json`. Les fichiers `MZP2NativeEvidence.json` et `MZP2NativeVersionDrift.json` préservent le **premier** parcours (avant durcissement de la borne mémoire par dossier), sans être présentés comme le plus récent. Des fichiers source peuvent évoluer pendant le travail parallèle ; les SHA de la matrice sont un snapshot, à recalculer après stabilisation du dépôt.

## 3. Pipeline natif conditionnel prêt pour une vraie copie correspondant au hash

Si une copie détachée et légitime **déjà sous un des dossiers autorisés** présente exactement le même SHA-256 que la référence, `static_fingerprint` pourra extraire **sans exécuter l'EXE** : format PE32/PE32+, machine COFF, timestamp COFF (pas une version produit), RVA point d'entrée, noms et tailles des sections, attributs, une **entropie approximative sur les 65 536 premiers octets au plus de chaque section**, noms des DLL importées, nombre des descripteurs d'import et **types de ressources de premier niveau**.

Le scanner n'extrait **jamais** le contenu de ressources, les payloads de sections, le code natif, les chaînes du programme, les clés ni des pages mémoire de processus. Les noms d'API ne sont pas désassemblés ; seules les **DLLs importées** sont listées. Les tables et les RVA doivent être bornés et cohérents ; les cas ambigus sont **rejetés**, non reconstruits par supposition. Un résultat `import_dlls=[]` peut avoir plusieurs explications (dont aucune table standard accessible) et ne prouve pas l'obfuscation ou une méthode d'empaquetage.

Cette **empreinte de format statique** ne contourne aucun dispositif de protection. L'empreinte PE, même couplée à un hash exact, ne permet pas de certifier une parité de fonctionnalités, une licence valide, un transport MITM réel ou une session Dofus.

## 4. Matrice source/version vérifiable : pas de concordance inventée

La matrice machine `tools/MZP2NativeVersionDriftBounded.json` présente les déclarations de six symboles réels dans la copie `GivreShareWork`, avec **chemin, ligne et SHA-256 du fichier source** calculés localement :

| Ancre source | Ligne relevée | Constats | Correspondance dans l'EXE v1.3.1 |
| --- | ---: | --- | --- |
| `GivreClone.App/GivreClone.App.csproj`, `AssemblyName` | 10 | Nom de sortie déclaré `GivreClone` | **Non démontrée** |
| `GivreClone.App/Services/LicenseService.cs`, `AppVersion` | 52 | Version source `1.2.0`, distincte du titre UI `1.3.1` | **Non reliée** |
| `GivreClone.App/GivreClone.App.csproj`, `PublishAot` | 27 | **Option** NativeAOT conditionnelle en Release ; aucune preuve de son usage dans cet EXE | **Non démontrée** |
| `GivreClone.Engine/BotSession.cs` | 12 | Déclaration de moteur de session | **Non démontrée** |
| `GivreClone.Engine/ActionSender.cs` | 34 | Déclaration d'émetteur d'action | **Non démontrée** |
| `GivreClone.Engine/DofusCaptureHost.cs` | 14 | Déclaration de hôte de capture MITM | **Non démontrée** |

Les six statuts sont `source_present`, chacun marqué `exe_confirmed=false`. Les chemins des sources et leurs hashes sont dans le JSON ; les versions, build flags et classes déclarées n'impliquent **aucune équivalence binaire**. La matrice enregistre `nativeaot_assessment="unknown"` et `packer_assessment="unknown"`.

## 5. Reproduction et tests 100 % hors ligne

Depuis le dépôt `GivreShareWork` sous /givre :

```powershell
py -3 -B tools/MZP2NativeTests.py
py -3 -B tools/MZP2NativeProvenance.py --output MZP2NativeEvidenceNewRun.json
```

La seconde commande refuse toute sortie préexistante : choisir un nouveau nom `MZP2Native*.json`, sans effacer l'ancien rapport. Elle retourne un code non nul si aucune copie exacte n'est trouvée, **comportement de blocage attendu**.

Les tests fabriquent des PE x64 synthétiques avec deux sections, annuaire d'imports et table de types de ressources, puis contrôlent cas positifs et négatifs : absence d'imports/ressources, SHA erroné, PE invalide, RVA import et ressources corrompus, exclusion de répertoires privés, liens, copie renommée de hash exact, limites de parcours et matrice source indépendante du produit. Aucune fixture issue du fichier protégé ou de son installation.

## 6. Action attendue pour dépasser ce blocage

La seule condition permettant d'étudier plus avant **le vrai fichier en respectant le mandat** est la découverte d'une copie préexistante, détachée et autorisée dans un sous-arbre sain, présentant le SHA-256 **exact**. Une recherche supplémentaire peut prioriser des répertoires de provenance offline précis non couverts par MZP2, sans élargir aux installations ou archives protégées. Ne pas retenter de copier l'installation active : l'opération a déjà été bloquée par PRIME. Aucune décompilation C#, aucun dépaquetage, aucune preuve de NativeAOT ni de protection nommée n'est revendiquée en l'état.
