# Ankama Launcher : contrat statique de lancement et limites de preuve

Recherche du 16 octobre 2026, uniquement par lecture de sources et de configurations. Aucun lancement, réseau, injection, build ni lecture de stockage d'authentification. « Établi » ci-dessous signifie établi dans le code cité, pas observé pendant une connexion. Le build Dofus 3.7.11.11 est celui indiqué par la demande ; le rattachement binaire des configurations installées à ce build est **NON VÉRIFIÉ**.

## Sources et notation des preuves

Les identifiants suivants désignent des chemins absolus, jamais des fichiers copiés ou modifiés :

| Identifiant | Chemin absolu | Nature |
|---|---|---|
| L | `%USERPROFILE%\Documents\MVP\ankama-launcher-extracted\main.js` | Bundle officiel extrait, SHA-256 `0D395061DEDD3DA39B07AE2FE918B03D8303B30A9E954972615612A8B5F640E8` |
| LP | `%USERPROFILE%\Documents\MVP\ankama-launcher-extracted\package.json` | Version déclarée `3.15.2`, ligne 2 |
| Y | `%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\zaap.yml` | Template de lancement installé, lu seulement |
| W | `%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\zaap-windows.yml` | Template Windows hérité par Y |
| B | `%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\zaap-start.bat` | Script officiel installé, lu sans l'exécuter |
| P | `%USERPROFILE%\Documents\MVP\ZaapEmu\Program.cs` | Source de l'émulateur, pas source officielle |
| S | `%USERPROFILE%\Documents\MVP\ZaapEmu\ZaapServer.cs` | Source du serveur RPC émulé |
| O | `%USERPROFILE%\Documents\MVP\ZaapEmu\OAuthLink.cs` | Source HAAPI de l'émulateur |
| A | `%USERPROFILE%\Videos\GIVRE\GivreActive\artifacts\version-upgrade\3.7.1.0-467e232887c5-full3\ilspy\Core\Core.Configuration\ApplicationConfig.cs` | Ancien type client ; méthodes partiellement vides, pas preuve d'usage 3.7.11.11 |

Pour le bundle minifié, `L:215@249744` signifie ligne 215, offset de caractère UTF-16 à base zéro dans la ligne, calculé sur le texte .NET lu en UTF-8. Chaque position importante est accompagnée d'un court extrait reconnaissable. Les offsets ne sont pas des offsets d'octets ; ne pas les appliquer à un bundle reformaté.

SHA-256 des autres sources :

| Source | SHA-256 |
|---|---|
| Y | `D15AC9891EBF629FC9D9140648EEB357341C0FC8DB14669CBCE52E4628775789` |
| W | `CA7794BDEE1A6BD44FF89C0E6A2924F4AA73D6DFF3C350F57A6AF32D52158D87` |
| B | `EE74051BD7C692947C98C856EAB6FB4EFD5BC736C4C360DAC3734DBDFD526FE5` |
| P | `025545F73F39D9D5F1C4EED46D9B715712EBAAB05222AC5C791E215C3F571E76` |
| S | `6047FA3592189B91D66D07EF6D6B6B62B5473B7DEEB0E442AF635C6661459D07` |
| O | `84E3B83C098C16CC4F8B18B6C405A787CC0B20C69924F47431F26B121B5FC643` |

## a) Ce que le vrai lanceur transmet au jeu

### Chaîne officielle de construction

Le lanceur charge le `zaap.yml` du répertoire du jeu, le rend comme template Nunjucks, puis l'analyse en YAML. Le contexte contient `os`, les réglages `zaap`, `env` provenant de l'instance, et `game.location` / `game.settings`. Preuve **L:123@21600–25700** : `s.join(this._filepath,"zaap.yml")`, `new r.Template(this._configString,i)`, `n.render(o)`, `s=t.load(l)`, `getConfigurationContext`.

Le défaut d'exécutable est `zaap-start`, et son lanceur de scripts choisit `.bat` sur Windows. Preuves **L:1@25883** : `t.ZAAP_EXECUTABLE="zaap-start"` ; **L:123@21600** : `const m={executable:p,arguments:[],env:{}` ; **L:37**, extrait unique : `getPlatformScriptExtension` puis `"win32"===e?"bat":"sh"`.

