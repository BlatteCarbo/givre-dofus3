# Ultrawork Notepad - outils de developpement Givre
Started: 2026-10-30

## Plan
Tier LIGHT : installation officielle et configuration selon des patrons
existants ; pas de nouvelle integration runtime ou de modification du jeu.
Skills : bun-1-4 (execution), lsp-setup/lua (configuration et verification),
research (sources primaires deja lues). Headless est une lane independante
deleguee a st_01a18eef ; le parent installe et verifie diagnostics et LuaLS.
Pas de planificateur ni de reviewer : procedures connues, aucun ulw-plan.

1. Lire SDK, projets et outils presents.
2. Restaurer diagnostics par manifeste local versionne.
3. Installer LuaLS officiel avec verification SHA256.
4. Configurer Lua 5.2 et les outils LSP sans modifier les autres worktrees.
5. Verifier versions, vraie collecte EventPipe, et echange LSP.
6. Integrer et executer les tests Headless isoles sous xUnit v3.
7. Relever les preuves, faire l'autorevue et nettoyer les ressources QA.

## Success criteria + QA scenarios
- Diagnostics : `dotnet tool run dotnet-trace --version` et
  `dotnet tool run dotnet-counters --version` retournent 0 ; une collecte
  EventPipe sur un processus QA sans compte produit une trace non vide.
- Headless : `dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj
  -c Release` retourne 0 et exerce la vraie vue AXAML, sans migrer xUnit v2.
- LuaLS : `lua-language-server.exe --version` retourne 3.19.2 ; initialize
  puis didOpen avec erreur syntaxique donnent le diagnostic attendu ;
  didChange corrige l'erreur, shutdown et exit terminent le serveur.
- Arret : les trois preuves passent, les commandes sont documentees et
  les ressources QA propres au lot sont nettoyees. Aucune action de jeu.

## Now
Installations terminees, configuration et preuves d'usage en cours.

## Todo
Configurer LuaLS ; collecte diagnostics ; verification Headless ;
autorisations et sources relues ; nettoyage et rapport.

## Findings
- SDK 10.0.402 ; dotnet-stack global 10.0.750501 deja present.
- App : Avalonia 12.1.1 ; tests existants xUnit 2.9.2, non modifies.
- Manifeste local : trace/counters/dump/stack 10.0.750501.
  `dotnet tool restore` code 0, quatre outils restaures.
- LuaLS release officielle 3.19.2 win32-x64, SHA256
  4a3dc851b3b31e7b75a23f645d1ba4a2137579a55f5fa4edbb17a3e2a6254044.
  Extraction a LOCALAPPDATA/Programs/LuaLS/3.19.2 ; installation code 0.
- Aucun AGENTS.md a la racine du worktree. Biome non installe pour JSON :
  ne pas ajouter un outil sans rapport ; verifier JSON par parseur natif.
- Une recherche large a rencontre deux repertoires artifacts inaccessibles ;
  ne pas confondre l'erreur de recherche et une absence de scripts.
- Creation de goal refusee : un goal global inacheve existe deja.
  Le contrat detaille de ce sous-lot est consigne ici sans le remplacer.
- Memoire partagee non modifiable : modification preexistante dans
  reference/givre-fm-parity.md. Ne pas committer ni effacer ce changement.
  Les faits et preuves de ce lot sont conserves dans ce document.

## Task resources
- RUN_TMP : %USERPROFILE%/AppData/Local/Temp/givre-devtools-ZYSKgs
- Variables pour commandes suivantes : TMPDIR=RUN_TMP,
  NODE_COMPILE_CACHE=RUN_TMP/node-compile-cache,
  BUN_RUNTIME_TRANSPILER_CACHE_PATH=RUN_TMP/bun-transpiler.
- Installations conservees : manifeste local et LuaLS utilisateur.
- bash_828 et bash_829 termines avec code 0 ; ZIP d'installation supprime
  par finally. Ne jamais effacer les caches NuGet partages.

## Learnings
Ne pas importer la branche master Headless dans la suite xUnit v2.
La version de paquet doit correspondre a Avalonia 12.1.1.
Une installation reussie ne remplace pas un echange LSP ou une vraie trace.

## Verification - resultats et corrections
- Quatre versions CLI verifiees : 10.0.750501.
- Premiere collecte code 3 : le separateur de `dotnet tool run` consommait
  les arguments enfant. Corrige avec `dotnet tool run dotnet-trace -- collect`.
- Commande valide : `dotnet tool run dotnet-trace -- collect --duration
  00:00:02 --output RUN_TMP/forge.nettrace -- dotnet
  tools/ForgeUiStress/bin/Release/net10.0/ForgeUiStress.dll`.
  Code 0, TRACE_PASS bytes=6374918 ; trace generee sans compte.
