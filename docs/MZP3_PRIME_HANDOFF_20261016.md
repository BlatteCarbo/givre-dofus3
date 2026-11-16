# PRIME MZP3 — 16 octobre 2026

## RESULT

Le véritable GivreClone.App complet reste BLOQUÉ par les sources manquantes
GivreCore et GivreLauncher.Core. Aucun faux build complet n'est déclaré.
Un WinExe indépendant de vraie gestion de profils test + inspecteur Protobuf
neutre + diagnostics MZP0 a en revanche été compilé, publié et exécuté sur
Windows. Les trois bots de GivreClone actif sont préservés.

## CHANGES

- GivreClone.Engine/BotOrchestrator.cs : aucun ActionSender historique
  non protégé par défaut. Sans vraie fabrique, utilise le graphe MZP1
  avec IActionSender refusant tous les envois ; test négatif ajouté.
- GivreClone.Protocol/MZP3RawProtobuf.cs et MZP3FrameAndEnvelope.cs :
  parser/writer borné wire 0/1/2/5, varints canoniques, tags inconnus
  et répétés conservés, framing neutre, slots enveloppe fournis explicitement.
  Aucun shim GivreCore ou tag Dofus supposé.
- GivreClone.App/Services/MZP3TestAccountService.cs et .Admission.cs :
  vraie persistance locale isolée, owner ACL, Save/Reload/CRUD, hash
  d'intégrité, réconciliation, candidat serveur/personnage sans identité
  gameplay, prévol MZP2 impossible sans autorités externes.
- tools/MZP3ReplicaWorkbench : interface Avalonia autonome réutilisant
  MZP0OperatorPanel, gestion des profils via vrai service, et page Paquets
  utilisant le codec MZP3. Autres vues explicites INDISPONIBLES.
- GivreRework/MZP3LegacyDecoderAdmission : portage limité et
  admission NotCompared quand le décodeur legacy 3.7.10.10 manque.
  G0 n'est PAS validé.

## VALIDATION

- WinExe MZP3ReplicaWorkbench Debug et Release : 0 erreur/warning.
  Publish Release win-x64 framework-dependent PASS.
- Windows UI réelle : fenêtre Debug puis Release, création/relecture
  après relancement et suppression d'un profil de test local; vue Paquets,
  fixture générée puis décodée dans l'UI (#1 Varint), sans réseau.
- MZP1GuardedWiringHarness : 29/29 Release PASS.
- MZP2HostAdmissionHarness : 41/41 Release PASS.
- MZP3TestAccountsHarness : 27/27 Release PASS (I/O Windows réelles,
  dossier de test isolé).
- MZP3AdmissionLinkHarness : 7/7 Release PASS.
- MZP3ProtoHarness : 18 suites PASS ; différentiel neutre et malformations.
- GivreRework MZP3ReplayHarness : 38 contrôles PASS offline.
- GivreRework DesktopHost Release Avalonia Pro : 0 erreur/warning.

Ces résultats sont des preuves UI et stockage local ou tests hors ligne.
Ils ne prouvent aucune connexion Dofus, aucun ACK, aucune parité gameplay.

## BLOCKERS / suite

1. Véritable GivreClone.exe v1.3.1 : binaire x64 sans CLR, gros segment
   .g0z forte entropie, aucune copie détachée autorisée retrouvée.
   NativeAOT/empaquetage NON CONCLUSIFS ; pas de C# décompilé attesté.
2. GivreCore/GivreLauncher.Core authentiques toujours absents. La
   compilation complète GivreClone.App reste impossible et les vrais services
   login/transport ne doivent pas être remplacés par les stubs de tests.
3. L'origine réseau indépendante, le décodeur 3.7.10.10 et un vérificateur
   positif de réponse serveur restent à fournir depuis un client de test.
4. GivreRework G0-G8 non certifiés.

Prochaine priorité : restaurer source fournisseur prouvé, puis un vrai
parcours test login→framing→décodage→réponse serveur attestée, avant
le combat, HDV, Lua et la supervision H24.
