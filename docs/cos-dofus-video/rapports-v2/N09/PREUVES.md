# N09 — Index exact des sources lues

Les chemins sont relatifs au worktree source `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`, **tous en lecture seule**.

| Référence | Réalité constatée |
| --- | --- |
| `GivreClone.App/Services/ExpeditionScript.cs:270-281` | `onDefeat()` continue ou arrête selon l'option ; commentaire précise « plus de Phénix » pour l'expédition actuelle |
| `GivreClone.App/Services/ExpeditionScript.cs:320-323,350-358` | `lastFightLost` s'appuie sur `combat:lastFightResult()`, mais est appelé dans la branche `elseif fight()` (single-map) ou après sortie de `if not fight() then break` |
| `GivreClone.App/Services/ExpeditionScript.cs:451-468` | Donjon : lecture `before`, appel `fight()`, traitement du résultat `lost`/**won** seulement si `fight()` vrai ; après échec, tentative de sortie/salle suivante sur `idle` |
| `GivreClone.App/Services/GivreLuaHost.Combat.cs:97-135` | `fight()` rend `false` si pas de cible, refus/échec d'engagement, timeout de départ ou timeout d'attente de fin ; en cas de combat terminé et retour en mode carte, rend `true` |
| `GivreClone.Tests/ExpeditionCompletionRegressionTests.cs:80-112` | Tests démontrent arrêt/reprise sur `lost` avec `fight()=true`, pas l'oracle `false` + nouvelle issue perdue |
| `GivreClone.App/Services/ExpeditionScript.cs:313-315,324-335,342-360` | Seuil pods, mode banque, limite combats/temps, échec de trajet borné, boucle expédition |
| `GivreClone.App/Services/ExpeditionScript.cs:399-409,419-448` | Dépôt banque avec confirmation et cas de PNJ absent ; trousseau de clés et entrée non automatisable reconnues |
| `GivreClone.Tests/ExpeditionLoopRegressionTests.cs:34-87` | Scénarios map unique, échec/repousse, pods, anomalie, temps et limite combats |
| `GivreClone.Tests/TreasureRunControlTests.cs:19-54,109-167` | Stop du bon script, refus et nouvel essai d'arrêt, sans confondre ordre et succès |
| `GivreClone.App/Services/Treasure/TreasureScript.cs:32-44` | `giveUp` appelle `treasure:quit()` sans tester `true/false` et réinitialise la machine locale |
| `GivreClone.App/Services/GivreLuaHost.SnowTreasure.cs:249-275` | L'abandon est négocié avec `TreasureFinishedSeq` / `LastTextInfo` ; wrapper retourne `false` en cas de refus/timeout et expose le cooldown |
| `GivreClone.App/Services/Treasure/TreasureScript.cs:52-66,76-150` | Refus de départ limités ; étapes (creusage, direction, phorreur, indice) et politique de préservation de chasse |
| `GivreClone.Tests/TreasureScriptExecutionTests.cs:163-200` | Trois refus de chasse sans abandon et cooldown traités par tests (pas `quit()==false`) |
| `GivreClone.App/Services/GivreLuaHost.SnowScript.cs:80-124` | Fonction `RunScriptPhenix` distincte et historique dans l'hôte générique ; ne pas la confondre avec un flux `ExpeditionScript` modernisé |

Les séquences listées sont celles de **code/test inspecté**, pas des événements de journal horodatés, donc aucun statut `valide-test`, `valide-live` ou `cause incident serveur prouvée`.
