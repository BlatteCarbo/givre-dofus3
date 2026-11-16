# Socket clientless — correctifs de transport et limites (20/10/2026)

Demande utilisateur : « fix le socket il fonctionne pas ».

## Défauts vérifiés dans le code (pas une validation serveur)

1. `SocketConnectionClient` présumait que toute `IdentificationSuccess`
   contenait déjà une liste de personnages et déduisait `NoCharacter` de
   métadonnées incomplètes, sans attendre l'événement séparé
   `CharacterListResponse`. Maintenant les deux flux (`CharactersEmbedded`
   ou `CharacterList`) sont traités séparément, quelle que soit leur arrivée.
2. `SocketGameClient.Inject()` acceptait les injections **avant la sélection du
   personnage** et parfois après la coupure, dans une queue **illimitée** ; le
   retour `true` pouvait donner l'illusion d'un mouvement ou craft accepté.
   Maintenant la queue refuse les actions tant que le serveur n'a pas accepté
   la sélection, redevient indisponible avant la fermeture, est bornée à 256
   trames et refuse plutôt que perdre silencieusement la 257e. Taille d'une
   trame bornée à 2 MiB, sans acceptation de null/vides.

## Ce qui n'est PAS certifié

- Une mise en file n'est pas un ACK serveur : seule l'observation du message
  de jeu correspondant permet d'affirmer une action réussie.
- Le mapping local peut rester incomplet (`ClientChallengeInitRequest.fyvk`)
  et sa liaison à la version Dofus réellement installée peut être absente.
  Il faut un mapping complet, versionné et éprouvé sur un compte/test
  **autorisé** avant de présenter Socket comme « opérationnel ».
- Aucune authentification, login live, réseau gameplay, secret de compte,
  client officiel ou deuxième compte n'a été utilisé pour ces correctifs.

Validation : `GivreClone.Tests/SocketTransportRegressionTests.cs` contient
6 régressions offline sur les contrats de production et sur la sémantique
de saturation d'une file .NET. Un test de source ne remplace pas une
preuve de réponse serveur ni l'instrumentation Socket réelle.