- Headless : 3 tests PASS, code 0. Captures 1440 et 760 inspectees.
  AVLN2000 detectait un ResourceInclude de theme invalide ; utiliser le
  dictionnaire type Amber, pas un URI vers son AXAML precompile.
- LuaLS : initialize annonce sumneko.lua 3.19.2, didOpen sur
  `local answer =\nreturn answer\n` emet keyword et exp-in-action, gravite 1.
  didChange vers `local answer = 42\nreturn answer\n` emet diagnostics=[].
  shutdown puis exit : code 0.
- Le client QA initial comparait incorrectement les URI Windows encodees
  et exigeait version=2 sur le diagnostic vide. LuaLS normalise le lecteur
  et omet legalement cette version optionnelle ; predicates corriges.
  Deux reprises ont aussi detecte des variables non persistantes du client
  eval ; corrigees avant le PASS. Aucun diagnostic du serveur n'est masque.
- Le lanceur utilisateur .local/bin/lua-language-server.cmd est accessible
  depuis PowerShell : --version retourne 3.19.2, code 0.
- Le moteur lsp_diagnostics de cette session conserve son erreur
  "Command not found: lua-language-server" malgre le lanceur et la
  configuration utilisateur. Ne pas presenter cela comme un PASS de ce
  moteur ; le protocole direct et la commande CLI sont verifies.

## Now - cloture
Suite xUnit v2 en cours ; preuves et nettoyage final a consigner.

## Resultats finaux
- Suite historique xUnit v2 : 3238 reussites, 0 echec, 2 ignores preexistants,
  23 secondes ; code 0. Aucun test ignore ou affaibli par ce lot.
- Le nouveau projet UI contient trois tests xUnit v3 verts et independants.
- Autorevue LIGHT : seuls manifeste, configuration et projet de tests ont ete
  ajoutes ; pas de modification runtime, de version Avalonia en production,
  de compte, de transport ou de jeu. Pas de reviewer requis, pas d'ulw-plan.
- Aucun commit : l'instruction de ne pas committer reste en vigueur.
- LuaLS est utilise par CLI et protocole direct. L'integration du moteur LSP
  deja demarre reste a verifier apres relance de l'hote ; ne pas la declarer
  fonctionnelle dans cette session.

## Nettoyage et transmission
- DEVTOOLS_CLEANUP_PASS processes=0 tmpAbsent=true, code 0 :
  aucun processus dotnet lancant ForgeUiStress.dll ne reste ;
  RUN_TMP et ses caches/logs/trace QA ont ete supprimes.
- LuaLS QA : code 0, signal null ; les sondes Lua ont ete supprimees.
- Les quatre fichiers JSON livres sont parses sans erreur.
- PATH utilisateur relu : LUALS_USER_PATH_PASS, code 0.
- Installations, captures Headless et documentation conservees pour usage.
- Le prochain controle est `lsp_diagnostics` sur un script apres relance
  normale d'OmO, meme compte Windows, sans privileges administrateur.
- Ce sous-lot d'installation est termine ; le goal global Givre et les
  autres lots restent ouverts. Aucune completion globale n'est revendiquee.

## Apres relance - integration LSP corrigee et verifiee
- Premiere invocation de lsp_diagnostics : encore Command not found.
  La relance seule ne corrigeait pas le vrai defaut de configuration.
- Le nouveau processus resolve bien LuaLS avec Bun.which ; le PATH est bon.
- Source locale plugin/extensions/omo.js, fonctions JZ/VZ/XZ :
  candidats projet .omo/lsp-client.json et .pi/lsp-client.json ;
  candidat utilisateur ~/.omo/lsp-client.json, puis ~/.pi/lsp-client.json.
  Le skill fourni decrit des chemins differents pour cet hote.
- Cree %USERPROFILE%/.omo/lsp-client.json apres avoir verifie son absence,
  avec lua-ls.command absolu et extensions .lua. Aucune installation refaite.
- PASS reel : lsp_diagnostics(
  filePath="%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/scripts/check-craft-plan-readonly.lua",
  severity="all") retourne "No diagnostics found".
- Aucun compte, script de jeu, privilege administrateur, nouvelle relance
  ou processus QA ponctuel necessaire. Le daemon LSP est un service utile,
  pas un processus QA a supprimer.
- La sauvegarde de ce fait en memoire a ete tentee mais reste refusee pour
  le changement preexistant dans reference/givre-fm-parity.md ; cette
  preuve durable reste ici. Aucun changement d'autrui n'a ete commite.
- Now : verification apres relance terminee. Todo de ce suivi : aucun.
