# GivreWork — Atlas du monde, grottes et transitions (lot 2)

Date : 20 octobre 2026. Travail limité aux nouveaux fichiers WorldAtlas, WorldAtlas*Tests et cette note. Aucune connexion Dofus, exécution de script, écriture dans GivreActive ou import automatique.

## Sources vérifiées et nuances de provenance

### Graphe du client et positions

GivreClone.App/Services/WorldGraphExtractor.cs:13-19,55-96 lit le composant Unity world-graph du bundle worldassets. Il distingue m_transitionMapId (identifiant émis lors d'une transition) de m_to.m_mapId (carte d'arrivée) et conserve cellule, direction, type, critère, zones de départ/d'arrivée et indices d'interactifs. Une carte répertoriée mais absente du graphe n'est pas présumée navigable.

GivreClone.Protocol/MapTransition.cs:7-33 et WorldGraphCache.cs:7-77 définissent le cache version 5 sous data/worldgraph5.json. Tableau compact par source : cellule, sentMapId, direction, type, critère, arrivalMapId, fromZone, toZone, elementId, skillId. Type 1 = bord ordinaire, type 32 = porte/interactif (ne prouve pas une grotte), type 2 = ScrollAction (ne prouve pas un zaap). Pour une arête type 2, le mapId d'arrivée peut être différent du mapId théorique transmis.

GivreClone.App/Services/MapDataExtractor.cs:11-28,83-89 décrit MapInformation : mapId, x, y, worldMap, subAreaId. Aucun étage physique n'est démontré par ces seules colonnes. L'atlas sépare worldId, layer, subarea et (x,y), tous optionnels si non observés. LaunchService.Packets.cs:76-86 pointe les caches data/map_positions.json et data/worldgraph5.json. Ce module n'accède à aucun cache sans sélection explicite de dossier et manifeste épinglé.

### Archives historiques

GivreActive/DataSets/3.7.7.6/worldgraph-2026-with-cellids.json.provenance.json:2-22 documente le client cible 3.7.7.6, le hash du worldAsset, l'extracteur et l'empreinte du JSON extrait. Empreinte du JSON historique : ADEB605764E8C44967EE1329DDCC38DAF2A088CF4C07BE45E6A9A3E4ECC060EA ; longueur déclarée 28 313 450 octets. Le schéma de ce fichier n'est PAS assimilé automatiquement au WorldGraphCache v5. Aucun chemin 3.7.12.12 ne peut être dérivé de ce snapshot historique.

### dofus-sqlite, DofusDB, DPLN

La documentation publique de ledouxm/dofus-sqlite, https://github.com/ledouxm/dofus-sqlite, explique que dofus.sqlite contient notamment des positions et que maps.sqlite contient des interactions de cartes. Le prime dispose de artifacts/game-data-review/3.7.12.12/maps.sqlite : 782 336 octets, SHA-256 10f0e6f54ec1bd49f3458a55768096813d4f1be3d08afbe91c9d6be479d1cf0b ; table map_interactions(mapId,worldId,gfxId,cellId,interactionId). Cette table indique des interactifs mais n'indique NI le nom du minerai, NI le chemin d'une grotte, NI la destination d'une porte ou d'un zaap. Le module existant GameDataUpdates/MapInteractionReviewReader.Preview permet déjà leur lecture numérique bornée sans utiliser le graphe.

La licence publique affichée sur https://api.dofusdb.fr/ (LPNC-IA 1.0) limite l'usage commercial et l'automatisation fondée sur l'IA. L'utilisateur a déclaré bénéficier d'un partenariat autorisant le scraping : son autorisation spécifique doit être attachée à un périmètre contractuel et aux usages autorisés avant ingestion ou fourniture à MCP. Le scraping DofusDB est confié à un autre worker ; aucune récupération DofusDB n'a été effectuée par ce module. DofusPourLesNoobs (DPLN) constitue une référence éditoriale possible pour annoter des entrées, prérequis et grottes, mais les droits de réutilisation de guides, leurs formats et leurs versions ne sont pas démontrés ici. Sans droits et preuve vérifiés, les annotations restent CANDIDATE. Ne pas copier de textes de guides.

## Contrat technique des API stables pour l'UI / MCP

Namespace : GivreClone.App.Services.WorldAtlas.

- AtlasMap(long MapId, int? X, int? Y, int? WorldId, int? Layer, int? SubAreaId, IReadOnlyList<string> Features, string Status)
- AtlasTransition(string EdgeId, long FromMapId, long ToMapId, string Kind, int? Direction, int? CellId, int? FromZone, int? ToZone, string? Criterion, string SourceId, string SourceKind, string SourceVersion, string SourceHash, string Status)
- AtlasSnapshot : GameVersion, SourceId, SourceKind, SourceHash, TrustedManifestMatched, Maps, Transitions.
- WorldAtlasReader.ReadDirectory(string folder, string? expectedVersion=null, string? trustedManifestSha256=null) retourne AtlasImportResult(Status,Snapshot,Issues).
- WorldAtlasReader.ReadLegacyCacheFiles(string dataDir,string expectedVersion) : aperçu immédiat des deux caches existants sans manifeste, résultat UNVERIFIED avec toutes les arêtes candidate.
- WorldAtlasEngine.GetMap(snapshot,mapId), GetTransitions(snapshot,mapId,includeUnverified=true,limit=128), FindMaps(snapshot,x?,y?,worldId?,layer?,subAreaId?,feature?,limit=128), FindRoute(snapshot,fromMapId,toMapId,maxHops=64,startZone=null).
- AtlasRoute(Status,Steps,Reason) : VERIFIED_SOURCE_PATH, SAME_MAP, sinon UNVERIFIED. CanExecuteAutomatically reste toujours false.

