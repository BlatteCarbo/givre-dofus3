# GivreWork — Round 4, UID HDV et diagnostic local

Round 4, base : `d661531`. Analyse et tests hors ligne ; aucun achat ou vente réel.

## Preuves et contrats

- Achat : `MZP26HdvBuyReceipt` existait déjà. Un `ExchangeBidHouseBuyResultEvent` n'est accepté que si son `object_uid` égale l'UID envoyé et `bought=true`. `bought=false`, même par omission proto3, est un refus.
- Vente : le `object_uid` de `ExchangeObjectMovePricedRequest` désigne le **stack du sac**. Le `BidItem.uid` de `ExchangeBidHouseItemAddedEvent.item` désigne le **lot du vendeur**. Les deux champs ne sont pas déclarés identiques. Le store temporaire `(PID, UID sac) -> UID lot` est créé uniquement après une paire d'effets serveur : (a) suppression du stack ou changement de quantité exactement attendu pour l'UID demandé et (b) nouvelle annonce avec UID inédit, GID, quantité et prix égaux à la requête. Plusieurs nouveaux lots candidats refusent la corrélation. Le store est purgé aux retraits, à la fermeture, à l'oubli du bot et à la perte de session.
- Retrait : `ExchangeBidHouseInListRemovedEvent.object_uid` doit égaler l'UID EXCHANGE demandé. `ExchangeBidHouseItemRemovedEvent.sell_id` ne peut confirmer que si cet identifiant est présent dans le store de corrélation issu de la vente. Un autre identifiant est ignoré. Erreurs explicites : `Refused` ; silence, ambiguïté et délais : `NeedsReview` (résultat booléen `false`). Les actions vendeur (vente/retrait/reprix) sont sérialisées par PID ; après timeout d'un retrait potentiellement envoyé, un nouveau retrait est bloqué jusqu'au contrôle manuel des annonces dans l'UI HDV (commande `ReconcileRemove`). L'incertitude n'est jamais convertie en `Confirmed` par une simple déclaration manuelle.
- Champs résolus via `data/fields.json` au runtime, sans anciens numéros figés.

**Limites des preuves :** aucune correspondance directe « UID sac ↔ sell_id » n'est renvoyée dans un seul message, et aucun nonce d'opération n'est observé. La corrélation de vente repose sur deux effets concordants pendant une opération sérialisée du moteur. Une action manuelle strictement concurrente, de même GID, lot et prix, ne peut pas être exclue par ce seul schéma : le caractère causal doit encore être validé avec les captures d'une session TEST et l'exclusivité des actions sur le PID. Aucun ACK de production ou résultat d'une transaction réelle n'a été attesté. Une mise en vente non résolue bloque la prochaine tentative automatique jusqu'à revue manuelle.

## Outils opérateur

- Le panneau opérateur existant affiche à la demande : client/version **non certifiés** (admission R3 non levée), présence du catalogue candidat, accès SQLite GameDb, disponibilité des bundles de cartes et nombre de cartes **déclaré dans le manifeste local** (sans le promouvoir au statut de cache vérifié), initialisation du journal transport et absence d'ACK certifié. L'affichage des preuves de messages / ACK reste dans les cartes distinctes du panneau MZP0, liées à une autorité indépendante.
- Inspecteur de prix HDV : PID, serveur, GID, tailles des lots observés, prix min et source cache local, horodatage estimé depuis l'âge du cache, fraîcheur, prix moyen et origine. Aucune vérification réseau ou disponibilité en temps réel.
- `GameDb.Scan` parcourt désormais toutes les lignes avec `LIMIT`/`OFFSET` par tranches de 1 024, ordonnées par `rowid` sur les tables SQLite ordinaires ; 5 000 limite les **résultats** et non l'espace recherché. `FindPage` / `SearchTextPage` paginent les *correspondances* sur la table entière (page 0 initiale). Même si `pageSize > 5000`, les pages se suivent sans trou. Ce mécanisme suppose un jeu SQLite local stable pendant la pagination et une table disposant de `rowid` ; une vue ou table SQLite `WITHOUT ROWID` ne satisfait pas encore ce contrat.

## Validation

- `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GameDbTests|FullyQualifiedName~HdvSellEvidenceTests|FullyQualifiedName~MZP26HdvBuyReceiptTests" -v:q -p:RunAnalyzers=false -clp:ErrorsOnly`
- `dotnet build GivreClone.App/GivreClone.App.csproj -c Debug -clp:ErrorsOnly -p:RunAnalyzers=false`

Les tests SQLite utilisent exclusivement une base synthétique temporaire avec plus de 5 300 lignes, dont un objet postérieur au seuil précédent. Le test ne modifie pas la base Dofus. Aucune preuve de wire live n'est générée par le build.

Dernière validation ciblée élargie (HDV, GameDb, preuve d'achat) : **96 tests réussis, 0 échec**. Compilation Debug de l'App : **0 erreur**, avertissements existants non traités par ce round.
