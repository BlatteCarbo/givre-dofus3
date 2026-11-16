# Analyse Approfondie des Rejets Serveur Dofus 175 ("Impossible de lancer ce sort actuellement.")

Date de l'analyse : 9 novembre 2026  
Périmètre : 4 fichiers journaux de transport (GivreReplica / test runs) totalisant 147 rejets serveur code 175 :
- `transport-2026-11-09_09-43-31.log` (51 rejets 175)
- `transport-2026-11-09_10-13-58.log` (30 rejets 175)
- `transport-2026-11-09_10-37-03.log` (2 rejets 175)
- `transport-2026-11-09_10-46-45.log` (64 rejets 175)

---

## 1. Vue d'Ensemble et Données Clés

Le message serveur 175 (`[combat-cast] text message=175 params=[] text=Impossible de lancer ce sort actuellement.`) est le code de refus générique émis par le serveur de jeu Dofus lorsqu'une requête de lancer de sort (`FightTurnCastSpellRequest`) enfreint les règles d'admissibilité temporelle du sort :
1. **Dépassement du nombre maximal de lancers par tour** (`maxCastPerTurn`, typiquement 1 lancer par tour pour les sorts à délai).
2. **Délai de relance non expiré** (`minCastInterval` ou `cooldown`), lorsque le sort est relancé trop tôt dans le même combat.
3. **Délai initial non écoulé** (`initialCooldown`).

Sur les 147 occurrences analysées :
- **100 % (147/147)** des rejets ont été attribués sans ambiguïté à leur lancer déclencheur (1 à 8 lignes au-dessus de la métrique `[turbo-metric] cast_refused message=175`).
- Les 147 rejets se répartissent sur **9 sorts distincts**.
- **Chacun des 9 sorts réussit régulièrement** pour le même personnage dans d'autres conditions (aucun sort n'est "définitivement impossible").
- **100 % (147/147)** des échecs proviennent soit d'une **tentative de double lancer dans le même tour** (injections concurrentes à quelques millisecondes d'intervalle ou repli "FILET" relançant le sort), soit d'un **lancer prématuré avant la fin du délai de relance en tours** (différence de tours inférieure au `minCastInterval`).

### Note Importante sur les PIDs et Personnages
Dans les sessions 1, 3 et 4 :
- PID `1073741825` = **Bastionbot** (Sacrieur 199)
- PID `1073741827` = **Shadowbot** (Sram 190-191)
- PID `1073741829` = **Sylvanbot** (Sadida 158-159)
- PID `1073741831` = **Archerbot** (Crâ 58-63)

Dans la session 2 (`transport-2026-11-09_10-13-58.log`), une permutation de PID a eu lieu lors du lancement des processus :
- PID `1073741825` = Bastionbot (Sacrieur 199)
- PID `1073741827` = Sylvanbot (Sadida 159)
- PID `1073741829` = **Archerbot** (Crâ 63)
- PID `1073741831` = **Shadowbot** (Sram 191)

Le tableau ci-dessous présente les comptes selon la table statique demandée (par PID assigné) ainsi que l'attribution réelle par personnage vérifiée dans les trames d'authentification et de sélection de personnage (`perso sélectionné`).

---

## 2. Tableau Exhaustif des 147 Rejets

Couverture intégrale des 147 rejets serveur 175 par identifiant de sort, personnage et compteurs :

| ID Sort | Nom du Sort | Classe Source | Refus 175 Total | Répartition par Personnage (PID) | Répartition Réelle Personnage |
| :---: | :--- | :--- | :---: | :--- | :--- |
| **32474** | Balise de Survie | Crâ | **61** | Archerbot (PID 1831) : 51<br>Sylvanbot (PID 1829) : 10 | Archerbot (Crâ) : 61 |
| **14011** | Pilori | Sacrieur | **30** | Bastionbot (PID 1825) : 30 | Bastionbot (Sacrieur) : 30 |
| **12736** | Transposition | Sacrieur | **22** | Bastionbot (PID 1825) : 22 | Bastionbot (Sacrieur) : 22 |
| **12910** | Piège d'Immobilisation | Sram | **15** | Shadowbot (PID 1827) : 10<br>Archerbot (PID 1831) : 5 | Shadowbot (Sram) : 15 |
| **12739** | Sacrifice | Sacrieur | **7** | Bastionbot (PID 1825) : 7 | Bastionbot (Sacrieur) : 7 |
| **12913** | Invisibilité | Sram | **4** | Shadowbot (PID 1827) : 4 | Shadowbot (Sram) : 4 |
| **24011** | Mantiscroc | Forgelance | **4** | Shadowbot (PID 1827) : 4 | Shadowbot (Sram) : 4 |
| **12741** | Punition | Sacrieur | **2** | Bastionbot (PID 1825) : 2 | Bastionbot (Sacrieur) : 2 |
| **12903** | Concentration de Chakra | Sram | **2** | Shadowbot (PID 1827) : 1<br>Archerbot (PID 1831) : 1 | Shadowbot (Sram) : 2 |
| **TOTAL** | — | — | **147** | — | — |

