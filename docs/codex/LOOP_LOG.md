# Journal de fiabilisation du clone — 2026-10-17

## 2026-10-17 00:28 +02:00 — unité 1 : `ScriptCombatTests.BuildReadyFramed_false_is_empty_body_with_ready_code`

- Référence avant correction : test en échec, `Expected: "jyo"`, `Actual: "jxj"` (suite de référence). Une exécution complète et un fichier TRX de référence ont été lancés ; bilan quantitatif à relever.
- Diagnostic : ancienne attente du client 3.6.11.12 (`ScriptCombatTests.cs:128`) devenue obsolète. Le catalogue 3.7.11.11 contient `type.ankama.com/jxj -> MessageType.FightReadyRequest` (`data/matching_current.json:804`) et `FightReadyRequest.is_ready -> 1` (`data/fields.json:2897-2899`). Le forgeur utilise déjà le nom `FightReadyRequest` et le catalogue (`MoveForge.cs:189-190`).
- Action prévue : vérifier le code d'enveloppe contre `ProtocolCatalog.CodeForName("FightReadyRequest")` et le numéro de champ contre `FieldNumber`, sans supprimer les vérifications d'égalité des codes, de valeur et de corps vide.
- Résultat : `dotnet build GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo -v q` réussi (0 erreur, 10 avertissements existants) ; `dotnet test ... --filter FullyQualifiedName~ScriptCombatTests` : **9 réussites / 0 échec**. Ancien test en échec passé sans supprimer d'assertion.

## 2026-10-17 00:31 +02:00 — unité 2 : `MoveForgeTests.BuildFramed_encodes_key_cells_packed_like_client`

- Échec initial : code attendu `jpt` alors que le forgeur produit `jok`. Le source 3.7 `data/matching_current.json:724` mappe `MapMovementRequest -> jok` ; `data/fields.json:5055-5059` mappe `key_cells -> 1`, `map_id -> 2`, `cautious -> 3`. Le test 3.6.11.12 prétend `map_id -> 3` ; c'est une attente périmée.
- Action prévue : conserver la vérification du champ packé, des 3 valeurs, du mapId, de l'absence de champ supplémentaire et de l'enveloppe ; remplacer le code et les numéros figés par `CodeForName` / `FieldNumber` provenant du catalogue local. 
- Résultat : build Release réussi ; `MoveForgeTests` **1 réussite / 0 échec**, y compris les valeurs du pack et l'absence de champ supplémentaire.

## 2026-10-17 00:33 +02:00 — unité 3 : `CharacterInfoTests.Character_information_code_is_pinned`

- Échec initial : `NameForCode("ksw")` ne vaut plus `CharacterListEvent` (actuel `CharacterDeletionPrepareEvent`). `data/matching_current.json:1054` établit `CharacterListEvent -> ksc` pour le bundle 3.7.11.11.
- Action prévue : vérifier que le catalogue contient un code pour `CharacterListEvent`, que son décodage inverse rend le nom clair attendu et que le code résulte exclusivement de `matching_current.json`; aucune valeur obfusquée figée ajoutée.
- Résultat : build Release réussi (0 erreur, 10 avertissements existants) ; **3/3 tests CharacterInfoTests réussis**.

## 2026-10-17 00:35 +02:00 — unité 4 : trois tests `ForgeEmissionTests` (ready / cast / mouvement)

- Échecs initiaux : `Request_goes_into_the_new_envelope_slot` et `Cast_sends_spell_then_cell` comparent encore des codes 3.6 (`jyo`, `jrj`), `Movement_sends_cells_then_map` compare `jpt` et le mapId au champ #3.
- Sources : `MoveForge.cs` forge par nom `FightReadyRequest`, `GameActionFightCastRequest` et `MapMovementRequest`; le catalogue 3.7 a `FightReadyRequest.is_ready=1`, `GameActionFightCastRequest.spell_id=1/cell=2`, `MapMovementRequest.key_cells=1/map_id=2` (`data/fields.json:2897-2899,3342-3345,5055-5059`).
- Action prévue : corriger seulement ces trois tests pour lire codes et numéros depuis `ProtocolCatalog` ; conserver les valeurs envoyées, les contraintes d'enveloppe et la présence de cellule packée.
- Résultat : premier passage encore 3 échecs, car le helper commun `Request` lisait `GameMessage.request #1` au lieu de `#4` et `Request.content #1` au lieu de `#2`. Sources `data/fields.json:3421-3425,6757-6759` et `EnvelopeBuilder.cs:56-60,82-95`. Helper corrigé par le catalogue ; build Release réussi, **3 réussites / 0 échec** au second passage. Aucune suppression d'assertion.

