# Refonte visuelle et historique FM

## Demande et reference

Demande : historique trop bavard et ralentissant le PC, interface trop sombre,
reprendre l'interface Dofus 3 dans le bot. Reference fournie :
`%USERPROFILE%/Pictures/Screenshots/Capture d'écran 2026-10-30 225812.png`.
La capture est un atelier de Costumagie : historique etroit a gauche,
table de caracteristiques au centre, inventaire distinct a droite,
panneaux ardoise violette et action principale vert anis.

## Incident reproduit

Le processus de test PID 18688, fenetre 2361494, ne repondait plus :
`stop_smith_leveling` a expire apres 15 secondes ; capture native expiree
apres 60 secondes. Arret cible du processus, puis absence confirmee par
Get-Process. Aucun autre processus termine.

Chemin couteux observe : `ForgeHistory.Changed` publie un appel dispatcher
par evenement. `RefreshForgeHistory` vide et reconstruit jusqu'a 2000 lignes,
y compris chaque rune ; les changements de CanStart relancent aussi ce travail.
L'historique XAML sans hauteur bornee agrandit toute la page.
Le journal de performance passe de 2823 Mo alloues/minute a 22:43:40
a 13352 Mo/minute a 23:00:40. C'est du debit d'allocation, pas 13 Go de
memoire residente : le working set est alors de 1296 Mo.

## Contrat de disposition

Layout: atelier FM - historique qui agrandit toute la page -> list-detail et
scroll-body-shell ; reference locale Dofus 3 ci-dessus ; scroll owners:
liste historique bornee, table atelier bornee et inventaire borne ;
anti-patterns : aucune liste infinie dans un StackPanel non borne,
aucun repositionnement global a chaque rune.

Action principale : lancer la session. Arret directement accessible.
Priorites : objet/metier et progression, table des effets, inventaire/supports.
Conserver objectifs, exos, montee adaptative, budgets, runes, fusion/devis,
essai de brisage, files et console. Les commandes secondaires peuvent etre
rangees dans des onglets ou sections repliables.

## Extension a l'ensemble du bot

La demande suivante elargit l'etude a tous les layouts Dofus. L'inventaire
complet du client est un livrable separe ; aucun chemin simplement trouve
dans un bundle ne doit etre presente comme un ecran entier reconstitue.
La palette et les modeles de controles partages s'appliquent a tous les
ecrans du bot, pas uniquement a SmithMagicView.

Direction :
- Produit : piloter et comprendre les sessions de jeu et leurs objectifs.
- Caractere : atelier, precis, vivant.
- Univers : interfaces d'inventaire et de metiers Dofus, surfaces mates.
- Moments visuels : objets/caracteristiques et resultats mesures, pas le fond.
- Signature : table d'effets compacte et action principale vert anis.

Audit initial commun :
- Controls.axaml : boutons principaux avec degrade et halo bleu, survol
  qui agrandit le bouton ; remplaces par surfaces unies et retour sobre.
- MainWindow.axaml : acrylic noir et halo radial sur toute la fenetre ;
  remplaces par fond opaque ardoise.
- MainWindow.axaml.cs : particules et overlay saisonnier au demarrage ;
  plus de decor lance automatiquement pendant le travail.
- Theme.axaml : textes secondaires gris sur noir et fonds presque noirs ;
  rampe plus claire, trois roles de texte et bordures discretes.
- SmithMagicView : historique non borne et empilement de panneaux ;
  disposition atelier avec scroll interne et options secondaires rangees.

Lecture complementaire : unslop de Damir Sokolovsky (@heydamir), CC BY 4.0,
https://github.com/heydamir/unslop ; principes adaptes, pas de code importe.
Les contraintes natives et la reference Dofus fournie priment sur les
exemples de landing pages web du skill.
UI/UX Pro Max et ibelick/ui-skills sont aussi consultes pour accessibilite,
alignement, densite et performance ; aucune installation globale implicite.

## Verification native du lot

- Suite Release : 3238 reussites, 0 echec, 2 tests deja ignores ; duree 30 s.
- Compilation du banc ForgeUiStress : code de sortie 0, aucune erreur.
- Banc natif isole : 10000 evenements, une ligne de bilan, 292 ms et
  8223640 octets alloues. Ces mesures concernent ce run et incluent le
  rafraichissement affiche ; elles ne constituent pas un benchmark universel.
- Capture bureau frame-18 : effets alignes, activite et inventaire avec scroll
  interne, commandes conservees. Le bilan regroupe est visible.
- Capture compacte frame-19, contenu 955 x 632 : inventaire deplace dans
  un onglet central, objectifs et commandes du bas accessibles sans
  debordement horizontal.
- Donnees simulees explicites ; aucun compte connecte dans le banc,
  aucune rune passee et aucune depense pour ces controles.
- La validation de l'application complete et des autres ecrans reste a faire.

Hallmark et Impeccable ont aussi ete lus a la demande de l'utilisateur.
Leurs principes de conservation du contenu/cablage et de controle des
debordements sont utiles ; leurs controles navigateur ne prouvent pas le
rendu AXAML natif. Le tri des outils non visuels est consigne separement.

## Palette globale

Fond #2B2C40 ; panneau #37394F ; controle #44475F ; survol #535771.
Texte principal #F2F5FB ; secondaire #E0E2F0 ; discret #BEC3DA.
Action atelier #C2D957, texte #252B18. Source unique : Styles/Theme.axaml.
Ces couleurs sont une adaptation visuelle de la capture, pas des valeurs
pretendument extraites du client.

## Verification requise

Tests deterministes de regroupement et bornage ; compilation et suite complete.
Application native a 1460x920 et largeur minimale 960, historique vide puis
charge de plusieurs milliers d'evenements sans achat ni rune de jeu.
Verifier les commandes et la reactivite MCP pendant la charge.
Pas de Lighthouse : application Avalonia native, pas une page web.
Pas de reprise automatique de forge pendant la refonte.
Banc d'essai natif : `tools/ForgeUiStress/ForgeUiStress.csproj`, donnees
synthetiques uniquement, profil isole `forge-ui-stress`, 10000 evenements,
attente du vrai changement de collection avant assertion d'une seule session.
La fenetre reste ouverte pour les captures et les essais de redimensionnement.

## Continuite

Le dernier palier confirme avant incident etait Costumage 10, depense globale
156/200000 kamas. Ne pas extrapoler le niveau ou le solde final apres incident :
relever les valeurs serveur a la reconnexion avant toute nouvelle depense.
Le joaillomage 60 precedent reste termine (233382/300000 kamas).
Depot memoire bloque par reference/givre-fm-parity.md non commis ; ne pas
commettre ni effacer cette modification etrangere.
