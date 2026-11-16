# GivreClone — garde des vrais prix HDV contre les dépassements (13 novembre 2026)

## Contexte

Après la sécurisation des **estimations affichées**, un audit lecture seule a
identifié trois chemins distincts de **prix effectivement proposés** susceptibles
de convertir une valeur `double` hors plage en `long` :

- `HdvViewModel.Bulk.cs`, `SanityFloor` : `Math.Ceiling(avg * lotSize * 0.5)`.
- `HdvViewModel.Bulk.cs`, `LotPrice` dérivé : `Math.Ceiling(u * lotSize)`.
- `HdvRepricePolicy.Target` : `Math.Floor(market * factor)`.

En présence de très grandes valeurs, les conversions hors plage pouvaient créer
une valeur de retour artificiellement petite et déboucher sur une vente ou un
repricing à mauvais prix. Une valeur saturée à `long.MaxValue` est acceptable
pour une **prévisualisation** mais **jamais** comme prix réel à envoyer.

## Changement

- Nouveau `GivreClone.App/Services/HdvSalePriceMath.cs` : calculs `decimal`
  stricts `HalfAverageFloor`, `DerivedPrice` et `MarketPercentageFloor`.
  Une valeur invalide ou non représentable renvoie `null` (pas de prix).
- `SanityFloor` convertit `null` en sentinelle `-1`. `LotPrice`, `LotsFor`,
  `ComputeTarget`, `ApplyAutoPrice`, `SellSelected` et `SellSelectedCore`
  **refusent** l'ordre quand ce plancher n'est pas calculable. Le calcul dérivé
  utilise maintenant les prix unitaires en `decimal` et refuse les dépassements.
- `HdvRepricePolicy.Target` calcule sa limite de sous-cote sans conversion
  `double → long` hors domaine ; les paramètres non finis invalident la décision.
- `HdvSalePriceGuard` refuse un plafond de référence dont le produit déborde
  `decimal` ; `LaunchService.HdvPriceGuard` refuse un taux de taxe HDV
  non fini avant conversion.
- Nouveau `GivreClone.Tests/HdvRealSalePriceOverflowTests.cs` : extrêmes
  `long.MaxValue`, valeurs invalides, précision au-delà de 2^53, taux non finis,
  prix de marché maximal et garde de prix de référence.
- `GivreClone.Tests/GivreClone.Tests.csproj` lie explicitement
  `HdvSalePriceMath.cs` aux sources pures déjà liées de
  `HdvRepricePolicy.cs` : ces deux fichiers doivent être compilés
  ensemble dans le projet de tests.

L'ordre n'est **jamais** remplacé par un montant saturé : sur incertitude,
la vente concernée est ignorée. Les quantités réelles, la corrélation UID
et la réservation fiscale par ordre du moteur Bulk restent inchangées.

## Limites et travaux distincts

- Le garde final de vente a toujours son propre contrat d'autorité UID/lot,
  frais et âge de marché. Il n'existe pas de garantie de prix plancher
  indépendante applicable à **toute** saisie humaine volontaire ; ne pas
  modifier une saisie manuelle par inférence.
- `HdvSellCorrelation` prouve l'apparition d'un lot, pas un reçu chiffré
  de taxe. Les deltas de solde KamAS ne portent pas l'UID de l'ordre.
  Les réservations Bulk doivent rester conservatrices tant que la
  causalité des dépenses n'est pas prouvée.
- Le plafond de taxe global en vente manuelle et en repricing demeure à
  auditer : une comparaison avec `TotalTax` seule ne réserve pas le
  coût prévisionnel de l'ordre suivant.
- Aucune validation live, ni performance ou bénéfice économique prétendus.

## Validation

Les tests ciblés `HdvRealSalePriceOverflowTests`,
`HdvRepricePolicyTests`, `HdvLiveUiTests`,
`HdvSellPreviewOverflowTests`, `CombatComboObservationsIngressTests`
et `DofusCaptureHostStartStopRaceTests` ont été exécutés dans une
compilation dédiée avec `--no-restore -m:1 -p:UseSharedCompilation=false`.

Première compilation : **exit 1**, erreur `CS0103` dans
`HdvRepricePolicy.cs` parce que son nouveau helper manquait dans
les sources `Compile Include` liées du projet tests. Aucun test
validé pendant cette tentative. **Correction :** ajout du lien
explicite dans `GivreClone.Tests.csproj`, puis compilation de nouveau.

**Validation ciblée finale : 82/82 PASS, zéro ignoré, zéro échec,
exit 0** ; journal
`docs/codex/validation-20261113-hdv-real-prices-targeted-final.log`.

**Régression de ce checkpoint (avant le garde de fermeture SessionBinder)** :

- `GivreClone.Tests`, filtre `FullyQualifiedName!~Live` :
  **5 858 PASS / 2 ignorés / 0 échec**, total 5 860, exit 0,
  `docs/codex/validation-20261113-hdv-real-prices-full-nonlive.log`.
- `GivreClone.PureTests` : **362/362 PASS**, exit 0,
  `docs/codex/validation-20261113-hdv-real-prices-puretests.log`.
- `CombatShadowComparison.Harness` : **65 assertions PASS**,
  exit 0, `docs/codex/validation-20261113-hdv-real-prices-shadow.log`.

Les changements `SessionBinder` ont été effectués **après** cette
compilation et reçoivent une validation indépendante. Ne pas ajouter
les quatre nouveaux tests SessionBinder au décompte de ce checkpoint.
