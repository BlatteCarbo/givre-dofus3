# MZP22 — contrat de démarrage Dofus réel, Launcher 2

Le 16 octobre 2026, la configuration de release Dofus 3 a été retrouvée sous
`%APPDATA%/zaap2/repositories/production/dofus/dofus3/windows/configuration/`.
Deux fichiers de configuration **statiques** ont été inspectés, jamais les profils de
compte ni les jetons. Le `zaap.yml` SHA-256 est
`d15ac9891ebf629fc9d9140648eeb357341c0fc8db14669cbce52e4628775789`,
et le `zaap-windows.yml` SHA-256 est
`ca7794bdee1a6bd44ff89c0e6a2924f4aa73d6dff3c350f57a6af32d52158d87`.

Le vrai template demande neuf paires : `--port/ZAAP_PORT`,
`--gameName/ZAAP_GAME`, `--gameRelease/ZAAP_RELEASE`,
`--instanceId/ZAAP_INSTANCE_ID`, `--hash/ZAAP_HASH`,
`--canLogin/ZAAP_CAN_AUTH`, `--langCode/language`,
`--autoConnectType/autoConnectType`, et `--connectionPort/connectionPort`.
Deux options conditionnelles `--hdReady` et `--4kReady` sont admises et
`directxVersion` est ajouté en dernier. La configuration Windows admet plusieurs
instances. Les modes `connectionPort` offerts sont 5555 ou 443 ; ce n'est pas
une preuve que le client TEST a effectivement établi un transport.

Le programme `tools/MZP22LauncherContract/verify_template.py` refuse toute
modification des octets de ces deux templates et produit uniquement la structure
des **arguments symboliques**. Tests négatifs dans `test_verify_template.py`.
Il ne lit aucune valeur d'environnement Zaap, compte, token ou PID. Cette
preuve complète les offsets statiques `GameLauncher.launch` et `compileGameSettings`
documentés dans `MZP20_LAUNCHER2_STATIC_PROCESS_AND_TICKET_20261016.md`.

Les `cwd`, exécutable effectif, variables de session, identité de compte et
ticket d'authentification restent liés à une instance créée par le vrai
Launcher. Aucun lancement de Dofus depuis le clone, authentification ou ACK
de serveur n'a été prouvé par cette étape.
