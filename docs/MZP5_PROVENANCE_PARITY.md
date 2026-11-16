# MZP5 — traçabilité source et écart GivreClone 1.3.1

## Preuve, pas décompilation fabriquée

Outil borné : `tools/MZP5Provenance/parity_audit.py`. L'entrée est une liste
explicite de **14 capacités** et de symboles à chercher uniquement dans des
sources `GivreClone.App`, `Engine` et `Protocol` déjà présentes. Chaque témoin
génère chemin, numéro de ligne et SHA-256 du fichier réel. La vérification
réexécute le calcul et refuse le drift. Aucun inventaire ZIP massif, accès
processus, EXE installé, dump `.dmp`, ressource privée ou réseau n'est effectué.

Commandes :

```powershell
py -3 -B tools/MZP5Provenance/parity_audit.py --self-test
py -3 -B tools/MZP5Provenance/parity_audit.py --verify
```

Résultat : **14/14 présences lexicales vérifiées** dans le snapshot. C'est
une preuve `source`, **pas** une preuve d'intégration, de comportement, de
décompilation de l'EXE ou de correspondance source v1.2.0 ↔ EXE v1.3.1.

| Fonction | Véritable source pertinente | État produit |
| --- | --- | --- |
| Comptes de TEST | `MZP3TestAccountService`, `MZP4TestAccountsPanel` | CRUD/IO UI Headless PASS |
| Comptes historiques, serveur/personnage | `AccountsViewModel`, `ServerPick`, `LaunchService.Character` | Sources présentes; runtime App bloqué |
| Auth/launcher | `LaunchService`, `MZP4LauncherPreflight` | Prévol TEST uniquement, pas de login |
| MITM et proxy | `MitmTransport`, `BotHost`, `SessionBinder` | Provider `GivreCore` manquant |
| Protobuf | `MZP3RawProtobuf`, `MZP4ProtocolAdapter`, `MZP5InboundProtocol` | Codec hors ligne compilé/testé; version live non attestée |
| Combat et déplacements | `LaunchService.Combat`, `MapPathfinder` | Sources historiques; aucune preuve 3.7.10.10 gameplay |
| Lua et métiers | `BotsViewModel.Lua`, `GivreLuaHost.Bindings` | Sources historiques; aucun test de script live |
| Inventaire, banque, HDV | `InventoryStore`, `LaunchService.Bank/Hdv` | Sources présentes, transport manquant |
| Planning | `PlanningEditor`, `BotsViewModel.Planning` | Source UI, H24 non attesté |
| Reprise | `MZP0RecoveryJournal`, `MZP5RecoveryHealthProjection` | Régression journal réelle hors réseau |

`GivreClone.App.csproj` possède une option `EnableAot` avec configuration
NativeAOT et un commentaire de protection VMProtect. Cela **ne prouve pas**
que le binaire `GivreClone.exe` v1.3.1 ait été compilé précisément de cette
façon. La référence historique SHA-256 de l'EXE original est connue par les
travaux précédents, mais non recalculée par cette passe puisqu'aucune copie
détachée autorisée correspondante n'a été identifiée. **Aucune décompilation
C# directe du binaire v1.3.1 n'est revendiquée.**

Le snapshot signale `GivreCore=false`, `GivreLauncher.Core=false` au sens
**projets source absents**, pas une absence de code équivalent dans toute
l'histoire de l'écosystème. Il n'atteste ni licence, ni authentification, ni
ACK, ni connexion, ni performance du binaire original.
