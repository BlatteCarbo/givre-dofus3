# Ultrawork Notepad - Givre Enhanced expansion
Started: 2026-10-31

## Plan
1. Discover current sessions, uncommitted files, installed game/mods and existing switching/overlay/breeding seams.
2. Research all eight supplied repositories and recent relevant GitHub projects in parallel read-only lanes.
3. Resolve module boundaries and dependency order from evidence with a planning child.
4. Install and verify Enhanced against installed game; preserve original artifacts and document rollback.
5. Implement character/server switching with busy/cancel handling and real session proof.
6. Implement Socket-to-visible-MITM transition without promising a hot transfer where reconnect is necessary.
7. Inspect game FM/hunt/HDV/cosmetics/breeding UI and capture reference interactions.
8. Implement native UI refactor and remove only demonstrated redundant controls.
9. Implement breeding support using observed state, with no invented protocol fields.
10. Implement in-game FM advice bridge from explicit bot objectives, item/rune state and unknown-data guard.
11. Evaluate other mods and integrate selected useful lightweight additions.
12. Run related tests and real-surface QA per increment; final suite, build, resource checks and self-review.
13. Clean owned caches/processes/watchers, retain user app/game/worktree and evidence.
14. Report all implemented outcomes, evidence, limitations and authorized atomic commits.

## Success criteria + QA scenarios
HEAVY: session-handling changes, external mod integration, cross-domain UI and breeding.
Ideal end state: one coherent Givre experience with visible game control, explicit character/server identity, useful breeding and in-game FM assistance, without added idle bloat.
Detailed executable scenarios will be fixed from discovery before each implementation; not PASSED until command/action, binary result and screenshot/state evidence are recorded.
Enhanced: installed hash/version, launch game mod menu screenshot; disable/rollback path.
Switching: select actual observed character/server, execute, compare MCP identity and native screenshot; busy/cancel preserves current session.
Socket visibility: invoke actual UI command, verify linked window/account and no duplicate ownership.
UI: native wide/compact screenshots of FM/hunt/HDV/cosmetics/breeding; no blank or clipped view.
FM: objective EXO PA -> observed next-rune advice visible in game; changed item refreshes; unknown state gives no recommendation.
Research: all eight repositories + recent projects evaluated with primary-source evidence and license/maintenance/fit.
Verification: existing tests and full final checks, resource measurements, owned cleanup receipts.
Stop when every requested outcome is observable, checks recorded, owned resources cleaned and final report supplied.

## Now
Bootstrap discovery and repository research.

## Todo
Mirror every plan item in live checklist; preserve previous cleanup obligations.

## Findings
Current shared worktree: %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024.
Prior work has uncommitted FM/performance/appearance changes. Do not revert or bundle them blindly.
Goal registration attempted with complete new scope; tool refused because historic unfinished goal exists (blocked user interruption). No false completion of old goal.
Root AGENTS.md not found. First reverse skill lookup used wrong path and returned ENOENT; correct path is %USERPROFILE%/.agents/skills/reverse-engineer-anything/SKILL.md.
Existing files include LaunchService.Character, CharacterPick, SocketCharacterSelectionGate, AccountsViewModel.ServerPick, IMitmRelay and MitmTransport.
Previous memory write failed due third-party uncommitted reference/givre-fm-parity.md; preserve it. User says current Boite d'Enutrof is daily; no repeated same-day attempt.

## Skills and delegation topology
bun-1-4: execution and temporary-resource builtins. git-master: history and authorized atomic commits.
codebase-design: session/mod integration seam. reverse-engineer-anything: actual client/mod evidence and UI mapping.
computer-use + visual-qa + frontend: parent real GUI proof and native layout changes.
refactor: removal/reorganization only after inventory. programming: any script or TypeScript bridge work.
memory-discipline: durable project decisions, no invented user receipts.
research: delegated repository evaluation. browser: only if interactive browser required.
Parallel background read-only research lanes: Enhanced/FM mod feasibility; supplied companion projects/recent GitHub.
Parent: current code/session discovery, installation, integration and real QA.
Planner: only after discovery returns, resolve open session/mod boundaries and execution order.
No ulw-plan requested: no plan-reviewer gate; record self-review unless separately requested.

