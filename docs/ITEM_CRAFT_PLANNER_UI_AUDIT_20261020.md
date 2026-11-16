# Audit ciblé du panneau Atelier ItemCraftPlanner — 20 octobre 2026

## Périmètre

OWN worker-15 : `GivreClone.App/Views/ItemCraftPlannerPanel.axaml`, `GivreClone.App/Views/ItemCraftPlannerPanel.axaml.cs`, `GivreClone.App/ViewModels/ItemCraftPlannerViewModel.cs`, ce document. Aucun changement de `MainWindow`, `ItemCraftAccessService`, moteur, projets de test, navigation, fichiers de données ou client Dofus. API d'injection `CatalogProvider`, `FolderCatalogProvider`, `PlanProvider`, `CatalogDirectorySelected` et constructeur sans argument inchangés.

Lecture seule des sources d'intégration :

- `ItemCraftAccessService.ListCatalog(query,mode,existingTargets)` privilégie les cibles existantes et les membres de panoplie, mais **retourne une sélection bornée à 300**, non un catalogue exhaustif. La query est évaluée dans ce service uniquement lors d'un appel, pas par le filtre du ViewModel. Les catégories `Objet` et `Panoplie` utilisent toutes deux un identifiant numérique, leurs plages pouvant se chevaucher.
- `ItemCraftAccessService.BuildPlan` retourne `IsVerified: sets.Source.LocalHashVerified` même lorsque le moteur a `result.IsComplete == false`. La complétude passe uniquement par `CoverageLabel` (`PARTIEL · ACQUISITIONS INCONNUES` ou `SOURCES LOCALES · À CERTIFIER EN JEU`), **pas par un booléen** du DTO précédent. Les étapes peuvent comporter quantité `INCONNU`, provenance `UNVERIFIED` et prix/durée indéterminés.
- Ce même adaptateur limite le rendu **à 160 nœuds**, ajoute normalement un avertissement si des étapes ne sont pas projetées, et peut limiter les avertissements à 15. Le panneau ne peut **pas récupérer les nœuds que le service n'a jamais transmis**. Remonter cette limite nécessite un correctif séparé chez le propriétaire du service ; **aucune modification n'a été apportée au service ici**.

## Défauts démontrés et correctifs UI

### 1. Refresh détruisant les objectifs hors page — corrigé

Avant, `LoadCatalog` retirait `Targets` lorsque leur ID était absent des 300 fiches reçues, y compris après un filtrage de recherche et pendant `RefreshPlan`. Une quantité choisie pouvait donc disparaître sans action utilisateur. Maintenant les cibles, noms, groupes et quantités **restent** en mémoire lors de l'import et du refresh. Un dictionnaire garde la provenance de chaque cible **au moment de sa sélection**. Avant le calcul, le VM vérifie la présence d'une fiche `Objet` avec le même ID et la même provenance dans le lot récent. En cas d'absence de page ou d'empreinte différente, les cibles restent visibles, mais **le plan est bloqué** avec un message indiquant les références à revérifier. Aucun objectif de source potentiellement périmée n'est transmis silencieusement au moteur.

Une panoplie n'est déployée que si **tous les membres possèdent la même provenance que sa fiche d'ensemble**, en plus de leurs identifiants et empreintes locales contrôlés. Une réactualisation ne peut pas assembler silencieusement des pièces de catalogues/versions distincts.

### 2. Gros catalogues difficiles à rechercher — corrigé dans les capacités UI

Le précédent filtre ne parcourait que les 300 premières fiches chargées et ne recherchait que par nom, même lorsqu'une requête par GID était traitée correctement dans l'adaptateur. Le bouton **« Rechercher tout le catalogue »** appelle maintenant le fournisseur injecté avec la recherche courante (comme `MainWindow` est déjà câblé), sans déclencher la fabrication. Le filtre local fonctionne par nom **et** identifiant. Un compteur indique résultats affichés/fiches reçues ; la limite de 50 résultats visibles reste bornée et le plafonnement à 300 fiches reçu du fournisseur est indiqué. La sélection reste conservée et sa preuve est recontrôlée. Aucun appel réseau ou gameplay n'est introduit.

Autre bug : `.GroupBy(e => e.Id)` supprimait un ensemble lorsque son ID numérique était identique au GID d'un objet. La déduplication est désormais `(Category, Id)` (casse normalisée) et l'import utilise un parcours borné sans matérialiser un `GroupBy` sur l'ensemble des fiches retournées.

### 3. IsVerified ≠ IsComplete ≠ UNKNOWN — clarifié

`ItemCraftCatalogEntry.EvidenceLabel` affiche désormais **« EMPREINTE LOCALE VÉRIFIÉE »** à la place de **« SOURCE VÉRIFIÉE »**. `IsVerified=true` **ne prouve** ni l'authenticité de l'éditeur/du jeu, ni des quantités d'inventaire, ni que toutes les dépendances de craft sont disponibles.

Le record `ItemCraftPlanSnapshot` possède un nouveau paramètre **optionnel et compatible avec les appels existants** : `bool? IsComplete = null` :

- `true` : le **moteur/fournisseur** affirme l'exhaustivité de ses données locales, toujours « à vérifier en jeu » ;
- `false` : affichage « INCOMPLET · UNKNOWN » ;
- `null` : « ISCOMPLETE NON TRANSMIS · UNKNOWN » (ou « PARTIEL · UNKNOWN » si le `CoverageLabel` signale déjà un état partiel), **pas un état complet inféré du hash**.

