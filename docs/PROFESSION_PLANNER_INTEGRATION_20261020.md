# GivreWork — Coach métiers : intégration du 20 octobre 2026

## Objectif

Réponse à « Bijoutier niveau 20 → 30 : calcul des recettes, plumes de Piou
à drop, huile de sésame issue d'un autre craft/farm, achats HDV éventuels »
**sans donner une valeur inventée à des quantités, à l'XP ou à un prix**.

Le module **Coach métiers** est accessible dans la barre de titre, la navigation
latérale et **Ctrl+K**. C'est une application Avalonia réelle, non un mockup
statique. L'écran est sombre avec turquoise, bleu et ambre. Son état initial
est « Bijoutier 20 → 30 », mais **aucune recette ni nombre de crafts n'est
certifié au démarrage**.

### Parcours visuel

1. Choisir le métier (recherche), niveau actuel et cible, et éventuellement
   un **nombre de crafts manuel** pour obtenir une nomenclature alors que
   l'XP manque. La valeur XP saisie n'est jamais automatiquement « vérifiée ».
2. Cliquer **Importer un catalogue local** et choisir un dossier contenant un
   manifeste et un asset figé ; le choix utilise le sélecteur natif du système.
   Aucun scraping, réseau ou scan de disque n'est lancé depuis l'écran.
3. Choisir une recette de la liste et remplir les stocks connus manuellement
   par **nom d'objet unique ou GID**, avec quantité ; doublons de noms
   ambigus ignorés plutôt qu'attribués au hasard.
4. Cliquer **Construire mon plan** pour voir étapes/arborescence, besoins,
   consommations de stock, dépendances et autres métiers, statuts vérifiables,
   coûts et durées **seulement quand toutes les entrées sont prouvées**.
   Les boutons de préférence Farm/Drop/Fabriquer/Acheter sont répercutés
   vers le moteur sur le GID, mais n'inventent jamais de source d'acquisition.
5. Le bouton **Voir la démo fictive** montre un scénario illustratif clairement
   annoncé FICTIF, avec plumes, huile et graines ; aucune valeur n'est
   publiée comme étant une recette Dofus réelle.

La page utilise les interfaces
`ProfessionPlannerPanel`/`ProfessionPlannerViewModel`, son adaptateur
`ProfessionAccessService`, le solveur pur
`ProfessionPlannerEngine` et `ProfessionCatalogReader`.

## Sources et fiabilité

- Le catalogue **n'est pas fourni d'office** : les seuls fichiers SQL
  actuellement repérés dans `artifacts/game-data-review` concernent
  `maps.sqlite` ; ils ne démontrent pas la recette Bijoutier.
- Le lecteur accepte seulement un **dossier choisi explicitement** contenant
  `manifest.json` et `recipes.json` ou `dofus.sqlite`, avec version exacte,
  longueur et SHA256. Source `local-extraction` ou
  `privately-licensed-offline`, autorisation explicite
  `permission=owner-authorized`. Chemins liés, altérations, schémas inconnus,
  SQLite sidecars mutables ou limites dépassées sont rejetés.
- Le schéma SQLite inspecté (RecipeData/junction ingrédient) ne prouve
  pas toujours la **quantité produite** ni **l'XP par craft**.
  Ces valeurs restent null/UNKNOWN ; le moteur n'interprète pas
  `resultLevel` ou `craftXpRatio` comme une équivalence XP.
- Les données présentes dans le catalogue sont sourcées par version/empreinte
  **locale** : un hash ne certifie ni la signature de l'éditeur, ni la
  disponibilité des ressources à l'instant T, ni la possibilité d'ouvrir
  une porte ou un atelier. Le stock manuel est une déclaration humaine.
- **DofusDB** : partenariat autorisant le scraping du site déclaré par
  l'utilisateur. La licence publique de son **API** LPNC-IA 1.0 comporte
  une restriction d'ingestion dans un agent d'IA. Aucun export DofusDB
  public n'alimente ce Coach ou son MCP sans exception contractuelle
  IA explicite. DofusPourLesNoobs peut inspirer les stratégies, sans
  copie automatisée de textes ou prétention de droits.

## Règles du moteur

- Décomposition récursive des ingrédients, craft secondaire d'un autre métier
  **seulement si son niveau est documenté** ; sinon étape non résolue.
- Réserve de stocks partagée sur tout le graphe ; surplus de craft réutilisé
  pour une autre branche ; cycles/profondeur/overflow bornés.
- Sources possibles Craft, Gather, Drop, Buy. La priorité kamas/temps
  n'est calculée qu'à partir de coûts et durées **comparables et sourcés**.
  Aucune prévision de taux de drop ou tarif HDV implicite.
- `SuggestTrainingRecipes` existe dans le moteur mais **ne choisit pas
  artificiellement une recette gagnante** si l'XP, le coût ou la durée
  d'au moins une candidate manque.
- Aucun script Lua, achat, session de récolte, combat, téléportation ou
  ACK serveur n'est déclenché par ce panneau.

## MCP — outil 54

`query_profession_plan` est ajouté au serveur MCP, annotation
`ReadOnly=true`. Forme :

```json
{"requestJson":"{\"op\":\"plan_job\",\"jobName\":\"Bijoutier\",\"startLevel\":20,\"targetLevel\":30}"}
```

La façade `ProfessionMcpFacade` comprend `capabilities`,
`plan_job`, `materials` et `steps`. Elle borne JSON et réponses,
ne reconnaît que les requêtes correspondant au snapshot approuvé et
reste **UNKNOWN sans provenance et droits IA**. Aucune carte farm ni trajet
n'est créé sans données WorldAtlas prouvées.

L'intégrateur construit un snapshot MCP depuis `local-extraction`
seulement, MCP activé, empreinte de l'asset conforme à
`GIVRE_PROFESSION_AI_APPROVED_SHA256` et
`GIVRE_PROFESSION_AI_AUTH_ID` explicite. **Ces paramètres ne démontrent
pas à eux seuls une permission juridique** : ils représentent
une admission manuelle indépendante qui doit reposer sur une preuve
réelle de droits. Aucun export public DofusDB n'est accepté.

## Validation locale

Au dernier build avant intégration Git :

- `dotnet build GivreClone.slnx -c Release -m:1 --no-restore` :
  **PASS 0 erreur / 205 avertissements**.
- `GivreClone.Tests` : **1893 PASS, 2 SKIP, 0 FAIL**,
  incluant **93 tests ciblés** profession/navigation/mapping.
- `GivreClone.PureTests` : **211 PASS**.
- `GivreMcp/Smoke.ps1` : **54 outils exacts PASS**.
- `git diff --check` : **PASS**.

Les tests validés sont majoritairement offline et utilisent des
fixtures **synthétiques** pour les exemples Piou/sésame. Aucune séance
client Dofus, rendu interactif de page Windows, authenticité d'une
recette 3.7.12.12, comparaison de prix d'un serveur, farming ou
progression de métier en jeu n'a été validé.

**À poursuivre pour le coach automatique complet** :
import local autorisé complet avec XP par craft et quantité produite,
snapshot métier+inventaire attribué à un personnage et actualisé avec
preuves, catalogues de drops/ressources avec maps et cellules,
prix HDV observés, scénario multi-recettes avec contraintes d'atelier,
et voie explicite de génération d'un brouillon Lua revu puis exécuté
avec garde serveur. Aucun de ces prérequis n'est déclaré fonctionnel ici.
