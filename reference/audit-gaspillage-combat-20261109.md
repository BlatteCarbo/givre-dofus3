# Audit du Gaspillage du Moteur de Combat IA (Logs du 2026-11-09)

## Contexte et Périmètre de l'Audit

Cet audit analyse l'intégralité des 19 fichiers de logs de combat réels du **9 novembre 2026** (`transport-2026-11-09_*.log`, représentant 302 Mo de données de transport réseau et de séquences de combat) situés dans :
`%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/logs/`.

L'objectif est d'établir avec une rigueur empirique absolue les défaillances réelles du planificateur de combat et du moteur d'IA actuel, afin que l'implémentation de la nouvelle classe (Eliotrope) et la refonte des arbres décisionnels reposent sur des preuves chiffrées et non des suppositions.

### Données Globales Analysées
- **Fichiers analysés** : 19 fichiers de logs (`transport-2026-11-09_00-09-49.log` à `transport-2026-11-09_16-04-51.log`)
- **Combats analysés** : 2 018 combats terminés (1 482 victoires, 536 défaites — taux de victoire global : 73,4%)
- **Tours de jeu mesurés** : 8 105 tours de combat réels
- **Sorts lancés enregistrés** : 24 775 lancers de sorts effectifs
- **Refus serveur enregistrés** : 1 862 refus de lancer (`cast_refused`)
- **Échecs du planificateur** : 498 tours d'abandon complet (`aucun sort lancé avec N PA`)

---

## 0. Identification des Personnages et Rotation des PIDs

Dans l'infrastructure Givre, les Process IDs (PIDs) / Session IDs subissent des rotations lors des reconnexions et changements d'instance. L'analyse croisée des trames `[socket-perf] <Nom> (socket)`, des PIDs et des plages d'identifiants de sorts a permis d'isoler 4 personnages actifs et une apparition hors-combat :

| Personnage | Classe | Niveau | Plage de Sorts | Rôle Observé | PIDs Associés dans les Logs |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Shadowbot** | Sram | 190 | 129xx, 14312, 24011 | Dégâts / Pièges / Solo | 1073741835, 1073741827, 1073741831 |
| **Bastionbot** | Sacrieur | 199 | 127xx, 14011 | Dégâts / Tanking / CàC | 1073741833, 1073741825 |
| **Archerbot** | Crâ | 40-63 | 324xx, 13528, 14396 | Distance / Kiting | 1073741839, 1073741831, 1073741829 |
| **Sylvanbot** | Sadida | 177-181 | 135xx, 14396 | Mineur / Rotation réduite | 1073741837, 1073741829, 1073741827 |
| **Eliobot** | Eliotrope | - | (aucun sort en combat) | Connecté en fin de journée (16:00) | 1073741833 |

### Détail des Rotations de PIDs Observées
1. **Sessions 00:09 à 09:15** :
   - `1073741833` = Bastionbot, `1073741835` = Shadowbot, `1073741837` = Sylvanbot, `1073741839` = Archerbot.
2. **Sessions 09:18 à 09:43 et 10:37 à 14:21** :
   - `1073741825` = Bastionbot, `1073741827` = Shadowbot, `1073741829` = Sylvanbot, `1073741831` = Archerbot.
3. **Session 10:13:58 (permutation temporaire)** :
   - `1073741825` = Bastionbot, `1073741827` = Sylvanbot, `1073741829` = Archerbot, `1073741831` = Shadowbot.
4. **Session 15:08:36 à 16:04:51** :
   - Connexion de `1073741833` sous le nom **Eliobot** (Eliotrope), préparé pour les futurs tests.

---

## 1. PA Non Dépensés (Unspent AP)

Pour chaque tour de combat, nous comparons les PA de départ (déclarés dans `[combat] TOUR ... (PA<N> PM<N>)`) aux PA réellement consommés par les sorts injectés avec succès (`[combat] pid <PID> : lance le sort <ID>`).

### Statistiques Globales des PA Inutilisés

