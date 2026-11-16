# F05 Expédition — audit hors ligne du 24 octobre 2026

Branche `codex-f05-20261024`, base `e920c41`, worktree `GivreWork-F05-20261024`. Aucune application lancée, connexion, commande de contrôle, base modifiée, publication ou modification des fichiers interdits. Journaux et SQLite installée lus directement en lecture seule ; aucune copie des journaux ni donnée d’authentification livrée.

## Défaut prouvé et corrigé : instances de combat dans le parcours

- Le catalogue brut de sous-zone inclut des cartes de combat portant un calque d’atlas et une position factice **0,0**. `CleanMaps` sélectionnait le calque majoritaire puis la première carte par coordonnée : il en conservait donc une. `NearestOrder` pouvait la placer avant les vraies cartes ; le voyage pouvait ensuite rechercher un homonyme en 0,0 (`ResolveNavigationMap`), sans rejoindre la cible demandée.
- Journal du 24, PID 1073741831 : quatre cibles 153887744/153888002/153888004/153888006 impossibles à rejoindre entre **09:31:47.781 et 09:32:17.142** ; première recherche de repli jusqu’à 09:32:16.572, soit **28,791 s** avant la cible suivante. La reprise Dakal fournie constate séparément `153887744, inFight=true` ; aucune nouvelle observation en jeu effectuée ici.
- Journal du 23, après minuit, PID 12184 : quatre voyages impossibles vers 153883648/153883906/153883908/153883910, **00:16:21.382–00:17:20.871**. À Astrub, 69993731 échoue à **23:27:22.678**, explicitement absente du world-graph.
- Correctif minimal : avant déduplication, exclure une position 0,0 lorsque le graphe de navigation chargé confirme l’absence de cette carte. Raccordement aux sélections UI simple/multiple et au lancement MCP. Une vraie carte 0,0 connue comme source **ou arrivée** reste éligible ; graphe absent/vide = état inconnu, comportement historique conservé. Les cartes hors 0,0 restent inchangées.
- Régressions : instance précédant une vraie carte 0,0, arrivée seule dans le graphe, graphe manquant, zone devenue vide, catalogue SQLite installé 449/450. Ces deux sous-zones donnent **7 cartes extérieures chacune**, sans les instances ni les intérieurs.
- Limite : cette règle utilise la navigation disponible, pas un drapeau client « combat uniquement » décodé. Un graphe incomplet peut écarter une vraie carte 0,0 isolée ; un graphe absent ne filtre pas les instances. Aucun masque `m_flags` inventé, aucun filtre global sur toutes les cartes 0,0.

## Mesures et observations sans correction spéculative

Méthode : événements `[lua pid …]` uniquement ; engagement = message `combat engagé`, fin = message exact `combat terminé`. Les reprises de récolte sont comptées séparément. Délai = fin observée → engagement suivant du même PID ; seules les paires complètes sont utilisées. Les pauses/rechargements de script restent dans la moyenne brute. Les fichiers sont des segments de rotation, pas l’intégralité d’une session par bot.

Journal du 24 (`01-14-39`) : **96 tentatives Expédition, 96 engagements confirmés, aucun timeout d’engagement** ; 95 fins normales et une reprise par la boucle hôte. Les trois autres PID font de la récolte : 10/7/5 fins de combat de récolte, sans série d’engagements explicites comparable. Un combat de récolte est déjà terminé au début du segment.