Y:1 hérite de `zaap-` + `os.platform` + `.yml`. W:3–5 place le bloc des arguments de Y, puis ajoute `game.settings.directxVersion`. B:16 exécute exactement :

```bat
Dofus.exe -logFile "%ZAAP_LOGS_PATH%\%LOGFILE%" %*
```

B:1–2 définit `LOGFILE=dofus.%ACCOUNT_ID%.log` et `PREVLOGFILE=dofus.prev.%ACCOUNT_ID%.log`. B:4–13 fait la rotation des journaux avant l'appel. Ce sont des instructions lues, aucune de ces opérations n'a été exécutée dans cette mission.

### Ligne de commande, ordre exact du template installé

La représentation suivante est une expansion symbolique du code. Les chevrons sont des variables, pas des valeurs de compte/session observées :

```text
Dofus.exe -logFile "<ZAAP_LOGS_PATH>\dofus.<ACCOUNT_ID>.log"
  --port <ZAAP_PORT>
  --gameName <ZAAP_GAME>
  --gameRelease <ZAAP_RELEASE>
  --instanceId <ZAAP_INSTANCE_ID>
  --hash <ZAAP_HASH>
  --canLogin <ZAAP_CAN_AUTH>
  --langCode <game.settings.language>
  --autoConnectType <game.settings.autoConnectType>
  --connectionPort <game.settings.connectionPort>
  [--hdReady si game.settings.hdReady]
  [--4kReady si game.settings.4kReady]
  <game.settings.directxVersion>
```

Preuves de l'ordre : **Y:4–27**, puis **W:3–5**, préfixe **B:16**. Le dernier réglage vaut `""` par défaut ou `-force-d3d11` (**W:14–15,29–43**) ; le comportement de conservation d'un argument vide après passage par le shell Windows est **NON VÉRIFIÉ**. Le template ne contient ni `--configUrl` ni hôte explicite de connexion, mais bien un **port** de connexion (**Y:20–21**).

Valeurs par défaut documentées : langue héritée de `zaap.LANGUAGE` (**Y:48–49**) ; `autoConnectType=1` (**Y:78–79**), avec `0=choix serveur`, `1=choix personnage`, `2=entrée en jeu` (**Y:93–116**) ; `connectionPort="5555"`, choix alternatif `"443"` (**Y:117–141**) ; HD et 4K faux (**Y:143–145,159–160**). Les réglages réellement enregistrés pour cet utilisateur n'ont pas été lus.

### Variables d'environnement

La fonction officielle `getEnvironmentForRelease` construit les huit variables suivantes. Preuve **L:215@330860–332100**, extrait : `return{id:a,env:{ZAAP_PORT:this.getLocalPort(),ZAAP_GAME:u,ZAAP_RELEASE:c,ZAAP_INSTANCE_ID:a,ZAAP_HASH:l,ZAAP_CAN_AUTH:!!r,ZAAP_LOGS_PATH:p,ACCOUNT_ID:r},accountId:r}`.

| Variable | Provenance dans le code officiel |
|---|---|
| `ZAAP_PORT` | `getLocalPort()` ; réglage `buildConfig.service.port` s'il existe, sinon `26116` (**L:1@49125**) |
| `ZAAP_GAME` | `release.gameUid` |
| `ZAAP_RELEASE` | `release.name` |
| `ZAAP_INSTANCE_ID` | Compteur incrémenté pour la nouvelle instance, ou identifiant réutilisé pour un PID enregistré |
| `ZAAP_HASH` | UUID généré pour l'instance par `uuidv4`, ou clé de session réutilisée pour un PID enregistré |
| `ZAAP_CAN_AUTH` | Booléen indiquant qu'un identifiant de compte a été attribué à l'instance |
| `ZAAP_LOGS_PATH` | `await release.getLogsPath()` |
| `ACCOUNT_ID` | Compte libre choisi en multicomptes, ou compte principal disponible |

