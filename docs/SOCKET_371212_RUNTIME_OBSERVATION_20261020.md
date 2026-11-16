# Dofus 3.7.12.12 — observation Socket réelle (20 octobre 2026)

## Portée

Observation non intrusive de **GivreClone** déjà ouvert, après le commit
`4e93c79`. Aucun compte, jeton, mot de passe, IP distante, nom de personnage,
payload protobuf ni trame brute ne sont conservés dans ce rapport. Les journaux
ont été lus en partage depuis le dossier privé du clone, sans déclencher de
nouvelle connexion ni manipuler l'autre processus MizanBot.

## Observations runtime

* Client installé : `3.7.12.12` ; source de mapping déclarée : `3.7.11.11`.
* Un reçu local `compatibility-review.json` lié aux fichiers existants était présent.
* Le journal de la session commencée vers 22:29 contient une revue de compatibilité
  inter-patch et ne rapporte pas `MappingBuildMismatch`.
* Authentification HAAPI, connexion TCP au serveur de connexion, authentification,
  sélection du serveur, connexion au serveur de jeu, acceptation du ticket,
  sélection du personnage et `ContextCreationEvent` ont été constatés.
* Une connexion réseau de l'application au port de jeu `5555` était établie lors
  de l'observation ; aucun processus `Dofus.exe` n'a été observé dans le même relevé.
* Le tableau de bord montrait `En jeu`, un serveur, une carte, des caractéristiques
  de personnage, et **624 paquets reconnus sur 624** à l'instant du relevé UI.
* Extraits de comptage du journal, à un instant différent : **35** lignes
  `[socket-tx]`, **671** lignes `[socket-rx]`, présence de
  `MapComplementaryInformationEvent`, `MapMovementEvent`,
  `SequenceNumberRequest/Event`, `BasicLatencyStatsRequest/Event` et
  `CharacterSelectionEvent`. Ces chiffres ne sont pas des métriques de fiabilité
  sur la durée ; certains événements correspondent à d'autres entités présentes
  sur la carte.

**Conclusion limitée :** le mode clientless s'est connecté et a effectivement
décodé des messages de jeu avec ce mapping historique sur le client installé
`3.7.12.12`. L'incompatibilité stricte de version n'est plus un blocage pour
**l'essai de connexion**. Ce succès ne prouve ni une identité exhaustive de tous
les IDs de message, ni la validité des actions automatisées, ni leurs ACK serveur.

## Ajustement UX

La notification d'ancienne version `LocalVersionNotice` reçoit désormais la
décision Socket courante, elle-même calculée **après** vérification de
l'empreinte du client installé. Si la paire est un candidat inter-patch admis,
la fausse alerte générale de « mise à jour incompatible » est remplacée dans
le panneau d'audit par une mention d'**essai Socket autorisé en revue**.
Le diagnostic local garde un avertissement explicite : **pilotage, scripts et
ACK gameplay non certifiés**. Tout autre build ou reçu invalide maintient
l'ancienne alerte. Les mappings historiques ne sont pas renommés.

## Validation

* Tests ciblés `LocalVersionNoticeTests` et `InstalledDofusVersionBindingTests` :
  **21/21 PASS** sur compilation Release. Le test explicite de la machine
  s'exécute seulement avec les deux variables `GIVRE_TEST_*` documentées.
* Build Release de l'application dans un **OutputPath séparé** : **0 erreur**,
  **197 avertissements** issus de l'ensemble des projets compilés.
* L'ancienne session Socket était active **au moment du relevé**. Lors du
  contrôle ultérieur, le clone n'était plus ouvert et des processus Dofus
  étaient présents. Aucune cause de cette fermeture n'est attribuée sans
  preuve. La nouvelle interface compilée **n'a pas été relancée** pour ne pas
  agir sur les sessions Dofus désormais ouvertes.

## Restant à démontrer

Une preuve aller-retour par **type d'action** est nécessaire avant d'activer
l'injecteur de déplacements, Lua, combat, récolte ou HDV en mode inter-patch.
Il faut associer une action sortante autorisée à sa réponse/ACK et à un nouvel
état serveur mesuré, notamment pour les déplacements, en isolant la session de
test et sans déduire cette preuve de simples événements de carte. La session
actuelle seule ne suffit pas à certifier un pilotage automatique.
