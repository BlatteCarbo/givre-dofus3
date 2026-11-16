# Bilan de reprise GivreShareWork — 17 octobre 2026

## État vérifié

- Reprise après une interruption du contexte de terminal : l'ancien identifiant de session de test n'était plus disponible. Plutôt que d'en déduire un échec, les deux tests de craft concernés ont été **réexécutés** et réussis : `Craft_result_capture_is_success_with_produced_item` et `Bank_deposit_by_list_then_craft_keeps_the_bag_count_right` (**2/2**).
- Source projet : `%USERPROFILE%\Videos\GIVRE\GivreShareWork`. Arbre Git fortement modifié **avant la reprise**, modifications préexistantes laissées en place (pas de réinitialisation ni de commit).
- Unité 23 terminée après reprise : les quatre tests `InteractivesAndZaapTests` ont été réalignés sur les `FieldNumber` réellement livrés et **4/4 passent** ; compilation du projet de tests Release **0 erreur**, 10 avertissements analyzers préexistants.
- Suite complète **après reprise** : `artifacts/LoopTestResults/LOOP_AFTER_23_RESUME.trx`, **1 208 tests : 1 151 réussites / 55 échecs / 2 ignorés**, exécution 12 secondes. Il s'agit de tests locaux, pas d'une validation en jeu.
- Comparatif strict d'identités des échecs avec `LOOP_AFTER_20.trx` : **7 échecs résolus, 0 nouvel échec**. Le total de tests a grandi de 1 199 à 1 208 entre les deux observations ; ne pas en déduire une régression à partir des seuls totaux.
- Build Application : un build Release en sortie **isolée** `artifacts/LoopAppBuild/` avait réussi (0 erreur, avertissements UI) pendant la première phase de la boucle. La sortie habituelle était verrouillée par `GivreClone.exe` PID 31136. **Aucun processus original ni jeu n'a été interrompu ou modifié**. La reprise n'a touché qu'aux tests/docs après le dernier build App.

## Couverture des corrections

- Forges et lecteurs protobuf dérivés de `data/matching_current.json` et `data/fields.json` : combat, mouvement, inventaire, quêtes, défis, chasse au trésor, caractéristiques/vitalité/énergie/XP, récolte/interactifs, zaap, craft. Assertions des valeurs attendues conservées ou renforcées, non substituées par des codes obfusqués inventés.
- Une fuite de catalogue **global** à travers `MZP0GuardedActionSenderTests` a été identifiée sous exécution parallèle ; collection xUnit exclusive + restauration dans `Dispose()`. Sous-suite combinée 30/30 réussites lors de la validation correspondante.
- HDV : `MZP26HdvBuyReceipt` valide `ExchangeBidHouseBuyResultEvent` uniquement sur `object_uid` attendu + `bought=true`, avec verrou par PID et mémoire de timeout ambigu, sans répétition automatique. Trois tests du classificateur réussis. La quantité/prix ne sont PAS renvoyés par l'événement de résultat, donc facture finale non prouvée côté serveur.
- Banque : `MZP27BankDepositTracker` n'accorde un dépôt complet que si toutes les piles ciblées sont confirmées par les événements d'inventaire côté serveur ; deux tests ciblés réussis, six tests MZP25 existants réussis. Un envoi accepté seul ne vaut pas dépôt confirmé. Cas réels non exercés.

## 55 échecs restants, répartis par classe

| Famille | Échecs | Limite |
|---|---:|---|
| `Patch361112Tests` | 14 | Fixtures de combat/acteurs encore ancrées sur des champs historiques |
| `CombatForcedMovesReplayTests` | 7 | Tests de rejouage de déplacements/combat non réalignés |
| `MappingContractTests` | 7 | Dont plusieurs exigent `data/confirmed.json` ou `server-mapping/confirmed.json`, absents ; au moins une recherche de champ introuvable |
| `FightResultTests` | 6 | Lecteurs et fixtures de résultats de combat à comparer au mapping actuel |
| `ScriptingPlatformTests` | 5 | Correspondances de champs de packet tree et party |
| `SummonReaderTests` | 4 | Formes de sous-messages d'invocations à vérifier |
| `ProtocolCatalogTests` | 4 | Anciennes attentes sur table de positions, parser et overrides |
| `HdvOfflineSalesMappingTests` | 4 | Assertions historiques non conformes à la table 3.7.11.11 |
| `LuaTooling` | 2 | Un exemple du formateur + un test de performance dépendant de la charge |
| `LuaSnowCompatTests` | 1 | Alias de catalogue non résolus |
| `MitmTurboSourceTests` | 1 | Contrat de fallback natif encore en échec |

**Ne pas créer `confirmed.json` artificiellement pour faire passer les tests.** Un test rouge peut être obsolète, mais certains échecs désignent de vraies lacunes : tri nécessaire sur preuve `matching_current.json`/`fields.json` et captures autorisées. Ni transport complet, ni ACK métier, ni mouvement en jeu ne sont à déclarer prouvés par le simple passage des tests hors ligne.

## Transport Givre original (lecture seule)

Le répertoire trouvé est `%APPDATA%\GivreClone\logs`, **pas** `%LOCALAPPDATA%\Givre\transport.log`. Rapport détaillé assaini dans `docs/codex/TRANSPORT_LOG_ANALYSIS.md`. Deux fichiers du 17 octobre (03:38 et 05:42) comptent 285 946 et 286 960 lignes ; le second contient 34 249 lignes `socket tx`, 153 540 lignes `socket rx`, et 99 171 autres. Présence de `GameActionFightEvent`, `MapMovementEvent`, `MapMovementConfirmResponse`, etc. Ce sont des **occurrences de lignes**, non des métriques TCP ou des accusés de réception corrélés à une transaction du clone.

**Aucun dump hexadécimal complet vérifié** ni longueur en octets n'a été trouvé dans les deux fichiers analysés. Les noms de messages et leur ordre sont utiles pour localiser les points de comparaison ; les octets exacts doivent encore provenir d'une capture non sensible, limitée au client de test autorisé, avec trames correctement corrélées.

## Reproduction

Dans le dépôt :

```powershell
dotnet build GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo -v q
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo --no-build --filter 'FullyQualifiedName~InteractivesAndZaapTests'
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo --no-build --logger 'trx;LogFileName=RECHECK.trx' --results-directory artifacts/LoopTestResults
```

Priorités restantes : investiguer le protocole réel des combats et des invocations à partir des champs 3.7 ; résoudre les écarts de catalogue sans fausses données ; tester le contrôle de corrélation banque/HDV en hors-ligne puis sous une seule session test contrôlée ; et vérifier la chaîne transport/framing complète avec preuve d'ACK serveur.
