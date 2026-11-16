# GivreClone — protection des estimations HDV contre les débordements

Date : 13 novembre 2026. Dépôt : `fix-f01-f16-final-20261024` (base observée `b4d28a8`).

## Domaine modifié

- `GivreClone.App/Services/HdvSellPreviewMath.cs` : calculs purs d'estimation.
- `GivreClone.App/ViewModels/HdvViewModel.Bulk.cs` : valeur par ligne, agrégation des quantités, simulation des lots, avertissement du plafond et cumuls affichés après vente.
- `GivreClone.App/Lang/{fr,en,es}.axaml` : message explicite lorsque le taux de taxe est inconnu.
- `GivreClone.Tests/HdvSellPreviewOverflowTests.cs` : cas normaux, extrêmes, valeurs négatives, tri décroissant, taux non finis et plafond.

## Problème et correctif

Auparavant, `AvgUnit * Qty`, `a.Qty + it.Qty`, les produits puis `Sum` de la prévisualisation et `TotalTax + est` pouvaient déborder. Une conversion double→long de la projection de taxe ou de la limite inverse pouvait également produire une valeur erronée. Le calcul prévisionnel remplaçait arbitrairement tout taux `<= 0` par 2 %, sans refléter la politique **fail-closed** de la vraie vente plafonnée.

La nouvelle arithmétique :

1. Annule les prix moyens et quantités négatifs dans les estimations, refuse les piles de quantité non positive et sature les quantités agrégées à `int.MaxValue`.
2. Sature les produits positifs `prix moyen × quantité` et les cumuls d'estimation à `long.MaxValue` sans jamais en faire un ordre HDV.
3. Calcule l'avertissement taxe avec `HdvTaxBudget.EstimateUpperBound` et une comparaison soustractive contre le solde du plafond (pas `TotalTax + est`). Elle fait apparaître un message « taux inconnu » pour les taux nuls, négatifs, non finis et supérieurs à 100 %.
4. Borne la conversion du montant indicatif maximal de lots, sans arrondi double→long hors plage.
5. Sature les totaux de mises en vente **affichés**, sans modifier `lot.Price` ni la transmission de l'ordre.

## Garantie et périmètre exclu

- Le moteur de vente, les taxes réellement réservées par lot avant envoi, les reçus et `HdvSellAsync` ne sont **pas modifiés**.
- `PreviewValue == long.MaxValue` veut dire « montant affiché plafonné à la plage de représentation », **pas** un cours réel ni un prix autorisé.
- Le montant maximal du hint est une approximation locale ; seul le garde d'envoi avec taux serveur confirme si une vente est admissible.
- Le cumul de quantités saturé n'autorise pas à envoyer une quantité virtuelle : chaque lot réel est relu sur les piles fraîches, vérifié par UID et quantité dans le moteur. La somme est seulement prévisionnelle.
- `SanityFloor` et `LotPrice` utilisent d'autres calculs pouvant produire un **vrai prix**. Leur changement nécessite une correction **séparée, fail-closed**, et des tests couvrant les appels manuels/repricing ; ne jamais les saturer puis les envoyer.
- Aucune preuve serveur ou jeu live ne découle de ces tests hors jeu.

## Validation

La nouvelle suite `HdvSellPreviewOverflowTests` et les suites `HdvTaxBudgetSafetyTests`, `HdvTaxBudgetTests`, `QaHdvTradeBankTests` sont ciblées avec `--no-restore -m:1 -p:UseSharedCompilation=false` ; enregistrer ici le résultat final exact après exécution.

**Validation ciblée réellement exécutée : 40/40 PASS, zéro ignoré, zéro échec, exit 0**.
Journal : `docs/codex/validation-20261113-hdv-preview-targeted.log`.
La compilation des nouveaux fichiers et les ressources Avalonia a réussi ; des avertissements préexistants Avalonia/xUnit sont encore émis.

**Régressions terminées** (toutes `exit 0`, hors jeu, séquentielles, `--no-build`) :

- `GivreClone.Tests`, filtre `FullyQualifiedName!~Live` : **5 830 PASS / 2 ignorés / 0 échec**, 5 832 cas, journal `docs/codex/validation-20261113-hdv-preview-full-nonlive.log`.
- `GivreClone.PureTests` : **362/362 PASS**, journal `docs/codex/validation-20261113-hdv-preview-puretests.log`.
- `CombatShadowComparison.Harness` : **65 assertions PASS**, journal `docs/codex/validation-20261113-hdv-preview-shadow.log`.

Ces nombres incluent 15 cas ajoutés par `HdvSellPreviewOverflowTests` depuis le précédent référentiel 5 815. **Attention** : les changements `ClassMechanics.cs` intervenus indépendamment **après** cette compilation n'y figurent pas encore et nécessitent leur propre build et vérification avant toute revendication supplémentaire.
