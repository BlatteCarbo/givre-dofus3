# Pack de scripts Kamas 2026 (GivreClone)

Huit scripts Lua pour Dofus 3, écrits uniquement avec les fonctions déjà utilisées par les scripts du dépôt
(`travelTo`, `gatherAll`, `fight`, `npc:npcBank`, `exchange:putAllItems`, `getJobLevel`, `onFightEnd`, etc.).
Chaque script commence par un bloc de configuration en français : `PODS_MAX`, `MAX_CONSECUTIVE_DEFEATS`, `BANK_MAP`.
Aucun script ne lit l'énergie du personnage (sans effet dans Dofus 3).

Vérifications automatiques : `dotnet test GivreClone.Tests/GivreClone.Tests.csproj --filter FullyQualifiedName~Kamas2026ScriptsTests`
(syntaxe, linter sans avertissement, chargement MoonSharp, monde simulé : banque, série de défaites, jalons, jackpots, lecture seule).
**Ces tests ne remplacent pas un essai en jeu** : aucun de ces scripts n'a été lancé sur un compte réel.

## Réglages communs

| Variable | Défaut | Rôle |
|---|---|---|
| `PODS_MAX` | 85 | % de pods qui déclenche le retour en banque |
| `MAX_CONSECUTIVE_DEFEATS` | 3 | arrêt propre après N défaites de protecteur/combat **d'affilée** ; `0` = illimité ; une victoire remet la série à 0 |
| `BANK_MAP` | 192415750 | banque d'Astrub (intérieur), déjà utilisée par les autres scripts |
| `MIN_LIFE_PERCENT` / `REGEN_MAX_WAIT` | 90 / 180 | régénération avant de repartir |

Le dépôt se vérifie : si le trajet vers la banque, l'ouverture ou le dépôt échoue, le script retente (`MAX_BANK_FAILURES`, 3 par défaut) puis s'arrête.
Le solde de kamas doit couvrir les frais de banque ; aucun retrait n'est jamais fait.

## Profil Sylvanbot (mineur) : `Mineur-1-200-Alliages-Sylvanbot.lua`

