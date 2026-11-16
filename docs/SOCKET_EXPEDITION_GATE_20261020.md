# Socket / Expédition — audit du gate, preuves et limites

Date : 20 octobre 2026. Périmètre worker-15. **Aucun client Dofus ni réseau gameplay utilisé.**

## Diagnostic sourcé

Vrais points d'entrée :

1. `GivreClone.App/Views/BotsView.axaml` : bouton « Lancer l'expédition » lié à `ExpeditionStartCommand`.
2. `GivreClone.App/ViewModels/BotsViewModel.Expedition.cs` : `ExpeditionStart()` valide simplement `SelectedBot.Pid > 0`, construit un `ExpeditionProfile` ou `DungeonProfile`, appelle `ExpeditionScript.Build(...)` / `.BuildDungeon(...)`, puis `RunLua()`.
3. `GivreClone.App/ViewModels/BotsViewModel.Lua.cs` : `RunLua()` possède ses **propres** refus (licence, follower, reconnexion, cap de niveau, syntaxe). Il est `void`; l'ancien `ExpeditionStart()` annonçait « lancée » **avant** cet appel, sans observation de l'hôte.
4. `GivreClone.App/ViewModels/BotsViewModel.Reconnect.cs` : `ScriptStartBlockedByReconnect()` refuse le socket en attente, parqué ou en backoff. `BotsViewModel.Lua.cs::DriveSocketResume()` attend une **nouvelle** séquence de session et une carte effective avant de reprendre un ancien script. Le booléen `AccountRow.Running` ne signifie pas « session de jeu active ».
5. `GivreClone.App/Services/LaunchService.cs` : les sessions Socket ont un **PID synthétique** `>= 0x40000000`. `RegisterSocketInjector` enregistre une sortie réseau sur ce PID ; `DispatchTransport.Inject` route directement vers cette fonction si elle est active. Si une session socket n'a pas d'injecteur, la forge renvoie `false` **sans repli MITM**. Il n'existe pas de besoin général d'injecter du code dans un vrai processus `Dofus.exe` pour faire tourner le moteur Lua Socket.
6. `LaunchService.Scripting.cs::CurrentMap(pid)` donne seulement une carte décodée et reçue ; `IsLinkDown` marque explicitement les liens coupés. Le service `GameData` (`LaunchService.LotM.cs`) lit une base **locale** commune aux deux transports. Ne pas confondre le catalogue offline et les événements reçus de la session réelle.
7. `GivreClone.App/Services/ExpeditionScript.cs` génère des appels à `travelTo`, `fight`, `map:forceFight`, `getCountFight`, `getPodsPercent`, `bank`, `sale`, `useItem` selon les options. Ces appels dépendent des capacités du moteur et de la **réception des données et confirmations serveur** ; la génération Lua ne prouve pas qu'un déplacement ou un combat peut démarrer.

## Bugs prouvés et correctifs dans le périmètre

### A. « Expédition lancée » alors que `RunLua()` a refusé

Avant, le VM changeait immédiatement `ExpStatus` en « expédition lancée », lançait le suivi d'anomalie et sauvait le profil, puis `RunLua()` pouvait revenir sans démarrer un hôte en raison d'un gate distinct. Ce diagnostic est démontré par le contrôle de flux (sans intervention dans le client).

Après, en **Socket seulement**, `ExpeditionStart()` reconnaît ce mode par `AccountRow.IsSocketSession` **ou par le PID synthétique** `LaunchService.IsSocketPid(pid)`, afin qu'un drapeau de compte transitoirement périmé ne fasse jamais emprunter la branche MITM. Il passe une photographie d'état à `ExpeditionSocketSessionGate` : session `Running`, pas parquée/backoff/reconnexion/en attente de nouvelle session, lien non coupé, `CurrentMap(pid) > 0`, **injecteur Socket enregistré pour le même PID synthétique** (`LaunchService.HasSocketInjector(pid)`, ajouté indépendamment par le prime), licence autorisant le botting, niveau dans le cap, pas de verrou follower, et droit de combat. Le message `ExpStatus` nomme le prérequis manquant. Le VM interroge également le diagnostic `AccountsViewModel.ObserveSocketControl(row)` fourni par worker-19 (lecture seule, **aucune modification** de ce module) : `mapping-unavailable`, `awaiting-map`, `observation-only`, `suspended`, `license-blocked` ou `botting-paused` sont refusés explicitement ; seul `channel-registered` permet de tenter de démarrer Lua. Le VM construit son script seulement après ces préflights. Suite à `RunLua()`, le VM ne décrit comme « hôte Lua démarré » que l'observation locale `_luaHosts[pid].Running` ; le profil et le suivi de l'anomalie ne sont enregistrés qu'après cela. **Ce n'est jamais un ACK du serveur.**

