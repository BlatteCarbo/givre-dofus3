# GivreClone — incrément de continuation, 13 novembre 2026

## Base et discipline

- Racine sélectionnée : `%USERPROFILE%/.t3/worktrees/GivreWork` (agrégateur de worktrees, **pas** un dépôt Git).
- Dépôt continué : `fix-f01-f16-final-20261024`, HEAD observé `b4d28a8` ; plus de 500 entrées dirty préexistantes **préservées**.
- Sous-sources voisines : `GivreProxy/GivreCore` pour le transport TCP permanent ; aucun fichier généré `obj/local-deps` modifié.
- Validation **hors jeu** : pas de client DOFUS, pas de connexion de jeu, pas d'ACK de combat réel, pas de MizanBot ou processus tiers manipulé.
- Référence des mécaniques prouvées : client DOFUS 3.7.13.17 / `docs/validation/roublard-bomb-contract-20261105.md`. Ne pas inventer d'autres bombes, multiplicateurs ou passifs.

## Incréments implémentés

### Combat et cartes

1. **Planificateur Roublard — identité bombes**. `CombatTurnPlanner.ClassifyRogueCombatants` filtre uniquement les bombes Explobombe template `3112` dont l'acteur est allié, vivant, à position confirmée, propriétaire du lanceur, stable entre snapshot serveur et résolution. Les monstres ennemis `2831..2836` ne sont plus pris pour des bombes ; les invocations simulées ne deviennent pas des preuves de mur. Nouveau `CombatRoguePlannerClassificationTests.cs`.
2. **Clic droit multibot — origine de la sélection**. `IsoMapControl` capture le bot, PID, carte, époque, version de sélection, génération monde, liste visible et identité de l'acteur lors de l'ouverture du menu. `BotsViewModel` revérifie au clic ; A→B→A, liste recyclée, cible déplacée/disparue ou carte périmée échouent fermés. Le bouton direct MapInspector conserve sa propre action. `MapAttackMenuProvenanceTests.cs` : tests synthétiques.
3. **Dispatch Attaquer — dernière fenêtre de concurrence**. `MonsterAttackTargetLease.TrySubmitFromConfirmedCell` prend le même verrou `ActorPublicationGate` que le store serveur lors du contrôle final **et** de l'injection. `LaunchService.AttackMonster` exige ce callback et le partage entre UI et Lua, directement ou après déplacement. Un groupe remplacé avant la section critique ne provoque pas d'envoi. Cela ne prouve pas qu'un serveur acceptera le combat.
4. **Modificateurs de sorts tardifs**. `LaunchService.Combat.SpellMods.cs` conserve un modificateur candidat si l'événement ApplySpellModifierEvent arrive après la barrière ; refuse les overlaps de deux casts du même acteur, changement de tour/acteur/fight, branches conditionnelles non déterminables, et nettoie la quarantaine au combat suivant. `LaunchService.Combat.Turn.cs` relit également `SpellModifierSet.Version` aux frontières de décision (rotation classique, casts répétés, repli offensif, coffre, budgets de portée) et reconstruit les fenêtres des objets `RotationSpell` sans changer la liste en cours d'itération. Nouveau `CombatSpellModifiersLateTests.cs`.

### Fiabilité moteur et économie

5. **Sessions TCP / Host**. `BotHost.Stop` détache le binder AVANT l'arrêt du proxy, `Start` ré-attache un epoch neuf. `BotOrchestrator.NotifyConnectionClosed(connectionId)` retire l'identité exacte. La source permanente `GivreProxy/GivreCore/Mitm/TcpProxyServer.cs` émet désormais `ConnectionClosed(int)` **une seule fois** après suppression/fermeture du lien, même si `Stop` et `finally` se croisent. L'orchestrateur s'abonne au signal typé lorsqu'il est disponible, et n'infère jamais de fermeture à partir du PID ou des logs. `Directory.Build.targets` préfère la source voisine lorsqu'elle existe ; la copie embarquée reste le fallback hermétique, sans événement de close individuel. Test : `BotHostConnectionCloseLifecycleTests.cs`.
6. **Budget taxes HDV**. Les ventes en lot réservent leurs taxes estimées sans soustraire les mises à jour d'un compteur `TotalTax` partagé avec d'autres opérations. Sans reçu attribué à l'UID/ordre, la réservation reste prudente même après ACK ambigu. `HdvTaxBudgetSafetyTests.cs` vérifie reçus extérieurs, overflow et configurations négatives ; 0 reste la valeur historiquement « illimitée ».

## Réserves significatives

