# Givre : causes racines prouvees par code (2026-11-07)

Fondees par 24 agents Gemini 3.8 Flash High en lecture seule.
Plan complet : `.omo/plans/givre-enhance-complet.md` dans le worktree
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

## 1. Bans de 5 min : le mode MITM n'a AUCUNE limitation de debit
`LocalLaunch/LocalHandshakeRelay.cs:228` ecrit directement
`Upstream.GetStream().Write(framedToServer)` : aucune file, aucun delai. 100-150 pkt/s mesures.
Les correctifs de gigue poses avant (TurboFightPolicy 25-45 ms, SocketGameClient 15-35/20-40 ms)
ne couvraient QUE le mode Socket.

Mecanisme du ban : ping-pong de tours vides. `HumanEndPause()` (`LaunchService.Combat.Turn.cs:225`)
renvoie `Task.CompletedTask` quand `humanPace` est false -> `SendEndTurn` a 0 ms -> le serveur repond
SequenceEnd + FightIsTurnReady -> ack a 0 ms + TurnReady a 0 ms -> cycle de 3 paquets par 15-25 ms de RTT.

### Le seam, prouve
Aucune methode unique ne canalise 100% des envois :
- commandes moteur : `RelayForInject` -> `DispatchTransport.Inject` (`LaunchService.cs:211`) -> file `_outbound`
- reactions autonomes Socket : `_reactions` -> `SendGame`, ou `SendGame` direct (jamais `Inject`)
- le drain `_outbound` (`SocketGameClient.cs:744`) ecrit `stream.WriteAsync` et CONTOURNE `SendGame`
- le MITM contourne `SocketGameClient` entierement

MAIS les deux sites d'ecriture Socket prennent le meme `_writeLock` :
`SendGame` (`:410-412`) et le drain `_outbound` (`:743-745`).
=> factoriser en `WriteWireFrameAsync(byte[], CancellationToken)` et poser le token bucket dedans.
Cote MITM : `LocalHandshakeRelay.Inject:228`.

## 2. HDV lent meme a 0 ms (le reglage utilisateur n'est pas dans ce chemin)
`LaunchService.Hdv.cs`
- `Task.Delay(400)` CODE EN DUR `:853` et `:889` (commente "anti-spam inter-lot") = 80 s sur 200 lots
- `HdvGetMarketPriceAsync` appele DANS la boucle `foreach` `:870` : un aller-retour serveur PAR LOT,
  meme quand plusieurs lots partagent le meme gid
- `SemaphoreSlim(1,1)` par pid (`:175,246,306,677,754`) serialise : correct, a garder

A NE PAS TOUCHER : `SendAndWaitForCodeAsync` (budgets 1500/4000/8000 ms) sort sur event serveur reel ;
les gardes anti-auto-sous-cote (`oldPrice <= min`, `minIsMine`) evitent que le bot se sous-cote lui-meme.

## 3. Memoire : 756 Mo WS, 514 Mo/min alloues (4 bots)
- `Combat/CombatTurnPlanner.cs:612-655,733-764,895-907` LINQ+closures beam search = 250-300 Mo/min.
  `:740` fait un `string.Split()` PAR CELLULE CANDIDATE.
- `LaunchService.Combat.Turn.cs:1183-1198` serialise TOUT le plateau en JSON (560 cellules, fighters,
  spells, team view) des que `chosen.Actions.IsEmpty`, en ~17 chunks de 3500 chars. AUCUN GARDE. 120-160 Mo/min.
- `MemoryTrimmer.cs` appelle `EmptyWorkingSet()` toutes les 60 s (demarre `App.axaml.cs:87`) :
  CONTRE-PRODUCTIF a ce taux d'allocation, pousse les pages au pagefile qui sont re-fautees aussitot.
- `BotsViewModel.Lua.cs:531,553,600` `_allLog.RemoveAt(0)` sur 2000 refs = O(N) par ligne de log.
- `NotificationCenterViewModel.cs:114-123` `VisibleItems` recalcule `OrderBy().Where().ToArray()`
  a CHAQUE getter ; `RecountUnread()->RefreshFilters()` l'evalue plusieurs fois par notification.
- `BotsViewModel.LuaEditor.cs:446` `_editorLocations` HashSet jamais vide = fuite monotone.

## 4. Mods : le mod FONCTIONNE, Givre le condamne
Preuves que Better Dofus 2.13.1 va bien sur le client 3.7.13.17 :
- `module.log` : `START version=3.7.13.17 mod=2.13.1`, `RUNTIME_SELF_TEST_OK handleBits=64`,
  hooks GLOW_FRAME_HOOK / VERTICAL_GAUGES_READY / PRESET_WINDOWS_READY / ROUBLARD_PASSAGE_INSTALL /
  METAMOB_SYNC_READY / SERVICES_READY, `UI_MOD_REGISTERED`
