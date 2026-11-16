# WorldAtlas → scripts IA et MCP — 20 octobre 2026

## Livrables et périmètre

Une façade locale **JSON purement read-only**, sans accès Dofus, réseau, fichiers secrets, bots, scripts Lua, ni MCP externe :

- GivreClone.App/Services/WorldAtlasMcp/WorldAtlasMcpSnapshot.cs : modèles source-backed et garde-fous de provenance, version, droits et volumes.
- GivreClone.App/Services/WorldAtlasMcp/WorldAtlasMcpFacade.cs : API WorldAtlasMcpFacade.QueryJson(string? requestJson, WorldAtlasMcpSnapshot? snapshot) : string.
- GivreClone.Tests/WorldAtlasMcpFacadeTests.cs : tests synthétiques et cas négatifs.

Le lot ne branche **aucun endpoint MCP** : prime en est propriétaire. Aucun edge, passage de grotte, destination de zaap, mapId ou cellId n'est créé à partir de coordonnées voisines, de noms de lieux ou de données absentes. Toutes les transitions proviennent obligatoirement d'enregistrements validés par la source WorldAtlas.

## Audit du MCP et du bridge existants (READ ONLY)

GivreMcp/GivreTools.cs déclare la classe McpServerToolType et ses **52 tools** ; le README GivreMcp les documente explicitement. GivreTools utilise ControlPipeClient.CallAsync et LocalControlBridge relaie les requêtes au BotsViewModel via le thread UI et la politique McpEnabled. Ces fichiers ont été inspectés sans aucune modification.

Les lectures get_map_state(bot), find_places(bot,query) et probe_navigation(bot,target) dépendent d'une position/compte/état du bot ; probe_navigation est un diagnostic de cellules, **pas** une navigation entre maps validée serveur. En revanche cette façade expose un atlas statique hors-jeu, indépendant de l'état d'un bot. Les tools d'écriture existants (run_lua_script, save_script, connect_bot, use_item...) ne doivent pas être accessibles à partir des requêtes atlas. Ne jamais ajouter un dispatcher générique de tool name ou une évaluation de scripts.

## Contrat avec worker-1 WorldAtlas

Worker-1 produit des contrats métiers :

- AtlasMap(MapId, X, Y, WorldId, Layer, SubAreaId, Features, Status).
- AtlasTransition(EdgeId, FromMapId, ToMapId, Kind, Direction, CellId, FromZone, ToZone, Criterion, SourceId, SourceKind, SourceVersion, SourceHash, Status).
- AtlasSnapshot(gameVersion, Maps, Transitions, ...).

La façade utilise des DTO autonomes de mêmes concepts. **Prime doit mapper explicitement**, sans contourner les preuves de source : WorldAtlasMcpSnapshot(gameVersion, IReadOnlyCollection<AtlasMap> maps, IReadOnlyCollection<AtlasTransition> transitions), AtlasMap avec WorldId facultatif, AtlasTransition avec CellId/FromZone/ToZone/Criterion facultatifs, AtlasProvenance(SourceKind,SourceId,GameVersion,Sha256,McpDisclosureAuthorized,AuthorizationEvidenceId). Les SourceId et critère sont bornés à des étiquettes ASCII sûres ; seuls les SHA-256 hexadécimaux et versions exactes entrent dans le certificat de source. Aucun chemin disque/URL/jeton ne remonte dans les sorties.

Les limites sont **25 000 maps, 100 000 transitions, 256 arcs sortants par map, maximum 24 features par map**. Les ID map sont strictement positifs ; les destinations absentes sont null. Le snapshot copie défensivement ses listes de features. Les doublons de maps ou d'edgeId et les champs hors bornes sont rejetés au chargement.

### Autorisations de sources (IMPORTANT)

Par défaut, McpDisclosureAuthorized=false. La visibilité requiert **en plus** un AuthorizationEvidenceId non vide au format limité, un SourceId, une version égale exactement au snapshot et un SHA-256 conforme. Ces valeurs DOIVENT provenir d'une revue indépendante : de simples champs fournis par un plugin ou un utilisateur ne sont pas des attestations.

**Les données DofusDB sont expressément interdites dans la sortie MCP**, même si McpDisclosureAuthorized=true : le partenariat annoncé pour scraper ne prouve pas le droit de diffuser des données auprès d'une IA. La levée de cette interdiction requiert la vérification contractuelle par le prime. Le présent lot ne fait aucun scrape, import, HTTP ni exposition partenaire. Les autres sources restent cachées si non autorisées ; ni leurs ID, ni leur nombre ni leurs arêtes ne sont divulgués.