## 2026-10-17 00:39 +02:00 — unité 5 : cinq tests `ForgeEmissionTests` (placement, tour, objet, utilisation, interactif)

- Échec initial : codes 3.6 codés en dur et champs inversés. Sources actuelles : `FightPlacementPositionRequest.cell_id=1/entity_id=2`, `ObjectDropRequest.object=2`, `ObjectUidWithQuantity.quantity=1/object_uid=3`, `MapChangeRequest.map_id=2`, `InteractiveUseRequest.skill_instance_uid=1/element_id=3` (`data/fields.json`).
- Action prévue : faire résoudre code et numéros via le catalogue tout en vérifiant les mêmes valeurs, la présence du sous-message d'objet, le corps vide des requêtes correspondantes, l'ensemble exact des champs de l'interactif et les caractères du jeu forgés.
- Résultat : build Release réussi (0 erreur, 10 avertissements existants), **5 réussites / 0 échec** sur les cinq cas ciblés. Le corps vide des deux forges est désormais également affirmé.

### Suite complète après 5 unités

- Commande exécutée : `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo`, TRX `artifacts/LoopTestResults/LOOP_AFTER_5.trx` : **1 091 réussites, 101 échecs, 2 ignorés, 1 194 total**. Le total diffère du référentiel filtré (1 193) car l'exécution inclut le test de temps à ignorer.
- Comparaison des identités en échec avec `LOOP_BASELINE_20261017.trx` (référence seconde exécution : 1 079 réussites, 112 échecs, 2 ignorés, 1 193 total) : **12 résolus**, dont 11 assertions protocolaires ciblées et un `MZP0GuardedActionSenderTests` sensible à la charge déjà présent ; **1 nouvel échec apparent** = seul `LuaToolingPerfTests.Lint_fold_and_format_of_10k_lines_under_300_ms` exclu de la référence à la demande de l'utilisateur. Aucun nouvel échec métier détecté.

## 2026-10-17 00:45 +02:00 — unité 6 : `ForgeEmissionTests.Challenge_sequence_replays_the_real_client_exchange`

- Échec initial : ancien code `kty` attendu, code `ktr` généré ; test contient des codes de 3.6 et `challenge_bonus` supposé champ #3, alors que `data/fields.json:1015` indique `challenge_bonus=1`. `ChallengeSelectionRequest.challenge_id=1` et `ChallengeValidateRequest.challenge_id=2` dans le bundle 3.7.11.11.
- Action : résoudre chaque code/numéro à partir du catalogue ; conserver validation des valeurs, messages sans contenu et des deux modes de bonus. Ne modifier aucun forgeur.
- Résultat : build Release réussi (0 erreur, 10 avertissements), test ciblé **1 réussite / 0 échec**, heartbeat actualisé.

## 2026-10-17 00:50 +02:00 — unité 7 : corrélation de l'achat HDV

- Défaut métier (revue des sources) : `LaunchService.Hdv.cs:219-224` interprète **tout** `ExchangeBidHouseBuyResultEvent` comme un achat réussi, même `bought=false` ou réponse pour un autre UID ; une requête en timeout peut être retentée sans vérification d'état. Le mapping `data/fields.json:2141-2144` fournit `object_uid` et `bought`, mais aucun champ réponse `quantity` ni `price` ; ceux-ci doivent être liés au contexte de requête exclusif. La confirmation complète du prix payé est donc NON VÉRIFIÉE tant qu'un état serveur additionnel n'est pas observé.
- Action prévue : un classificateur pur de réponse par UID et flag d'achat issu du mapping, verrou par PID pour éviter les achats simultanés, mémorisation des timeout non résolus qui interdit un nouvel achat sur cette session, réponse `false` lorsqu'un résultat reste ambigu. Tests de régression avec réponse étrangère, rejet, mapping absent et succès corrélé. Aucune répétition automatique.
- Résultat : test de régression introduit avant production, build initial volontairement rouge (classificateur absent) ; `MZP26HdvBuyReceipt` puis `LaunchService.Hdv.cs` implémentés ; **3/3 tests du classificateur réussis**, build des tests Release réussi (10 avertissements). Build App en cours. Limite : le serveur n'écho pas quantité/prix dans `ExchangeBidHouseBuyResultEvent`, ils restent liés au contexte de la requête exclusive mais la facture finale n'est pas vérifiée sur wire.

## 2026-10-17 00:55 +02:00 — unité 8 : dépôt Banque confirmé par les mouvements de piles