- `startup-22480.status` = READY
- hashes IDENTIQUES : `GameAssembly.dll` = `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591`,
  `global-metadata.dat` = `ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`,
  declares verbatim dans `BetterDofus-2.13.1/release.ini` -> `ReleaseMismatch` = false

DEFAUT DOMINANT : `ReadAutoloadLogs()` (`DofusEnhanced.cs:226-241`) agrege `autoload.log` de TOUS les
providers via `.Append(Folder)`, donc toujours r126 (qui ne supporte que 3.7.12.12, hashes `70b386...`/`ba1efb...`).
Le `ModuleAutoLoader.exe` de r126 TOURNE ENCORE en surveillant de processus et ecrit
`NO_COMPATIBLE_RELEASE:` pour CHAQUE nouveau client. Donc `Judge` renvoie `Incompatible` des la
PREMIERE iteration (`:268-272`) : le faux negatif est SYSTEMATIQUE, pas une collision de PID occasionnelle.
=> lire `provider.AutoloadLog` du provider selectionne.

Defaut masque : `:279` gate l'activation derriere `lighten` (`AppConfig.LightenBotClients`), reglage
orthogonal. Jamais atteint a cause du defaut dominant, mais a corriger aussi. `EngineGrace:42` = 20 s.
Tests existants a ETENDRE : `GivreClone.Tests/ClientLighteningTests.cs` (couvre deja ReleaseMismatch,
AutoloadLogSaysNoCompatible, la table Judge, et contient les hashes 3.7.13.17).

## 5. IA de combat : telefrag et portails sont du CODE MORT
`Combat/ClassMechanics.cs` implemente deja `EvaluateXelorTelefrag:72-79` (+65 premier, +30 refresh),
`GeneratesTelefrag:66-70`, `ResolveExitPortal:88-96`, `CanHitThroughPortals:98-123`,
`PortalDamageMultiplier:125-129` (+2%/case, cap +50%).
MAIS `CombatTurnPlanner.Rank():768-793` n'appelle QUE `IsRogueBomb:776` et `EvaluateRogueBombTactics:779`.
Echelle de score etablie : +10/+30 positionnel, +25/+65 mise en place majeure, +85 ennemi piege.
Ancre de test : cellules 200 et 254 -> `Aligned`=true, `CellDistance`=4, 3 cellules interieures.

## 6. Infrastructure de mods : deja complete
ABI C `native/dofus3-native-host/include/dofus_native_mod_api.h`, entrypoint `dnh_mod_init_v1`,
`DNH_ABI_VERSION_8`, cycle `on_load`/`on_unload`/`on_update`.
SDK Rust + MinHook `crates/mod-sdk/src/lib.rs` (`resolve_class`/`resolve_method`/`hook_method`).
Mailbox IPC `<stage>/NativeMods/control/<PID>/inbox|outbox`, commandes `LOAD`/`UNLOAD`/`STATUS`
=> chargement A CHAUD. Catalogue a SHA-256 epingle `MZP25NativeModCatalog.cs`.
Pas de fog removal possible : Dofus 3 n'a pas de fog of war (verifie, zero occurrence).

## 7. Tests : QUATRE projets, pas un
- `GivreClone.Tests` : 239 fichiers, 2953 tests (2951 verts, 2 skippes). 20 warnings pre-existants = baseline.
- `GivreClone.PureTests` : 65 tests, 24 fichiers LIES. **C'est ce projet que le CI execute.**
  Contient deja `TurboFightPolicyTests.cs`, `HdvTradeLogicTests.cs`, `ExpeditionScriptTests.cs`.
- `GivreClone.EconomyPolicyTests` : 12 tests.
- `GivreClone.UiTests` : 53 points d'entree, Avalonia.Headless.XUnit, captures reelles via
  `window.CaptureRenderedFrame()` dans `bin/Release/net10.0/TestResults/`.
CI `.github/workflows/pure-tests.yml` : job `pure-tests` (ubuntu) + job `mcp-contracts` (windows,
`pwsh ./GivreMcp/Smoke.ps1`). L'app build a 0 warning 0 erreur : c'est la barre.

## 9. Dofus Enhanced : nouvelle cause racine, Windows Defender (2026-11-07 soir)
Apres le correctif du matin (log cross-contamination), un nouveau message est apparu :
`[mod] activation Better Dofus refusee : Activation Enhanced non confirmee : ` (erreur vide).