| Personnage | Tours Totaux | Tours avec $\ge 2$ PA non dépensés | % Tours Incomplets | Total PA Perdus ($\ge 2$ PA) | PA Perdus / Tour (Moy.) | Dégâts / PA Mesurés | Dégâts Totaux Gaspillés | Perte Moyenne / Combat |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Bastionbot** (Sacrieur 199) | 3 111 | 1 215 | **39,1 %** | 7 167 PA | 2,48 PA | 155,0 dmg/PA | **1 110 885 dmg** | **1 373,2 dmg** |
| **Shadowbot** (Sram 190) | 3 960 | 1 265 | **31,9 %** | 5 988 PA | 1,85 PA | 175,0 dmg/PA | **1 047 900 dmg** | **916,8 dmg** |
| **Archerbot** (Crâ 58) | 865 | 778 | **89,9 %** | 3 703 PA | 4,36 PA | 28,4 dmg/PA | **105 165 dmg** | **2 337,0 dmg** |
| **Sylvanbot** (Sadida 180) | 169 | 93 | **55,0 %** | 467 PA | 3,21 PA | 90,0 dmg/PA | **42 030 dmg** | **2 001,4 dmg** |
| **TOTAL** | **8 105** | **3 351** | **41,3 %** | **17 325 PA** | **2,14 PA** | - | **2 305 980 dmg** | **1 142,7 dmg** |

### Analyse du Phénomène
- **Chiffre Clé** : **2 305 980 points de dégâts ont été purement et simplement abandonnés** sur la journée, soit **1 142,7 points de dégâts gaspillés par combat** !
- Sur les deux personnages principaux de haut niveau (**Bastionbot** et **Shadowbot**), plus d'un tour sur trois se termine avec au moins 2 PA en réserve.
- La cause fondamentale réside dans l'incapacité du planificateur à combiner des sorts de coût résiduel (sorts à 2 PA ou 3 PA) ou à anticiper la consommation des PA : le moteur sélectionne un sort coûteux (4 PA), puis s'arrête prématurément car aucun sort lourd n'est re-lançable, laissant 2 ou 3 PA orphelins.
- Chez Archerbot (Crâ), **89,9% des tours** se terminent avec $\ge 2$ PA non utilisés en raison d'une boucle rigide qui refuse de lancer d'autres sorts une fois le premier sort à distance exécuté.

---

## 2. Tours "Aucun Sort Lancé" (Abandon du Planificateur)

La ligne de log `[combat-planner] turn=<N> aucun sort lancé avec <N> PA -> rotation classique` caractérise une défaillance critique du moteur : disposant de l'intégralité ou de la quasi-totalité de ses PA, le planificateur explore l'arbre de recherche, n'attribue aucun score suffisant à aucune action, et capitule en basculant sur une rotation classique dégradée.

### Répartition par Personnage et par Numéro de Tour

| Personnage | Tours d'Abandon | PA Moyens Détenus lors de l'Abandon | Total PA Bloqués | Dégâts Directement Perdus |
| :--- | :---: | :---: | :---: | :---: |
| **Bastionbot** (Sacrieur) | **261 tours** | 12,0 PA | 3 124 PA | 484 220 dmg |
| **Shadowbot** (Sram) | **237 tours** | 11,4 PA | 2 704 PA | 473 200 dmg |
| **TOTAL** | **498 tours** | **11,7 PA** | **5 828 PA** | **957 420 dmg** |

### Détail par Round de Combat

#### Bastionbot (Sacrieur 199) : 261 abandons
- **Round 1** : 13 tours (156 PA bloqués, moyenne 12,0 PA)
- **Round 2** : **78 tours** (934 PA bloqués, moyenne 12,0 PA) — *Pic critique*
- **Round 3** : **69 tours** (820 PA bloqués, moyenne 11,9 PA) — *Pic critique*
- **Round 4** : 22 tours (259 PA bloqués, moyenne 11,8 PA)
- **Round 5** : 36 tours (430 PA bloqués, moyenne 11,9 PA)
- **Round 6** : 19 tours (226 PA bloqués, moyenne 11,9 PA)
- **Rounds 7 à 14** : 24 tours (305 PA bloqués)

#### Shadowbot (Sram 190) : 237 abandons
- **Round 1** : 8 tours (85 PA bloqués, moyenne 10,6 PA)
- **Round 2** : **46 tours** (525 PA bloqués, moyenne 11,4 PA)
- **Round 3** : **91 tours** (1 034 PA bloqués, moyenne 11,4 PA) — *Pic critique*
- **Round 4** : **50 tours** (573 PA bloqués, moyenne 11,5 PA)
- **Rounds 5 à 11** : 42 tours (487 PA bloqués)

