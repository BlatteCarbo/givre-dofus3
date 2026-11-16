# GivreWork — Revue d’intégration, 20 octobre 2026

## Base

La branche d’intégration est issue de Archerbot/GivreWork / claude/magical-keller-2zszje, commit be43e68, dans le dossier GivreWork-Integration-20261020. Aucun lancement de Dofus, ordre d’achat ou de vente, ni message Discord réel n’a été déclenché. Les modifications ne prouvent pas la compatibilité wire ou une réponse serveur.

## Corrections et comportements attendus

- MCP : aligner les outils enregistrés, leurs droits et le test de fumée. Une commande envoyée ne constitue pas une confirmation serveur. Les sorties filtrent les secrets et bornent les collections.
- HDV : le repricing est confirmé seulement lorsque l’ancien UID de lot disparaît et qu’un nouveau UID correspondant au GID, lot et prix demandés est reçu. Les issues indécidables verrouillent les nouvelles transactions jusqu’au rapprochement humain; ce rapprochement ne crée pas d’ACK.
- Changement de personnage : invalider les liens vendeur et les UID de l’ancien personnage même si le PID persiste.
- Discord : dédupliquer par serveur, sous-zone, destination et sens; ne compter un envoi réussi qu’après HTTP 2xx. Stocker le webhook sous protection DPAPI et ne pas inscrire son jeton en journal.
- Diagnostic : lire les états de session, carte et HDV et rendre les incertitudes protocole/ACK explicites, sans commande de jeu.
- Inventaire : interrompre une séquence d’équipement si l’étape précédente n’est pas confirmée par l’inventaire serveur.
- Anomalies : les listes de destinations de type 4 ne prouvent pas le contrat de téléportation. Ce chemin refuse une forge conjecturale.

## Reproduction locale

Depuis la racine du worktree :

    dotnet build GivreClone.slnx -c Release -m:1
    dotnet test GivreClone.PureTests/GivreClone.PureTests.csproj -c Release
    dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release -m:1
    dotnet build GivreMcp/GivreMcp.csproj -c Debug
    ./GivreMcp/Smoke.ps1
    dotnet run --project AnomalyAlertReliabilityHarness -c Release

La CI GitHub couvre les tests de logique pure sous Linux et le smoke MCP sous Windows. La compilation de l’ensemble nécessite les dépendances voisines GivreProxy et GivreLauncher. Une compilation réussie ne démontre pas un fonctionnement réel en jeu.

## Deuxième passe : fonctionnalités consultatives

- L'historique des prix est lu **sur clic**, par objet/lot sélectionné, dans les limites du protocole `ObjectAveragePricesHistoryEvent`. Le serveur n'y atteste pas le GID en retour : la vue indique explicitement l'absence de corrélation forte, sans présenter une notification comme une valeur garantie.
- Le budget du donjon affiche les clés demandées sur plusieurs passages et celles visibles dans le **sac du personnage choisi**. Les prix/caches banque non reçus restent inconnus; aucun ordre HDV n'est déclenché. Les composants purs GearComparison et ExpeditionBudget sont documentés dans `docs/ECONOMY_FEATURES_20261020.md`.
- Les anciennes APIs Lua `mount`, `merchant`, `quest`, `fightSlave` ont désormais des résultats de disponibilité explicites au lieu de donner un faux sentiment de fonctionnalité. Voir `docs/portal/worker-6-lua-handoff-20261020.md`.
- Projection de journaux Lua avec source/lignes et statut trivalent disponible via API pure, sans observation réseau ni commande; documentation dans `docs/portal/lua-runtime-diagnostics.md`.

## Fonctions non certifiées

Téléportation vers les anomalies par zaap, opérations merchant:* de magasins de joueurs, une partie des API monture/quête/fightSlave, autonomie gameplay multi-comptes et représentation riche complète du prototype Atlas. Une capture autorisée et une réponse serveur attribuable à la session sont nécessaires avant de déclarer ces parcours actifs. La refonte tactique et l’ergonomie peuvent être validées hors ligne séparément.