## Contrat JSON (v1)

L'entrée est un objet JSON **de 4 096 octets UTF-8 maximum**, sans propriétés inconnues ou dupliquées. Aucune clé action, command ou script n'est acceptée. Sortie JSON de **65 536 octets maximum**, sinon retour UNKNOWN neutre. Toutes les réponses déclarent readOnly=true, executed=false, runtimeCertified=false ; capabilities et les erreurs indiquent également clientContacted=false.

| op | Paramètres autorisés | Résultat |
|---|---|---|
| capabilities | aucun | Version du schéma, cinq opérations, plafonds et droits requis, même si atlas indisponible |
| list_maps | offset 0..25000, limit 1..50 (défaut 20), feature sur liste blanche | Carte(s) divulguables triées par mapId, pagination et provenance |
| inspect_map | mapId entier positif | Coordonnées, layer, worldId, subAreaId, features, statut et preuve |
| map_transitions | mapId, offset, limit | Arcs enregistrés de cette seule carte : kind, direction, cellId, zones, criterion, toMapId éventuel et provenance |
| find_path | mapId, targetMapId, maxHops 1..32 (défaut 16) | BFS statique limité à 2 048 expansions, basé **seulement** sur les arcs VERIFIED entre cartes VERIFIED, toutes divulguables |

Exemples de requêtes purement démonstratives (aucun résultat ni edge garanti) :

    {"op":"capabilities"}
    {"op":"list_maps","feature":"mine","offset":0,"limit":20}
    {"op":"inspect_map","mapId":88081690}
    {"op":"map_transitions","mapId":88081690,"limit":20}
    {"op":"find_path","mapId":88081690,"targetMapId":68552196,"maxHops":16}

Un arc de grotte avec ToMapId=null reste **UNKNOWN**, sans destination imaginée. Les zaaps candidats ne sont pas utilisables en recherche d'itinéraire. Si la source d'une destination est cachée, son mapId est masqué en sortie. En cas d'absence de trajet vérifié, le retour est **UNKNOWN** (jamais « inatteignable »). Un chemin de graph statique ne certifie pas un trajet dans le jeu, les conditions de déplacement, un transfert entre zones ou un ACK serveur. La façade ne propose aucune commande d'exécution de gameplay.

## Intégration prime (hors OWN)

Pour disposer d'outils MCP réellement visibles, prime doit :

1. Adapter un snapshot WorldAtlas **versionné, rehashé et validé juridiquement** vers WorldAtlasMcpSnapshot, sans auto-exposer des sources dont les droits IA/MCP sont inconnus.
2. Publier explicitement cinq tools marqués ReadOnly=true : atlas_capabilities, atlas_list_maps, atlas_inspect_map, atlas_map_transitions, atlas_find_path. Pas de generic execute. Vérifier les arguments individuels avant de construire le JSON de la façade.
3. Préserver McpEnabled et les contrôles de sécurité du bridge. Aucune écriture, run de script, lecture de secrets ou commande de bot ne doit être intégrée à ces tools. Les sorties ont besoin d'être bornées avant le transport de réponse MCP.
4. Mettre l'atlas à disposition du générateur Lua/mineur comme **données de planification**, sous réserve de validation des transitions et droits de données. Toute exécution réelle de Lua ou de navigation est une étape indépendante.

Le projet GivreClone.Tests utilise des sources pures compilées via links explicites dans son csproj. Prime a ajouté la ligne WorldAtlasMcp/*.cs dans ce csproj hors de ce OWN. Aucun nouveau MCP tool, aucune modification de GivreMcp ou du bridge n'a été effectuée dans ce lot.

## Validation offline

    dotnet test GivreClone.Tests\GivreClone.Tests.csproj --no-restore -c Debug --filter FullyQualifiedName~WorldAtlasMcp -v:q -clp:ErrorsOnly

Tests sur snapshots synthétiques seulement : parser strict, doubles clés, champs inconnus, entrée trop longue, volume dépassé, destinations inconnues, pagination, provenance redirigée/non divulguable, blocage DofusDB, trajets candidats refusés, limites maxHops, absence de route UNKNOWN et invariants readOnly. Aucun client Dofus, HTTP, compte, script Lua ou processus MCP n'est démarré.