## Resources
RUN_TMP=%USERPROFILE%/AppData/Local/Temp/givre-enhanced-20261031-01a18bff
Ownership marker created via apply_patch before commands. TMPDIR/TEMP/TMP, NODE_COMPILE_CACHE and BUN_RUNTIME_TRANSPILER_CACHE_PATH set in eval.
Each child uses own subdirectory with marker; cleanup required after child exits.
Shared user game/profile/worktree must remain.

## Learnings
The daily chest refusal is not evidence of a hunt protocol failure.
Actual incoming mounted look proves colors field5; public old renderer still drops rider, so avoid claiming appearance complete.

## Discovery wave receipts
Git status/history read exit0 with GIT_MASTER=1. Numerous old modified files,
including FM views/services/tests and mapping; preserve all.
History uses French descriptive subjects, latest89fcadd,2f1ed6a,5aeaaa2.
Existing SwitchMode in BotsViewModel.CombatExtras.cs:50 only toggles mode
when row.Running=false. This is not a live Socket-to-window transition.
AccountsViewModel.Launch.cs:48 manual launcher forces MITM; :168 reconnect
can target character; :193 Socket reconnect stays same synthetic PID;
:243 MITM relaunch keeps bot entry. ServerPick already handles startup
server/character modal. CharacterPick pure exact-name/highest-level helper.
LaunchService.AutoPick uses generation-owned requests but a25s UI delay;
reuse identity/cancel semantics, do not infer safe direct server switching.
MZP25NativeModCatalog owns hashes of host/bootstrap and optional native
mods. ModControl per-PID file polling500ms exists; do not add global
polling without need. native/GivreCombatVisuals README contradicts old
summary: it now says ABI8 built/wired, live visual proof still absent.
Actual dll exists167424bytes. Installed game root contains no proxy
version.dll or loader/mod directory; do not infer Enhanced installed.
Installed version read:3.7.12.12, build2026-10-19T15:11:45+00:00.
Computer capability win32 granted, stopPath global. GamePID10496,
GivrePID22172. First game capture black map but controls and victory
summary present; second capture actual active Gelaxiem dimension fight.
Shift+C sent once with background window input; no cosmetics opened while
fight became active, so scenario NOT PASS. No further game input until
combat-safe state; do not interfere with user's current dungeon.
Research children st_01a19355 Enhanced/FM and st_01a19356 otherrepos
running; architecture child st_01a19359 read-only with above sourcefacts.
Parent retains installation/session/UIQA. Pending research not assumed.
Memory durable-save reminder handled by project notepad because memory
repo has known thirdparty dirty parity file; do not reset/commit it.

## Enhanced release evidence
Official GitHub releases/latest HTTP200 read via fetch on31October.
tagv1.20.2, published2026-10-27T09:06:17Z. One release ZIP2285967bytes,
DofusMiniatures-r126 with expected GameAssembly hash70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663
and metadata hashba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6
in filename. Source https://api.github.com/repos/Altansar69/dofus-enhanced/releases/latest.
Release notes:3.7.12.12 native calls checked, offline tests only, live
rendering explicitly unconfirmed by author. Parent must verify actual
installation and rendering, not infer it from release.
Research child initially inspected wrong Downloads/givrework path;
steered to exact .t3/worktrees path, no shared changes. Its findings on
existing CombatVisuals integration pending corrected-path inspection.

## Research integration
Child st_01a19356 completed. Full report copied via apply_patch to
docs/codex/DOFUS_GITHUB_RESEARCH_20261031.md before cleanup.
All seven supplied companion repos and six recent discoveries evaluated.
Monture Minute MIT stageengine chosen as breeding reference, D3B MIT as
native Avalonia UI reference; Astrub Companion MIT as HDV parser reference.
Prospection behavior reference only (no source license/non-distribution).
Avoid extra Electron, OCRloop or Npcapservice for data Givre already sees.
MagusWin is documented nonfunctional stub; Skinator is Retro, not UnityD3.
Nine research checklist items closed only after terminal result integrated.
Enhanced/miniatures and architecture remain pending, not inferred complete.

