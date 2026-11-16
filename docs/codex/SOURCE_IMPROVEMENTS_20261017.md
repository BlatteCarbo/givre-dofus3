# GivreWork : améliorations des sources — 17 octobre 2026

La consigne active est de travailler exclusivement sur les sources de GivreWork. Aucune décompilation, lecture mémoire, connexion MCP à MizanBot ou exécution de l'application n'a été effectuée après cette consigne. Les modifications HDV déjà présentes dans le dépôt sont conservées.

## Ajouts

- `print`, `printSuccess`, `printWarning`, `printError` affichent tous leurs arguments, séparés par une tabulation, y compris les booléens et les nil finaux. Les alias existants des modules reprennent ces callbacks. La signature `printMessage(message, niveau)` reste distincte.
- `craft:plan(objet, quantité)` accepte un identifiant ou un nom, déduit le sac et la banque connus du moteur, ordonne les sous-crafts avant le résultat et retourne les matières manquantes, leur provenance disponible, les quantités déjà possédées et les niveaux de métier. Le même stock n'est jamais utilisé deux fois dans le plan.
- Les cycles, graphes excessifs et dépassements numériques échouent explicitement. Le plan ne lance ni achat ni fabrication.

Le plan de craft n'est pas annoncé comme une reproduction intégrale du contrat historique : les champs de compétence `skill` et l'estimation `buyMissingAtAuctionHouse` ne sont pas fournis. Les provenances reprennent les données disponibles dans le module existant. Le contenu bancaire reste celui du dernier état connu du moteur.

## Vérification

63 tests ciblés réussis, zéro échec et zéro ignoré : LuaPrintTests, CraftPlannerTests, GameDbTests et LotMLuaModuleTests. Ils exécutent réellement MoonSharp et SQLite ; ils ne se connectent pas au jeu.

Compilation Release réussie : zéro erreur, 173 avertissements. Journal : `artifacts/source-improvements-build.log`. Tests : `GivreClone.Tests/TestResults/source-improvements.trx` et `artifacts/source-improvements-tests.log`.

Commande de test :

```sh
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --filter 'FullyQualifiedName~CraftPlannerTests|FullyQualifiedName~LuaPrintTests|FullyQualifiedName~GameDbTests|FullyQualifiedName~LotMLuaModuleTests' --artifacts-path artifacts/SourceImprovements
```

La compilation isolée est dans `artifacts/SourceImprovements/bin/GivreClone.App/release/`. Elle n'a pas été lancée ni installée. La suite entière et les actions en jeu n'ont pas été testées dans ce lot. Aucune nouvelle fonctionnalité provenant de GivreRework n'est présentée comme validée.