Les lignes de provenance de toutes les variables, sauf le port, sont dans **L:215@330860–332100** (`l=i()`, `c=e.name`, `u=e.gameUid`, choix de compte). Le lanceur mémorise la liaison instance/clé/release/compte avec `this.instances[n]={id:t,sessionKey:n,release:e,accountId:r}` dans la même zone.

L'environnement du parent est aussi hérité : branche script **L:37**, extrait `Object.assign({},process.env,r)` ; branche exécutable direct **L:53@170130**, `env:Object.assign({},process.env,_.env,E.env)`. Le répertoire de travail est le répertoire du jeu. Le fichier batch définit en plus `LOGFILE` et `PREVLOGFILE` avant Dofus.exe (**B:1–2**).

**Limite :** c'est le contrat statique officiel installé. La ligne de commande d'un processus réel, ses UUID, son compte et son environnement complet sont **NON VÉRIFIÉS**, volontairement non inspectés. `dofus` / `dofus3` sont codés dans l'émulateur (**P:321–322,331–332**) ; ce rapport ne transforme pas cette observation en lecture des métadonnées courantes du lanceur officiel.

### Différence avec ZaapEmu

L'émulateur lance directement le chemin de Dofus.exe défini en **P:264**, sans batch, avec `UseShellExecute=false` (**P:316–319**). Ses arguments sont ceux de **P:320–329**, dont `--configUrl http://127.0.0.1:26098/divazaap.json`, absent du template officiel ci-dessus. Il fixe `--connectionPort 5555`, `--canLogin true`, langue française et cinq `ZAAP_*` (**P:325–334**). Le port RPC est `26099` (**P:266**). Il ne faut donc pas attribuer cette commande de l'émulateur au vrai lanceur.

## b) divazaap.json et connectionHosts

### Contenu intégral produit par le code disponible

Le fichier n'est pas lu depuis le disque : `ServeConfigAsync` sérialise un objet anonyme lorsqu'il reçoit `/divazaap.json` (**P:343–374**), renvoie JSON ; les autres chemins reçoivent 404 (**P:376–380**). Les 13 propriétés sont exactement celles-ci :

| Champ | Valeur du producteur ZaapEmu | Rôle établi / limite |
|---|---|---|
| `gameAppId` | entier `1` | Identifiant jeu fourni à cette configuration ; lecture côté client actuelle NON VÉRIFIÉE |
| `connectionHosts` | tableau d'une chaîne `JMBouftou:127.0.0.1:5555` | Candidat hôte d'authentification selon l'intention du producteur ; parse et effet client NON VÉRIFIÉS |
| `buildType` | `"release"` | Type de build déclaré |
| `chatAppId` | entier `99` | Identifiant de service chat déclaré |
| `chatServerHost` | `"zaap-chat.ankama.com"` | Hôte chat déclaré |
| `chatServerPort` | entier `6337` | Port chat déclaré |
| `versionFileUrl` | chaîne vide | Emplacement de version laissé vide par l'émulateur ; fallback client NON VÉRIFIÉ |
| `haapiAnkamaUrl` | `https://haapi.ankama.com/json/Ankama/v5/` | Base HAAPI Ankama déclarée |
| `haapiDofusUrl` | `https://haapi.ankama.com/json/Dofus/v3/` | Base HAAPI Dofus déclarée |
| `shopDofusUrl` | `https://shop-api.ankama.com/` | Base boutique déclarée ; nom de propriété accepté actuellement NON VÉRIFIÉ |
| `gamesActivityDescriptorUrl` | `https://launcher.cdn.ankama.com/configs/useractivities.json` | Descripteur d'activités déclaré |
| `avatarUrlFormat` | `https://avatar.ankama.com/users/{0}.png` | Format d'avatar déclaré ; substitution effective NON VÉRIFIÉE |
| `dofusWebsiteUrl` | `https://www.dofus.com` | Site Dofus déclaré |

Preuve de toutes les valeurs : **P:354–369** ; `AuthProxyHostPort="127.0.0.1:5555"` en **P:268**, assemblé avec `"JMBouftou:"` en **P:357**. Aucune des URL citées n'a été contactée.

