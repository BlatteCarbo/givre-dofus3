# MZP6 — inspecteur protocole Avalonia, privé et autonome

**Livré le 16/10/2026 : véritable panneau Avalonia et harness Headless, 43/43 validations PASS.** Un message accepté reste uniquement un candidat de structure Protobuf hors ligne, jamais un ACK ou une session authentifiée.

## Fichiers nouveaux exclusivement

| Fichier | Rôle |
| --- | --- |
| GivreClone.App/Views/MZP6ProtocolInspectorPanel.cs | UserControl Avalonia sombre autonome, saisie HEX volontaire, validation de scope, sortie expurgée |
| tools/MZP6ProtocolUiHarness/MZP6ProtocolUiHarness.csproj | Compile la source du panneau et les vraies sources MZP0/MZP3/MZP4/MZP5 sous .NET 10 Windows avec Avalonia.Headless 12.1.1 |
| tools/MZP6ProtocolUiHarness/Program.cs | Tests Headless avec mappings, aliases et messages 100 % synthétiques limités au harness |
| docs/MZP6_PROTOCOL_UI.md | Présent contrat |

Sources lues uniquement : MZP4OfflinePacketPanel, MZP4TestAccountsPanel, MZP3/MZP4/MZP5 et anciens harness UI. Aucun .csproj App, aucune page existante, aucun client, processus, dossier de comptes, secret, connexion ou fichier Givre modifié.

## Contrat produit et admission

Le constructeur crée un panneau de quatre sections sombres avec accents bleus : statut de catalogue, trame manuelle, résumé de wire et historique catégoriel. La commande Analyser est désactivée au démarrage. Aucun catalogue, alias ou tag synthétique n'est intégré au produit. Aucun chargement de fichier, socket, API MITM, PacketBus, processus ou réseau n'est appelé.

L'intégrateur App appelle explicitement panel.Configure(MZP4ProtocolAdapter catalogue, MZP5SourceScope scope). Cette méthode transmet les deux objets au VRAI MZP5InboundReader.Open, et retourne bool true seulement si le lecteur est un candidat hors ligne. MZP4 exige des catalogues matching/fields et des empreintes SHA fournies par une étape séparée. MZP5 exige version candidate exacte 3.7.10.10, SHA matching/fields cohérents, source et stream déclarés de forme valide, connexion/génération positives, slots Request/Event/Response résolus par mapping. Ces métadonnées sont déclarées et comparées, mais **ne démontrent jamais l'identité réelle du client ou du serveur**.

L'opérateur colle **un seul GameMessage non préfixé** en hexadécimal (ASCII hex + espaces/retours ligne), choisit la direction déclarée et une séquence positive contiguë à partir de 1. La saisie est limitée à 262 144 caractères, soit au plus 131 072 octets décodés. Le panneau rejette texte vide, préfixe 0x, nombres de caractères impairs, autres signes, taille excessive et séquences invalides.

Le panneau appelle réellement MZP5InboundReader.ReadUnframed, lequel utilise le parser MZP3 et les slots issus de MZP4. Pour une observation valide avec alias **connu dans le catalogue fourni**, le rendu affiche UNIQUEMENT branche Request/Event/Response, nombre de champs, types wire, numéros de champs et longueur de leurs contenus. Les valeurs scalaires, chaînes, blobs, UID, alias, code obfusqué, source, identifiants de connexion et empreintes ne sont jamais copiés dans l'UI. L'historique contient au plus 12 catégories expurgées, sans raw data.

En cas de type inconnu, même si MZP5 admet son format neutre, **le panneau refuse le résultat et verrouille cette session**. Version/empreintes/source inconnues ne permettent pas d'appeler Inspect. Wire malformé, séquence manquante/rejouée et direction incohérente déclenchent les refus réels de MZP5 et sa quarantaine par direction. Il est également interdit de rouvrir avec Configure un scope déjà consommé pour contourner un échec. Reprise : fournir explicitement une **nouvelle génération de scope**. Aucun reset, recherche ni connexion automatique.

Après chaque tentative, les octets du tableau temporaire sont remis à zéro et le texte HEX est effacé. Cela ne garantit pas l'effacement physique des chaînes temporaires immuables et objets parser dans le tas .NET ; ne jamais coller de secrets. Aucun contenu n'est écrit sur disque, exporté ou transmis.

**HasAuthenticSource, ServerAckCertified et CanConnect sont toujours false.** L'UI ne déduit ni authentification, ni activité live, ni ACK d'une Response avec UID ou d'un flux séquentiel.

## Validation reproductible

Depuis %USERPROFILE%\Videos\GIVRE\GivreShareWork, exécuter successivement :

1. dotnet restore tools/MZP6ProtocolUiHarness/MZP6ProtocolUiHarness.csproj --ignore-failed-sources
2. dotnet run --project tools/MZP6ProtocolUiHarness/MZP6ProtocolUiHarness.csproj -c Release --no-restore -m:1
3. dotnet build tools/MZP6ProtocolUiHarness/MZP6ProtocolUiHarness.csproj -c Release --no-restore -m:1 -v:q

Le harness instancie une vraie fenêtre Avalonia.Headless, le vrai panneau et les vrais codecs MZP0/MZP3/MZP4/MZP5. **43/43 contrôles PASS** : absence de catalogue par défaut, refus de source/version/SHA, refus de direction/séquence/hex invalide, Event puis Response puis Request issus de vrais builders, absence de fuite des chaînes et nombres privés, UID masqué, pas de faux ACK, alias inconnu, replay, quarantaine, wire malformé, séquence manquante, direction contradictoire, scope consommé refusé, effacement, reconfiguration explicite et historique limité.

**Fictif uniquement en tests :** les numéros 7/5/9 des branches et codes e2/r3/a1 sont des fixtures déclarées uniquement dans le harness et **ne correspondent à aucune prétention sur Dofus 3.7**. La classe produit ne contient aucun fallback de catalogue.

## Points à intégrer par PRIME hors périmètre

Le contrôle est prêt pour injection de dépendances, mais volontairement non raccordé aux pages App car celles-ci et App.csproj étaient explicitement interdits. Un futur propriétaire devra fournir de vrais manifests vérifiés indépendamment, prouver le framing réel, l'origine, la connexion et les réponses serveur avant toute autre fonctionnalité. Aucun accès client/processus/runtime/compte n'a été effectué.
