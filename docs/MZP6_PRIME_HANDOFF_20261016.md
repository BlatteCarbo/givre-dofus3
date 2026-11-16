# MZP6 PRIME — 16 octobre 2026 (suite MZP5)

## RESULT

Nouveau **WinExe Avalonia Pro** indépendant compilé et publié en Release
Windows x64, avec une vraie nouvelle page Sécurité TEST et un vrai inspecteur
de protocole GameMessage en mode candidat uniquement. L'interface a été
vérifiée dans une **vraie fenêtre Windows** via Chat On Steroids Desktop,
puis cette seule fenêtre de test a été fermée ; le GivreClone v1.3.1 original
est resté ouvert. Deux workers réellement actifs ont produit les modules
MZP6 Protocol Inspector et Givre P0. Aucun worker échoué n'est compté.

**Important : pas de jeu, session authentifiée, ACK, MITM ou parité 1.3.1
réelle revendiqués.** L'exécutable livré est une tranche réellement
fonctionnelle pour les opérations locales de TEST ; les autres onglets
historiques disent explicitement qu'ils sont indisponibles.

## CHANGES — application reliée au code de production

- `tools/MZP3ReplicaWorkbench/ReplicaWorkbenchWindow.cs` : navigation
  `Sécurité TEST` qui monte le vrai `MZP4TestAccountsPanel` (MZP3→MZP5) ;
  page Paquets monte le vrai `MZP6ProtocolInspectorPanel` ; stockage Workbench
  seulement après clic, pas d'ouverture automatique au démarrage.
- `tools/MZP3ReplicaWorkbench/MZP3ReplicaWorkbench.csproj` : compile les
  modules de production MZP0/MZP3/MZP4/MZP5/MZP6; option Pro (TreeDataGrid +
  Charts) et Debug DiagnosticsSupport, clé uniquement depuis les variables
  d'environnement, jamais enregistrée dans le dépôt.
- `tools/MZP3ReplicaWorkbench/Program.cs` : DevTools uniquement en Debug.
- `GivreClone.App/Views/BotsView.axaml` : le vrai onglet Paquets possède
  également `<views:MZP6ProtocolInspectorPanel />` sans fournisseur fictif.
- `GivreClone.App/Views/MZP6ProtocolInspectorPanel.cs` (worker-1) : vrai C#
  MZP5InboundReader + MZP4 catalogues, envoi absent, saisie HEX effacée,
  scope explicite et quarantaine, sortie expurgée. Sans catalogue/provenance
  indépendante, la commande est désactivée.
- `tools/MZP6ProtocolUiHarness/` et `docs/MZP6_PROTOCOL_UI.md`.
- `tools/MZP6WorkbenchUiHarness/` : navigation réel WinExe, montages
  contrôles, statuts, absence de création de TEST store en construction.
- `tools/MZP6Release/README.txt` : avertissements et lancement.

Dans `/givre/GivreRework` (worker-5) :

- `src/GivreRework.Infrastructure/Protocol/Differential/MZP6P0TimelineAdmission.cs`
- `tests/MZP6P0Harness/`
- `docs/MZP6_P0.md`

Admission source-only supérieure à MZP5 : cinq jalons étiquetés et ordonnés,
autorité indépendante à fournir, vérifications de SHA avant/après callbacks
et deux ports, rejet TOCTOU/mutations et versions legacy historiques. Les
étiquettes ne sont **jamais** assimilées à des observations de gameplay.

## VALIDATION — commandes et niveaux

| Contrôle | Résultat observé |
| --- | --- |
| `MZP6WorkbenchUiHarness -c Release` (standard Headless) | **21/21 PASS** |
| `MZP6ProtocolUiHarness -c Release` | **43/43 PASS** |
| `MZP3ReplicaWorkbench -c Debug -p:EnableAvaloniaPro=true` avec restore Debug Pro | **0 erreur / 0 warning** |
| `dotnet publish MZP3ReplicaWorkbench -c Release -r win-x64 -p:EnableAvaloniaPro=true` après restore Release Pro | **EXIT 0** |
| MZP6 fenêtre Windows via Chat On Steroids Desktop | Vraie navigation Sécurité TEST et statut offline vérifiés; écran fermé ensuite |
| WinExe **Release x64 publié** via Chat On Steroids Desktop | **Lancé réellement** ; onglet Paquets montre le vrai MZP6, bouton Analyser désactivé sans catalogue, état ACK inconnu ; seule fenêtre de TEST fermée |
| `GivreRelayAgent -c Release` | **0 erreur / 0 warning** |
| `GivreClone.App -c Release` | **FAIL CS0246**, `GivreCore` absent |
| Givre `MZP6P0Harness -c Release` | **57/57 PASS** offline |
| Givre `MZP5P0Harness -c Release` | **42/42 PASS** offline |
| Givre `DesktopHost -c Release -p:EnableAvaloniaPro=true` | **0 erreur / 0 warning** |
| Givre `GivreRework.App -c Debug -p:EnableAvaloniaPro=true` | **0 erreur / 0 warning** |

**Attention à la matrice de build :** en partageant le même `obj/` entre
compilation Pro et harness standard, les assets de NuGet issus de la
configuration précédente peuvent causer `AVLIC0001` ou `WithDeveloperTools`
introuvable. Restauration explicite de **chaque** configuration avant son
build et séparation des deux validations ont donné les résultats ci-dessus.
Le harness Headless est standard; le WinExe direct est publié **avec Pro**.
La fenêtre `GivreClone v1.3.1` originale était encore ouverte après fermeture
de **notre seul** Workbench publié, vérification via MCP Desktop.

