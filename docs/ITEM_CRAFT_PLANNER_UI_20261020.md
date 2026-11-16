# Planificateur d'objets et panoplies — UI Avalonia, Lot 2

**Date :** 20 octobre 2026
**Responsable :** worker-15
**État :** panneau autonome préparé pour raccordement par le prime

## Fichiers livrés

Nouveaux fichiers exclusivement :

- `GivreClone.App/Views/ItemCraftPlannerPanel.axaml`
- `GivreClone.App/Views/ItemCraftPlannerPanel.axaml.cs`
- `GivreClone.App/ViewModels/ItemCraftPlannerViewModel.cs`
- `docs/ITEM_CRAFT_PLANNER_UI_20261020.md`

Le panneau s'instancie avec `new ItemCraftPlannerPanel()` et possède son propre ViewModel : `panel.ViewModel`. Il ne nécessite pas le ViewModel du shell. **MainWindow, navigation, services métiers, csproj et fichiers historiques n'ont pas été modifiés par ce worker.**

## Parcours sans code

1. L'opérateur choisit `Un objet`, `Panoplie` ou `Liste libre`.
2. Il charge un catalogue via le sélecteur de **dossier local** et l'adaptateur d'import injecté. La recherche se limite aux fiches explicitement vérifiées et filtrées par type. Aucune fiche de vrai objet n'apparaît sans nom, identifiant et provenance vérifiés.
3. Il indique la quantité, puis ajoute une cible. `Un objet` remplace l'ancienne cible ; `Panoplie` déplie **toutes** les pièces documentées de l'ensemble, avec quantité identique par pièce ; `Liste libre` accumule plusieurs objets. Le même objet ajouté à nouveau remplace sa quantité, sans double ajout implicite.
4. Il renseigne son stock par nom/quantité ; une ressource existante est remplacée, et les autres cibles utilisent le **même dictionnaire de stock**.
5. `Calculer les besoins` fait appel à `PlanProvider`, affiche la source, le graphe parent → enfant → sous-craft, le stock utilisé et les manquants, ainsi que les coûts/temps uniquement lorsqu'ils sont réellement sourcés.
6. Cliquer un nœud de l'arbre révèle les méthodes `farm`, `acheter`, `craft` si **le fournisseur les atteste**. L'opérateur choisit la méthode, puis clique `Recalculer avec mes choix` pour transmettre `ItemMethodChoices`. `Simuler (hors jeu)` fournit un résumé consultatif sans agir dans Dofus.

La sélection de mode invalide les anciennes cibles. La modification du stock invalide le calcul précédent ; en démo, elle recalcule seulement l'arbre pédagogique. Le sélecteur de dossier n'effectue pas lui-même l'import ; il transmet le chemin à l'adaptateur fourni par le prime, à qui incombe la vérification du format, de la version, de la source et de la preuve.

## API publique pour branchement par le prime

```csharp
var panel = new ItemCraftPlannerPanel();
panel.CatalogProvider = () => /* liste de ItemCraftCatalogEntry issus de sources locales fiables */;
panel.FolderCatalogProvider = folder => /* vérification + lecture dossier local */;
panel.PlanProvider = request => /* ItemCraftPlanSnapshot depuis le moteur métier */;
panel.CatalogDirectorySelected += folder => /* sélectionner/réinitialiser le dossier de l'adaptateur */;

panel.LoadCatalog(items);  // poussée facultative
panel.LoadPlan(snapshot); // poussée facultative
```

Les délégations sont sur le panneau et accessibles également depuis `panel.ViewModel`.

### Catalogue

`ItemCraftCatalogEntry(Id, Name, Category, Provenance, IsVerified, MemberItemIds?)`

- `Category` vaut exactement `Objet` ou `Panoplie`.
- Une fiche unitaire est un `Objet` identifié par `Id`.
- Une `Panoplie` référence ses pièces par `MemberItemIds`, chaque pièce devant exister parmi les `Objet` vérifiés du catalogue.
- Les entrées `IsVerified == false`, sans provenance, ou explicitement marquées `FICTIF` sont **rejetées pour le mode réel**. Le catalogue est borné à 300 fiches ; la recherche montre au plus 50 correspondances, afin de rester fluide.