- PID 1073741831 : **66 engagements** ; 64 intervalles, moyenne **55,417 s**, médiane **13,938 s**. Avec seuil descriptif <300 s : 63 intervalles, **15,978 s** en moyenne. L’intervalle incluant la pause après le plafond de cinq combats explique la forte moyenne brute.
- PID 1073741833 : **28 engagements** ; 27 intervalles, moyenne **40,400 s**, médiane **22,374 s** ; 26 intervalles <300 s, moyenne **30,129 s**.
- PID 1073741835 : **2 engagements** ; un intervalle **13,609 s**. Échantillon insuffisant pour une moyenne représentative.
- À titre de contexte pour les trois bots de récolte, fin → prochaine *détection* de combat : PID 1073741825, 9 intervalles, moyenne **3765,727 s** ; PID 1073741827, 6 intervalles, **617,639 s** ; PID 1073741829, 4 intervalles, **2043,739 s**. Ces moyennes brutes incluent les longues interruptions et ne mesurent pas un rythme de farm Expédition.
- Les quatre annonces du plafond de cinq combats à **08:22:32.888/33.081** et **08:29:49.413/49.642** ne prouvent pas un dépassement : le script peut être relancé ; le compteur dépend de la session du script. Aucune correction du plafond justifiée par ces seules traces.
- **12 attentes fin → prochaine attaque entre 13,0 et 13,3 s**, total **157,644 s** (ex. 08:21:10.637 → 08:21:23.773). Hypothèse : attentes de synchronisation `WaitSelfReady` (8 s position + 5 s géométrie). Les traces ne prouvent pas quel état manque ; délais non raccourcis sans preuve de sécurité. Vérifier les invalidations/confirmations de carte côté GameLink, fichier réservé.
- PID 1073741831 : **126 arrivées de trajet**, 18 cartes distinctes ; 153879300 et 153879812 apparaissent **34 fois chacune**. 90 des 125 intervalles entre arrivées n’ont aucun engagement explicite. PID 1073741833 : **117 arrivées**, 14 cartes distinctes ; 96 des 116 intervalles sans engagement. PID 1073741835 : 5 arrivées, 5 cartes ; 3 des 4 intervalles sans engagement. Ce sont des arrivées du moteur de voyage, incluant les transits et replans ; elles ne prouvent pas toutes une visite de farm inutile ni une boucle infinie.
- **7 arrivées dans des intérieurs sans combat** pour le PID 1073741831 : 153356296 deux fois (08:22:14.571, 08:22:54.735), 153354240 (08:23:31.098), 153355272 deux fois (08:28:31.734, 09:14:46.092), 153355264 deux fois (08:29:17.193, 09:12:05.774). La base confirme worldMap=-1. Le nettoyage par calque déjà présent sur la base `e920c41` les exclut comme destinations d’Expédition ; ce n’est pas un nouveau défaut à corriger ici. Il n’interdit pas un transit nécessaire par un intérieur.
- À **09:32:24.643–09:32:30.003**, six replans partent de 153879552 vers 153880064, puis « aucun autre chemin » à 09:32:30.532. L’échec d’envoi/refus de déplacement est réel, mais la cause de session/annulation n’est pas établie hors ligne. Transmettre à F06 ; aucun changement des fichiers de mouvement des autres lots.

Journal du 23 (`23-22-13`) : **25 tentatives, 20 engagements confirmés, 4 expirations explicites**. La tentative restante n’a pas d’issue explicite dans la chaîne Lua ; ne pas la qualifier de refus serveur.

- 23:28:51.274 → **23:29:03.364**, PID 1073741825 : délai **12,090 s**.
- 23:33:57.886 → **23:34:10.770**, PID 1073741827 : **12,884 s**.
- 00:14:42.666 → **00:14:54.816**, PID 12184 : **12,150 s**.
- 00:15:13.731 → **00:15:25.894**, PID 12184 : **12,163 s**.

Soit **49,287 s** d’attente cumulée. Deux échecs du PID 12184 visent « Feu Follet » sur des cartes/cellules différentes : pas de preuve d’une répétition du même groupe rejeté. `FightOne` attend déjà `IsInFight` avant d’annoncer l’engagement ; `inject=OK` ne constitue pas une acceptation serveur. Aucun décompte fiable d’attaques *refusées par le serveur* ne peut être déduit de ces quatre timeouts.

## Audit des autres exigences

