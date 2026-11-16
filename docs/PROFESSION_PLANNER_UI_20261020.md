# Coach métiers — interface du planificateur (20/10/2026)

## Livré

Surface autonome `new ProfessionPlannerPanel()` dans `GivreClone.App.Views`, dark UI Avalonia 12, avec ViewModel local : progression du niveau **saisi** vers la cible (20 → 30 par défaut), recherche métier/recette, catalogue filtrable sur les sources vérifiées, stock manuel, arbre de besoins avec retraits visuels par profondeur et filtre de méthodes (« Toutes les étapes », « Farm / Drop », « Acheter HDV », « Craft »). Un clic sur un nœud affiche requis, stock, manquant, méthodes **effectivement transmises par la source** et provenance. La simulation est consultative. Aucune action de jeu.

Le bandeau de progression normalise le **niveau fourni par l'utilisateur** sur l'objectif de niveau; il ne représente pas des points d'XP, des taux de drop ou une progression constatée dans le client. Les chiffres XP, coût et durée restent inconnus sans données vérifiées. Le scénario de démonstration utilise uniquement des quantités/dépendances **FICTIVES**, affichées comme telles ; il ne prétend pas refléter des recettes Bijoutier officielles.

## API d'intégration pour prime / workers 2 et 12

**Contrats existants conservés** :

- `panel.PlanProvider : Func<ProfessionPlannerGoal, ProfessionPlannerSnapshot?>`
- `panel.CatalogProvider : Func<IReadOnlyList<ProfessionCatalogRecipe>>`
- `panel.FolderCatalogProvider : Func<string, IReadOnlyList<ProfessionCatalogRecipe>>`
- `panel.CatalogDirectorySelected` / `CatalogFolderSelected` (événement après choix par sélecteur natif).
- `panel.LoadCatalog(IEnumerable<ProfessionCatalogRecipe>)`, `panel.LoadPlan(ProfessionPlannerSnapshot?)`.

`ProfessionPlannerGoal` porte le métier, les niveaux, inventaire manuel, recette choisie, stratégie et nombre de crafts facultatif. Le moteur existant `Services/ProfessionPlanner/ProfessionPlannerEngine.cs` reçoit les vraies recettes, tables XP, stocks et sources via **l'adaptateur du prime** ; le panneau ne lit aucune donnée partenaire par lui-même. `ProfessionPlannerSnapshot` transmet des étapes `ProfessionPlannerStep` avec `Depth`, `Kind`, `Required`, `InInventory`, `Missing`, `Evidence`, `Provenance`, `AllowedActions`. Les méthodes sont une sélection locale, transmises à `PlanProvider` au recalcul. Les libellés « source vérifiée » des recettes reflètent le flag du fournisseur et impliquent que l'importeur a contrôlé l'origine.

**Nouvelle sortie Atlas facultative** :

`panel.VerifiedAtlasRouteProvider : Func<ProfessionPlanStepRow, ProfessionVerifiedAtlasRoute?>`, avec `ProfessionVerifiedAtlasRoute(MapIds, Source, GameVersion, Sha256, EvidenceReference, IsVerified)`. L'interface n'affiche un trajet que si IsVerified, au moins deux mapIds positifs, une source, une version, une preuve et un SHA-256 complet sont présents. Le fournisseur doit avoir certifié **chaque transition** et la cohérence de version avec le vrai moteur WorldAtlas. Aucune liaison déduite à partir des coordonnées ou du type de ressource.

## UX

Le héros présente le besoin principal et les actions « Construire mon plan » / « Démo fictive ». Quatre cartes numérotées purement décoratives ont été supprimées, ainsi qu'une commande de recalcul et un bandeau de navigation répétitifs. Le tableau de synthèse XP/kamas/temps est conservé, avec état inconnu explicite. Les recettes peuvent être recherchées par nom ou ID et filtrées selon leur preuve, l'arbre par méthode d'acquisition, tout en gardant l'inspection de nœud et le résumé de stock.

## Limites

Le catalogue n'est pas automatiquement téléchargé. Le sélecteur de dossier appelle uniquement `FolderCatalogProvider` raccordé par prime. Sans fournisseur : écran initial explicatif, listes vides et données inconnues. Aucun chiffre ni route de jeu ne sont fabriqués ; la démo est une fixture pédagogique volontairement imaginaire. L'outil n'exécute pas de farm, achat, craft, navigation ou client.

## Validation

Build ciblé Release App réussi avec **0 erreur** et 197 avertissements du dépôt après retrait d'un ZIndex invalide dans MainWindow. Commande : `dotnet build GivreClone.App/GivreClone.App.csproj --no-restore -c Release -v:q -clp:ErrorsOnly -p:BuildProjectReferences=false -p:DefaultExcludesInProjectFolder=**/obj/**%3B**/bin/**%3B**/.*/**`. Le contournement ne modifie pas les `obj` des autres workers. Validation finale de la solution et vérification visuelle déléguées au prime.
