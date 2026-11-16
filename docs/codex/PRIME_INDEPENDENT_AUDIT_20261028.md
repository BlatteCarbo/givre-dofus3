# Revue indépendante Integration Prime — 28 octobre 2026

## Périmètre et décision

Autorisation utilisateur : exécuter les tests et clôturer si justifié. Revue exécutée seul, sans worker ni intervention sur les sessions de jeu.

La branche la plus récente examinée est `integration/prime-20261026-wave12`, malgré son nom ancien : HEAD observé `a579d65640c4a1b92fcd12b7a528f8e83b425f27` (F02 W92). La première appréciation de wave15 était dépassée. Le worktree wave12 contient également des modifications non commitées et des tests actifs appartenant à l'autre coordinateur.

Les sources et changements locaux ont été copiés dans `artifacts/prime-audit-20261028`, sans bin/obj généraux ni journaux historiques. Les dépendances locales source ont été copiées séparément. La photographie a été prise pendant une activité concurrente : elle n'est pas un checkout pur de a579d65. Les 1 206 empreintes de `audit-source-manifest.json` identifient les fichiers inspectés. Aucun fichier du worktree Prime n'a été modifié.

**Décision : aucune clôture globale ou de fonctionnalité en jeu.** Les preuves ci-dessous acceptent des vérifications limitées ; elles ne rendent pas les prototypes utilisables. La suite complète reste intermittente. L'enchaînement de combats est bloqué par une garde de production.

## Résultats exécutés

| Vérification | Résultat indépendant |
|---|---|
| Restore projet de tests et dépendances | Exit 0 |
| Build application Release | Exit 0 après copie du module natif requis |
| Suite .NET complète, configuration par défaut | 3 783 PASS, 4 FAIL, 2 SKIP ; total 3 789 |
| Relance ciblée classes H03 W43, F02 W86, PacketBus, C02 W90 et F02 W92 | 38 PASS, 0 FAIL |
| Suite complète avec xUnit MaxParallelThreads=4 | 3 786 PASS, 1 FAIL, 2 SKIP ; total 3 789 |
| Relance StrictTeamWaitTests | 12 PASS, 0 FAIL |
| U09 modèle de préférences Python | 29 PASS |
| Q03 tests du vérificateur Python | 18 PASS |
| N01 politique de mine Python | 24 PASS |
| Q03 audit strict des sources courantes | 0 erreur, 0 anomalie ; 737 sources, 2 176 clés référencées |
| Dictionnaires FR/EN/ES | 2 713 clés chacun, aucun doublon |

Le premier build s'est arrêté sur MSB3030 : notre exclusion générale des dossiers bin avait omis `native/GivreCombatVisuals/bin/GivreCombatVisuals.dll`. Après copie depuis Prime, le build application réussit. SHA-256 du module : `eca6acc6cfa393dc5166bead2863734fa04a4640254518ef863980f8f038efd8`. Cet incident de préparation de l'audit n'est pas compté comme un défaut du code Prime.

Échecs du passage par défaut :

- `H03Wave43BuyerSessionGenerationTests.Concurrent_close_waits_for_exact_dispatched_write_under_the_same_gate`
- `MZP0PacketBusTests.Publish_commits_waiter_before_slow_subscriber_even_if_cancel_runs`
- `F02Wave86BankDepositDispatchTests.Two_concurrent_deposits_must_not_both_reach_the_physical_injector`
- `MZP0PacketBusTests.Competing_publish_cancel_and_timeout_settle_once_under_load`

Échec du passage à parallélisme limité :

- `StrictTeamWaitTests.Automatic_wait_resumes_when_the_missing_member_leaves_the_expected_team`

Les cinq scénarios passent lors des relances ciblées. Plusieurs tests mêlent Task.Run, attentes synchrones et délais courts ; la sensibilité à l'ordonnancement est une hypothèse étayée, pas une cause racine démontrée pour tous les échecs. Aucun délai ou assertion n'a été modifié pour obtenir ces résultats. Aucun troisième passage complet n'a été lancé pour masquer l'intermittence.

## Blocage produit prioritaire : deuxième combat

Dans `GivreClone.App/Services/LaunchService.C02TerminalUncertainty.cs`, `C02DropUnprovenFightStart` appelle `C02MarkCurrentFightUnknown` dès qu'un combat a déjà été vu sur la session physique. Ce chemin ne requiert pas l'observation d'un ancien paquet effectivement retardé : le deuxième départ admis suffit.

