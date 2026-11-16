# Kit de Sorts de la Classe ELIOTROPE (Données Officielles Client Dofus 3 Unity)

**Date d'extraction :** 9 novembre 2026  
**Source des données :** Fichiers locaux du client Dofus installés sur la machine (`Dofus-dofus3/Dofus_Data/StreamingAssets/Content/`)  
**Méthode d'extraction :** Outil autonome C# .NET 10 via `AssetsTools.NET 3.0.0` (même bibliothèque et même pattern que `GameNameResolver.cs`)  
**Fichiers bundles sources exploités :**
- `Content/Data/data_assets_breedsdataroot.asset.bundle` : Définition des classes (`id`, `shortNameId`, `descriptionId`)
- `Content/Data/data_assets_spellvariantsdataroot.asset.bundle` : Paires de variantes (`id`, `breedId`, `spellIds`)
- `Content/Data/data_assets_spellsdataroot.asset.bundle` : Définition générale des sorts (`id`, `nameId`, `descriptionId`, `iconId`, `spellLevels`)
- `Content/Data/data_assets_spelllevelsdataroot.asset.bundle` : Paramètres par grade/niveau (`id`, `spellId`, `grade`, `minPlayerLevel`, `apCost`, `minRange`, `range`, `m_flags`, `minCastInterval`, `maxCastPerTurn`, `maxCastPerTarget`, `statesCriterion`, `effects`)
- `Content/Data/data_assets_spellstatesdataroot.asset.bundle` : Table des états de sort (`id`, `nameId`)
- `Content/Data/data_assets_effectsdataroot.asset.bundle` : Modèles d'effets/actions (`id`, `descriptionId`, `elementId`)
- `Content/I18n/fr.bin` : Textes français canoniques du jeu (index binaire Ankama : `nameId` -> offset UTF-8)

---

## Avertissement Préalable : Résolution de l'Identifiant de Classe (Breed ID)

Le mandat initial mentionnait : *"ELIOTROPE (breed id 15)"*.  
L'ouverture et la vérification rigoureuse du bundle officiel client **`data_assets_breedsdataroot.asset.bundle`** croisé avec **`fr.bin`** révèle formellement la nomenclature exacte des classes dans le moteur Dofus :

| Breed ID | Clé `shortNameId` | Nom résolu (`fr.bin`) | Extrait description officielle (`descriptionId`) |
| :---: | :---: | :--- | :--- |
| **14** | 657 | **Zobal** | *"Les Zobals portent des masques..."* |
| **15** | 92496 | **Steamer** | *"Les Steamers ont plus d'une tour dans leur sac mécanique..."* |
| **16** | 504829 | **Eliotrope** | *"Apparus par accident, les Eliotropes sont des reflets de leur créateur..."* |
| **17** | 577229 | **Huppermage** | *"Les Huppermages sont capables de combiner les quatre éléments..."* |
| **18** | 720113 | **Ouginak** | *"Quand on est un barbare avec une mâchoire d'acier..."* |
| **20** | 970511 | **Forgelance** | *"Les Forgelances sont des lanciers sans foi ni loi..."* |

- Le **Breed ID 15** correspond au **Steamer** (sorts : *Harponneuse, Tactirelle, Gardienne, Scaphandre, Foène, Sonar...*).
- Le **Breed ID 16** correspond bien à l'**ELIOTROPE** (sorts : *Portail, Errance, Neutral, Rayon de Wakfu, Commotion, Audace...*).

Conformément à la règle de travail sur données réelles et pour satisfaire l'objectif de monter un **Eliotrope** niveau 200 en exploitant ses portails, **l'ensemble des données extraites, documentées et analysées ci-dessous correspond au véritable kit Eliotrope (Breed 16)**.

---

## 1. Liste Complète des Sorts de l'Eliotrope (44 Sorts / 22 Paires de Variantes)

Chaque valeur ci-dessous provient directement de l'extraction des bundles :
- **ID & Nom :** `spellsdataroot` (`id`, `nameId`) résolu dans `fr.bin`.
- **Variante :** `spellvariantsdataroot` (`id`, `breedId=16`, `spellIds[2]`).
- **Niveau Max, PA, Portée, Ligne de vue, Lancer en ligne, AoE, Cooldown, Lancers/tour/cible :** `spelllevelsdataroot` au rang maximal (`grade` max) :
  - PA : champ `apCost`
  - PO min / max : champs `minRange` / `range`
  - PO Modifiable : `m_flags` bit 6 (`& 64`)
  - Ligne de vue (LdV) : `m_flags` bit 2 (`& 4`)
  - Lancer en ligne : `m_flags` bit 0 (`& 1`)
  - AoE (Zone) : `effects[0].zoneDescr.shape` (caractère ASCII Ankama) + `param1` (rayon/taille)
  - Relance (Cooldown) : champ `minCastInterval` (0 = aucun délai)
  - Lancers par tour / cible : champs `maxCastPerTurn` / `maxCastPerTarget` (0 = sans limite spécifique)
  - Dégâts et éléments : tableau `effects` (`actionId`, `diceNum`, `diceSide`, `effectElement`)

