# Atelier universel : recettes locales UUID et rendements inconnus (20/10/2026)

## Bug constaté et correction

L'export local `dofus.sqlite` lu en lecture seule expose `RecipeData.id`
comme UUID, et non comme entier. La table fournit les liens d'ingrédients,
leurs quantités par fabrication et `resultLevel`, mais **aucune quantité
d'objet produite par fabrication**. L'ancienne sélection de recettes dans
`ItemCraftAccessService.BuildPlan` exigeait à la fois un `Id` numérique et
`ResultQuantity > 0`, ce qui écartait toutes ces recettes locales du plan.

La correction accepte les recettes ayant des ingrédients connus, même avec
`ResultQuantity = null`, et assigne des **clés numériques éphémères de graphe**
aux recettes de la fermeture de dépendances. Ces clés ne sont pas des IDs
officiels Dofus et ne doivent pas être enregistrées comme tels. Le tri stable
par identifiant source empêche les collisions à l'intérieur du plan borné.

Le moteur `ItemCraftPlanner` affiche désormais une étape consultative de
fabrication au **rendement inconnu**, avec les ingrédients observés et leurs
quantités **par fabrication**, mais avec besoins totaux `UNKNOWN` :

- Aucun nombre de fabrications n'est déduit d'un rendement absent.
- Aucune multiplication d'ingrédients par une quantité de fabrication supposée.
- Aucun stock d'ingrédient n'est réservé/consommé pour cette étape indécidable.
- Aucune acquisition Farm/Drop/Achat n'est déclenchée par cette estimation.
- Aucune fabrication/surplus n'est crédité dans un inventaire projeté.
- `Missing[itemId]` du résultat demandé garde la quantité non satisfaite et
  `Missing[ingredientId] = null` pour les besoins non quantifiables.
- `IsComplete`, `Executed` et `Acknowledged` restent faux tant qu'une source
  complète ne justifie pas la planification exacte et ses preuves en jeu.

Ce changement rend le **détail des recettes locales** consultable, pas les
quantités globales exploitables pour un farm autonome.

## Preuves de schéma (lecture seule)

Au 20 octobre 2026, le fichier local de 380 866 560 octets contient :

| Source | Enregistrements | Limite métier |
| --- | ---: | --- |
| `RecipeData` | 4 858 | UUID et rendement non fourni |
| `RecipeData_ingredientIds_junction` | 24 532 | Liens ingrédients |
| `ItemData` | 20 979 | `itemSetId` n'est pas un manifeste de droits |
| `ItemSetData` | 929 | Composition de panoplie présente localement |

Le fichier brut demeure **hors du dépôt**. Son contenu, sa version exacte et
ses droits d'utilisation ne sont pas établis par la seule lecture de schéma.
Les exporteurs versionnés et les manifestes autorisés sont des travaux
séparés ; aucun téléchargement DofusDB n'est promu par cette modification.

## Validation

`dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release -m:1 --no-restore --filter 'FullyQualifiedName~ItemCraftPlannerEngineTests' -v:q`

La fixture synthétique « rendement inconnu » vérifie que l'ingrédient par
craft est visible, que les quantités totales restent `null`, et qu'aucune
récolte, drop, achat ou consommation de stock n'est inventée.

Les clés numériques de graphe et les informations `UNKNOWN` sont des
fonctionnalités consultatives hors ligne. Aucun ACK, farm, craft, stockage
Havre-Sac ou compatibilité runtime n'est attesté par ces tests.
