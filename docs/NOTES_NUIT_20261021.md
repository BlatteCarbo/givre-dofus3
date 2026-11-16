# Notes de la nuit du 20 au 21 octobre 2026

Branche `integration-opus-20261020` (33 commits depuis `4c8b501`, **rien poussé**). Tests : `GivreClone.Tests` 2 273 verts, `GivreClone.PureTests` 321 verts,
smoke MCP 66 outils. L'app Release compile sans erreur ; elle tourne avec le dernier build (relancée en fin de nuit).

## Ce qui est nouveau (et où le voir)

| Fonction | Où | Vérifié |
|---|---|---|
| **Création de personnage (Socket)** | Comptes → Socket → serveur → « Nouveau personnage » : sexe, classe (icônes), tête, corps, nom (vide = suggestion), couleurs, tutoriel, récapitulatif | **en jeu** (Rivith, Brial) + octets de la capture réelle reproduits par un test |
| **Atelier éco** | Outils → Atelier éco, ou Ctrl-K « craft » | interface testée dans l'app (hors jeu) |
| – Craft ou acheter | recherche hors ligne, arbre récursif, forçage, prix manuels, **import d'une liste de prix collée**, liste de courses, objets suivis, miniatures | testé dans l'app |
| – Forgemagie | poids d'un objet du sac, runes, calcul de mélange Ra/Pa/Ba, séances | testé (hors objet réel) |
| – Panoplies | coût d'une panoplie pièce par pièce (fabriquer ou acheter), totaux, liste de courses fusionnée | compile + logique testée, pas vu à l'écran |
| – Patrimoine | valeur du sac + banque + kamas | non testé avec un bot |
| – Bonnes affaires | lots sous la moyenne HDV | non testé avec un bot |
| – Alertes de prix | cible atteinte à l'HDV observé → Discord | UI testée, envoi non testé |
| – Abonnements | fin d'abonnement par compte (saisie manuelle possible), alertes Discord 7/3/1 j + expiration, carte sur le tableau de bord | UI testée ; le jeu 3.7.12 n'envoie pas toujours la date |
| – Carnet | positions, notes, messages à coller | testé |
| **Reconnexion auto d'un bot bloqué** | Paramètres du bot → anti-blocage | logique testée, pas en jeu |
| **Pilotage socket complet via Cytrus** | automatique au lancement socket | **à valider en combat** (`hms`, `joc`) |
| Valeur des drops par zone / rentabilité d'un donjon | onglets Expédition / Donjon | non testé avec un bot |
| Alerte de revente (objet du sac au-dessus de la moyenne) | Paramètres du bot → notifications | non testé |
| Scan HDV craft : décomposition + taxe | Bots → HDV → scan | non testé avec un bot |
| Copie des réglages de messagerie vers d'autres persos | Paramètres du bot → « Copier vers d'autres persos » | non testé |
| Décor de saison (Halloween, Noël) | automatique ; Réglages → Effets pour le couper | vu à l'écran |
| MCP : `get_inventory`/`get_bank` (effets), `start_dungeon` (élixir), `get_craft_advice`, `get_market_deals`, `get_subscriptions`, `get_wealth` | `GivreMcp` | smoke OK |

