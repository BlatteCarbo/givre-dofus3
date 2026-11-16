# GivreClone — réservation fiscale HDV commune (13 novembre 2026)

## Défaillance de départ

Avant cette vague, la vente en masse réservait une estimation pour ses propres
lots, mais **les ventes manuelles et les repricings** comparaient seulement
`TotalTax` au plafond. Avec un plafond de 100 et un total observé de 90,
une nouvelle annonce dont la taxe potentielle est 20 pouvait être envoyée
avant toute mise à jour du compteur. De plus, deux opérations séparées
pouvaient chacune ignorer l'estimation réservée par l'autre.

Le montant réel de taxe n'est **pas attribué** à l'UID de l'ordre par un
reçu serveur. `KamasUpdateEvent` expose un solde, pas la causalité d'une
vente : une hausse de `TotalTax` ne permet pas de libérer un ordre précis.

## Correctif

- `GivreClone.App/Services/HdvTaxReservations.cs` introduit un registre
  thread-safe de sommes réservées par **PID + profil personnage scoped**.
  `TryReserve` combine `TotalTax`, les réservations antérieures et le
  prochain coût estimé via `HdvTaxBudget.CanReserve`, sans addition overflow.
  Les montants sont conservés même si la réponse d'envoi est indécidable.
  **Aucune API de libération** n'existe sans reçu causal.
- `HdvViewModel.cs` réserve sous `Gated` (sérialisation des mutations
  HDV) juste avant les appels potentiellement émetteurs :
  `HdvSellAsync`, `HdvRepriceAsync` manuel, sous-cote manuelle et
  `RunRepriceCycle` (watcher / « tout sous-coter »).
- `HdvViewModel.Bulk.cs` partage le **même** registre ; l'ancienne
  somme locale `projectedTaxSinceStart` est remplacée par une réservation
  au dernier point sous `Gated`, après vérification de l'UID et de la
  quantité du lot. Impossible d'envoyer l'ordre si la réservation échoue.
- Un taux fiscal invalide ou absent **refuse** l'opération plafonnée avant
  envoi. `MaxTaxSpend = 0` conserve la configuration historique illimitée.
- Le cumul affiché `TotalTax` sature désormais plutôt que de déborder
  lors d'updates extrêmes.
- `GivreClone.Tests/HdvSharedTaxReservationsTests.cs` couvre plafond
  franchi par la prochaine opération, identité PID/profil, impossibilité
  de libération par un delta global ambigu, valeurs invalides, limite
  `long.MaxValue`, configuration illimitée et concurrence.

## Garanties et limites

**Garantie locale au ViewModel en cours** : si un plafond strictement positif
est actif et que le taux est déterminable, toutes les mutations HDV de ce
ViewModel vérifient `observed + reserved + next <= cap` avant émission.
Une réponse d'ordre non confirmée ne supprime pas sa réservation.

**Limites non résolues** :

- Ce registre est **en mémoire seulement**. Une fermeture/redémarrage
  de l'application efface les réservations non attribuées ; il ne constitue
  pas une garantie financière inter-redémarrages.
- Le passage d'un plafond illimité à un plafond strict en cours de session
  ne reconstitue pas rétroactivement les dépenses sans reçu.
- Une opération peut être conservativement réservée alors que le serveur
  l'a finalement refusée (disponibilité réduite, préférable à un dépassement
  silencieux du plafond).
- Aucun compte de taxes attribuables à un ACK, aucun solde exact de jeu
  et aucune validation live ne sont revendiqués.
- Les autres chemins externes (scripts, autres vues ou clients) ne sont
  pas automatiquement soumis à ce registre ViewModel.

## Validation

Le build et les tests ciblés `HdvSharedTaxReservationsTests`,
`HdvTaxBudgetSafetyTests`, `HdvTaxBudgetTests`,
`HdvSellPreviewOverflowTests`, `HdvRealSalePriceOverflowTests`,
`HdvLiveUiTests` ont réussi : **74/74 PASS, exit 0**.
Journal `docs/codex/validation-20261113-hdv-shared-reservations-targeted.log`.

Après ajout du garde de provenance TCP, une nouvelle compilation ciblée
réunissant `HdvSharedTaxReservationsTests`, le binder et les régressions
TCP a réussi : **34/34 PASS, exit 0**.
Journal `docs/codex/validation-20261113-hdv-tcp-provenance-targeted.log`.
**Régression finale sur le dernier binaire compilé, séquentielle et hors jeu** :
`GivreClone.Tests` **5 870 PASS / 2 ignorés / 0 échec**,
`GivreClone.PureTests` **362/362 PASS** et
`CombatShadowComparison.Harness` **65 assertions PASS**.
Toutes les commandes ont retourné **exit 0**.
Journaux : `docs/codex/validation-20261113-hdv-tcp-provenance-{full-nonlive,puretests,shadow}.log`.
