# Marché & kamas — historique HDV visible dans GivreWork (20/10/2026)

## Demande utilisateur

Captures officielles Dofus : `Cours du marché` (24 heures / 7 jours / 30 jours,
prix médian, prix moyen, articles vendus), Bourse Kamas/Ogrines, boutique et
cadeaux/récompenses. L'utilisateur veut consulter ces données dans GivreWork
pour identifier des opportunités de gagner des kamas, sans interfaces inutiles.

## Ce qui est réellement raccordé

- Un écran compact **Marché & kamas** accessible depuis la navigation et le
  menu `Outils`, sans nouveau protocole ou back-end concurrent.
- Le lecteur EXISTANT `LotMReader.PriceHistory` décode
  `ObjectAveragePricesHistoryEvent` en date, prix moyen, articles vendus.
- Le service EXISTANT `LaunchService.PriceHistoryAsync(pid,gid)` ne demande
  une réponse qu'après un clic explicite, dans une session avec relais valide.
- Le cache et le ViewModel existants `HdvViewModel.PriceHistory` séparent
  **PID + personnage + GID demandé**, bornent les points et annulent les
  lectures périmées. Le nouvel écran réutilise `BotsVM.Hdv`, donc le même
  personnage sélectionné et les mêmes données que `Bots → HDV`.
- Filtrage consultatif des dates réellement parsées sur 24 h, 7 j et 30 j,
  courbe du **prix moyen journalier reçu** et quantités vendues. Pas de
  graphique lorsque la réponse n'a pas au moins deux points datés ; le
  tableau d'origine demeure accessible dans Bots → HDV si une date n'est
  pas interprétable.

## Ce qui N'EST PAS présent ou prouvé

- La réponse `ObjectAveragePricesHistoryEvent` ne donne pas de GID ; le
  contexte de la requête est associé par la session, sans pouvoir certifier
  la correspondance si d'autres requêtes concourent. **Aucune vente d'un
  objet précis n'est déclarée prouvée à partir de ce seul message.**
- Le prix **médian** affiché par le client officiel, son éventuelle moyenne
  agrégée et sa dernière mise à jour ne sont pas dans ces points ; ne pas
  reconstruire en prétendant montrer les chiffres officiels.
- DofusDB donne des métadonnées d'objets, recettes et drops, PAS les ventes
  historiques HDV du serveur. Les revenus, coûts de craft, ROI ou bénéfices
  ne peuvent être chiffrés que sur données de marché réellement reçues et
  pertinentes à la version du jeu, pas sur le champ `price` de DofusDB.
- Bourse Kamas/Ogrines, taux instantanés, offres, boutique, codes, cadeaux,
  abonnements : les captations et commandes nécessaires ne sont pas
  démontrées dans GivreWork. Le nouvel écran les mentionne dans **une seule
  section secondaire repliée**, sans faux tableaux et sans boutons
  d'achat, d'échange ou de récupération.
- Les 24 h, 7 j et 30 j sont des fenêtres sur les points effectivement
  reçus ; aucune interpolation des journées sans données. Le code ne
  prétend pas récupérer les 30 derniers jours à coup sûr.
- Aucune preuve live de réception sur Dofus 3.7.12.12 lors de ce lot :
  il faut sélectionner un personnage avec HDV ouvert et cliquer
  `Lire l'historique` pour vérifier réellement la réponse serveur.

## Intégration & test

`MarketInsightsPanel.axaml` et son code-behind, nouvelle navigation via
`MainViewModel.MarketInsights.cs`, enrichissement de
`HdvViewModel.PriceHistory.cs` **sans changer les requêtes serveur**.
La page s'ouvre même sans connexion, avec états « en attente » lisibles.
Les courbes sont seulement alimentées par les points reçus du serveur.
Vérifier build Release, ouverture Windows, navigation, puis test réel HDV
séparé si le titulaire souhaite explicitement une connexion de test.