| ID | Nom du Sort | Variante (Paire) | Rang Max (Niv) | Coût PA | Portée | PO Mod. | LdV | En Ligne | Forme & Taille Zone | Relance | Max/ Tour | Max/ Cible | Élément & Dés (Rang Max) |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **14575** | Affront | Mépris (14602) | Rang 3 (132) | 3 | 1-5 | Oui | Oui | Oui | Ligne T (1) | 0 | 2 | ∞ | Air : 26-28 |
| **14579** | Rayon de Wakfu | Faisceau (14606) | Rang 3 (136) | 3 | 1-5 | Oui | Oui | Oui | Ligne (2) | 0 | 2 | ∞ | Feu : 26-28 |
| **14583** | Commotion | Convulsion (14605) | Rang 3 (134) | 3 | 0-6 | Non | Oui | Non | Carré (1) | 0 | 2 | ∞ | Terre : 23-26 |
| **14593** | Audace | Tribulation (14603) | Rang 3 (133) | 3 | 1-5 | Non | Non | Non | Point (1) | 0 | 3 | 2 | Eau : 26-29 |
| **14574** | Portail | Errance (14604) | Rang 3 (139) | 1 | 1-6 | Non | Oui | Non | Point (1) | 0 | 2 | ∞ | — *(Utilitaire / Soutien)* |
| **14582** | Neutral | Interruption (14607) | Rang 3 (144) | 1 | 0-8 | Non | Oui | Non | Point (1) | 0 | 2 | ∞ | — *(Utilitaire / Soutien)* |
| **14620** | Persiflage | Sinécure (14610) | Rang 3 (149) | 3 | 1-6 | Oui | Oui | Non | Point (1) | 0 | 3 | 2 | Terre : 25-29 |
| **14587** | Cicatrisation | Conjuration (14622) | Rang 3 (154) | 3 | 0-2 | Non | Oui | Non | Cercle (2) | 0 | 1 | ∞ | — *(Utilitaire / Soutien)* |
| **14608** | Outrage | Aplomb (14618) | Rang 3 (159) | 3 | 1-7 | Oui | Oui | Non | Point (1) | 0 | 2 | ∞ | Feu : 24-28 |
| **14589** | Odyssée | Exode (14616) | Rang 3 (164) | 1 | 1-4 | Non | Oui | Non | Point (1) | 0 | 2 | ∞ | — *(Utilitaire / Soutien)* |
| **14637** | Insolence | Camouflet (14590) | Rang 3 (169) | 3 | 1-6 | Oui | Oui | Non | Point (1) | 0 | 3 | 2 | Eau : 25-28 |
| **14617** | Cabale | Résilience (14615) | Rang 3 (174) | 2 | 0-6 | Oui | Oui | Non | Croix diag. (1) | 0 | 1 | ∞ | — *(Utilitaire / Soutien)* |
| **14592** | Brimade | Cataclysme (14619) | Rang 3 (179) | 3 | 0-6 | Non | Non | Non | Carré (1) | 0 | 2 | ∞ | Air : 23-25 |
| **14591** | Sillage | Stupeur (14585) | Rang 3 (184) | 3 | 1-4 | Non | Oui | Non | Point (1) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14581** | Thérapie | Poing Fulgurant (14576) | Rang 3 (189) | 3 | 1-7 | Oui | Oui | Non | Point (1) | 0 | 3 | 2 | Terre : 23-25 (vol) |
| **14577** | Distribution | Orgueil (14614) | Rang 3 (194) | 2 | 0-6 | Oui | Oui | Non | Point (1) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14584** | Affliction | Offense (14588) | Rang 3 (198) | 4 | 1-6 | Oui | Oui | Non | Point (1) | 0 | 3 | 2 | Eau : 23-26 (vol) + 23-26 (poison si portail) |
| **14580** | Transcendance | Exil (14609) | Rang 2 (137) | 2 | 0-0 | Non | Non | Non | Point (1) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14595** | Raillerie | Sarcasme (14638) | Rang 2 (142) | 4 | 1-6 | Oui | Oui | Non | Point (1) | 0 | 3 | 2 | Air : 32-36 |
| **14611** | Résonance | Vestige (14612) | Rang 2 (147) | 2 | 0-6 | Non | Oui | Non | Point (1) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14594** | Extinction | Sermon (14613) | Rang 2 (152) | 5 | 1-5 | Oui | Oui | Non | Point (1) | 0 | 2 | 1 | Feu : 32-35 + 32-35 (poison si portail) |
| **14596** | Entraide | Coalition (14621) | Rang 2 (157) | 2 | 0-0 | Non | Non | Non | Cercle (2) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14602** | Mépris | Affront (14575) | Rang 2 (162) | 3 | 1-7 | Oui | Oui | Oui | Ligne (2) | 0 | 2 | ∞ | Air : 24-27 |
| **14603** | Tribulation | Audace (14593) | Rang 2 (167) | 3 | 1-7 | Oui | Oui | Oui | Ligne T (1) | 0 | 2 | ∞ | Eau : 23-26 |
| **14605** | Convulsion | Commotion (14583) | Rang 2 (172) | 3 | 0-5 | Non | Oui | Non | Croix ortho. (1) | 0 | 2 | ∞ | Terre : 24-27 |
| **14606** | Faisceau | Rayon de Wakfu (14579) | Rang 2 (177) | 3 | 1-6 | Oui | Oui | Oui | Cône (1) | 0 | 2 | ∞ | Feu : 23-26 (vol si portail) |
| **14604** | Errance | Portail (14574) | Rang 2 (182) | 1 | 1-4 | Non | Oui | Non | Point (1) | 0 | 3 | ∞ | — *(Utilitaire / Soutien)* |
| **14607** | Interruption | Neutral (14582) | Rang 2 (187) | 1 | 0-0 | Non | Non | Non | Tout le terrain | 0 | 2 | ∞ | — *(Utilitaire / Soutien)* |
| **14610** | Sinécure | Persiflage (14620) | Rang 2 (192) | 2 | 1-6 | Oui | Oui | Non | Point (1) | 0 | 2 | ∞ | Air : 12-14 (vol) |
| **14622** | Conjuration | Cicatrisation (14587) | Rang 2 (197) | 2 | 1-6 | Non | Oui | Non | Point (1) | 0 | 2 | 1 | — *(Utilitaire / Soutien)* |
| **14618** | Aplomb | Outrage (14608) | Rang 1 (135) | 4 | 1-8 | Oui | Oui | Non | Point (1) | 0 | 3 | 2 | Eau : 32-36 |
| **14616** | Exode | Odyssée (14589) | Rang 1 (140) | 2 | 1-1 | Non | Non | Oui | Point (1) | 0 | 1 | ∞ | — *(Utilitaire / Soutien)* |
| **14590** | Camouflet | Insolence (14637) | Rang 1 (145) | 3 | 1-4 | Oui | Oui | Non | Croix diag. (1) | 0 | 2 | ∞ | Terre : 24-27 |
| **14615** | Résilience | Cabale (14617) | Rang 1 (150) | 3 | 0-4 | Oui | Oui | Non | Point (1) | 0 | 2 | 1 | — *(Utilitaire / Soutien)* |
| **14619** | Cataclysme | Brimade (14592) | Rang 1 (155) | 4 | 0-4 | Non | Non | Oui | Carré (1) | 0 | 1 | ∞ | Feu : 34-38 |
| **14585** | Stupeur | Sillage (14591) | Rang 1 (160) | 2 | 1-5 | Non | Oui | Non | Point (1) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14576** | Poing Fulgurant | Thérapie (14581) | Rang 1 (165) | 3 | 1-4 | Non | Oui | Non | Point (1) | 0 | 3 | 2 | Eau : 23-25 |
| **14614** | Orgueil | Distribution (14577) | Rang 1 (170) | 2 | 0-6 | Non | Oui | Non | Point (1) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14588** | Offense | Affliction (14584) | Rang 1 (175) | 4 | 0-7 | Non | Oui | Non | Cercle (2) | 0 | 1 | ∞ | Feu : 30-34 |
| **14609** | Exil | Transcendance (14580) | Rang 1 (180) | 2 | 0-6 | Non | Oui | Non | Point (1) | 2 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14638** | Sarcasme | Raillerie (14595) | Rang 1 (185) | 4 | 1-4 | Non | Oui | Non | Point (1) | 0 | 3 | 2 | Terre : 34-38 |
| **14612** | Vestige | Résonance (14611) | Rang 1 (190) | 2 | 1-3 | Non | Oui | Non | Point (1) | 3 tr | ∞ | ∞ | — *(Utilitaire / Soutien)* |
| **14613** | Sermon | Extinction (14594) | Rang 1 (195) | 4 | 1-5 | Oui | Oui | Non | Point (1) | 0 | 3 | 1 | Air : 38-42 |
| **14621** | Coalition | Entraide (14596) | Rang 1 (200) | 2 | 0-4 | Non | Oui | Non | Point (1) | 0 | 1 | ∞ | — *(Utilitaire / Soutien)* |

