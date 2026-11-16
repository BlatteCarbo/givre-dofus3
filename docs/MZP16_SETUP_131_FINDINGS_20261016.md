# MZP16 — vérification statique du setup annoncé v1.3.1

## RESULT

Fichier fourni : %USERPROFILE%\Downloads\GivreClone-Setup-1.3.1.exe

Copie détachée à SHA identique : offline-evidence/setup-1.3.1-4D4D6458/GivreClone-Setup-1.3.1.exe

- SHA-256 : 4D4D6458A0B808173749EB98D9FC3B199748B707CEFD8C7319809769C1DF6E83
- Taille : 226304 octets ; PE natif Windows x64.
- Authenticode : NotSigned ; pas de version resource exploitable.
- CLR / IL : absent ; ILSpy ne récupérera pas l'application historique depuis ce setup.
- 7z : sections PE et ressources icônes/manifeste seulement ; pas de ZIP intégré.
- Overlay PE : 0 octet.
- Imports : WinHTTP, BCrypt/SHA-256, Crypt32, SHELL32.
- La version 1.3.1 est annoncée dans le nom du fichier ; elle n'est pas prouvée par une signature/ressource PE.

Le vrai fichier application GivreClone.exe n'est pas présent à l'intérieur de ce setup.

## SOURCE NATIVE RETROUVÉE

Les sources antérieures native/GivreUpdaterExe/main.cpp concordent fortement avec
les imports et chaînes du binaire (sans attestation byte-identique).
Ce C++ n'est PAS la décompilation de l'App historique v1.3.1.

La source décrit : GET /version?v=0.0.0, lecture d'une enveloppe JSON,
payload base64, clés download_url / sha256 / current_version, téléchargement
d'un ZIP externe, contrôle SHA-256 puis installation via tar.exe.

La source détachée contient BaseUrlEnc[] = {0} et indique que la valeur de
production est retirée de la copie. Le scan offline borné du format XOR
déclaré n'a pas identifié d'URL HTTP candidate utilisable. Cela ne prouve
pas l'absence d'URL en production ; ce n'est pas une raison de simuler un
serveur ni de contourner les règles de licence.

## SÉCURITÉ DE L'INSTALLATION ORIGINALE

Le code source expose ApplyZip(..., backup=false) : en mode install,
le dossier cible app existant peut être supprimé puis remplacé.
En mode update, WaitForPid peut même terminer un processus après délai.
Le setup propose aussi une exclusion Windows Defender.

La machine possède déjà GivreClone avec trois bots actifs, donc
NE PAS LANCER CE SETUP et ne pas l'autoriser à remplacer l'installation.
L'analyse a été entièrement statique, sans lecture de comptes,
dumps, tokens ou de GivreClone.exe installé.

## CHANGES / VALIDATION

- tools/MZP16InstallerAudit.py : PE, SHA, imports, chaîne de téléchargement.
- tools/MZP16EncodedBaseUrlProbe.py : test statique du format XOR documenté.
- tools/MZP16InstallerContractHarness.py : tests source/binaire adversariaux.
- offline-evidence/setup-1.3.1-4D4D6458 : copie binaire SHA-vérifiée.

## BLOCKERS / NEXT

Le setup seul ne livre pas le binaire historique : il télécharge un paquet
ZIP depuis le canal normal de distribution. Aucun autre paquet GivreClone
n'a été trouvé par la recherche ciblée des noms Givre* sous Downloads.

Pour analyser exactement la v1.3.1, il faut soit le ZIP distribué officiellement,
soit une copie DETACHÉE du GivreClone.exe v1.3.1 original, fournie sans accès
aux installations/sessions en cours. L'agent pourra alors effectuer un audit
PE/CLR/AOT, une analyse native/IL si possible, confronter les API au vrai
GivreClone.App, puis tester une nouvelle instance TEST autorisée.

Ne pas prétendre parité de source exacte, connexion serveur, ACK ou gameplay
sur la seule base du setup, d'une SHA ou des builds historiques offline.
