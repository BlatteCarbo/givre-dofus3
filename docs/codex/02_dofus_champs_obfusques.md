# REA Dofus 3.7.11.11 : champs obfusqués et conflits de sources

Recherche statique du 16 octobre 2026. Aucun code applicatif modifié, aucun client exécuté, aucun réseau, aucune injection, aucun nouveau dump. Ce rapport n'établit aucun nouvel encodage ni parcours en jeu.

## 1. Identité et portée des preuves

- Catalogue du clone : 1337 alias, 3725 entrées de champs. SHA-256 matching_current.json : c1da4e5090d07c41cc5f6e0d74abe0cdb4777db08ccd2c73de2692e0a4a2dc01 ; fields.json : 58ba845bf9057556127bbe683617f07acbb9dde9824c8be81c70fe1744910f19.
- Candidat 3.7.11.11 : 244643 octets, SHA-256 bf3799e516ee52d968432db5651fa662a23308d94ac8314862189e59dbd41651. Le manifeste annonce 2260 messages/6493 champs : %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\manifest.json:3.
- Recalcul local : le snapshot %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.10.10\dofus-3.7.10.10.proto a également 244643 octets et la même empreinte SHA-256. Cette identité des deux fichiers candidats ne constitue pas une preuve indépendante de compatibilité des clients.
- Le manifeste déclare certified:false, sémantique messages/enum/références/dispatch NON VÉRIFIÉE, candidat fourni par l'appelant, protocol_promotion:false et candidate_execution_independently_attested:false : %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\manifest.json:2, %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\manifest.json:9, %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\manifest.json:17, %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\manifest.json:31.
- Identité documentaire du client : Dofus.exe 1f13781121325cc224bd41b823bbbfdf6bd160390f046d185e602537f5bb57d4 ; GameAssembly.dll e08da74cfcac126a4d0929bf76d79b7c4f45b902ece082041b55d2715db9bf13 ; métadonnées b04b4cee266b0a9f0aa6023b15e389e3c0dec6f054947288d8511416dbcf8a36. Source : %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\manifest.json:23. Ces empreintes sont celles du manifeste, pas un nouveau hash des binaires installés.
- Notes pipeline lues avant réemploi : %USERPROFILE%\Videos\GIVRE\GivreActive\opencode_handoff.md:7-12 cite tools/full-il-decompile/out/Core.dll sur le build du 31 août, pas 3.7.11.11. Le chemin %USERPROFILE%\Videos\GIVRE\GivreActive\tools\full-il-decompile est absent lors de la recherche (erreur système 2). Les projections ILSpy restantes sous GivreActive/artifacts/version-upgrade/3.7.1.0-467e232887c5-full3/ilspy/ sont historiques.
- Limites déjà documentées : %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\docs\PROTO_STUDIO_LINEAGES_AND_NATIVE_LIMITS_20261009.md:9-19 et :28-35 (lignée candidate et registre ne prouvent pas la sémantique). %USERPROFILE%\Videos\GIVRE\GivreActive\analysis\REA_DOFUS\REA_TOOLING.md:27-36 donne d'autres empreintes ; ce rapport ancien ne prouve donc pas le client demandé.

**Lecture des tableaux :** nom clair vers alias = déclaration du catalogue. Le type au même tag dans le candidat ne devient pas automatiquement le type du champ clair. Les divergences interdisent cette assimilation. Confiance élevée dans les lectures de fichiers ; confiance NON ÉTABLIE dans leur liaison sémantique 3.7.11.11. int32/int64 sont les libellés du candidat, sans preuve indépendante du détail signed/fixed.

## 2. a) Achat et vente PNJ marchand

### ExchangeBuyRequest

Alias déclaré : type.ankama.com/kby ; %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:872.

| Message | Champ | Tag catalogue | Type au même tag candidat, sans équivalence prouvée | Résolu ? | Preuve |
|---|---|---:|---|---|---|
| ExchangeBuyRequest | fwwc | 1 | int32 fymm #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9352 | NON VÉRIFIÉ | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2215 |
| ExchangeBuyRequest | fwwd | 2 | int32 fymn #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9353 | NON VÉRIFIÉ | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2216 |
| ExchangeBuyRequest | fwwe | 3 | kbw fymo #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9354 | NON VÉRIFIÉ | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2217 |

### ExchangeSellRequest

Alias déclaré : type.ankama.com/jzp ; %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:838.