- Progression selon `getJobLevel(24)` : Fer → Cuivre → Bronze → Kobalte/Manganèse → Argent/Étain → Bauxite/Or → Silicate/Obsidienne → Dolomite.
- Les cartes sont celles du script `Mineur-1-200.lua` (mines d'Astrub puis secteurs plus profonds). Les mines d'île (Dolomite) ne sont ajoutées
  qu'avec `ISLAND_MAPS_ENABLED = true` (accès non confirmé).
- Jalons d'alliages affichés dans la console (Ferrite 20, Bakélite 60, Kriptonite 80, Rutile 120, Ardonite 160) : **le script ne fabrique rien**,
  il indique quand les crafter à la main. Seuils tirés du rapport de recherche 03, à vérifier en jeu.
- Bilan console tous les `SUMMARY_EVERY` passages, à chaque dépôt et à l'arrêt : minerais récoltés par type, jalons atteints ou manquants.
- Lancement : ouvrir la fiche métiers une fois (le niveau doit être connu), placer le personnage hors combat, démarrer le script.

## Profil Shadowbot / Bastionbot (Pandala) : `Pandala-Farm-Prudent-1-3-Mobs-Recolte.lua`

- Shadowbot (Sram 110/120) et Bastionbot (Sacrieur 160/120) : `PROFILE = "auto"` lit `getCharacterName()` ; sinon le profil `default`.
- Taille de groupe par profil dans `PROFILES` (`groupMin` / `groupMax`, bornés à 1..3 par le script) : Shadowbot 1..2, Bastionbot 1..3. Régénération à 90 %
  avant combat (Bastionbot attend jusqu'à 900 s : sa vitalité est élevée).
- Le farm couvre le secteur Plantala de Pandala (23 cartes observées dans les expéditions). À chaque carte : jusqu'à `FIGHTS_PER_MAP` combats,
  puis `gatherAll()` si `HARVEST = true` (les ressources non débloquées sont simplement ignorées).
- `MAX_CONSECUTIVE_DEFEATS` : arrêt après N défaites d'affilée, bilan final dans la console. Une victoire remet la série à 0.
- Le dépôt en banque envoie le personnage à `BANK_MAP` (Astrub). Depuis Pandala c'est un long trajet : surveiller le premier aller-retour.
- Prudence : une défaite est un signal de diagnostic. Les journaux récents montrent des combats bloqués à Pandala ; ne pas laisser tourner sans surveillance.

## Profil Paysan / Alchimiste : `Paysan-Alchimiste-1-200-Consommables.lua`

- Deux métiers lus ensemble : Paysan (28) Blé → Orge → Avoine → Houblon → Riz, Alchimiste (26) Ortie → Sauge → Trèfle → Menthe.
- Zones, dans l'ordre : Astrub → Amakna → Cania → Pandala (Riz à partir de 120). Les identifiants de carte d'Astrub viennent des scripts `Nuit-*`;
  les autres étapes sont des coordonnées `"x,y"` du rapport de recherche 03, **non validées en jeu**. L'arrivée est vérifiée par `getCurrentPos()`;
  une carte refusée est écartée pour `BLOCK_VISITS` visites.
- Pas de craft ni de vente : le bilan range les récoltes par famille (céréales / plantes) et annonce les consommables devenus fabricables
  (farines, pains, potions de rappel et de cité, potions de soin).

## Profil Bûcheron : `Bucheron-1-200-Planches-Substrats.lua`

- Frêne → Châtaignier → Noyer/Chêne → Bombu/Oliviolet → Érable/If → Bambou/Merisier → Orme/Bambou Sombre → Tremble/Bambou Sacré, selon `getJobLevel(2)`.
- Cartes : identifiants extraits des circuits de `Bastionbot-Bucheron-1-200-ameliore.lua`. Dépôt systématique à `PODS_MAX` (le bois est lourd).
- Résumé « SUBSTRATS » : le bois récolté est comparé à `SUBSTRATE_WOOD_TARGET` pour dire quand crafter les planches. Les substrats demandent aussi des
  potions d'alchimiste ; rien n'est fabriqué par le script.

## Profil Pêcheur : `Pecheur-1-200-Poissons-Rares.lua`

- Astrub → Amakna → Sufokia → Cania → Otomai → Frigost, selon `getJobLevel(36)`. Seule la zone Amakna utilise des identifiants vérifiés
  (circuits de `Bastionbot-Pecheur-1-200-ameliore.lua`) ; les autres sont des coordonnées à valider.
- Compteur de « jackpots » : chaque gain dont le nom contient un fragment de `RARE_PATTERNS` (ou dont l'identifiant est dans `RARE_GIDS`)
  est annoncé en vert et totalisé dans le bilan. Compléter les listes avec les poissons rares observés sur ton serveur.

## Audit consultatif : `Audit-Rentabilite-Brisage-Et-Lots-Readonly.lua`

**Lecture seule.** Aucun ordre d'achat, de vente, de craft, de déplacement, de banque ni de dépôt. Il lit l'état (pods, kamas, sac, prix moyens en cache)
et affiche : une liste « A FAIRE », la formule d'arbitrage par lots et un tableau de poids de runes.

- Formule : acheter le lot de 100 seulement si `Prix(100) / 10 < Prix(10) * 0.98` (`LOT_DISCOUNT`). Marge brute par 10 :
  `Prix(10) * (1 - 0.02) - Prix(100) / 10` (taxe HDV 2 %). Saisir les prix relevés à la main dans `LOT_PRICES = { { "Ortie", p1, p10, p100 } }`.
- Poids des runes (simple / Ra) : Ga PA 100, Ga PM 90, Po 51, Invocation 30, Dommages 20, Critique 10, Sagesse 3 (Ra 30), Prospection 3,
  Puissance 2 (Ra 20), Fo/Ine/Cha/Age 1 (Ra 10), Vitalité 0,25 (Ra 2,5 : une Ra Vi vaut 10 points de vitalité), Initiative 0,1 (Ra 1).
  Sources : <https://dafous.app/guides/poids-runes-fm.html> et <https://dofocus.fr/guide/>.
- Colonne « focus » : le tableau affiche `poids × FOCUS_FACTOR` (0,60 par défaut), c'est le facteur de **60 %** demandé pour ce pack. Il s'agit d'un réglage de
  lecture, pas d'une règle officielle : DoFocus décrit le focus comme 100 % pour la stat ciblée et 50 % pour les autres lignes, avec un malus global de 50 %.
  Ajuster `FOCUS_FACTOR` selon ton calcul.
- Le brisage dépend d'un coefficient serveur dynamique (1 % à 4000 %) : casser un objet test avant toute série. Le script ne lit pas ce coefficient.

## Chasseur d'anomalies temporelles : `Chasseur-Anomalies-Temporelles-Bonus.lua`

- `ANOMALY_ZONES` : liste ordonnée de zones candidates `{ name, subarea, minLevel, maxLevel, maps / ranges }`. `subarea` est l'identifiant **ou** le nom accepté par
  `anomaly:isOpen(sousZone)` (même usage que le script d'expédition généré). Par défaut : Plantala (Pandala, niveau 100+) puis Cimetière d'Incarnam (id 449, niveau 1..25).
- À chaque passage la **première** zone autorisée pour le niveau dont l'anomalie est ouverte est choisie, et l'anomalie est revérifiée avant chaque combat :
  fermeture = arrêt des combats de la zone et nouvelle sélection au passage suivant.
- Aucune anomalie ouverte : `FALLBACK_TO_DEFAULT = true` (défaut) farme la zone par défaut (`DEFAULT_ZONE`, sinon la première zone autorisée) sans bonus ;
  `false` attend (`CLOSED_WAIT_MS`) et s'arrête après `MAX_IDLE_CHECKS` vérifications à vide (0 = attendre sans limite).
- Groupes `MIN_MONSTERS`..`MAX_MONSTERS` (1..3 par défaut, bornés à 1..`MONSTERS_HARD_CAP` = 8), `MAX_CONSECUTIVE_DEFEATS` (3, 0 = illimité, une victoire remet à 0), banque à `PODS_MAX`.
- Bilan console par zone : combats sous bonus, combats en repli, victoires, défaites.
- **Non vérifié en jeu** : le nom « Plantala » n'est pas confirmé dans la base ; si `anomaly:isOpen` ne répond jamais vrai, mettre l'identifiant numérique de la sous-zone
  (onglet Expédition > anomalies). Cartes Incarnam : les sept cartes extérieures du script `Rangerbot-Cimetiere-prudent.lua`.

## Stock quotidien Almanax / artisanat : `Preparation-Almanax-Et-Artisanat-Quotidien.lua`

- `RESOURCES` : quotas par ressource (Ortie, Sauge, Trèfle, Blé, Orge, Frêne, Châtaignier, Fer, Cuivre : 100 chacune, Trèfle 60), avec métier, niveau minimal et cartes.
  Le script récolte la première ressource sous quota, passe à la suivante au quota atteint, et saute celles dont le métier n'a pas le niveau requis.
- `INITIAL_STOCK = { Ortie = 40 }` : stock déjà possédé (banque comprise), à renseigner à la main ; le script ne lit pas la banque. Les récoltes de la session sont suivies par différence de sac.
- Console : une ligne `STOCK` par ressource avec barre de progression et pourcentage, puis `QUOTAS atteints N/9`, à chaque dépôt, tous les `SUMMARY_EVERY` passages et à l'arrêt.
- Banque à `PODS_MAX` ; quand tous les quotas sont atteints (ou ressources mises de côté : cartes vides ou inaccessibles), dépôt final (`FINAL_DEPOSIT`) puis arrêt.
- Cartes : Astrub et Amakna (bois) vérifiées via les autres scripts ; Sauge, Trèfle, Blé et Orge reprennent les coordonnées `"x,y"` non validées du pack paysan/alchimiste.
- Rien n'est acheté, vendu ni fabriqué : le script prépare la matière première des offrandes Almanax et des crafts courants.

## Limites connues

- Les cartes non vérifiées (coordonnées « x,y ») sont signalées dans l'en-tête de chaque script ; un trajet refusé est écarté, jamais forcé.
- Les paliers de niveau et les jalons d'alliages viennent de rapports de recherche (`research/01` et `research/03`), pas de mesures en jeu.
- Aucun de ces scripts ne vend, n'achète ni ne fabrique : les gains en kamas viennent du stock déposé en banque, à valoriser à la main.
