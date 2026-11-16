# Foragerbot : preparation du stuff demande

Utilisateur : acheter https://d-bk.net/fr/d/15YjI, bons jets vita/agi/dommages/puissance/PA/PM/PO. Resistances et prospection non prioritaires. Gelano avec PA obligatoire. Puis groupes plus forts et plusieurs monstres.

Lien consulte : PANDAWA 110 PVM AIR, equipement16101480. GID resolus via catalogue application : Collier DC8273, Ftalmo18689, Gelano2469, String Leolhyene18428, Mules DC8278, Coiffennex18427, Lames Ortimus1040, Anneau DC8271, Cape Desert18426, Acrobate12713, Equilibriste13764, Ravageur Air12686, Cawotte972. Dragodinde Ebene33020 dans catalogue courant ; modalite equipement/monture non verifiee.

Aucun achat ni equipement effectue. Foragerbot arrete hors combat a Bonta212600837, banque/dialogues fermes, solde1996942, PV1030, energie351. Ne pas le relancer automatiquement sur le script Piou seul : nouvelle demande utilisateur en cours.

Offres renvoyees, pas preuve du minimum global ni de bons jets : Collier63999, Ftalmo4199999, Gelano175000, String59000, Mules86000, Coiffennex120000, Lames56500, Anneau125000, Cape155555, Acrobate120994, Equilibriste135899, Ravageur173483, Cawotte679999. Somme6151428 hors monture. Banque cache406897, aucune piece cible dans328piles consultees. Budget maximum demande via question asynchrone, sans reponse au dernier controle.

Limite bloquante du runtime : BidHouseStore._offers conserve UN tuple(uid,prix[]) par GID, ecrase les variantes. LaunchService.Packets jzn ne conserve pas les effets ; sale:buyItem recherche a nouveau par GID et achete la variante ecrasante, sans selection de jet. Ne pas utiliser pour ce besoin. Hook Lua de lecture prouve plusieurs Gelano dont certains sans PA (ex effet vita seul), dautres PA/vita/PM/PO. Achat specifique par UID et controle des effets avant achat requis avant execution fiable.

Autre limitation observee : enchainement de categories dans la meme session peut retourner -1 malgre offres presentes. Reouvrir npc:npc(-1,6) entre chaque recherche a permis de recevoir les huit derniers prix en08:27:48-08:27:56UTC. -1 ne signifie pas rupture de stock.

Dernier curseur2258. Script diagnostic scripts/foragerbot_stuff_inspection.lua : lecture seule, aucun achat. Aucun changement sur code applicatif, aucun redemarrage. Aucun test de mobs plus forts avant equipement.

Budget utilisateur confirme : plafond total2 000 000 kamas. Ecarter le chiffre Ftalmo4,2M comme prix achat fiable ; utilisateur le signale comme faux prix moyen. Aucune autorisation de financement depuis un autre compte. Achat toujours bloque par selection fiable des jets.

## Achats effectués à la reprise

Nouveau plafond utilisateur : **1 500 000 kamas**. Application active HdVOffers-20261026, achat par UID avec vérification prix/effets et confirmation serveur. Script `foragerbot_stuff_achat.lua`, achats tous confirmés, cumul **1 403 373 kamas**, solde **593 569** contre 1 996 942 avant achat. Pas de retrait bancaire ni financement par un autre compte.

13 objets reçus et équipés, chaque équipement confirmé par le serveur : Collier DC 69 000 (171 vita, 28 puissance, PA), Ftalmo 35 999 (159 vita, 27 agi, PO), Gelano 105 000 (**PA et 165 vita**), String 28 000 (84 vita, 40 agi), Mules DC 38 000 (78 vita, 28 puissance, PM), Coiffennex 50 000 (78 vita, 49 agi), Lames Ortimus 54 999 (50 agi, PM), Anneau DC 81 000 (170 vita, 28 puissance), Cape Désert 20 999 (64 vita, 53 agi, PO), Acrobate 120 994, Equilibriste 135 899, Ravageur Air 173 483, Cawotte 490 000 (60 sagesse).

Caractéristiques observées après équipement : 1873/1873 PV, 11 PA, 5 PM, 767 agilité, 84 puissance, 3 PO. Ancien équipement conservé au sac. Aucune relance du farm ni combat plus fort. Dragodinde Ébène encore absente : inspection de l'HDV des créatures de Bonta en cours ; budget restant pour elle **96 627**, pas le solde complet du compte.

### Terminé, monture comprise

L'HDV des créatures est accessible directement depuis **212601345**, action PNJ -1/6 ; son bâtiment intérieur mène aux services d'élevage et n'est pas le point d'achat. Dragodinde Ébène niveau100 (300 vitalité, 100 agilité), offre58620 à **50 000**, achetée et reçue UID6681300 puis équipée avec confirmation serveur. **Total final 1 453 373**, marge **46 627** sous le plafond, solde **543 569**. Les 14 objets de la liste sont achetés et équipés. Aucun ancien équipement vendu, aucun autre compte débité.