| Message | Champ | Tag catalogue | Type au même tag candidat, sans équivalence prouvée | Résolu ? | Preuve |
|---|---|---:|---|---|---|
| ExchangeSellRequest | fwzd | 1 | int32 fybr #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9044 | NON VÉRIFIÉ | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2460 |
| ExchangeSellRequest | fwze | 2 | int32 fybs #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9045 | NON VÉRIFIÉ | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2461 |

Le candidat kby contient int32 fymm=1, int32 fymn=2, enum kbw fymo=3. Le candidat jzp contient int32 fybr=1, int32 fybs=2, enum jzn fybt=3, int32 fybu=4 : %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9351 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9043. Le nombre de champs de vente et les noms divergent. **Objet/GID/UID, quantité, prix, contexte de boutique et valeurs d'enum : NON VÉRIFIÉ**, faute de corps client exact et de liaison indépendante. Ne pas renommer selon l'ordre historique.

Le clone confirme son absence de câblage : %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.App\Services\LotM\LotMLuaModule.cs:416-418 rend false, 0 et une raison pour npc.buy/npc.sell. Cela décrit le clone, pas le client officiel.

## 3. b) QuestStartRequest et fpbo #2

fields.json déclare quest_id #1 et fpbo #2 : %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:6543. Alias déclaré icf : %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:254. Or le candidat icf ne possède que int32 fqkc=1 : %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:3281. **Sens et type de fpbo #2 : NON VÉRIFIÉ.** Aucun tag 2 dans cet alias ; un homonyme fpbo dans une autre classe ne prouve rien. Le clone émet seulement quest_id et documente la valeur inconnue : %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.Protocol\MoveForge.cs:552-553. L'acceptation de cette omission en jeu n'est pas établie.

## 4. c) ClientChallengeInitRequest et fyvk

**Aucun numéro de fyvk n'est établi.** Le catalogue contient challenge_key #1, fyvi #2, fyvj #4, aucun fyvk : %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1578. Alias kof : %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:1006. Le candidat kof contient string gaca=1 et optional bool gacb=2, sans tag 4 : %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:10666.

La chaîne d'engagement est une intention du clone : %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.App\Services\Transport\Socket\SocketGameClient.cs:970-972 appelle SN(ClientChallengeInitRequest, fyvk, schnorr.ChallengeInit()). %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.App\Services\Transport\Socket\SocketSchnorr.cs:43 décrit G^r mod P en décimal. %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.Protocol\RequiredMapping.cs:99 exige fyvk. Cela prouve le besoin et le trou du mapping, pas le numéro ni l'algorithme officiel. **Rôle officiel et numéro : NON VÉRIFIÉ.** Le fyvk du candidat est int32 #2 dans kep (%USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9662), pas une chaîne du challenge. Le string #1 de kof est une piste, pas une résolution.

## 5. d) Retour à la sélection de personnage

| Nom catalogue | Champs catalogue | Forme de l'alias candidat | Preuves |
|---|---|---|---|
| CharacterReplayRequest | character_id #1, fzir #2, fzit #3 | int64 gatl #1 ; ksx gatm #2 | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:1070 ; %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1404 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11245 |
| CharacterReplayWithRemodelRequest | remodeling_information #1, character_id #2, fzmw #4, fzmt #5 | lmo gapj #1 ; int64 gapk #2 | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:1051 ; %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1409 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11128 |
| CharacterListRequest | aucune entrée fields.json | vide | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:1058 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11180 |
| CharacterSelectionRequest | character_id #1 | int64 gatq #1 | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:1071 ; %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1427 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11256 |
| ReloginRequest | aucune entrée fields.json | bool fzwx #1 | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:995 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:10546 |

Ces noms sont des candidats du catalogue. **Message(s), ordre et préconditions réels pour revenir de la partie à la sélection : NON VÉRIFIÉ**, faute de corps officiel 3.7.11.11 de l'action UI. Ne pas annoncer CharacterReplayRequest comme commande de retour : fzit #3 est déjà absent de ksz. L'envoi de liste du clone, %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.App\Services\Transport\Socket\SocketGameClient.cs:953-960, concerne sa connexion ; il ne prouve pas un retour officiel.

## 6. e) Quitter le tutoriel guidé

Un candidat nommé **GuideModQuitRequest** existe, orthographe exacte, alias ibd : %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:242. L'alias historique GuidedModeQuitRequestMessage pointe dessus : %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.Protocol\LuaMessageAliases.cs:675. fields.json déclare fpat #1 (%USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:3505), tandis que ibd est vide dans le candidat (%USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:3154). GuideModReturnRequest/ibo a trois champs catalogue contre un candidat vide : %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:3508 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:3204.

