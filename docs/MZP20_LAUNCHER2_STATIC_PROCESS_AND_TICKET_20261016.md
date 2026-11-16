# MZP20 — Launcher 2 : création exacte du processus et contrat Zaap, preuve statique

**16 octobre 2026 · worker-1 · lecture seule de l'archive Launcher 2.**

## R — Résultat

**Le chemin d'exécution réel du code Launcher 2 est identifié** : son entrée
Electron appelle le vrai `node:child_process.spawn` pour lancer chaque release
de jeu, avec un `cwd`, des arguments, un environnement et des options de
processus explicites. La gestion des instances et des tickets Zaap est aussi
localisée. Ce résultat **ne fournit pas une commande `Dofus.exe` directement
reproductible** : les valeurs concrètes `executable`, `arguments` et `env`
proviennent d'une configuration de release rendue dynamiquement. Aucun ticket
valide n'est embarqué dans les sources.

Le périmètre de version installé est **Dofus 3.7.11.11**, documenté dans
`docs/MZP11_PRIME_HANDOFF_20261016.md:3-5` par la ligne Cytrus
`releaseVersion=6.0_3.7.11.11`. Cela qualifie le binaire local, **pas** le
fonctionnement runtime du lancement ni le gameplay.

## Source exacte et empreintes

Seuls les membres suivants de
`%LOCALAPPDATA%/Programs/Ankama Launcher 2/resources/app.asar`
ont été lus, par offset et longueur issus de l'index ASAR :

| Membre | Taille | SHA-256 |
| --- | ---: | --- |
| `package.json` | 1 389 o | `076a07e0afc3dfc219bd108aa4494908845cf6e4cea1e7f02ed8eb9e0e8515ba` |
| `electron-main.js` | 717 454 o | `8e099b65b380102cb8e56177cc8e586884b8260d1b9c6f107039bdadd25afc6f` |
| `node_modules/protocol-generated-nodejs/ZaapService.js` | 150 459 o | `22225dfef610e7f820388037f38c34c6b07d104dff38583b1135cb297e7ec0a4` |

`package.json` indique `name=ankama-launcher-v2`,
`version=0.14.3` et `main=./electron-main.js`. Les positions suivantes
sont des **offsets de caractères dans le JS décodé**, pas des RVA natifs.
Le fichier est minifié : les noms courts `pI, i, d, n, m, h` sont ceux de
cette build, sous cette empreinte exclusivement.

## Preuves ciblées : création du jeu

Dans `electron-main.js` :

- Vers **580 668** : `import{spawn as pI}from"node:child_process"`.
- Vers **667 500–669 900**, classe nommée
  `GameLauncher`, méthode `async launch(r)` : vérifie la release et les
  instances déjà enregistrées, obtient
  `n=await this.getEnvironmentForRelease(i,void 0,t)`, puis
  `d=await y.resolve(ie).compileGameSettings(i,void 0,n)`.
- L'exécutable est **`d.executable`**. Si cette valeur vaut
  `zaap-start`, sous Windows il utilise `zaap-start.bat` et
  `shell=true`; sinon `shell=false` et utilise la valeur du fichier
  de configuration. Le fichier `installLocation/executable` doit
  exister. Les arguments viennent de **`d.arguments`**, avec guillemets
  ajoutés conditionnellement pour le mode script/shell.
- L'appel réel est :

```javascript
pI(m, I, {
  cwd: i.installLocation,
  env: Object.assign({}, process.env, n, d.env ?? {}),
  shell: h,
  detached: true,
  stdio: "ignore",
  windowsHide: h && !qa()
})
```

Ici `m` est l'exécutable résolu et `I` le tableau d'arguments résolu.
La provenance de `cwd` est **la valeur de `installLocation` de la release
choisie**, et non une constante supposée `%LOCALAPPDATA%`.
`env` est une superposition dans cet ordre : environnement du Launcher
parent, variables Zaap `n`, environnement de la configuration de jeu `d.env`.
`detached` et `stdio` sont bien les options **du véritable spawn jeu**,
pas les autres spawn observés pour DevTools, deeplinks ou raccourcis.

