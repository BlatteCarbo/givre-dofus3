# MZP22 — Audit adversarial des fonctions Lua du clone local

**16 octobre 2026 — worker-2 — lecture et tests de sources hors ligne uniquement.**

## R — Résultat

Le harnais recoupe **14 fonctions explicitement annoncées** dans `docs/portal/lua-api-doc.json` avec les inscriptions de callbacks du code reconstitué. **10/14 échouent à la parité fonctionnelle de la surface source** : leur binding renvoie intentionnellement une valeur constante, une valeur neutre ou un refus annoncé. Les **4 autres disposent d'un chemin d'appel** vers les services de l'application ; ni leur réussite en jeu, ni leur équivalence avec le binaire natif Givre v1.3.1 ne sont mesurées.

Ce résultat distingue trois niveaux : (1) **PASS** pour les 13 tests de diagnostic statique, (2) **FAIL** pour les fonctions liées à un stub ou retour constant, (3) **UNTESTED** pour le résultat fonctionnel réel de chaque API et sa parité binaire. Un callback présent ne constitue ni une preuve d'envoi gameplay ni une réponse serveur.

## C — Fichiers ajoutés

- `tools/MZP22FeatureParityHarness/audit.py` : extraction bornée des inscriptions `Fill(...)`, `table["name"]` et `foreach` + vérification du corps des helpers `M`, `NotYet`, `Stub`, `Neutral`, avec conservation des lignes et empreintes SHA-256 ; lecture seule de quatre fichiers locaux.
- `tools/MZP22FeatureParityHarness/test_audit.py` : tests unitaires normaux et adversariaux (bindings absents, faux bindings commentés, chaîne `https://`, `foreach` avec nom absent, helper neutralisant remplacé, expression de lecture dynamique, documentation tronquée, frontière ACK).
- Ce rapport. Aucun projet applicatif, configuration, compte, instance Givre, client de jeu, EXE ou service n'a été modifié.

## Matrice de parité constatée dans les sources

`FAIL_STUB` : la fonction existe mais annonce une indisponibilité et retourne un résultat neutre. `FAIL_CONSTANT` : la fonction annonce son existence mais retourne une constante, sans lecture de données. `SOURCE_PATH_PRESENT` : chemin C# non constant présent, sans test métier ou authentification serveur. Les lignes sont celles des fichiers indiqués dans la colonne preuve.

| API annoncée | Preuve source reconstruite | Audit source | Exécution/parité v1.3.1 |
| --- | --- | --- | --- |
| `quest:objectiveText` | `SnowNpc.cs:171`, toujours `Str("")` | **FAIL_CONSTANT** | UNTESTED |
| `quest:path` | `SnowNpc.cs:173`, avertissement puis `nil` | **FAIL_STUB** | UNTESTED |
| `character:accountId` | `SnowNpc.cs:204`, toujours `-1` | **FAIL_CONSTANT** | UNTESTED |
| `character:getSpellMinPlayerLevel` | `SnowNpc.cs:202`, toujours `-1` | **FAIL_CONSTANT** | UNTESTED |
| `mount:toggleRiding` | `SnowNpc.cs:215-218`, helper `M` + retour `false` | **FAIL_STUB** | UNTESTED |
| `mount:getEnergy` | `SnowNpc.cs:215,219`, helper `M` + retour `-1` | **FAIL_STUB** | UNTESTED |
| `sale:buyToPnj` | `SnowEconomy.cs`, `_launch.NpcBuyAsync` (boutique PNJ ouverte) | **SOURCE_PATH_PRESENT** | UNTESTED |
| `sale:sellToPnj` | `SnowEconomy.cs`, `_launch.NpcSellAsync` (boutique PNJ ouverte) | **SOURCE_PATH_PRESENT** | UNTESTED |
| `merchant:buyItem` | `SnowEconomy.cs:213-215`, helper `Stub` + `false` | **FAIL_STUB** | UNTESTED |
| `fightSlave:castSpellOnCell` | `SnowMap.cs:465-474`, helper `Neutral` + `false` | **FAIL_STUB** | UNTESTED |
| `quest:initialize` | `SnowNpc.cs:156-165`, lecture `_launch.QuestsLoaded` | SOURCE_PATH_PRESENT | UNTESTED |
| `npc:replyByIdAndChangeMap` | `SnowNpc.cs:100-109`, `_launch.NpcReplyById` et attente de carte | SOURCE_PATH_PRESENT | UNTESTED |
| `sale:AvailableSpace` | `SnowEconomy.cs:159`, lecture `_launch.HdvFreeSlots` | SOURCE_PATH_PRESENT | UNTESTED |
| `craft:isOpen` | `SnowEconomy.cs:205`, lecture `_launch.CraftOpen` | SOURCE_PATH_PRESENT | UNTESTED |

