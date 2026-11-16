# Missions paralleles pour Chat On Steroids

## Prompt de coordination pret a coller

Tu es le coordinateur de recherche Dofus pour GivreClone. Lis `PROMPT.md` et `MODELE_FICHE.md` dans ce dossier. Produis des specifications prouvees et des scenarios reproductibles pour le developpeur qui travaille en parallele. Les outils CoS reellement exposes font foi ; utilise des agents independants si disponibles, sinon traite les missions successivement.

Commence par M01 et M02, puis M03 et M04, puis les autres missions. Fais un premier lot borne : une mecanique precise par mission et au maximum trois videos pertinentes par mecanique. Tu peux rechercher les videos si l'utilisateur n'en a pas fourni ; note leur version et explique leur pertinence. Le budget disponible permet de recouper davantage, pas de multiplier les rapports sans nouvelle preuve.

Chaque agent ecrit exclusivement dans `rapports/Mxx/`. Un seul coordinateur met a jour `rapports/SYNTHESE.md`. Aucune modification du code, des configurations ou des donnees du bot. Aucune connexion de compte, commande au jeu, publication ou operation Git. Si l'acces au code est disponible, lecture seule pour identifier le point d'integration ; une proposition n'est pas une fonctionnalite implementee.

Chaque mission livre `RAPPORT.md`, les fiches du modele et `PASSAGE_DEVELOPPEUR.md`. Ce dernier contient les trois regles prioritaires, leurs preuves exactes, les entrees/transitions necessaires au simulateur, un cas positif, un cas negatif et les inconnues bloquantes. Les tests proposes restent marques non executes. Une mission est terminee lorsque son cas precis est documente ou lorsque les preuves manquantes sont identifiees explicitement ; ne declare pas toute une classe couverte.

Dans la synthese, indique mission, mecanique traitee, fichier, version source/cible, regles retenues, contradictions et prochaine verification. Examine les passages cites avant de retenir les conclusions des agents. Reprends les collectes interrompues depuis leur dernier intervalle, sans recommencer les videos deja traitees.

## Missions

| ID | Sujet et question precise | Livrable attendu |
| --- | --- | --- |
| M01 | Sram terre : un reseau avec declenchement et deplacement force. Dans quel ordre les pieges, mouvements et degats se resolvent-ils ? | Etat initial, positions relatives, ordre exact, obstacle ou ordre inverse qui fait echouer le reseau. Distinguer activation, declenchement et retrait d'une marque. |
| M02 | Roublard : une sequence de mur puis detonation. Que changent l'age des bombes, leur mort et l'ordre d'explosion ? | Chronologie sur plusieurs tours, conditions du mur, ressources et propagation ; inconnu explicite pour toute formule non prouvee. |
| M03 | Xelor : une sequence courte qui cree puis exploite un Telefrag. Quelles positions et quels historiques doivent etre conserves ? | Transitions acteur/cible, conditions de creation et consommation, couts/remboursements prouves et contre-exemple de placement. |
| M04 | Eliotrope : un tir a travers plusieurs portails. Comment la sortie est-elle choisie et comment obstacle ou portail ferme modifie-t-il le trajet ? | Schema relatif, ordre de routage, conditions de portee et ligne de vue, exemple negatif distinguant les hypotheses de selection. |
| M05 | Sacrieur : placement puis attaque, par exemple permutation/attirance si disponibles dans la version examinee. Quand un boost est-il reellement rentable ? | Comparaison de deux plans avec PA/PM, beneficiaire, attaques rendues possibles, exposition et repli si le placement est refuse. |
| M06 | Compagnons : Korbax en priorite. Quelles actions de degats, soutien et placement sont legales et utiles ? | Identites de sorts seulement si prouvees, contraintes et beneficiaires ; scenarios expliquant un tour sans attaque sans attribuer arbitrairement la cause au bot. |
| M07 | Challenges : Duel et De la meme lignee, avec personnage et compagnon. Quelle attribution d'acteur/cible faut-il suivre ? | Regles exactes versionnees, scenarios a deux acteurs et deux cibles, cas de reussite/echec et incertitudes de comptage. |
| M08 | Synergie de quatre comptes : une sequence de preparation et execution. Que vaut-elle quand un allie joue avant/apres le placeur ? | Ordre des tours, ressources separees, gain par rapport aux actions independantes et plan degrade si une etape devient impossible. |
| M09 | Feca : un glyphe et son interaction avec teleportation, tacle et debut/fin de tour. Quel evenement declenche chaque effet ? | Chronologie de pose/entree/sortie/tour et contre-exemple ; distinguer observation et inference. |
| M10 | Degats de poussee : une situation ou ils concurrencent une attaque directe. Quels obstacles et resistances changent le choix ? | Donnees necessaires au calcul, collision et ordre des effets ; formule citee ou valeur inconnue, jamais extrapolee depuis un seul jet. |
| M11 | Un monstre ou un boss de donjon : invulnerabilite et declencheur de vulnerabilite. | Machine a etats, action/cible requise, duree, echec dangereux et scenario de test ; couverture limitee au monstre choisi. |
| M12 | Couverture des classes restantes : choisir une classe encore non traitee, par exemple Zobal. Quelle mecanique specifique manque a un simple profil de degats ? | Une mecanique prioritaire, son benefice, ses contraintes et un test discriminant. Puis seulement choisir la classe suivante. |

## Premiere livraison utile

Rends M01 et M02 des qu'elles sont pretes sans attendre toutes les missions. Une fiche courte avec un cas reproductible et ses limites aide davantage qu'un catalogue general de combos. Le developpeur pourra integrer une transition pendant que les recherches suivantes continuent.

## Si des journaux sont fournis ensuite

Une mission separee peut analyser des extraits de combats expurges : identifier acteur, decision, confirmation serveur et effet observe. Distinguer temps de planification, reseau et animation. N'invente pas de donnees absentes ; une video seule ne mesure pas le temps CPU du planificateur. Produire une liste de cas reproductibles plutot qu'une correction speculative. Cette mission reste en attente tant qu'aucun extrait n'est fourni.