- Taille, obligatoires et interdits : configuration générée et sélection raccordées dans `GivreLuaHost.Combat.cs` ; pas de profil effectif complet corrélé à chaque groupe dans ces segments. Aucune violation démontrée. Le traitement des quantités spécifiques pour un monstre absent (`c > 0`) est une hypothèse de sémantique à préciser, pas un correctif livré.
- Groupe atteignable : `MonsterGroupReachability` teste les cellules d’appui et passe aux groupes suivants ; la géométrie inconnue conserve un repli de synchronisation. Aucun cas prouvé où le groupe inatteignable masque durablement un groupe valide.
- Pods, durée, plafond, anomalie et zone vide : protections présentes dans le script généré et tests existants `ExpeditionLoopRegressionTests`. Les corrections du 23 font déjà partie de la base ; pas de nouveau dépassement prouvé.
- Défaite : l’Expédition générique ne promet pas un arrêt systématique après défaite dans `ExpeditionProfile` ; la boucle hôte peut ressusciter. Les scénarios de défaite de la reprise fournie concernent des scripts spécifiques et un autre segment. Pas de preuve suffisante pour imposer un nouveau comportement d’arrêt ici.
- Attente d’équipe : `FightOne` ignore le résultat du timeout d’attente des suiveurs (déjà signalé dans la reprise du 23). Pas de preuve d’équipe absente dans ces traces, donc aucun patch.

## Essais réels à effectuer par le propriétaire du runtime

1. Graphe chargé, sélectionner les sous-zones **449 puis 450**, séparément dans l’UI et via MCP : sept destinations extérieures chacune, aucune recherche vers les instances 0,0. Faire un tour complet avec limite de cinq combats, vérifier fin serveur et aucun sixième engagement.
2. Sélectionner une vraie carte accessible en 0,0 connue du graphe, vérifier qu’elle est conservée. Relancer sans cache de graphe : le catalogue doit conserver son repli, sans classer arbitrairement toute carte 0,0 comme combat. Contrôler le rafraîchissement après chargement du graphe.
3. Carte avec deux groupes, le plus proche bloqué et l’autre accessible : vérifier groupe choisi, taille/filtres effectifs, refus serveur éventuel, autre groupe essayé sans rafale. Pour chaque engagement, comparer requête envoyée, entrée en combat et résultat serveur.
4. Deux combats sur la même carte : horodater fin serveur, restauration carte roleplay, SelfReady, CellsReady et prochaine attaque pour expliquer les 13 s. Intervention éventuelle dans GameLink à transmettre au propriétaire du fichier réservé.
5. Essais distincts : pods juste sous puis au seuil (arrêt sans banque, retour bancaire configuré), durée atteinte pendant attente de repousse, zone sans cible puis zone sans carte joignable, défaite avec politique d’arrêt explicitement choisie. Vérifier qu’aucun engagement ne précède la résolution des limites.

## Validation

Commandes exécutées dans ce seul worktree, séquentiellement :

- `dotnet build GivreClone.App -c Release --no-restore` : **réussi, 0 erreur, 207 avertissements** (3 min 10 s).
- `dotnet test GivreClone.Tests -c Release --no-restore` : premier passage **2646 réussis, 3 échecs de temporisation, 2 ignorés**. Échecs dans `TeamSupervisorTests.Lua_teams_preserves_requested_status_and_options` et `MZP0PacketBusTests.Publish_commits_waiter_before_slow_subscriber_even_if_cancel_runs` / `Cancellation_registration_and_timer_initialization_race_do_not_hang_or_leak_waiters` ; sources de ces tests et services inchangées.
- Même commande complète relancée, sans changement de sources ni de configuration : **2649 réussis, 0 échec, 2 ignorés, total 2651**, durée des tests 1 min 8 s. Les trois nouveaux tests F05 passent, dont le catalogue réellement installé. Le second passage vert ne démontre pas que les temporisations existantes sont devenues fiables.
- `git diff --check` : aucune erreur. Clés `x:Key` uniques : **fr 2701, en 2585, es 2584** ; aucune clé ajoutée.

Les deux projets externes GivreCore/GivreLauncher.Core sont copiés sans bin/obj dans `artifacts/game-data-review/f05-validation` (ignoré Git). `DirectoryBuildTargetsPath` redirige localement les références pour que restauration et compilation n’écrivent pas dans leurs dépôts. Aucun csproj versionné modifié.

La variable d’environnement locale `DirectoryBuildTargetsPath` pointe sur `artifacts/game-data-review/f05-validation/Validation.targets` pour les commandes de build/test ci-dessus ; une restauration agrégée réussie prépare App et Tests avec cette même redirection. Les deux tentatives préparatoires (nom de solution incorrect puis syntaxe de redirection invalide) n’ont restauré aucun projet. Aucun binaire produit n’a été lancé.
