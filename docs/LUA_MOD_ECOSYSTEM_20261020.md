# Catalogue Lua local et écosystème de mods Givre — 20 octobre 2026

## Périmètre livré

`GivreClone.App/Services/ScriptCatalog/` fournit une **lecture seule** de packages Lua locaux, indépendamment du mécanisme actuel de lancement de scripts. Chaque package possède un manifeste JSON versionné, le SHA-256 exact de son fichier `.lua`, un numéro de contrat API et une liste de capacités à auditer. L'inspection renvoie un état, des constats, les capacités classées et, uniquement après vérification d'intégrité, un aperçu limité à 2 000 caractères. Aucun chargement automatique ni aucune exécution n'existe dans ce module.

Structure d'un package local :

```text
<racine-catalogue>/
  demo/
    manifest.json
    main.lua
```

Exemple `demo/manifest.json` (l'empreinte est **à calculer sur les octets exacts** de `main.lua` ; la valeur illustrative ci-dessous sera rejetée tant qu'elle n'est pas remplacée) :

```json
{
  "schemaVersion": 1,
  "id": "demo",
  "version": "1.0.0",
  "name": "Repères de récolte",
  "description": "Prépare des marqueurs depuis des données observables",
  "entryPoint": "main.lua",
  "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
  "apiVersion": "1.0",
  "capabilities": ["map.read", "ui.overlay"]
}
```

PowerShell, depuis le dossier du package : `(Get-FileHash -Algorithm SHA256 .\main.lua).Hash.ToLowerInvariant()`. Le modèle de manifeste est `ScriptPackageManifest`, et l'entrée de lecture `new LocalScriptCatalog(root).Inspect("demo")` ou `.Scan()` (au plus 200 dossiers immédiats). Un utilisateur ne fournit qu'un `id` simple, jamais un chemin absolu. Le nom du répertoire doit correspondre exactement à `id`.

## Politique et limites

Le schéma `1` exige `apiVersion: "1.0"` et `version: "MAJOR.MINOR.PATCH"`. L'entrée relative doit terminer en `.lua`, sans `..`, segments vides, chemins de disque, `:` ou séparateurs Windows. Chaque segment du chemin est vérifié pour les liens symboliques/jonctions. Le manifeste a une limite de 32 Kio et le Lua de 512 Kio ; l'UTF-8 est décodé de manière stricte. Une empreinte incorrecte, un schéma inconnu ou une capacité inconnue ferme l'inspection. Le catalogue est destiné à une racine locale contrôlée par Givre ; sa vérification de liens et sa comparaison SHA-256 ne constituent pas une protection contre un processus local malveillant modifiant les fichiers simultanément. Le runtime futur devra ouvrir les fichiers de manière atomique et refaire la validation immédiatement avant tout lancement autorisé.

Les capacités déclaratives sont triées comme suit :

| Niveau | Capacités exemples | Décision actuelle |
|---|---|---|
| Observation | `world.read`, `map.read`, `inventory.read`, `quest.read`, `ui.overlay` | Aperçu vérifié si SHA valide ; zéro exécution |
| Permission explicite future | `movement.request`, `gather.request`, `combat.request`, `economy.market`, `bank.write`, `network.http` | Aperçu avec demande de revue ; aucune permission accordée |
| Interdite | `filesystem.write`, `process.launch`, `client.inject`, `runtime.native`, toute capacité inconnue | Package bloqué, aucun aperçu du Lua |

Les déclarations ne sont **jamais** une autorisation runtime. Un futur superviseur devra enregistrer des permissions par script ID + version + SHA-256 + API version + capability, les révoquer si un de ces éléments change, demander les autorisations nécessaires et vérifier à chaque appel le contexte et les preuves disponibles. Refuser les scénarios client non connecté et les données manquantes. Garder les capacités client natives/injection hors du modèle Lua.

## Contrat de l'extension API Givre

`GivreScriptApiV1` définit seulement des **signatures documentaires** : `world.snapshot()`, `map.resources()`, `inventory.snapshot()`, `quest.progress()`, `overlay.publish(markers)`, `movement.request(target)` et `gather.request(resource)`. Ce sont des propositions de bindings dans un **adaptateur Givre**, non des fonctions déjà implémentées dans MoonSharp ou Dofus. Les retours doivent être des snapshots DTO immuables avec provenance, horodatage et état « non reçu » lorsque les informations manquent. Les marqueurs de l'overlay sont dessinés dans l'UI Avalonia Givre ; leur contenu ne doit pas provoquer de clic automatisé. Le moteur de scripting existant ne doit appeler ce nouvel adaptateur qu'après les vérifications d'intégrité et les permissions confirmées.

Versionner les signatures et les éventuels schémas de paramètres/résultats : une nouvelle signature incompatible impose un nouveau `apiVersion`. La v1.0 ne contient aucune primitive d'injection, pointeur mémoire ou téléchargement de DLL. Prévoir une table de compatibilité explicite avec chaque build Dofus lorsque des données issues du jeu sont consommées ; aucun numéro de version de Dofus ne se déduit du manifeste Lua.

## Pistes de fonctionnalités : source d'inspiration, aucun code tiers repris

Le dépôt public [Altansar69/dofus-miniatures](https://github.com/Altansar69/dofus-miniatures), consulté le 20/10/2026, annonce un mod expérimental **Dofus Unity 3.7.12.12 r78**. Son README évoque miniatures, surbrillance et filtres interactifs, affichage de ressources sur carte/mini-carte (avec quantité recensée et réserve concernant l'état après récolte), et une aide manuelle aux chasses au trésor. L'affichage des ressources r78 est indiqué comme vérifié hors jeu avec un rendu en jeu encore à confirmer. Une compatibilité avec **3.7.10.10** n'est donc pas établie. Le dépôt ne présente pas de licence globale détectable via les métadonnées consultées : ne reprendre ni code, ni icônes, ni assets, ni approche d'injection. Les idées de produit ci-dessous sont spécifiées indépendamment pour une UI Givre.

1. **Mini-carte Supervision** : couche Avalonia indépendante, mosaïque de cartes authentifiées, position/cellId reçue affichée avec provenance, miniaturisation configurable des portraits, zoom et filtre. État d'attente explicite pour les positions non prouvées.
2. **Récoltes et métiers** : projection de ressources connues par métier, niveau et zone ; marqueurs avec source et fraîcheur ; disponibilité inconnue après collecte, sans comptage inventé. Générateur visuel de configuration de farm sans programmation, exportant ensuite un manifeste Lua auditable.
3. **Surbrillance de l'UI Givre** : surligner des cibles/PNJ/objets *dans les panneaux Givre* sur base d'événements attestés, limiter densité et budget de rendu, options de contraste/accessibilité. Aucune surcouche injectée dans le client Dofus n'est nécessaire.
4. **Chasses et progression** : journal d'étapes, indices saisis ou issus de données publiques attribuées, aperçu de destination, navigation prévisionnelle et checklist des succès/quêtes. Ne jamais marquer une étape terminée sans événement prouvé ou action manuelle.
5. **Écosystème Lua** : cartes visuelles « capacités demandées », dépendances et version, approbation explicite à terme, aperçu de diff/empreinte à chaque mise à jour, exemples certifiés hors jeu, catalogue local avec recherche et catégories, et export/import après validation.

La prochaine intégration UI peut brancher `Inspect` et `Scan` sur un ViewModel dédié (sans parcourir d'autres dossiers). Le branchement au moteur de scripts est un jalon distinct nécessitant un moteur de permission et des tests d'appel ; aucune exécution automatique ne doit être déduite du simple statut `VerifiedForPreview`.

## Validation et intégration

Tests `GivreClone.Tests/ScriptCatalogSecurityTests.cs` : manifeste valide, changement d'empreinte, traversal UNIX/Windows, flux alternatifs NTFS, version API/schéma incompatible, permissions sensibles et inconnues, limite de taille, doublons JSON/capacités et liens symboliques lorsqu'ils sont disponibles. Le projet Tests référence explicitement les sources de l'application : intégrer les nouveaux fichiers de `GivreClone.App/Services/ScriptCatalog/*.cs` dans `GivreClone.Tests.csproj` via un `<Compile Include="..\GivreClone.App\Services\ScriptCatalog\*.cs" Link="Linked\ScriptCatalog\%(Filename)%(Extension)" />` avant le `dotnet test` normal. Cette ligne est confiée au responsable de l'intégration puisque le `.csproj` est hors du périmètre de cette contribution.
