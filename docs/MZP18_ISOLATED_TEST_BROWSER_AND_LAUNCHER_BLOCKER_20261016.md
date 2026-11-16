# MZP18 — connexion navigateur TEST isolée dans GivreClone.App

## RESULT

MZP18 poursuit directement le produit historique `GivreClone.App`, et non un faux
client de jeu. Le premier composant interactif de la verticale TEST — le
navigateur Ankama WebAuth — possède désormais **son propre exécutable reconstruit,
son profil WebView2 séparé et un bouton dans l'écran réel Comptes**.

Le test d'authentification officiel n'a **pas encore été exécuté en ligne** :
l'écran fournit un parcours que l'opérateur peut choisir, pas une attestation de
connexion déjà obtenue. Aucun client Dofus, serveur, personnage, carte, sort,
inventaire ou ACK réel n'est certifié.

## PROVENANCE & CHANGES

- Source : huit fichiers C# de `GivreLauncher.WebAuth.dll` récupérée du ZIP
  isolé MZP17, avec compilation antérieure `0 erreur`. Les sources de référence
  authentiques restent **inchangées**.
- `tools/MZP18IsolatedWebAuth/` : compilation indépendante Windows x64, avec
  deux fichiers d'adaptation locaux (`Program.Isolated.cs`,
  `BrowserSetup.Isolated.cs`). Ce n'est pas le bootstrap natif original.
- `Program.Isolated.cs` : refuse l'exécution **avant toute fenêtre/réseau** si
  l'environnement n'a pas fourni une racine de profil TEST autorisée ; retire
  entièrement le chemin de requêtes diagnostics CanaryFailover hérité.
- `BrowserSetup.Isolated.cs` : supprime l'ancien répertoire commun
  `%LOCALAPPDATA%/GivreClone/browser` et toute migration de cache historique ;
  uniquement `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/webauth/<GUID>`.
  Refuse les chemins externes, les jonctions et les anciens profils.
- `GivreClone.App/Services/MZP18IsolatedWebAuthRunner.cs` : huit empreintes SHA
  exactes des fichiers du nouveau compagnon ; ownership du processus enfant
  démarré par l'App ; profil TEST aléatoire et propriétaire privé ; aucune lecture
  de keydata, compte Givre original, jeton d'origine ou PID arbitraire.
  Le navigateur demande la saisie manuelle sur `auth.ankama.com`, retourne un
  état d'authentification **sans persister les jetons reçus**. Annuler/fermer la
  page révoque le helper enfant par son seul handle.
- `GivreClone.App/Views/MZP4TestAccountsPanel.cs` : nouveau bouton
  « Authentification Ankama TEST isolée (navigateur) », actif seulement quand un
  profil local de test est sélectionné. Un départ de la page annule le helper.
  L'écran distingue explicitement OAuth et gameplay.
- `GivreClone.App/GivreClone.App.csproj` : copie code-only du helper compilé dans
  `WebAuthTest/`, séparé du véritable `WebAuth/` stage MZP17.
- `WebAuthRunner.cs` : réutilise le calcul historique de handshake pour le
  helper reconstruit, sans publier la valeur ni altérer l'original.

**Séparation sensible :** le WebView2 reconstruit peut sauvegarder des cookies
dans son **répertoire privé TEST**, même si les jetons retournés ne sont jamais
écrits dans le stockage applicatif ni inclus dans les ZIP. Ce cache privé ne doit
pas être traité comme dépourvu de secrets. Aucune suppression automatique
récursive de profils inconnus n'a lieu.

## VALIDATION (LOCAL WINDOWS 2026-10-16)

| Vérification | Résultat | Ce qui est prouvé |
|---|---|---|
| Rebuild MZP18IsolatedWebAuth Release x64 | 0 erreur, 37 avertissements de décompilation | Recompilation du composant |
| MZP18TestBrowserHarness | 8/8 PASS | Code de contrôle d'intégrité production + refus de deux lancements du helper sans autorité de profil |
| MZP4AccountsUiHarness | 27 PASS | Nouveau bouton réellement monté sur Comptes, refus du navigateur pour un profil de harness étranger, CRUD préexistant intact |
| MZP4LauncherHarness | 35/35 PASS | Isolation des dossiers TEST, permissions et interdiction des ports historiques |
| MZP17WebAuthHarness | 10 PASS | Ancien compagnon authentique toujours intègre et inchangé |
| MZP17WebAuthContract | 18 PASS | Contrat source récupéré ↔ runner d'origine intact |
| Mutations de DLL de même longueur / EXE +1 octet | REFUS | Contrôle de SHA et longueur effectif |
| WebView2Loader.dll absente | REFUS | Dépendance native obligatoire |
| Racine navigateur absente ou étrangère | Code de sortie 3 | Aucune fenêtre de login/WebView2 lancée |
| `GivreClone.App` Release win-x64 | 0 erreur, 153 avertissements historiques | Produit historique compile avec bouton TEST |
| `GivreClone.App` Debug win-x64 | 0 erreur, 154 avertissements historiques | Pas une attestation de gameplay |
| Login Ankama TEST interactif | NON EXÉCUTÉ | Aucun vrai OAuth confirmé |
| Client Launcher 2 attribué à notre clone | NON | Aucune preuve de spawn officiel |
| Réponse serveur, UID, carte, combat, HDV, Lua H24 | NON | Parité restant à construire |