- Défaut métier : `LaunchService.Bank.cs:125-140` renvoie `true` après simple `relay.Inject`, `Task.Delay(900)` et fermeture du coffre, sans corrélation avec les mouvements d'inventaire serveur. Les autres chemins `BankDepositAllExceptAsync` utilisent déjà un rapprochement de UID via `MZP25BankReceipt`.
- Action : accumuler les UID de piles réellement transférées via événements `ObjectDeletedEvent` / `ObjectsDeletedEvent` / `ObjectQuantityEvent`, tous décodés avec le catalogue, et ne retourner `true` que si toutes les piles attendues sont attestées. Si réponse manquante, donner « inconnu » dans le journal et retourner `false`, sans répéter l'envoi. Tests purs vérifiant refus d'un événement sur une autre pile, accumulation de confirmations et mapping absent.
- Résultat : tests de régression introduits en premier (build rouge, `MZP27BankDepositTracker` absent), classificateur d'UID confirmé ajouté et intégré dans `BankDepositAllAsync`. `MZP27BankDepositTrackerTests` **2/2**, `MZP25BankReceiptTests` **6/6** et `MZP26HdvBuyReceiptTests` **3/3** ; build tests Release réussi, build App Release **0 erreur / 165 avertissements Avalonia**, sortie isolée `artifacts/LoopAppBuild/` (exécutable Release habituel verrouillé par PID 31136, aucune intervention sur ce processus). Le transfert réel est NON VÉRIFIÉ ; les résultats ambigus renvoient `false`.

## 2026-10-17 01:01 +02:00 — unité 9 : cinq tests d'émission `LotETests`

- Diagnostic : le lecteur de test `LotETests.Request` attend encore les anciens slots `GameMessage.request #1 / Request.content #1`, le bundle actuel indique `#4 / #2`. Quatre tests d'options/duels/sorts et la sauvegarde du zaap utilisent des codes obfusqués périmés (ancien `jyb`, `hqk`, `hnk`, `hin`) alors que les forges du clone résolvent correctement leurs noms. `FightOptionToggleRequest.option` est #1 selon `fields.json:2817`, et les autres champs sont résolus par nom.
- Action : rectifier seulement le lecteur et cinq tests d'émission à partir de `ProtocolCatalog` ; conserver les valeurs, les absences proto3, les champs exclus et les assertions de corps vide.
- Résultat : build Release réussi, **5/5 tests d'émission ciblés passés**. Aucune assertion supprimée ; heartbeat mis à jour.

## 2026-10-17 01:04 +02:00 — unité 10 : lecture Lot E (quatre événements) + ContextQuit

