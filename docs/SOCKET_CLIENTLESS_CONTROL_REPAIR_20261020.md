# Socket clientless — récupération du pilotage après connexion (20/10/2026)

## Défaut constaté

Sur le clone Windows .NET 10, la session Dofus `3.7.12.12` utilisant le
catalogue historiquement déclaré `3.7.11.11` a effectivement atteint le jeu :
HAAPI, serveur de connexion, serveur de jeu, ticket accepté, sélection du
personnage, contexte et carte décodés. Pourtant, tout pilotage était impossible.

Dans `SocketProbe.RunOnceAsync`, `compatibilityReview` installait toujours
`RegisterInjector = null` et `UnregisterInjector = null`. La session pouvait
fournir les informations du monde mais `LaunchService.RelayForInject` n'avait
aucun injecteur pour le pid Socket synthétique. Les demandes depuis Lua,
navigation, combat, récolte, HDV et expéditions échouaient **avant tout envoi**.

## Correctif

**État actuel du code :** `reviewRestricted` distingue deux niveaux. Sans
validation additionnelle de version publiée, la revue locale n'ouvre que des
déplacements de diagnostic. Si le service de métadonnées Cytrus confirme que la
version installée est la version publiée, la configuration actuelle permet un
**essai de pilotage complet** (mouvements, combat, Lua, expédition) après
sélection du personnage et contexte de jeu. Cette concordance de version ne
prouve toutefois **pas** que les codes réseau obfusqués ou les accusés serveur
sont identiques : c'est un essai opérateur non certifié, et non une preuve de
compatibilité de gameplay.

Après admission du mapping avec source, cible et empreintes exactes, la session
Socket publie désormais l'injecteur réel, y compris sous revue inter-patch.
Elle ne tombe jamais en repli sur un pid MITM, et le routage conserve les droits
de licence et le plafond de niveau. Les options Turbo restent désactivées en
revue. `SocketGameClient.Inject` conserve ses vérifications de vie de session et
ne traite `true` que comme une **mise en file**, jamais comme un ACK du serveur.

Cette décision corrige une panne d'architecture incontestable ; elle ne prouve
pas que *chaque* paquet de gameplay émis sera accepté. Le champ
`GameplayCertified` de la décision reste `false`. Le **premier mouvement**,
la **première entrée en combat** et les interactions utilisées par une
expédition doivent être corrélés séparément à une réponse réelle côté serveur,
avant de revendiquer un fonctionnement complet.

## Risques et limites

- L'égalité des fichiers `.proto` entre les deux versions est prouvée, mais pas
  l'égalité indépendante de tous les codes obfusqués du transport ni de la
  sémantique serveur pour chaque commande.
- Le reçu `compatibility-review.json` signifie une revue locale de l'opérateur,
  pas une certification cryptographique émise par Ankama.
- La publication de l'injecteur n'accorde aucun droit absent, ne force pas de
  compte à se connecter, et ne doit pas déclencher d'envoi spontané de scripts.
- `GivreLuaHost.Start`, `RunOnce` et `StartRevive` vérifient désormais aussi
  centralement que le canal de commande est enregistré pour le PID Socket.
  Ceci protège les chemins Planning/Équipe/Superviseur qui appellent le host
  directement sans passer par le bouton Lua ; un refus ne lance pas de tâche.
  Les scripts continus exigent le canal complet ; les sessions restreintes ne
  peuvent tenter que des déplacements manuels ponctuels.
- Une connexion Socket affichée « En jeu » prouve la lecture et la présence du
  personnage, pas la réussite de déplacements, combats ou expéditions.
- Pour le diagnostic en conditions réelles, tester une action *manuelle*
  clairement choisie, relever son type de demande, son écho/réponse et le nouvel
  état du personnage. Arrêter en cas de `REFUSED`, timeout ou désynchronisation.

## Validation attendue

- Tests d'admission avec marqueur Dofus installé, source/bundle historiques et
  reçu SHA inchangé.
- Contrôle de source garantissant que la revue ne coupe plus l'injecteur et que
  Turbo reste désactivé.
- Build Release et tests des fonctionnalités Socket, Lua, navigation/expéditions.
- Aucune modification de `GivreActive`, `MizanBot` ou du client Dofus installé.

Ce dossier documente la correction statique et les limites ; il **ne constitue
pas** un résultat de test live de déplacements/combat/expédition.

## Résultats de validation locale — 21/10/2026

- Compilation de `GivreClone.App.csproj` en Release : **PASS**, 0 erreur,
  198 avertissements préexistants dans ce build complet.
- Suite `GivreClone.Tests.csproj` Release : **2175 PASS, 2 SKIP, 0 FAIL**.
- Contrats Socket/UI/Lua/expédition/version ciblés : **83 PASS, 0 FAIL**.
- Harness de mouvement Socket et PID synthétique : **10 PASS, 0 FAIL**,
  aucun client Dofus ni réseau jeu utilisés.
- `git diff --check` : **PASS**, aucune anomalie de whitespace.

Le client Dofus actif sur le poste n'a pas été pris en main pendant cette
validation. Les capacités effectivement acceptées par son serveur demeurent à
mesurer par une session de test explicitement contrôlée, distincte de ces PASS.