## Scope addition from user
Screenshot %USERPROFILE%/Pictures/Screenshots/Capture d'écran 2026-10-31 200648.png
shows enabled combat spectator lock and team combat lock checkboxes.
User reports many functions broken, requests recovery from own old project
%USERPROFILE%/Downloads/givre-GivreActive/Videos/GIVRE/GivreActive :
automatic achievement acceptance, near/far placement and whether to approach,
Socket/MITM transition, interface/features inventory beyond those examples.
Preserve current Enhanced scope; add oldproject parity/bug diagnosis lane.
No assertion that screenshot alone proves a network failure.

## Current feature parity facts
CombatProfile.cs already has per-spell AllowApproach, Style and kite
distance bands. BotSettings.AchievementAutoClaim defaults true,
BotsViewModel.Tick pushes it to LaunchService.SetAutoClaimSetting;
LotM has automatic and explicit confirmation paths. Fight locks merge
script/profile and are applied during placement. These are sourcefacts,
not proof of runtime success; diagnose missing effect with packet/state
evidence instead of duplicate options.
diagnosing-bugs skill loaded for reported failures; regression feedback
loop required before rootcause claim or fix.
Oldproject parity child st_01a19361 owns read-only comparison.
Enhanced report preserved docs/codex/ENHANCED_FEASIBILITY_20261031.md.
Its terminal evidence identifies exact overlapping Compositor.Update
RVA0x20F9080; checkbox-off does not remove detours. Need exclusive
provider ownership before activating full Enhanced.

## Portable preparation receipt
Parent downloaded official ZIP with Invoke-WebRequest and checked SHA256,
inspected nine safe relative entries then Expand-Archive, exit0
(monitor mon_ZEHPQD8P6SR0HV7H). No loader/updater executed.
Installed files:
%USERPROFILE%/AppData/Local/GivreReplica/Tools/DofusEnhanced/r126/DofusMiniatures-3.7.11.11-r126/
Parent read release.ini: clientVersion3.7.12.12 revision126.
Parent independently hashed extracted DLL21e7e4b7d81270c9aee222a50a6e93a6a94b7b5a73463f7b8fa7b74a47e21212
and loader40b1bd9d590cdcc9c67de0ac117d0c349b1f9e1f3771282b25faeb6c68b285e3,
both match manifest. Portable preparation only; activation/UI criterion
not complete. No gamefolder overwrite or autoload registry change.
Retain intended installed tool directory; cleanup only QA ZIP/run caches.

## Bestiary contextual farming scope
User requests a game mod action from bestiary (right-click or equivalent):
choose desired quantity and farm it. Add verified native UI binding,
explicit resource/monster identity, quantity validation, owned-stock
accounting and stop condition, then connect to existing farming runner.
Do not infer arbitrary bestiary context-menu API from Enhanced outlines.
Candidate adjacent contextual flows: recipe -> missing ingredients,
item -> FM objective. Ideas are proposals until individually implemented.
QA must use an actual bestiary selection and bounded target, assert
correct identity/count and no continued farming after target/cancel.

## Automation interaction contract
User clarifies: « si t'as des idées hesite pas, le but est que tout soit
automatisé via sois direct un bouton, ou des reglages + bouton ».
Primary workflows must execute end-to-end from one action with defaults,
or explicit bounded settings followed by Start. A generated script,
comparison-only panel or advice-only result is not completion of an
automation workflow. Show live progress/remaining target and Stop.
Keep the originally requested FM advice mode, but also connect existing
automatic forge execution where selected rather than requiring manual
copying between the advice UI and bot. Distinguish mode in UI.
This is current-session scope, not permission inferred from memory.

## Live provider probe
Read-only Get-Process10496.Modules exit0: GivreKama, GivreTurboBootstrap,
DofusNativeHost, GivreCombatVisuals and GivreItemIdOverlay actually loaded.
Paths are this Release build and isolated live userdata/NativeTurbo/10496.
Windows VERSION.dll is SYSTEM32, not a gamefolder proxy.
Concrete activation condition currently fails: CombatVisuals IS resident.
Do not inject Enhanced merely because its package hashes match.
Need unload/selection ownership before interactive activation, retaining
the independent item-ID overlay if compatible with chosen runtime.

