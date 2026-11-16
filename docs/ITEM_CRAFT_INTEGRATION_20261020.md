# Atelier universel — objet unique, panoplie entière ou liste libre

Intégration GivreWork du **20 octobre 2026** ; exemple demandé :
« fabriquer toute la Panoplie Bouftou, farmer les ressources, créer
les sous-crafts et déduire l'inventaire, la banque et le Havre-Sac ».

Le nouveau panneau Avalonia premium **Atelier de fabrication** est
accessible depuis la barre de titre, la navigation et Ctrl+K.
Le Coach métiers reste indépendant : il traite la progression de niveau ;
l'Atelier cible des **objets réels ou un ensemble**.

## Workflow fonctionnel hors jeu

1. Modes **Un objet**, **Panoplie**, **Liste libre**.
2. Recherche dans le **catalogue local manifesté et SHA-256 contrôlé**,
   sélecteur natif de dossier. Recherche sur toute la source; seules
   les entrées filtrées (jusqu'à la limite de la vue) sont affichées,
   et une panoplie n'est proposée que si toutes ses pièces peuvent
   également être montrées. Un utilisateur peut préciser son texte.
3. Choix de pièce/set et quantité, ajout à la liste. En mode panoplie,
   le catalogue doit apporter les IDs de chaque équipement : aucune
   reconnaissance implicite par mot « Bouftou ».
4. `ItemCraftPlanner.PlanList` regroupe les cibles, explose récursivement
   les ingrédients, calcule les rendements et les surplus de sous-crafts,
   utilise un **stock global** sur toutes les pièces et signale
   les recettes inconnues, cycles, niveaux métiers et ateliers non attestés.
5. Choix consultatif Craft/Farm/Drop/Acheter si une source alternative
   observable est présente ; prix/temps sont des UNKNOWN sans données
   comparables, aucune destination mapId n'est inventée. La simulation
   n'exécute aucun mouvement, achat ou combat.
6. Trois emplacements de stock ont un modèle séparé. **Seuls le sac
   et la banque ont des snapshots décodés attestés dans le code actuel**.
   Havre-Sac = contenu UNKNOWN ; voir `UNIFIED_STOCK_SOURCES_20261020.md`.

Les cartes DofusDB/étages/grottes restent dans WorldAtlas consultatif :
leur présence sans une arête prouvée ne fournit pas un script Mineur.

## Données et provenance

`EquipmentSetCatalogReader.LoadDirectory` exige un fichier
`equipment-sets.json`, `manifest.json`, la longueur, SHA256,
gameVersion, permission et origine. `ProfessionCatalogReader`
exige séparément `recipes.json` ou `dofus.sqlite` accompagné de son
propre manifeste. L'Atelier prend un dossier parent `sets/` et
`recipes/` (ou un dossier conforme à l'un des assets), jamais un
fichier arbitraire du répertoire téléversé au projet.

Un hash local certifie **l'intégrité des octets au moment de leur lecture**
mais pas l'éditeur ni la compatibilité serveur 3.7.12.12. Le vrai
`dofus.sqlite` local a été inspecté pour les schémas de
`ItemSetData.items` et `ItemData.itemSetId` (929 ensembles)
mais **aucun jeu local complet avec droit d'usage, version et manifest
admis n'est livré/activé par défaut**. Aucun inventaire de pièces
Panoplie Bouftou n'a été inventé.

Le schéma de recettes inspecté ne fournit pas systématiquement
`resultQuantity` ni `XP par craft`, donc ces recettes demeurent
UNKNOWN jusqu'à une source adaptée. Le moteur parcourt un sous-graphe
pertinent depuis les cibles plutôt que tronquer arbitrairement
les premières recettes de toute la base.

## MCP

Outil ajouté `query_item_craft`, 55e outil, annoté read-only.
`requestJson` accepté par `ItemCraftMcpFacade` :

```json
{"op":"list_targets","kind":"set","query":"Bouftou","limit":20}
```

```json
{"op":"plan_set","setId":1234,"quantity":1}
```

`1234` est un identifiant **fictif de schéma**, pas le setId
attesté de la Panoplie Bouftou. Le MCP propose aussi
`capabilities, plan_item, materials, steps`, en conservant
les champs de preuve, l'absence de route certifiée et
`executed=false/acknowledged=false`.

La diffusion exige `GIVRE_ITEM_AI_APPROVED_SETS_SHA256`,
`GIVRE_ITEM_AI_APPROVED_RECIPES_SHA256`, `GIVRE_ITEM_AI_AUTH_ID`,
un catalogue local `local-extraction` dûment revu, versions concordantes
et MCP activé. **Une variable d'environnement n'est pas une preuve
juridique de droits** : l'admission doit refléter un accord réel.
Les données DofusDB publiques et exports partenaires non spécifiquement
autorisés pour IA restent hors MCP.

## DofusDB / CloakBrowser

Un collecteur supplémentaire est disponible sous
`tools/dofusdb-partner-catalog/collector.py`.
`crawl-site` inspecte uniquement des pages HTML HTTPS dans une
allowlist, s'arrête sur robots/403/CAPTCHA, fait du checkpoint SHA256 ;
`review-partner-export` audite un export partenaire explicitement
fourni par l'opérateur sans l'activer en IA. 6 tests offline PASS.

Le test `crawl-site --pages 1` a terminé **BLOCKED_ROBOTS,
zéro page collectée**. CloakBrowser n'a pas été trouvé sur le poste
connecté et aucun contournement furtif de robots/reCAPTCHA n'a été
tenté. Le partenariat revendiqué permet de développer la voie
d'import, mais aucune exception contractuelle d'usage IA/MCP
n'est encore prouvée.

**Limitations :** pas de base DofusDB complète importée, pas de
vraie carte d'entrée de grotte certifiée, pas de taux de drop,
pas de retrait de banque, pas de coffre HavreSac indépendant
observé, pas de scénario 100 % automatique exécuté en jeu,
pas d'ACK serveur certifié. Les démos visuelles Bouftou sont
clairement **FICTIVES**.

## Vérification

Compiler via `dotnet build GivreClone.slnx -c Release -m:1 --no-restore`,
tester `GivreClone.Tests` et `GivreClone.PureTests`,
exécuter `GivreMcp/Smoke.ps1` et les deux suites Python.
La CI GitHub doit être observée séparément pour le commit poussé.
