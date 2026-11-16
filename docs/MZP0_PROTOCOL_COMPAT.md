# MZP0 — garde de compatibilité protocole hors ligne

**Périmètre :** comparaison du **code source** `GivreShareWork/GivreClone.Protocol` aux deux **catalogues candidats** 3.7 de `GivreRework/data/3.7`. Aucune interrogation de processus, réseau, DLL en cours d'exécution, session, compte ou capture. La parité avec **GivreClone.exe** n'est pas prouvée par ce dépôt : tout résultat du script conserve `exe_parity: NOT_VERIFIED`.

## Constat vérifié sur les sources présentes le 16/10/2026

| Élément source | Réemploi possible | Limitation / conflit avec la cible 3.7 |
| --- | --- | --- |
| `GivreClone.Protocol/ProtocolCatalog.cs:41-73,118-169` | Chargement indépendant du disque ou de chaînes JSON, résolution par nom de message/champ ; base intéressante pour des diagnostics | `LoadFields` fait `byNumber[num] = field.Name` (ligne 130), ce qui **perd silencieusement** les autres noms en cas de collision de tags. Un dictionnaire inverse `int -> string` n'est pas sûr sur ces catalogues. `LoadFromStrings` accepte en outre des catalogues absents. |
| `GivreClone.Protocol/RequiredMapping.cs` et `.Extra.cs` | Inventaire de couples *message/champ* nécessaires aux lectures et forges ; reprise de l'idée de couverture statique | La présence d'un nom n'établit ni numéro unique, ni sémantique de la version cible. Le garde conserve les noms en collision. |
| `GivreClone.Protocol/EnvelopeBuilder.cs:21-62,79-96` | Exemple de résolution **dynamique** des slots d'enveloppe (`GameMessage`, `Request`, `Event`, `Response`) avant écriture | Le port exige tous les champs présents, non ambigus et vérifiés sur la version du client ; aucun envoi sans preuve du framing + transport réel. |
| `GivreClone.Protocol/GameCodec.cs:9-19` | Exemple de `Google.Protobuf` et varint préfixé | **Non réutilisable tel quel** : le commentaire source le dit figé en `3.6.10.11`, slots générés périmés depuis `3.6.11.12`. |
| `GivreClone.App/Services/Transport/Socket/ProtoWire.cs:13-115` | Encodage minimal varint/longueur pour la connexion | `Reader.Varint` avance sans contrôle préalable de borne ni plafond du varint ; pas de preuve de sûreté sur flux fragmenté. Reprendre les exigences des tests de framing 3.7, pas ce lecteur tel quel. |
| `GivreClone.Protocol/RequestCorrelator.cs:3-15` | Alloue un UID croissant | Le commentaire dit explicitement que la corrélation stricte réponse/UID est ultérieure : UID alloué **ne prouve pas** ACK serveur. |
| `GivreClone.App/Services/MappingCatalog.cs:65-97` | Point de jonction entre bundle reçu et catalogue | Le contrôle `MissingRequiredFields` journalise des manquants, puis publie quand même `_streamed` et `Ready`; prévoir un *gate* bloquant avant promotion. Le build demandé au serveur est `latest` (ligne 20), sans identité client vérifiée. |
| `GivreClone.Engine/DofusCaptureHost.cs:53-76` | **Lecture source seulement** | Son `Poll` énumère **tous** les PID Dofus puis tente d'injecter une DLL. **Ne pas lancer** `BotHost.Start`/`Capture.Start`, tests end-to-end ou console de démonstration tant que GivreClone tourne. |

Les octets et SHA-256 candidats de GivreRework sont consignés dans `../GivreRework/data/3.7/PROVENANCE.md` (fichiers utilisateur importés le 15/10). Le script épingle indépendamment :

- `matching_current.json` — 89 144 octets, SHA-256 `c1da4e5090d07c41cc5f6e0d74abe0cdb4777db08ccd2c73de2692e0a4a2dc01` ;
- `fields.json` — 295 299 octets, SHA-256 `58ba845bf9057556127bbe683617f07acbb9dde9824c8be81c70fe1744910f19`.

**Résultat observé :** 1 337 noms de messages distincts, 3 725 types avec champs, **487 couples requis** par `RequiredMapping` (`Scanned ∪ Extra`), **un champ requis absent** `ClientChallengeInitRequest.fyvk`, et **deux couples requis ambigus** : `FightPlacementPositionRequest.entity_id` partage le tag `#2` avec `actor_id`, `MapMovementEvent.character_id` partage le tag `#4` avec `actor_id`. Les SHA-256 du manifeste de déclarations Givre sont inclus dans chaque rapport. Ces observations portent seulement sur le contrat source et le candidat 3.7, sans équivalence établie à un vrai binaire GivreClone ni sous-version 3.7.10.10 authentifiée sur trame. Le champ `requested_target_version` du JSON désigne le **build visé**, et `client_build_attested: false` précise qu'aucun exécutable de ce build n'a été attesté.