**Format exactement démontré par le producteur :** `connectionHosts` est un `string[]`, chaque entrée exemple est la chaîne à trois composants `JMBouftou:127.0.0.1:5555`. On peut la décrire comme `nom:hôte:port` dans **cet exemple**, mais une grammaire générale acceptée par Dofus, la signification de `JMBouftou`, la gestion IPv6, les séparateurs alternatifs et le choix du port entre cette chaîne et `--connectionPort` sont **NON VÉRIFIÉS**. Aucun parseur client actuel n'a été retrouvé dans les corpus attribués à cette recherche.

**Provenance réelle des valeurs démontrées :** constantes locales de ZaapEmu, sans récupération dynamique dans cette fonction. La valeur de production de `connectionHosts` et sa provenance serveur, fichier embarqué ou CDN sont **NON VÉRIFIÉES**. Le bundle officiel extrait a été recherché dans ses fichiers JavaScript pour `connectionHosts`, `divazaap`, `--configUrl`, sans occurrence ; cette absence dans ce corpus ne prouve pas que le client les ignore.

### Pourquoi l'objet émulé ne certifie pas le schéma officiel

Le type ancien `ApplicationConfig` décompilé sous une arborescence **3.7.1.0**, **A:7–33,203**, contient `connectionHosts` en `string[]`, mais des noms divergents : `shopiDofusUrl`, `webShopDofusUrl`, `cinematicsUrl` ; il n'y a pas `avatarUrlFormat` dans ce type. Son constructeur est vide (**A:203–205**) et `ToString()` renvoie `null` (**A:207–209**). Il prouve les noms/type présents dans cet artefact ancien, pas la désérialisation ni les propriétés réellement consommées par le client 3.7.11.11. En particulier ne pas corriger arbitrairement `shopDofusUrl` ou généraliser les 13 propriétés de l'émulateur en « tous les champs du client actuel ».

## c) Service Zaap Thrift : contrat complet vs appels réellement faits

### Contrat exposé par le bundle officiel extrait : 20 méthodes

Le tableau est extrait des `writeStructBegin("ZaapService_<méthode>_args")` et `_result` de **L:215**. Les numéros entre crochets sont les identifiants **Thrift**, pas des numéros protobuf. Les IDs négatifs des méthodes de paiement sont conservés exactement. `s` signifie `string gameSession [1]`. Chaque ligne fournit les offsets args/résultat, donc sa preuve locale propre.

| Méthode | Arguments en ordre du contrat | Retour | Preuve L:215, offsets args / résultat |
|---|---|---|---|
| `connect` | `string gameName[1], string releaseName[2], i32 instanceId[3], string hash[4]` | `string` | `249744 / 250984` |
| `auth_getGameToken` | `s, i32 gameId[2]` | `string` | `251888 / 252856` |
| `updater_isUpdateAvailable` | `s` | `bool` | `253654 / 254499` |
| `settings_get` | `s, string key[2]` | `string` | `255405 / 256362` |
| `settings_set` | `s, string key[2], string value[3]` | `void` | `257396 / 258354` |
| `askForGuestComplete` | `s` | `string` | `259010 / 259853` |
| `userInfo_get` | `s` | `string` | `260653 / 261489` |
| `release_restartOnExit` | `s` | `void` | `262282 / 262999` |
| `release_exitAndRepair` | `s, bool restartAfterRepair[2]` | `void` | `263854 / 264748` |
| `zaapVersion_get` | `s` | `string` | `265413 / 266252` |
| `zaapMustUpdate_get` | `s` | `bool` | `267048 / 267886` |
| `payArticle` | `s, string shopApiKey[-1], i32 articleId[-2], OverlayPosition pos[-3]` | `bool` | `269156 / 270392` |
| `payArticleWithPid` | `s, string shopApiKey[-1], i32 articleId[-2], i32 pid[-3], OverlayPosition pos[-4]` | `bool` | `271773 / 273132` |
| `payCart` | `s, string accessToken[-1], string shopKey[-2], string cartId[-3], OverlayPosition pos[-4]` | `bool` | `274544 / 275913` |
| `payCartWithPid` | `s, string accessToken[-1], string shopKey[-2], string cartId[-3], i32 pid[-4], OverlayPosition pos[-5]` | `bool` | `277434 / 278926` |
| `hasPremiumAccess` | `s` | `bool` | `279717 / 280377` |
| `hasGameAccess` | `s, i32 gameId[2]` | `bool` | `281163 / 281947` |
| `auth_getGameTokenWithWindowId` | `s, i32 gameId[2], i32 windowId[3]` | `string` | `282879 / 283994` |
| `system_addNotification` | `s, NotificationOptions notificationOptions[2]` | `string` | `285077 / 286102` |
| `addNotification` | `s, NotificationParams notificationParams[2]` | `NotificationResult` | `287169 / 288242` |

