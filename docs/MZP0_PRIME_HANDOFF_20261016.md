# MZP0 — handoff PRIME, 16 octobre 2026

## Objectif et verdict

Livrer une application indépendante reproduisant GivreClone.exe v1.3.1 :
écrans, navigation, transport, combat, Lua, métiers, inventaire, banque,
commerce, supervision et robustesse H24. **Objectif NON terminé**.
Les tests ci-dessous sont exclusivement hors ligne (code/contrats), sauf
observation passive de la fenêtre active ; aucune session gameplay de la
nouvelle application n'a été attestée.

## Preuves et écarts réels

- Fenêtre GivreClone v1.3.1 observée avec trois bots actifs. Aucun bot coupé,
  aucun processus fermé ni redémarré. Observation UI : navigation principale
  Comptes/Bots/Équipe/Proxys/Adaptateurs et sous-pages Console, Combat, Carte,
  Inventaire, Banque, HDV, Lua, Stats, Planning, Paramètres, Paquets, Infos.
- GivreShareWork est une copie de sources partielle. La source
  GivreClone.App/Services/LicenseService.cs déclare AppVersion 1.2.0,
  mais cette chaîne n'établit pas la provenance du binaire v1.3.1 observé.
- Aucun EXE v1.3.1 détaché/autorisé sous /givre ; **aucun C# réel extrait
  de ce binaire**. Les dépendances de compilation
  GivreProxy/GivreCore.csproj et GivreLauncher/GivreLauncher.Core.csproj
  sont absentes. Pas d'EXE clone utilisable produit dans cette passe.
- Mappings candidats 3.7 : 1 337 alias / 3 725 messages avec champs,
  487 couples requis côté Givre, un couple absent et deux ambigus.
  Ni l'origine du wire ni un ACK serveur n'ont été démontrés.
- GivreRework/HANDOFF.md garde G0 non validé. Aucune modification
  GivreRework n'a été faite dans cette passe : portage conditionné à G0
  et tests de compatibilité Dofus PC 3.7.10.10.

## Code nouveau et intégration

1. GivreClone.Engine/PacketBus.cs : course publication/timeout/cancel corrigée ;
   GivreClone.Tests/MZP0PacketBusTests.cs et tools/MZP0PacketBusHarness.
2. GivreClone.Engine/Recovery/MZP0RecoveryModels.cs,
   MZP0RecoveryJournal.cs et MZP0GuardedActionSender.cs : journal durable,
   nonce de génération, gestion des opérations incertaines, aucune API
   de replay automatique, vérification externe exigée pour un ACK.
   GivreClone.Tests/MZP0RecoveryJournalTests.cs et
   MZP0GuardedActionSenderTests.cs ; tools/MZP0RecoveryHarness.
3. GivreClone.App/Controls/MZP0OperatorPanel.axaml (+ codebehind),
   ViewModels/MZP0OperatorViewModel.cs et MZP0OperatorContracts.cs ;
   MainWindow.axaml/.cs : onglet Diagnostics intégré au code du shell,
   navigation réversible, source absente = NON ATTESTÉE / ACK INCONNU.
   Tests tools via GivreClone.App/Controls/MZP0OperatorHarness.csproj.
4. GivreClone.Protocol/MZP0ProtocolAdmission.cs :
   contrôle C# de version, SHA, schémas, tags et ambiguïtés
   avec refus systématique d'autorisation gameplay.
   tools/MZP0ProtocolAdmissionHarness (13 xUnit PASS Debug et Release).
5. tools/MZP0_ProtocolGuard.py, MZP0_SourceParity.py,
   MZP0_OfflinePeAudit.py, MZP0_OfflineDecompile.py et tests associés.
   Le pipeline de décompilation contrôlé exploite ilspycmd pour des
   binaires CLR IL-only détachés, sans exécuter l'EXE.

## Validation reproductible

- tools/MZP0PacketBusHarness : 14/14 xUnit PASS .NET10.
- tools/MZP0RecoveryHarness : 17/17 xUnit PASS Release (sources réelles
  du sender, protocole et journal ; seuls types GivreCore stubbés).
- GivreClone.App/Controls/MZP0OperatorHarness.csproj : 21/21 xUnit PASS,
  incluant contrats XAML de navigation ; rendu natif complet non testé.
- tools/MZP0ProtocolAdmissionHarness : 13/13 xUnit PASS Release.
- tools/MZP0_ProtocolTests.py : 18 scénarios source-only PASS.
- tools/MZP0_SourceParityTests.py : 14 scénarios PASS ;
  tools/MZP0_SourceParity.py --check PASS après régénération.
- tools/MZP0_OfflinePeAuditTests.py : 11/11 PASS et
  tools/MZP0_OfflineDecompileTests.py : 18/18 PASS (fixtures).
- git diff --check : exit 0, avertissements de conversion LF/CRLF sans erreur.

## Trois priorités de la prochaine passe

1. Obtenir une **copie offline autorisée et détachée** de GivreClone.exe v1.3.1
   sous /givre et documenter hash/identité du build. Lancer l'audit PE,
   puis décompiler réellement s'il contient du CLR ILONLY. Pour AOT/native,
   documenter la frontière statique sans fabriquer de sources.
2. Restaurer les dépendances de compilation autorisées GivreCore et
   GivreLauncher.Core depuis leurs sources/projets légitimes ; compiler
   toute GivreClone.App, vérifier le panneau Diagnostics sur un hôte de test
   **distinct** sans perturber les trois bots actifs. Raccorder de vrais ports
   d'origine de session et de réponse au guarded sender seulement après
   preuve indépendante ; ne pas injecter tous les PID Dofus.
3. Comparer EXE et code fonctionnalité par fonctionnalité : comptes,
   transport MITM, personnages/serveurs, Lua, navigation, combat,
   HDV/banque/métiers et H24. Après G0 GivreRework et contrôle exact
   du protocole 3.7.10.10, seulement alors portage et tests gameplay.

Ne jamais confondre code présent, build isolé, runtime applicatif
ou preuve de gameplay. Préserver les autres modifications déjà présentes.