## Exécution reproductible

Depuis `GivreShareWork` en PowerShell (commande **strictement hors ligne**, elle lit les sources et les deux JSON) :

```powershell
py -3 -B tools/MZP0_ProtocolTests.py
py -3 -B tools/MZP0_ProtocolGuard.py --source-root . --target-dir ../GivreRework/data/3.7
```

Le code retour `2` de la seconde commande est **attendu** tant que la cible source n'a pas été attestée : JSON `verdict: SOURCE_ONLY` avec les champs manquants, collisions et hachages. Les erreurs de fichiers manquants, mauvais SHA, mauvais build, JSON à clés dupliquées, noms/code ambigus et tags illégaux conduisent à `BLOCKED` et code `2`.

Pour comparer deux corpus explicitement fournis hors ligne, ajouter `--source-bundle <dossier>` et `--source-manifest <fichier.json>`. Les deux sont obligatoires ensemble. Format du manifeste local :

```json
{
  "client_version": "3.7.10.10",
  "origin": "authorized_offline_extract",
  "files": {
    "matching_current.json": {"size": 89144, "sha256": "SHA256_DU_FICHIER_SOURCE"},
    "fields.json": {"size": 295299, "sha256": "SHA256_DU_FICHIER_SOURCE"}
  }
}
```

Les tailles affichées dans cet **exemple de schéma** sont celles du candidat Givre ; remplacer tailles et SHA par les valeurs exactes du corpus source indépendant. L'autre origine déclarative acceptée est `user_supplied_corpus`. Une déclaration d'origine par JSON garantit seulement la **reproductibilité** des octets contrôlés, pas l'authenticité de l'EXE ni la version du client ; la provenance de cet EXE doit être vérifiée séparément par le PRIME. Le script compare les ensembles d'alias et les tags par couple, avec différences exposées (100 premiers éléments maximum, total exact). `CATALOGUE_CONCORDANT_CANDIDATE` et code `0` exigent une concordance totale sans champs requis absents et sans ambiguïtés ; ce verdict **n'autorise aucune émission**. `REVIEW_REQUIRED` et code `2` signalent le drift ou une collision.

## Instruction d'intégration ciblée pour PRIME

1. Ajouter ultérieurement un **adaptateur read-only** qui exécute un garde équivalent avant la promotion d'un catalogue au point `MappingCatalog.FetchCoreAsync` (`GivreClone.App/Services/MappingCatalog.cs:65-96`) ou au point d'ingress P0 de GivreRework. Vérifier identité du client/source, empreintes, noms ↔ codes, tous les couples requis, collisions et slots de l'enveloppe ; laisser le décodeur et les actions désactivés tant qu'un prérequis est absent. Ne pas réutiliser le vieux `GameCodec` généré ni le `ProtocolCatalog` inverse pour les tags non uniques.
2. Conserver la **liste** de noms candidats pour un tag donné (`Dictionary<int, IReadOnlyList<string>>` ou équivalent) jusqu'à désambiguïsation par structure/observations privées authentifiées. La collision n'est pas résolue en prenant arbitrairement la dernière entrée JSON. Inscrire un état explicite `UNKNOWN/AMBIGUOUS` dans les diagnostics plutôt que `0`/faux succès.
3. Avant toute revendication de **parité GivreClone.exe**, inventorier un **EXE local autorisé et arrêté**, consigner son SHA-256 et sa version et établir une correspondance binaire ↔ comportement. Ce garde ne décompile pas d'EXE ; il n'en cherche pas et ne se connecte à aucun processus. Seul un corpus réel expurgé, capturé dans une session autorisée et attribuée, pourra établir le décodage entrant ; seul un événement serveur corrélé pourra établir un ACK. La chaîne `DofusCaptureHost` doit rester inactive sur cette machine pendant la session GivreClone préexistante.

`tests` : `tools/MZP0_ProtocolTests.py` comporte 18 scénarios dont provenance absente, dérive SHA, version incorrecte, doublons JSON, décalage de tags, collisions, manifeste incomplet, entrée surdimensionnée et corpus source invalide. Aucun test ne lit une session, aucun outil ne démarre GivreClone/Dofus.