Status des maps/edges : verified, candidate ou unknown. Types de transition : walk, door, stair, cave, zaap, unknown. Les zones fromZone/toZone sont des secteurs connectés et ne doivent pas être confondues avec les couches layer.

## Import borné / lecture seule

ReadDirectory lit exactement atlas-manifest.json (64 KiB max) et deux fichiers locaux de taille/empreinte déclarées, en refusant répertoires et fichiers liés, URLs, chemins relatifs sortants, assets inconnus et hash incorrect. Chaque manifeste v1 indique gameVersion (quatre nombres), sourceId, sourceKind, schemaVersion=1, assets[file,bytes,sha256].

Deux formats reconnus :

1. maps.json + edges.json : JSON explicite de cartes (mapId, x,y,worldId,layer,subAreaId,features,status) et transitions (edgeId,fromMapId,toMapId,kind,direction,cellId,fromZone,toZone,criterion,status,evidenceKind,evidenceHash).
2. map_positions.json + worldgraph5.json : adaptation STRICTE du cache local (format v5 seulement). Les transitions de ce format restent toujours candidate, même si les octets sont épinglés, car une arête de cache n'est pas un passage serveur observé.

L'API complémentaire ReadLegacyCacheFiles(dataDir,expectedVersion) permet de visualiser immédiatement le contenu local des caches du runtime sans construire de nouveau manifeste, sans écrire ni copier les fichiers ; elle calcule l'empreinte du graphe comme diagnostic, mais n'accepte aucune route et marque explicitement la version fournie comme non vérifiée. Les dossiers et fichiers liés restent refusés.

Un SHA-256 expectedManifestSha256 fourni de l'extérieur est requis pour qualifier la provenance locale ; si absent, toutes les cartes et arêtes sont candidate (ou unknown). Même avec pin correct, une exportation partenaire reste candidate sans revue/observation de transition indépendante. Si le pin fourni est faux, l'import échoue. Une arête moderne n'est verified que sous les conditions CUMULATIVES suivantes : déclaration verified, manifeste épinglé, evidenceKind observed-transition et evidenceHash présent, source reviewed/observation, carte source/cible identifiée, cellule et secteurs connus, aucune condition non résolue. Aucune correspondance de coordonnées ou type d'interactif n'est promue en edge vérifié.

Limites : maps/positions <= 8 MiB, edges/cache <= 24 MiB, maximum 25 000 cartes et 100 000 arêtes. Le moteur de lecture est PUR, sans accès au réseau ni à un processus. Les DTOs sont utilisables par le panneau et le MCP en lecture seule, sans actions gameplay.

## Politique de trajet : pas de faux chemins

FindRoute ne traverse que des edges marqués verified du snapshot épinglé, sans criterion, avec secteurs source et arrivée valides et raccordés de proche en proche. Si les sorties de la carte de départ utilisent plusieurs zones et que la zone courante n'a pas été donnée, la réponse est UNVERIFIED. Une recherche BFS bornée à maxHops entre 1 et 128 empêche les cycles et les explorations sans fin. Les couches superposées n'ont aucun passage implicite. Une route VERIFIED_SOURCE_PATH ne démontre ni l'accès réel à la porte/zaap au moment du script ni un ACK serveur. Aucun chemin généré n'est automatiquement exécutable.

## Proposition UI / MCP / Mineur

Le prime peut exposer recherche par mapId, coordonnées monde, couche explicite, sous-zone, source et features ; retourner une liste ordonnée de transitions et un statut de route documenté. La vue Mineur doit distinguer les interactifs numériques issus de maps.sqlite (ID seulement), les annotations de ressources validées, les entrées de grottes candidates et les routes vérifiées. Un générateur Lua/MCP peut proposer un brouillon lisible mais doit laisser des étapes non prouvées en UNKNOWN/UNVERIFIED plutôt que de fabriquer un itinéraire. La façade MCP et la vue UI sont du ressort du prime/des workers dédiés.

## Tests et limite principale

GivreClone.Tests/WorldAtlasReaderTests.cs couvre manifeste épinglé/non épinglé, source partenaire candidate, chemin cave/escaliers/porte uniquement avec arêtes observées, secteurs multiples, absence de lien entre couches aux mêmes (x,y), critère de quête non résolu, ScrollAction différent de zaap, cache v4 rejeté, tamper/hash, versions invalides, chemins sortants et garde-fous de bornes. La suite ciblée WorldAtlas comprend aussi les tests MCP/UI ajoutés indépendamment par un autre worker.

La couverture réelle de TOUTES les grottes, mines, entrées, zaaps et cartes pour la 3.7.12.12 demeure NON VÉRIFIÉE faute de snapshot complet du world-graph courant et d'annotations de ressources/compétences prouvées. Ne pas annoncer un atlas exhaustif ou un trajet minier fonctionnel sans ces éléments.

## Résultats des contrôles exécutés

- Build App standard tenté : bloqué par dix erreurs CS0579 d'attributs d'assembly dupliqués dans le worktree partagé ; les fichiers responsables sont en dehors de OWN. Aucun csproj/obj d'un autre worker n'a été édité par ce lot.
- Build App avec génération d'AssemblyInfo et TargetFrameworkAttribute désactivée uniquement en options de compilation : PASS, 0 erreur / 196 avertissements du worktree. Cette variante ne remplace PAS une validation du build standard par le prime.
- dotnet test GivreClone.Tests --no-restore --filter FullyQualifiedName~WorldAtlas : PASS, 60/60 (comprend tests de façade MCP d'un autre worker) après recompilation du module, zéro échec/ignoré.
- git diff --check des fichiers dans OWN : PASS. Branche integration-opus-20261020, HEAD 4a773de inchangé au terme du lot, aucun commit/push.
