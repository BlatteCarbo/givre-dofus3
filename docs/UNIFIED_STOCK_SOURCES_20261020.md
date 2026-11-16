# Stocks unifiés — sac, banque, Havre-Sac (20 octobre 2026)

## Besoin

L'objectif « fabriquer un objet / une panoplie entière » doit soustraire de
la liste des ressources à farmer ce que possède réellement le personnage :
sac et banque, et stockage du Havre-Sac **uniquement lorsqu'il existe un
snapshot complet et attribué**. Ne jamais transformer une absence
d'observation en quantité zéro.

## Évidence effective dans GivreWork

- **Sac** : `LaunchService.Inventory : InventoryStore` est alimenté par
  `InventoryContentEvent` puis ObjectAdded/Quantity/Deleted
  (éventuellement lot). Stacks `(UID,GID,Qty,Position)` par PID. Une copie
  est accessible via `Inventory.Items(pid)`. Objets équipés (position  != 63)
  ne sont pas considérés libres à consommer pour une recette.
- **Banque** : `LaunchService.Bank : InventoryStore`, snapshot complet
  `StorageInventoryContentEvent` à l'ouverture, suivi des événements
  `StorageObjectUpdateEvent/RemovedEvent`. `StorageOpen(pid)`
  vérifie la session du coffre sur la carte courante.
- **Coffres de maison à code** : leurs événements Storage sont traités
  séparément dans `LaunchService.Packets.cs`, sans écraser la banque.
- **Havre-Sac** : le code de navigation/zaap et `HavenBagEnterRequest`
  existent, mais **aucun lecteur de contenu de stockage distinct n'a été
  établi**. `HavenBag` reste donc UNKNOWN, pas zéro, jamais déduit.
- **HDV** : `HdvMergedItems(pid)` fusionne sac et banque en favorisant
  le sac pour un UID présent dans les deux. Ce comportement a été repris
  dans le calcul de stocks unifiés.
- **Identité** : `CharacterId(pid)` est vérifié; à la sélection d'un
  autre personnage sur le même PID, les stores principaux sont vidés.

## Modules livrés

- `UnifiedStockPlanner.Allocate(accountKey, characterId, gameVersion, now,
  observations, required, maxAge?)` : moteur **pur** qui alloue les
  quantités entre emplacements, déduplique par StorageIdentity partagé
  et UID d'instance, rejette vieux/incomplets/autre compte/autre perso/
  autre version, piles réservées. Les quantités non vues restent UNKNOWN
  tandis que le manque calculé est provisoire.
- `LiveStockEvidence.Attach(LaunchService)` : observe les paquets
  déjà décodés **sans envoyer de requête**, retient dernier événement
  complet sac/banque par PID+characterId, horodatage et durée de
  fraîcheur (5 minutes par défaut). `Read(...)` livre seulement
  les observations valides, ne crée jamais de ligne HavreSac.
- `ItemCraftAccessService.BuildPlan` : peut déduire ces lignes depuis
  le bot sélectionné après vérification de sa version locale GameData,
  de son identité et d'un snapshot récent ; crée des `ItemStockStack`
  vérifiés pour le graphe objet/panoplie.

La version `LotMGameVersion` provient des données GameData locales, **pas
d'une authentification au serveur live**. Quand elle manque ou diverge,
l'inventaire observé n'est pas réutilisé par cet adaptateur.
Le stock saisi manuellement dans l'UI reste consultatif et
`Verified=false`; il ne se substitue pas à la lecture serveur.

Les puits de stocks sont indépendants des commandes Lua de retrait/dépôt.
Aucune manipulation d'objets, ouverture de banque, entrée de Havre-Sac,
action HDV ou activité Dofus n'est lancée par ces modules.

## Cas négatifs testés

Les tests `UnifiedStockPlannerTests` couvrent : allocation partagée
sur 3 emplacements fictifs, double alias Banque/HavreSac,
UID dupliqué sac/banque, stock réservé, snapshot périmé/partiel,
personnage ou version divergente, alias contradictoire, manque UNKNOWN.
La présence d'une ligne HavreSac dans un **test synthétique** ne prouve
en rien une intégration serveur réelle pour ce coffre.

## Blocages de l'automatisation totale

Confirmation en jeu de l'accès/temps de lecture de banque, source réelle
distincte de stockage HavreSac si pertinente, attribution précise
par personnage/compte, prix HDV observés, ressources disponibles et
trajets grottes/zaaps vérifiés, receipts de farm/craft et reprise
idempotente. Ces prérequis ne sont pas simulés comme « OK ».
