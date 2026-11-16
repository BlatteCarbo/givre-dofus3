# Performances GivreClone - 31 octobre 2026

## Verification complementaire en fenetre restauree

Revue ciblee finale APPROVE pour B2 : changement de bot, annulation
du demarrage et verrouillage des selections corriges ; B1 deja clos.
Rapport : %USERPROFILE%/Downloads/.omo/evidence/givre-performance-20261031-gate-review.md.
Le relecteur a inspecte code, tests et captures sans reexecuter les tests.
Ce verdict ne valide ni le cavalier live ni le phorreur, encore ouverts.

Seconde passe de revue : le changement de bot ne stoppait que Running,
et les controles du sac hors du bloc Mode restaient actifs pendant Ready.
Correction : AttachPid annule desormais Busy (Running ou Starting) ;
WorkshopColumns entier est desactive pendant Starting, en laissant
Arreter hors de cette zone. Regression changement A -> autre bot -> A
avant publication de Ready ; assertions UI sur la zone globale et
l'inventaire aux deux largeurs, avec captures starting.
Logique : 9 tests passent. UI : 2 echecs sur largeur760 (clair/sombre),
assertion WorkshopInventoryTabs.IsEffectivelyEnabled encore vraie.
Le contenu Sac est deplace entre hotes par le code de layout compact ;
la desactivation du conteneur seul ne couvre pas ce controle.
Correction : liaison !Starting directement sur WorkshopInventoryTabs.
Nouvelle suite UI sous mon_57GD6ZF2XEXB727D : 14 reussites, aucun
echec ni ignore, exit0, 42s ; aucun test affaibli.

Revue finale : deux blocages trouves et corriges au demarrage FM.
InvalidDataException est gere comme une archive illisible, sans lancer
d'action. L'attente de Ready est maintenant annulable par Stop ; etat
Starting/Busy expose pour activer Arreter et figer les options manuelles,
refuser un second demarrage MCP avant toute mutation de ses parametres.
Regression deterministe avec TaskCompletionSource, sans temporisation :
double attente refusee, Stop termine l'attente avant meme la fin du
chargement, puis une nouvelle attente est possible. Validation finale
unitaire : 3373 reussites, 2 ignores preexistants, total3375, exit0,
1min09. Suite UI finale : 14 reussites, aucun echec ni ignore, exit0,
1min01. Ces resultats valident le code et les fixtures, pas le rendu
live du cavalier ni le chargement du dernier correctif dans l'app ouverte.

Mapping couleurs : test du paquet reel RED (Assert.Contains, exit1),
puis indexed_colors=5 dans les deux catalogues et empreinte actualisee.
Les 21 tests cibles apparence/combat/historique passent (exit0) avant
l'ajout de l'etat Starting/Busy. Les nouveaux looks de carte mettent
a jour uniquement l'acteur CharacterId, jamais un autre joueur.

Profil Windows `dotnet-sampled-thread-time` termine : collecte exit0,
rapport top20 exit0. Le rapport ne suffit pas a attribuer une baisse CPU
aux corrections. SurfaceRenderTarget.Blit represente 0,26 % exclusif,
EglContext.MakeCurrent 0,04 % dans cet echantillon.

Navigation reelle Inventaire puis Forgemagie dans le processus 22172 :
panneaux rendus, aucune session FM declenchee, aucun achat ni changement
d'equipement. L'apparence n'est PAS validee : la capture utilisateur
`Capture d'écran 2026-10-31 190836.png` montre un cavalier sur une monture
violette et blanche ; le panneau affiche encore une monture grise seule.
Le mapping EntityLook duplique le champ 1 pour bones_id et indexed_colors.
Le premier est prouve par les paquets de coffre ; les autres champs
necessitent une preuve avant correction. Investigation distincte en cours.

## Demande

Terminer les travaux restants sans alourdir le bot. Le delai d'ouverture
est maintenant prioritaire, avec distinction entre lancement du processus
et retour sur une fenetre existante.
L'utilisateur a precise : retour sur une fenetre deja ouverte.

## Mesures initiales

- Processus live 19016 : environ 848 Mio de working set moyen sur 17 points.
- Allocations : moyenne 4990026 octets/seconde sur 15 points, hors charge
  de forge. CPU utilisateur + systeme : environ 0,077 seconde/seconde
  (environ 1 % de la machine a huit processeurs logiques).
- MCP ponctuel : get_app_status 10200 ms, get_bot_state 114 ms,
  read_forge_history(limit=1) expire a 15002 ms.
- Le CPU moyen ne prouve pas une saturation constante. Les gels sont
  mesures separement, pas deduits du seul working set.

## Defauts reproduits et corrections en cours de verification