---

## 2. Étude Approfondie des Sorts de Portail (Mécaniques & Télémétrie)

Toutes les mécaniques ci-dessous sont issues du décodage des actions du bundle `data_assets_spelllevelsdataroot.asset.bundle` et des masques de ciblage `targetMask`.

### 2.1 Sorts Créateurs de Portails (`actionId = 1181`)

L'action **1181** correspond dans `effectsdataroot` au modèle :  
`Pose un portail (+#3% dommages, +#1% dommages par case d'éloignement entre 2 portails)`

| ID Sort | Nom du Sort | Rang Max | Coût PA | Portée | Ligne de Vue | Case Libre Requise | Modalités & Effets Déclenchés |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :--- |
| **14574** | **Portail** | Rang 3 (139) | 1 PA | 1-6 | Oui | **Oui** (`m_flags & 8`) | Pose 1 portail sur cellule libre. Confère +2% Dommages Finaux (`actionId 1171`) et +2% Soins Finaux (`actionId 2971`) pendant 3 tours au lanceur par sort projeté (cumulable 10 fois). |
| **14604** | **Errance** *(Variante)* | Rang 2 (182) | 1 PA | 1-4 | Oui | Non | Pose 1 portail à portée plus courte (1-4). 3 lancers par tour au lieu de 2. Mêmes bonus finaux. |
| **14591** | **Sillage** | Rang 3 (184) | 3 PA | 1-4 | Oui | Non | **Pose un portail sous le lanceur** puis le téléporte sur la case ciblée (`actionId 4`). Condition d'état : `HS!7` (interdit en Pesanteur). Relance 3 tours. |
| **14585** | **Stupeur** *(Variante)* | Rang 1 (160) | 2 PA | 1-5 | Oui | Non | **Pose DEUX portails simultanément** : un sous le lanceur et un sous la cible, puis échange les positions (`actionId 8`). Condition d'état : `HS!7`. Relance 3 tours. |
| **14609** | **Exil** *(Variante)* | Rang 1 (180) | 2 PA | 0-6 | Oui | Non | Pose et **active immédiatement un portail sous une cible**, déclenchant un `Téléportail` immédiat (`actionId 1182`). Relance 2 tours. |
| **14611** | **Résonance** | Rang 2 (147) | 2 PA | 0-6 | Oui | Non | Pose l'état Résonance (`6251`) sur la cible. **Sur la 1re attaque subie** (`Triggers=D|XD`), pose un portail sous elle et déclenche `Téléportail` (`actionId 1182`). Relance 3 tours. |
| **14612** | **Vestige** *(Variante)* | Rang 1 (190) | 2 PA | 1-3 | Oui | Non | Invoque le **Totem Eliotrope** (`actionId 181`, monstre ID 5109). Le totem pose un portail sous un allié qui l'attaque. |

### 2.2 Téléportation entre Portails (`actionId = 1182` et Mécanique de Déplacement)

La projection et la téléportation reposent sur deux canaux :
1. **L'action de déclenchement forcé `1182` (`Téléportail`) :**  
   Présente dans les effets de **Exil (14609)** et **Résonance (14611)**. Elle force l'entité présente sur la cellule du portail à emprunter le réseau instantanément vers le portail de sortie le plus éloigné / adéquat.
2. **Le franchissement au déplacement (Moteur de Combat) :**  
   Toute entité marchant ou finissant son déplacement sur une case de portail actif est téléportée vers le portail de sortie. *Règle client stipulée dans la description du sort Portail : les ennemis ne peuvent pas emprunter un portail au premier tour de jeu.*

### 2.3 Désactivation et Gestion du Réseau (`actionId = 1183`)

L'action **1183** (`Désactive un portail`) est le pivot de fermeture :
- **Neutral (14582) :** Portée 0-8, 1 PA. Désactive un portail précis ciblé. Rembourse immédiatement 1 PA (`actionId 120`, coût net = 0 PA). 2 lancers par tour.
- **Interruption (14607, variante) :** Portée 0-0, 1 PA, Zone `A(1)` (Tout le terrain). **Désactive simultanément TOUS les portails actifs du terrain**. 2 lancers par tour.

### 2.4 Nombre Maximal de Portails et Durée de Vie

- **Durée de vie dans les données client :** Le champ `duration` de l'action `1181` vaut **`-1`** sur tous les sorts créateurs. Une durée de `-1` encode une **durée infinie / permanente** (le portail reste actif indéfiniment jusqu'à fermeture manuelle via Neutral/Interruption ou remplacement).
- **Limite de coexistence simultanée :** Dans les règles du moteur Dofus, un Eliotrope peut maintenir jusqu'à **4 portails actifs simultanément**. Lors du lancer d'un 5ème portail, le portail le plus ancien (Portail 1) disparaît automatiquement.
- **Amplification du nombre de lancers :** Le sort **Transcendance (14580)** applique l'action **290** (`+#3 lancers par tour au sort #1`, `diceNum=14574`, `value=1`), permettant d'effectuer **3 poses de portail** dans le même tour.

### 2.5 Modificateurs de Portée et d'Effets Liés aux Portails

Dans les paramètres de l'action `1181` :
- `diceNum = 2` : **Bonus de +2% de dégâts finaux par case d'éloignement** entre le portail d'entrée et le portail de sortie du réseau.
- Effets `1171` et `2971` sur le lanceur : **+2% de dommages finaux et +2% de soins finaux** pendant 3 tours chaque fois qu'un sort est tiré à travers un portail (cumulable 10 fois, soit jusqu'à +20% de bonus fixe cumulé).

