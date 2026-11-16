# MZP0 — garde C# de catalogue (16/10/2026)

GivreClone.Protocol/MZP0ProtocolAdmission.cs est un contrôle de source uniquement,
compilable en .NET 10 indépendamment de GivreCore. Il examine les deux catalogues
UTF-8 (matching_current.json, fields.json) avant leur emploi :

- Sous-version exigée exactement 3.7.10.10 ; contrôle SHA-256 des deux octets
  originaux avec des empreintes attendues fournies par un composant indépendant
  de l'émetteur, jamais par une déclaration du serveur elle-même.
- Budgets d'octets/messages/champs, JSON strict, codes et noms univoques,
  détection des clés JSON dupliquées, tags Protobuf interdits.
- Manifeste explicite de couples message/champ requis ; conservation de tous
  les noms en collision sur le même tag, état RequiresReview si ambiguïté
  ou champ manquant est observé.

Même OfflineCandidateOnly ne signifie jamais session, connexion, ACK ou
compatibilité en jeu : PermitsGameplay vaut toujours false. L'application
appelante doit mettre en place, en plus, la provenance indépendante, le
PID/heure de naissance/build/empreinte du client, les contrôles de flux entrant
et la preuve des réponses serveur. L'outil seul ne certifie pas ces éléments.

Point de raccord futur vérifiable : GivreClone.App/Services/MappingCatalog.cs,
FetchCoreAsync autour de ProtocolCatalog.LoadFromStrings et publication
_streamed / événement Ready (actuellement publication possible malgré
MissingRequiredFields). Avant un raccord, préserver la branche réellement
utilisée et ne pas bloquer arbitrairement les versions 3.6 de l'application
existante. Le catalogue GivreRework data/3.7 demeure candidat et contient
des collisions : le test du garde doit rester négatif sur ces entrées.

Validation isolée, sans lancement d'app ni réseau :

    dotnet test tools/MZP0ProtocolAdmissionHarness/MZP0ProtocolAdmissionHarness.csproj -c Debug -m:1

Ce harness lie la véritable source de production et teste notamment
la conformité synthétique, les collisions, les doublons, les manquants, les
SHA divergents, la mauvaise version et les tags réservés. Aucun binaire Givre
ni compte utilisateur n'est requis.