*Vérification arithmétique : 61 + 30 + 22 + 15 + 7 + 4 + 4 + 2 + 2 = 147 rejets.*

---

## 3. Répartition Cible : Soi-Même (`sur MOI`) vs Cellule Distante

Pour chaque sort refusé, l'analyse des cellules cibles et des positions relatives (`from=cell`, `maCellule=cell` et `[combat] sort ... sur MOI`) établit la partition suivante :

| ID Sort | Nom du Sort | Refus 175 | Cible : Soi-Même (`sur MOI`) | Cible : Distante (`REMOTE`) | Détail & Justification Géométrique |
| :---: | :--- | :---: | :---: | :---: | :--- |
| **32474** | Balise de Survie | 61 | **0** (0%) | **61** (100%) | Invocation de balise posée à distance (1 à 4 cases). |
| **14011** | Pilori | 30 | **30** (100%) | **0** (0%) | Auto-buff Sacrieur (`sur MOI (<cell>) [buff]`). |
| **12736** | Transposition | 22 | **0** (0%) | **22** (100%) | Échange de position ciblant une entité distante (1 à 9 cases). |
| **12910** | Piège d'Immobilisation | 15 | **0** (0%) | **15** (100%) | Pose de piège / glyphe sur cellule libre ou distante (1 à 6 cases). |
| **12739** | Sacrifice | 7 | **0** (0%) | **7** (100%) | Sort de protection ciblant un allié distant. |
| **12913** | Invisibilité | 4 | **4** (100%) | **0** (0%) | Auto-buff Sram (`execute from=<cell> cell=<cell> target=<self>`). |
| **24011** | Mantiscroc | 4 | **0** (0%) | **4** (100%) | Sort d'attaque à distance (zone / portée 4). |
| **12741** | Punition | 2 | **0** (0%) | **2** (100%) | Sort d'attaque au corps-à-corps strict (portée 1-1). |
| **12903** | Concentration de Chakra | 2 | **0** (0%) | **2** (100%) | Sort ciblant un ennemi distant (portée 1-6). |
| **TOTAL** | — | **147** | **34** (23,1%) | **113** (76,9%) | Exactement 34 sur soi-même, 113 distants. |

---

## 4. Résolution des Noms et Données Client

Tous les identifiants de sorts ont été résolus directement depuis les fichiers binaires du client Dofus (Unity Dofus 3 sous `%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\Dofus_Data\StreamingAssets\Content`) en reproduisant le mécanisme de `GameNameResolver.cs` :
- **Noms** : Lus via `data_assets_spellsdataroot.asset.bundle` (table `id -> nameId`) croisé avec `Content/I18n/fr.bin` (index 340 817 entrées localisées).
- **Caractéristiques de combat** : Lues via `data_assets_spelllevelsdataroot.asset.bundle` (coûts en PA, portées, `minCastInterval`, `initialCooldown`, `maxCastPerTurn`, `statesCriterion`).
- **Classes / Variantes** : Lues via `data_assets_spellvariantsdataroot.asset.bundle` (table `spellId -> breedId`) et `data_assets_breedsdataroot.asset.bundle`.

Tableau des données techniques extraites du client de jeu :