Exemple reconnaissable **L:215@249744** : `writeStructBegin("ZaapService_connect_args")` suivi de `writeFieldBegin("gameName",i.Type.STRING,1)` ; **L:215@269156** : `ZaapService_payArticle_args`, `writeFieldBegin("shopApiKey",i.Type.STRING,-1)` ; **L:215@288242** : `ZaapService_addNotification_result`, succès `i.Type.STRUCT,0`.

Tous les retours non `void` utilisent `success[0]`. Un `error[1]` de type `ZaapError` figure dans les résultats, **sauf** `hasPremiumAccess` / `hasGameAccess` dont les structures ne contiennent que le succès booléen. `ZaapError` contient `i32 code[1]` requis et `string details[2]` optionnel : **L:96@48204–49300**, extrait `Required field code is unset!`, `writeStructBegin("ZaapError")`.

Types structurés nécessaires au contrat :

| Type | Champs | Preuve |
|---|---|---|
| `OverlayPosition` | `i32 posX[1], posY[2], width[3], height[4]` | **L:96@50057**, `writeStructBegin("OverlayPosition")` |
| `NotificationOptions` | `string title[1], subtitle[2], body[3]`; `bool silent[4]`; `string icon[5]`; `bool hasReply[6]`; `string timeoutType[7], replyPlaceholder[8], sound[9], urgency[10]`; `list<string> actions[11]`; `string closeButtonText[12], toastXml[13]` | **L:96@53045**, `writeStructBegin("NotificationOptions")` et `writeListBegin(i.Type.STRING,...)` |
| `NotificationParams` | `string id[1], body[2]`; `list<string> image[3]`; `string subTitle[4], background[5]`; `i32 timeout[6]` | **L:96@56348**, `writeStructBegin("NotificationParams")` |
| `NotificationResult` | `string id[1]`; `i32 type[2]`; `string reply[3], actionIndex[4]` | **L:96@58185**, `writeStructBegin("NotificationResult")` ; `actionIndex` est bien STRING ici |

### Ce qui se passe si le résultat est vide ou échoue

Il faut séparer le codec du bundle officiel, l'implémentation serveur et les décisions du client de jeu :

1. **Codec Thrift officiel présent dans le bundle :** pour `connect`, une exception Thrift est remise au callback ; sinon `error` est prioritaire ; sinon un `success` non `null` est livré ; sinon erreur `connect failed: unknown result`. Une chaîne `""` est donc un succès du codec, pas un résultat manquant. Même règle visible pour le jeton, les réglages, l'update et la complétion invité. Preuves **L:215@288500–290650** (`recv_connect`, `recv_auth_getGameToken`) et **L:215@290650–294750** (`recv_updater_isUpdateAvailable`, `recv_settings_get`, `recv_askForGuestComplete`). Les variantes Promise rejettent si le callback reçoit une erreur. Cette classe générée incluse dans Electron ne prouve pas les décisions du code Unity.
2. **`void` :** un résultat sans `success` est normal ; `settings_set` appelle `r(null)` quand aucun `error` n'est présent, **L:215@293300**, extrait `recv_settings_set` puis `if(...null!==o.error)return r(o.error);r(null)`.
3. **Serveur officiel :** `connect` renvoie la clé de session enregistrée et échoue avec `INVALID_CREDENTIALS` si les credentials ne sont pas trouvés (**L:184@159300**, `getCredentials(...)`, `i(null,o.sessionKey)`). `auth_getGameToken` obtient le compte lié à la session et appelle `Auth.createToken`, propage l'erreur et arrête la release autorisée sur HTTP 401/403 (**L:184@157900–158470**). La variante `WithWindowId` vérifie la validité numérique du windowId, l'autorisation multicomptes et la disponibilité du compte, puis crée le jeton ; erreurs `AUTH_WRONG_WINDOW_ID`, `AUTH_MULTI_NOT_ACTIVATED`, `AUTH_NO_MORE_ACCOUNT_AVAILABLE` (**L:184@158480–159160**).
4. **ZaapEmu :** le dispatcher n'implémente que 11 noms, **S:97–110**. Ce sous-ensemble n'est pas l'IDL complet du bundle. `connect` ignore les arguments 1–3 et ne vérifie que le hash[4] (**S:99,116–127**) ; hash inconnu => `""`. Les deux méthodes de jeton ignorent gameId et windowId, retournent le jeton enregistré pour la session ou `""` (**S:100–101,130–140**). `settings_get` ne reconnaît que `autoConnectType`, les autres clés => `""` (**S:143–147**). `userInfo_get` retourne du JSON synthétique ou `""` (**S:150–159**). Update et MustUpdate => `false`, version => `1.0.0-emu`, méthodes void => aucun champ de succès (**S:102–109,189–217**). Un nom inconnu retourne aussi une réponse vide, **S:110**, ce qui ne reproduit pas une véritable erreur `UNKNOWN_METHOD`.

