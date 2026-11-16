# P02 — Buffs, initiative et expiration

Date : 2026-11-04. Statut : **chronologies candidates hors jeu, aucune expiration serveur mesurée**. Source : effets des sorts N01, DOFUS 3.7.13.17. Le point nouveau est la **jointure chiffrée des durées/délais/masks avec un modèle d'événements aux frontières de tour** ; les durées n'autorisent toujours pas une convention d'expiration inventée.

## Champs réellement trouvés, grade 1, branche normale

| Famille candidate | Sort, ID | `actionId`, `effectUid` | Donnée client vérifiée | Limite |
| --- | --- | --- | --- | --- |
| PA immédiat | Éliotrope **Coalition** 14621 | 111, 226301 / 226269 | `duration=2,delay=0`, `diceNum=1/2`, masques `a,r` / `a,R` | Distinction des deux masques et expiration non prouvées |
| PA différé | Xélor **Flou Temporel** 13246 | 111, 210099 | `duration=1,delay=1,diceNum=2`, masque `a,A` | Le tic auquel s'applique le retard est Unknown |
| PA direct, un tour | Xélor **Conservation** 13291 | 111, 402799 | `duration=1,delay=0,diceNum=1`, masque `a` | Début/fin du tour propriétaire non identifié |
| PM candidat | Sram **Invisibilité** 12913 | 128, 208233 | `duration=2,delay=0,diceNum=1`, masque `a` | Éligibilité du bénéficiaire et disparition exacte Unknown |
| Puissance | Éliotrope **Cabale** 14617 | 138, 404718 / 404719 | `duration=2,delay=0,diceNum=50/100`, masques `a,r` / `a,R` | Ne pas additionner les deux lignes sans résoudre le masque |
| Puissance | Sram **Truanderie** 12902 | 138, 371825 | `duration=3,diceNum=20`, masque `C` (cible liée au lanceur) | Ne pas prétendre transférer à un allié quelconque |
| Bouclier | Roublard **Roublardise** 13431 | 1020, 259838 | `duration=1,delay=0,diceNum=150`, masque `a,P,F3112,F3113,F3114,F5161,F5163,F5162` | Le code calcule PB depuis niveau du lanceur, pas expiry |
| Réduction/état candidat | Roublard **Rémission** 13445 | 950, 259886 / 260133 | même `value=2512`, `duration=3` sur un masque / `1` sur l'autre | **La signification de l'état 2512 n'est pas prouvée**, donc pas d'oracle de réduction |
| PA immédiat sans durée | Éliotrope **Neutral** 14582 | 120, 313020 | `duration=0,delay=0,diceNum=1`, masque `C` | `duration=0` ne prouve pas « expire fin de tour » |

Les `effectUid` relient directement ce tableau aux lignes de `N01/EFFECTS_3.7.13.17.csv`. **Rémission** montre aussi une permutation d'ordre d'effets normal/critique (P04) : il faut comparer par masque + action + durée, pas par index seul.

## Dix chronologies réutilisables S→B et B→S

Notation : **S** est le lanceur, **B** le bénéficiaire si son éligibilité cible est validée ; `start(X,n)` et `end(X,n)` sont les frontières de tour de l'acteur X au round n. Dans chaque paire, le seul facteur modifié est l'ordre d'initiative. Le paquet confirmé, la variation de ressource et le signal de retrait de buff seront nécessaires à une vraie validation. Voir [chronologies.json](chronologies.json) pour les événements ordonnés.

| Cas | Chronologie initiale (événements ordonnés) | Champ client | Attendu aux frontières **vérifiées** |
| --- | --- | --- | --- |
| P02-01 PA S→B | `start(S,1) → cast(14621) → buff? → end(S,1) → start(B,1) → end(B,1) → start(S,2)` | 111/durée 2 | Aucun front de retrait validé : `Unknown` à chacune des frontières |
| P02-02 PA B→S | `start(B,1) → end(B,1) → start(S,1) → cast(14621) → end(S,1) → start(B,2)` | 111/durée 2 | Ne pas accorder PA rétroactivement au B déjà passé |
| P02-03 PM S→B | `start(S,1) → cast(12913) → end(S,1) → start(B,1) → end(B,1) → start(B,2)` | 128/durée 2 | Le bonus candidat ne devient certifié qu'avec delta PM serveur sur B |
| P02-04 PM B→S | `start(B,1) → end(B,1) → start(S,1) → cast(12913) → start(B,2) → end(B,2)` | 128/durée 2 | Expiration avant/après B2 : `Unknown` |
| P02-05 puissance S→B | `start(S,1) → cast(14617) → end(S,1) → start(B,1) → castDamage(B) → end(B,1)` | 138/durée 2 | Damage delta non prouvé : éviter tout score de buff garanti |
| P02-06 puissance B→S | `start(B,1) → castDamage(B) → start(S,1) → cast(14617) → start(B,2)` | 138/durée 2 | Le cast de B1 précède le buff : pas de gain attribuable |
| P02-07 PB S→B | `start(S,1) → cast(13431) → end(S,1) → start(B,1) → incomingDamage → end(B,1)` | 1020/durée 1 | PB gagné/perdu et date retrait exigent trois états serveur distincts |
| P02-08 PB B→S | `start(B,1) → end(B,1) → start(S,1) → cast(13431) → start(B,2) → incomingDamage` | 1020/durée 1 | Visibilité/protection du PB à B2 : `Unknown` |
| P02-09 réduction S→B | `start(S,1) → cast(13445) → end(S,1) → start(B,1) → damageTaken → end(B,1)` | 950/état2512, durée 1 **ou 3** | Identifier d'abord masque/target et l'effet 2512, sinon `Unknown` |
| P02-10 réduction B→S | `start(B,1) → end(B,1) → start(S,1) → cast(13445) → start(B,2) → damageTaken` | 950/état2512, durée 1 **ou 3** | Hypothèse de réduction non certifiée ; effet et expire restent `Unknown` |

Les lignes de table expriment des **oracles de cohérence indépendants de la règle exacte** (pas de rétroactivité, aucune dépense sans confirmation, aucun retrait déduit d'un simple délai). Aucun des dix scénarios n'a une frontière d'expiration **observée** : le champ `serverOracle` reste `Unknown`.

## Durée zéro, immédiat, différé et cause d'expiration

`SpellResolver.cs:62-74` précise que les effets avec `Delay>0` ou `Triggers` non immédiats sont **retenus sans exécution** et que les durées/expirations demandent un simulateur de tours. `SpellResolver.cs:395-411` modifie actuellement PA/PM ou des états **sans gérer une horloge d'expiration**. Un `duration=0` peut coder un effet instantané, un comportement spécial ou autre : seule la transition serveur permet de décider. Une durée négative, repérée pour certaines poses d'état de N01 (par exemple Double 12915/action 950, -1), doit rester valeur brute sans supposer l'infini.

Pour départager les modèles : observer dans le **même combat + epoch + round + actorId** (1) cast reçu, (2) nouveau buff/valeur de ressource, (3) fin du tour S, (4) début/fin du tour B, (5) début du tour S suivant, (6) événement de retrait et snapshot après celui-ci. Répéter avec initiative inversée et, pour `Flou Temporel`, capturer `delay=1` et le premier delta réellement reçu. Tant que ces éléments manquent, aucun planificateur ne devrait valider un bénéfice de buff qui expire potentiellement avant son usage.

**Livré :** dix chronologies documentées, exemples exacts à effets et masques tracés. **Non livré :** convention d'expiration prouvée, tests offline exécutés ou observation en jeu.
