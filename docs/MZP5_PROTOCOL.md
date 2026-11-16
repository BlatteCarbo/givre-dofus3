# MZP5 — contrat inbound neutre, versionné, hors réseau

**16 octobre 2026 : code de production compilé, validation hors ligne 133/133 PASS en .NET 10 Release.**

## Sources et rôle

Le code existant CaptureDecoder.Feed(pid, clientToServer, bytes) déframe puis décode des paquets et les diffuse à l'application. BotMessage contient Seq, Direction, ConnectionId, Code, Name et Payload ; PacketBus publie les messages reçus. RequestCorrelator alloue des UID sans démontrer l'existence d'une réponse serveur.

EnvelopeBuilder définit GameMessage avec trois branches (Request, Event, Response) et des champs de contenu/UID dont les numéros proviennent du catalogue. Les anciens slots ont déjà changé d'un patch à l'autre. Les sources existantes MZP0ProtocolAdmission, MZP3RawProtobuf, MZP3FrameAndEnvelope, MZP4ProtocolAdapter, EnvelopeBuilder, CaptureDecoder, BotMessage, PacketBus et RequestCorrelator ont été inspectées en lecture seule.

Nouveaux fichiers uniquement :

| Fichier | Rôle |
| --- | --- |
| GivreClone.Protocol/MZP5InboundProtocol.cs | MZP5InboundReader, métadonnées source/sens/séquence déclaratives, lecteur GameMessage, quarantaines par direction, diagnostics privés |
| tools/MZP5ProtocolHarness/MZP5ProtocolHarness.csproj | Compile les VRAIES sources produit MZP0, MZP3, MZP4, MZP5 sans shim GivreCore |
| tools/MZP5ProtocolHarness/Program.cs | Teste des trames synthétiques et cas négatifs sur le vrai code |
| docs/MZP5_PROTOCOL.md | Cette documentation |

## Contrat public prêt pour intégration

MZP5InboundReader.Open(MZP4ProtocolAdapter catalogue, MZP5SourceScope scope, MZP3Limits? limits) vérifie les entrées et retourne un MZP5InboundOpenResult. Le résultat positif n'est que OFFLINE_SHAPE_ONLY. Le scope contient SourceLabel, StreamKey, ConnectionId, Generation, DeclaredClientVersion, MatchingSha256 et FieldsSha256. Ces sept attributs sont **des déclarations**, non une attestation du transport. Version exacte requise : 3.7.10.10. Les deux empreintes doivent correspondre au snapshot MZP4 (déjà vérifié structurellement et SHA-pinné).

Les noms de champs exigés dans le même catalogue sont GameMessage.request, GameMessage.event, GameMessage.response, Request.content, Request.uid, Event.content, Response.content et Response.uid. Les trois tags de branches doivent être distincts, comme les champs content et uid de leurs messages respectifs. Aucune valeur de tag Dofus n'est codée en dur. Le harness utilise des positions fictives 7/5/9 pour exercer le comportement uniquement.

MZP5InboundReader.ReadUnframed(MZP5InboundObservation observation, ReadOnlySpan<byte> data) accepte **un seul GameMessage NON PRÉFIXÉ** déjà isolé par un futur déframer dont la provenance est indépendante. Le déframing TCP n'est ni implémenté ni revendiqué. Chaque observation répète le scope attendu, la direction ClientToServer ou ServerToClient **déclarée**, et la séquence positive.

Pour un message valide, MZP5InboundResult.Packet contient MZP5DecodedPacket : branche Request/Event/Response, direction/séquence déclarées, code, nom clair seulement si alias connu, UID facultatif et arbres wire Root, BranchFields, AnyFields, Payload. Ces arbres MZP3 conservent EXACTEMENT ordre, répétitions et champs inconnus ; leur méthode Encode réémet les octets de chacun des niveaux. Un code non reconnu mais syntaxiquement valide garde son code et ClearName nul. Le Any.value absent signifie payload vide conformément à proto3.

Le parser refuse les enveloppes avec plusieurs branches oneof, un contenu/UID répété ou mauvais wire type, une URL Any incorrecte, des tags illégaux, groupes, UTF-8 invalide, corruption de varint, payload tronqué et limites MZP3 dépassées. La profondeur des trois sous-messages est explicitement bornée. Un échec ne retourne JAMAIS un paquet partiel.

## Isolation séquences et états privés

Chaque instance traite un seul scope logique. La séquence commence à 1 et doit rester strictement contiguë pour **chaque direction séparée**, sous verrou : un doublon reçu en concurrence ne peut être publié qu'une seule fois. Une séquence manquante, dupliquée, remise en ordre, un message malformé ou une direction incompatible met en quarantaine la direction concernée. Toute observation suivante de cette direction est rejetée StreamQuarantined ; l'autre direction conserve son propre état.

Une mauvaise source, connexion, génération, empreinte ou version est rejetée sans déplacer le curseur d'un flux attendu. Il n'existe ni reset silencieux ni rattrapage automatique. La reprise exige une **nouvelle instance avec nouvelle génération** et une décision d'identité externe. Attention : le module ne persiste pas de séquence entre plusieurs objets Reader ; l'intégrateur doit gérer leur propriété et empêcher l'ouverture non contrôlée d'un nouvel objet pour la même génération.

Snapshot() expose seulement compteurs client/serveur, nombre de rejets et indicateurs de quarantaine, sans code, source, nom ni octets. Les payloads retournés par Packet sont structurés pour l'intégrateur autorisé mais peuvent être privés : aucune sortie de journal, export brut, transport ou publication automatique n'existe dans MZP5.

**Tous les indicateurs IsAuthenticated, ServerAckCertified, ReplayAuthorized et IsRuntimeCertified sont toujours FALSE.** Le fait de lire une Response avec UID, un libellé ServerToClient ou une séquence monotone ne prouve JAMAIS un ACK serveur. Le résultat accepté signifie seulement structure décodable. MZP5 n'a aucune interface d'envoi, de socket, de hook, de PID ou d'injection.

## Reproductibilité des tests

Dans la racine Windows %USERPROFILE%\Videos\GIVRE\GivreShareWork :

1. dotnet restore tools/MZP5ProtocolHarness/MZP5ProtocolHarness.csproj --ignore-failed-sources
2. dotnet run --project tools/MZP5ProtocolHarness/MZP5ProtocolHarness.csproj -c Release --no-restore -m:1

Résultat observé : **133/133 PASS**, vrai code produit MZP0/MZP3/MZP4/MZP5, compilé sous .NET 10 avec TreatWarningsAsErrors. Couverture : Request réel depuis builder MZP4, Event et Response neutres, UID signé, alias inconnu, unknown fields byte-for-byte à quatre niveaux, séquences replay/gap, versions/sources/connexions/générations divergentes, doublon concurrent, malformations wire, cases de framing ambigu, limites et catalogues incomplets. Aucun fichier source précédent n'a été édité.

## Maillons externes encore nécessaires

PRIME pourra ultérieurement relier cette verticale à un vrai producteur inbound versionné, un déframer démontré sur Dofus 3.7.10.10 et un consommateur protégé, avec une **preuve indépendante de l'identité/connexion** et une **preuve distincte de la réponse positive serveur**. Le code MZP5 ne fabrique aucune preuve, n'atteste aucun gameplay réel et n'accède à aucun processus, réseau, compte, secret ni dump.