Dans `LaunchService.cs`, `DispatchTransport.Inject` renvoie false lorsque `C02TerminalOutcomeUnknown(pid)` est vrai. Les paquets de tour, de combattants, de séquence et certains retours RPG sont également filtrés. Le nettoyage `C02ForgetTerminalScope` est lié à `ForgetSession`.

Le scénario `C02Wave90PreAmbiguityIngressTests.Second_start_becomes_unknown_before_subscribers_can_schedule_combat_actions` a été exécuté avec succès dans la sélection des 38 tests. Il démontre le chemin réel OnDecoded : premier combat, fin, deuxième combat, état Unknown avant les abonnés. D'autres tests de la même classe vérifient le filtrage des événements et le renouvellement explicite de session.

Cette stratégie refuse des actions ambiguës, mais ne satisfait pas l'objectif d'enchaîner les combats de manière autonome. Ne pas présenter ce test vert comme la résolution du problème de combat. Il faut une récupération réellement raccordée (ou une corrélation justifiée par le contrat du transport) et un essai de deux combats successifs, puis plusieurs combats, sur la version résultante. Ne pas simplement supprimer la garde sans traiter le risque de paquets périmés.

## Qualification des lots examinés

- **Q03 : vérificateur statique accepté dans son périmètre.** Tests adversariaux et audit courant réussis ; pas de validation de rendu/culture réelle ni de preuve du packaging hébergé. Lot complet non clôturé.
- **U09 : modèle testé accepté, intégration ouverte.** Le rapport indique explicitement que le stockage Python n'est pas raccordé au chargement/sauvegarde Avalonia. Les 29 tests ne démontrent pas une migration de vrais profils de l'application.
- **N01 : prototype testé accepté, correction de navigation ouverte.** Les tests portent sur la politique hors ligne ; portes/cellules et contournements réels non démontrés.
- **F02 W92 : vérification de code acquise, lot ouvert.** Build et scénarios ciblés passés ; aucune opération bancaire réelle effectuée par cet audit.
- **C02 et autonomie : non clôturables.** Blocage de production au deuxième combat, aucune preuve de reprise automatique correspondante.
- **Fiabilité de la suite : ouverte.** Deux passages globaux en échec avec scénarios différents ; les réussites ciblées n'annulent pas ces résultats.

## Reproduction et preuves

Depuis `artifacts/prime-audit-20261028` avec NUGET_PACKAGES pointant sur `%USERPROFILE%/.nuget/packages` :

```powershell
dotnet restore GivreClone.Tests/GivreClone.Tests.csproj --verbosity quiet
dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=prime-audit.trx' --results-directory audit-results --verbosity quiet
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-build --no-restore --settings audit.runsettings --logger 'trx;LogFileName=prime-audit-bounded.trx' --results-directory audit-results --verbosity quiet
py -B -m unittest discover -s docs/agents/20261026/U09/u09-preference-migration -p test_preferences.py -v
py -B -m unittest discover -s docs/agents/20261026/Q03/q03-localization -p test_audit.py -v
py -B -m unittest discover -s docs/agents/20261026/N01/n01-mine-exit-policy -p test_policy.py -v
py -B docs/agents/20261026/Q03/q03-localization/audit.py --repo . --strict
```

`audit.runsettings` contient `<RunSettings><xUnit><MaxParallelThreads>4</MaxParallelThreads></xUnit></RunSettings>`.

Preuves locales : `audit-app-build-final.log`, `audit-full-tests.log`, `audit-full-bounded-tests.log`, `audit-targeted-tests.log`, `audit-team-retest.log`, `audit-u09.log`, `audit-q03.log`, `audit-n01.log`, `audit-localization-current.log`, quatre rapports TRX dans `audit-results/` et `audit-source-manifest.json`. Ces fichiers appartiennent à l'audit et ne contiennent pas de journaux de sessions de jeu.

Prochaine priorité de validation : résoudre/revoir la récupération après quarantaine C02, stabiliser les tests concurrents sans réduire leurs garanties, puis tester en session une séquence de combats et une reprise après défaite. Cette photographie ne doit pas être déployée automatiquement sur les bots actifs.
