# Atelier éco — craft ou acheter, forgemagie, bonnes affaires, abonnements, carnet

Ouvrir : **Outils → Atelier éco** (ou Ctrl-K, « craft »). Lecture seule côté jeu : l'atelier lit l'HDV **déjà observé** par le bot sélectionné et les données
du jeu (`dofus.sqlite`), et n'achète, ne vend ni n'envoie rien. Les fichiers sont dans `…\userdata\eco\` (`prices.json`, `notebook.json`, `fm.json`,
`subscriptions.json`).

## Craft ou acheter (inspiré de Dofus-Craft)
- Recherche d'un objet par nom (hors ligne, base du jeu), quantité, arbre de fabrication récursif : chaque ingrédient fabricable est compté au **moins cher** entre
  son prix HDV et sa propre fabrication. Boutons *Acheter / Fabriquer / Auto* par ligne pour forcer un choix.
- Résultat : coût de fabrication, achat HDV, économie (kamas et %), recommandation, revente nette de la taxe, **liste de courses** copiable, avertissement si des prix
  datent de plus de 48 h, liste des prix manquants.
- **Prix** : le plus récent entre l'HDV observé (lot le moins cher, sinon moyenne) et un **prix manuel par serveur** (saisi sur une ligne, ou **importé en collant
  une liste « nom  prix »** : tabulation, `;`, `:` ou espace ; `12 500`, `12.500`, `12,5k`, `1,2m`).
- **Objets suivis** : tableau de rentabilité (verdict, économie, coût fabrication / achat) pour tous les objets suivis.

## Forgemagie (inspiré de STAKK et Dofus-Craft)
- **Poids** d'un objet du sac : le poids par point vient des données du jeu (`EffectData.effectPowerRate` : PA 100, PM 90, Portée 51, Invocation 30, Dommages 20,
  Sagesse 3, Force 1, Vitalité 0,2…). Poids maximum optionnel → pourcentage atteint. **Aucune formule de réussite** : elle dépend des jets maximums, absents hors ligne.
- **Runes** (Ba = base, Pa = ×3, Ra = ×10 pour Fo, Ine, Cha, Age, Sa, Vi, Pod, Ini) avec poids et prix ; autres runes volontairement non calculées.
- **Mélange** : décompose une hausse de points en Ra / Pa / Ba (le moins de runes) et estime le coût.
- **Séances** : achats de runes (alerte si le prix s'écarte de plus de 25 % du prix connu), runes utilisées, dépense totale, prix moyen, coût par rune utilisée, stock.

## Patrimoine
Valeur estimée du sac et de la banque (objets regroupés × prix HDV connu), kamas, et les objets les plus chers de chacun. Indicatif : objets sans prix non comptés, banque à ouvrir au moins une fois.

## Bonnes affaires
Lots dont le prix unitaire est nettement sous la moyenne du marché, d'après les prix déjà reçus (filtres : remise, gain du lot, âge). Indicatif : la moyenne et le lot ne
datent pas forcément de la même minute.

## Alertes de prix
Objet + condition (passe sous / au-dessus de) + prix cible : une alerte Discord part (webhook du personnage) quand un bot **observe** ce prix à l'HDV (prix de marché reçu depuis moins de 3 h ; un prix saisi à la main ne déclenche rien). Anti-spam : 6 h, ou 10 % de plus dans le sens de l'alerte.

## Abonnements
Fin d'abonnement par compte, jours restants, niveau (OK / Bientôt / Urgent / Expiré), **alertes Discord à 7, 3 et 1 jour(s) puis à l'expiration** (webhook du personnage).
Le jeu n'envoie pas toujours la date de fin (`AccountInformationUpdateEvent` n'apparaît pas dans les sessions 3.7.12 observées) : elle peut être **saisie à la main** (✎) ;
une valeur reçue du jeu la remplace. Carte « Abonnement » sur le tableau de bord.

## Carnet
Positions (coordonnées copiables), notes, messages prêts à coller (copie dans le presse-papiers ; rien n'est envoyé au chat du jeu).

## MCP (lecture seule)
`get_craft_advice`, `get_market_deals`, `get_subscriptions`, `get_wealth` — mêmes calculs que l'interface (service `EcoService`).

## Limites connues
- Les prix viennent de ce que le bot a vu ; sans HDV ouvert récemment, seuls les prix manuels existent.
- La valeur d'un craft ignore les taux de succès, les kamas de recette et les quantités produites par craft (toujours 1).
- Forgemagie : pas de simulateur de réussite ; la table des runes couvre les caractéristiques de base seulement.
- Tests : `GivreClone.PureTests` (CraftAdvisor, Forgemagie, FmLedger, MarketDeals, SubscriptionTracker, PriceImport) et `GivreClone.Tests`.

## En script (Lua)

Table `eco`, mêmes calculs que l'interface : `eco.find(texte, limite)`, `eco.price(gid)`, `eco.setPrice(gid, kamas)`, `eco.craftAdvice(gid | "nom", quantité)`,
`eco.wealth()`, `eco.deals(minRemise, minGainLot, ageMaxMin)`, `eco.subscriptions()`, `eco.addAlert(gid, "below"|"above", prix)`, `eco.removeAlert(gid)`, `eco.alerts()`.

## Création et suppression de personnage en script

- Lua superviseur : `accounts:createCharacter(compte, { server = "Brial", breed = "Cra", sex = "f", name = "", head = 0, body = 0, colors = { hair = "#3A6FD8" }, leaveTutorial = true })`
  et `accounts:deleteCharacter(compte, { server = "Brial", name = "Perso", answer = "réponse secrète" })` (compte Socket avec code secret uniquement).
- MCP : `create_character`, `delete_character` (`confirm=true` obligatoire).
- `head` / `body` = rang (1..n) parmi les choix de création ; 0 ou absent = au hasard. Nom vide = nom aléatoire proposé par le serveur.

## XP des métiers et chasse au trésor (idées reprises de dofus-miniatures, avec l'accord de son auteur)

- **XP métiers** (onglet « XP métiers », MCP `plan_job_xp`, Lua `eco.jobPlan`, téléphone > Éco) : plan hors ligne pour monter un métier ; à chaque niveau, la recette la moins chère par XP
  (prix de l'Atelier) ou la plus rapide, nombre de crafts, XP, coût et liste de courses. Formules du jeu : niveau = ⌊(√(1+0,4·xp)+1)/2⌋, gain = ⌊20·niveauRecette/(((niveau−niveauRecette)^1,1)/10+1)·ratio⌋.
- **Chasse au trésor** (MCP `find_treasure_clue`, Lua `treasure.findClue(s)`, téléphone > Chasse) : depuis une position et une direction (0 droite, 2 bas, 4 gauche, 6 haut), la carte la plus proche
  qui porte l'indice. Données de l'API publique DofusDB (seuls la position et la direction partent), validées puis mises en cache sur disque.

- **Ressources** (onglet « Ressources », MCP `find_resource_maps`, Lua `eco.resources` / `eco.resourceMaps`, téléphone > Récolte) : les 86 ressources de récolte (bois, minerais dont 16, plantes, céréales, poissons, eau)
  et leurs 7 773 emplacements, classés depuis la carte du bot (ou depuis des coordonnées). Données embarquées (`GivreClone.App/Data/resource_map.json`, régénérables par `tools/gen_resource_map.py`) :
  ce sont des points de récolte recensés, pas la disponibilité en direct.
- **Script de récolte** (bouton dans l'onglet Ressources, MCP `make_gather_script`, Lua `eco.resourceRoute`, téléphone > Récolte) : génère un script Lua prêt à lancer pour une ressource — un circuit de cartes
  riches et proches du bot (tournée du plus proche voisin), `ITEMS_TO_GATHER`, `travelTo`, `gatherAll`, puis on recommence. Enregistré sous « Récolte <ressource> ». À vérifier avant de le laisser tourner seul (pas de banque).