L'objet retourné `C` est mémorisé dans
`GameLauncher._gameInstances` avec `E.pid=C.pid` si disponible et
`E.gameProcess=C`. Le `close` de cet enfant appelle la révocation de
l'instance. Il s'agit de **la parenté logique détenue par le processus
Launcher 2** ; la parenté effective au niveau OS et un PID de client TEST
ne sont pas observés dans cet audit.

## Origine de la configuration qui empêche de figer argv/env

Vers **610 900–611 100**, le code constitue les fichiers de release sous
`app.getPath("userData")/repositories/[univers]/gameUid/releaseName/platformName`.
Vers **625 700–625 850**, `getGameConfigurationDirPath` ajoute
`configuration` au dossier de release. Vers **628 950–630 500**,
`compileGameSettings` charge son `configuration/zaap.yml` avec un
moteur de template (contexte `os`, `zaap`, `env` et `game.settings`),
réévalue les options (au plus cinq passes), puis produit
`executable`, `arguments`, `env`, `maxInstances`, etc.

Sans ce `zaap.yml` exact, ses options de jeu et les variables au moment du
lancement, il est **impossible de déduire les argv/env concrets** d'un
Dofus 3.7.11.11 démarré par Launcher 2. La valeur de repli du code
en absence de configuration est `executable="zaap-start"`,
`arguments=[]`, `env={}`, soit la recherche du script
`zaap-start.bat` sous Windows ; rien ne prouve ici que cette valeur de
repli corresponde à l'installation réellement préparée. Aucun fichier
de profil, dépôt Launcher utilisateur ou `zaap.yml` runtime n'a été ouvert.

## Environnement Zaap / appartenance du ticket

Vers **673 900–674 350**, la méthode
`GameLauncher.getEnvironmentForRelease` compose :

| Nom | Expression / sémantique |
| --- | --- |
| `ZAAP_PORT` | `ZaapServer.getLocalPort()`, port lié dynamiquement |
| `ZAAP_GAME` | `gameRelease.gameUid` |
| `ZAAP_RELEASE` | `gameRelease.releaseName` |
| `ZAAP_INSTANCE_ID` | identifiant numérique créé et conservé par `GameLauncher` |
| `ZAAP_HASH` | `String(instance.id)`, **pas une preuve cryptographique** |
| `ZAAP_CAN_AUTH` | `accountId != null` |
| `ZAAP_LOGS_PATH` | répertoire de journaux de release |
| `ACCOUNT_ID` | identifiant de compte de **l'instance Launcher**, non valeur libre |

Vers **663 800–665 800**, `ZaapServer` choisit un port de boucle locale.
Dans cette build, la base production déclarée est **26116**, avec cinq
candidats consécutifs ; les bases staging et pre-production diffèrent.
La valeur finale est dynamique et **n'a pas été recherchée via socket**.
Ces ports ne constituent pas une permission pour notre clone de joindre le
Launcher existant. Les ports Givre originaux **5657/5658/26666** restent
strictement hors périmètre.

Vers **649 000–650 600**, les RPC utilisés sont :

1. `connect(gameName, releaseName, instanceId, hash)` : construit la
   release à partir du jeu et de la plateforme, cherche une instance interne
   correspondant aux paramètres et renvoie **l'ID de l'instance**.
2. `auth_getGameToken(gameSession, gameId)` : résout
   `getGameInstance({gameInstanceKey})?.accountId`, refuse toute instance
   absente, puis délègue à `createToken(gameId, accountId)`.
   `WithWindowId` vérifie aussi le numéro de fenêtre et l'accès multi-compte.
