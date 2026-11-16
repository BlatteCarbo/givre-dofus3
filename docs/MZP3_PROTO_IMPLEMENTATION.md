# MZP3 — vrai codec Protobuf hors réseau, source de production

**Statut au 16 octobre 2026 : `SOURCE_COMPILED / OFFLINE_TESTED`, pas `GIVRECORE_RESTORED`, pas `APP_BUILD_GREEN`, pas `WIRE_3_7_CERTIFIED`, pas `ACK`.**

## Implémentation livrée

Seulement de **nouveaux** fichiers, sans modifier `GivreCore`, les projets existants, les manifests de production ou les fichiers *dirty* :

| Fichier | Rôle réel |
|---|---|
| `GivreClone.Protocol/MZP3RawProtobuf.cs` | Types de champs wire immuables, `MZP3RawProtobuf.TryParse/Parse/ReadVarint/EncodeVarint`, `MZP3WireMessage.Encode`, `MZP3MessageBuilder`, champs opaques, `ParseEmbedded` explicite |
| `GivreClone.Protocol/MZP3FrameAndEnvelope.cs` | `MZP3FrameCodec.TryReadOne/Prefix` (longueur-varint neutre), `MZP3EnvelopeCodec.BuildAny/BuildClientRequest/TryReadClientRequest`, slots d'enveloppe **explicitement fournis** |
| `tools/MZP3ProtoHarness/MZP3ProtoHarness.csproj` | Compile directement les **deux fichiers source du produit** ci-dessus en .NET 10 ; compile comme oracle indépendant `GivreRework/.../P0WirePrimitives.cs` sans le modifier |
| `tools/MZP3ProtoHarness/Program.cs` | Suites exécutables hors réseau : fixtures, malformed/fuzz, comparaison d'implémentations, aller-retour, budget mémoire, lecture d'enveloppe et interdictions d'ACK implicite |

**Namespace : `GivreClone.Protocol`**, préfixe public `MZP3`. Cela rend le code exploitable par le futur `MessageReader`, par le vrai `EnvelopeBuilder` ou un adaptateur source fondé sur ces contrats. Il n'implémente **pas** de classes de noms `GivreCore.Protobuf.RawProtobuf` / `ProtoField` destinées à faire passer les anciennes références au compilateur : un tel remplacement serait une fausse réparation du produit.

## Preuves sources réutilisées

1. **GivreRework** `src/GivreRework.Infrastructure/Protocol/Raw/P0WirePrimitives.cs` : varint strict (10 octets maximum, 10e groupe borné à `0/1`, rejet non canonique), tags `1..536870911` hors `19000..19999`, types wire `{0,1,2,5}`, champs inconnus répétés en ordre, taille par message/champ/nombre de champs, séparation de framing et de vérité runtime. Ce fichier est compilé **inchangé** dans le harness à titre de comparaison différentielle.
2. **GivreActive** `DofusUnityMapping/Protocol.cs` (sections `ReadVarint` et `TryExtractLengthPrefixedFrame`) : validation de préfixes length-varint, rejet des préfixes non canoniques et de la longueur débordante, refus des lectures TCP non bornées. Source consultée en lecture seule, non copiée.
3. **GivreShareWork** `GivreClone.Protocol/EnvelopeBuilder.cs` : `Any { type_url=1,value=2 }`, `Request { content,uid }` et `GameMessage { request }` avec **numéros fournis par catalogue**, `uid=0` omis (proto3), `uid=-1` encodé sur 10 octets, résultat `GameMessage` non préfixé. `PayloadBuilder.cs` définit la forme des valeurs (varint pour nombres/booleans, bytes UTF-8, champ répété) et `CaptureDecoder.cs` consomme des flux préfixés avec champs inconnus. Les anciennes sources ne sont pas modifiées.

La comparaison au lecteur P0 et la fixture de l'enveloppe sont des **preuves de cohérence du codec wire générique**, pas des preuves qu'un message actuel de Dofus 3.7 a ces numéros de champ.

## API concrète et modes fail-closed

```csharp
using GivreClone.Protocol;

// Codec hors réseau ; builder de vrais champs wire 0/1/2/5.
var fields = new MZP3MessageBuilder()
    .AddVarint(1, 150)
    .AddFixed32(2, 0x12345678)
    .AddUtf8(3, "té")
    .AddBytes(4, new byte[] { 0xFF, 0x00 })
    .Build();
byte[] raw = fields.Encode();
MZP3ReadResult parsed = MZP3RawProtobuf.TryParse(raw);
// parsed.Ok == true et parsed.Message!.Encode() est IDENTIQUE à raw.

// Ces slots doivent venir d'un catalogue authentifié séparé, jamais du parser.
var slots = new MZP3RequestSlots(1, 1, 2); // FICTION TEST UNIQUEMENT
byte[] envelope = MZP3EnvelopeCodec.BuildClientRequest(slots, "abc", raw, -1);
bool ok = MZP3EnvelopeCodec.TryReadClientRequest(envelope, slots, out var request);
// ok ne prouve PAS que l'enveloppe a été reçue ou acceptée par le serveur.
```

