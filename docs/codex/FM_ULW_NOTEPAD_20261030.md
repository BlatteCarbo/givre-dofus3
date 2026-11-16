# Ultrawork Notepad - FM autonome et journal MCP global
Started: 2026-10-30, continuation de la session active.

## Plan (exhaustively detailed)
Tier HEAVY : historique concurrent partage, execution adaptative et integration MCP/UI.
L'objectif ideal : lancer depuis l'application une montee de metier adaptee au
serveur, observer ce que fait le MCP quelle que soit la fonction, comprendre les
depenses et les arrets sans lire les fichiers de transport.

1. Integrer les unites deleguees dans leurs fichiers disjoints : historique et
watchdog (st_01a18e84), economie pure (st_01a18e85), achats supports
(st_01a18e86). Les trois enfants ont termine ; leurs tests ne prouvent pas le live.
2. Parent : raccorder planificateur, budget, histoire des achats, comparaison
sur offres reelles et essai d'un seul objet avant confirmation du reste.
3. Parent : wrapper commun de TOUS les appels MCP, journal borne global visible
dans BotsView, erreurs/annulation/acceptation distinctes, sans code ni secrets.
4. Compiler et executer les tests cibles ; corriger chaque regression introduite.
5. Relancer le build valide sans perdre ni supprimer de personnage, reconnecter
Bastionbot, observer l'UI native avec AX et captures.
6. Tester lectures MCP, rejet cible inconnue et script borne, verifier les
entrees correspondantes dans le journal global de l'application.
7. Choisir Costumage 64 (niveau 1) si les supports accessibles le permettent,
sinon Cordomage 62 ; monter vers 60 sous 200000 kamas, exclusivement avec
le nouveau parcours integre et preuves serveur/historique/captures.
8. Tester comparaison achat/fusion sur les offres de Mikhal, devis seulement ;
essai de brisage selectionne un objet bon marche, puis afficher rendement.
Ne pas briser le reste sans un bouton de confirmation distinct.
9. Suite complete, build Release, autoverification des criteres et journal final.
10. Nettoyer uniquement ressources temporaires possedees ; conserver les preuves
et le worktree partage. Laisser l'application utilisateur utile, session arretee.

Topologie : trois sous-taches deep-low independantes pour les fichiers disjoints
ci-dessus ; parent responsable des contrats, branchements, compilation centralisee
et toutes les preuves live. Pas de nouvelle equipe ni de planificateur necessaire :
les points d'integration sont maintenant connus. Aucun ulw-plan n'a ete demande,
donc autoverification finale, pas de reviewer de plan.

Skills utilises : diagnosing-bugs (repro transport du watchdog), bun-1-4
(kernel et commandes), computer-use (AX et application native), visual-qa
(lisibilite et captures), git-master (etat/diff/historique en lecture seule),
memory-discipline (faits durables et depot memoire bloque).
Programming consulte, mais aucun fichier TS/Python/Rust/Go de production dans ce lot.
Research et reverse non relances : les notes des sites sont deja livrees.

## Success criteria + QA scenarios
C1 Compilation/tests : `dotnet test GivreClone.Tests/GivreClone.Tests.csproj
-c Release --filter "Smith|ForgeHistory|McpAction|RuneAcquisition|Crush"` puis
suite complete et `dotnet build GivreClone.App/GivreClone.App.csproj
-c Release -m:1`. PASS : sorties 0, aucun echec ajoute ; enregistrer les ignores
preexistants, ne pas les masquer. Artefacts : transport des commandes surveillees
et resume dans LIVE_TEST_20261030.md.

C2 Metier : `ctl('start_smith_leveling',{bot:'Bastionbot',jobId:64,targetLevel:60,
maxKamas:200000,maxRunes:10000})`, apres build/reconnexion et controle du niveau.
PASS : niveau 60 ou arret explicite au plafond, depense reelle <=200000,
aucun objet equipe/lie/mauvais type utilise. Captures de Forgemagie et reponse
`read_forge_history`, logs serveur de niveau et solde.
La session doit depasser 180s sans faux arret watchdog ; les refus/resultats
incomplets doivent rester sans NoteProgress dans les regressions.

