# N09 — Audit lecture seule : chasse, expédition, donjon

**4 novembre 2026 — inspection de code et de tests uniquement.** Aucun déplacement, combat, session, PNJ, inventaire ou commande en jeu. Le résultat ci-dessous sépare les constats de code **reproductibles par lecture** et les risques de runtime non établis par une trace serveur.

## Synthèse des findings

| Ref | Priorité | Constat | Portée exacte |
| --- | --- | --- | --- |
| N09-F01 | P1 à tester | La gestion de défaite dans `ExpeditionScript.Build` ne consulte `lastFightLost()` **que si** `fight()` retourne `true`. Si `fight()` rend `false` malgré un nouvel état `lost=true`, `onDefeat()` est contourné (boucle single-map et multi-map, ainsi que donjon) | **Branche statique prouvée**, mais la sémantique normale de `fight()` est « combat terminé → true », donc **pas un bug live démontré**. Tester le cas timeout/refus/issue tardive et la fraîcheur `fightId/Seq` avant correction |
| N09-F02 | P1 à tester | `TreasureScript.giveUp` appelle `treasure:quit()` puis remet `fails, candidate, lastKey, walked` à zéro et lit un cooldown **sans consulter le booléen du quit**. Or le wrapper `GivreLuaHost.SnowTreasure.quit` renvoie explicitement `false` si l'abandon n'est pas confirmé | **Décalage de contrôle démontré** dans le script. Le serveur peut conserver la chasse, et le prochain cycle repart avec des compteurs locaux remis à zéro ; dommage concret et cadence restent à reproduire hors jeu |
| N09-F03 | P2 / limite documentée | En donjon, si aucune clé locale n'est trouvée, `hasKeys` journalise l'absence puis retourne `true` **intentionnellement**, car un trousseau peut permettre l'entrée. L'entrée PNJ/porte est explicitement rendue à l'humain quand `travelTo(ROOMS[1])` échoue | **Pas une vulnérabilité prouvée** : exiger un événement de refus de salle/PNJ avec origine plutôt que « absence de clé » seule |

## Protections présentes à ne pas casser

- **Défaite** : `ContinueAfterDeath` existe et `onDefeat()` arrête par défaut, ou continue avec compteur; tests `ExpeditionCompletionRegressionTests:80-112` (issues confirmées, séries pertes/victoires). Aucune routine Phénix n'est appelée par `ExpeditionScript`. Il existe un **ancien API de script** `RunScriptPhenix` ailleurs (`GivreLuaHost.SnowScript.cs`), mais sa présence ne prouve **pas** son invocation par la boucle expédition moderne.
- **Route restrictive** : `ExpeditionScript:342-346` traite les échecs `travelTo` avec un `travelFailures` borné à `2 * nombre de cartes` ; si la zone est inaccessible, le script s'arrête au lieu de répéter indéfiniment. `LaunchService.Movement.cs` conserve compteur/refus et recherche de case de transition atteignable ; l'accord serveur reste à vérifier.
- **Inventaire plein** : `ExpeditionScript:313-315,328,354` utilise seuil pods ou renvoi au moteur `bank()` selon option ; en donjon, `depositAtBank` refuse explicitement un dépôt non confirmé `:403-409`, avec test existant `ExpeditionCompletionRegressionTests`. Manque à vérifier : `STOP_PODS=0` pour l'expédition avec banque implique sortie par le chemin de dépôt, pas par la simple condition de fin.
- **Dialogue / entrée** : `ExpeditionScript:444-448` met en garde et temporise si l'entrée doit être faite à la main ; `npc:npcBank()` peut refuser et bloque le `depositAtBank` du donjon. Aucun dialogue PNJ réel observé.
- **Chasse** : ``treasure:startHunt` refuse une tranche inaccessible sans envoi : `GivreLuaHost.SnowTreasure.cs:110-138` ; trois refus consécutifs arrêtent le script sans abandon, couverts dans `TreasureScriptExecutionTests:163-184` ; cooldown exempt de ce décompte `:186-200`. `TreasureScript:79-82,104-111,134-142` arrête/conserve la chasse si origine inconnue ou trajet impossible.

## Réduction du risque et collecte minimale

Pour N09-F01 : fixture de Lua `fight()=false` suivi de `combat.lastFightResult={lost=true,fightId=newId}`, une autre `fight()=true` avec issue inconnue, et un test de résultat **stale** avec même `fightId` ; vérifier si ces états peuvent réellement être obtenus dans le code host sans timeout actif. Pour N09-F02 : `quit()=false`, `isHunting()=true`, cooldown négatif/positif, deux passages successifs `move()` ; constater la remise à zéro prématurée et établir un mécanisme de reprise sûr sans répétition destructive.

**Pas de test exécuté**, pas de preuve de dommages utilisateur ni de changement de source. Priorisation à revalider sur données de logs locales anonymisées.
