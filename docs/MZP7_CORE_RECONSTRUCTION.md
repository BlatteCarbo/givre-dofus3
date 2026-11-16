# MZP7 P0 CORE — Typage protobuf source-only démontré, sans shim GivreCore

**16 octobre 2026. Verdict : COMPONENT_PRODUCTION_COMPILED / OFFLINE_TESTED, et NON GivreCore reconstruit ni App compilée.**

## Livrables exclusifs

| Nouveau fichier | Contenu |
| --- | --- |
| GivreClone.Protocol/MZP7TypedProtobufContract.cs | API production typée sur les vrais objets MZP3/MZP4/MZP5, validation de manifeste et de wire, champs inconnus conservés |
| tools/MZP7CoreContractHarness/MZP7CoreContractHarness.csproj | Compilation des sources MZP0/MZP3/MZP4/MZP5/MZP7 telles quelles en .NET 10, sans shim GivreCore |
| tools/MZP7CoreContractHarness/Program.cs | Fixtures Protobuf synthétiques, tests positifs et adversariaux, conversions réelles |
| docs/MZP7_CORE_RECONSTRUCTION.md | Analyse honnête CS0246, API et voies de raccord |

### Inventaire ciblé, lecture seule

- GivreClone.Protocol/CharacterInfoReader.cs utilise GivreCore.Protobuf.ProtoField, WireType, Field, Varint, String, Message et la résolution par nom de champ. Des heuristiques telles que le « plus grand petit varint = niveau » ne prouvent pas le typage d'une version 3.7 donnée.
- GivreClone.Protocol/FightPrepReader.cs lit des cellules et identifiants en listes int32/int64 packed ou unpacked, des sous-messages Challenge et des numéros provenant du catalogue. Son parseur privé de packed varints ne vérifie pas entièrement les octets non canoniques ou les varints tronqués ; MZP7 fournit une alternative strictement bornée.
- GivreClone.Protocol/EnvelopeBuilder.cs construit Any, Request et Event puis appelle GivreCore.Protobuf.RawProtobuf.Encode. Ses slots dépendent du catalogue. La lecture d'un UID Response n'est **pas** une preuve d'ACK.
- GivreClone.Protocol/PayloadBuilder.cs et PayloadReader lisent ou écrivent des listes ProtoField à partir des noms de champs, avec casts et UTF-8. CaptureDecoder.cs utilise RawProtobuf.ConsumeFrames, Parse et FindTypeUrls pour des observations MITM, non reproduites ici.
- GivreClone.Protocol/ProtoSchemaText.cs expose ProtoMessageDef / ProtoFieldDef depuis du texte .proto au moyen d'un parseur explicitement tolérant, non une compilation protoc complète. MZP0/MZP3/MZP4/MZP5 sont les vrais codecs compilés.
- Un inventaire ciblé des fournisseurs source dans GivreClone.App/Engine/Protocol/tools ne trouve « namespace GivreCore.Protobuf » qu'en **stubs de tests** MZP0. La recherche ciblée des dossiers voisins *Givre* ne trouve pas les projets GivreProxy/GivreCore et GivreLauncher.Core au chemin requis. Le code source GivreRelayAgent correspond à un agent de relais, pas au fournisseur protobuf MITM. Les rapports précédents docs/MZP2_DEPENDENCIES.md et docs/MZP4_PRIME_HANDOFF_20261016.md confirment la même absence. Aucun ZIP, DLL, dump, installation, processus, secret ou nouvel extrait n'a été inspecté.

## Contrat productif MZP7

MZP7TypedCodec.Open reçoit :

1. Un adaptateur **réel** MZP4ProtocolAdapter, déjà candidat via version exacte 3.7.10.10 et empreintes SHA matching/fields.
2. MZP7TypeSource avec version, deux SHA, empreinte d'un manifeste typé explicitement fourni et source label. ComputeManifestSha256 calcule un SHA déterministe indépendant de l'ordre des messages/champs, pour comparaison avec une valeur supposée fournie indépendamment. Le fait de calculer et présenter soi-même ce hash n'est pas une authentification.
3. Des MZP7MessageDeclaration / MZP7FieldDeclaration explicites : nom de message et champ, type, cardinalité répétée, acceptation packed, sous-message cible, présence obligatoire seulement si elle est connue. Les numéros viennent EXCLUSIVEMENT de MZP4.FieldNumber(message, field). Les types **ne sont jamais déduits** des seuls numéros de fields.json : les noms, les collisions de tags, la cardinalité et les limites sont vérifiés avant construction.

API réelle utilisable :

