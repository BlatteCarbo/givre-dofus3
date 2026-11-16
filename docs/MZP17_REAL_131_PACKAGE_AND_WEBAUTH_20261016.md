# MZP17 — distribution originale retrouvée, WebAuth raccordé

## RESULT

Le chemin fourni par l'utilisateur %USERPROFILE%\AppData\Local\Givre contient la véritable distribution ZIP présente sur ce poste :

- update/pkg.zip : 93 234 109 octets, SHA-256 D66690B642204FC35B3EF2E4A3B9EF53483B41A447B0BFE3C04DFA2D38DB06FD.
- 22 membres ZIP, 115 859 840 octets décompressés, sans encryption ou path traversal.
- app/GivreClone.exe : 54 083 584 octets, SHA-256 21C9DE5BA4D15763E9DAF029FD7432E3574EA113C9D1F44175FA9E0590290758.
- L'entrée GivreClone.exe dans le ZIP a exactement le même SHA que l'exécutable installé.

ATTENTION : la ressource FileVersion annonce 1.0.0.0 et non le numéro commercial v1.3.1. Le nom du setup annonce 1.3.1 mais ce ZIP n'est pas une attestation cryptographique autonome de ce numéro. Il prouve la distribution du programme effectivement installé.

La copie isolée est sous offline-evidence/v131-app/GivreDistribution-candidate-D66690B6.zip. Aucune installation ni client n'a été exécuté.

## CODE AUTHENTIQUE ET DECOMPILATION

- GivreClone.exe : PE64 natif sans CLR, avec indication NativeAOT et sections aux noms atypiques. Source C# exacte non obtenable simplement via ILSpy.
- GivreKama.dll et GivreMods.dll : binaires natifs protégés/empaquetés probables, aucune conclusion de malveillance.
- GivreMcp.exe : NativeAOT probable.
- WebAuth/GivreLauncher.WebAuth.exe : bootstrap natif empaqueté.
- WebAuth/GivreLauncher.WebAuth.dll : véritable assembly CLR décompilé avec ILSpy en 8 fichiers C# sous offline-evidence/v131-app/decompiled-WebAuth-IL.
- tools/MZP17RecoveredWebAuth/MZP17RecoveredWebAuth.csproj : ces fichiers C# originaux décompilés ont été compilés en Release Windows x64 avec zéro erreur et 37 warnings de nullabilité. La reconstruction du bootstrap natif n'est pas incluse.

## CHANGES

- tools/MZP17ExtractOwnedDistribution.py : extraction code-only à liste blanche de 9 fichiers, SHA attestés.
- tools/MZP17StageWebAuth.py : copie 10 fichiers WebAuth depuis le ZIP isolé vers vendor/GivreWebAuth131, empreintes vérifiées.
- GivreClone.App/GivreClone.App.csproj : copie automatique du compagnon WebAuth officiel dans les sorties Debug/Release en l'absence de projet WebAuth construit séparément. Avant cela, le composant était absent du build du clone.
- GivreClone.App/Services/MZP17WebAuthIntegrity.cs : validation des fichiers WebAuth, SHA, tailles, liens symboliques et paths ; refus d'exécuter un composant altéré.
- GivreClone.App/Services/WebAuthRunner.cs : utilise uniquement le compagnon vérifié et neutralise un flag de diagnostic hérité pouvant provoquer des requêtes non essentielles. Aucune licence ni 2FA contournée.
- tools/MZP17WebAuthContract.py : compare sans exposer de secret le contrat d'appel du helper décompilé et de notre runner (stdin, HMAC 30 secondes, arguments, JSON réponse).

## VALIDATION

| Contrôle | Résultat |
| --- | --- |
| ZIP installé vs copie isolée | SHA identiques |
| EXE ZIP vs exécutable installé | SHA identiques |
| PE statiques | 9 classifiés |
| WebAuth DLL IL | 8 fichiers C# récupérés |
| WebAuth source recompilé Release | 0 erreur / 37 warnings |
| WebAuth véritables fichiers staged | 10 SHA validés |
| Intégrité du compagnon | 10 PASS offline |
| WebAuth decomp / runner | 18 PASS offline |
| GivreClone.App Release win-x64 | 0 erreur / 143 warnings |
| GivreClone.App Debug win-x64 | 0 erreur / 144 warnings |
| Archive reproductible | 80 membres, 34 846 300 octets |

Archive : artifacts/MZP17-Historical-GivreCloneApp-WinX64-WEBAUTH-OFFLINE-ONLY.zip
SHA-256 : 9D4144A6B7CAAAC8D160BB836668C411E1DA22511B437744AD867C66C0D28E94

Cette archive contient la nouvelle application historique reconstruite et le compagnon WebAuth authentique, pas le binaire GivreClone original entier rebaptisé clone.

## BLOCKERS

La présence de la distribution supprime le blocage ancien « original absent », mais le corps de GivreClone.exe est NativeAOT/protégé et ne fournit pas directement ses sources C# en IL. La version reconstruite n'est pas encore jouable à 100 %.

Aucun login Ankama, serveur, personnage, carte, action, réponse corrélée, banque, combat ou HDV réel n'a été testé ici. Le lancement du client TEST et la preuve d'identité/provenance restent la priorité suivante ; jamais de bypass de licence/authentification.

L'installation originale et ses trois bots actifs n'ont été ni lancés, ni arrêtés, ni injectés ni modifiés. Les dossiers GivreLauncher et GivreReplica n'ont pas été ouverts au-delà de leurs noms de répertoires. Aucun profil, compte, dump ou jeton privé n'a été lu.