Le chemin de lancement **MITM est préservé tel quel** (même ordre des actions, même texte de statut, aucune nouvelle règle Socket appliquée).

### B. Onglet vide rendu définitivement « chargé » quand `GameData` manquait

Avant, `ExpeditionOpen()` positionnait `_expLoaded=true` avant de chercher la base offline. Si celle-ci était absente, l'onglet restait vide sans même une possibilité d'un nouvel essai du module. Maintenant `_expLoaded` devient vrai seulement après le chargement des zones ; l'état annonce que la base GameData est locale, **pas** propre à Socket. **Limite amont :** le getter `LaunchService.GameData` retient `_gameDbTried` après un premier échec ; la récupération effective d'un dossier GameData ajouté en cours d'exécution exigerait un changement dans ce service (hors OWN), ou un redémarrage. Ne pas prétendre ici que l'ajout tardif est pris en charge.

### C. Préflight testable, sans réseau

Nouveau module pur `GivreClone.App/Services/Expedition/ExpeditionSocketSessionGate.cs` :

`ExpeditionSocketSessionSnapshot(...)` + `ExpeditionSocketSessionGate.StartBlockReason(...)`, dans le fichier pur `Services/ExpeditionScript.cs` déjà référencé par le projet de tests (aucune modification du `.csproj`).

La méthode ne possède aucun port réseau ou méthode d'injection : elle retourne un motif de refus ou `null` pour « préconditions connues satisfaites ». `null` n'est pas une promesse d'injecteur vivant, de succès, de mouvement, de combat ou de réception serveur. Tests offline négatifs dans `GivreClone.Tests/ExpeditionSocketSessionGateTests.cs` : MITM jamais filtré, carte inconnue, état parqué/backoff/reconnexion, lien coupé malgré carte périmée, injecteur absent, absence de droit Fight, hors ligne, licence/cap, follower, session Socket prête **sans simulation d'ACK**.

## Limites à laisser ouvertes, sans faux « PASS gameplay »

- La présence d'une carte et d'un injecteur enregistré ne prouve pas que le socket est encore vivant à l'instant d'un envoi, ni que chaque requête de mouvement/combat possède un mapping et un framing corrects. Si les actions échouent, inspecter dans leurs modules respectifs le résultat de `DispatchTransport.Inject` et les **événements serveur reçus**, sans forger d'accusé.
- Le moteur Lua démarre localement, mais la fonction `travelTo` peut échouer faute d'atlas/transitions vérifiés, la recherche de groupes peut dépendre d'une carte et de groupes décodés, et `fight` requiert le droit, le message de départ réel, puis les messages de combat.
- Donjons : les clés et le contenu du sac demandent des observations serveur. Une base `GameData` offline ne confirme jamais la présence réelle des clés ou les données de combat en jeu.
- Ce lot ne modifie **ni** `SocketProbe`, `SocketGameClient`, `LaunchService`, `Socket mapping`, `MainWindow`, `AccountsViewModel`, `MappingCatalog`, **ni** le runtime Lua global. Les pannes de ces couches sont tracées mais non masquées dans le module Expéditions.

## Commandes de validation

```powershell
dotnet build GivreClone.App/GivreClone.App.csproj --no-restore --no-dependencies -c Debug -v:q -clp:ErrorsOnly -p:DebugSymbols=false -p:DebugType=None
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --no-restore -c Debug --filter 'FullyQualifiedName~Expedition' --logger 'console;verbosity=minimal'
git diff --check -- GivreClone.App/ViewModels/BotsViewModel.Expedition.cs
```

Observé : **build App 0 erreur / 198 avertissements existants** avec symboles de débogage désactivés, **tests Expédition 32/32 PASS**, `git diff --check` sans erreur. Une compilation Debug ordinaire a rencontré un **verrou `GivreClone.pdb` par un processus .NET concurrent**, non un défaut du code. Aucune tentative de fermeture de processus n'a été faite. Le premier essai de nouveaux tests échouait car le projet lie explicitement certains fichiers source ; le gate pur a été déplacé dans `ExpeditionScript.cs`, déjà lié au projet, ce qui a corrigé l'échec **sans aucune modification de csproj**.

Pas de commit/push, aucune connexion en jeu, aucun ACK ou résultat runtime certifié.
