# Prix moyen : reprise ciblée du 25 octobre

Base : `ce2a9bbd956b3022a53d7dabd759227cbfb7827a`. Aucun compte/client/pipe ni donnée utilisateur consulté. Aucun build, test .NET, compilation native, commit ou publication exécuté : validation réservée au parent.

## Chaîne relue

`CaptureDecoder.Feed(pid, direction, bytes)` reconstitue les trames par PID/sens ; `RawProtobuf.FindTypeUrls` trouve le payload et le catalogue résout `isr` en `ObjectAveragePricesEvent`. `LaunchService.OnDecoded` appelle `SnowObserve` en tête. Seul le sens serveur est traité. `ReadAveragePrices` résout les champs du mapping (`objects_average_prices` #3, `object_gid` #1, `average_price` #2 dans les données livrées), puis écrit les valeurs positives dans `_avgPrices`. La portée est le serveur identifié, sinon le PID. `AveragePrice` lit cette portée puis le repli PID reçu avant identification.

L'historique est séparé : `PriceHistoryAsync` / `LotMReader.PriceHistory` / vue HDV. Son événement ne porte pas de GID attesté ; il ne remplit pas la table de prix unitaires. Aucun historique, minimum HDV, valeur manuelle ou prix de substitution ajouté ici.

## Défauts démontrables dans la source

- Le détail d'inventaire sélectionné n'avait aucune ligne de prix moyen.
- L'infobulle lisait le prix une seule fois à son attachement. Ni l'arrivée d'une table après ouverture ni les changements de ses propriétés GID/UID/PID ne reconstruisaient son affichage.
- Le réducteur ne publiait aucun signal de mise à jour de la table.

Correction : émission de `AveragePricesChanged` après écriture de la table ; contrôle `ItemAveragePrice` partagé entre infobulle et détail, abonné uniquement pendant son attachement, actualisé sur le thread UI. Les changements de PID/GID relisent le cache ; les changements de GID/UID/PID reconstruisent aussi la fiche d'infobulle. Une valeur inconnue reste `—`.

Ces défauts d'affichage sont prouvés par lecture de code. Ils ne prouvent pas que le paquet du cas utilisateur atteint le bot : aucune capture live n'a été disponible ou consultée dans ce travail.

## Tests préparés, non exécutés

`AveragePriceReceptionTests` utilise le vrai `CaptureDecoder` et le vrai `SnowObserve` de l'application référencée par alias, sur un service non initialisé dont seuls les dictionnaires nécessaires sont créés. Aucun transport ou compte n'est construit. Les fixtures sont des octets protobuf synthétiques conformes au mapping livré, pas des captures annoncées comme réelles.

Cas : reconstitution de trame scindée, GID/prix exacts, notification après écriture lisible, partage entre bots d'un même serveur, séparation des serveurs, réception avant identification, deuxième valeur reçue, GID inconnu, paquet sortant ignoré, prix nul ignoré, historique non converti en prix unitaire.

Le parent doit compiler l'application puis exécuter au minimum `AveragePriceReceptionTests` et `LotMReaderTests` dans son créneau coordonné. La compilation XAML et la vérification UI restent nécessaires ; aucun résultat vert revendiqué ici.

## Preuve live minimale restant au parent

Sur son client autorisé, choisir un objet dont le jeu affiche un prix moyen. Relever uniquement le GID et la moyenne affichée ; vérifier réception de `ObjectAveragePricesEvent` et résultat `AveragePrice` pour le bot concerné. Ouvrir détail et infobulle avant puis après réception, et changer de bot/objet pendant leur affichage. Vérifier que la moyenne reste unitaire et propre au serveur. Si l'événement n'arrive pas ou si le cache reste vide, conserver une extraction ciblée non sensible de ce seul payload (pas de journal complet/auth) pour reproduire le défaut de décodage. Ne pas conclure que la chaîne live est corrigée avant cette comparaison.