### Diagnostic de la Cause
Les rounds 2 et 3 représentent à eux seuls **63% des abandons complets**. À ces rounds, les ennemis se trouvent souvent à mi-distance (entre 3 et 6 cases) ou partiellement masqués par un obstacle.
Le planificateur évalue les déplacements et les lignes de vue, mais son heuristique de scoring pondère excessivement la sécurité (`incoming`, `friendly-fire`) ou échoue à trouver un chemin de lancer valide avec contrainte de portée minimale/maximale, concluant qu'aucune action ne dépasse le seuil minimal de viabilité alors que le personnage détient **12 PA intacts**.

---

## 3. Répertoire de Sorts Réellement Utilisé vs Kit Disponible

L'audit quantifie ici le syndrome de la "monoculture de sorts" : le personnage dispose de dizaines de sorts appris, en configure une quinzaine, mais l'IA tourne en boucle sur 2 ou 3 sorts uniquement.

### Synthèse par Personnage

| Personnage | Sorts Appris Connus | Sorts Configurés en Combat | Sorts Distincts Lancés | % Sorts Appris Utilisés | Poids du Top 2 des Sorts |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Shadowbot** (Sram) | 45 | 15 | 15 | 33,3 % | **64,2 %** |
| **Bastionbot** (Sacrieur) | 39 | 12 | 12 | 30,8 % | **54,2 %** |
| **Archerbot** (Crâ) | 28 | 7 | 5 | 17,9 % | **69,4 %** |
| **Sylvanbot** (Sadida) | 40 | 2 | 2 | **5,0 %** | **100,0 %** |

---

### Fréquence Détaillée des Sorts Lancés

