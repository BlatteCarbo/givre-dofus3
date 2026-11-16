# GivreClone — navigation et planification métiers, continuation hors jeu

## Navigation : A* conservant les routes du client

Le code de `GivreClone.Protocol/MapPathfinder.cs` garde la topologie Dofus
560 cellules et les mêmes ordres de voisins, coûts de déplacements,
conditions de coin diagonal, portes de secteur et compression
`keyMovements`. Le tableau immuable des 8 voisins est calculé
une fois ; les états de recherche (`g`, `came`, visites) sont des
spans temporaires de taille bornée par appel, plutôt que de
réallouer des dictionnaires/ensembles à chaque chemin.

Les tests `MapPathfinderRecoveryPerformanceTests` comparent
contre l'algorithme historique indépendant sur des cartes
générées avec graines constantes : routes encodées exactes,
déplacements dirigés, carte à cellules bloquées, choix de
cellule d'interaction, changements de prédicats entre
générations et sécurité des différences d'altitude extrêmes.
Un benchmark 96 paires × 3 mesure ticks et octets alloués
des deux implémentations, **sans assertion de gain**.
Le débit réel et les métriques ne seront annoncés qu'après exécution.

Ce changement n'introduit **aucun cache cross-map ou cross-session** :
les prédicats d'occupation, de franchissement et de terrain
sont interrogés à chaque requête. Il n'invente aucune route
vers des cellules inconnues ni de vitesse en jeu.

## Métiers : capacité d'approvisionnement par plan

Le `ProfessionPlannerEngine` admettait une source avec
`MaxUnits` autorisé pour chaque besoin individuellement,
sans additionner toutes les branches du graphe. Deux
ingrédients peuvent donc chacun demander 2 d'une même
source plafonnée à 2, et un plan incorrectement complet
en demander 4 au total.

La correction garde un registre de capacité **unique par
source et par exécution de Plan**. Les allocations entre
sources comparables sont déterministes et peuvent se
répartir le besoin. Si aucune combinaison admissible ne
couvre la demande, elle reste `Unresolved`, sans crédit
partiel inventé. Des lignes de catalogue qui prétendent
être une même source (GID, méthode, version) sont
rejetées plutôt qu'additionnées sans identité de stock.

`ProfessionPlannerRecoverySafetyTests` couvre branches,
cap inférieur/suffisant, split, stock initial prouvé,
recalcul après nouvelle observation, cycle de recettes,
coûts inconnus, doublons et overflow.

**Limites :** ces plans restent consultatifs.
`Executed=false` et `Acknowledged=false` ne deviennent
jamais vrais sur une simple simulation de récolte,
craft ou achat. Les sources et recettes proviennent
toujours d'entrées versionnées fournies à l'intégrateur,
sans inventer de données de serveur.

## Validation

À compléter avec compilation, résultats des tests ciblés,
régression générale hors Live, PureTests et benchmark.