### 2.6 Les 20 Sorts Modifiés lors d'une Projection à Travers un Portail

Dans le moteur Unity, le champ `targetMask` utilise les discriminateurs **`r`** (*lancer direct / hors portail*) et **`R`** (*lancer routé à travers un portail*).  
Voici la table exhaustive des 20 sorts modifiés :

| ID Sort | Nom du Sort | Élément / Type | Comportement Direct (`r`) | Comportement Routé dans un Portail (`R`) |
| :---: | :--- | :---: | :--- | :--- |
| **14575** | Affront | Air | Dégâts Air (26-28) + **Repousse de 2 cases** (`Mask=a,A,r`) | Dégâts Air identiques, **aucune poussée** (sort de frappe pure). |
| **14583** | Commotion | Terre | Attire les cibles vers le centre de zone (1 case, hors lanceur) | **Attire également le lanceur** vers le centre (`Mask=a,A,R`). |
| **14593** | Audace | Eau | Dégâts Eau (26-29) + **Avance le lanceur de 2 cases** vers la cible | Dégâts Eau identiques, **aucun déplacement du lanceur**. |
| **14620** | Persiflage | Terre | Dégâts Terre (25-29) + **Retire 3 PO** (`action 116`, `Mask=r`) | Dégâts Terre (25-29) + **Vole 3 PO** (`action 320`, `Mask=R`). |
| **14608** | Outrage | Feu | Dégâts Feu (24-28) | Dégâts Feu (24-28) + **Confère +1 PO au lanceur** (`action 117`, 2 tours). |
| **14617** | Cabale | Soutien | Zone croix : **+100 Puissance** et **+1 PM** aux alliés (2 tours) | Zone croix : **+200 Puissance** et **+2 PM** aux alliés (bonus doublé !). |
| **14592** | Brimade | Air | Dégâts Air (23-25) + repousse du centre (2 cases, hors lanceur) | **Repousse également le lanceur** depuis le centre (`Mask=a,A,R`). |
| **14584** | Affliction | Eau | Vole 23-26 PV Eau à l'ennemi ou soigne un allié | Ajoute un **poison Eau de 23-26** au début de tour de l'ennemi / allié (`Triggers=TB`). |
| **14595** | Raillerie | Air | Dégâts Air (32-36) | Dégâts Air (32-36) + **Retire 3 PM** (`action 1080`, 1 tour). |
| **14594** | Extinction | Feu | Dégâts Feu (32-35) | Dégâts Feu (32-35) + **Poison Feu de 32-35** en fin de tour (`Triggers=TE`). |
| **14606** | Faisceau | Feu | Zone cône : Dégâts Feu ordinaires (23-26) (`action 99`, `Mask=r`) | Zone cône : Dégâts transformés en **Vol de vie Feu** (23-26) (`action 94`, `Mask=R`). |
| **14610** | Sinécure | Air | Vol de vie Air (12-14) / soin allié | Vol de vie Air (12-14) + **Retire 40 Résistance Poussée** (`action 417`, 2 tours). |
| **14622** | Conjuration | Soutien | Réduit la durée des effets de 1 tour (`action 1075`) | Réduit la durée de 1 tour + **Réduit les soins reçus par la cible de 30%** (`x70%`). |
| **14618** | Aplomb | Eau | Dégâts Eau (32-36) | Dégâts Eau (32-36) + **Confère +1 PM au lanceur** (`action 128`, 1 tour). |
| **14590** | Camouflet | Terre | Zone croix : Dégâts Terre (24-27) | Zone croix : Dégâts Terre (24-27) + **Retire 40 Dommages** aux ennemis (`action 145`). |
| **14615** | Résilience | Soin | Soigne l'allié ciblé de 10% de ses PV max | Soin 10% PV max + **Augmente ses soins reçus de 15%** (`x115%`, `action 1159`). |
| **14576** | Poing Fulgurant | Eau | Dégâts Eau (23-25) + **Repousse de 2 cases** (`action 5`, `Dice=2-0`, `Mask=r`) | Dégâts Eau (23-25) + **Repousse de 4 cases** (`action 5`, `Dice=4-0`, `Mask=R`). |
| **14638** | Sarcasme | Terre | Dégâts Terre (34-38) | Dégâts Terre (34-38) + **Retire 3 PA** à la cible (`action 1079`, 1 tour). |
| **14613** | Sermon | Air | Dégâts Air (38-42) + **Repousse de 2 cases** (`action 5`, `Dice=2-0`, `Mask=r`) | Dégâts Air (38-42) + **Repousse de 4 cases** (`action 5`, `Dice=4-0`, `Mask=R`). |
| **14621** | Coalition | Soutien | Confère +1 PA à l'allié ciblé + état Coalition | **Confère +2 PA à l'allié ciblé** (bonus PA doublé !) + état Coalition. |

---

## 3. Préconditions d'États (`statesCriterion`) & Résolution des Rejets Serveur 175

### 3.1 La Règle Technique du Message Serveur 175

Le message serveur Dofus 175 (`[combat-cast] text message=175 text=Impossible de lancer ce sort actuellement.`) sanctionne toute violation de contrainte d'admissibilité de sort :
1. **Délai de relance non écoulé** (`minCastInterval` non respecté).
2. **Dépassement de la limite par tour ou par cible** (`maxCastPerTurn` ou `maxCastPerTarget`).
3. **Non-satisfaction de l'expression de condition d'état `statesCriterion`**.

### 3.2 Décryptage Exhaustif du Champ `statesCriterion` de l'Eliotrope

Dans le bundle `data_assets_spelllevelsdataroot.asset.bundle`, les sorts Eliotrope suivants portent une contrainte d'état explicite :

| ID Sort | Nom du Sort | Grades Concernés | Expression `statesCriterion` | ID d'État | Nom Résolu dans `spellstatesdataroot` / `fr.bin` | Explication Fonctionnelle |
| :---: | :--- | :---: | :---: | :---: | :--- | :--- |
| **14591** | **Sillage** | Grades 1, 2, 3 | **`HS!7`** | **7** | **Pesanteur** | Le sort téléporte le lanceur (`actionId 4`). L'état Pesanteur interdisant tout déplacement téléporté, le sort est prohibé sous cet état. |
| **14616** | **Exode** | Grade 1 | **`HS!7`** | **7** | **Pesanteur** | Le sort effectue une téléportation symétrique (`actionId 1104`). Strictement interdit sous l'état Pesanteur. |
| **14585** | **Stupeur** | Grade 1 | **`HS!7`** | **7** | **Pesanteur** | Le sort effectue un échange de positions (`actionId 8`). Strictement interdit sous l'état Pesanteur. |