L'adaptateur ne doit pas déduire des pièces manquantes ou des recettes « connues » de mémoire. Si le lecteur source ne sait pas déterminer l'ensemble, afficher son état indisponible et ne pas proposer une panoplie faussement complète.

### Demande de plan

```csharp
public sealed record ItemCraftTarget(
    string ItemId, string Name, long Quantity, string GroupName = "");

public sealed record ItemCraftPlannerRequest(
    string Mode, IReadOnlyList<ItemCraftTarget> Targets,
    IReadOnlyDictionary<string, long> Inventory, string Strategy,
    IReadOnlyDictionary<string, string>? ItemMethodChoices = null);
```

La quantité cible est comprise entre 1 et 100 000, les cibles sont bornées à 60 et le stock manuel à 100 ressources. Le moteur doit calculer les besoins des **plusieurs cibles en une seule exécution**, consommer les piles **une seule fois globalement**, détecter les cycles, restrictions de niveau et données absentes, et ne comparer temps/kamas que lorsque les éléments sont réellement comparables. `ItemMethodChoices` porte l'identifiant stable du nœud et le choix utilisateur ; le fournisseur détermine si la méthode est applicable.

### Résultat du moteur

```csharp
public sealed record ItemCraftPlanNode(
    string Id, string ItemName, int Depth, string Kind, string Required,
    string Available, string Missing, string Description, string Provenance,
    IReadOnlyList<string>? AllowedMethods = null);

public sealed record ItemCraftPlanSnapshot(
    string Title, string Explanation, string Provenance, bool IsVerified,
    bool IsFictional, string CostLabel, string DurationLabel, string ItemsLabel,
    IReadOnlyList<ItemCraftPlanNode> Nodes,
    string Warnings = "", string CoverageLabel = "PARTIEL");
```

Les snapshots réels sans preuve `IsVerified == false` sont refusés. Les snapshots fictifs sont refusés en mode réel. Les champs chiffres sont des **chaînes présentées** pour que le producteur puisse indiquer `Inconnu`, `Partiel`, `Non prouvé` et garder une provenance précise. Le ViewModel borne l'affichage à 160 nœuds ; le moteur garde la responsabilité de la correction du graphe et des quantités.

## Démonstration Bouftou

Le bouton visible `✦ Démo Bouftou fictive` active **uniquement sur clic** une simulation pédagogique sous le titre **« Panoplie Bouftou · démonstration fictive »**. Toutes les pièces sont des placeholders `Pièce A d'exercice (FICTIVE)`, `Pièce B d'exercice (FICTIVE)`, `Pièce C d'exercice (FICTIVE)` ; **aucune liste réelle de la panoplie Bouftou n'est prétendue**. Les ressources `Fil pédagogique fictif` et `Fibre pédagogique fictive`, leurs ratios, les méthodes et les quantités sont entièrement inventés.

La démo montre un sous-craft commun aux pièces A et B et une ressource commune à toutes les pièces. Sa déduction est déterministe : pour deux branches utilisant le même stock, chaque quantité réservée est retranchée du stock partagé avant la branche suivante. Les fibres nécessaires au sous-craft sont recalculées selon les fils déjà présents dans le stock. La quantité globale de pièces peut être modifiée dans l'entrée quantité du panneau. Les avertissements, le badge du haut et le pied de page indiquent tous le statut **FICTIF**.

## Validation et limites

- Commande de build ciblé : `dotnet build GivreClone.App/GivreClone.App.csproj --no-restore -c Debug -v:q -clp:ErrorsOnly`.
- Aucun paquet, aucune requête réseau, aucun accès jeu, aucune recette ou prix inventés en mode réel.
- `Simuler` n'exécute aucune collecte, aucun mouvement et aucune commande Lua : c'est un contrôle local de la couverture et des choix de méthodes.
- La conception XAML utilise `ScrollViewer` pour fenêtre et petits écrans, styles de focus hérités de Fluent, boutons d'action nommés pour l'accessibilité et deux colonnes sur desktop.
- L'intégration dans la navigation appartient au prime et n'est pas incluse dans ces quatre nouveaux fichiers.
