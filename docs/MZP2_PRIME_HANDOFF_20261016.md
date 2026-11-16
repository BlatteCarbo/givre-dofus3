# MZP2 PRIME — 16/10/2026 — parité GivreClone v1.3.1 encore ouverte

## Résultat

Le produit complet n'est PAS compilé ni connecté. Les sources GivreClone.App
contiennent les vrais parcours Comptes/Bots/Combat/Carte/Inventaire/Banque/HDV/
Lua/Stats/Planning/Équipe/Proxys/Adaptateurs, mais GivreCore et
GivreLauncher.Core restent absents. Aucun bot actif n'a été fermé, relancé,
déconnecté ni visé par les tests ; aucune licence ou authentification contournée.

## Rétro-ingénierie source et binaire

- La référence installée GivreClone v1.3.1 reste un PE x64 sans répertoire CLR,
  SHA-256 21C9DE5BA4D15763E9DAF029FD7432E3574EA113C9D1F44175FA9E0590290758,
  54 083 584 octets ; son gros segment .g0z haute entropie rend plausible une
  protection/encapsulation native, sans prouver NativeAOT ni type de protecteur.
  La copie hors installation avait été refusée ; aucune décompilation réelle.
- tools/MZP2NativeProvenance.py et docs/MZP2_NATIVE_PROVENANCE.md :
  20 zones complémentaires /givre, environ 101 907 entrées, 474 EXE,
  aucun binaire ayant l'empreinte/la taille exacte, deux zones partielles.
  Matrice de six preuves source/version, EXE_CONFIRMED faux.
- tools/MZP2DependencyInventory.py, docs/MZP2_DEPENDENCIES.md :
  source-only 510 références lexicales réparties sur 100 fichiers,
  GivreRelay authentique n'est ni GivreCore ni GivreLauncher.Core ;
  faux stubs exclus du build. Les 171 erreurs du dernier build App complet
  restent imputables principalement à ces projets absents.

## Intégrations MZP2 sur les VRAIES sources produit

- GivreClone.Engine/BotSession.cs, SessionBinder.cs, BotOrchestrator.cs :
  distinguent connexion observée / session protégée authentifiée.
  AuthenticatedSessions est recalculé sans garder le verrou binder pendant
  l'appel d'autorité ; SessionAuthenticated est distinct de SessionOpened.
- BotOrchestrator reçoit maintenant explicitement une fabrique
  hostBoundGuardedFactory(capture, catalog, sink, pacer) qui obtient
  le vrai ProxyFrameSink de son propre TcpProxyServer, jamais une fausse
  appartenance déclarée par un sink externe. BotHost le relaie ;
  BotHostService.StartBound expose le port protégé au service App.
- BotHostService.Start garde le chemin sans argument désactivé et exige
  un client épinglé, une fabrique protégée, un port de test différent
  de 5657/5658 et un compte de TEST sous LOCALAPPDATA/GivreReplica/TestRuns,
  sans lire APPDATA/GivreClone/accounts.json.
- DofusCaptureHost n'énumère/injecte jamais tous les PID Dofus ; garde
  uniquement un client épinglé par PID, naissance, chemin/version/SHA.
  Pas de hash périodique pour PID déjà admis ni deux tentatives concurrentes.
  Stop attend la sortie de l'injection engagée, et aucun nouveau hook ne
  peut commencer après le refus d'admission. Aucun client n'a été injecté.
- MZP2OwnedHostAdmission.cs (worker) porte les contrats pour permis,
  autorité de session, verifier de réponse indépendant et journal durable.
  Configuration dédiée MZP2IsolatedTestHostConfiguration obligatoire :
  approbation opérateur indépendante, port non 5657/5658 et .json sous
  LOCALAPPDATA/GivreReplica/TestRuns ; aucun compte Givre existant chargé.
  Tant que le host binding et ces autorités ne sont pas intégralement
  vérifiés en vrai, le démarrage et le serveur ACK restent REFUSÉS.

## Vérifications de cette passe

- Givre: tools/MZP1GuardedWiringHarness 28/28 PASS Release après
  nouvelle fabrique host-bound et nouveaux scénarios d'identité ;
  MZP1CaptureOwnerHarness 4/4 PASS.
- tools/MZP2HostAdmissionHarness compile les sources App/Engine protégées,
  41/41 xUnit PASS Release (injections et hôtes uniquement stubbés dans
  le harness), incluant tests des chemins comptes isolés, ports dédiés et
  interdiction d'auto-attribuer une liaison hôte.
- MZP2 dépendances : 11/11 tests Python PASS, inventaire source SHA-guardé.
  MZP2 analyse PE : 15/15 scénarios synthétiques PASS.
- MZP1OperatorShell autonome : dotnet publish Release win-x64,
  self-contained=false PASS (fichier publish/MZP1OperatorShell.exe).
  Vrai shell diagnostics Windows avait déjà été ouvert puis fermé sans jeu ;
  il ne remplace pas le produit GivreClone.
- Les tests utilisent des GivreCore substitutes isolés dans tools/.
  Aucun test n'est une preuve réseau/gameplay/parité complète.

## Limites et priorité suivante

1. Récupérer légalement les sources GivreCore, GivreLauncher.Core ou une
  représentation hors ligne autorisée authentifiable du vrai binaire :
  sans cela impossible de compiler le shell complet, prouver le codec
  et récupérer C# de GivreClone v1.3.1.
2. Brancher au vrai host-bound sink la provenance réseau indépendante
  et le vérificateur de réponse positive d'une instance de test DISTINCTE ;
  ne pas reproduire un ACK ou une connexion depuis un journal local.
3. Tester une connexion réelle autorisée et tous les parcours Givre,
  puis parité exhaustive écran/moteur, sessions, H24 et performance.
4. GivreRework P0 : le pont producteur → framer → dual replay a été compilé
  et testé hors ligne dans son propre repo ; G0 reste non validé tant que le
  décodeur legacy réel et un corpus privé authentifié manquent.