C3 Journal global : appeler `ctl('get_inventory',{bot:'Bastionbot'})`,
`ctl('get_bot_state',{bot:'unknown'})`, puis un script Lua en lecture seule
borne ; ouvrir l'onglet Console de BotsView via AX et voir les trois entrees.
PASS : cible/outil/heure/etat visibles ; erreur distincte ; accepte ne signifie
jamais action terminee ; corps de script/messages et secrets absents.
Artefacts : capture native + reponses MCP. Tests bornage/erreur/annulation.

C4 Histoire : ouvrir Forgemagie dans l'app et
`ctl('read_forge_history',{bot:'Bastionbot',since:0,limit:200})`.
PASS : meme session et compte, XP connue/inconnue honnete, supports/runes,
depenses et raison d'arret. Capture + reponse.

C5 Economie : ouvrir l'HDV runes via parcours existant, utiliser dans l'UI
CompareRuneGid=1545, CompareRuneQuantity=10, FusionTravelCost=0 seulement si
trajet estime connu nul pour ce devis local ; sinon -1 conserve l'inconnu.
Cliquer CompareRuneCostsCommand. PASS : prix d'offres serveur, routes chiffreables,
devis explicitement sans achat/fusion automatique. Essai de brisage :
un seul UID choisi, quantite envoyee 1, difference sac et runes mesurees ;
aucun reste ni autre objet traite sans confirmation distincte.

WHEN TO STOP : des que C1-C5 passent sur les vraies surfaces, le second metier
a atteint 60 ou le plafond de 200000 avec arret confirme, les preuves et recettes
de nettoyage sont consignees et le bilan final est livre.

## Now
Raccorder le journal global MCP, puis compiler les integrations.

## Todo
- Journal MCP commun et surface Console.
- Compilation et regressions ; aucun test du parent n'a encore ete execute.
- Reconnexion du nouveau build ; QA native historique, journal et economie.
- Second metier sous 200000 ; niveaux et comptabilite.
- Suite finale et autoverification.
- Nettoyer RUN_TMP et watchers possedes avec verification d'absence.

## Resources
Verification du31 octobre : RUN_TMP absent (ENOENT), aucun effacement
necessaire. Le suivi de la tentative quotidienne mon_E6G0282X6XPXSMZ3
a ete arrete explicitement apres confirmation scriptRunning=false.
Le grand objectif historique reste bloque dans le registre ; ne pas
le declarer accompli pour clore artificiellement la verification presente.

RUN_TMP = %USERPROFILE%/AppData/Local/Temp/givre-fm-ulw-BoryB2
Command env : TMPDIR=RUN_TMP, NODE_COMPILE_CACHE=RUN_TMP/node-compile-cache,
BUN_RUNTIME_TRANSPILER_CACHE_PATH=RUN_TMP/bun-transpiler.
Le worktree et le processus de l'app sont partages/utiles au proprietaire,
pas des ressources jetables a supprimer.
Watcher RPC rpc_1, mon_KB0WEF5B3W4JB9MW : arrete avec kill_bash ;
receipt « watcher killed ». Il observait accidentellement le parent et
produisait du bruit ; ne pas le relancer avec events=all.

## Findings
- Preuve watchdog : transport-2026-10-30_20-31-26.log:5485 environ,
21:08:33.446, anti-stuck ferme l'atelier a 180s pendant des resultats valides.
- Services/LaunchService.Forge.cs utilise l'injection directe ; NoteProgress
doit suivre un resultat valide, jamais le simple envoi de la requete.
- Historique livre : BeginForgeHistorySession/RecordForgeCashExpense/
EndForgeHistorySession et ForgeAsync(historySessionId:...). Parent raccorde
les frais reels aux phases support/runes/trajet sans refacturer la consommation.
- Les achats supports utilisent la relation GameDb skill/type, pas le nom ;
un familier avait ete selectionne dans une ancienne liste UI, jamais achete.
- C# LSP indisponible : csharp-ls absent. Les diagnostics ont ete tentes ;
compilation centralisee requise, aucune affirmation de diagnostic vert.
- Economie pure enfant : 17/17, sortie 0. Ancien PureTests manque de references
InventoryReader/MapInteractive ; non modifie. Projet isole EconomyPolicyTests livre.
- Depot memoire refuse les ecritures : changement preexistant non commis
reference/givre-fm-parity.md. Ne pas commettre ni effacer ce travail.
Faits et demandes du lot conserves ici et dans LIVE_TEST_20261030.md.
- create_goal tente apres ultrawork : refuse car objectif existant non termine,
ancien statut blocked « user interrupted the turn ». Ne pas clore fictivement
le grand objectif ; ces criteres sont la portee courante de sa continuation.
- Git status lu, nombreux fichiers deja modifies/stages avant ce lot :
ne rien reinitialiser ni indexer. Aucun commit/stage autorise.
- Historique git des points touches : ce2a9bb recus atelier,
041b4fd validation Lua statique, ba77756 actions Lua/craft/forge.