Les résultats source hashés mais incomplets demeurent affichables avec leurs étapes inconnues, avertissements et valeurs « Inconnu ». Les résultats sans validation d'empreinte locale (`IsVerified=false`) restent refusés comme plans réels, et leur message de blocage est restitué plutôt que remplacé par une formule générique. Les états de démo portent toujours **FICTIF**.

**Intégration possible plus tard (hors OWN) :** l'adaptateur peut transmettre `IsComplete: result.IsComplete` avec le même constructeur, sans casser les invocations existantes. Ce worker ne modifie pas cet adaptateur.

### 4. Graphe 160+ nœuds tronqué silencieusement — corrigé côté UI

Avant : `LoadPlan` faisait `.Take(160)` sans expliquer la perte d'informations. Maintenant le VM accepte jusqu'à **4 096** lignes sourcées puis affiche des **pages de 160** (`Page précédente`, `Page suivante`), avec compteur *affichés / fournis*, inspecteur de nœud et choix de méthodes conservés d'une page à l'autre. Le dictionnaire de méthodes collecte les choix même depuis les pages non affichées et les transmet dans `ItemMethodChoices` au rafraîchissement.

Un résultat source de plus de 4 096 lignes est délibérément borné et étiqueté **PARTIEL · GRAPHE BORNÉ** avec avertissement. Un résultat limité à 160 **par le fournisseur** reste étiqueté comme incomplet si le fournisseur confirme la coupure, ou « exhaustivité UNKNOWN » s'il ne fournit pas cette confirmation. Le panneau ne prétend pas que ses 160 lignes sont tout le graphe.

Lors du rechargement d'un plan, les anciens choix de méthode ne sont réappliqués que si le nœud existe encore dans le graphe reçu **et** si cette méthode fait toujours partie des méthodes autorisées ; aucun choix obsolète n'est renvoyé au moteur.

### 5. Avertissements et démo — corrigé

Sur toute invalidation du plan, le panneau réinitialise les avertissements et ne conserve plus le message d'une ancienne simulation. Le scénario Bouftou affiche « DÉMO FICTIVE · aucune validation de pièces réelles » dans la zone des cibles ; les vraies fiches et preuves restent isolées de la démonstration.

## Matrice de non-régression

| Cas | Comportement attendu | État |
|---|---|---|
| Source locale >300 entrées | au plus 300 fiches reçues affichables, recherche explicite via provider, avertissement si provider passe >300 | Corrigé dans l'UI ; limitation de lecture du service maintenue |
| Query numérique GID | filtre un objet selon `Name` **ou** `Id` | Corrigé |
| Même ID objet + panoplie | les deux cartes restent accessibles sous leur propre mode | Corrigé |
| Refresh avec cibles absentes de la page | cibles/quantités conservées, calcul refusé tant que preuve non revue | Corrigé |
| Refresh même source, membres présents | cibles conservées, le moteur reçoit les quantités et le stock | Corrigé (contrat) |
| Nouvelle empreinte avec ID identique | cible conservée mais bloquée jusqu'à resélection explicite | Corrigé |
| Hash local vérifié, dépendances UNKNOWN | arbre partiel visible, aucune promotion implicite à complet | Corrigé |
| `IsVerified=false` réel | pas de plan exploitable ; message de blocage explicite | Préservé/clarifié |
| `IsComplete=null` | indication de complétude non transmise, jamais « complet » par défaut | Corrigé |
| 161, 320, 4 096 nœuds reçus | pages de 160, total exact des nœuds **reçus** | Corrigé dans l'UI |
| >4 096 nœuds reçus | 4 096 maximum et état PARTIEL/UNKNOWN | Corrigé dans l'UI |
| 160 nœuds limités par service | message de troncature/UNKNOWN, données additionnelles inaccessibles dans l'UI | État honnête ; blocage externe au OWN |
| Changement de page et choix de méthode | choix conservé, transmis après recalcul si la méthode reste admissible | Corrigé dans l'UI |
| Changement de catalogue ou alternatives | aucune méthode historique non proposée n'est réinjectée | Corrigé |
| Panoplie, membres de sources différentes | refus de composer des pièces portant des provenances incompatibles | Corrigé |

## Validation / limites restantes

Le build ciblé et les tests existants sont exécutés sans modifier de projet/test. Une matrice de scénarios d'interface est fournie ici sans prétendre à une automatisation de tests xUnit du ViewModel : aucun fichier de test ne peut être ajouté hors OWN et aucune session de GUI réelle n'a été ouverte. Les services en lecture seule n'ont pas été instrumentés.

**Limites restantes à signaler au prime :**

1. `ItemCraftAccessService.BuildPlan` produit au plus 160 nœuds, indépendamment de la pagination UI. L'UI ne peut pas révéler le graphe complet tant que le service ne fournit pas des lignes supplémentaires ou un accès paginé sourcé.
2. L'API actuelle du service n'envoie pas `IsComplete` explicitement ; seule la chaîne `CoverageLabel` transporte l'information. Le nouveau champ optionnel du DTO permet de supprimer cette ambiguïté lorsque le service sera modifié par son owner.
3. `ItemCraftAccessService.ListCatalog` borne volontairement à 300 fiches par recherche (référence/source), la recherche doit être affinée. Les imports géants entièrement client-side ne doivent pas être considérés comme chargés dans leur intégralité.
4. Un stock manuel saisi n'est **pas** une preuve d'inventaire, banque ou Havre-Sac observé ; le calcul de source refuse sa déduction autoritaire. Aucun achat, farm ou craft réel n'est exécuté par ce panneau.
