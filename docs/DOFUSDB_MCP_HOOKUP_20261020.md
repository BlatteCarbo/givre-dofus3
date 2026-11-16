# DofusDB — outils MCP locaux, lecture seule (20 octobre 2026)

## Résultat / périmètre

Nouveaux tools de consultation dans GivreMcp/GivreTools.cs, avec attribut MCP ReadOnly=true :

| Tool public | Signature (hors CancellationToken injecté) | Réponse |
|---|---|---|
| dofusdb_catalog_capabilities | aucun | Opérations, limites, statut de confiance |
| dofusdb_catalog_status | aucun | Comptes items/recipes/item-sets après vérification |
| dofusdb_search_items | query:string, offset:int=0, limit:int=20 | Recherche FR/EN sur noms, max 30 |
| dofusdb_get_item | itemId:long | Métadonnées minimales d'un GID |
| dofusdb_get_recipe | resultItemId:long | ID résultat, jobId et paires d'ingrédients |
| dofusdb_get_set | setId:long | Liste de membres d'une panoplie |
| dofusdb_list_sets | offset:int=0, limit:int=20 | Panoplies triées et paginées, max 30 |

L'implémentation GivreMcp/DofusDbCatalogReadTools.cs ne passe pas par le ControlPipeClient, ne sollicite aucun bot, n'exécute aucune commande Lua, ne fait ni HTTP ni appel API DofusDB, et ne touche pas le client Dofus ou les comptes. Les seuls fichiers ouverts sont le review-summary.json et les trois collections locales items, recipes, item-sets, avec métadonnées de pages associées. Le Status sans autorisation est BLOCK ; capabilities reste lisible sans accès aux données.

## Source réellement observée

Racine locale : tools/dofusdb-partner-catalog/.cache/full-catalog (non mise dans Git).

Le manifeste existant déclare 19 collections, 87 327 documents, complete=true, status=REVIEW_ONLY_COMPLETE, sourceGameVersion=UNKNOWN, aiMcpAllowed=false, gameplayCertified=false, authorizedRuntimeUse=false, sourceRightsVerifiedIndependently=false. Les collections utilisées déclarent items=21 554 documents sur 432 pages, recipes=4 743 sur 95 pages, item-sets=940 sur 19 pages. Ce sont des comptes **déclarés dans le manifeste**, contrôlés ensuite contre les pages lors de la lecture, et pas une certification de l'éditeur Ankama ou de la version du jeu.

Au début du lot, l'importeur worker-14 DofusDbPartner n'était pas encore présent. Ses sources ont ensuite été déposées dans le worktree et leurs signatures relues : DofusDbPartnerCatalogReader.LoadDirectory(string cacheRoot, DofusDbPartnerAdmission admission) retourne un DofusDbPartnerLoadResult ; le catalogue indexe GetItem, GetSet, les recettes et les positions avec provenance/sourceGameVersion UNKNOWN. Cet importeur demande une admission de runtime distincte (OperatorApprovedRuntimeUse) ; nous ne l'activons pas à travers un outil MCP uniquement consultatif. Notre reader autonome spécialisé dans trois collections reste strictement read-only et n'attribue **aucune permission runtime**. Prime pourra ultérieurement dédupliquer le lecteur avec worker-14 après analyse des politiques d'admission, sans assouplir les garanties de droits. **Aucun autre service ni fichier du bridge existant n'a été modifié.**

### Gate explicite opérateur (opt-in, jamais automatique)

La déclaration de partenariat DofusDB fournie par l'utilisateur autorise une revue locale mais ne vaut pas vérification indépendante des conditions juridiques de l'API. Les pages capturées indiquent aiMcpAllowed=false. Pour les consulter depuis MCP dans un processus local test contrôlé, l'opérateur doit renseigner **les quatre variables d'environnement suivantes** dans la configuration de démarrage du processus MCP :

    GIVRE_DOFUSDB_MCP_CACHE_ROOT=%USERPROFILE%\Videos\GIVRE\GivreWork-Integration-20261020\tools\dofusdb-partner-catalog\.cache\full-catalog
    GIVRE_DOFUSDB_MCP_SUMMARY_SHA256=<sha256 exact des octets du review-summary.json>
    GIVRE_DOFUSDB_MCP_REVIEW_ONLY=1
    GIVRE_DOFUSDB_MCP_OPERATOR_SCOPE=api_ai_mcp

