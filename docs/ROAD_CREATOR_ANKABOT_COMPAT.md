# Road Creator AnkaBot → GivreClone

## Utilisation

Le bot dispose déjà d'un **enregistreur natif** dans l'onglet Bots : il observe les
transitions et propose un fichier de trajet depuis l'éditeur. Il est recommandé
pour les passages PNJ, interactions et portes.

Pour le **script Lua passif** inspiré du Road Creator de Pharwell/AnkaBot,
ouvrir `tools/RoadCreator_Givre_v3_1.lua` dans l'éditeur Lua du bot, puis
**Lancer**. Se déplacer manuellement dans Dofus ; **Arrêter** pour terminer.
Le script ne pilote aucun déplacement : il enregistre les paquets de la
session déjà attribuée au personnage et génère :

`%APPDATA%/GivreClone/scripts/Road_<timestamp>_<random>.lua`

Une version exécutable complète est sauvegardée **à chaque changement de
carte**. Les réglages `RC_ASK_FIGHT`, `RC_ASK_GATHER`,
`RC_PREFER_MAPID` et `RC_INCLUDE_CELLID` sont en tête de script.
Les prompts récolter/combattre utilisent la modale GivreClone existante.

## Éditeur visuel (onglet Trajets)

Sans parcourir le trajet : cliquer les cartes dans l'ordre sur la carte du monde
(clic droit = retirer, glisser = déplacer, molette = zoom, « + Ma carte » = carte
du perso). Chaque étape est vérifiée sur le graphe du jeu : vert = à pied (nombre
de cartes), orange = zaap / havre-sac nécessaire, rouge = injoignable ou carte
absente du graphe. Les cartes en double sont signalées (une carte n'a qu'une
sortie dans un trajet : la dernière l'emporte).

Le script généré vise la carte suivante par son id (`changeMap = "<id>"`) : le
moteur calcule le chemin lui-même, aucune carte intermédiaire à saisir. Avec
« Banque auto », la banque la plus proche (repères du jeu) est appelée par
`bank()` quand les pods atteignent `MAX_PODS`, porte du bâtiment comprise. Jamais
de `npcBank` dans `move()` : l'étape redéposerait à chaque tick sans repartir.

Actions : Lancer (refusé tant qu'une étape est rouge), Vers l'éditeur Lua,
Enregistrer (`scripts/Road_<nom>.lua`), Optimiser l'ordre, Inverser. Un trajet
généré ici porte une ligne `-- @road` et se rouvre à l'identique ; un trajet de
l'enregistreur ou écrit à la main est repris au mieux (étapes `map = …`).

## Compatibilité du Road Creator AnkaBot original

Le moteur MoonSharp de GivreClone conserve son bac à sable. Le pont ajoute
seulement un sous-ensemble de `io.open` pour les fichiers plats
`Road_*.lua` directement sous `scripts/` : modes `r`, `w`, `a`,
`read('*all')`, `write(...)`, `close()`; taille maximale 512 Kio.
Les chemins relatifs avec sous-dossiers, chemins absolus, jonctions et autres
fichiers restent interdits. `io.popen` n'est pas exposé.

`global:rcAskFight()` et `global:rcAskGather()` retournent les fragments Lua
attendus par le script historique (des chaînes, et non les anciens stubs
`false`). Les hooks sortants `developer:registerMessage` étaient déjà
supportés dans la couche de paquets GivreClone ; les noms de messages
doivent exister dans le catalogue du client.

## Portée et limites

La version adaptée observe les sorties cardinales, zaap/zaapi, havre-sac et
interactifs de type porte ; elle ne capture pas toute séquence de dialogue
PNJ ni chaque mouvement à l'intérieur d'une carte. Les sorties alternatives
sur une carte déjà enregistrée ne sont pas ajoutées : seule la première
sortie connue est conservée. Les téléportations inconnues retombent sur
un `path` par identifiant de carte ; leur rejouabilité dépend du graphe
de navigation et des permissions du personnage.

Le mode `RC_INCLUDE_CELLID=false` est préférable : la cellule de sortie
réellement négociée avec le serveur n'est pas toujours déductible des
requêtes. Vérifier la route dans l'éditeur avant de la lancer. Un chemin
vers une carte non adjacente n'est pas une preuve de téléportation disponible.

## Vérification hors ligne

Exécuter depuis la racine du dépôt :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-RoadCreatorGivre.ps1
```

Ce test simule 4 cartes et valide bord, zaapi, porte, exclusion de récolte,
doublon de message, format d'enregistrement et syntaxe du trajet généré
avec MoonSharp. Il **ne teste pas** les paquets réels ni le client.

La compilation complète de cette copie publique GivreShareWork peut être
bloquée par la référence manquante
`../../GivreProxy/GivreCore/GivreCore.csproj`. Ne pas publier de binaire
sans d'abord rétablir cette dépendance et réussir build + tests.