| ID Sort | Nom Résolu (Client) | nameId (fr.bin) | breedId | Classe | Coût PA | Portée | Cooldown (`cd`) | Cooldown Initial | Critère d'États |
| :---: | :--- | :---: | :---: | :--- | :---: | :---: | :---: | :---: | :---: |
| **12736** | Transposition | 797268 | 11 | Sacrieur | 3 | 1-9 | **3 tours** | 1 tour | `HS!7` (Pas en état Pesanteur) |
| **12739** | Sacrifice | 797253 | 11 | Sacrieur | 2 | 0-5 | **4 tours** | 0 tour | *(aucun)* |
| **12741** | Punition | 797294 | 11 | Sacrieur | 4 | 1-1 | **3 tours** | 0 tour | *(aucun)* |
| **12903** | Concentration de Chakra | 797752 | 4 | Sram | 3 | 1-6 | **3 tours** | 1 tour | *(aucun)* |
| **12910** | Piège d'Immobilisation | 797665 | 4 | Sram | 3 | 1-6 | **4 tours** | 0 tour | *(aucun)* |
| **12913** | Invisibilité | 797777 | 4 | Sram | 2 | 0-6 | **3 tours** | 0 tour | *(aucun)* |
| **14011** | Pilori | 800579 | 11 | Sacrieur | 2 | 0-4 | **3 tours** | 0 tour | *(aucun)* |
| **24011** | Mantiscroc | 966696 | 19 | Forgelance | 3 | 0-4 | **2 tours** | 0 tour | *(aucun)* |
| **32474** | Balise de Survie | 1178436 | 9 | Crâ | 2 | 1-4 | **3 tours** | 0 tour | *(aucun)* |

*Note sur Mantiscroc (24011)* : Il s'agit d'un sort de classe Forgelance (breed 19). Shadowbot (Sram 190) l'a acquis via une arme d'incarnation ou un objet d'équipement fournissant ce sort.

---

## 5. Analyse Détaillée des Causes : Succès vs Échecs

### 5.1 Sort 32474 — Balise de Survie (Crâ) : 61 Refus
- **Statistiques globales de lancer** : 130 requêtes transmises, 68 succès (52,3%), 61 refus code 175 (46,9%), 1 refus code 171 (portée).
- **Le sort réussit-il ?** : **OUI**, il réussit abondamment (68 fois au total).
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé au tour 1 du combat, ou lorsque $\Delta \text{tours} \ge 3$ après le précédent lancer réussi (ex : tour 1, tour 4, tour 7, tour 10, tour 13, tour 16).
  - **Refus 175** : Systématiquement tenté au tour $\text{précédent} + 2$ (ex : tour 3 après tour 1, tour 6 après tour 4, tour 9 après tour 7, tour 12 après tour 10, tour 15 après tour 13).
  - *Preuve* : Sur les 61 refus, 52 présentent un $\Delta \text{tours} = 2$ par rapport au lancer précédent dans le combat (les 9 autres correspondent à des combats où des tours intermédiaires ont été passés, mais avec relance avant les 3 tours de cooldown).