Calcul du SHA sur le poste où les données sont présentes :

    Get-FileHash 'tools/dofusdb-partner-catalog/.cache/full-catalog/review-summary.json' -Algorithm SHA256

Il faut l'empreinte calculée **sur les octets réellement admis**. Aucune valeur par défaut, aucun chargement caché depuis un chemin privé, aucun read-through réseau et aucune activation par simple chat. Les variables ne doivent pas contenir de secrets. La racine doit être un dossier local, non UNC, non redirigé. Le contrôle de source exige les métadonnées de chaque page, son SHA-256 et ses comptes exacts ; une seule corruption ou collection partielle interdit **toute** réponse de la requête (UNKNOWN). Le manifeste est rehashé à chaque requête. Une fois construite, l'instance en mémoire représente un snapshot contrôlé des octets vérifiés ; une modification du cache après le chargement nécessite de redémarrer le processus MCP avant d'utiliser la nouvelle source.

Important : ce gate documente uniquement un **opt-in de consultation opérateur** (sourceRights = OPERATOR_REVIEW_ONLY) et garde rightsIndependentlyVerified=false. Ce n'est pas la preuve signée d'une licence DofusDB, ni une promotion du aiMcpAllowed=false dans le manifeste. L'outil Python partner_review_contract.py peut créer un reçu de droits en scope API/IA/MCP et SHA exact, mais refuse à juste titre d'attester une version de jeu UNKNOWN comme version stable. Le module n'invente donc aucune GameVersion et ne prétend pas que les données sont compatibles avec une version installée. L'intégration d'un contrat juridique/attestation de production vérifié et versionné reste une étape différente à réaliser par prime.

### Bornes et sémantique du JSON

L'API ne permet ni URL, ni path, ni script, ni tool dispatch contrôlés par l'appelant. query est borné à 2–80 caractères non contrôles ; offset entre 0 et 25000, limit entre 1 et 30. Les GID sont positifs. Les réponses JSON font au maximum 48 Kio, sinon UNKNOWN neutre. Page JSON limitée à 4 Mio, méta à 2048 octets, manifeste à 16 Kio, total 500 pages items, 120 pages recipes et 30 pages item-sets. La lecture refuse les symlinks connus, les comptes, SHA, identifiants et volumes incohérents.

Toutes les sorties consultatives comprennent source.kind=dofusdb, gameVersion=UNKNOWN, sourceGameVersionVerified=false, sourceRights=OPERATOR_REVIEW_ONLY, rightsIndependentlyVerified=false, gameplayCertified=false, serverAckVerified=false, readOnly=true, executed=false, clientContacted=false. L'absence de résultat identifié retourne UNKNOWN, jamais de recette ou de membre de panoplie inventé.

- Item : GID, noms fr/en bornés, niveau, typeId, itemSetId s'ils existent. Aucun objet raw, description libre, credentials ou URL.
- Recette : resultItemId, jobId et tableaux ingredientId/quantity alignés, strictement de la page. resultQuantity=null, xpPerCraft=null, secondsPerCraft=null et hdvPrice=null tant que la source fiable ne les fournit pas ; pas de déduction depuis craftXpRatio ou resultLevel.
- Panoplie : setId et itemIds lus dans la liste items de la source, pas depuis une heuristique d'appartenance.
- Routes : routeStatus=UNKNOWN, routes=[] ; cette API ne convertit pas des map-positions en edges ni n'établit un ACK serveur.

Smoke.ps1 est mis à jour pour contrôler **62 outils exacts**, les annotations ReadOnly et la réponse BLOCK du catalogue en l'absence des variables d'environnement ; ce smoke n'exécute pas de bot. Il accepte également en option -ReviewCacheRoot et -ReviewSummarySha256 pour une requête de revue locale réelle, explicitement hash-pinnée par l'opérateur (aucune activation implicite). Les tests C# synthétiques DofusDbMcpCatalogTests.cs valident des pages fictives SHA-pinnées, les erreurs de droits et les refus de corruption ; le prime a ajouté le lien Compile de la classe pure au projet GivreClone.Tests (hors OWNER).

## Intégration à suivre

L'appel MCP est désormais direct au catalogue de consultation local, **sans** modification App/VM/UI et sans liaison au client. Les trois catalogues et outils métier existants peuvent rester indépendants jusqu'à ce que le service d'admission partenaire unifie les provenances et les versions ; cette étape ne doit ni alimenter des plans de scripts exécutables ni modifier le protocole du jeu sans tests complémentaires. Aucun git commit ni push par worker-10.