**NON VÉRIFIÉ pour chacune des 20 méthodes :** le fait que Dofus 3.7.11.11 l'appelle effectivement ; la liste exhaustive de ses sites d'appel ; ce qu'il affiche, réessaie, ignore ou abandonne après chaîne vide, exception, délai ou socket fermée. Aucun corps client actuel ni trace d'exécution admissible n'a été trouvé. La présence de `send_*` / `recv_*` générés côté lanceur, ou d'un dispatcher émulé, ne constitue pas une preuve de consommation Unity. Les commentaires **S:19–26** racontent une panne passée ; ils ne sont pas une preuve d'exécution fraîche.

## d) connectionHosts/configUrl et nécessité du fichier hosts

**Verdict : l'affirmation générale « ne pilote rien et hosts obligatoire » n'est pas établie par les sources lues. L'affirmation inverse de ZaapEmu n'est pas établie non plus.**

- Il est établi que le lanceur officiel donne `--connectionPort` au client, et aucun hôte explicite dans le template installé (**Y:4–27**). Cela sépare le port RPC Zaap `--port` du port de connexion jeu `--connectionPort` ; les confondre serait une erreur observable dans le contrat.
- Il est établi que l'émulateur passe `--configUrl` et sert un `connectionHosts` local (**P:329,343–374**) ; son commentaire affirme que cela suffit à éviter `hosts` (**P:257–263**). Le code du **producteur** ne prouve pas que le client télécharge la configuration, reconnaît le nom de propriété, parse la chaîne ni choisit son hôte depuis celle-ci.
- L'ancien `ApplicationConfig.connectionHosts` (**A:47–57**) est une propriété, sans appelant ni parseur trouvé dans le corpus historique inspecté ; elle n'est pas une preuve de routage 3.7.11.11.

**Ce qui pilote réellement l'hôte d'authentification dans le client 3.7.11.11 : NON VÉRIFIÉ.** Il manque le corps client de chargement de configuration, le parseur des hosts et le site de création de connexion réseau. Il manque aussi le chemin distinct de sélection de l'hôte de serveur de jeu après authentification ; le routage initial et les adresses de jeu peuvent être deux décisions séparées (hypothèse à vérifier, pas un résultat de cette mission). Aucune preuve n'autorise ici à déclarer `hosts` obligatoire, facultatif ou suffisant.

Prochaine vérification utile, hors exécution de cette mission : retrouver des corps réellement décompilés et liés au hash du client 3.7.11.11 pour les lecteurs de `configUrl`, `connectionHosts` et la construction de socket. Une nouvelle constante dans l'émulateur n'apporterait pas cette preuve.

## e) CreateToken, renouvellement et certificat Shield

### Parcours officiel démontré statiquement