## Additional repositories from user
Evaluate https://github.com/ix-xs/metamob.api ,
https://github.com/vDAKK/stakk-open-source ,
https://github.com/Fronne/Dofus-Craft ,
https://github.com/Valentin-alix/BillBot .
Keep same evidence standard: actual source behavior, license, exact
freshness, integration/idlecost, concrete one-button workflow candidate.
Do not silently treat these links as authorization for remote uploads,
publishing, messaging or account writes beyond the requested local bot.

## Execution design accepted
Architect st_01a19359 terminal. Givre remains session/objective/execution
owner. No second capture stack. Order:
0 preserve dirtybaseline/contracts; 1 exclusive nativeprovider; 2 explicit
session transition with sourcegeneration and awaitedSocketteardown;
3 visibleMITM by verifiedreconnect (hotmirror is distinct laterparity);
4 activate Enhanced only after1; 5 observation-only FMprojection plus
existingautomaticrunner; 6 singleeventdriven nativebridge afterUIbindings;
7 licensedbreedingstageengine thenobservedactions; 8 coherentnativeUI;
9 targetedcombatparity; 10 boundedbestiary/ingredientobjectives; 11 QA.

Parallel scopes:
S sessions: AccountsViewModel core/Launch/Socket/ServerPick, Bots reconnect
partials, SocketConnectionClient/SocketProbe, CharacterSelectionQueue and
new sessiontransitiontests. BotsViewModel.CombatExtras parentonly.
B breeding: new Engine/Breeding, Services/Breeding, BreedingViewModel,
domain tests. No invented protocolreader untilcapture.
F FM: new Services/ForgeAdvice, SmithMagicViewModel.Advice, surgical
SmithMagicViewModel.cs/Editor integration andadvice tests. Preserveoldhunks.
P parent: NativeTurboRuntime, nativeproviderpolicy, sharedwiring, LotM
receiptcorrelation, actualinstallation andallGUIQA. UIchanges integrated
aftercontracts stable. No parallel edits to projectfiles orviews.
One build/test owner parent to avoid sharedbin/obj races.

Scenario invocations fixed for firstincrements:
Achievement: dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Debug
--filter FullyQualifiedName~Achievement_claim_ignores_another_reward_before_its_own_rejection.
PASS=false matchingreceipt afterunrelatedsuccess, oneinjection, waiterremoved;
RED first, laterGREEN andnativeclaimreceipt QA.
Provider: Get-Process -Id10496 .Modules beforeactivation, then Enhanced
loaderinteractive onlyafter source/providerexclusion; PASS nooverlapping
CombatVisuals/skipper loaded/reactivated and Enhancedoptionsvisible.
UI: computer.window({app:'Dofus.exe'}).press('shift+c') outsidecombat,
screenshot actualcosmeticspanel, closewithoutapplyingequipment.
Fullchecks parent: dotnet test Tests and UiTests Debug --no-restore -m:1,
dotnet build App Release --no-restore -m:1, exit0 required.

New report docs/codex/GIVREACTIVE_PARITY_20261031.md preserves readonly
oldsourcecomparison. Explicitachievementwaiter canacceptanotherID,
independentplacementmissing, per-spellapproachdoesnotgovernendturn,
oldmirrorprotocolconditional. Do not copyoldregenfakeHPassignment.

## Tactical and Frigost scope correction
User adds broken tactical mode reference %USERPROFILE%/Downloads/mode tactique.avif
and asks to inspect https://doc.frigost.dev/ possibilities. Then clarifies
« pas en priorité, le but est simplement que tu fasses tout très bien ».
Keep tactical in the integrated quality lane, not a reprioritization.
AVIF read tool returned binarytext, not viewed; convert with supported
image decoder before claiming its contents. Frigost official root fetched,
points to llms.txt and .md pages. API catalog research remains pending.

## AnkaBot reference
User adds https://doc.ankabot.dev/ to missingfeature/ideas comparison.
Official root read points to llms.txt and markdown pages. Compare actual
currentimplementation, not just same-namedmethod; distinguish requested
automationgaps fromoptionalideas anddo notpromiseentireexternalproducts.
TacticalAVIF converted using Bun.Image to RUN_TMP/tactical-reference.png
and viewed: blackbackground withbluegreencellgridanddarkraisedobstacles,
entities/portalsretained. This isexpectedvisualreference, notcurrentbugproof.