- **Roublard** : la reconnaissance `3112` n'est PAS une IA complète de murs/charges/boost/poussée/détonation. `VerifyObservedCombo` continue d'exiger un événement indépendant `DetonationConfirmed` corrélé ; aucune trame existante ne le synthétise à partir d'un dommage `13455/action99`.
- **Éliotrope/Xélor/Féca/Écaflip/Sacrieur** : contrats de preuves de séquences hors jeu, pas encore orchestrations live multi-tours.
- **Ciblage** : `MapActor` ne contient pas d'instance de groupe serveur séparée de contextualId ; malgré le verrou, un changement après l'injection et avant traitement serveur est hors garantie.
- **TCP** : l'événement de fermeture se déclenche sur le proxy issu de la source permanente présente sur ce PC ; un clone autonome qui utilise l'ancienne source embarquée ne peut pas certifier un événement qu'elle ne publie pas. `BotHost.Stop` détache toujours le binder.
- **SpellMods** : le fil de packets n'expose qu'`actor_id`, pas l'ID de cast causal ; deux casts non confirmés restent définitivement ambigus durant ce combat. Les principaux points de décision relisent maintenant la version de modificateurs, mais une émission exactement concurrente à la dernière vérification n'est pas une garantie d'ACK. La preuve d'origine du paquet par cast reste à bâtir.
- **HDV** : pas de garantie mathématique sur taxes réelles tant que taux/arrondi et reçus serveur ne sont pas prouvés. Le compteur réservé peut être sur-conservateur.
- **Pathfinding** : un worker dédié n'a pas pu démarrer ; aucun gain de performance A* revendiqué.

## Tests et statut de la vague

Les suites ont été exécutées **séquentiellement hors jeu**, sans client ni port de test ouvert.

| Contrôle | Résultat exact | Preuve |
| --- | --- | --- |
| Modificateurs de sorts et intégration Transcendance | **12/12 PASS**, après correction de la fixture de personnage réellement présent dans `_fighters` | `docs/codex/validation-20261113-spellmods-fixture.log` |
| Nouvelles régressions combat, UI, sessions, HDV et historique | **263/263 PASS** | `docs/codex/validation-20261113-wave-continue-targeted-final.log` |
| Fermeture TCP individuelle, arrêt concurrent et reprise host | **5/5 PASS** | `docs/codex/validation-20261113-tcp-close-contract.log` |
| Suite complète `GivreClone.Tests` hors tests dont le nom contient `Live` | **5 810 PASS / 2 ignorés / 0 échec**, total 5 812 | `docs/codex/validation-20261113-wave-continue-full-nonlive.log`, exit 0 |
| PureTests | **362/362 PASS** | `docs/codex/validation-20261113-wave-continue-puretests.log`, exit 0 |
| Comparateur de décisions shadow | **65 assertions PASS** | `docs/codex/validation-20261113-wave-continue-shadow.log`, exit 0 ; hors jeu |

La compilation initiale a échoué une fois parce que l'override `Directory.Build.targets` sélectionnait la source figée `obj/local-deps/GivreCore` sans le nouveau `ConnectionClosed`. Le fichier de build préfère désormais **la source permanente voisine quand elle existe** et conserve le repli embarqué pour les clones autonomes ; l'abonnement à l'événement dans `BotOrchestrator` reste optionnel et typé, donc le repli ne se fait pas passer pour une preuve de fermeture. Après ce correctif, compilation et suites vertes.

Le premier lot ciblé après intégration donnait **109/114 PASS** : les cinq échecs étaient tous dans des tests synthétiques de `CombatSpellModifiersLateTests`. Leur fixture avait enregistré `_charId` mais aucun combattant correspondant dans `_fighters`; `SelfActorId` refusait ainsi à juste titre l'actor-only event. Ajouter l'acteur à la liste confirmée du combat a restauré les cinq positives sans relâcher les gardes ; les 12 tests ciblés puis les 5 812 cas généraux ont passé.

## Prochain raccordement utile

1. Étendre la revalidation des règles de sorts à un éventuel événement serveur survenant **pendant** une recherche de cible/ligne de vue déjà engagée, idéalement via une identité/epoch de cast ; ne jamais réattribuer un actor-only event ambigu.
2. Relier les preuves serveur de murs/portails/Téléfrag/états à `VerifyObservedCombo` et au planner ; ajouter des replays positifs et négatifs versionnés.
3. Relier taxes HDV à des reçus attribués aux actions précises et lever les réservations seulement après preuve.
4. Profiler navigation multi-cibles et économie/forge sans modifier règles protocole ni générer des prix fictifs.
5. Sérialiser la publication d'événements dans `MZP1GuardedEngineWiring.TryDispatchCaptured` : la mise à jour de `GameState` se fait sous `_gate` mais la publication `PacketBus.Publish` est hors verrou et deux captures simultanées peuvent se publier dans le mauvais ordre ; une file ordonnée et révocable est à concevoir/tester sans exécuter les callbacks sous `_gate`.