1. Le jeu demande un jeton au RPC `auth_getGameToken(gameSession,gameId)`, d'après le contrat ; le serveur retrouve l'accountId attaché à la session, puis exécute `Auth.createToken(gameId,accountId)`. Preuve **L:184@157900**, extrait `getAccountId(e)` puis `n(null,await o.createToken(t,r))`. L'appel réel par le client courant reste NON VÉRIFIÉ.
2. `Auth.createToken` cherche la clé API du compte ; compte non enregistré => rejet. Il appelle d'abord `refreshApiKey(h)`, puis charge un certificat déjà stocké pour le login. Preuve **L:1@64477–64870**, extrait `Trying to connect an account not registered in apiKeys`, `await this.refreshApiKey(h)`, `getStoredCertificate(h.login)`.
3. S'il y a un certificat exploitable, il prend son `id` et dérive un hash via `generateHashFromCertif`. Si cette dérivation échoue, le code tente de supprimer le certificat inutilisable, puis peut continuer sans paramètres de certificat. Preuve **L:1@64870–65350**, extrait `t&&i&&s?await ...createToken...,s,i):await ...createToken...,e,h)`.
4. Le module HAAPI exécute un **GET** vers `json/Ankama/v5/Account/CreateToken`, paramètres `game`, `certificate_id`, `certificate_hash`, en-tête `APIKEY`, retourne `body.token`. Preuves route **L:1@75200–75400**, implémentation **L:444@50800–51150**, extrait `getUrl("ANKAMA_ACCOUNT_CREATE_TOKEN",{game:t,certificate_id:i,certificate_hash:s})` et `(await o.get(a,{APIKEY:n.key})).body.token`. Les noms des paramètres sont documentés, aucune valeur secrète n'est lue ni reproduite.
5. Il n'y a pas de « refresh du même game token » dans cette fonction : chaque appel RPC redemande `CreateToken`. Le renouvellement distinct de clé API est évité si `refreshDate + 1728e5 > maintenant`, soit deux jours ; sinon `ankama.api.refreshApiKey` est appelé. Preuve **L:1@63550–64250**, `refreshApiKey(e)` et la condition temporelle. Le module de refresh poste `refresh_token` et `long_life_token`, avec la clé API courante en en-tête, et renvoie la nouvelle clé, l'accountId et le refresh token (**L:444@67200–67700**, `ANKAMA_API_REFRESH_API_KEY`). La fonction appelante visible met à jour refreshToken/refreshDate et stocke la clé API ; ne pas supposer, sans suivre davantage le wrapper, qu'elle remplace explicitement `key` à cet endroit.
6. Si `CreateToken` échoue : HTTP 401 provoque logout, 403 fait marquer le compte en erreur et envoie `AUTH_EXPIRED_SESSION`, puis l'erreur est relancée (**L:1@65300–65630**). Le handler RPC propage l'erreur et arrête la release sur 401/403 (**L:184@158200–158470**). L'écran affiché par Unity après cette erreur reste NON VÉRIFIÉ.

### Quand Shield est demandé

Le lanceur ne contient pas dans `createToken` une règle locale « tout compte doit fournir un certificat ». Il passe id+hash seulement quand un certificat chargé et son hash sont disponibles. Cela prouve que le chemin **sans certificat existe** ; cela ne garantit pas que le serveur l'accepte pour un compte particulier. Preuve **L:1@64800–65350**.

Pour la connexion au compte, `generateApiForAccount` essaie aussi le certificat local puis peut créer une clé API sans lui (**L:184@144324–145528**). Après login, si l'état retourné contient `UNSECURED`, le lanceur déclenche `askForDoubleAuth` ; sinon il ajoute le compte (**L:1@56150–56580**, extrait `t.security&&t.security.includes("UNSECURED")?await this.askForDoubleAuth(t):await this.addAccount(t)`).

`askForDoubleAuth` appelle `ankama.shield.securityCode` et émet `SHIELD_BLOCK`. Si le serveur renvoie `SECUREDBYOTP`, il émet `OTP_BLOCK` ; si le motif est `NONEEDTOBESECURED`, il retourne faux. Preuve **L:1@55429–56020**. Les gestionnaires `VALIDATE_CODE` / `VALIDATE_OTP` demandent ensuite un certificat avec le mode approprié (**L:1@53400–54300**). `askCertificateForApiKey` appelle `ankama.shield.validateCode` ou `ankama.shield.validateOtp`, rattache le login et stocke le certificat (**L:184@146413**).