## Learnings
- L'XP metier arrive via JobExperiencesUpdateEvent, forme liste au login,
objet unique pour une seule mise a jour ; ne pas extrapoler une formule par rune.
- Une reponse MCP accepted n'est pas une preuve de fin : conserver l'etat
jusqu'a un vrai signal ou afficher explicitement « fin non confirmee ».
- Les prix Brial des guides ne remplacent jamais les offres Mikhal.

## Now - integration en compilation
Les trois enfants sont termines et integres. Wrapper global ajoute dans
BotsViewModel.McpJournal.cs : tous les appels passent par ControlAsync,
ancien dispatch renomme ControlCoreAsync. Le modele borne a 200 entrees ne
recoit aucun corps de requete/reponse ni texte d'exception. Acceptation conservee
distincte de fin ; RunFinished et Running=false sont des signaux d'arret, pas
des preuves de succes metier.

Surface ajoutee : onglet Activite MCP dans BotsView.axaml. Capture avant build :
computer.window("2033152").screenshot(), frame-4, 1920x1032, ancien processus
13928 et niveau joaillomage 60 visible dans la console.

Verification lancee : monitor mon_G766NPXBA6F0R3P6 / bash_785,
PowerShell avec TMPDIR/NODE_COMPILE_CACHE/BUN_RUNTIME_TRANSPILER_CACHE_PATH
sous RUN_TMP, `dotnet test GivreClone.Tests/GivreClone.Tests.csproj
-c Release --no-restore -m:1 --filter
'FullyQualifiedName~Smith|FullyQualifiedName~ForgeHistory|FullyQualifiedName~McpAction|FullyQualifiedName~RuneAcquisition|FullyQualifiedName~Crush'
-v minimal`. Resultat encore attendu, aucun PASS affirme.

## Todo - apres premiere compilation
- Corriger seulement les erreurs de la compilation ou des tests du lot.
- Capturer le nouvel onglet MCP et la FM sur le build reconstruit.
- Tester le metier 64, budget 200000, depuis le nouveau parcours.
- Comparaison et essai brisage, suite finale, autoverification et nettoyage.

## Findings - premiere verification
22:42 reprise : achat support finalement confirme apres handshake categorie.
Louffeur 940 UID4747526, niveau1, prix99. Solde841349, plafond absolu641448.
Reste199901 pour TOUTES les reprises, ne pas reinitialiser a200000.
Arret suivant : catalogue runes vide bien que moyennes serveur recues.
Cause : RuneItemsByGid comparait les noms au resolver de bundles avant
chargement et mettait le resultat vide en cache. Utilise maintenant db.ItemName
et ne cache pas le vide ; regression ForgeRuneCatalogStartupTests ajoutee.
Suite complete 3232 reussis, 2 ignores, 0 echec ; build0.
Fenetre actuelle2361494. Demarrage trop tot apres connexion refuse sans effet
(metiers pas encore annonces). Apres JobExperiencesUpdateEvent, start accepte.
Suivi actuel mon_VG1EY4D9EHJH7MVP / bash_818 : niveau1, trajet vers90703617
en cours, pas de nouvelle depense a ce stade. Catalogue corrige passe le filtre.