1. La pagination d'historique deserialisait tous les fichiers pour chaque
   requete et pour retrouver une session. Reproduction deterministe :
   `Small_page_does_not_open_commits_beyond_its_lookahead` echoue avant
   correction en ouvrant un commit verrouille situe bien apres la page.
   Apres correction : 51 tests historique/comptabilite reussis, aucun echec.
   La continuite des noms est toujours verifiee ; les donnees de chaque
   commit lu restent validees et le chargement initial valide l'archive.
2. Pile live du thread UI : BotsViewModel.Tick -> AttachPid ->
   RebuildPicks -> GameDb.Scan -> SqliteValueReader.GetValue.
   Les recherches de ressource et de competence forge materialisaient
   des tables entieres pour chaque objet. Le test
   `Resource_lookup_does_not_materialize_the_entire_skill_table_for_each_item`
   echoue avant correction. Remplacement par requetes SQL ciblees, sans
   cache de catalogue supplementaire ; validation apres correction a faire.
3. Une vue Bots retiree continuait a faire tourner sa minuterie combat
   de 300 ms. Reproduction sur la vraie vue avec attachement/detachement
   headless : test rouge avant correction. Le correctif arrete et redemarre
   les minuteries avec la vue, detache le redimensionnement de la fenetre
   et le suivi de console Lua au depart. Verification verte a faire.
4. Le processus QA avec une copie de l'archive reste sans fenetre apres
   plus de 160 secondes ; une pile le situe dans ForgeHistoryPersistence.Read
   depuis le constructeur de LaunchService/Stores. WaitForInputIdle avait
   retourne trop tot (fenetre=0), ce n'est donc pas une mesure valide
   d'ouverture a 2 secondes. Le chargement de l'archive est maintenant
   optionnellement en arriere-plan pour LaunchService ; lectures/ecritures
   restent bloquees sur sa fin complete, pas sur une vue vide partielle.
   L'interface indique le chargement ou l'erreur, et les commandes FM
   attendent sans bloquer le dispatcher.

## Interface et achats

- Une recherche HDV fournit maintenant les prix de tous les lots d'une
  rune ; l'ancienne boucle refaisait la meme recherche pour 100,10,1.
  L'achat garde sa propre revalidation fraiche et son plafond exact.
- Les filtres manuels supports/runes sont captures au depart, sans
  charger de catalogue entier a la construction de la vue.
- L'API MCP distingue starting/running/not-started, au lieu de laisser
  croire qu'une commande acceptee a forcement cree une tentative.
- La capture 105632 montre une monture seule. Le parseur de look actuel
  ignore sub_entities ; test de cavalier ajoute, correction et QA encore
  a faire. Le format de sous-entite est documente dans le code primaire
  du renderer : https://github.com/PyDofus/d3-ts-renderer/blob/main/src/look/look.ts

## Limites et conservation

- Aucun achat reel supplementaire pour les tests de performance.
- Le test de demarrage utilise une copie de l'historique sans comptes.
- Les mesures avant/apres sur le binaire corrige restent a effectuer.
- La sauvegarde memoire externe reste refusee par la modification tierce
  non commitee de reference/givre-fm-parity.md. Ce fichier n'est pas touche.

## Validation apres corrections

- 3369 tests metier PASS,2 SKIP existants ;14 tests headless PASS.
  Un debordement decimal dans PlanExploration a ete detecte par la suite
  puis corrige par ordre de calcul, sans retirer le test.
- SQL : le test d'allocations de recherche ressource est vert.
- Minuterie : le test attach/detach/reattach de la vraie BotsView est vert.
- Apparence : test du cavalier en sous-entite rouge avant, vert apres
  ajout du parseur et de sa serialisation. Validation du rendu live encore
  a terminer ; aucune apparence ni aucun equipement n'a ete force.
- Release build exit0. Premiere fenetre native apres31810ms sur le profil
  live ; ne pas annoncer un demarrage instantane. Les autres services de
  demarrage ne sont pas attribues a l'archive sans autre profilage.
- Reduction/restauration reelle du processus22172 : acquittement13ms,
  requete UI suivante3ms et capture du corps complet. Fenetre initialement
  reduite aux coordonneesWindows -32000,-32000 ; aucune preuve d'une
  sauvegarde de position erronee dans le code.
- get_app_status :49ms puis3ms ; get_bot_state :2ms ;
  read_forge_history(limit1) :327ms, avec total484644/plafond500000
  et plancher335000 toujours conserves.
- Premiere collecte apres : working set moyen742849536octets,
  allocations1964455octets/s ; elle inclut une navigation, pas du repos pur.
- Collecte stabilisee, personnage MITM connecte : working set moyen
  770475008octets (environ735Mio), allocations2693944octets/s,
  CPU0,196875seconde/seconde, soit environ2,46% des8 processeurs logiques.
  L'activite, le mode et le premier plan different du relevé initial :
  aucune amelioration CPU causale n'est revendiquee. Le profilage restant
  doit expliquer les points chauds avant conclusion.