## Reprise 31 octobre apres limite ChatGPT
Utilisateur demande les agents sur Cursor Grok 4.7 xhigh fast. Lecture seule:
task subagent_type + model cursor/grok-4.7-xhigh-fast. Edition: cursor-agent.ps1
--model grok-4.7-xhigh-fast (les categories refusent un modele).
st_01a1936f (sessions) et st_01a19356 (Frigost/AnkaBot) stoppes par la limite.
Audit docs relance: st_01a1937d (Frigost, AnkaBot, DoCraft, ExoFast).
Sources sessions partielles presentes, compilation non prouvee:
mon_XD9MTEM7HRE6X2JH / bash_214, --artifacts-path RUN_TMP/art, log RUN_TMP/iso-test.log.
Memoire: ecriture refusee, reference/givre-fm-parity.md non commis et etranger, non touche.
Etat live: PID10496 hors combat, Puits des Songes; CombatVisuals toujours charge,
Enhanced non active. App 22172 = ancien binaire, sans PrepareForEnhancedAsync prouve.


## Autorisations utilisateur, suite
Utilisateur autorise: capture de decouverte (diagnostic) et clics MITM par computer use pour mapper si necessaire.
Utilisateur demande aussi plusieurs agents Grok 4.7 xhigh fast (ou meilleurs via Cursor), amelioration globale, idees proposees ET implementees.
Erreur de ma part corrigee: ce message venait bien de l'utilisateur, pas d'une sortie d'outil.
Capture: LaunchService.Packets.cs:194 exige AppConfig.PacketDump + LicenseService.IsDeveloper; coupure auto apres 15 min (LaunchService.PerfGuards.cs:7). Sortie transport-*.log. Pas encore activee.
Agents lecture seule Grok: bot-improvement-proposals, combat-parity-diagnosis, bestiary-farm-seams, plus audit st_01a1937d.
Agents editeurs seulement apres fin de mon_WQ72XC7618CBVVFF: ne pas modifier les sources pendant une compilation.

Tests isoles sessions/FM/elevage: 59 reussis, 0 echec, %USERPROFILE%/AppData/Local/Temp/givre-enhanced-20261031-01a18bff/iso-test.log (avant branchement UI).
Constat: TransitionSessionAsync, DofusEnhanced.ActivateAsync, advice et BreedingViewModel n'ont aucun appelant UI. Lots non livres tant que non branches.
Reglage "Capture exhaustive" trouve ACTIVE dans l'app 22172 (verbeux), a verifier.
Editeurs Grok CLI lances: session, advice. Prompts et logs: %USERPROFILE%/AppData/Local/Temp/givre-enhanced-20261031-01a18bff/agents/.

Hook plugin Cursor impeccable casse sous Windows: desactive avec accord utilisateur, hooks/hooks.json renomme hooks.json.disabled-by-omo dans ~/.cursor/plugins/cache/cursor-public/{73000484,impeccable}/9dad388.../hooks. Une synchro Cursor peut le restaurer. Lecture CLI retestee OK.
Capture de decouverte activee ~20h53 (coupure auto 15 min). Clic de fond sur Mes tenues sans effet visible. Panneau Cosmetique ouvert par l'utilisateur, capture. Aucun transport-*.log trouve sous LOCALAPPDATA/APPDATA: emplacement a determiner.

Agents Grok via task (provider cursor) s'arretent apres leur preambule, sans rapport, meme relances (st_01a1937d inconclusif). Utiliser le CLI cursor-agent pour tout livrable. Audit docs relance en CLI: %USERPROFILE%/AppData/Local/Temp/givre-enhanced-20261031-01a18bff/agents/docs-report.md.

## Decisions utilisateur (grill, 31 oct)
Q1 combiner Enhanced dans une section Mods de Givre si possible, sinon activation manuelle. Q2 tactique natif sans Enhanced si non combines. Q3 pas de coupure: transfert a chaud Socket->MITM existait dans Givre, corriger le cas combat (ex: deconnecter juste avant la connexion). Q4 mapper le routage serveur MITM. Q5 budget tests 1 000 000 kamas, +1 000 000 pris en banque si besoin. Q6 Costumage deja 60, lot clos. Q7 utilisateur absent, feu vert total: installer, telecharger, outils, plugins, MCP, skills, recherche, reverse. Q8 Metamob a faire; cle dans %LOCALAPPDATA%/GivreReplica/secrets/metamob.key, jamais dans le depot ni les journaux. Envoi de prix HDV: non decide, pas de cle dediee. Q9 sniper delai 0-10000 ms au choix; Kolizeum non prioritaire. Q10 commit et push libres.
Ajouts: zaapi par clic droit sur zaap sans havre-sac (havre-sac reste un choix); bouton rouge porte+fleche pour quitter un donjon, ex donjon Champs, a mapper.

