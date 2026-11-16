# PRIME MZP1 — 16 octobre 2026 — résultats vérifiables

## Produit et frontière de preuve

Objectif conservé : successeur GivreClone v1.3.1 indépendant, parité des parcours
Comptes/Bots/Équipe/Proxys/Adaptateurs, transport MITM, protocole, Lua,
combat, inventaire, métiers, banque, HDV, reprise H24 ; transfert
GivreRework uniquement sur preuve Dofus PC 3.7.10.10.

**Non livré** : application complète compilable avec vrai transport, gameplay
ou connexion. Aucun ACK serveur, session vérifiée ou jeu opérationnel annoncé.
Le GivreClone actuellement actif et ses trois bots n'ont pas été arrêtés,
redémarrés, déconnectés ni modifiés.

## EXE v1.3.1 installé — vérification statique réelle

- Installation en lecture seule :
  %USERPROFILE%/AppData/Local/Givre/app/GivreClone.exe
- Taille : 54 083 584 octets ; timestamp fichier 15/10/2026 18:05.
- SHA-256 fichier installé :
  21C9DE5BA4D15763E9DAF029FD7432E3574EA113C9D1F44175FA9E0590290758
- PE machine 0x8664 (x64), signature PE 0x00004550, répertoire CLR
  RVA=0/size=0 ; ILSpy ne peut pas récupérer du CLR depuis cet EXE.
- Section native .g0z : 53 967 360 octets, entropie 7.952 bits/octet ;
  autres sections standard en grande partie size brute nulle. Fort indice
  de code empaqueté/protégé, mais ni NativeAOT ni protecteur identifiés
  formellement. Aucun pseudo C# décompilé.
- Bibliothèques de l'installation lues exclusivement en métadonnées :
  GivreKama.dll, GivreMods.dll, GivreMcp.exe. Pas de dump/process memory.
- Une copie vers offline-input a été tentée mais bloquée par le contrôle
  d'outil : aucune copie réussie ni manifeste de décompilation créé.
- GivreShareWork/Services/LicenseService.cs annonce 1.2.0 ; la fenêtre
  observée porte 1.3.1. Les chemins diffèrent en version déclarée ;
  correspondance par hash de sources/EXE non démontrée.

## Recherche de sources et dépendances (workers réutilisés)

- tools/MZP1_BinaryIndex.py, MZP1_BinaryVerify.py et docs/MZP1_BINARY_FINDINGS.md :
  GivreShareWork scanné intégralement dans le périmètre autorisé,
  257 dossiers/1 805 entrées, aucun GivreClone.exe. Index global à budget
  plafonné : ne prétend pas une absence sur tout le disque.
- tools/MZP1DependencyAudit.ps1 et docs/MZP1_DEPENDENCIES.md :
  8 808 répertoires utiles et 170 ZIP examinés, aucun projet authentique
  GivreProxy/GivreCore.csproj ou GivreLauncher/GivreLauncher.Core.csproj
  trouvé. Tools/MZP1DependencyRecovery/ProjectLinks.targets ne fabrique
  pas de dépendances ; remappage MSBuild optionnel seulement sur sources réelles.
- Compilation véritable GivreClone.App tentée : échec 171 erreurs (CS0246,
  GivreCore/ProtoField absents). Aucun client Dofus lancé.

## Intégrations de code et premier binaire exécutable

- GivreClone.App/Services/MappingCatalog.cs : catalogue incomplet REFUSÉ avant
  publication ; MissingRequiredFields vidé par Clear ; source directement
  exercée par tools/MZP1MappingPublicationHarness/ (6/6 tests Debug
  et Release PASS sur dépendances de test distinctes).
- GivreClone.Engine/Recovery/MZP1GuardedEngineWiring.cs :
  chaîne de vrais GameState/PacketBus/IActionSender/BehaviorHost sous
  vérification indépendante ; aucune autorité live fournie.
- GivreClone.Engine/BotSession.cs, SessionBinder.cs, BotOrchestrator.cs,
  BotHost.cs : chemin de création d'une vraie BotSession protégé
  branchable explicitement, sans créer un second ActionSender non protégé
  dans cette branche. tools/MZP1GuardedWiringHarness compile le vrai moteur :
  25/25 tests Debug et Release PASS avec seuls GivreCore hors ligne stubbés.
  L'ancien chemin legacy demeure : ne pas le traiter comme attestation.
- GivreClone.Engine/MZP1OwnedClientPermit.cs,
  DofusCaptureHost.cs, App/Services/BotHostService.cs :
  capture par défaut refusée sans client de test épinglé et garde de
  version/PID/naissance/chemin/SHA. Le balayage/injection de tous les
  processus Dofus a été remplacé par l'admission d'un seul PID déclaré.
  Aucun permis réel ni vérificateur de session n'est fourni.
  tools/MZP1CaptureOwnerHarness : 4/4 xUnit PASS (sources de production,
  substitutions sans socket ni injection uniquement dans harness).
- tools/MZP1OperatorShell/ : vrai WinExe .NET10 Avalonia autonome,
  réutilise MZP0OperatorPanel.axaml et la VM originales. Build Debug
  **PASS 0 erreur / 0 avertissement**. Binaire :
  tools/MZP1OperatorShell/bin/Debug/net10.0/MZP1OperatorShell.exe.
  Ouvert réellement sous Windows, fenêtre Observateur/Opérateur,
  cartes Session/Provenance/ACK et journal correctement rendus ;
  affiche NON ATTESTÉE / INCONNU, sans connexion ni données privées.
  Cette unique fenêtre de test a été refermée après contrôle.

## Limitations et prochaine priorité

1. Obtenir, par une méthode explicitement autorisée, une copie détachée
   du binaire exact x64 protégé pour étude statique native plus profonde ;
   ne pas déclarer ILSpy applicable à cette version.
2. Retrouver les vrais projets GivreCore et GivreLauncher.Core, ou
   reconstruire leurs implémentations par provenance/test différentiel.
   Aucun stub d'un harness ne doit passer en production.
3. Établir une identité de client TEST indépendante et son vrai décodage
   inbound, puis brancher les autorités de réponse positive distinctes et
   tester 1 connexion/3 cartes/combat à l'écart des trois bots existants.
4. GivreRework : aucun portage protocole effectué, parce que la preuve
   différentielle P0/G0 et les mappings non ambigus 3.7.10.10 manquent.

Classes de preuves : métadonnées EXE installées = observation statique ;
source C# = code source ; tests harness = hors ligne/simulation de dépendances ;
fenêtre MZP1 = UI native validée ; gameplay = NON VALIDÉ.
