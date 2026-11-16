# N09 — Handoff de vérification, aucune modification prod

### P1 — Défaite et retour de `fight()`

Écrire **en dehors de cette mission** un test pur sur la génération `ExpeditionScript.Build` et `BuildDungeon` : injecter un résultat `{lost=true,fightId=nouveau}` dans chacun des chemins `fight()=false` et `fight()=true` ; en faire de même avec résultat obsolète, avec et sans `ContinueAfterDeath`. L'oracle ne peut pas être « `fight()==false` signifie une défaite », puisqu'il peut aussi désigner une carte sans cible ou un échec de start : la décision doit être conditionnée à un résultat serveur **récent** `Seq/fightId` confirmé. Conserver timeout `Unknown`, pas de boucle destructive.

### P1 — Abandon de chasse confirmé

Préparer deux fixtures `TreasureScript.Build` : `treasure:quit()=true` avec changement d'état `isHunting()→false`, et `quit()=false` avec chasse maintenue `isHunting()=true`. Vérifier qu'aucun état local de chasse n'est réinitialisé **comme si** l'abandon était établi dans la deuxième fixture. Conserver la limite de refus, le cooldown et l'option `preserveHunt`.

### P2 — Dialogue, pleine charge, carte restrictive

Étendre le corpus existant au cas `npc:npcBank()=false`, `exchange:putAllItems()=false` et `getPodsPercent()≥STOP_PODS` après dépôt ; ajouter une carte avec `travelTo==false` sur deux circuits et un PNJ de donjon inaccessible. Lire l'événement serveur refusant l'entrée, au lieu de décréter que le manque d'une clé locale empêche le trousseau. À énergie 0 ne pas appeler Phénix à partir de ces scripts modernes ; vérifier les appels du host avant de changer son éventuelle fonction historique.

**Priorité de vérification :** les risques N09-F01/F02 sont des déductions de branches de code, **pas des défauts observés en jeu**. Ne pas éditer le code au titre de cette mission documentaire ; demander fixture, logs anonymisés et preuve causale avant correctif.