## À vérifier en premier (en jeu)
1. **Combat Socket** : une attaque + un changement de carte en 3.7.12.12 (le journal affichera `✔ Cytrus : … → pilotage complet`).
2. **Création** d'autres classes / sexes / couleurs perso ; Forgelance (id 20).
3. Un bot connecté : onglets Patrimoine / Bonnes affaires (ouvrir l'HDV avant) ; alerte de revente ; reconnexion de secours (simuler un blocage).
4. Webhook Discord : une alerte de prix et une alerte d'abonnement.

## Pistes non faites (voir `docs/MAPPING_TODO_20261021.md`)
Suppression de personnage (capture à faire, dont le code secret), Bourse / boutique d'ogrines, clic droit joueur, dialogue PNJ en interface, chasse au trésor
(pas de base d'indices hors ligne), bascule MITM ↔ Socket sans déconnexion (référence : `GivreActive/EasyBot/Sockets/SwitchSocket.vb`), rendu des têtes et corps
(d3-ts-renderer / superdofus), simulateur d'XP des métiers (pas de table d'XP des métiers dans la base).

## Outils ajoutés
- `tools/pktdump_extract.py` : décode les paquets relevés par la « Capture de découverte » (PacketDump) d'un `transport-*.log`.
- `tools/gen_mapping_inventory.py` : régénère `docs/MAPPING_INVENTORY_20261021.md`.
- Dossier des données de l'atelier : `…\userdata\eco\` (`prices.json`, `notebook.json`, `fm.json`, `subscriptions.json`, `alerts.json`).

## Réserves honnêtes
- L'accès à MizanBot par l'automatisation d'écran a échoué (il ignore les clics simulés) : je n'ai donc pas pu m'en inspirer en direct, seulement par son changelog et ses captures.
- Les tests « en jeu » de la nuit se limitent à la connexion et à l'écran de choix du personnage (aucun personnage créé la nuit, aucun achat).
- La table des runes et les poids (Forgemagie) reposent sur les données du jeu pour les poids et sur les quantités usuelles des runes de base ; pas de simulateur de réussite.

## Suppression de perso (état)
- **Avec code secret : fait et vérifié en jeu** (Socket) — hash = `md5(idPerso~réponse)` hexadécimal minuscule (3 captures), sélecteur de perso : bouton ✕, réponse secrète, case de confirmation.
- **Sans code secret : non pris en charge.** 7 captures du vrai client (ids et hash différents) : le hash n'est ni `md5(id~)` ni `md5(id~nom)`, ni avec l'email du compte, ni les valeurs usuelles (recherche exhaustive).
  Tentatives Frida sans résultat : `HashAlgorithm.ComputeHash`, `MD5CryptoServiceProvider`, BouncyCastle `MD5Digest`, la routine MD5 native de GameAssembly (RVA 0x11DFD5B), les routines de `UnityPlayer.dll`, `BCrypt*`/`CryptHashData`,
  `String.Format/Concat/Join`, et une recherche de l'id en clair dans la mémoire : jamais appelés / jamais trouvé. Piste suivante : retrouver par la table des méthodes IL2CPP (dump Cpp2IL de la 3.7.12.12) la méthode qui construit `kso`
  et lire son code, plutôt que de chercher la fonction de hachage à l'aveugle.

## Ajouts de la journée (à tester ensemble)
- **Carte du monde DofusDB** (bouton « Carte du monde · ressources, bots ») : images DofusDB par coordonnée (cache disque `worldmap/`), glisser/molette/flèches, règle de coordonnées, « Ajuster la vue », « Aller à x,y », ressource en surbrillance (pastilles orange = nombre de points), bots en rouge, ligne bot → sélection avec distance, « Copier travelTo(carte) ». Non vu à l'écran : à regarder en premier.
- **HDV Tainéla → Astrub** : si le graphe ne connaît pas la carte, marche par coordonnées (carte voisine par carte voisine, 80 sauts max).
- **Expédition** : l'attaque juste après un changement de carte relit la carte et retente (avant : « pas de chemin » → carte sautée).
- **Onglet Carte du bot** : flèches Haut/Gauche/Bas/Droite autour de la carte ; un 2ᵉ clic sur la même flèche annule la sortie (le perso finit sa marche mais ne change pas de carte), un clic sur une autre flèche annule puis repart dans la nouvelle direction. Zaaps et bouton « Envoyer le bot ici » sur la carte du monde.
- **Petit écran (14")** : hauteur de la carte et largeur du panneau s'adaptent à la fenêtre. Carte « État » : niveau, carte, zone, kamas, XP, énergie, pods, abonnement ; PID / IP de sortie / paquets / serveur repliés dans « Technique ».
- **Image du jeu en fond** (case « Image du jeu en fond (DofusDB) ») : image DofusDB pleine taille sous les cases, avec curseurs de calage (décalage X/Y, zoom, opacité). Le calage par défaut est une estimation : à régler ensemble, puis je fige les valeurs.
- **Copie au clic** : id de carte, position et cellule survolée se copient en cliquant dessus ; bouton « Copier carte · position · cellule ».
- **Après combat** : le serveur répond en ~30 ms et le bot demande la carte à +0 ms (vérifié dans le log) ; le délai restant venait de la pause « settle » de 400-800 ms, ramenée à 150-350 ms en mode Rapide (mode Legit inchangé).
- **Personnage dans l'inventaire** : panneau à gauche avec le rendu du perso. Le look est lu dans les paquets (liste des persos à la connexion, puis `ContextRefreshEntityLookEvent` quand un équipement ou un skin change) et rendu par le moteur public d'Ankama (`static.ankama.com/dofus/renderer`). Non vérifié en jeu : le rendu peut manquer pour les skins très récents (message affiché dans ce cas). Si l'app démarre alors que le perso est déjà en jeu, l'image n'apparaît qu'au premier changement d'équipement ou à la reconnexion. Pas d'état « assis » (non décodé) ; l'activité du bot est affichée sous l'image.
- **ExoFast / Docraft** (dossiers de recherche) : deux moteurs purs et testés, pas encore branchés à l'interface : `Services/Eco/ForgeEngine.cs` (prochaine rune selon priorités, intervalles et puits, règles type ExoFast) et `Services/Eco/LotPlanner.cs` (choix de la taille de lot : « grand d'abord » ou « le moins cher / le plus cher à la marge près »). Le HDV de GivreWork couvre déjà prix min/max, repricing, sniper et achat de craft. La forgemagie automatique demande une capture du protocole de l'atelier avant tout code d'envoi.
- **Pistes** : DofusLab (open source) pour les données de builds / objets ; Barbofus et Dofusbook (partenariats) pour des builds et prix à importer — à décider ensemble.
- **DofusLab** (github.com/dofuslab/dofuslab) : exploré. Le dépôt n'a **aucun fichier de licence** (le README dit « open source », mais sans licence le code et les données ne sont pas réutilisables tels quels) : je n'ai rien copié. Ses données d'objets et de panoplies viennent du jeu, que `dofus.sqlite` contient déjà. Idée reprise, réécrite à partir des lignes d'infobulle du jeu : le **total des caractéristiques de l'équipement porté** (`Services/Equipment/BuildStats.cs`), affiché dans le panneau personnage de l'inventaire (« Stats de l'équipement »), hors bonus de panoplie. Une demande d'autorisation aux auteurs permettrait d'aller plus loin (builds partagés, calcul de dégâts).

## Combat (analyse des derniers combats + améliorations)
- **Analyse du dernier log** : un combat de 10+ tours venait du défi **Statue** (« sans bouger ») contre un monstre qui se téléporte : le bot passait ses tours sans pouvoir attaquer (« FILET : aucun sort castable… tour passé »). Le travail de la session précédente (repris tel quel, commit 7a33477) abandonne Statue après 2 tours bloqués et rétablit le déplacement.
- **Sorts de zone** : le bot visait toujours la case d'un ennemi. Il vise maintenant la case qui touche le plus d'ennemis dans la forme du sort (croix, cercle, ligne…), avec les mêmes garde-fous (portée, ligne de vue, alliés, défis). Journal : `[combat] zone <sort> : centre X -> Y`. Pas de décalage s'il n'y a qu'un seul ennemi.
- **Zombie / Nomade** : si le tour peut finir le combat (dernier ennemi, ou un ennemi à 30 % de PV ou moins), le déplacement exigé est fait **avant** le coup de grâce. Nomade : si la route la plus longue finit au contact (tacle), il **recule puis revient** pour dépenser tous ses PM sans être taclé.
- **Non vérifié en combat réel.** Pas encore fait : estimation des dégâts par sort pour tous les persos, apprentissage depuis les replays, lecture des guides de défis (dofuspourlesnoobs).
- **Fenêtre MITM sans déconnecter un bot Socket** : demandé ; c'est un « handoff » de transport (document `PORTAGE_GIVRE_GIVRE_MITM_SOCKET_NOANIM_TURBO.md`, phases A à I) à faire avec tests en direct ; non commencé.
- **Discussion** : journal de chat par bot (tous canaux, MP envoyés) et outils MCP `read_chat` / `send_private_message`. Seul le canal 10 (MP) est identifié ; l'onglet de discussion de l'app et de la page téléphone reste à faire.
- **Sorts** : le même sort peut être ajouté plusieurs fois à la rotation (une ligne « sur moi », une sur l'ennemi, une sur un allié…) ; chaque ligne a ses réglages. Attention : le « temps de relance » est suivi par sort, pas par ligne (si deux lignes du même sort ont un temps de relance, elles se bloquent entre elles ; laisser 0 sur l'une). Nouveau ciblage « Lancer sur une case adjacente » : le sort part sur la case collée à toi qui touche le plus d'ennemis dans sa zone (corps-à-corps, zones autour de soi). Non vérifié en combat réel. GivreActive : son « IsHandToHand » correspond à ce ciblage ; son réordonnancement tactique (beam-search) n'est pas repris (gros chantier, voir `COMBAT_ACTIVE_COMPARISON_20261020.md`).
- **Onglet « Discussion »** (page du bot) : deuxième console à côté de la console technique. Elle affiche les messages de tous les canaux et tes MP envoyés (case « MP seulement »), et réutilise la barre d'envoi existante (canal, pseudo pour un MP, message). Les noms de canaux suivent la numérotation d'envoi de l'app (0 général, 1 groupe, 2 guilde, 3 alliance, 5 commerce, 6 recrutement) : seul le 10 (MP) est confirmé à la réception ; si un nom est faux, dis-moi le canal concerné. Les messages reçus avant le lancement de l'app ne sont pas là.
- **Chaîne de production via MCP** : nouvel outil `plan_production_chain` (objet par nom ou gid, quantité, bot) : renvoie ce qui se **récolte** (avec les meilleures cartes proches du bot), ce qui s'**achète**, ce qui se **fabrique** et la **vente** (marge après taxe). Lecture seule : l'IA enchaîne ensuite `find_resource_maps`, `make_gather_script`, `travel_to`, `run_lua_script`. La fabrication et la vente à l'HDV restent à scripter ; aucun enchaînement automatique n'est lancé.
- **Garde-fous du sniper HDV** (`Services/Eco/SniperGuard.cs`, pur, 6 tests) : le sniper n'existe pas encore côté moteur (seul le réglage « % sniper » est là) ; ce module dit **non** au moindre doute : marché trop mince (moins de 4 autres lots), référence instable (médiane et moyenne pondérée qui divergent = marché truqué), objet de masse (plus de 20 000 unités visibles : eau, blé…), lot qui fait l'essentiel de l'offre (mur de vendeur), rabais insuffisant sous la médiane des **autres** lots (25 %), marge de revente trop faible taxe déduite (10 %), plafond de dépense par achat et par objet, cadence par heure, liste d'objets interdits. À brancher au moteur d'achat quand on le construit.
- **Assistant de progression** : outil MCP `recommend_zones` (niveau ou bot) : sous-zones de combat dont les monstres ont le niveau du perso jusqu'à +25 %, classées avec nombre de cartes et de monstres (`Services/Progression/ZoneAdvisor.cs`, pur, 3 tests). Estimation sur les niveaux seuls : ni résistances, ni dégâts réels, ni équipement. Suite logique : `list_expedition_zones(subArea)` puis `start_expedition`. Pas encore d'écran dans l'app.
- **Estimation des dégâts** : `Services/Combat/DamageEstimator.cs` (pur, 5 tests) + outil MCP `estimate_spell_damage` (bot ; sort précis ou toute la rotation ; monstre optionnel avec son grade). Formule : dés du sort × (100 + caractéristique de l'élément + % dommages) / 100 + dommages fixes (tous + élément), puis résistance % et réduction de la cible ; critique pondéré par la chance de critique du perso. Les caractéristiques viennent maintenant de `CharacterCharacteristicsEvent` (tous les ids : force 10, intel 15, chance 13, agilité 14, % dommages 25, dommages 16, critique 18, résistances 33-37…) ; elles n'arrivent qu'à la connexion ou à un rafraîchissement de fiche (`characteristicsKnown` = faux sinon : reconnecter). Les résistances et PV des monstres viennent de `dofus.sqlite` (grade le plus proche du niveau demandé). **Estimation, jamais exacte** : ignore les modificateurs propres au sort, la puissance, les états, les effets spéciaux ; la correspondance des éléments (2 feu, 3 eau, 4 air, 5 terre, 0/1 neutre) est celle du moteur actuel (terre confirmée, les autres déduites). À comparer avec de vrais dégâts en combat avant de s'y fier. Pas encore branché au choix des sorts en combat.

## Retours du soir (17 h 49)
- **Coordonnées en combat** : un combat (carte d'instance) ou le havre-sac annoncé en (0,0) n'écrase plus la position affichée ni la heatmap : on garde la vraie carte d'avant le combat (`IsMeaninglessPosition`).
- **Donjon** : une clé absente du sac n'arrête plus le script (avertissement, tentative quand même : un trousseau peut la fournir).
- **Expédition** : on peut sélectionner **plusieurs sous-zones** (clic pour basculer) : cartes et monstres sont réunis.
- **Inventaire** : « Équiper » n'apparaît que pour les objets équipables, « Utiliser » ni pour les équipements ni pour les ressources.
- **Infos** : icônes des métiers (DofusDB, mises en cache). **Personnage** : si le rendu Ankama échoue, repli sur l'icône de classe et affichage de la cause (code HTTP) et du look pour diagnostic.
- **Console** : « 💬 Discussion » est maintenant une 3e vue de la Console, à côté de Journal et Technique (l'onglet séparé est supprimé).
- **Trajets** : la mini-carte affiche les vraies images DofusDB sous les cases dès que le zoom le permet (cases de 17 px ou plus ; zoom par défaut 20). Pas encore : recherche de ressource et filtres serveur/calque façon Mizan.
- **Pas fait / à investiguer avec toi** : expédition (un seul combat par carte, déjà traité côté relecture de carte : à re-tester avec le nouveau build et me donner le log), « temps entre les tours », HDV (voyage / ouverture échoués, rentabilité), abonnement Dofus affiché « — » (GivreActive l'affiche : à regarder), fusion d'autres onglets.

## Suite du soir (19 h 45)
- **Tuiles de la mini-carte Trajets désactivées** (trop lourdes) : une constante à remettre à 17 pour les réactiver (`RouteMapControl.TileMinSize`).
- **Personnage invisible** : le look reçu était `{0|3|0=2443|100}` (squelette 0), que le moteur d'Ankama ne sait pas dessiner. Le squelette 1 (humain standard) est maintenant utilisé quand il est absent, et une ligne `[look]` détaille le paquet dans le journal pour comprendre d'où vient ce 0. Si ça ne rend toujours rien : donne-moi cette ligne.
- **Fichier hosts** : au démarrage, GivreClone lit (sans jamais l'écrire) le fichier hosts et prévient (journal + notification) si un serveur Ankama y est redirigé vers ce PC. Constat dans le log 18:55-18:57 : le nom `dofus2-co-production.ankama-games.com` se résolvait en 127.0.0.1, donc les connexions Socket échouaient (« proxy interdit le port 5555 ») : la redirection hosts était déjà active à ce moment-là. Ce qui l'a posée n'est pas identifié.
- **Expédition « bugué »** : aucun log d'expédition exploitable depuis le dernier build (les connexions Socket échouaient à cause de la redirection). À re-tester maintenant que hosts est propre, puis me donner le log.