Tous ces noms figurent dans le manifeste documentaire `docs/portal/lua-api-doc.json` (catégories/méthodes/signatures). Il s'agit de **14 échantillons ciblés**, et non d'un taux de parité global des centaines de méthodes du clone. Les quatre `SOURCE_PATH_PRESENT` attestent exclusivement l'existence de références C#, pas le succès d'une commande, sa provenance, ni la fidélité de l'API en exécution.

Les causes concrètes visibles : `mount` indique qu'il manque encore les messages de monture ; `sale:buyToPnj` et `sale:sellToPnj` passent désormais par l'achat / la vente PNJ du moteur ; `merchant` signale une absence d'équivalent ; `fightSlave` n'est pas piloté ; la lecture de texte d'objectif de quête et certaines informations de personnage n'ont pas de fournisseur. Une fonction qui retourne `false` peut être conçue pour signaler l'échec, mais ici **l'implémentation même** est un stub constant.

## V — Validation reproductible

Depuis la racine `GivreShareWork` :

```powershell
py -3 -B tools/MZP22FeatureParityHarness/test_audit.py
py -3 -B tools/MZP22FeatureParityHarness/audit.py
```

**13/13 tests unitaires PASS** ; le scanner a classé **10 FAIL source + 4 SOURCE_PATH_PRESENT**, avec **14/14 inscriptions documentées**, et est sorti avec code `0` parce que toutes les classifications attendues ont été retrouvées. Le code `0` signifie que l'inventaire est cohérent, **pas** que les 10 fonctions fonctionnent. Un enregistrement de binding disparu, un faux nom injecté dans un commentaire, une réécriture du helper qui ne renvoie plus le neutre, ou une modification de la documentation déclenche une classification différente et demande une nouvelle revue.

SHA-256 des preuves utilisées :

| Source | SHA-256 |
| --- | --- |
| `GivreLuaHost.SnowNpc.cs` | `9af1389f603bb4d1f25b9a1d6b7020b1fac21156903516a430cbb997b7883ba4` |
| `GivreLuaHost.SnowEconomy.cs` | `72012051a3b7a43a99e48d72b7151ce4305aed12d982d00af2ca3a492ef40a75` |
| `GivreLuaHost.SnowMap.cs` | `34fef85a94af834b4791f14b9d4c81f38eda830496c04a989578bc3bd2c2cc44` |
| `docs/portal/lua-api-doc.json` | `f6ccde833278dbcb92393e68318f6bedca4670b4b35043040cf693dccb9f8ec8` |

Les tests contrôlent des chemins de source C# et leurs séquences visibles. Ils n'exécutent pas MoonSharp, n'effectuent aucun protocole Dofus 3.7.11.11 et ne prétendent pas reproduire intégralement le compilateur C#. La classification reste prudente : une modification inattendue devient `REVIEW` ; aucun nouveau résultat n'est automatiquement déclaré fonctionnel.

## B — Blocages et priorités d'intégration

1. **Montures / marchands / PNJ** : rattacher les callbacks à des lectures, identifiants et échanges de requêtes réellement extraits et testés pour la release ; avant cela conserver les retours explicites `false`/`unknown` et la mention de fonctionnalité indisponible.
2. **Quêtes et personnage** : récupérer le fournisseur correct pour `objectiveText`, `accountId`, `getSpellMinPlayerLevel` avec distinction valeur inconnue et valeur valide ; éviter de présenter un champ vide comme une vraie observation de jeu.
3. **Combat piloté `fightSlave`** : nécessite un contexte de tour authentifié et une confirmation de la résolution d'action. Le code `Neutral` prouve que la méthode est une promesse d'API non tenue à ce stade.
4. **Validation réelle** : le code local startup du lot MZP19 ouvre le shell, mais MZP20 ne dispose pas d'un lancement Dofus TEST indépendant et attribué ; MZP21 ne certifie aucun ACK de gameplay. Sans ces prérequis, aucune déclaration « clone v1.3.1 opérationnel » ne peut être déduite des tests hors ligne.

Conformément au périmètre, aucun binaire ni processus Givre d'origine n'a été ouvert ou manipulé.
