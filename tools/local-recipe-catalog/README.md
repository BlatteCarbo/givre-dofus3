# Export local de recettes Dofus (consultation)

Python 3.10+ standard library. Aucune connexion réseau, client jeu, mise à
jour de `dofus.sqlite`, fabrication, farming ou interaction gameplay.

Depuis `%USERPROFILE%\Videos\GIVRE\GivreWork-Integration-20261020` :

```powershell
python -m unittest discover -s tools/local-recipe-catalog -p test_export_recipes.py -v

python tools/local-recipe-catalog/export_recipes.py inspect `
  --sqlite '%USERPROFILE%\Videos\GIVRE\dofus.sqlite' `
  --expected-sha256 599908fe9396cde1800390497ca703949576b53acbd261271d103001bda9b199

python tools/local-recipe-catalog/export_recipes.py preview `
  --sqlite '%USERPROFILE%\Videos\GIVRE\dofus.sqlite' `
  --expected-sha256 599908fe9396cde1800390497ca703949576b53acbd261271d103001bda9b199 `
  --output-dir tools/local-recipe-catalog/.cache/nouveau-review
```

`preview` écrit `recipes.json` réel et `manifest.review.json` dans un cache
ignoré par Git. **La version est UNKNOWN et le dossier ne contient pas
`manifest.json`.** Le lecteur `ProfessionCatalogReader.LoadDirectory` ne
peut donc pas l'admettre automatiquement. L'aperçu du 20/10/2026 est déjà
présent sous `.cache/real-20261020-v2/` (4858 recettes).

Pour générer un `manifest.json` **compatible** avec ce lecteur, l'opérateur
doit d'abord vérifier et attester lui-même l'origine, les droits sur le
fichier, la convention d'ordre des IDs dans la table de liaison, et déclarer
la version du jeu. La version ci-dessous est **une valeur d'exemple et ne
constitue pas une version établie pour ce fichier** :

```powershell
python tools/local-recipe-catalog/export_recipes.py export `
  --sqlite '%USERPROFILE%\Videos\GIVRE\dofus.sqlite' `
  --expected-sha256 599908fe9396cde1800390497ca703949576b53acbd261271d103001bda9b199 `
  --output-dir tools/local-recipe-catalog/.cache/authorized-vetted-export `
  --game-version 3.7.12.12 `
  --attest-source-owned `
  --attest-junction-rowid-order `
  --acknowledge-version-unverified
```

**Ne lancer cette dernière commande qu'après les vérifications indiquées.**
La présence de `owner-authorized` dans le manifeste signifie uniquement
qu'un opérateur a fourni le drapeau d'attestation. Elle ne constitue
pas une preuve indépendante de licence, de publication, de version cliente
ou de compatibilité en jeu. Aucun export n'est autorisé pour IA/MCP par ce
script ; un autre accord éventuel doit être examiné séparément.

Le format de sortie `recipes.json` est strictement :

```json
{
  "schemaVersion": 1,
  "items": [{"id": 1, "name": null, "level": null}],
  "jobs": [{"id": 16, "name": "Bijoutier"}],
  "recipes": [{
    "id": "example-synthetic-uuid",
    "jobId": 16,
    "resultItemId": 1,
    "requiredLevel": 20,
    "resultQuantity": null,
    "experiencePerCraft": null,
    "ingredients": [{"itemId": 2, "quantity": 3}]
  }],
  "levelThresholds": []
}
```

**Exemple fictif de contrat**, sans rapport avec une recette réelle. Le
format n'ajoute aucun champ que le lecteur C# ne comprend pas. Les identités
de recettes source sont des UUID textuels, jamais forcées en entier. Les
ingrédients sont reliés aux quantités par `ORDER BY rowid` comme le lecteur
SQLite existant, mais le schéma SQLite ne stocke pas d'ordinal métier :
**cette correspondance repose sur une hypothèse** qui exige l'attestation
explicite `--attest-junction-rowid-order`. Si cette hypothèse ne peut être
vérifiée auprès du producteur de la base, seul `preview` convient.

L'XP par craft, le nombre d'objets produits et les seuils de niveaux sont
tous laissés UNKNOWN (`null` ou tableau vide). `resultLevel` est traité
comme niveau requis, jamais comme XP. Les objets manquants dans `ItemData`
restent déclarés avec identifiant, niveau null et, uniquement si la table
`RecipeData` contient une traduction explicite `resultNameId`, son nom
attesté par cette relation. Aucune supposition de drop, de ferme ou de
nécessité de kamas.

Document d'audit : `docs/LOCAL_RECIPE_EXPORT_20261020.md`.