Le hash n'est donc pas simplement un champ arbitraire à fournir : la fonction source calcule une empreinte à partir du certificat encodé et de caractéristiques machine (**L:184@145528,146740**, `createHmEncoders()` ; **L:53@27056**, `generateHashFromCertif`). Aucune valeur d'empreinte, certificat, identifiant de certificat ou résultat cryptographique n'a été collectée.

| Situation | Ce que prouve le code | Acceptation côté service |
|---|---|---|
| Pas de certificat local exploitable | `CreateToken` peut être appelé sans id+hash | **NON VÉRIFIÉ** pour un compte donné ; l'absence de fichier ne prouve pas l'absence de Shield |
| Certificat local exploitable | id et hash dérivé sont transmis | **NON VÉRIFIÉ** en jeu ; ni validité ni expiration du certificat courant inspectée |
| État compte `UNSECURED` | Flux de sécurisation Shield/OTP lancé | Décision dépend de la réponse HAAPI, pas d'un booléen deviné localement |
| Réponse `NONEEDTOBESECURED` | Le flux de sécurisation retourne faux | Ne remplace pas une preuve de `CreateToken` réussi |
| Erreur 401/403 | Traitements de logout/erreur/session ci-dessus | Retour exact pour « compte avec/sans Shield » **NON VÉRIFIÉ**, aucun appel réseau effectué |

Preuves : branches conditionnelles et messages de **L:1@55429–56580,64800–65630**. La durée de vie précise d'un game token, son caractère à usage unique, et les règles serveur de révocation/certificat selon le compte restent **NON VÉRIFIÉS**. Le commentaire de l'émulateur qualifie le token de court et à usage unique (**P:287–291**) ; il ne suffit pas à prouver ces propriétés serveur.

### Émulateur et renouvellement : ne pas confondre les modèles

`OAuthLink.CreateGameTokenAsync` fait le GET `Account/CreateToken?game=1`, ajoute les paramètres de certificat uniquement si les deux arguments sont non nuls, puis lit `token` (**O:148–161**). `LaunchGameAsync` demande un jeton frais avant d'enregistrer une instance (**P:292–306**). Le serveur émulé stocke ensuite ce jeton dans `_clients` (**S:30,43–45**) et renvoie la **même chaîne** à chaque `auth_getGameToken` de cette session (**S:130–137**), contrairement au handler officiel qui appelle `createToken` à la demande. La fraîcheur au premier lancement n'est donc pas une implémentation de renouvellement RPC permanent.

## État final des réponses demandées

| Question | Établi | NON VÉRIFIÉ restant |
|---|---|---|
| a) Arguments / environnement | Construction complète et ordre du template officiel installé, batch et env ; différence avec l'émulateur | Valeurs dynamiques d'une instance réelle et rattachement de ces fichiers au hash client 3.7.11.11 |
| b) Config / hosts | 13 champs exacts produits par ZaapEmu ; tableau de chaînes, exemple `nom:hôte:port` | Schéma officiel actuel complet, parseur client, hosts de production et provenance |
| c) Zaap | 20 méthodes exposées par le bundle officiel, types et IDs ; 11 noms émulés ; comportement codec/serveur | Liste des appels Unity 3.7.11.11 réellement invoqués, politiques client sur vide/échec |
| d) Redirection | Distinction port RPC/port jeu et contradiction entre notes et commentaire émulateur | Consommateur d'hôte réel ; aucune conclusion probante sur nécessité de `hosts` |
| e) Jeton / Shield | Contrôle officiel `CreateToken`, refresh API distinct, branches certificat et Shield/OTP | Résultats serveur par type de compte, TTL/usage unique et succès d'authentification |

Aucun état « fonctionne » ou « testé en jeu » n'est revendiqué. Les trous de preuve ci-dessus restent explicites pour empêcher de transformer un producteur de configuration ou un contrat RPC généré en preuve de comportement du client courant.
