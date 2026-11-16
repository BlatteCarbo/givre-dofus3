# MZP4 — intégration produit du protocole neutre, hors réseau

**16 octobre 2026 — SOURCE_COMPILED / OFFLINE_TESTED (30/30 PASS).**

## Pourquoi ce code est utile

Le chemin original `ActionSender -> PayloadBuilder -> EnvelopeBuilder -> GivreCore.Protobuf.RawProtobuf` a besoin d'un `ProtocolCatalog` et des dépendances `GivreCore` actuellement absentes. `EnvelopeBuilder` compose un `GameMessage { request { content: Any, uid } }` **non préfixé**, avec les numéros de champs résolus par `FieldNumber` ; `PayloadBuilder` encode des champs par nom et choisit explicitement leur type. `CaptureDecoder` fragmente un flux reçu puis cherche des `type_url`. Ces sources ont été lues mais n'ont pas été modifiées.

MZP3 fournit les primitives wire réelles, conservant ordre et octets des champs inconnus. **MZP4ProtocolAdapter** les raccorde concrètement au format des catalogues déjà exploités par `ProtocolCatalog` (`matching_current.json` et `fields.json`) **sans dépendance sur GivreCore**. Il prépare une requête depuis des champs nommés, puis lit une enveloppe non préfixée ou une seule enveloppe préfixée de longueur-varint neutre. Une trame isolée n'est ni une preuve du framing Dofus, ni un ACK.

## Sources de production créées

| Chemin | Responsabilité |
| --- | --- |
| `GivreClone.Protocol/MZP4ProtocolAdapter.cs` | Validation MZP0, résolution snapshot des alias et numéros, `MZP4NamedPayloadBuilder`, fabrication et lecture neutres avec MZP3, refus des mélanges de catalogues |
| `tools/MZP4ProtoIntegrationHarness/MZP4ProtoIntegrationHarness.csproj` | Compile **telles quelles** les quatre sources du produit MZP0/MZP3/MZP4 sous .NET 10, warnings as errors ; aucun shim `GivreCore` |
| `tools/MZP4ProtoIntegrationHarness/Program.cs` | 30 tests hors réseau avec fixtures exclusivement synthétiques, cas corrects et cas de refus |
| `docs/MZP4_PROTO_PRODUCT.md` | Ce contrat et ses limites |

Les fichiers préexistants `EnvelopeBuilder`, `PayloadBuilder`, `CaptureDecoder`, `ProtocolCatalog`, `MZP0`, `MZP3`, les projets `.csproj` principaux et les sources des autres agents sont inchangés.

## Contrat de consommation

```csharp
using GivreClone.Protocol;

// Ces SHA-256 doivent être fixés séparément par une distribution de preuves
// fiable : NE PAS calculer les pins des octets entrants à la même invocation.
var reference = new MZP4CatalogReference(
    "3.7.10.10", knownMatchingSha256, knownFieldsSha256, "source-de-reference");
var candidate = MZP4ProtocolAdapter.Open(matchingJsonBytes, fieldsJsonBytes,
    reference,
    new[] { new MZP0RequiredField("MapMovementRequest", "map_id") });
if (!candidate.IsOfflineCandidate)
    throw new InvalidOperationException(candidate.Reason);

var adapter = candidate.Adapter!;
var payload = adapter.ForMessage("MapMovementRequest")
    .Int64("map_id", 123)
    .Bool("cautious", false)
    .RepeatedInt64("key_cells", new long[] { 1, 2 });
byte[] unframed = adapter.BuildClientRequest(payload, -1);
bool structureOnly = adapter.TryReadClientRequest(unframed,
    out var decoded, out var reason);
```

Les identifiants de champs ci-dessus sont **illustratifs**, et ne certifient aucune position wire Dofus 3.7.10.10. `fields.json` fournit le **numéro** et non le type wire : le consommateur choisit `Int64`, `Bool`, `RepeatedInt64`, `Utf8`, `Bytes`, `Fixed32` ou `Fixed64` selon un vrai contrat typé externe. Les champs inconnus peuvent être conservés avec `PreserveUnknown(MZP3WireField)` ; ils ne deviennent pas des champs connus par spéculation. Aucun payload non nommé ne peut être envoyé via cet adaptateur et un payload d'un autre snapshot est refusé.

`Open` applique `MZP0ProtocolAdmission.Review` avec les champs obligatoires `GameMessage.request`, `Request.content`, `Request.uid`, puis chaque champ supplémentaire demandé. Il exige `3.7.10.10`, les deux SHA pins indépendants, un label de provenance non vide, des tags protobuf valides, aucun alias ou tag ambigu et tous les champs requis présents. La lecture et l'écriture MZP3 conservent les budgets (128 KiB message et 64 KiB champ par défaut, borne dure à 1 MiB) et rejettent un framing neutre avec trailing, sous-lecture ou préfixe non canonique.

## Limite essentielle de provenance

Le hash identifie les **octets d'un catalogue** ; il ne prouve pas qu'ils correspondent aux messages du client Dofus 3.7.10.10. Le `ClientVersion` et le `SourceLabel` sont des déclarations fournies par l'appelant : MZP4 ne dispose d'aucune attestation cryptographique du processus client, de la naissance du PID, de sa session, ni du trajet transport/serveur. Il n'ouvre donc **aucune voie d'envoi** ; `IsRuntimeCertified`, `CanSendToGame`, `CanCertifyServerAck`, `IsRuntimeAuthenticated` et `ServerAckCertified` restent **false**. Le seul statut positif de `Open` est `OFFLINE_CANDIDATE_ONLY`, même lorsque SHA et structure concordent. Une vraie autorité live doit provenir d'un producteur indépendant, externe à ce module, avec version/identité/provenance et contrôle de session ; l'adaptateur ne la fabrique pas.

En particulier, les sources historiques `GameCodec` / `Generated/Givre.cs` signalent des numéros obsolètes 3.6 et ne constituent pas un contrat pour 3.7.10.10. Le harness utilise volontairement des slots fictifs `7 / 4 / 6` pour vérifier le branchement par catalogue, jamais comme numéros réseau.

## Exécution reproductible

```powershell
Set-Location %USERPROFILE%\Videos\GIVRE\GivreShareWork
dotnet restore tools/MZP4ProtoIntegrationHarness/MZP4ProtoIntegrationHarness.csproj --ignore-failed-sources
dotnet run --project tools/MZP4ProtoIntegrationHarness/MZP4ProtoIntegrationHarness.csproj -c Release --no-restore
```

Résultat observé : **30/30 PASS**, compilé en `.NET 10` avec `TreatWarningsAsErrors=true` (tests et code source MZP0/MZP3/MZP4 réels). Cas couverts : snapshot versionné, résolution alias/champs, encodage `int64` signé/bool/UTF-8/bytes/repeated/fixed32, roundtrip, préservation d'inconnus, UID -1 et zéro, framing, refus des messages et champs absents, rejet des catalogues croisés, mauvais SHA, ancien client, label manquant, mutation des octets, alias/tags ambigus, troncatures, corruption de payload, framing non canonique et limites strictes.

**Restant pour PRIME :** obtenir les dépendances légitimes de la production et une chaîne de provenance de session authentifiée, puis brancher MZP4 dans le vrai point d'appel sous contrôle d'une admission live séparée. Le harness MZP4 ne compile pas l'application complète et ne la certifie pas. Ne pas substituer MZP4 à `GivreCore` par des classes fantômes.
