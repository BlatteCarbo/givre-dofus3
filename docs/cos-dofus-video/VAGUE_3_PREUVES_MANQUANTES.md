# Vague 3 CoS : resoudre les inconnues restantes

## Prompt pret a coller

Travaille dans ce projet GivreClone. Les vagues precedentes sont dans
`docs/cos-dofus-video/rapports/` et `rapports-v2/`. Lis leurs index puis les
conclusions de la mission correspondante avant de commencer. Cette vague doit
apporter des preuves nouvelles, pas reformuler ces rapports.

Lance les six missions ci-dessous en parallele. Chaque mission ecrit seulement
dans `docs/cos-dofus-video/rapports-v3/Pxx/`. Les sources applicatives, les tests
existants et les dossiers des autres missions restent en lecture seule. Utilise
les donnees locales et des sources publiques attribuees. Aucun lancement,
arret, injection ou pilotage de Givre/Dofus ; les sessions de jeu servent aux
tests du developpeur. Aucun commit, publication ou message externe.

Chaque mission livre `RESULTAT.md`, `PREUVES.md` et, si les donnees le permettent,
des fixtures JSON documentees. Cite les fichiers/lignes ou URL/date/timecode,
la version, le hash des entrees et les commandes exactes executees. Distingue
donnee extraite, comportement observe, hypothese et cas synthetique. Une absence
de preuve devient une inconnue explicite, jamais une regle inventee.

## Missions

### P01 - Interdiction de soins et vol de vie

Point de depart : N08/PASSAGE_DEVELOPPEUR.md, puis les extractions N01/N07.
Retrouve dans les donnees 3.7.13.17 les actions et etats qui interdisent les
soins. Relie sorts, grades, effets normaux/critiques et conditions. Separe soin
direct, vol de vie, regeneration, bouclier et PV maximum. Produis des exemples
concrets positifs et negatifs ; indique pour chaque interaction si les donnees
serialisees suffisent ou si une observation serveur manque.
Termine avec une table d'IDs tracables et six scenarios minimum dont l'oracle
est justifie, ou un constat precis des branches sans oracle.

### P02 - Durees, initiative et expiration

Reprends N08 et les recherches de buffs des deux vagues. Etablis les conventions
d'expiration : debut/fin du tour, tour du lanceur ou du beneficiaire, duree zero,
effet immediat et effet differe. Donne des chronologies S->B et B->S pour PA,
PM, puissance, bouclier et reduction de degats. Les champs client seuls ne
prouvent pas le moment d'expiration : cite une observation ou marque inconnu.
Termine avec huit chronologies reutilisables, leurs evenements ordonnes et le
resultat attendu a chaque frontiere verifiee.

### P03 - Compagnons et attribution des challenges

Reprends N02/N04/N10 et les rapports compagnons/challenges de la vague 1.
Construis une matrice par famille de contrainte : acteur autorise, ordre des
attaquants, cible imposee, case finale, element, invocation/compagnon. Recherche
les regles de De la meme lignee, Duel et Prudent en priorite. Separe joueur,
compagnon controle et invocation. Pour les logs locaux, recherche les liens
actorId/ownerId, ressources de debut/fin de tour, action demandee et confirmation.
Termine avec les regles prouvees et une liste exacte des champs manquants pour
expliquer un tour compagnon inactif ; un log d'envoi seul ne prouve pas un sort.

### P04 - Critiques utiles au planificateur

Compare les effets normaux/critiques de sorts 3.7.13.17 dans N01/N07. Classe
les differences : valeur seule, effet ajoute/retire, cible, duree, etat,
deplacement ou invocation. Etudie les actions 115/418 et les bornes de chance
critique a partir de preuves attribuees. Le resolveur actuel utilise une moyenne
non critique : propose des oracles independants avant toute recommandation.
Termine avec dix exemples differencies, dont un critique modifiant autre chose
que les degats si disponible, et une proposition de calcul d'esperance qui
declare les branches aleatoires non modelisees au lieu de les ignorer.

### P05 - Sram et Roublard : transitions minimales

Relis les rapports de classe existants. Choisis deux mecanismes Sram et deux
Roublard encore insuffisamment prouves. Pour chacun, donne un etat initial
cellules/acteurs/etats, une action, l'ordre des declenchements et l'etat final.
Priorite aux reseaux de pieges, poussee/teleportation, murs et explosions en
chaine. Recherche surtout les contre-exemples aux regles simplistes.
Termine avec huit transitions minimum, leur provenance et les confirmations
serveur necessaires. Separe la regle documentaire de sa validation en jeu.

### P06 - Mesure Turbo et marche PM par PM

Reprends N03/N10 et le journal de validation courant. Catalogue les horloges et
evenements deja disponibles : calcul du plan, demande mouvement, confirmation,
debut/fin visuels, premier tour adverse, vitesse effective et retour hors combat.
Produis un protocole de comparaison Socket/MITM avec memes scenarios et metriques
separees. Les hooks visuels 3.7.12.12 refusent actuellement 3.7.13.17 ; un audit
des signatures est deja en cours cote developpeur, ne le duplique pas.
Termine avec un schema de trace, les points d'instrumentation proposes
(fichier/symbole), et des exemples de diagnostics distinguant recalcul lent,
confirmation lente et animation lente. Aucun benchmark de DLL ancienne ne doit
etre presente comme mesure du code courant.

## Consolidation et arret

Ecris `rapports-v3/INDEX.md` : liens, preuves nouvelles par rapport a la vague 2,
contradictions, inconnues et trois integrations prioritaires. Chaque affirmation
de completion pointe vers un livrable ; chaque commande annoncee comme executee
possede son resultat. Arrete apres les six missions et cette consolidation.
Si une source manque, livre le manque et sa consequence sans prolonger une
recherche generale. Les fixtures livrees sont des entrees candidates pour les
tests du developpeur, pas la preuve que le bot les gere deja.