Nom présent dans le clone : établi. Forme vide candidate : établie dans ce proto. **Action officielle de sortie, sens/type de fpat, préconditions et acceptation serveur : NON VÉRIFIÉ.** Aucun envoi ni sortie effective n'est revendiqué.

## 7. f) Inventaire exhaustif échange/HDV encore obfusqué

Périmètre reproductible : noms commençant par Exchange, Bid ou ObjectAveragePrice, plus ObjectPriceDateEffect et SellingConditions. Champs filtrés par [fg][a-z]{3} ou unresolved_. Le nom fake peut être un mot anglais ou un identifiant opaque : son rôle n’est pas démontré ; il est ajouté comme cas ambigu, sans le qualifier arbitrairement de clair. price et ready ne sont pas classés obfusqués par ce filtre. Résultat : **54 entrées, 85 champs à examiner : 84 champs du filtre principal et le champ ambigu fake**. Ce périmètre couvre boutique, échange, craft, banque/transfert, HDV et données de prix ; les tarifs de guilde/zaap sont exclus.

Aucune résolution sémantique nouvelle sur le client exact. La colonne candidat contrôle seulement l'alias déclaré et le tag. Tag absent = conflit concret ; pas de liaison = aucun alias de premier niveau fourni pour ce type, éventuellement imbriqué. La confiance de toutes les résolutions sémantiques ci-dessous reste NON VÉRIFIÉE.