Commande du composant :

```powershell
dotnet build tools/MZP18IsolatedWebAuth/MZP18IsolatedWebAuth.csproj -c Release -r win-x64 -m:1 -v:q
dotnet run --project tools/MZP18TestBrowserHarness/MZP18TestBrowserHarness.csproj -c Release -r win-x64 -v:q -- '%USERPROFILE%\Videos\GIVRE\GivreShareWork\tools\MZP18IsolatedWebAuth\bin\Release\net10.0-windows\win-x64'
dotnet build GivreClone.App/GivreClone.App.csproj -c Release -r win-x64 --no-restore -m:1 -v:q
py -3 -B tools/MZP18Release/package.py
```

## BLOCKERS GAMEPLAY — VRAIES PREUVES REQUISES

1. Le `GameLauncher` existant attend `IIndependentTestGameLaunchAuthority`.
   `LaunchService` construit actuellement un `GameLauncher()` sans cette
   autorité et garde les ports historiques 26666/5658, qui sont protégés : le
   refus **avant toute authentification historique et tout lancement** est
   correct. Ce MZP18 ne le contourne pas.
2. Launcher 2 `app.asar` et `ZaapService.js` statiquement vérifiés (MZP10/MZP11).
   Contrats Thrift `connect`, `auth_getGameToken`,
   `auth_getGameTokenWithWindowId` connus ; argv/env/CWD/ticket enfants,
   identité et parenté d'un processus Dofus réellement démarré **non prouvés**.
3. Les données protobuf natives candidates
   `DofusReverseIndexer/local-output/historical-extractions-v3/3.7.11.11`
   attestent des **artefacts de schéma** locaux, mais leur manifeste dit
   `certified=false`, `review_only=true`, `protocol_promotion=false`.
   Leurs 2 260 messages / 6 493 champs et leur SHA de proto identique à
   3.7.10.10 ne prouvent ni le dispatch, ni les messages inbound, ni un ACK.
   `GivreClone.Protocol/MZP0ProtocolAdmission.cs` cible encore explicitement
   3.7.10.10 : le code MZP4/5/7 **refuse** donc la version installée 3.7.11.11,
   tant que la chaîne indépendante exacte ne sera pas établie.
4. Un véritable TEST devra utiliser un compte saisi interactivement et un
   enfant Dofus attribué indépendamment, jamais des secrets historiques, les
   ports 5657/5658/26666, les processus des trois bots ou un PID seul.

## NEXT

Récupérer la recette officielle exacte de création d'un enfant Dofus sous
Launcher 2 et une autorité de session/transport attribuable au clone, puis
connecter le flux TEST `OAuth -> Launcher/Zaap -> enfant Dofus ->
réponse protocole version exacte -> personnage/carte -> action/ACK`.
Les garde-fous MZP9–MZP17 restent actifs. Ne pas déclarer le clone complet avant
les preuves gameplay et la matrice de parité sur le programme principal natif.

## LIVRAISON DÉTERMINISTE

Le paquet reconstruit final (application historique compilée + WebAuth original
MZP17 + WebAuth TEST rebâti et isolé) a été produit deux fois au même SHA-256 :

```
artifacts/MZP18-Historical-GivreCloneApp-WinX64-ISOLATED-AUTH-OFFLINE-ONLY.zip
bytes=35201234
members=88
sha256=C14E8F898918197A4A4F8E4CEF74775169CA852738EE5DB245880848C46D5A7A
reproducible=true
```

Ce ZIP n'est **pas** un installateur. Ne pas le déployer par-dessus
`%LOCALAPPDATA%/Givre` ni l'exécuter comme un client certifié.
Le dernier rebuild incrémental Release avant ce package a terminé avec
`0 erreur / 0 avertissement` ; le rebuild complet antérieur du jalon avait
`0 erreur / 153 avertissements historiques`, Debug
`0 erreur / 154 avertissements`.