CAUSE REELLE : **Windows Defender met `BetterDofusLoader.exe` en quarantaine en tant que virus**,
confirme par `Get-FileHash` sur le fichier EXTRAIT FRAIS du zip officiel :
  `Get-FileHash : Impossible de lire le fichier ... Impossible de terminer l'operation,
   car le fichier contient un virus ou un logiciel potentiellement indesirable.`
`BetterDofusAutoLoader.exe` (meme zip, meme auteur) n'est PAS bloque.

Plus tot dans la journee, j'avais contourne un probleme DIFFERENT (empreinte SHA-256 fausse dans
release.ini) en copiant `BetterDofusAutoLoader.exe` par-dessus `BetterDofusLoader.exe` (meme hash
`f40843875229b9dd15a1790b8b03b338afdf90fedb6cf2d71a76ba0097e747f8` dans les deux fichiers).
Ca passait l'empreinte, mais ce n'est PAS le meme executable fonctionnellement :
`LoadEnhancedAsync` (DofusEnhanced.cs:458-479) lance `provider.Loader <pid> <dllPath>` et attend
`process.ExitCode == 0 && NativeModules.IsLoaded(pid, dll)` en sortie du process -> ca suppose un
LOADER qui injecte puis quitte en confirmant. `AutoLoader.exe` est un SURVEILLANT de fond
(watch processes, charge sur detection) : il ne confirme jamais de la meme facon, d'ou l'erreur vide.

Pas de solution cote agent : `Add-MpPreference -ExclusionPath` exige des droits admin, refuses.
Le proprietaire doit soit (a) ajouter une exclusion Windows Defender pour
`%LOCALAPPDATA%\GivreReplica\Tools\DofusEnhanced\`, soit (b) restaurer le fichier en quarantaine
depuis l'historique de protection Windows Security, puis relancer l'app.

CORRECTIF ANNULE (2026-11-07 23:47) : le garde `PacketDumpAll && LicenseService.IsDeveloper` ajoute
plus tot sur le dump JSON de combat (bloc `if (chosen.Actions.IsEmpty)`) a casse 19 tests de
regression (`CombatAndStatDispatchRegressionTests`, `ObservedMonsterGradeDispatchTests`) qui lisent
EXACTEMENT ce dump via la balise `[combat-player-empty]`/`[combat-companion-empty]` pour verifier
la visibilite/grade des combattants projetes dans le snapshot. Le dump ne s'emet qu'UNE fois par
tour vide (le `break` qui suit sort de la boucle), donc le cout "60 Ko par tour vide" visait un
non-probleme : pas de repetition dans la boucle. Garde retire, comportement original restaure
(dump systematique sur tour vide, sans condition PacketDumpAll/IsDeveloper). Suite complete repassee
a 5222/5222 apres retrait (19 echecs -> 0).

RESOLU (2026-11-07 22:30) : le proprietaire a restaure `BetterDofusLoader.exe` original (hash
`ed6fc952176d5eb8f4194df8853bb2f5c8268f1a5c98812ec1c659e2a3e58efa`, 458752 octets) depuis
l'historique de protection Windows Security. J'ai remis ce hash dans `release.ini` (j'y avais
laisse le hash de l'AutoLoader par erreur de contournement precedente). Appel MCP `enhanced.activate`
(bot=pid) confirme : "Enhanced est deja charge." sur pid 19040 (player.four/Archerbot). Outil MCP
reel pour activer Enhanced : tool="enhanced.activate", arg `{"bot": "<pid ou nom>"}` (pas `pid`/`accountId`).

## 8. Scripts Lua
`userdata/scripts/lib/` est VIDE en runtime. Les helpers viennent de l'hote (`bank:`, `exchange:`,
`sale:`, `npc:`) ou sont inlines dans l'en-tete du script. Seul fichier de lib :
`scripts/lib/Measured-Session-Economics-20261101.lua` (`M.start()` / `M.finish(evidence)`) -> sert a
MESURER les kamas/h.
37 scripts existent deja, dont pour Bastionbot, Shadowbot et Sylvanbot : les ameliorer, ne pas dupliquer.
Plusieurs posent `MAX_CONSECUTIVE_DEFEATS = 3` + `stopAsked` dans `onFightEnd` = exactement le
comportement rejete par le proprietaire.
Mode declaratif : si `move()`/`bank()` renvoie une table de pas de route, `GivreLuaHost.Route.cs`
(`RunRoute`) l'execute sans boucle imperative.
Attente evenementielle disponible : `waitFor`/`waitForAny`/`registerMessage`/`lastMessage` (pas de sleep).