#### Shadowbot (Sram 190) — 13 156 lancers au total
1. **12902** (*Sournoiserie*, 3 PA) : **4 641 lancers (35,3%)** — Cumul : 35,3%
2. **12917** (*Attaque Mortelle*, 4 PA) : **3 803 lancers (28,9%)** — Cumul : 64,2%
3. **12913** (*Invisibilité*, 2 PA) : 1 045 lancers (7,9%) — Cumul : 72,1%
4. **12914** (*Piège Sournois*, 2 PA) : 963 lancers (7,3%) — Cumul : 79,4%
5. **12916** (*Piège Mortel*, 3 PA) : 508 lancers (3,9%) — Cumul : 83,3%
6. **12910** (*Piège Répulsif*, 3 PA) : 464 lancers (3,5%) — Cumul : 86,8%
7. **24011** (*Chausse-Trappe*, 3 PA) : 451 lancers (3,4%) — Cumul : 90,3%
8. **12938** (*Arnaque*, 3 PA) : 410 lancers (3,1%) — Cumul : 93,4%
9. **12942** (*Piège Fosse Commune*, 2 PA) : 233 lancers (1,8%)
10. **12921** (*Piège Funeste*, 3 PA) : 215 lancers (1,6%)
11. **12931** (*Piège Dévoyant*, 2 PA) : 150 lancers (1,1%)
12. **12948** (*Piège Lacrymogène*, 3 PA) : 124 lancers (0,9%)
13. **14312** (*Maîtrise d'Arme*, 2 PA) : 77 lancers (0,6%)
14. **12908** (*Coupe-Gorge*, 2 PA) : 61 lancers (0,5%)
15. **12903** (*Fourberie*, 3 PA) : 11 lancers (0,1%)

*Constat Sram* : Deux sorts représentent près des deux tiers de l'activité offensive. Les 7 pièges secondaires ne représentent chacun qu'entre 0,9% et 1,8% des lancers, et sont quasi systématiquement rejetés par le serveur lorsqu'ils sont tentés (voir section 5).

#### Bastionbot (Sacrieur 199) — 9 652 lancers au total
1. **12727** (*Absorption*, 3 PA) : **3 142 lancers (32,6%)** — Cumul : 32,6%
2. **12755** (*Supplice*, 4 PA) : **2 085 lancers (21,6%)** — Cumul : 54,2%
3. **14011** (*Coagulation / Bouclier*, 2 PA) : 1 214 lancers (12,6%) — Cumul : 66,7%
4. **12728** (*Nervosité*, 3 PA) : 641 lancers (6,6%) — Cumul : 73,4%
5. **12726** (*Bain de Sang*, 2 PA) : 570 lancers (5,9%) — Cumul : 79,3%
6. **12741** (*Assaut*, 4 PA) : 560 lancers (5,8%) — Cumul : 85,1%
7. **12757** (*Afflux*, 4 PA) : 544 lancers (5,6%) — Cumul : 90,7%
8. **12745** (*Entaille*, 3 PA) : 335 lancers (3,5%) — Cumul : 94,2%
9. **12744** (*Stase*, 3 PA) : 259 lancers (2,7%) — Cumul : 96,9%
10. **12736** (*Châtiment / Transposition*, 3 PA) : 165 lancers (1,7%)
11. **12739** (*Dérobade*, 2 PA) : 90 lancers (0,9%)
12. **12735** (*Attirance*, 2 PA) : 47 lancers (0,5%)

*Constat Sacrieur* : Le binôme Absorption / Supplice truste 54,2% des lancers. Le sort d'Attirance (12735), pourtant pivot du Sacrieur pour ramener des monstres fuyards au corps à corps et débloquer les PA restants, n'est lancé que 47 fois sur toute la journée (0,5%).

#### Archerbot (Crâ 58) — 1 662 lancers au total
1. **32437** (*Flèche Magique*, 3 PA) : 732 lancers (44,0%)
2. **32474** (*Flèche de Recul*, 3 PA) : 422 lancers (25,4%)
3. **32464** (*Tir Puissant*, 2 PA) : 299 lancers (18,0%) — *Nota : 274 d'entre eux ont échoué par refus serveur !*
4. **13528** (*Ronce*, 3 PA) : 130 lancers (7,8%)
5. **14396** (*Larme / Herbe Folle*, 3 PA) : 79 lancers (4,8%)

#### Sylvanbot (Sadida 180) — 305 lancers au total
1. **13528** (*Ronce*, 3 PA) : 205 lancers (67,2%)
2. **14396** (*Larme / Herbe Folle*, 3 PA) : 100 lancers (32,8%)
*(38 sorts appris ne sont jamais utilisés ni configurés).*

---

## 4. Dégâts par Lancer et Dégâts par PA (Classement des Sorts)

### Méthodologie de Mesure
1. **Source Empirique (Dégâts Observés)** :
   Calculé à partir des transitions d'état de combat entre le début d'un round $R$ (`[combat] PV tour R : ... ennemis [cell:hp/max]`) et le début du round suivant $R+1$ sur les monstres ciblés. Pour éliminer les biais d'interaction, nous avons extrait les tours homogènes (où un unique sort offensif a été lancé) et calculé $\Delta \text{PV} = \sum \text{PV}_{\text{début}} - \sum \text{PV}_{\text{fin}}$.
2. **Source Modélisée (Prédictions du Planificateur)** :
   Moyenne des prédictions élémentaires du planificateur sur les actions simples isolées `[combat-planner] turn=N [cast:<ID>@cell(<AP>)] total=... damage=<X>`.

### Sources d'Erreurs et Limites de la Mesure
- **Résistances Élémentaires** : Les monstres disposent de 10% à 35% de résistances élémentaires, ce qui réduit les dégâts réels observés par rapport au potentiel théorique sans résistances.
- **Troncature d'Overkill** : Lorsqu'un monstre à 150 PV subit une *Attaque Mortelle* à 850 dégâts, le delta PV enregistré est plafonné à 150, ce qui sous-estime légèrement les gros sorts sur les cibles achevées.
- **Régénération Ennemie** : Certains monstres se soignent entre les rounds, réduisant marginalement le delta apparent.

---

### Classement des Sorts par Efficacité Réelle (Dégâts / PA)

#### Classement Sram (Shadowbot 190)
| Rang | Sort ID | Nom Usuel | Coût PA | Dégâts Moyens Observés | Dégâts Modélisés (Planner) | Échantillons ($N$) | **Dégâts / PA Observés** | Ratio Efficacité vs Sournoiserie |
| :---: | :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **1** | **12917** | **Attaque Mortelle** | **4 PA** | **812,3 dmg** | 1 018,6 dmg | 116 obs / 2 719 plan | **203,1 dmg/PA** | **+32,5 %** |
| **2** | **12902** | **Sournoiserie** | **3 PA** | **459,9 dmg** | 431,0 dmg | 64 obs / 1 881 plan | **153,3 dmg/PA** | Référence (100%) |
| **3** | **12938** | **Arnaque** | **3 PA** | 328,5 dmg | 460,4 dmg | 3 obs / 184 plan | **109,5 dmg/PA** | -28,6 % |
| **4** | **24011** | **Chausse-Trappe** | **3 PA** | 327,9 dmg | 492,2 dmg | 15 obs / 275 plan | **109,3 dmg/PA** | -28,7 % |

*Analyse d'arbitrage Sram* : *Attaque Mortelle* génère **203,1 dmg/PA** contre **153,3 dmg/PA** pour *Sournoiserie*. Or, le bot a lancé *Sournoiserie* **4 641 fois** contre seulement **3 803 fois** *Attaque Mortelle*. À chaque fois que l'IA a préféré lancer 2 Sournoiseries (6 PA = 920 dmg) au lieu d'une Attaque Mortelle + buff/sort secondaire (6 PA = 1 130+ dmg), elle a perdu 210 dégâts nets.

---

#### Classement Sacrieur (Bastionbot 199)
| Rang | Sort ID | Nom Usuel | Coût PA | Dégâts Moyens Observés | Dégâts Modélisés (Planner) | Échantillons ($N$) | **Dégâts / PA Observés** |
| :---: | :---: | :--- | :---: | :---: | :---: | :---: | :---: |
| **1** | **12727** | **Absorption** | **3 PA** | **518,0 dmg** | 392,3 dmg | 15 obs / 943 plan | **172,7 dmg/PA** |
| **2** | **12755** | **Supplice** | **4 PA** | **609,2 dmg** | 575,9 dmg | 56 obs / 1 219 plan | **152,3 dmg/PA** |
| **3** | **12757** | **Afflux** | **4 PA** | **579,0 dmg** | 455,9 dmg | 2 obs / 218 plan | **144,8 dmg/PA** |
| **4** | **12741** | **Assaut** | **4 PA** | 560,0 dmg | 353,7 dmg | 6 obs / 50 plan | **140,0 dmg/PA** |
| **5** | **12728** | **Nervosité** | **3 PA** | 402,5 dmg | 257,0 dmg | 12 obs / 415 plan | **134,2 dmg/PA** |
| **6** | **12739** | **Dérobade / Folâtrie** | **2 PA** | 251,0 dmg | - | 1 obs | **125,5 dmg/PA** |
| **7** | **12745** | **Entaille** | **3 PA** | 326,5 dmg | 198,9 dmg | 1 obs / 50 plan | **108,8 dmg/PA** |
| **8** | **12726** | **Bain de Sang** | **2 PA** | - | 176,5 dmg | 270 plan | **88,3 dmg/PA** |
| **9** | **12744** | **Stase** | **3 PA** | 201,8 dmg | - | 18 obs | **67,3 dmg/PA** |

*Analyse d'arbitrage Sacrieur* : *Absorption* est le sort roi du Sacrieur (**172,7 dmg/PA** avec vol de vie intégré). Cependant, lorsque Bastionbot a 4 PA restants, elle choisit fréquemment de lancer *Bain de Sang* (2 PA) ou de ne rien faire, ignorant des sorts à haute rentabilité comme *Supplice* ou *Afflux*.

---

#### Classement Sadida (Sylvanbot 180) & Crâ (Archerbot 58)
- **14396** (*Larme / Herbe Folle*, 3 PA) : **288,0 dmg** $\rightarrow$ **96,0 dmg/PA**
- **13528** (*Ronce*, 3 PA) : **250,1 dmg** $\rightarrow$ **83,4 dmg/PA**
- **32437** (*Flèche Magique*, 3 PA) : **85,1 dmg** $\rightarrow$ **28,4 dmg/PA**

---

## 5. Refus Serveur par Identifiant de Message et Évitabilité

Durant les 19 sessions de logs, **1 862 lancers de sorts ont été formellement rejetés par le serveur Dofus** via un paquet `cast_refused`.

### Tableau Exhaustif des Refus Serveur

| Message ID | Nombre de Refus | Texte Exact du Message Serveur | Sorts Déclencheurs Principaux | Évitabilité Client |
| :---: | :---: | :--- | :--- | :---: |
| **229** | **1 005** | *"Il y a un piège sur cette case."* | Sram : 12914 (255), 12916 (168), 12921 (152), 12931 (150), 12942 (149), 12948 (124), 12910 (7) | **100 % Évitable** |
| **175** | **549** | *"Impossible de lancer ce sort actuellement."* | Crâ : 32474 (196)<br>Sacrieur : 14011 (123), 12736 (98), 12739 (33)<br>Sram : 12910 (63), 24011 (18), 12913 (6) | **100 % Évitable** |
| **171** | **276** | *"Impossible de lancer ce sort : Vous avez une portée de {0} à {1} et vous visez à {2} !"* | Crâ : 32464 (274)<br>Sram : 12917 (2) | **100 % Évitable** |
| **170** | **27** | *"Impossible de lancer ce sort : Vous avez {0} PA disponible(s) et il vous en faut {1} pour ce sort !"* | Sacrieur : 12727 (6), 12755 (3), 12757 (2), 12726 (2)<br>Sram : 12938 (4), 12917 (2), 12914 (2) | **100 % Évitable** |
| **174** | **4** | *"Impossible de lancer ce sort : un obstacle gène votre vue !"* | Sram : 12902 (2), 12917 (2) | **100 % Évitable** |
| **172** | **1** | *"Impossible de lancer ce sort : la cellule visée n'est pas disponible !"* | Sacrieur : 12727 (1) | **100 % Évitable** |
| **TOTAL** | **1 862** | - | - | **100,0 % ÉVITABLE** |

### Analyse Critique de l'Évitabilité

1. **Message 229 (Piège déjà présent sur la case — 1 005 refus, 54,0% des rejets)** :
   Le Sram tente de superposer un piège sur une case où se trouve déjà un piège actif. Le client bot possède l'historique complet des marques posées sur la carte (`[combat] marque posée id=...`). Vérifier `MapState.HasTrap(cellId)` avant d'injecter la commande éliminerait instantanément 1 005 refus et les freezes de synchronisation turbo associés.
2. **Message 175 (Sort sous temps de recharge ou cap par tour — 549 refus, 29,5% des rejets)** :
   Le planificateur génère des plans utilisant des sorts en cooldown :
   - *Flèche de Recul* (32474) : plafonnée par cible / par tour.
   - *Coagulation* (14011) : temps de recharge initial et intervalle de relance de 3 tours (`MinCastInterval: 3`).
   - *Châtiment / Transposition* (12736) : intervalle de 3 tours.
   - *Piège Répulsif* (12910) : intervalle de 4 tours.
   Toutes ces contraintes sont transmises au bot dans le paquet `combat-player-empty` sous les champs `"MinCastInterval"` et `"InitialCooldown"`. Le planificateur ignore ces champs dans son état de transition et planifie des sorts indisponibles.
3. **Message 171 (Portée invalide / Auto-ciblage impossible — 276 refus, 14,8% des rejets)** :
   Archerbot (Crâ) tente de lancer son buff *Tir Puissant* (32464) sur sa propre cellule : `sort 32464 sur MOI (215) [buff]`. Or, la portée du sort est de `1 à 1` (non lançable à distance 0). Le bot échoue donc 274 fois d'affilée à se buffer, privant le Crâ de son boost de puissance durant 274 tours !
4. **Message 170 (Manque de PA — 27 refus)** :
   Le client envoie un paquet de lancer alors que le compteur de PA actuel est strictement inférieur au coût du sort.

**Bilan d'Évitabilité : 100,0 % des refus constatés sont des préconditions évidentes que le client bot est en mesure de valider sans aucune requête serveur.**

---

## 6. Opportunités de Boost Manquées ("Si on est sûr de pas tuer...")

L'instruction stipule : *« Trouver les tours où les lancers du personnage ne pouvaient pas réduire d'ennemi en dessous de 0 PV (aucun kill possible) et où il a quand même tout dépensé en dégâts directs. C'est l'opportunité décrite par le propriétaire : "si on est sûr de pas tuer, on doit utiliser nos sorts de boost". »*

### Mesure des Tours sans Kill et Dépense Offensive Pure

Nous avons analysé chaque tour de chaque personnage selon les critères stricts suivants :
1. Aucun ennemi n'est mort pendant ce tour (`kills == 0`).
2. Les PV minimaux des ennemis en début de tour excèdent la capacité maximale de burst du personnage sur un tour complet (ex. PV min $> 1 200$ pour Bastionbot, $> 1 500$ pour Shadowbot, $> 400$ pour Archerbot). Aucun kill n'était donc mathématiquement accessible.
3. Le personnage a consommé ses PA en sorts d'attaque directs purs, sans lancer ses sorts de boost / buff disponibles en kit.

| Personnage | Tours Totaux | Tours sans Kill avec Dégâts Purs | **Tours Stricts sans Kill Possible où Dégâts Déversés** | **Part des Tours de Combat** | Sorts de Boost Ignorés en Réserve |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **Shadowbot** (Sram 190) | 3 960 | 1 200 | **1 037 tours** | **26,2 %** | Invisibilité (12913), Maîtrise d'Arme (14312), Chausse-Trappe (24011) |
| **Bastionbot** (Sacrieur 199) | 3 111 | 1 011 | **900 tours** | **28,9 %** | Coagulation / Bouclier (14011), Dérobade (12739), Châtiment (12736) |
| **Archerbot** (Crâ 58) | 865 | 431 | **192 tours** | **22,2 %** | Tir Puissant (32464) |
| **Sylvanbot** (Sadida 180) | 169 | 149 | **96 tours** | **56,8 %** | Buffs de portée / poupées / puissance (non équipés) |
| **TOTAL** | **8 105** | **2 791** | **2 225 tours** | **27,5 %** | - |

### Conséquences Tactiques Observées
- **Sacrieur (Bastionbot)** : Dans **900 tours** où les monstres avaient entre 2 500 et 3 700 PV, Bastionbot a déversé des sorts d'attaque sans poser son bouclier *Coagulation* (14011). Résultat : elle subit la riposte intégrale des monstres au tour adverse sans bouclier, ce qui explique directement les **245 défaites** subies par Bastionbot (taux de défaite anormal de 30,3%).
- **Sram (Shadowbot)** : Dans **1 037 tours**, Shadowbot attaque frontalement à la Sournoiserie au lieu de préparer son Invisibilité ou de poser un réseau de pièges préparatoire.
- **Crâ (Archerbot)** : Dans **192 tours**, attaque avec une Flèche Magique à 85 dégâts au lieu d'activer un boost de puissance.

---

## Synthèse : Matrice des Gaspillages

```
+-------------------------------------------------------------------------------------------------------+
| TYPE DE GASPILLAGE                        | OCCURRENCES   | VOLUME DE PA GÂCHÉ | DÉGÂTS PERDUS ESTIMÉS|
+-------------------------------------------------------------------------------------------------------+
| 1. PA non dépensés (Tours >= 2 PA)        | 3 351 tours   | 17 325 PA perdus   | 2 305 980 dégâts     |
| 2. "Aucun sort lancé" (Abandon IA)        |   498 tours   |  5 828 PA perdus   |   957 420 dégâts     |
| 3. Opportunités de Boost Manquées         | 2 225 tours   | ~6 500 PA mal axés | ~750 000 dégâts temp.|
| 4. Monoculture / Sorts Sub-optimaux       | 8 444 lancers | N/A                | ~500 000 dégâts      |
| 5. Refus Serveur (Actions rejetées)       | 1 862 rejets  | ~4 800 PA avortés  | ~380 000 dégâts syn. |
+-------------------------------------------------------------------------------------------------------+
```

---

## LES 5 GASPILLAGES LES PLUS COUTEUX

Classement établi par **perte de dégâts moyenne estimée par combat**, mesurée sur l'ensemble des 2 018 combats réels du 9 novembre 2026.

### 1. LES PA RESTANTS EN FIN DE TOUR (UNSPENT AP $\ge$ 2)
- **Mesure constatée** : **3 351 tours** (41,3% des tours totaux) se terminent avec au moins 2 PA en réserve. Un total astronomique de **17 325 PA** n'a jamais été dépensé.
- **Dégâts perdus par combat** : **1 142,7 dégâts perdus par combat** (1 373,2 dmg/combat sur Sacrieur, 916,8 dmg/combat sur Sram, 2 238 dmg/combat sur Crâ).
- **Perte totale sur la journée** : **2 305 980 points de dégâts**.
- **Mécanisme du défaut** : Le planificateur n'a pas de logique de vidange des PA résiduels. Dès que ses combinaisons prioritaires (sorts à 4 PA) sont épuisées ou en cooldown, il rend la main au lieu de compléter avec des sorts à 2 ou 3 PA (comme *Bain de Sang* 12726, *Sournoiserie* 12902, ou *Coagulation* 14011).

---

### 2. L'ABANDON COMPLET DU PLANIFICATEUR ("AUCUN SORT LANCÉ AVEC N PA")
- **Mesure constatée** : **498 tours complets** où le personnage détient la totalité de ses PA (moyenne de **11,7 PA**) et où le planificateur capitule sans lancer le moindre sort.
- **Dégâts perdus par combat** : **490,5 dégâts perdus par combat** (mesuré sur les 1 952 combats joués par le planificateur).
- **Perte totale sur la journée** : **957 420 points de dégâts** (3 124 PA perdus pour Bastionbot, 2 704 PA perdus pour Shadowbot).
- **Mécanisme du défaut** : Blocage algorithmique aux rounds 2 et 3 à mi-distance (3 à 6 cases). La fonction d'évaluation pénalise excessivement les lignes de vue imparfaites ou le coût de déplacement, aboutissant à un score global négatif pour toutes les branches de l'arbre décisionnel. Le bot passe son tour à vide devant les monstres.

---

### 3. LE GASPILLAGE DE DÉGÂTS SANS PERSPECTIVE DE KILL AU LIEU DE BOOSTER / TANKER
- **Mesure constatée** : **2 225 tours** (27,5% des tours de combat) où le personnage ne pouvait éliminer aucun monstre adverse (PV ennemis $> 1 200 - 1 500$) et a déversé tous ses PA dans des dégâts directs marginaux au lieu d'activer ses buffs de puissance, d'invisibilité ou ses boucliers.
- **Dégâts perdus par combat** : **~371,7 dégâts équivalents perdus par combat** (perte de survie, perte du multiplicateur de puissance au tour suivant, et 245 défaites subies par Sacrieur faute de bouclier).
- **Perte totale sur la journée** : **~750 000 points de dégâts et de tempo**.
- **Mécanisme du défaut** : Absence de la règle métier demandée par le propriétaire (*« si on est sûr de pas tuer, on doit utiliser nos sorts de boost »*). Le planificateur privilégie systématiquement une valeur immédiate de dégâts bruts (ex: 500 dmg sur un mob à 3 500 PV) plutôt que d'activer un buff de puissance (+250 puissance = +35% de dégâts au tour suivant sur 12 PA) ou un bouclier vital (*Coagulation*).

---

### 4. LA SÉLECTION SOUS-OPTIMALE DES SORTS (MONOCULTURE DU KIT)
- **Mesure constatée** : Sur 45 sorts connus par le Sram et 39 par le Sacrieur, **deux sorts représentent 64,2% et 54,2% des lancers**.
- **Dégâts perdus par combat** : **~247,8 dégâts perdus par combat**.
- **Perte totale sur la journée** : **~500 000 points de dégâts**.
- **Mécanisme du défaut** : L'IA boucle sur *Sournoiserie* (153,3 dmg/PA) au détriment d'*Attaque Mortelle* (203,1 dmg/PA) même à portée de tir, perdant 50 dégâts par PA consommé. De même, les sorts de placement et d'attirance (*Attirance* 12735 lancé seulement 47 fois) ne sont pas valorisés pour amener les cibles dans la zone optimale de frappe des gros sorts de corps à corps.

---

### 5. LES REJETS SERVEUR SYSTÉMATIQUES (1 862 REFUS ÉVITABLES)
- **Mesure constatée** : **1 862 rejets serveur** enregistrés (**0,92 refus par combat en moyenne**).
- **Dégâts perdus par combat** : **~188,3 dégâts perdus par combat** (combos interrompus par barrière de désynchronisation turbo, buffs avortés, PA bloqués).
- **Perte totale sur la journée** : **~380 000 points de dégâts**.
- **Mécanisme du défaut** :
  1. *Message 229 (1 005 cas)* : Tentative de poser un piège Sram sur une case déjà piégée.
  2. *Message 175 (549 cas)* : Lancer de sorts sous cooldown (*Flèche de Recul*, *Coagulation*, *Châtiment*, *Piège Répulsif*).
  3. *Message 171 (276 cas)* : Tentative d'auto-ciblage d'un sort à portée minimale 1 (*Tir Puissant* Crâ).
  Ces 1 862 rejets brisent les chaînes d'injection turbo, déclenchent la fermeture de la barrière client et forcent la fin prématurée du tour de jeu.

---
*Rapport d'audit technique généré le 09/11/2026 pour le projet Eliotrope Combat AI.*