Chemin de publication de l'exécutable :

`%USERPROFILE%\Videos\GIVRE\GivreShareWork\tools\MZP3ReplicaWorkbench\bin\Release\net10.0-windows\win-x64\publish\MZP3ReplicaWorkbench.exe`

SHA-256 du binaire EXE du publish final :

`23AB1C86B17A1F852A13449AD9FBBED0F3529B5A88D07C56C0477B65E0CC7087`

`MZP3ReplicaWorkbench.exe` est un **apphost .NET** (environ 162 Ko) : pour
installer, conserver **toutes les DLL et fichiers du dossier publish**,
pas seulement cet EXE. Une archive Windows x64 privée est produite dans
`artifacts/`, sans `.pdb` ni secrets. Le runtime .NET 10 est requis.

Artefacts Windows vérifiés, conservés **localement dans ce dépôt** :

| Archive | Contenu |
| --- | --- |
| `artifacts/MZP6-ReplicaWorkbench-win-x64-AvaloniaPro.zip` | 38 fichiers : EXE, DLL, JSON de runtime et README |
| `artifacts/MZP6-Source-Modules.zip` | 39 sources/tests/documents et script de packaging |

`artifacts/MZP6-release-manifest.json` donne les SHA des archives et de
**chaque fichier**. C'est la référence exacte à utiliser après une
republication (évite les empreintes autoréférentielles du ZIP source).
La création par `py -3 -B tools/MZP6Release/package.py` est vérifiée
déterministe (deux exécutions successives, mêmes deux empreintes SHA-256).
L'archive source est un paquet des sources ciblées et de l'application
indépendante ; elle **ne remplace pas tout GivreShareWork** pour construire
le produit historique. Les contrôles premium Avalonia sont référencés et
distribués avec le build Pro, mais les visualisations premium ne sont pas
présentées comme fonctionnelles tant qu'aucune donnée authentique ne les
justifie ; l'UI réelle utilise les contrôles Avalonia classiques.

## BLOCKERS

### Écart mesuré avec la parité complète

| Domaine demandé | Acquis réels dans cette passe | Parité GivreClone v1.3.1 |
| --- | --- | --- |
| Comptes, serveurs, personnages | CRUD et candidats de **TEST** persistants, UI native | **Non** : pas de login ni sélection serveur authentifiée |
| Launcher / Ankama / Zaap | Prévol chemin, ACL et port TEST | **Non** : fournisseur `GivreLauncher.Core` manquant |
| MITM et socket | Sources consommateur, source GivreRelayAgent compilable | **Non** : `GivreCore` et binding réel manquants |
| Protobuf / émission / réception | Vrais modules C# MZP3–MZP6, inspecteur borné | **Partiel offline** : framing/producteur live non démontrés |
| États et ACK | Journal MZP0, flags d'admission et résultats distincts | **Non** : aucun vérificateur de réponse serveur |
| Carte / déplacements | Sources métier historiques conservées | **Non** : effets gameplay non observés |
| Combat / challenges | Sources métier historiques conservées | **Non** : combat Dofus 3.7.10.10 non attesté |
| Lua / métiers / planning | Sources/écrans anciens dans le dépôt | **Non** : aucun scénario d'exécution gameplay validé |
| Inventaire / équipement / banque / HDV | Lecteurs/services sources historiques | **Non** : flux applicatif bout-en-bout absent |
| Reprise / H24 | Journal et détection d'incertitude réels, tests de panne | **Non** : ni reconnexion vérifiée ni endurance 24 h |
| UX / distribution | WinExe sombre et Pro Win-x64 publié, UI Windows testée | **Partiel** : fonctions historiques non disponibles |

Pour GivreRework : la porte **G0 est bloquée** (pas de deuxième décodeur
legacy indépendant et pas de corpus authentifié). Les portes **G1–G8**
restent également non validées ; un build Avalonia Pro ou un rapport de
tests hors ligne ne remplace pas ces preuves.

1. Projets fournisseurs authentiques `GivreProxy/GivreCore/GivreCore.csproj`
   et `GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj` toujours
   absents. Aucune façade mock en production ne les remplace. L'App GivreClone
   principale ne compile toujours pas, et la plupart des fonctions gameplay
   historiques ne sont pas opérationnelles dans l'exécutable limité.
2. Aucune copie détachée vérifiée de l'EXE GivreClone v1.3.1 n'est utilisée :
   nous n'avons pas produit de décompilation C# du binaire lui-même, seulement
   intégré et tracé des sources existantes autorisées. Aucun accès au dump
   privé, comptes, secret ni processus Givre n'a été pratiqué.
3. Aucune véritable réception entrante authentifiée 3.7.10.10 ni ACK serveur.
   Les catalogues et les champs restent candidats hors ligne.
4. Givre G0–G8 non validés : absence de vrai corpus privé connexion +
   3 cartes + combat et deuxième décodeur legacy exact 3.7.10.10.
5. Aucun MCP Avalonia DevTools n'est exposé comme outil dédié dans cette
   session. Validation native par **Chat On Steroids Desktop**, compilation Pro
   par **Chat On Steroids Core**. Ne pas attribuer une inspection DevTools
   Pro qui n'a pas eu lieu.

**Prochaine priorité substantielle :** provenance des deux vrais fournisseurs,
rebuild complet `GivreClone.App`, vrai producteur ingress MITM authentifié et
test bout-en-bout sur instance séparée. Puis deuxième port legacy exact
3.7.10.10 + capture privée pour G0 Givre.
