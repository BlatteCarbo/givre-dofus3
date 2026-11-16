# Worker 6 — Lua PNJ, économie, montures, quêtes et combat

## RESULT

Périmètre implémenté et validé hors ligne dans `GivreWork-Integration-20261020`.
Les méthodes `sale:buyToPnj` et `sale:sellToPnj` de la branche Opus existaient déjà ;
elles utilisent respectivement `LaunchService.NpcBuyAsync` et `NpcSellAsync`,
avec accusés `ExchangeBoughtEvent` et `ExchangeSoldEvent`.

L'état des quêtes vient d'un snapshot de connexion et des événements de quête.
`QuestListRequest` n'a pas de fournisseur disponible. Le moteur observe les
invocations, mais ne sait pas sélectionner/piloter une invocation contrôlée.
Les commandes et états de monture ainsi que le mode marchand joueur sont indisponibles.

## CHANGES

- `GivreClone.App/Services/GivreLuaHost.SnowNpc.cs` : `npc:npc(id, 1)`
  n'annonce l'ouverture de boutique que si `ShopOpenAsync` confirme
  `ExchangeNpcShopStartedEvent` ; `quest:status()` et `mount:status()` donnent
  l'état des fournisseurs et une raison d'indisponibilité explicite.
- `GivreClone.App/Services/GivreLuaHost.SnowEconomy.cs` :
  `sale:buyToPnj` et `sellToPnj` refusent les quantités explicites
  nulles/négatives avant envoi ; la vente retourne `true` uniquement si
  toute la quantité est confirmée ; le bilan de `sellAllItemToPnj`
  compte les ventes partielles comme échecs ; `merchant:status()` distingue
  mode marchand joueur historique et boutique PNJ.
- `GivreClone.App/Services/GivreLuaHost.SnowMap.cs` :
  `fightSlave:status()` renvoie `available=false`, la raison exacte et
  `observedOwnSummons` calculé depuis `FightersDetail` (lecture seule).
- `GivreClone.Tests/LuaUnimplementedApisTests.cs` : 9 cas de parité
  documentation/source et de validation des garde-fous.
- `docs/portal/lua-api-doc.json` et
  `docs/portal/lua-api-portal-v2.0.1.txt` : contrats et limites synchronisés.

Les noms et valeurs sentinelles Lua existants sont conservés. Aucun paquet
protocolaire n'a été inventé. Aucun service moteur hors périmètre n'a été modifié.

## VALIDATION

- `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~LuaUnimplementedApisTests` : **9 PASS, 0 FAIL**.
- `dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore` :
  **PASS, 0 erreur** (avertissements existants hors périmètre).
- `docs/portal/lua-api-doc.json` : parsing `ConvertFrom-Json` **PASS**.
- `git diff --check` : **PASS**.

## BLOCKERS

- Validation uniquement hors ligne ; aucun accusé LIVE généré dans cette passe.
- `mount`, `merchant` et `fightSlave` restent des API de compatibilité,
  avec `status()` explicite, jusqu'à preuve de fournisseurs réels.
- Le canal `agents(action=message/finish)` du worker vers prime renvoie
  `AGENTS_BUSY: no agent family belongs to this conversation`. Le présent
  rapport est donc conservé ici pour la reprise par prime.
- Aucun client Dofus lancé, aucune modification de GivreActive, MizanBot
  ou du GivreWork d'origine ; aucun push/merge.