Le parseur **ne devine jamais** qu'un wire `length-delimited` est une chaîne ou un sous-message (les deux, et les bytes quelconques, ont le même wire type). `GetData()` retourne des octets, `TryGetStrictUtf8()` refuse UTF-8 invalide et `ParseEmbedded()` est un appel volontaire avec profondeur bornée. Les `MZP3WireField` immuables gardent les octets encodés EXACTS. Un champ inconnu, répété ou d'ordre arbitraire survit au `Parse()` / `Encode()` byte pour byte ; `AddRaw()` permet de conserver un champ inconnu en recomposant un autre message. **Option explicite** `allowNonCanonicalValues:true` sur `TryParse()` pour décoder sans perdre un varint-valeur historique non canonique ; tags, longueurs et préfixes restent canoniques même dans ce mode.

Limites par défaut : message **128 KiB**, champ **64 KiB**, **2 048** champs, **12** niveaux de messages embarqués *explicitement* explorés. Bornes maximales : message/champ ≤ 1 MiB, champs ≤ 16 384, profondeur ≤ 64. Échecs documentés par `MZP3WireError` + `ErrorOffset`, message partiel **jamais publié**. Le writer vérifie le total avant allocation finale ; `MZP3FrameCodec` rejette dépassements et trailing si `requireExact=true`. Groupes wire `3/4` **non supportés** volontairement (rejet explicite), pas d'ignorance silencieuse.

**Sécurité de la session :** aucun hook, PID, TLS, réseau, utilisateur, proxy, transport ou appel au processus de jeu. `MZP3ReadResult.ServerAckCertified`, `MZP3RequestPayload.ServerAckCertified` et `MZP3FrameResult.GameFramingCertified` valent toujours **false** ; leur existence est explicite pour empêcher de promouvoir une simple reconnaissance de forme en observation serveur. Aucun envoi/replay n'existe dans les fichiers MZP3.

## Validation hors réseau réelle

```powershell
cd %USERPROFILE%\Videos\GIVRE\GivreShareWork
dotnet restore tools/MZP3ProtoHarness/MZP3ProtoHarness.csproj --ignore-failed-sources -v:q
dotnet run --project tools/MZP3ProtoHarness/MZP3ProtoHarness.csproj -c Release --no-restore
```

**Résultat vérifié : 18/18 suites PASS, .NET 10, `TreatWarningsAsErrors=true`.** Parmi les cas : varint `ulong.MaxValue`, sign-extension UID `-1`, overflow 10e octet, varints non canoniques, champs nuls/réservés, wire groupes interdits, fixed32/64 little-endian, troncatures, tailles négatives, inconnus et répétitions, copie défensive, UTF-8, messages imbriqués, budgets en lecture/écriture, flux length-prefixed fractionné/coalescé, 500 messages aléatoires à décodage différentiel contre la VRAIE source `P0RawProtobufReader` et 1 200 trames aléatoires additionnelles malformées, extraction Any et enveloppe avec cas négatifs, et un vecteur d'octets construit indépendamment.

**Limites de cette validation :** le projet harness compile des fichiers source réels, mais ne compile pas **l'application complète**. `GivreCore` / `GivreLauncher.Core` originaux restent absents, donc `GivreClone.App` demeure structurellement bloqué. Le code MZP3 est directement inclus par défaut dans `GivreClone.Protocol.csproj` via le glob SDK `**/*.cs` ; la construction normale du projet échoue toujours en amont de ce codec en l'absence de ses autres dépendances. Aucune session Dofus 3.7, aucun ACK, aucun décodage de vraie trame authentifiée n'ont été certifiés.

## Suite d'intégration pour PRIME

Brancher **après** restauration de la chaîne source légitime et du mapping 3.7 attesté : appeler `MZP3RawProtobuf` pour les octets neutralisés, construire les `MZP3RequestSlots` depuis les champs réellement certifiés, puis comparer sortie MZP3 et sortie `EnvelopeBuilder` sur les mêmes fixtures hors réseau. Ne pas remplacer l'ancien code ni envoyer de paquets avant cette validation. Garder l'absence de preuve d'identité/ACK comme état `UNKNOWN`, et le moteur sans accès réseau dans ce lot.
