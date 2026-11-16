# Road & Quest Creator Givre v4.0 — quêtes et donjons

## Ce qui est implémenté

Le script `tools/RoadCreator_Givre_v4_0.lua` est un **enregistreur passif** :
il observe les paquets du personnage sélectionné en MITM, ne fabrique
aucune requête réseau et sauvegarde le résultat dans
`%APPDATA%/GivreClone/scripts/Road_<timestamp>_<random>.lua`.

| Catégorie | Capture | Replay |
| --- | --- | --- |
| Cartes / retours dans une même zone | Visites **chronologiques**, sans fusion | Séquentiel, contrôle strict de carte |
| Bord / portes / interactifs | Direction, cellule, type si connus | `path`, `door`, `map:useCell` |
| Déplacements intra-carte | Dernier keyMovement si disponible | `map:moveToCell` |
| Zaap / zaapi / havre-sac | Destination et source si reconnues | Mot-clé de téléportation, prérequis non inventés |
| Dialogues PNJ | Action, modèle PNJ si visible, identifiants de réponse et question | `npc:npc`, `npc:hasReply`, `npc:sendReply` |
| Fermeture de dialogue | Événement explicite | `global:leaveDialog` |
| Consommables | UID converti en GID via inventaire reçu | Utilisation d'**un** exemplaire |
| Quêtes | Début, étape, objectif et validation observés | Journal de contrôle + `quest:validateObjective` pour demande explicitement capturée |
| Émotes, commerce, objet ciblé | Action et type journalisés | **Arrêt avec instruction manuelle** (pas de requête improvisée) |
| Combats / récolte | Choix par carte | `fight` / `gather` disponibles dans la route |

Le script produit deux modes : `RC_MODE="quest"` est choisi automatiquement
dès qu'une interaction autre qu'un déplacement est capturée ; `RC_MODE="route"` restitue
le circuit classique (moins adapté aux quêtes avec revisites).

En mode quête, les étapes et actions sont conservées dans un ordre précis.
Les curseurs `memory:set/get` sont persistants **par personnage**.
À la relance après coupure, le script ne saute pas sur une autre carte :
il vérifie la carte actuelle et s'arrête si elle ne correspond pas à
l'étape attendue. La validation d'une action est enregistrée après
l'envoi confirmé par l'API GivreClone, pas après garantie de réussite
de la quête par le serveur.

## Comment enregistrer une quête

1. Dans GivreClone (source recompilée incluant l'API Road Creator), ouvrir
   `tools/RoadCreator_Givre_v4_0.lua` et **lancer** le script sur le personnage.
2. Jouer manuellement la quête : déplacer le personnage, cliquer les PNJ,
   choisir leurs réponses, interagir avec les portes/leviers, utiliser les objets.
3. Sur chaque nouvelle carte, répondre à la fenêtre combat/récolte si activée.
4. Arrêter l'enregistrement. Le fichier généré est sauvegardé après
   chaque interaction et après chaque transition.
5. **Relire et compléter** les tâches non rejouables, puis lancer le
   fichier généré pour tester chaque objectif, sans supposer un donjon
   réexécutable automatiquement.

Pour recommencer **délibérément** : mettre `RC_RESET_PROGRESS=true` dans
le script généré, puis repasser à `false`. Il réinitialise les curseurs
de ce script uniquement. `RC_REPEAT=true` ne répète que si le dernier
passage revient **effectivement** à la première carte ; réglage
désactivé par défaut (quêtes potentiellement non répétables).

Après avoir effectué **soi-même** une action non rejouable (émote,
échange, commerce...), mettre `RC_SKIP_CURRENT_MANUAL=true` dans
le script généré avant de relancer : seule l'action manuelle
actuellement bloquante est ignorée, puis le réglage repasse à `false`
dans la session. Ne pas l'utiliser avant d'avoir réellement effectué
l'action correspondante en jeu.

## Limites importantes

- Un PNJ est identifié par son modèle stable quand il apparaît dans les
  acteurs connus. À défaut, seul l'acteur contextuel est enregistré :
  **à remplacer manuellement** pour un replay après reconnexion.
- Les réponses PNJ sont conservées par **ID**, mais l'API de question peut
  ne pas refléter toutes les conditions d'une quête : toute divergence
  déclenche un arrêt au lieu de sélectionner une autre réponse.
- Des paquets inconnus, erreurs de mapping et versions Dofus différentes
  peuvent masquer des actions. Aucun support garanti des puzzles, émotes,
  échanges, trades, achats HDV, combats scriptés, passage de donjon à clé
  ou dialogue à conditions complexes.
- Les validations de quête observées sont des **commentaires d'audit**.
  Elles ne prouvent pas que les prérequis seront identiques au replay.
- `io.open` n'est disponible qu'avec le correctif C# du dépôt
  `GivreLuaHost.RoadCreator.cs` : GivreClone déjà lancé et anciens EXE
  ne sont **pas** mis à jour à chaud.
- La copie `GivreShareWork` est incomplète pour compiler :
  `../../GivreProxy/GivreCore/GivreCore.csproj` manque. Ne pas prétendre
  que l'intégration Windows est validée tant que build/test réels ne passent pas.

## Vérifications

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-RoadCreatorQuestV4.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-RoadCreatorGivre.ps1
```

Le premier test exécute **l'enregistreur** dans MoonSharp avec paquets
simulés (PNJ, questions/réponses, validations, mouvements, objets,
interactifs, revisite et téléport), puis exécute **le fichier généré**
dans une deuxième instance Lua simulée. Il vérifie l'ordre et
l'arrêt sécurisé pour une action qui exige une intervention manuelle,
ainsi que le refus d'une réponse PNJ lorsque le serveur présente
une question différente.

L'enregistreur natif de l'interface conserve désormais les dialogues
qui **ne changent pas de carte**, les réponses par ID quand leur index
visible est inconnu et un journal des événements de progression de quête.
Le test C# isolé compile et exécute le véritable `RoadRecorder.cs`,
sans la dépendance GivreCore manquante :

```powershell
dotnet run --project tools/RoadRecorderQuestOffline/RoadRecorderQuestOffline.csproj -c Release
```

Le mode quête **chronologique**, avec revisites et état persistant, reste
spécifique au script Lua v4. Ne pas supposer que l'ancien export natif
fusionné par carte gère toutes les quêtes répétant un lieu.

Références :
- https://doc.ankabot.dev/installation/road-creator
- https://doc.ankabot.dev/ankabot-pc/methodes/quest
- https://doc.ankabot.dev/ankabot-pc/mode-developpeur/developer
