# Métiers DofusDB — consultation hors ligne, 20 octobre 2026

**OWN worker-12 :** nouveaux `GivreClone.App/Services/DofusDbProfessions/*`,
`GivreClone.Tests/DofusDbProfessions*Tests.cs` et ce document.
Le prime raccorde `MainWindow` / `ProfessionAccessService` / projet de test.
Pas de commit, de push, de réseau, d'action client Dofus ni de runtime gameplay dans ce lot.

## Source et couverture mesurée

Cache existant :
`tools/dofusdb-partner-catalog/.cache/full-catalog`.
Fichier `review-summary.json` indiquant `status=REVIEW_ONLY_COMPLETE`,
`complete=true`, `sourceGameVersion=UNKNOWN`,
`aiMcpAllowed=false`, `gameplayCertified=false`.
La preuve est une **capture de l'API publique DofusDB via le collecteur partenaire**,
et non un jeu de données propriétaire certifié ni une attestation
indépendante de droits IA/MCP.

| Collection | Enregistrements annoncés | Pages annoncées |
| --- | ---: | ---: |
| `recipes` | 4 743 | 95 |
| `items` | 21 554 | 432 |
| `jobs` | 23 | 1 |
| **Total adapté** | **26 320** | **528** |

Les autres collections du cache restent hors de l'adaptateur métier.
Le lecteur contrôle les 528 fichiers de pages et 528 métadonnées en lecture
seule : taille, SHA256, `category`, `skip`, `limit=50`,
`total`, `records`, URL source attendue `https://api.dofusdb.fr/<collection>?$limit=50&$skip=<offset>`,
flags `versionStatus=UNKNOWN`, `runtimeCertified=false`,
`aiMcpAllowed=false`. Il rejette l'ensemble si une page manque,
est modifiée, contient des clés/quantités incohérentes, si les comptes
ne correspondent pas ou si le résumé n'annonce pas une capture complète.
Le `SourceProofSha256` est l'empreinte dérivée déterministe du SHA
du résumé et de l'ordre des SHA de chaque page, **pas une signature
cryptographique de DofusDB**.

**Validation réelle du lecteur C#** : le test
`Existing_complete_quarantined_capture_has_hash_validated_real_coverage_when_present`
a lu et vérifié localement les **528 pages réelles**, soit 4 743 recettes,
21 554 objets et 23 métiers, avec `AdmittedForLocalReview=true`,
`AllowedForAiOrMcp=false`, `CertifiedForGameRuntime=false`,
`SourceGameVersion=UNKNOWN`. Aucun fichier source du cache n'a été modifié.

Le schéma de la capture `recipes` contient `_id` (identité documentaire),
`resultId`, `resultLevel`, `jobId`, tableaux `ingredientIds` et
`quantities` de même longueur, `resultName.fr` facultatif. Le lecteur
ne déduit jamais de quantités produites (`resultQuantity` absent),
de XP par craft (`craftXpRatio` n'est pas de l'XP),
de seuils XP, de coûts HDV, de récoltes, de drops ou de chemins.
Les versions Dofus 3.7.11.11 et 3.7.12.12 ne sont pas implicitement
associées à ce cache.

## API de raccordement (signatures prime)

Namespace : `GivreClone.App.Services.DofusDbProfessions`.

```csharp
DofusDbProfessionReview review =
    DofusDbProfessionReader.LoadReview(cacheRoot);

bool readyForHumanLocalReview = review.AdmittedForLocalReview;
// SourceGameVersion toujours UNKNOWN ; AllowedForAiOrMcp toujours false.

IReadOnlyList<ProfessionCatalogRecipe> cards =
    DofusDbProfessionReader.ListRecipeCards(review, "Bijoutier", max: 200);

ProfessionPlannerSnapshot details =
    DofusDbProfessionReader.BuildReviewSnapshot(review, goal);
```

Le fichier `DofusDbProfessionReader.cs` est **pur** (sans Avalonia,
sans ViewModel) pour liaison dans le projet de tests. Il offre
`FindRecipesForJob(jobName, max)` sur `DofusDbProfessionReview`
retournant `DofusDbRecipeEvidence` avec IDs et ingrédients observés,
`ResultQuantity=null`, `ExperiencePerCraft=null`.
Les deux méthodes de projection UI sont dans
`DofusDbProfessionReader.Projection.cs`, déclaration `partial`
du même type, construisent exclusivement les modèles UI existants.

Pour l'interface, `ListRecipeCards` sélectionne le métier par nom
français exact et unique, affiche nom du résultat via `items` ou
`resultName.fr` quand présent, avec `IsVerified=false`,
rendement/XP/prix inconnus. `BuildReviewSnapshot` affiche les
ingrédients directs de la recette choisie, sous le niveau métier actuel
et, si `RequestedCrafts` est renseigné, calcule **uniquement** la
multiplication des ingrédients par ce nombre explicite de fabrications.
Le résultat fabriqué, le nombre de fabrications nécessaires pour 20→30,
la progression et l'approvisionnement restent inconnus. Aucune
fabrication/farm/achat n'est lancé ; toutes les propositions sont
consultatives.

## Build et tests

Tests synthétiques :
`GivreClone.Tests/DofusDbProfessionsAdapterTests.cs`, notamment
test de paires ingrédients exactes, rendement/XP inconnus, intégrité,
droits déclarés non admissibles, version déclarée fausse, mauvais
provenance URL, page absente, cache incomplet et quantités invalides.

Le projet `GivreClone.Tests.csproj` ne compile pas spontanément les
nouvelles sources ; le prime doit y ajouter la ligne suivante, **sur
le fichier du cœur pur uniquement** :

```xml
<Compile Include="..\GivreClone.App\Services\DofusDbProfessions\DofusDbProfessionReader.cs" Link="Linked\DofusDbProfessions\DofusDbProfessionReader.cs" />
```

Ne pas lier `DofusDbProfessionReader.Projection.cs` dans le projet de
tests (les vrais records UI vivent dans l'application Avalonia). La
projection est compilée automatiquement dans `GivreClone.App.csproj`.

**Résultat du contrôle ciblé Release :**
`dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore
--filter FullyQualifiedName~DofusDbProfessionsAdapterTests` :
**13 réussis / 0 échec**, dont la vérification réelle des 528 pages.
Un build `GivreClone.App` Release a réussi avec
**0 erreur** ; ses avertissements existants proviennent de l'application
partagée. La validation ne certifie pas de comportement gameplay.

Le droit déclaré au scraping du site est distinct d'une attestation
contractuelle de redistribution dans IA/MCP. Le cache conserve
`aiMcpAllowed=false` : **aucun passage MCP / modèle IA** par ces
méthodes. Si un reçu partenaire signé indépendamment est fourni, une
révision des contrôles reste nécessaire ; ne jamais transformer
`sourceGameVersion=UNKNOWN` en version certifiée.