- ReadUnframedPayload(messageName, octets non préfixés) : parse via MZP3RawProtobuf et retourne MZP7TypedResult.
- ReadWire(messageName, MZP3WireMessage) : renormalise par encode/parse borné, même si le message vient d'un builder plutôt que du décodeur.
- FromMZP4(MZP4DecodedRequest) / FromMZP5(MZP5DecodedPacket) : exige la concordance stricte CodeForName du catalogue avant conversion ; le record caller-supplied ne peut pas s'auto-attester.
- MZP7TypedMessage.Values(field) et Single(field) : accès typé aux MZP7TypedValue (Signed, Unsigned, Boolean, Text, CopyBytes, Nested) ; EncodeLossless() conserve exactement les octets source et champs inconnus/repeated grâce à MZP3.

Types présents : Int32, Int64, UInt32, UInt64, Boolean strict 0/1, Utf8String strict (accents pris en charge, UTF-8 invalide refusé), Bytes défensivement copiés, Fixed32, Fixed64, EmbeddedMessage explicitement déclaré. Repeated int32/int64/uint32/uint64/bool peut être déclaré packed et accepte alors varints ordinaires ou champs Len packed, avec validation des varints canoniques, erreurs de dépassement 32-bit, budget de sous-champs et profondeur. Int32 négatif exige l'extension de signe standard sur 64 bits : la suite tronquée sur 5 octets n'est pas acceptée. Aucun zig-zag sint32/sint64 n'est supposé ou implicitement confondu avec int32/int64.

Les types imbriqués doivent être déclarés. Un champ singulier répété est rejeté plutôt que traité en « last wins » silencieux. Champ Required absent, mauvais wire type, UTF-8 incorrect, bool 2, packed tronqué / non canonique / overflow, champ inconnu dans un manifeste, identité d'alias contradictoire et budgets dépassés sont des **refus catégoriels**, sans paquet partiel. Les champs unknown du message, même binaires, restent conservés sans être interprétés comme string/embedded.

**Toutes les propriétés TypeSourceAuthenticated, TypesIndependentlyAuthenticated, ClientAuthenticated, ServerAckCertified et MayInject retournent toujours false.** Ni l'origine, ni la version du vrai binaire, ni la direction authentique de MZP5, ni la sémantique du schéma, ni un ACK serveur ne sont créés par cette API.

## Tests positifs / adversariaux

Commande ciblée (pas de build App) depuis la racine de GivreShareWork :

    dotnet run --project tools/MZP7CoreContractHarness/MZP7CoreContractHarness.csproj -c Release --no-restore -m:1
    dotnet build tools/MZP7CoreContractHarness/MZP7CoreContractHarness.csproj -c Release --no-restore -m:1 -v:q

Les fixtures sous tools utilisent des noms SampleRequest / SampleEvent / Child et des tags 1..11, 800, 999 **synthétiques uniquement** ; ce ne sont pas des mappings Dofus. Harness : conversions de vrais objets MZP3, de vraie requête produite/décodée MZP4, puis d'une observation décodée MZP5 ; int32 négatif, UID non certifié, UTF-8 accentué, bytes copiés, liste packed+unpacked, messages imbriqués et unknowns byte-for-byte ; nombreux cas adversariaux bool/type/version/hash/schema/collision/ordre/budgets. Ces tests n'affirment ni comparaison à un binaire 3.7 ni réception serveur.

## Bilan précis sur CS0246

**Réduction immédiate des erreurs CS0246 du projet principal : 0.** Cette passe ne modifie aucune référence de projet, aucun appel existant et n'ajoute délibérément aucune déclaration dans l'espace de noms GivreCore.Protobuf. Le csproj réel GivreClone.Protocol référence toujours ../../GivreProxy/GivreCore/GivreCore.csproj, actuellement absent ; Engine et App conservent leurs autres imports GivreCore/GivreLauncher.Core. Le harness compile MZP0-7 parce qu'il lie les nouveaux fichiers source productifs **sans** importer les anciens consommateurs non migrés. « Harness vert » n'est pas « GivreClone.App compile ».

**Piste réelle pour réduire progressivement les dépendances :**

1. Obtenir légalement et vérifier indépendamment les véritables sources GivreCore/GivreLauncher.Core si elles sont disponibles, avec licences, empreintes et signatures originales. Ne jamais réutiliser un stub sous tools comme source de production.
2. Sinon, sous une mission autorisée de modification des anciens consommateurs : remplacer un lecteur sélectionné, par exemple FightPrepReader sur des champs nommés et typés prouvés dans un schéma de la version ciblée, par MZP7TypedCodec + MZP3. Conserver un test de parité sur fixtures vérifiées et comparaison contre un vrai fournisseur disponible ; n'inférer aucune origine ni ACK.
3. Répéter par consommateur ; seulement après migration de toutes les API importées, reconfigurer les références de projets et mesurer les CS0246 réellement supprimés. Transport MITM, launcher, packet/capture, génériques ProtoField historiques et provenance live restent des obstacles **indépendants** de ce port.

Le composant est donc un socle productif strict, réutilisable sans dépendances extérieures pour décoder des messages typés explicitement déclarés ; ce n'est ni une substitution trompeuse de GivreCore ni une parité GivreClone v1.3.1 certifiée.
