# Socket → contrôle Lua : admission des scripts / reprise (20 octobre 2026)

## Périmètre

Worker-12 : modifications **exclusivement** à
`GivreClone.App/ViewModels/BotsViewModel.Lua.cs`,
`GivreClone.App/Services/BotHostService.cs`, nouveau module pur
`GivreClone.App/Services/SocketLuaControlAdmission.cs` autorisé par
le prime, nouveau test
`GivreClone.Tests/SocketLuaControlTests.cs` et ce document.
Aucun changement à SocketProbe, SocketGameClient, LaunchService,
AccountsViewModel, MainWindow ou Expédition. Aucun gameplay ni compte live,
pas de commit/push.

## Cause exacte démontrée dans les sources

La ligne de compte Socket reçoit un **PID synthétique** dès l'ouverture de
la tentative ; `row.Running=true` et la réception d'une carte serveur
prouvent une connexion observable, **pas** qu'il existe une voie de
commande. `AccountsViewModel.ObserveSocketControl(row)` renvoie par
exemple `observation-only` si le serveur a envoyé une carte mais si
`LaunchService.HasSocketInjector(pid)` est faux.

Avant cette correction :

- `RunLua()` pouvait appeler `GivreLuaHost.Start` après seulement
  `ScriptStartBlockedByReconnect`, sans vérifier l'injecteur Socket.
  Il marquait ensuite `BotScripts.Set(..., true)` et `LuaRunning=true`,
  confondant lancement local de la tâche Lua et capacité de piloter le jeu.
- `OnScriptControlRequested(... start:true)` présentait aussi `ok=true`
  et `running=true` sans ce prérequis.
- `DriveSocketResume()` déclenchait `Restart()` à la première carte
  d'une nouvelle session ; or la publication de l'injecteur peut suivre
  la carte. Il supprimait alors son état d'attente avant que les commandes
  ne deviennent possibles.
- `BotHostService.IsRunning` testait seulement la présence de l'objet
  `_host`, même si son composant `Capture` s'était déjà arrêté. Ce
  service n'est **pas** l'hôte Lua Socket ; il héberge le moteur headless
  protégé, et ne constitue donc pas la cause principale du refus de
  mouvements Socket. Son état a néanmoins été corrigé dans le périmètre.

## Correctifs

Le composant pur `SocketLuaControlAdmission.StartBlockReason`, dans
`SocketLuaControlAdmission.cs`, exige **pour Socket uniquement** :

1. Compte actif, ni parqué, ni en backoff, ni reconnectant, ni en attente
   d'une nouvelle session ;
2. Carte serveur connue depuis la session courante ;
3. Observation du contrôle de session `State == "channel-registered"`,
   avec `InjectorRegistered=true`.

Toute autre situation retourne une raison courte et factuelle, sans
révéler identifiant, IP, secret ni contenu Lua. **MITM est inchangé**.
`RunLua` et `START_SCRIPT` distant appellent la même politique. Une
demande refusée n'appelle pas `host.Start` et ne modifie pas la télémétrie
du script ; `START_SCRIPT` renvoie un refus distinct de la réussite
du lancement local. Les états locaux `LuaRunning` et `BotScripts`
prennent désormais `host.Running` après `Start`, et non `true` forcé.
`STOP_SCRIPT`, `StopLua` et le killswitch annulent aussi la reprise
automatique Socket précédemment mémorisée.

`DriveSocketResume` attend désormais à la fois la **nouvelle génération
de session**, l'absence de coupure, la carte reçue **et** la publication
du canal de contrôle enregistré. Il ne consomme pas `_socketWaiting`
ni `_socketResume` quand seule la carte est visible.

`BotHostService.IsRunning` mesure le véritable `Capture.IsRunning`.
Une tentative de `StartCore` ayant alloué un hôte dont la capture n'a
pas démarré appelle `Stop()` avant de sortir. Une instance inerte
éventuellement retenue est stoppée avant une nouvelle tentative gardée.
L'autorisation `MZP1OwnedClientPermit`, les factories gardées, le port
de test dédié et le store isolé restent **obligatoires**.

## Limites explicites

`channel-registered` signifie qu'une fonction d'injection est
enregistrée pour ce PID. **Cela ne prouve ni sa possibilité d'écrire à
cet instant, ni l'ACK d'un déplacement, combat, récolte, expédition ou
script entier.** Le transport Socket doit confirmer chaque opération
par la réponse serveur appropriée. Un script peut se terminer tout seul
après un lancement local ; le moteur et la télémétrie distinguent cet
état de l'admission du canal.

Les chemins Lua des autres fichiers (`BotsViewModel.Planning.cs`,
`AutoStart.cs`, `Team.cs`, `SnowAccounts.cs` et
`SupervisorOrders.cs`) appellent aussi `GetOrCreateHost(...).Start`.
**Ils sont hors OWN** : cette livraison corrige les admissions du bouton
Lua, du web START_SCRIPT et de la reprise Socket, **pas** tous ces
points d'entrée. Le prime doit reprendre la même politique avant chacun
de ces lancements. Ni `GetOrCreateHost` ni les fonctions de transport
ne sont modifiés pour lever une exception asynchrone non gérée.

## Contrat de tests

Nouveau `GivreClone.Tests/SocketLuaControlTests.cs` teste la politique
pure avec scénarios **offline** : MITM préservé, canal enregistré sur carte,
carte seule, injecteur manquant, licence / mapping indisponibles, attente,
backoff, session parquée, session déconnectée, état inconnu, refus en
absence d'identité de session. Pour le compiler dans le projet de test
isolé, le prime possède le `GivreClone.Tests.csproj` et peut y ajouter :

```xml
<Compile Include="..\GivreClone.App\Services\SocketLuaControlAdmission.cs" Link="Linked\SocketLuaControlAdmission.cs" />
```

Le prime a ajouté ce lien au projet de tests partagé (pas modifié par ce
worker). Commandes de validation :

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SocketLuaControlTests
dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore
```

**Résultats vérifiés** :
`dotnet test ... --filter FullyQualifiedName~SocketLuaControlTests` :
**26 réussis, 0 échec, 0 ignoré** en Release.
`dotnet build GivreClone.App/GivreClone.App.csproj -c Release
--no-restore -clp:ErrorsOnly` : **PASS, 0 avertissement, 0 erreur**
(build incrémental final). `git diff --check` sur les deux fichiers
existants OWN : **PASS**.

Ne lancer aucun compte ou client pour ces tests : ils ne nécessitent
ni communication réseau, ni injection, ni simulacre d'ACK serveur.