3. Vers **544 800–545 800**, `createToken` exige une identité de compte
   et une clé d'API obtenue par le système officiel du Launcher, ainsi que
   les éléments de certificat seulement lorsque l'état de ce compte
   l'exige, avant de demander un ticket via
   `json/Ankama/v5/Account/CreateToken`. **Aucun appel réseau n'a été fait**.

`ZAAP_HASH`, `ZAAP_INSTANCE_ID` et le code `connect` sont des
**identifiants fonctionnels**. Ils ne donnent ni un ticket réutilisable,
ni une autorité sur un compte déjà connecté. Les tickets naissent d'une
résolution **à la demande**, liée au compte de l'instance tenue en mémoire
par Launcher 2. Le WebAuth TEST isolé MZP18 est une étape distincte,
qui n'établit pas à lui seul un `GameLauncher._gameInstances` ou un
serveur Zaap TEST indépendant.

## C — Modifications

Uniquement deux fichiers **nouveaux** sous le périmètre autorisé :

- `tools/MZP20LauncherContract/inspect_launcher2_source.py` : lecture ASAR
  bornée, liste blanche de trois sources précises, fenêtres
  de source/offsets. Aucune copie de l'archive, aucun script exécuté.
- `tools/MZP20LauncherContract/verify_contract.py` : contrôles de SHA
  et de fragments de code explicites de cette build.

Ce document est également nouveau. Aucun code de production, configuration,
installation, compte, clé, session, processus ni port original Givre n'a été
consulté ou modifié.

## V — Validation

Commande purement statique :

```powershell
py -3 -B tools/MZP20LauncherContract/verify_contract.py
```

**23 vérifications PASS** contre les membres ASAR réels, dont trois SHA,
l'entrée/version, le véritable import et appel `spawn`, la composition
argv/env/cwd, l'association ChildProcess/instance, les huit variables
Zaap, le dispatch `connect/auth_getGameToken` et le contrat Thrift généré.
La sortie dit explicitement
`STATIC_SPAWN_AND_ZAAP_CONTRACT_PINNED`,
`RUNTIME_CONFIGURATION_REQUIRED`,
`RUNTIME_ACCOUNT_BOUND_RPC_NO_STATIC_VALUE` et
`GAME_RUNTIME_OR_AUTHORITY=NOT_TESTED`.
Zéro test de lancement, réseau, compte ou client.

## B — Blocages exacts pour un lancement indépendant TEST

1. **argv/env concrets** : il manque la valeur rendue, versionnée et
   attestée du `configuration/zaap.yml` de cette release et des options
   de jeu. Le fichier de configuration utilisateur Launcher en service
   reste non lu conformément au périmètre ; pas de reconstitution fiable
   de `m` et `I` uniquement depuis le code minifié.
2. **Détention du ticket** : la création repose sur un compte officiellement
   authentifié et sur la table d'instances privée de Launcher 2.
   MZP18 ne prouve ni création de cette instance sous notre contrôle,
   ni retour authentifié du RPC, ni ticket attribué à un enfant de notre
   clone.
3. **Transport et origine** : aucune implémentation indépendante TEST de
   `ZaapServer` et `IIndependentTestGameLaunchAuthority` n'est certifiée
   en production. Les paramètres d'environnement déclarés ne suffisent
   pas à prouver qui écoute ni à distinguer une instance existante.
4. **Client / gameplay** : ni PID/enfant Dofus 3.7.11.11 appartenant au
   clone, ni framing authentifié, ni réponse serveur/ACK observés.

**Décision étayée :** la **recette algorithmique de création de processus**
du Launcher 2 est maintenant source-pinnée ; la **commande exacte et le
ticket utilisables dans une session TEST indépendante** demeurent inconnus.
Ne pas les inventer, ne pas importer la session d'un Launcher ou de Givre
historique. L'accord utilisateur pour un compte poubelle dans le clone
ne change pas cette absence de preuve statique, et ce lot reste sans réseau.
