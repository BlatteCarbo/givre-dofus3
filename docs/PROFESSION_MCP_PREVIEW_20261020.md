# ProfessionMcp — aperçu visuel des métiers, itinéraires attestés et brief Lua

## Périmètre

La façade historique ProfessionMcpFacade existe déjà dans ce dépôt, avec capabilities, plan_job, materials et steps basés sur le vrai ProfessionPlannerEngine. Elle traite l'XP, les matériaux et les prix uniquement lorsque la source les fournit et ne génère pas de routes. Ce lot **complète sans remplacer** la façade :

- ProfessionMcpPreviewEvidence.cs : ProfessionMcpPreviewSnapshot, ProfessionMcpRouteEvidence et ProfessionMcpPartnerGrant.
- ProfessionMcpPreviewFacade.cs : QueryJson(requestJson, snapshot) renvoie les opérations capabilities, visual_preview, farm_routes et script_outline.
- ProfessionMcpPreviewTests.cs : tests offline du rendu, des droits, des entrées malformées et de l'absence de gameplay.

Exemples d'appels avec une cible de métier déjà validée par l'application (les identifiants servent d'exemples, aucune vraie recette Bijoutier n'est inventée) :

    {"op":"visual_preview","jobId":28,"startLevel":20,"targetLevel":30,"limit":20}
    {"op":"farm_routes","jobId":28,"startLevel":20,"targetLevel":30}
    {"op":"script_outline","jobId":28,"startLevel":20,"targetLevel":30}

Chaque requête doit correspondre exactement au ProfessionPlanRequest déjà contrôlé. L'API ne prend ni XP, ni prix, ni inventaire venant du client MCP. La prévisualisation affiche les nœuds du graphe BOM, quantités et dépendances, champs de coût et XP en nullable s'ils sont inconnus. Les statuts UNKNOWN et warnings sont préservés.

## Chemins et scripts

Les routes farm/drop/achat HDV ne sont renvoyées que lorsque ProfessionPlannerEngine.Plan possède une étape d'acquisition VERIFIED correspondante et que le prime a fourni une ProfessionMcpRouteEvidence versionnée, hashée, avec droits et carte/edges explicitement connus par WorldAtlas. La façade ne calcule aucun chemin à partir de coordonnées, ne devine aucun mapId, aucun zaap ou entrée de grotte, et ne transforme jamais une route en ordre serveur. En absence de telles preuves : farm_routes.status=UNKNOWN et routes=[].

script_outline expose une séquence de planification NON exécutable et un champ luaDraft contenant seulement des commentaires Lua construits depuis les actions prouvées du moteur. executableLua=null, luaDraftIsExecutableGameplay=false, requiresExplicitScriptGeneration=true et requiresSeparateGameplayPermission=true. Un agent IA peut reprendre cette ébauche pour rédiger une proposition Lua sous contrôle séparé, mais aucun appel au moteur Dofus, achat, crafting, farming ou HTTP n'est produit ou exécuté dans ce service.

Limites : requêtes JSON 4096 octets maximum, sans propriétés dupliquées, inattendues ou invalides. Pages 1..30, offset 0..2048, réponse plafonnée à 65536 octets. Snapshot de 512 routes au plus, chacune composée d'au plus 32 edges explicites. L'application et le MCP conservent leurs autorisations gameplay et la responsabilité de l'admission de sources.

## Partenariat DofusDB

L'utilisateur atteste un droit IA/MCP de son partenariat. Le nouveau reçu local vérifie la correspondance déclarative site/API/version/SHA, mais **n'affirme pas une vérification indépendante du contrat**. Le module complémentaire exige un ProfessionMcpPartnerGrant et une provenance explicitement DofusDB cohérente avec le reçu, sans rebaptiser la source. La façade existante reste fermée au partenaire tant que prime n'a pas validé et raccordé le mécanisme. Les autres sources demeurent conditionnées à l'autorisation AI/MCP de leur origine.

## Intégration prime

Le prime possède GivreTools.cs, LocalControlBridge.cs, ProfessionAccessService et les fichiers csproj. Il doit adapter des snapshots cohérents, approuvés et issus de WorldAtlas/ProfessionPlanner à la nouvelle façade, puis créer des tools read-only explicitement nommés pour preview/routes/outline. Ne pas offrir de dispatcher arbitraire d'action, ne pas prendre les grants ou les routes directement du JSON MCP, et ne jamais déduire l'ACK serveur d'un calcul statique.

Validation : dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~ProfessionMcp -v:q -clp:ErrorsOnly. Aucun test ne contacte le jeu, DofusDB ou un backend extérieur.