| Message / alias déclaré | Champ | Tag catalogue | Type au même tag candidat, sans équivalence prouvée | Résolu ? | Preuve |
|---|---|---:|---|---|---|
| BidActivity / sans alias | gadu | 1 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:824 |
| BidActivity / sans alias | gadr | 3 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:825 |
| BidActivity / sans alias | gads | 4 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:826 |
| BidActivity / sans alias | gadt | 5 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:827 |
| BidActivity / sans alias | gadq | 6 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:828 |
| BidActivityResponse / kxt | gady | 1 | repeated kxr gbkb #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11754 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:831 |
| BidExchangerObject / sans alias | fwtt | 7 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:839 |
| ExchangeBidBuyerStartedEvent / jyt | fxeq | 3 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2133 |
| ExchangeBidBuyerStartedEvent / jyt | fxeo | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2134 |
| ExchangeBidHouseBuyResultEvent / jzx | fxbp | 1 | jzv fycw #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9082 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2142 |
| ExchangeBidHouseGenericItemAddedEvent / sans alias | fxim | 1 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2147 |
| ExchangeBidHouseInListRemovedEvent / jyu | fxeg | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2164 |
| ExchangeBidHouseItemRemovedEvent / jye | fxhv | 2 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2180 |
| ExchangeBidHousePriceRequest / kap | fxcn | 3 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2191 |
| ExchangeBidHouseTypeRequest / kcp | fxlr | 1 | int32 fyok #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9426 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2198 |
| ExchangeBuyRequest / kby | fwwc | 1 | int32 fymm #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9352 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2215 |
| ExchangeBuyRequest / kby | fwwd | 2 | int32 fymn #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9353 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2216 |
| ExchangeBuyRequest / kby | fwwe | 3 | kbw fymo #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9354 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2217 |
| ExchangeCraftCountModifiedEvent / jzg | fxli | 2 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2221 |
| ExchangeCraftCountRequest / kdj | fxln | 1 | kdh fyrb #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9514 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2224 |
| ExchangeCrafterJobLevelUpEvent / jyc | fxbt | 2 | int32 fxvx #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:8867 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2249 |
| ExchangeKamaModifiedEvent / kbm | fxmc | 2 | kbk fyki #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9289 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2267 |
| ExchangeMoneyMovementLimitEvent / kaq | fxqh | 1 | int64 fygk #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9179 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2275 |
| ExchangeMountsStableAddedEvent / kbn | fxlx | 1 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2279 |
| ExchangeMoveKamaRequest / jyj | fxpj | 3 | int64 fxxd #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:8898 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2283 |
| ExchangeMultiCraftCrafterCanUseHisResourcesEvent / kdu | fwzz | 1 | kds fyso #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9567 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2286 |
| ExchangeMultiCraftCrafterCanUseHisResourcesEvent / kdu | fwzy | 2 | bool fysp #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9568 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2287 |
| ExchangeMultiCraftCustomerStartedEvent / kbu | fxnu | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2295 |
| ExchangeNpcShopStartedEvent / jzi | fxjw | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2309 |
| ExchangeObjectModifiedInBagEvent / kab | fwqn | 2 | jzz fydx #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9101 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2320 |
| ExchangeObjectMoveAndSetReadyRequest / jxu | fwsh | 1 | int32 fxug #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:8828 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2329 |
| ExchangeObjectMoveAndSetReadyRequest / jxu | fwsk | 3 | int32 fxui #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:8830 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2331 |
| ExchangeObjectMoveAndSetReadyRequest / jxu | fwsi | 5 | bool fxuk #5 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:8832 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2333 |
| ExchangeObjectMoveAndSetReadyResponse / sans alias | fxir | 1 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2336 |
| ExchangeObjectMoveAndSetReadyResponse / sans alias | fxis | 2 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2337 |
| ExchangeObjectMoveToTabRequest / kdy | fxhh | 1 | int64 fysy #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9589 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2349 |
| ExchangeObjectMoveToTabRequest / kdy | fxhm | 6 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2353 |
| ExchangeObjectMoveToTabRequest / kdy | fxhi | 7 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2354 |
| ExchangeObjectPutInBagEvent / kat | fwxd | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2359 |
| ExchangeObjectPutInBagEvent / kat | fwxe | 5 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2360 |
| ExchangeObjectPutInBagEvent / kat | fwxa | 6 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2361 |
| ExchangeObjectRemovedFromBagEvent / kdo | fxca | 2 | repeated bool fyrs #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9545 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2369 |
| ExchangeObjectTransferAllFromInventoryRequest / kbs | fwro | 1 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2373 |
| ExchangeObjectTransferAllToInventoryRequest / kaw | fwyc | 1 | kau fyhh #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9202 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2376 |
| ExchangeObjectTransferListToInventoryRequest / kbp | fxbd | 1 | repeated int32 fylc #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9316 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2382 |
| ExchangeObjectTransferListToInventoryRequest / kbp | fxbe | 2 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2383 |
| ExchangeObjectTransferListToInventoryRequest / kbp | fxbf | 3 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2384 |
| ExchangeObjectUseInWorkshopRequest / kct | fwvp | 1 | int32 fypb #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9432 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2393 |
| ExchangeObjectUseInWorkshopRequest / kct | fwvq | 2 | int32 fypc #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9433 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2394 |
| ExchangeObjectsModifiedEvent / kdr | fwzm | 1 | bool fysi #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9561 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2402 |
| ExchangeObjectsModifiedEvent / kdr | fwzo | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2405 |
| ExchangeObjectsRemovedEvent / jxz | fxmm | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2410 |
| ExchangeObjectsSellRequest / kcb | fake | 2 | tag absent au premier niveau de kcb ; type exact NON VÉRIFIÉ | Non (nom ambigu) | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2414 ; %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json:873 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9367-9373 |
| ExchangePlayerMultiCraftRequest / jyr | fwsa | 5 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2420 |
| ExchangePodsModifiedEvent / jza | fxkq | 2 | string fxzz #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:8991 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2427 |
| ExchangeReadyEvent / kec | fxfi | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2439 |
| ExchangeReadyRequest / kba | fxig | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2444 |
| ExchangeRecycleTradeStartedEvent / kbq | fwrt | 1 | int32 fylg #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9320 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2447 |
| ExchangeRecycleTradeStartedEvent / kbq | fwru | 2 | int32 fylh #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9321 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2448 |
| ExchangeRecycleTradeStartedEvent / kbq | fwrv | 3 | repeated lmy fyli #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9322 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2449 |
| ExchangeRecycleTradeStartedEvent / kbq | fwrs | 4 | repeated int32 fylj #4 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9323 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2450 |
| ExchangeRecycleTradeStartedEvent / kbq | fwrw | 5 | repeated int32 fylk #5 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9324 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2451 |
| ExchangeRequestedTradeEvent / kaz | fwqf | 5 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2457 |
| ExchangeSellRequest / jzp | fwzd | 1 | int32 fybr #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9044 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2460 |
| ExchangeSellRequest / jzp | fwze | 2 | int32 fybs #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9045 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2461 |
| ExchangeStartedEvent / kdk | fxaq | 1 | string fyrg #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9526 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2467 |
| ExchangeStartedEvent / kdk | fxar | 4 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2469 |
| ExchangeStartedWithPodsEvent / kbc | fwss | 8 | bool fyip #8 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9245 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2484 |
| ExchangeStartedWithStorageEvent / jyf | fxna | 3 | kyg fxwm #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:8892 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2489 |
| ExchangeStartedWithStorageEvent / jyf | fxmz | 5 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2490 |
| ExchangeTaxCollectorEquipmentRequest / kai | fxmv | 2 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2494 |
| ExchangeTranscendentConfirmationEvent / keb | fxkw | 1 | tag absent du candidat associé | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2513 |
| ExchangeTranscendentConfirmationRequest / kdn | fxps | 2 | bool fyrm #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9532 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2516 |
| ExchangeTypesItemsExchangerDescriptionForUserEvent / jzs | fwtz | 2 | int32 fych #2 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9060 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2524 |
| ExchangeTypesItemsExchangerDescriptionForUserEvent / jzs | fwub | 3 | int32 fyci #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9061 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2525 |
| ExchangeWeightEvent / keg | fxdj | 3 | int32 fyuc #3 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9623 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2530 |
| ObjectAveragePrice / sans alias | frob | 3 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5396 |
| ObjectAveragePrice / sans alias | froe | 4 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5397 |
| ObjectAveragePrice / sans alias | froc | 6 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5398 |
| ObjectAveragePriceHistory / sans alias | frru | 2 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5402 |
| ObjectAveragePricesEvent / isr | frok | 1 | iso fswd #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:5165 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5407 |
| ObjectAveragePricesRequest / iqu | frjf | 1 | iqs fsob #1 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:4946 | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5418 |
| ObjectPriceDateEffect / sans alias | gbza | 1 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5531 |
| ObjectPriceDateEffect / sans alias | gbzb | 2 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5532 |
| ObjectPriceDateEffect / sans alias | gbzc | 3 | type NON VÉRIFIÉ : pas de liaison message vers type | Non | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:5533 |