Essai suivant : meme absence a la revalidation, 0 depense. L'attente de
publication n'etait donc pas suffisante. Transport 22:33:24-36 : une recherche
sans offre (8533) laisse _hdvBuyerType perime alors que le serveur a bien
selectionne la categorie. La recherche suivante renvoie kdr sur la categorie
deja active, cas connu qui avale la recherche. Correctif : attendre kbj
(ExchangeTypesExchangerDescriptionForUserEvent) et memoriser la categorie
independamment du jzs d'offres ; envoyer follow ensuite. Test reel requis.
Suite complete/rebuild/relance mon_955FA84CY2NPNYBZ / bash_814.

Diagnostic du Rebuild confirme : `offre 940/3124 a 99 non retrouvee a la
revalidation (0 offres)`. Toujours 0 depense, niveau 1.
Correctif LaunchService.Hdv : abonnement HdvUpdated AVANT injection, signal
uniquement apres OfferFor(pid,gid) frais ; attente bornee a la meme echeance,
desabonnement dans finally. Plus de reabonnement aux seuls paquets bruts
entre l'accuse precoce et la publication des variantes. Revalidation exacte
UID/prix/effets et invalidation des offres perimees conservees.
Sources figees avant compilation ; suite complete puis build et relance
mon_KEXV5W5DYGK4RTDW / bash_811. Validation live encore necessaire.

Reprise 22:25 : trois tentatives adaptatives arretees niveau 1, aucune depense.
Devis Lua confirme des offres admissibles (1697 a 200 ; 8533 a 3 kamas).
Ne pas conclure marche vide. Ajout de raisons detaillees dans SmithLeveling,
filtre effet 2825 et reutilisation d'un HDV acheteur deja ouvert sur meme carte.
Tests HDV/FM : 218 reussis, 0 echec. Un edit fait pendant compilation peut
laisser le binaire ancien puis tromper le build incremental (sortie plus recente
que la source lue par le compilateur). Rebuild complet impose, sources figees,
mon_5RDXMD3QCZJDQ0QP / bash_808 : compilation reussie, fenetre attendue.
Piste a confirmer : LaunchService.Packets.cs publie _codeWait a la ligne 218,
avant SeedBuyerOffers ligne 561. Le parcours async natif peut lire l'offre
avant sa publication ; Lua bloquant semble laisser le temps au decodage.
Ne pas modifier ce mecanisme sans confirmer le diagnostic du nouveau binaire.
Plancher global toujours 641448 pour le lot second metier (depart 841448).

Reprise apres compaction : joaillomage 60 termine, depense 233382/300000
(preuve dans LIVE_TEST). Second metier 64 encore non monte, budget 200000 intact.
Dernier essai a 21:58 : voyage atelier 91750921 vers HDV bloque sur porte 458,
liste MCP des interactifs vide apres reconnexion. `map:sendMapRequest()` a
restaure porte 520535 et six interactifs. Correctif ajoute a Travel : abonnement
avant requete, attente bornee de publication, puis relecture du snapshot sans
blacklister d'emblee l'unique sortie. Empreinte lua-f07 mise a jour.
Adaptive : achats/supports inscrits dans ForgeHistory et resultats de chaque
lot reportes dans les compteurs et l'historique UI.
Validation en cours : mon_RS6K44Y89X38EY2G / bash_792, tests cibles puis build.
Application fermee avant compilation. Diagnostics LSP non disponibles
(csharp-ls absent). Nouvelle sauvegarde memoire refusee : changement etranger
non commis reference/givre-fm-parity.md, ne pas toucher.

mon_G766NPXBA6F0R3P6 / bash_785 a termine sortie 1 : MSB3027/MSB3021,
Engine.dll verrouillee par l'ancienne application PID 13928. Pas un PASS ;
aucun test execute. Correctif de procedure : bouton natif Fermer trouve par AX
e808 puis clique. Verification PowerShell Get-Process -Id 13928 :
OLD_APP_STOPPED, sortie 0. Ne jamais reconstruire le meme output avec l'app ouverte.
Relance de la meme commande ciblee, meme environnement isole :
mon_MB9KARVQZ7H0P4R9 / bash_786, resultat attendu.