- Constat : `LotEEventsReader` utilise déjà `UseCatalog(FieldNumber)` (production correcte), mais les tests encodent encore la position des champs 3.6.11.12 (ex : `ExchangeRequestedTradeEvent.source_id` est maintenant #2, `target_id` #3, `PlayerFightFriendlyRequestedEvent.fight_id` #1). `ContextQuitRequest` a changé de code mais reste un corps vide.
- Action : construire les payloads synthétiques avec le catalogue actuel et conserver les assertions exactes sur la sémantique (`source`, `target`, `fight_id`, `author`, `lock_duration`, corps vide).
- Résultat : build Release réussi ; les **5/5 tests ciblés réussissent**. Suite complète `artifacts/LoopTestResults/LOOP_AFTER_10.trx` : **1 106 réussites / 91 échecs / 2 ignorés** (1 199 total avec les cinq nouveaux tests métier). Comparaison à `LOOP_AFTER_5.trx` : 11 anciens échecs résolus, un échec concurrent `MZP0PacketBusTests.Cancellation_registration_and_timer_initialization_race_do_not_hang_or_leak_waiters` apparaît uniquement sous charge et **passe isolément (1/1, 56 ms)** ; aucun nouveau défaut métier stable identifié.

## 2026-10-17 — unité 11 : cinq tests `ComfortLotTests` (forges et défis)

- Source `data/fields.json` : `CharacterStatus.status=3`, `Challenge.targets=5/challenge_id=2/drop_bonus=4/xp_bonus=3`, `ChallengeTarget.target_cell=5`, `ChallengeTargetsEvent.challenge=1`. Les tests encore en échec encodent l'ancien 3.6.11.12. `FightPrepReader` et `MoveForge` consomment déjà le catalogue actuel.
- Action : remplacer cinq charges/attentes figées par `Catalog().FieldNumber` et `CodeForName` tout en conservant les valeurs métier (carte, statut, bonus, id négatif, cellule, exclusions).
- Résultat : build Release 0 erreur (10 avertissements existants) ; les **5/5 tests corrigés passent**, sans diminuer les assertions.

## 2026-10-17 — unité 12 : deux défis actifs et une option de combat

- `ChallengeListEvent.challenges=1`, `Challenge.challenge_id=2`, `Challenge.state=1`, `ChallengeResultEvent.challenge_id=3/success=2`, `FightOptionUpdateEvent.team=1/state=2/option=3/fight_id=4` dans le catalogue 3.7.11.11. Les trois tests en échec utilisent encore les numéros de la version 3.6.
- Action prévue : bâtir la charge protobuf de chaque test avec les numéros de `fields.json`, conserver les mêmes résultats et assertions sur faux/true et champs absents.
- Résultat : build Release 0 erreur, tests ciblés **3/3 réussis** et heartbeat actualisé.

## 2026-10-17 — unité 13 : quatre forges `TreasureForgeTests`

- Échecs : `TreasureForgeTests.Request` lit les anciennes enveloppes `GameMessage.request=1` / `Request.content=1`. Les forges résolvent déjà les noms corrects, mais leurs codes obfusqués et `TreasureHuntFlagRequest.index=2/quest_type=3`, `TreasureHuntFlagRemoveRequest.index=3/quest_type=4` ne correspondent plus aux constantes dans les tests. Le refus d'invitation de guilde doit rester un corps vide.
- Action : dériver l'enveloppe, les codes et champs de `data/matching_current.json` + `data/fields.json` via `ProtocolCatalog` et préserver toutes les assertions de valeur/omission.
- Résultat : build Release 0 erreur, **4/4 tests du groupe réussis** ; heartbeat actualisé.

## 2026-10-17 — unité 14 : lecture PV/régénération et émotes

- Mapping `UpdateLifePointsEvent.life_points=3/max_life_points=1/life_points_gained=4`, `LifePointsRegenBeginEvent.regen_rate=1`, `EmotePlayEvent.actor_id=5`, `EmotePlayRequest.emote_id=1/fxra=2`. Les cinq tests `SurvivalTests` concernés utilisaient les anciens numéros en dur.
- Action : conserver les invariants (`Life <= Max`, champ absent, gained, taux de regen, actor signé, champ obfusqué omis) et générer les arbres synthétiques depuis le catalogue livré.
- Résultat : build Release 0 erreur, **4/4 tests ciblés réussis**. Le cinquième ancien cas `SelfVitals` dépend d'un sous-message complexe et sera traité séparément si les champs sont vérifiables ; heartbeat actualisé.

## 2026-10-17 — unité 15 : deux forges Lot J

- Échecs ciblés `LotJTests.LockableUseCode_carries_code_only` et `LotJTests.InteractiveUse_with_instance_and_identity_without` : les valeurs et champs sont déjà résolus par `F(cat,...)`, mais deux assertions figent encore le code obfusqué de 3.6.11.12 (`kfp`, `iva`).
- Action : conserver toutes les assertions sur code (`Payload` le vérifie déjà), champ, contenu, identifiant d'instance, surcharge et omission ; remplacer uniquement les codes figés par une vérification de présence du code clair dans le catalogue livré.
- Résultat : build Release 0 erreur, **2/2 tests ciblés réussis** ; mais suite complète `LOOP_AFTER_15.trx` : **1 112 réussites / 85 échecs / 2 ignorés** (même total 1 199), et **13 tests de forge corrigés ont régressé sous exécution parallèle**, tout en passant individuellement. Ne pas considérer le lot validé globalement avant résolution de cette interférence.

## 2026-10-17 — unité 16 : fuite globale de catalogue dans le harnais MZP0

- Diagnostic exact : `MZP0GuardedActionSenderTests.Catalog()` à `MZP0GuardedActionSenderTests.cs:48` appelle `EnvelopeBuilder.UseCatalog()` avec un catalogue **synthétique ancien** (`GameMessage.request=1`), ce qui écrase le catalogue du `TestSetup.Init()` (`GameMessage.request=4`) partagé avec les autres tests. Aucune restauration en `Dispose()`, alors que xUnit exécute plusieurs classes en parallèle. Cela provoque `ForgeEmissionTests.Request Assert.NotNull` et `TreasureForgeTests.Request Sequence contains no matching element` lors de la suite complète ; les tests ciblés isolés passent.
- Action : isoler la collection xUnit qui modifie ce singleton (`DisableParallelization`) et restaurer le catalogue fourni au test (`TestSetup.Init()`) après chaque instance. Conserver toutes les assertions et vérifications du harnais.
- Résultat : collection xUnit exclusive et restauration du catalogue dans `Dispose()` ; build 0 erreur ; sous-suite regroupée **30/30** et suite complète `LOOP_AFTER_16.trx` : **1 125 réussites / 72 échecs / 2 ignorés** sur 1 199, soit disparition de 13 échecs induits par fuite. Aucun test de forge ciblé en échec dans la suite.

## 2026-10-17 — unité 17 : trois forges quêtes/équipement (`LotIQuestInventoryTests`)

- L'enveloppe du helper du test lit encore `GameMessage.request=1/Request.content=1`, tandis que les messages utilisent des codes périmés et les champs `ObjectSetPositionRequest.quantity=1/object_uid=2/position=3`, `QuestObjectiveValidationRequest.objective_id=1/quest_id=2`, `QuestStepInformationRequest.quest_id=1/fpbs=2` dans `data/fields.json`.
- Action : remplacer ces constantes par `CodeForName` / `FieldNumber` ; conserver les valeurs envoyées, le champ `quantity` et l'omission de `fpbs`.
- Résultat : build Release 0 erreur, tests ciblés **3/3 réussis**, heartbeat actualisé.

## 2026-10-17 — unité 18 : trois lecteurs inventaire / quêtes

- Sources : `ObjectMovementEvent.object_uid=1/position=3`, `InventoryContentEvent.objects=2`, `ObjectItemInventory.item=1/position=4`, `ObjectItem.uid=5/quantity=3/gid=4`, `QuestsEvent.finished_quests=5`, `QuestFinished.quest_id=2/fpci=4`. Les fixtures 3.6 utilisent des conteneurs/sous-champs périmés.
- Action : construire trois messages synthétiques via `F()` : position absente = emplacement 0, identité de la pile conservée, quêtes terminées séparées du champ opaque `fpci`. Aucune interprétation inventée de `fpci` en production.
- Résultat : build Release 0 erreur, tests ciblés **3/3 réussis**, heartbeat actualisé.

## 2026-10-17 — unité 19 : lecture des quêtes imbriquées et événements incrémentaux

- `QuestsEvent.finished_quests=5/active_quests=6/player_id=3`, `QuestActive.details=3`, `Details.objectives=3`, `QuestObjective.objective_id=4`, `Completion.current_completion=2/max_completion=1` sont tous définis par `fields.json`; le lecteur `QuestReader` résout déjà ces noms mais les fixtures sont en 3.6. Idem `QuestStepInformationEvent.information=1/player_id=2`, `QuestsFollowedEvent.quests=2`, `QuestStepStartedEvent.quest_id=3/step_id=4`.
- Action : construire deux scénarios de test du snapshot et des événements incrémentaux depuis le catalogue, conserver les assertions portant sur IDs, complétions, succès, suivi et détails présents/absents.
- Résultat : build Release 0 erreur, tests ciblés **2/2 réussis**, heartbeat actualisé.

## 2026-10-17 — unité 20 : deux tests de vitalité/énergie/XP `SurvivalTests`

- `CharacterCharacteristicsEvent.stats=1`, `CharacterCharacteristics.characteristics=1/experience_level_floor=8/experience_next_level_floor=2`; chaque `CharacterCharacteristic.usable=1/characteristic_id=2`; la valeur détaillée utilisable emploie `base=4`, `objects_and_mount_bonus=1`, `context_modification=6`. La fixture du test encode encore l'enveloppe de caractéristiques 3.6. Les identifiants de métier (`Life=0`, `Vitality=11`, `Energy=29`, `MaxEnergy=47`, `LifeDelta=97`) sont des ID de données utilisés par le lecteur et ne sont pas des numéros wire.
- Action : construire les piles de stats depuis les noms du mapping et conserver l'assertion sur 2349 PV max, -500 delta, énergie 8500/10000, niveaux d'XP 1000/9000, et absence de delta lorsque ce champ est absent.
- Résultat : build Release 0 erreur, **2/2 tests ciblés réussis**, heartbeat actualisé. Suite complète `LOOP_AFTER_20.trx` : **1 135 réussites / 62 échecs / 2 ignorés** sur 1 199 ; comparaison à `LOOP_AFTER_16.trx` : **10 échecs métier résolus, 0 nouveau**.

## 2026-10-17 — unité 21 : lecteur de niveau monstre et attaquants des défis

- D'après `fields.json`, un monstre affiché se lit dans `FightFighterShowEvent.information → ActorPositionInformation.actor_information → ActorInformation.fighter → FightFighterInformation.ai_fighter → AIFighterInformation.monster_fighter_information → MonsterFighter.creature_level`. Les champs du niveau/du GID ont tourné (`creature_level=2/monster_gid=3`). Un `Challenge` a `targets=5/challenge_id=2`, et `ChallengeTarget.attackers_id=2/target_cell=5`.
- Action : reproduire les deux arbres avec le mapping actuel et conserver les assertions niveau du monstre, absence pour un joueur, attaquants packed/unpacked et absence d'attaquants.
- Résultat : build Release 0 erreur, les **2/2 tests corrigés passent** ; aucune lecture de production modifiée.

## 2026-10-17 — unité 22 : résultat de craft produit et effet réel sur le sac

- `CraftInventoryTests.Craft_result_capture_is_success_with_produced_item` échoue car l'ancienne fixture utilisait `ExchangeCraftResultEvent.result=2/object=3`, alors que le mapping 3.7 livre `result=1/object=2`, `ExchangeCraftResultWithObjectDescription.object=4`, `ObjectItem.quantity=3/gid=4`. Le scénario de dépôt+craft adjacent pouvait passer à tort avec quantité produite zéro, car il affirmait seulement `avant + r.Qty`.
- Action : partager une fixture de reçu synthétique `result=SUCCESS`, quantité 100, gid 7652 avec les champs du catalogue, puis renforcer le scénario avec `r.Made`, `r.Gid=7652`, `r.Qty=100` et le comptage final de sac.
- Résultat : build Release 0 erreur, les **2/2 tests ciblés réussissent** (dont le scénario avant/après doté de trois assertions supplémentaires). Aucun succès métier obtenu seulement parce que la quantité aurait été 0.

## 2026-10-17 — unité 23 : quatre lecteurs interactifs / zaap

- `MapComplementaryInformationEvent.interactive_elements=7/stated_elements=2`, `InteractiveElement.element_id=6/element_type_id=4/on_current_map=2/enabled_skills=5/disabled_skills=3`, `StatedElement.cell_id=4/state=5`, `TeleportDestinationsEvent.destinations=4/spawn_map_id=1`, `TeleportDestination.map_id=4`. Les fixtures historiques de `InteractivesAndZaapTests` réutilisaient d'anciens champs et se soldaient par listes vides ou mauvaises cellules.
- Action : reconstruire les quatre messages de test avec les numéros de champs du catalogue, valider les deux états de récolte, skill instance, cellule, proto3 0 et trois destinations de téléportation.
- Résultat après reprise : `InteractivesAndZaapTests` corrigés avec `FieldNumber` pour les champs imbriqués, skill activé puis désactivé, cellule/state et destinations de zaap ; `dotnet build ... -c Release` **0 erreur / 10 avertissements préexistants** ; test filtré **4/4 réussis**. La suite complète est en cours de vérification.

### Complément de lecture transport

- `docs/codex/TRANSPORT_LOG_ANALYSIS.md` mis à jour avec les sessions 03:38 et 05:42 du 17/10 (lecture seule `%APPDATA%\GivreClone\logs`). Plus de 285 000 lignes/session et nombreux événements RX, notamment ACK de mouvement ; aucun dump de trame hexadécimale complète identifié. Aucun secret ni payload brut copié.

## 2026-10-17 — passe A / unité 24 : mapping HDV hors ligne et challenge (4 cas)
- Diagnostic : tests historiques attendaient le code kdk et ObjectGidWithQuantity{quantity=1,price_date_effect=2,object_gid=3}, alors que matching_current.json donne ExchangeBidHouseOfflineSoldItemsEvent=jze et fields.json donne quantity=2/price_date_effect=4/object_gid=1. ClientChallengeInitRequest.fyvk est absent ; aucun alias déduit.
- Action : HdvOfflineSalesMappingTests lit les deux bundles data/ et server-mapping/, vérifie retour code/nom, tags actuels et l'absence explicite de fyvk ; assertions maintenues pour champs connus de sélection et du challenge.
- Résultat : Release 0 erreur ; suite ciblée mixte Scripting+HDV 29 tests dont 4/4 HDV passent (LOOP_UNITS_24_25.trx). Aucune action jeu.

## 2026-10-17 — passe A / unité 25 : packet tree et groupe (5 échecs historiques)
- Diagnostic : ChatChannelMessageEvent et PartyJoinEvent étaient encodés avec les anciennes positions ; en absence du .proto clair, les membres d'un groupe devenaient des clés numériques, non des Character.* accessibles aux hooks Lua.
- Action : fixtures depuis FieldNumber, arbres Character et CharacterBasicInformation conformes à fields.json ; PacketTree porte uniquement le typage documenté PartyJoinEvent.members -> Character lorsqu'un schéma .proto n'est pas présent.
- Résultat : Release 0 erreur ; ScriptingPlatformTests + HdvOfflineSalesMappingTests 29/29 réussis (LOOP_UNITS_24_25.trx), assertions conservées. Contrat groupe réel NON VÉRIFIÉ en jeu.

## 2026-10-17 — passe A / unité 26 : trois alias Lua morts, 1 contrat
- Diagnostic : D2ToD3 contenait trois cibles absentes du matching_current.json (ExchangeBidHouseGenericItemAddedEvent, ExchangeJobIndexStartedEvent, PartyLocateMembersRequest) : elles ne peuvent pas résoudre de code. Ne pas inventer de destination.
- Action : retirer seulement les trois entrées cibles non résolues de LuaMessageAliases ; conserver les identifiants Dofus 2 documentaires et les centaines d'autres alias valides.
- Résultat : Release 0 erreur, le contrat xUnit Every_alias_target_exists_in_the_shipped_catalog réussit (LOOP_UNITS_26_27.trx).

## 2026-10-17 — passe A / unité 27 : lecture bidirectionnelle du catalogue, 2 contrats
- Diagnostic : tests supposaient MapComplementaryInformationEvent=jpo et map_id=#6 (anciens champs 3.6) ; 3.7.11.11 livré associe jnj et #10.
- Action : adapter assertions du nom et du numéro dans les deux directions, conserver refus des champs inconnus, ne créer aucune donnée.
- Résultat : Release 0 erreur, 2/2 contrats corrigés verts (LOOP_UNITS_26_27.trx).

## 2026-10-17 — passe A / unité 28 : fail-closed Schnorr de re-vérification, 2 contrôles plus manifeste
- Diagnostic : SocketGameClient émettait ClientChallengeInitRequest.fyvk inexistant dans les deux fields.json ; cette exception survient sur ServerVerificationEvent et aucune valeur binaire ne peut être déduite. RequiredMapping avait persisté la dépendance périmée.
- Action : arrêt explicite de la session avec motif journalisé, aucune réponse fabriquée, retrait uniquement de la dépendance absente dans RequiredMapping.cs. Ceci n'apporte PAS la prise en charge du challenge : session longue NON VÉRIFIÉE.
- Résultat : build Release 0 erreur ; 4/4 vérifications ciblées incluant les deux copies de Every_lookup_written_in_the_code et le manifeste, plus alias (LOOP_UNIT_28.trx).

## 2026-10-17 — passe B / unité 29 : contrat NativeTurbo, un test
- Diagnostic : l'ancienne assertion textuelle exigeait l'implantation directe sous le dossier GivreClone original et des chaînes de LOAD par module, alors que le clone protège maintenant les chemins par MZP25NativeModPaths.Turbo et centralise LOAD dans EnsureLoadedAsync.
- Action : source-test adapté aux appels effectivement présents et à la politique de séparation du clone. Conserve les assertions hash-pin, PID, generation, restore ACK, absence version.dll. Aucun module chargé ou processus inspecté.
- Résultat : Release 0 erreur, ciblé 1/1 (LOOP_UNIT_29R.trx). La première tentative du même lot était rouge et a été réparée sans changement produit.

## 2026-10-17 — passe B / unités 30–31 : inventaire/personnage/sorts/challenge, six fixtures
- Diagnostic : ces fixtures de 3.6.11.12 utilisent les anciens numéros de champs même si les lecteurs de production demandent ceux de fields.json 3.7.11.11.
- Action : F(message,field) d'après le catalogue livré pour InventoryContentEvent/Item/Quantity, CharacterListEvent/Look/gender, SpellsEvent/SpellItem et ChallengeProposalEvent/Challenge/Target ; assertions de UID, GID, quantités, niveau/nom/sexe, sorts/grades et défi inchangées.
- Résultat : 6/6 passés (LOOP_UNITS_30_31.trx), Release 0 erreur, aucun wire présumé.

## 2026-10-17 — passe B / unité 32 : acteurs/carte/décès en combat, quatre fixtures
- Diagnostic : ancien MapComplementaryInformationEvent.actors=9, dispositions et sous-actions de combat périmés ; MapActorsReader/MapFightReader lisent déjà par nom 3.7.
- Action : quatre fixtures avec numérotation du mapping pour les acteurs joueur/PNJ/groupe de 3 monstres, l'indice chasse, la mort versus slide et la disposition avec case 0 proto3. Toutes les assertions de métier conservées.
- Résultat : 4/4 passés (LOOP_UNIT_32.trx), Release 0 erreur.

## 2026-10-17 — passe B / unité 33 : Banque/HDV, quatre nouvelles épreuves de sécurité
- Diagnostic : les confirmations sont par UID et disparition de pile, pas par succès de relay ; une réponse HDV BuyResultEvent ne donne ni quantity ni price, la génération ne se trouve pas sur le fil.
- Action : tests sur quantité serveur 0 vs non 0, UID externe, doublons attendus, ensemble vide, champ Wire invalide, UID absent, autorité absente et preuve insuffisante prix/quantité. Pas de remise en vente/réémission testée ici.
- Résultat : Release 0 erreur, ciblés 9/9 (LOOP_UNIT_33.trx : 5 préexistants + 4 nouveaux), aucun contrôle en jeu.

## 2026-10-17 — passe B / unité 34 : résultats de combat et défis, six fixtures
- Diagnostic : FightResultTests conservait les champs 3.6 (`FightEndEvent.results=5`, `duration=4`) non conformes à fields.json 3.7.11.11 (`results=4`, `duration=8`), et des numéros obsolètes jusqu'au butin, à l'XP et aux défis.
- Action : construction entièrement par `ProtocolCatalog.FieldNumber` de la hiérarchie résultat/combattant/XP/récompenses/butin et des défis ; toutes les assertions XP, kamas, cumul du loot, issue, durée et proto3 maintenues. Fixtures synthétiques : aucune prétention de captures wire.
- Résultat : build tests Release **0 erreur**, cible `LOOP_UNIT_34.trx` **6/6 PASS**. Aucun client/processus/réseau d'origine touché.

## 2026-10-17 — passe B / unité 35 : invocations, quatre fixtures
- Diagnostic : tags `GameActionFightEvent.summons`, `ActorPositionInformation`, `FightFighterInformation` et des branches par acteur/contexte périmés ; risques de classer une invocation alliée comme monstre.
- Action : remplacer chaque tag par le mapping 3.7 livré, conserver id/cell/team/summoner, invocations multiples, équipe proto3 0 et absence de preuve quand spawn manque.
- Résultat : build tests Release **0 erreur**, `LOOP_UNIT_35.trx` **4/4 PASS**. Aucune trame réelle d'invocation certifiée.

## 2026-10-17 — passe B / unité 36 : déplacements forcés, sept fixtures
- Diagnostic : tags `death/slide/life_points_lost/points_variation/teleport/throw/drop/carry/invisible/exchange_positions` changés dans fields.json ; vieux tests d'octets 3.6 prétendaient à tort rejouer des captures identiques.
- Action : F(message, champ) pour requêtes et sous-messages avec vérifications de signe, cellule, PA/PM, non-confusion décès/poussée et proto3 cellule 0. Commentaire clarifié : reconstitution synthétique, pas preuve réseau.
- Résultat : build tests Release **0 erreur**, `LOOP_UNIT_36.trx` **7/7 PASS**.

## 2026-10-17 — passe B / unité 37 : PA/PM, tour et équipe, quatre derniers tests Patch361112
- Diagnostic : `FightRefreshCharacterStatsEvent` et ses caractéristiques, `FightTurnEvent.character_id`, `FightFighterShowEvent` et `FightTeamUpdateEvent` utilisent des numéros 3.7 différents ; source-test encore présentée comme captures identiques, désormais inexact.
- Action : nouvelle fixture commune `DetailedCharacteristic` fondée sur fields.json, quatre scénarios reconstruits, assertions de PA=2/PM=0, tour exact, PV max=2349, équipe 0 et membre 41234567890 conservées. Commentaire de provenance rectifié.
- Résultat : build tests Release **0 erreur**, `LOOP_UNIT_37.trx` **18/18 PASS** sur toute la classe Patch361112Tests (dont les 4 anciennement rouges).

## 2026-10-17 — passe B / unité 38 : contrat confirmed.json facultatif
- Diagnostic : cinq assertions historiques accédaient sans garde au `confirmed.json` inexistant, alors que `ProtocolCatalog.LoadFromDirectory` traite explicitement cet override comme **facultatif**. `matching_current.json` et `fields.json` sont les sources obligatoires, disponibles dans les deux dossiers.
- Action : maintenir les invariants forts de contenu de confirmed.json s'il est livré (aucune section ignorée, aucun renommage), et exiger présence symétrique `data/` / `server-mapping/`. Le test refuse toujours l'absence des deux JSON obligatoires et toute divergence des fichiers existants. Aucun confirmed.json créé.
- Résultat : build tests Release **0 erreur**, classe `MappingContractTests` `LOOP_UNIT_38.trx` **16/16 PASS**, couvrant les cinq anciens rouges.