Mapping live 21h26: clics de fond imprecis; controle premier plan accorde, clics precis (options, raccourcis: carte du monde = M, songes infinis = Alt+M). Touches clavier (press et holdKeys) ignorees par Dofus. Curseur deplace par un tiers pendant un move: controle relache. Chat 21:26 "paye 2 085 kamas pour acceder a ce coffre": origine non etablie (mon clic de fond ou l'utilisateur).

Rapports livres: ENHANCED_PORT (fusion apparence dans CombatVisuals, bascule exclusive pour le reste), HOT_MIRROR (rejeu local du snapshot monde, attendre fin de combat). Agents hotmirror et enhappear lances. Zaapi capture: ZAAP_MAP_TELEPORT_CAPTURE.

## Grill 2 (31 oct ~22h)
Q1 profils: afficher ce qui est visible, sinon onglet cache. Q2 canaux: choix des canaux en Socket ET en MITM; l'utilisateur coche/decoche pour le mapping. Q3 bonnes affaires du chat Commerce: alerte seulement, ni achat ni message. Q4 push direct sur la branche. Q5 onglet "Creer un personnage" comme GivreActive, creation a la connexion, en reconnexion ou directe. Q6 budget tests: "2M2" + 1M en banque si besoin (montant a confirmer). Bug overlay ID (recettes/ID dans les listes) a corriger, non prioritaire.

Budget tests precise (22h03): ~1 200 000 kamas en inventaire de Bastionbot + 1 000 000 a retirer en banque si besoin = 2 200 000 au total. Solde de depart ~1 226 080 (zaapi 21:37). Ne pas toucher au reste de la banque.

Journal transport: %LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/logs/transport-<date>.log (session actuelle transport-2026-10-31_18-35-32.log). Utilisateur: test de la chasse seulement apres minuit (coffre quotidien). Tests filtres des lots: 253 reussis 0 echec (agent testfix). Suite complete bloquee par 4 erreurs XAML SocialView (HasItem...), correctif lance.

Inactivite: popup client apres ~36 min sans action (observation utilisateur). Aucun hook natif dans native/. Ancien projet: externalnotificationsdata INACTIVITY_WARNING (alerte cote client); FightTurnFinishRequest a un champ is_afk (champ 2, decompile JitsuriMITM 20260914) = probable "hook true" dont parle l'utilisateur, a verifier dans notre MoveForge (envoyer is_afk=false). Popup dismiss 22:47 par clic premier plan.

Routage agents: si quota Cursor epuise, basculer sur les modeles ChatGPT (categories deep-low/gpt-6.1-sol, deep-high/gpt-6-astra) avec accord utilisateur du 1er nov.

## Etat au 1er nov 09:30 (reprise apres compaction)
Commits pousses: 51bfad3..c061b63 (21 lots), 94e6ddf chasse, 6c9a2ae relance perso, 26efb17 barre+creation, 9fbb0b3 zaap connexion+orientation.
Valide en jeu: changement de perso par relance (2 sens), chasse trajet+Arreter, zaap en un clic (340 kamas), menu natif Changer de perso (survol requis), barre d'actions affichee.
Non valide: anti-inactivite (coupure client ~30 min apres derniere saisie reelle; get_anyKey et orientation inefficaces; resetInactivity echoue), mod tactique (DNM_Load corrige, flood corrige, chargement non confirme), sortie de donjon, zaapi, creation de personnage, HDV/sniper/production/Metamob/social en jeu, chasse quotidienne (Boite d'Enutrof non recoltable).
Non commite: LaunchService.AntiAfk.cs (cadence), natifs ItemIdOverlay 0.4.2 et tactique, epinglages. Agent nativefix3 en cours.
Budget kamas depense en tests: ~1040 (1225260 -> 1224570 + 350). Plafond 2,2 M.
