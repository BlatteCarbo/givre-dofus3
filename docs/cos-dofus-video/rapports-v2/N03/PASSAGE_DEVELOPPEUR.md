# N03 — Passage développeur

**Constat mesuré** : une recherche synthétique de trois sorts/deux ennemis atteint 64,898 ms médian / 149,118 ms maximum sur douze passages mesurés, mais **sur DLL du 3 novembre**, pas sur la source plus récente du 4 novembre. Le budget n'a été épuisé par aucun cas ; pas de conclusion sur les incidents de combat plus lents.

1. Capturer un snapshot de plan réel **sans données identifiantes** incluant cases, acteurs, sorts versionnés, initiative et toute entrée de `CombatTurnSnapshot`. Veiller à ne pas masquer les distinctions entre personnage et invocation.
2. Compiler le code à auditer dans un **worktree isolé**, associé à son SHA source, pour ne pas tester un binaire stale.
3. Réexécuter [bench/Program.cs](bench/Program.cs) ou un harness équivalent avec au moins un cas connu rapide, un long, un cas sans action et un cas support, en enregistrant warmup, steady, P95/P99, `Work`, `BudgetExhausted`, actions et score.
4. Vérifier **l'égalité des décisions et l'absence de commandes risquées**, pas seulement la baisse de latence. Ne pas augmenter brutalement le budget sans mesurer le coût adversarial. Le compteur `Work` n'est pas un nombre de nœuds au sens classique : il inclut reachability, casts et réponses adverses.

**Aucun patch de production, aucune comparaison versionnée de performance, aucune mesure live.**