- **États actifs** : Aucun état bloquant (l'entité a 0 état actif dans le log).
- **Cause racine** : Le bot sous-estime le délai de relance de `Balise de Survie` (il utilise un intervalle de 2 tours au lieu des **3 tours** requis par le serveur).

---

### 5.2 Sort 14011 — Pilori (Sacrieur) : 30 Refus
- **Statistiques globales de lancer** : 269 requêtes transmises, 239 succès (88,8%), 30 refus code 175 (11,2%), 0 autre refus.
- **Le sort réussit-il ?** : **OUI**, il réussit 239 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé au tour 1 du combat, ou au moins 3 tours après le précédent Pilori (ex : tour 1, puis tour 4).
  - **Refus 175** :
    - Dans **14 cas**, tenté au tour immédiatement suivant ($\Delta \text{tours} = 1$, ex : lancé au tour 1, retenté au tour 2).
    - Dans **9 cas**, tenté 2 tours après ($\Delta \text{tours} = 2$, ex : lancé au tour 1, retenté au tour 3).
    - Dans **7 cas**, retenté dans le même tour ($\Delta \text{tours} = 0$) après une première action.
  - *États associés* : Lorsque Pilori a réussi au tour 1, Bastionbot porte les états `4199` (Pilori) et `97` (Indéplaçable). Les logs montrent que ces états sont retirés à la fin de leur durée, prouvant que le buff/cooldown était encore actif lors de la tentative rejetée.
- **Cause racine** : Le planificateur de combat Sacrieur n'enregistre pas ou ne respecte pas le délai de relance de **3 tours** (`minCastInterval = 3`) pour Pilori, et le reprogramme dès que le personnage a 2 PA disponibles au tour suivant.

---

### 5.3 Sort 12736 — Transposition (Sacrieur) : 22 Refus
- **Statistiques globales de lancer** : 37 requêtes transmises, 15 succès (40,5%), 22 refus code 175 (59,5%), 0 autre refus.
- **Le sort réussit-il ?** : **OUI**, il réussit 15 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé une unique fois dans le tour, à partir du tour 2 (`initialCooldown = 1`, donc interdit au tour 1), avec intervalle $\ge 3$ tours.
  - **Refus 175** :
    - Dans **20 cas sur 22**, il s'agit d'un **double lancer rapide dans le même tour** ! Le bot injecte un premier lancer de Transposition (qui réussit), puis, entre 20 ms et 125 ms plus tard, le pipeline Turbo injecte une deuxième requête `lance le sort 12736` sur une autre cellule ou la même cellule.
    - Dans **2 cas** (lignes 123717 et 123752 du log 1), le bot retente Transposition au tour 5 alors qu'il a été lancé au tour 3 ($\Delta \text{tours} = 2 < 3$, violation du cooldown), suivi de deux réinjections immédiates par le filet de sécurité (`FILET attaque obligatoire : sort 12736 castsThisTurn=3` et `castsThisTurn=4`).
- **Cause racine** :
  1. Pipeline Turbo concurrent : le bot n'attend pas l'accusé de réception avant de valider un second plan de lancer.
  2. Non-prise en compte de la limite stricte de **1 lancer par tour** (`maxCastPerTurn = 1`) et du cooldown de **3 tours** (`minCastInterval = 3`).

---

### 5.4 Sort 12910 — Piège d'Immobilisation (Sram) : 15 Refus
- **Statistiques globales de lancer** : 122 requêtes transmises (Shadowbot + Archerbot-account), 62 succès (50,8%), 15 refus code 175 (12,3%), 45 refus code 171 (portée).
- **Le sort réussit-il ?** : **OUI**, il réussit 62 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé au tour 1 ou avec $\Delta \text{tours} \ge 4$.
  - **Refus 175** :
    - **6 cas** : Double injection dans le même tour à 41 ms–142 ms d'intervalle (lignes 99395, 40753, 68097, 82448, 29881, 189274).
    - **9 cas** : Tentative de lancer alors que le délai de relance de **4 tours** (`minCastInterval = 4`) n'est pas expiré ($\Delta \text{tours} = 1, 2$ ou $3$).
- **Cause racine** : Double lancer non verrouillé dans la boucle de décision et cooldown de 4 tours non honoré.

---

### 5.5 Sort 12739 — Sacrifice (Sacrieur) : 7 Refus
- **Statistiques globales de lancer** : 14 requêtes transmises, 7 succès (50,0%), 7 refus code 175 (50,0%), 0 autre refus.
- **Le sort réussit-il ?** : **OUI**, il réussit 7 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Le premier lancer du tour réussit systématiquement.
  - **Refus 175** : **100 % (7/7)** des refus sont des **doubles lancers immédiats dans le même tour**, injectés exactement 22 ms à 64 ms après le premier lancer réussi !
- **Cause racine** : Le planificateur injecte deux fois de suite `Sacrifice` sur le même allié ou deux alliés différents dans la même trame de décision Turbo, ignorant le `maxCastPerTurn = 1` et `minCastInterval = 4`.

---

### 5.6 Sort 12913 — Invisibilité (Sram) : 4 Refus
- **Statistiques globales de lancer** : 232 requêtes transmises, 217 succès (93,5%), 4 refus code 175 (1,7%), 11 refus code 171 (portée).
- **Le sort réussit-il ?** : **OUI**, il réussit 217 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé au tour 1, puis relancé au tour 4, tour 7, etc. ($\Delta \text{tours} \ge 3$).
  - **Refus 175** : **100 % (4/4)** des refus se produisent lorsque le bot tente de relancer `Invisibilité` au tour $\text{précédent} + 1$ ou $+ 2$ (ex : log 4 ligne 69532 au tour 5 après le tour 4 ; ligne 104679 au tour 2 après le tour 1 ; ligne 105085 au tour 3 après le tour 1).
- **Cause racine** : Violation du délai de relance de **3 tours** (`minCastInterval = 3`).

---

### 5.7 Sort 24011 — Mantiscroc (Forgelance / Arme) : 4 Refus
- **Statistiques globales de lancer** : 112 requêtes transmises, 105 succès (93,8%), 4 refus code 175 (3,6%), 3 refus autres.
- **Le sort réussit-il ?** : **OUI**, il réussit 105 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé avec $\Delta \text{tours} \ge 2$, maximum 1 fois par tour.
  - **Refus 175** :
    - **2 cas** : Double lancer dans le même tour à 60 ms et 73 ms d'intervalle (lignes 93215 et 122208 de log 4).
    - **2 cas** : Lancer en tours consécutifs ($\Delta \text{tours} = 1$) alors que le cooldown est de **2 tours** (`minCastInterval = 2`).
- **Cause racine** : Violation du cooldown de 2 tours et double injection dans le même tour.

---

### 5.8 Sort 12741 — Punition (Sacrieur) : 2 Refus
- **Statistiques globales de lancer** : 121 requêtes transmises, 119 succès (98,3%), 2 refus code 175 (1,7%), 0 autre refus.
- **Le sort réussit-il ?** : **OUI**, il réussit 119 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé au tour 1, puis au tour 4, 7... ($\Delta \text{tours} \ge 3$).
  - **Refus 175** :
    - **Refus 1** (log 1 ligne 99980) : Lancé au tour 1 (réussi ligne 99084), puis retenté au **tour 2** ($\Delta \text{tours} = 1$).
    - **Refus 2** (log 2 ligne 103239) : Lancé au tour 2 (réussi ligne 102402), puis retenté au **tour 3** ($\Delta \text{tours} = 1$).
- **Cause racine** : Violation du délai de relance de **3 tours** (`minCastInterval = 3`).

---

### 5.9 Sort 12903 — Concentration de Chakra (Sram) : 2 Refus
- **Statistiques globales de lancer** : 9 requêtes transmises, 7 succès (77,8%), 2 refus code 175 (22,2%), 0 autre refus.
- **Le sort réussit-il ?** : **OUI**, il réussit 7 fois.
- **Différence exacte entre Succès et Refus** :
  - **Succès** : Lancé une unique fois dans le tour à partir du tour 2 (`initialCooldown = 1`).
  - **Refus 175** : **100 % (2/2)** des refus sont des **doubles lancers rapides dans le même tour** (47 ms et 49 ms après le premier lancer réussi du même tour).
- **Cause racine** : Non-verrouillage immédiat du sort après la première émission dans le tour (`maxCastPerTurn = 1`).

---

## 6. Synthèse Comparative : "Jamais" vs "Sous Condition"

Le rapport d'admissibilité pour les 9 sorts s'établit comme suit :

| ID Sort | Nom du Sort | Comportement Observé | Condition Requise pour le Succès (`Condition X`) |
| :---: | :--- | :--- | :--- |
| **32474** | Balise de Survie | **Succeeds under condition X** | $\Delta \text{tours} \ge 3$ depuis le dernier lancer (cooldown 3 tours respecté). |
| **14011** | Pilori | **Succeeds under condition X** | $\Delta \text{tours} \ge 3$ depuis le dernier lancer (cooldown 3 tours respecté) ET 1 seul lancer par tour. |
| **12736** | Transposition | **Succeeds under condition X** | $\text{Tour} \ge 2$ (cooldown initial 1 tour) ET $\Delta \text{tours} \ge 3$ ET strictement 1 lancer par tour. |
| **12910** | Piège d'Immobilisation | **Succeeds under condition X** | $\Delta \text{tours} \ge 4$ depuis le dernier lancer (cooldown 4 tours respecté) ET 1 seul lancer par tour. |
| **12739** | Sacrifice | **Succeeds under condition X** | $\Delta \text{tours} \ge 4$ depuis le dernier lancer ET strictement 1 lancer par tour. |
| **12913** | Invisibilité | **Succeeds under condition X** | $\Delta \text{tours} \ge 3$ depuis le dernier lancer (cooldown 3 tours respecté). |
| **24011** | Mantiscroc | **Succeeds under condition X** | $\Delta \text{tours} \ge 2$ depuis le dernier lancer ET strictement 1 lancer par tour. |
| **12741** | Punition | **Succeeds under condition X** | $\Delta \text{tours} \ge 3$ depuis le dernier lancer (cooldown 3 tours respecté). |
| **12903** | Concentration de Chakra | **Succeeds under condition X** | $\text{Tour} \ge 2$ (cooldown initial 1 tour) ET strictement 1 lancer par tour. |

---

## 7. RECOMMENDED GUARD PER SPELL

Pour éliminer 100 % des 147 rejets serveur 175, le moteur de combat du bot doit implémenter deux mécanismes de garde complémentaires :
1. **Un garde d'intervalle / cooldown par combat** vérifiant le numéro de tour courant `currentRound`.
2. **Un verrou d'émission atomique local** (`castsThisTurn`) incrémenté dès l'émission de la trame Socket (sans attendre l'acquittement serveur) pour empêcher les doubles lancers en mode Turbo.

### Spécification des Préconditions par Sort

#### 1. Balise de Survie (ID 32474)
```text
Précondition :
- currentRound >= (lastCastRound[32474] + 3)
- castsThisTurn[32474] == 0
Action de garde :
Si (currentRound < lastCastRound[32474] + 3), exclure 32474 de la sélection du tour.
```

#### 2. Pilori (ID 14011)
```text
Précondition :
- currentRound >= (lastCastRound[14011] + 3)
- castsThisTurn[14011] == 0
- !hasActiveState(4199)  // Ne pas relancer si l'état Pilori est encore actif
Action de garde :
Verrouiller immédiatement castsThisTurn[14011] = 1 dès l'injection. Interdire toute relance avant (lastCastRound + 3).
```

#### 3. Transposition (ID 12736)
```text
Précondition :
- currentRound >= 2     // initialCooldown = 1 tour : interdit au tour 1
- currentRound >= (lastCastRound[12736] + 3)
- castsThisTurn[12736] == 0
- !hasActiveState(7)    // statesCriterion HS!7 : interdit si Pesanteur
- !hasActiveState(97)   // interdit si Indéplaçable (ex: sous Pilori)
Action de garde :
Ne JAMAIS tenter Transposition deux fois dans le même tour (verrou strict castsThisTurn == 0). Désactiver tout repli "FILET" sur 12736.
```

#### 4. Piège d'Immobilisation (ID 12910)
```text
Précondition :
- currentRound >= (lastCastRound[12910] + 4)
- castsThisTurn[12910] == 0
Action de garde :
Bloquer l'évaluation du sort si (currentRound - lastCastRound < 4) et poser le verrou castsThisTurn = 1 dès le cast.
```

#### 5. Sacrifice (ID 12739)
```text
Précondition :
- currentRound >= (lastCastRound[12739] + 4)
- castsThisTurn[12739] == 0
Action de garde :
Empêcher formellement la réinjection de Sacrifice dans le même tour (100% des rejets venaient d'un second cast à +30ms).
```

#### 6. Invisibilité (ID 12913)
```text
Précondition :
- currentRound >= (lastCastRound[12913] + 3)
- castsThisTurn[12913] == 0
- !hasActiveState(250) && !hasActiveState(5348) // Pas déjà invisible
Action de garde :
Bloquer le cast tant que (currentRound < lastCastRound + 3).
```

#### 7. Mantiscroc (ID 24011)
```text
Précondition :
- currentRound >= (lastCastRound[24011] + 2)
- castsThisTurn[24011] == 0
Action de garde :
Interdire le double lancer dans le même tour et respecter l'intervalle de relance de 2 tours.
```

#### 8. Punition (ID 12741)
```text
Précondition :
- currentRound >= (lastCastRound[12741] + 3)
- castsThisTurn[12741] == 0
Action de garde :
Ne pas planifier Punition au tour 2 ou 3 si elle a été lancée au tour 1 ou 2. Attendre 3 tours complets.
```

#### 9. Concentration de Chakra (ID 12903)
```text
Précondition :
- currentRound >= 2     // initialCooldown = 1 tour
- currentRound >= (lastCastRound[12903] + 3)
- castsThisTurn[12903] == 0
Action de garde :
Verrouiller castsThisTurn[12903] = 1 dès la première émission. Interdire tout second lancer dans le même tour.
```

---

## 8. Conclusion Technique

Le refus serveur 175 n'est causé par aucun bug obscur de géométrie ou de désynchronisation de carte : il s'agit exclusivement de **violations temporelles de cooldown et de lancers par tour** provoquées par l'architecture Turbo du bot :
1. Le combat-planner ou le mode Turbo injecte une seconde requête avant d'avoir mis à jour son état local d'admissibilité.
2. Les tables de données de cooldown (`minCastInterval`, `initialCooldown`) lues dans `spelllevelsdataroot` n'étaient pas rigoureusement consultées ou respectées avant de soumettre les sorts dans la boucle décisionnelle.

En intégrant les gardes ci-dessus sur `currentRound`, `lastCastRound` et `castsThisTurn`, **l'intégralité des 147 rejets serveur 175 est éliminée**.