## 8. Tableau récapitulatif

| Message | Champ | Numéro | Type client exact | Résolu ? | Preuve |
|---|---|---:|---|---|---|
| ExchangeBuyRequest | fwwc | 1 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2215 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9351 |
| ExchangeBuyRequest | fwwd | 2 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2216 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9351 |
| ExchangeBuyRequest | fwwe | 3 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2217 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9351 |
| ExchangeSellRequest | fwzd | 1 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2460 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9043 |
| ExchangeSellRequest | fwze | 2 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:2461 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:9043 |
| QuestStartRequest | quest_id | 1 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:6544 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:3281 |
| QuestStartRequest | fpbo | 2 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:6545 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:3281 |
| ClientChallengeInitRequest | challenge_key | 1 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1579 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:10666 |
| ClientChallengeInitRequest | fyvi | 2 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1580 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:10666 |
| ClientChallengeInitRequest | fyvj | 4 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1581 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:10666 |
| CharacterReplayRequest | character_id | 1 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1405 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11245 |
| CharacterReplayRequest | fzir | 2 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1406 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11245 |
| CharacterReplayRequest | fzit | 3 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1407 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11245 |
| CharacterSelectionRequest | character_id | 1 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1428 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:11256 |
| GuideModQuitRequest | fpat | 1 catalogue | NON VÉRIFIÉ | Tag catalogue établi ; sens/type exact non résolus | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:3506 ; %USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto:3154 |
| ClientChallengeInitRequest | fyvk | NON VÉRIFIÉ | NON VÉRIFIÉ | Absent du mapping | %USERPROFILE%\Videos\GIVRE\GivreShareWork\data\fields.json:1578 ; %USERPROFILE%\Videos\GIVRE\GivreShareWork\GivreClone.Protocol\RequiredMapping.cs:99 |
| Échange/HDV : 54 entrées | 84 champs obfusqués + fake ambigu | Voir §7 | Voir §7 | Aucun nom officiel résolu | Une preuve précise par champ au §7 |

## 9. Prochain élément de preuve utile

Il manque une sortie des corps client explicitement reliée aux empreintes 3.7.11.11, ou une preuve indépendante versionnée reliant type réseau, constructeur/sérialisation et appel métier. Réconcilier le catalogue et le proto avant npc.buy/sell, fyvk ou sorties de session. Une capture expurgée future pourrait corroborer l'attribution des champs ; elle n'a pas été entreprise ici. Les inconnues restent inconnues, sans valeur inventée ni promotion du candidat.
