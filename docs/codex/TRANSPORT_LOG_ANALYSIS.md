# Journaux transport Givre — observation en lecture seule (17 octobre 2026)

Source originale **jamais modifiée** : `%APPDATA%\GivreClone\logs\transport-2026-10-16_*.log`. Le répertoire `%LOCALAPPDATA%\Givre` héberge l'application et les mises à jour mais aucun `transport.log` lors de la vérification. Ce rapport retient seulement les types de messages, les directions et les comptages ; aucun jeton, identifiant de compte, hôte, contenu brut ou octet d'authentification n'est recopié.

| Fichier | Lignes | `socket tx` | `socket rx` | Autres |
|---|---:|---:|---:|---:|
| `transport-2026-10-16_20-51-37.log` | 96 156 | 11 316 | 50 127 | 34 713 |
| `transport-2026-10-16_22-53-13.log` | 15 178 | 2 094 | 8 464 | 4 620 |

Les compteurs correspondent à des **lignes de journal**, pas des paquets TCP distincts, transactions confirmées ni octets envoyés. Chaque ligne a été parcourue localement et classée par présence des marqueurs `socket tx` / `socket rx` ; les autres lignes peuvent rapporter une intention d'envoi ou une action, sans être `tx`.

## Séquence et types effectivement nommés

- Sortants (`socket tx`) : `LoginMessage`, `SelectServerRequest`, `IdentificationRequest`, `CharacterListRequest`, `ClientIdRequest`, `CharacterSelectionRequest`, `SequenceNumberRequest`.
- Entrants (`socket rx`) : `AuthenticationTicketAcceptedEvent`, `ServerSettingsEvent`, `ServerSessionReadyEvent`, `CharacterListEvent`, `CharacterSelectionEvent`, `TimeEvent`, `QueueStatusEvent`, `InventoryContentEvent`, `JobDescriptionEvent`, `JobExperiencesUpdateEvent`.
- Autres familles dominantes : sur la session 20:51, `GameActionFightEvent` (4 518 lignes RX), `MapMovementEvent` (3 503 RX), `MapMovementConfirmResponse` (1 703 RX), `GameActionUpdateEffectTriggerCountEvent` (7 096 RX), `SequenceStartEvent`/`SequenceEndEvent` (3 633 RX chacun). Sur 22:53, `KamasUpdateEvent` (944 RX), `ExchangeBidHouseItemAddedEvent` (937 RX), `ExchangeBidHouseItemRemovedEvent` (561 RX), `ExchangeBidPriceEvent` (300 RX), `ObjectQuantityEvent` (408 RX).

## Octets et limites

Les deux fichiers parcourus contiennent **zéro ligne avec un bloc contigu de 32 caractères hexadécimaux** ; 73 et 2 lignes, respectivement, correspondent au motif plus large d'octets hexadécimaux séparés (ces lignes n'ont pas été copiées : certaines pourraient contenir des données privées). Aucune ligne ne nomme explicitement une longueur avec `bytes`, `octets`, `length`, `len`, `size` ou `taille` dans ce comptage. Les journaux disponibles **ne permettent pas de reconstruire avec certitude les octets exacts des frames protobuf** ou leur ordre d'encodage ni de relier une réponse à un UID de transaction individuel.

Le catalogue local indépendant `data/matching_current.json` permet d'associer les noms de messages aux codes obfusqués 3.7.11.11 et `data/fields.json` fournit les numéros de premier niveau ; `GivreClone.Protocol/EnvelopeBuilder.cs` définit la requête `GameMessage.request` et sa forme `Request.uid/content` d'après ces champs. Cette correspondance est une **analyse statique** : les journaux de noms ne certifient pas, à eux seuls, les valeurs ni les octets réellement émis. La confirmation en jeu par le clone est **NON VÉRIFIÉE**.

## Complément après reprise du 17 octobre 2026 (sessions plus récentes)

Répertoire initial `%LOCALAPPDATA%\Givre` : aucun fichier nommé `transport.log` trouvé lors de la première recherche. Les fichiers `transport-*.log` réellement exploitables restent dans `%APPDATA%\GivreClone\logs`. La session `transport-2026-10-17_07-38-22.log` était **vide (0 octet)** au moment de l'inspection ; elle ne prouve pas une nouvelle connexion. Les fichiers précédents ont été analysés **en lecture seule** et **sans afficher de contenus d'authentification**.

| Fichier | Lignes | TX | RX | Autres |
|---|---:|---:|---:|---:|
| `transport-2026-10-17_03-38-14.log` | 285 946 | 33 904 | 153 068 | 98 974 |
| `transport-2026-10-17_05-42-21.log` | 286 960 | 34 249 | 153 540 | 99 171 |

Sur le fichier 05:42, les noms les plus répétés côté RX comprennent `GameActionUpdateEffectTriggerCountEvent` (18 976), `GameActionFightEvent` (16 628), `SequenceStartEvent` (14 695), `SequenceEndEvent` (14 695), `FightRefreshCharacterStatsEvent` (9 207), `MapMovementEvent` (8 105) et `MapMovementConfirmResponse` (5 052). Sur 03:38, `GameActionFightEvent` apparaît 15 550 fois, `MapMovementEvent` 8 281 fois et `MapMovementConfirmResponse` 5 332 fois. Ces valeurs sont des **occurrences dans le journal**, pas le nombre de mouvements réellement terminés. Un ACK nommé ne certifie ni la correspondance à la requête du clone ni la réussite d'une transaction particulière.

À nouveau, dans chacun de ces deux fichiers : **0 bloc hexadécimal contigu d'au moins 32 caractères, 0 longueur explicite en octets (`bytes`, `octets`, `length`, `len`, `size` suivis d'une valeur)** ; respectivement 298 et 294 lignes correspondent à un motif large d'octets hexadécimaux espacés, qui peut aussi reconnaître du texte ou des identifiants non binaires. **Ce ne sont pas des dumps vérifiés.** Aucune trame complète ni payload personnel n'a été recopié.

### Prochaine preuve technique, sans empiéter sur l'original

Pour valider l'équivalence **octet pour octet**, le clone doit journaliser *localement, sur la seule session de test autorisée*, des couples `(direction, code de message, longueur, hash ou échantillon hexadécimal strictement non sensible)` de trames **après framing**, avec la version de mapping, la génération de session et l'UID d'enveloppe assainis. Les contenus d'identification, tickets, cookies et tokens doivent rester exclus. Comparer séparément `GameMessage.request`, `Request.uid/content`, `Any.type_url/value`, et le préfixe de longueur. Ni ces journaux originaux ni les tests de forge ne constituent aujourd'hui une preuve de conformité wire ou d'ACK serveur pour le clone.