*Syntaxe de la formule :*  
- **`HS`** = *Has State* (Possède l'état)  
- **`!`** = Négation logique (Ne doit PAS posséder)  
- **`7`** = Identifiant d'état dans `spellstatesdataroot` -> **Pesanteur**  
*Règle pour le bot de combat :* Si l'Eliotrope est affligé de l'état 7 (Pesanteur), le moteur d'IA doit **immédiatement exclure Sillage, Exode et Stupeur** de ses options de lancer sous peine de provoquer un rejet 175 instantané.

### 3.3 États Spécifiques Appliqués par l'Eliotrope en Combat (`actionId = 950`)

| ID d'État | Nom Résolu (`fr.bin`) | Sort Source | Effet en Jeu |
| :---: | :--- | :--- | :--- |
| **96** | **Intaclable** | Transcendance (14580) | Le lanceur ignore le tacle adverse pendant 1 tour. |
| **6251** | **Résonance** | Résonance (14611) | Déclenche la pose d'un portail sous la cible et son saut au premier coup subi. |
| **6252** | **Entraide** | Entraide (14596) | Confère +1 PA et un soin de zone de 3% PV max à chaque passage de portail. |
| **3979** | **Tribulation cibles** | Tribulation (14603) | Marqueur de ciblage pour l'attirance en ligne vers le portail. |
| **2252** | **Cataclysme** | Cataclysme (14619) | Marqueur de zone différée infligeant des dégâts Feu en fin de tour. |
| **6049** | **Orgueil** | Orgueil (14614) | Renvoie 30% des dommages subis à l'attaquant et confère +150 Puissance au tour suivant. |
| **6249** | **Coalition** | Coalition (14621) | Soigne la cible de 3% de ses PV max chaque fois qu'elle traverse un portail. |

---

## 4. Tableau de Progression du Niveau 1 au Niveau 200 (`minPlayerLevel`)

L'Eliotrope gagne ses sorts de base aux niveaux 1 à 90, puis débloque l'intégralité de ses variantes aux niveaux 95 à 200, tout en faisant évoluer le rang (grade) de ses sorts intermédiaires.  
Voici la chronologie intégrale des **91 événements de montée en puissance** enregistrés dans le client :

| Niveau Requis | Sort Débloqué ou Évolué | ID Sort | Événement & Rang | Coût PA | Portée |
| :---: | :--- | :---: | :--- | :---: | :---: |
| **1** | **Affront** | 14575 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-5 |
| **1** | **Rayon de Wakfu** | 14579 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-5 |
| **1** | **Commotion** | 14583 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 0-6 |
| **1** | **Audace** | 14593 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-5 |
| **5** | **Portail** | 14574 | **DÉBLOCAGE Sort** (Rang 1/3) | 1 PA | 1-4 |
| **10** | **Neutral** | 14582 | **DÉBLOCAGE Sort** (Rang 1/3) | 1 PA | 0-6 |
| **15** | **Persiflage** | 14620 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-4 |
| **20** | **Cicatrisation** | 14587 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 0-2 |
| **25** | **Outrage** | 14608 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-5 |
| **30** | **Odyssée** | 14589 | **DÉBLOCAGE Sort** (Rang 1/3) | 1 PA | 1-2 |
| **35** | **Insolence** | 14637 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-6 |
| **40** | **Cabale** | 14617 | **DÉBLOCAGE Sort** (Rang 1/3) | 2 PA | 0-4 |
| **45** | **Brimade** | 14592 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 0-4 |
| **50** | **Sillage** | 14591 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-3 |
| **55** | **Thérapie** | 14581 | **DÉBLOCAGE Sort** (Rang 1/3) | 3 PA | 1-5 |
| **60** | **Distribution** | 14577 | **DÉBLOCAGE Sort** (Rang 1/3) | 2 PA | 0-4 |
| **65** | **Affliction** | 14584 | **DÉBLOCAGE Sort** (Rang 1/3) | 4 PA | 1-4 |
| **66** | **Affront** | 14575 | Évolution Rang 2/3 | 3 PA | 1-5 |
| **67** | **Audace** | 14593 | Évolution Rang 2/3 | 3 PA | 1-5 |
| **68** | **Commotion** | 14583 | Évolution Rang 2/3 | 3 PA | 0-6 |
| **69** | **Rayon de Wakfu** | 14579 | Évolution Rang 2/3 | 3 PA | 1-5 |
| **70** | **Transcendance** | 14580 | **DÉBLOCAGE Sort** (Rang 1/2) | 2 PA | 0-0 |
| **72** | **Portail** | 14574 | Évolution Rang 2/3 | 1 PA | 1-5 |
| **75** | **Raillerie** | 14595 | **DÉBLOCAGE Sort** (Rang 1/2) | 4 PA | 1-5 |
| **77** | **Neutral** | 14582 | Évolution Rang 2/3 | 1 PA | 0-7 |
| **80** | **Résonance** | 14611 | **DÉBLOCAGE Sort** (Rang 1/2) | 2 PA | 0-5 |
| **82** | **Persiflage** | 14620 | Évolution Rang 2/3 | 3 PA | 1-5 |
| **85** | **Extinction** | 14594 | **DÉBLOCAGE Sort** (Rang 1/2) | 5 PA | 1-4 |
| **87** | **Cicatrisation** | 14587 | Évolution Rang 2/3 | 3 PA | 0-2 |
| **90** | **Entraide** | 14596 | **DÉBLOCAGE Sort** (Rang 1/2) | 2 PA | 0-0 |
| **92** | **Outrage** | 14608 | Évolution Rang 2/3 | 3 PA | 1-6 |
| **95** | **Mépris** | 14602 | **DÉBLOCAGE Sort** (Rang 1/2) | 3 PA | 1-6 |
| **97** | **Odyssée** | 14589 | Évolution Rang 2/3 | 1 PA | 1-3 |
| **100** | **Tribulation** | 14603 | **DÉBLOCAGE Sort** (Rang 1/2) | 3 PA | 1-6 |
| **102** | **Insolence** | 14637 | Évolution Rang 2/3 | 3 PA | 1-6 |
| **105** | **Convulsion** | 14605 | **DÉBLOCAGE Sort** (Rang 1/2) | 3 PA | 0-5 |
| **107** | **Cabale** | 14617 | Évolution Rang 2/3 | 2 PA | 0-5 |
| **110** | **Faisceau** | 14606 | **DÉBLOCAGE Sort** (Rang 1/2) | 3 PA | 1-5 |
| **112** | **Brimade** | 14592 | Évolution Rang 2/3 | 3 PA | 0-5 |
| **115** | **Errance** | 14604 | **DÉBLOCAGE Sort** (Rang 1/2) | 1 PA | 1-3 |
| **117** | **Sillage** | 14591 | Évolution Rang 2/3 | 3 PA | 1-3 |
| **120** | **Interruption** | 14607 | **DÉBLOCAGE Sort** (Rang 1/2) | 1 PA | 0-0 |
| **122** | **Thérapie** | 14581 | Évolution Rang 2/3 | 3 PA | 1-6 |
| **125** | **Sinécure** | 14610 | **DÉBLOCAGE Sort** (Rang 1/2) | 2 PA | 1-5 |
| **127** | **Distribution** | 14577 | Évolution Rang 2/3 | 2 PA | 0-5 |
| **130** | **Conjuration** | 14622 | **DÉBLOCAGE Sort** (Rang 1/2) | 2 PA | 1-5 |
| **131** | **Affliction** | 14584 | Évolution Rang 2/3 | 4 PA | 1-5 |
| **132** | **Affront** | 14575 | Évolution Rang 3/3 | 3 PA | 1-5 |
| **133** | **Audace** | 14593 | Évolution Rang 3/3 | 3 PA | 1-5 |
| **134** | **Commotion** | 14583 | Évolution Rang 3/3 | 3 PA | 0-6 |
| **135** | **Aplomb** | 14618 | **DÉBLOCAGE Sort** (Rang 1/1) | 4 PA | 1-8 |
| **136** | **Rayon de Wakfu** | 14579 | Évolution Rang 3/3 | 3 PA | 1-5 |
| **137** | **Transcendance** | 14580 | Évolution Rang 2/2 | 2 PA | 0-0 |
| **139** | **Portail** | 14574 | Évolution Rang 3/3 | 1 PA | 1-6 |
| **140** | **Exode** | 14616 | **DÉBLOCAGE Sort** (Rang 1/1) | 2 PA | 1-1 |
| **142** | **Raillerie** | 14595 | Évolution Rang 2/2 | 4 PA | 1-6 |
| **144** | **Neutral** | 14582 | Évolution Rang 3/3 | 1 PA | 0-8 |
| **145** | **Camouflet** | 14590 | **DÉBLOCAGE Sort** (Rang 1/1) | 3 PA | 1-4 |
| **147** | **Résonance** | 14611 | Évolution Rang 2/2 | 2 PA | 0-6 |
| **149** | **Persiflage** | 14620 | Évolution Rang 3/3 | 3 PA | 1-6 |
| **150** | **Résilience** | 14615 | **DÉBLOCAGE Sort** (Rang 1/1) | 3 PA | 0-4 |
| **152** | **Extinction** | 14594 | Évolution Rang 2/2 | 5 PA | 1-5 |
| **154** | **Cicatrisation** | 14587 | Évolution Rang 3/3 | 3 PA | 0-2 |
| **155** | **Cataclysme** | 14619 | **DÉBLOCAGE Sort** (Rang 1/1) | 4 PA | 0-4 |
| **157** | **Entraide** | 14596 | Évolution Rang 2/2 | 2 PA | 0-0 |
| **159** | **Outrage** | 14608 | Évolution Rang 3/3 | 3 PA | 1-7 |
| **160** | **Stupeur** | 14585 | **DÉBLOCAGE Sort** (Rang 1/1) | 2 PA | 1-5 |
| **162** | **Mépris** | 14602 | Évolution Rang 2/2 | 3 PA | 1-7 |
| **164** | **Odyssée** | 14589 | Évolution Rang 3/3 | 1 PA | 1-4 |
| **165** | **Poing Fulgurant** | 14576 | **DÉBLOCAGE Sort** (Rang 1/1) | 3 PA | 1-4 |
| **167** | **Tribulation** | 14603 | Évolution Rang 2/2 | 3 PA | 1-7 |
| **169** | **Insolence** | 14637 | Évolution Rang 3/3 | 3 PA | 1-6 |
| **170** | **Orgueil** | 14614 | **DÉBLOCAGE Sort** (Rang 1/1) | 2 PA | 0-6 |
| **172** | **Convulsion** | 14605 | Évolution Rang 2/2 | 3 PA | 0-5 |
| **174** | **Cabale** | 14617 | Évolution Rang 3/3 | 2 PA | 0-6 |
| **175** | **Offense** | 14588 | **DÉBLOCAGE Sort** (Rang 1/1) | 4 PA | 0-7 |
| **177** | **Faisceau** | 14606 | Évolution Rang 2/2 | 3 PA | 1-6 |
| **179** | **Brimade** | 14592 | Évolution Rang 3/3 | 3 PA | 0-6 |
| **180** | **Exil** | 14609 | **DÉBLOCAGE Sort** (Rang 1/1) | 2 PA | 0-6 |
| **182** | **Errance** | 14604 | Évolution Rang 2/2 | 1 PA | 1-4 |
| **184** | **Sillage** | 14591 | Évolution Rang 3/3 | 3 PA | 1-4 |
| **185** | **Sarcasme** | 14638 | **DÉBLOCAGE Sort** (Rang 1/1) | 4 PA | 1-4 |
| **187** | **Interruption** | 14607 | Évolution Rang 2/2 | 1 PA | 0-0 |
| **189** | **Thérapie** | 14581 | Évolution Rang 3/3 | 3 PA | 1-7 |
| **190** | **Vestige** | 14612 | **DÉBLOCAGE Sort** (Rang 1/1) | 2 PA | 1-3 |
| **192** | **Sinécure** | 14610 | Évolution Rang 2/2 | 2 PA | 1-6 |
| **194** | **Distribution** | 14577 | Évolution Rang 3/3 | 2 PA | 0-6 |
| **195** | **Sermon** | 14613 | **DÉBLOCAGE Sort** (Rang 1/1) | 4 PA | 1-5 |
| **197** | **Conjuration** | 14622 | Évolution Rang 2/2 | 2 PA | 1-6 |
| **198** | **Affliction** | 14584 | Évolution Rang 3/3 | 4 PA | 1-6 |
| **200** | **Coalition** | 14621 | **DÉBLOCAGE Sort** (Rang 1/1) | 2 PA | 0-4 |

---

## 5. Couverture Élémentaire et Synergies de Rotations

L'Eliotrope dispose de **24 sorts de dégâts**, répartis de manière rigoureusement équilibrée : **exactement 6 sorts par voie élémentaire**.  
**Aucun sort n'est multi-élément** : chaque sort appartient strictement à l'un des 4 éléments fondamentaux (Air, Feu, Eau, Terre).

### 5.1 Voie Air (Mobilité, Poussée & Portée) - 6 Sorts

La voie Air est la reine du contrôle spatial et de la poussée :
1. **Affront (14575) :** 3 PA, PO 1-5 modifiable en ligne. Dés : 26-28 (Moy. 27). Repousse de 2 cases hors portail.
2. **Brimade (14592) :** 3 PA, PO 0-6 non modifiable sans LdV. Dés : 23-25 (Moy. 24). Zone carré Q(1), repousse de 2 cases.
3. **Raillerie (14595) :** 4 PA, PO 1-6 modifiable. Dés : 32-36 (Moy. 34). Retire 3 PM si routé dans un portail !
4. **Mépris (14602) :** 3 PA, PO 1-7 modifiable. Dés : 24-27 (Moy. 25.5). Zone ligne L(2), repousse de 1 case.
5. **Sinécure (14610) :** 2 PA, PO 1-6 modifiable. Dés : 12-14 vol (Moy. 13). Réduit les rési poussée de 40 si portail.
6. **Sermon (14613) :** 4 PA, PO 1-5 modifiable. Dés : 38-42 (Moy. 40). **Poussée massive de 4 cases si projeté dans un portail**.

*Rotation recommandée Air (Tour type à 11-12 PA) :*  
`Pose Portail (1 PA) -> Sermon à travers portail (4 PA, 40 dmg, poussée 4) -> Raillerie à travers portail (4 PA, 34 dmg, -3 PM) -> Affront (3 PA, 27 dmg)`.  
Total dégâts bruts de base : **101 dégâts de base** + bonus d'éloignement des portails !

### 5.2 Voie Feu (Dégâts Différés, Poisons & Régénération) - 6 Sorts

La voie Feu combine d'immenses ratios de dégâts avec les poisons de fin de tour et le vol de vie :
1. **Rayon de Wakfu (14579) :** 3 PA, PO 1-5 en ligne. Dés : 26-28 (Moy. 27). Zone ligne L(2).
2. **Outrage (14608) :** 3 PA, PO 1-7 modifiable. Dés : 24-28 (Moy. 26). Confère +1 PO au lanceur si portail.
3. **Extinction (14594) :** 5 PA, PO 1-5 modifiable. Dés : 32-35 direct + 32-35 poison si portail (Moy. **67 cumulé**).
4. **Faisceau (14606) :** 3 PA, PO 1-6 en ligne. Dés : 23-26 (Moy. 24.5). Zone cône V(1). Devient un vol de vie si portail.
5. **Cataclysme (14619) :** 4 PA, PO 0-4. Dés : 34-38 (Moy. 36). Zone carré G(1).
6. **Offense (14588) :** 4 PA, PO 0-7 sans LdV. Dés : 30-34 (Moy. 32). Zone cercle C(2).

*Rotation recommandée Feu (Tour type à 11-12 PA) :*  
`Extinction à travers portail (5 PA, 33.5 direct + 33.5 poison fin de tour) -> Cataclysme (4 PA, 36 dmg) -> Faisceau à travers portail (3 PA, 24.5 vol de vie)`.  
Total dégâts bruts de base : **127.5 dégâts de base** !

### 5.3 Voie Eau (Rapprochement, Poisons de Début de Tour & Harcèlement) - 6 Sorts

La voie Eau maximise le DPS immédiat et la mobilité d'attirance :
1. **Audace (14593) :** 3 PA, PO 1-5 sans LdV. Dés : 26-29 (Moy. 27.5). Avance de 2 cases hors portail.
2. **Insolence (14637) :** 3 PA, PO 1-6 modifiable. Dés : 25-28 (Moy. 26.5). Recul de 2 cases hors portail.
3. **Affliction (14584) :** 4 PA, PO 1-6 modifiable. Dés : 23-26 vol direct + 23-26 poison début de tour si portail (Moy. **49 cumulé**).
4. **Tribulation (14603) :** 3 PA, PO 1-7 modifiable. Dés : 23-26 (Moy. 24.5). Zone T(1), attire les cibles vers le portail.
5. **Aplomb (14618) :** 4 PA, PO 1-8 modifiable. Dés : 32-36 (Moy. 34). Confère +1 PM au lanceur si portail.
6. **Poing Fulgurant (14576) :** 3 PA, PO 1-4. Dés : 23-25 (Moy. 24). Pousse de 4 cases si portail.

*Rotation recommandée Eau (Tour type à 11-12 PA) :*  
`Affliction à travers portail (4 PA, 24.5 vol + 24.5 poison début de tour) -> Audace à travers portail (3 PA, 27.5 dmg) -> Insolence à travers portail (3 PA, 26.5 dmg) -> Errance (1 PA)`.  
Total dégâts bruts de base : **103 dégâts de base** + vol de vie.

### 5.4 Voie Terre (Retrait PA/PO, Attirance & Pilonnage Brut) - 6 Sorts

La voie Terre est la plus entravante et permet d'annihiler la portée et les PA adverses :
1. **Commotion (14583) :** 3 PA, PO 0-6 sans LdV. Dés : 23-26 (Moy. 24.5). Zone carré Q(1), attire vers le centre.
2. **Persiflage (14620) :** 3 PA, PO 1-6 modifiable. Dés : 25-29 (Moy. 27). Vole 3 PO si projeté dans un portail !
3. **Thérapie (14581) :** 3 PA, PO 1-7 modifiable. Dés : 23-25 vol (Moy. 24). Vol de vie Terre direct.
4. **Convulsion (14605) :** 3 PA, PO 0-5. Dés : 24-27 (Moy. 25.5). Zone croix +(1), repousse de 1 case.
5. **Camouflet (14590) :** 3 PA, PO 1-4 modifiable. Dés : 24-27 (Moy. 25.5). Zone croix X(1), retire 40 dommages si portail.
6. **Sarcasme (14638) :** 4 PA, PO 1-4. Dés : 34-38 (Moy. 36). Retire 3 PA si projeté dans un portail !

*Rotation recommandée Terre (Tour type à 11-12 PA) :*  
`Sarcasme à travers portail (4 PA, 36 dmg, -3 PA) -> Persiflage à travers portail (3 PA, 27 dmg, vol 3 PO) -> Commotion (3 PA, 24.5 dmg) -> Portail (1 PA)`.  
Total dégâts bruts de base : **87.5 dégâts de base** avec un retrait dévastateur de 3 PA et 3 PO !

---

## 6. Tableau « DÉGÂTS PAR PA » (Classement de Rentabilité pour l'IA de Combat)

Ce tableau fournit à l'IA le classement analytique strict de chaque sort de frappe au rang maximal :
- **Dégâts Moyens Directs :** Calculés sur l'espérance mathématique exacte des dés du bundle : `Moyenne = (Min + Max) / 2`.
- **Ratio Dégâts/PA (Direct) :** `Dégâts Moyens Directs / apCost`. C'est la métrique direct-cast immédiate.
- **Ratio Dégâts/PA (Avec Bonus Portail / Poison) :** Intègre les effets différés obligatoires déclenchés lors du tir à travers un portail (notamment les poisons à 100% de déclenchement sur Affliction et Extinction).

| Rang | Nom du Sort | ID Sort | Élément | Coût PA | Portée | Dés Réels (Rang Max) | Dégâts Moy. (Direct) | Dégâts / PA (Direct) | Dégâts / PA (Via Portail) | Limite Lancers / Tour | Limite Lancers / Cible |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **1** | **Sermon** | 14613 | Air | **4 PA** | 1-5 | 38-42 | 40.0 | **10.00** | 10.00 | 3 | 1 |
| **2** | **Audace** | 14593 | Eau | **3 PA** | 1-5 | 26-29 | 27.5 | **9.17** | 9.17 | 3 | 2 |
| **3** | **Affront** | 14575 | Air | **3 PA** | 1-5 | 26-28 | 27.0 | **9.00** | 9.00 | 2 | ∞ |
| **4** | **Rayon de Wakfu** | 14579 | Feu | **3 PA** | 1-5 | 26-28 | 27.0 | **9.00** | 9.00 | 2 | ∞ |
| **5** | **Persiflage** | 14620 | Terre | **3 PA** | 1-6 | 25-29 | 27.0 | **9.00** | 9.00 | 3 | 2 |
| **6** | **Cataclysme** | 14619 | Feu | **4 PA** | 0-4 | 34-38 | 36.0 | **9.00** | 9.00 | 1 | ∞ |
| **7** | **Sarcasme** | 14638 | Terre | **4 PA** | 1-4 | 34-38 | 36.0 | **9.00** | 9.00 | 3 | 2 |
| **8** | **Insolence** | 14637 | Eau | **3 PA** | 1-6 | 25-28 | 26.5 | **8.83** | 8.83 | 3 | 2 |
| **9** | **Outrage** | 14608 | Feu | **3 PA** | 1-7 | 24-28 | 26.0 | **8.67** | 8.67 | 2 | ∞ |
| **10** | **Raillerie** | 14595 | Air | **4 PA** | 1-6 | 32-36 | 34.0 | **8.50** | 8.50 | 3 | 2 |
| **11** | **Mépris** | 14602 | Air | **3 PA** | 1-7 | 24-27 | 25.5 | **8.50** | 8.50 | 2 | ∞ |
| **12** | **Convulsion** | 14605 | Terre | **3 PA** | 0-5 | 24-27 | 25.5 | **8.50** | 8.50 | 2 | ∞ |
| **13** | **Aplomb** | 14618 | Eau | **4 PA** | 1-8 | 32-36 | 34.0 | **8.50** | 8.50 | 3 | 2 |
| **14** | **Camouflet** | 14590 | Terre | **3 PA** | 1-4 | 24-27 | 25.5 | **8.50** | 8.50 | 2 | ∞ |
| **15** | **Commotion** | 14583 | Terre | **3 PA** | 0-6 | 23-26 | 24.5 | **8.17** | 8.17 | 2 | ∞ |
| **16** | **Tribulation** | 14603 | Eau | **3 PA** | 1-7 | 23-26 | 24.5 | **8.17** | 8.17 | 2 | ∞ |
| **17** | **Faisceau** | 14606 | Feu | **3 PA** | 1-6 | 23-26 | 24.5 | **8.17** | 8.17 | 2 | ∞ |
| **18** | **Brimade** | 14592 | Air | **3 PA** | 0-6 | 23-25 | 24.0 | **8.00** | 8.00 | 2 | ∞ |
| **19** | **Thérapie** | 14581 | Terre | **3 PA** | 1-7 | 23-25 | 24.0 | **8.00** | 8.00 | 3 | 2 |
| **20** | **Poing Fulgurant** | 14576 | Eau | **3 PA** | 1-4 | 23-25 | 24.0 | **8.00** | 8.00 | 3 | 2 |
| **21** | **Offense** | 14588 | Feu | **4 PA** | 0-7 | 30-34 | 32.0 | **8.00** | 8.00 | 1 | ∞ |
| **22** | **Extinction** | 14594 | Feu | **5 PA** | 1-5 | 32-35 | 33.5 | **6.70** | **13.40** *(poison)* | 2 | 1 |
| **23** | **Sinécure** | 14610 | Air | **2 PA** | 1-6 | 12-14 | 13.0 | **6.50** | 6.50 | 2 | ∞ |
| **24** | **Affliction** | 14584 | Eau | **4 PA** | 1-6 | 23-26 | 24.5 | **6.13** | **12.25** *(poison)* | 3 | 2 |

### Synthèse Décisionnelle pour l'IA de Combat

1. **Sorts de Finition / Burst Pur (Sans Portail) :**
   - **Sermon (Air, 4 PA) :** Avec un ratio de **10.00 dégâts/PA**, c'est le sort direct le plus rentable de toute la classe.
   - **Audace (Eau, 3 PA) :** Ratio de **9.17 dégâts/PA**, sans ligne de vue requise.
   - **Affront (Air), Rayon de Wakfu (Feu), Persiflage (Terre), Cataclysme (Feu), Sarcasme (Terre) :** Ratios de **9.00 dégâts/PA**.

2. **Sorts de Préparation et Poisons à Travers Portails :**
   - **Extinction (Feu, 5 PA) :** Bien qu'à 6.70 dégâts/PA en direct pur, son tir à travers un portail applique un poison équivalent (32-35 en fin de tour), propulsant sa rentabilité à **13.40 dégâts/PA** (le plus puissant de la classe si la cible survit au tour).
   - **Affliction (Eau, 4 PA) :** Passe de 6.13 dégâts/PA (vol de vie) à **12.25 dégâts/PA** grâce au poison de début de tour.

3. **Arbitrage Sorts de Vol de Vie vs Dégâts Purs :**
   - **Sinécure (Air, 2 PA) :** 6.50 vol/PA, excellent sort de faible coût pour achever ou se régénérer à 2 PA résiduels.
   - **Faisceau (Feu, 3 PA) :** Devient un vol de vie de zone (8.17/PA) dès qu'il franchit un portail.
