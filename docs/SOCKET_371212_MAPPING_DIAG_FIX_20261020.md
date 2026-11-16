# Dofus 3.7.12.12 — correction de la détection de version et essai Socket borné

## Cause diagnostiquée sur l'installation réelle (20/10/2026)

Le clone détectait `manifest.cytrus` à côté de `Dofus.exe` puis considérait
`3.7.11.11` comme build courant, tandis que le client officiel est désormais
installé en **3.7.12.12**. Les fichiers contrôlés, **en lecture seule**, sont :

- `%LOCALAPPDATA%/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/version` :
  `Version=3.7.12.12`, `BuildDate=2026-10-19T15:11:45+00:00`.
- `%LOCALAPPDATA%/Ankama/Games/dofus/manifest.cytrus` : ancien
  `releaseVersion=6.0_3.7.11.11` ; son origine différente de l'installation
  et son décalage en font une mauvaise autorité pour la version installée.
- `data/bundle-build.json` : source de mapping local déclarée **3.7.11.11**.

`MZP11CytrusVersionProbe.InspectInstalledDofus` lit donc le marqueur de
l'installation Dofus effectivement choisie, exige le format quatre parties,
et lie l'identité au SHA-256 composite des quatre fichiers présents dans
l'installation : version, Dofus.exe, GameAssembly.dll, global-metadata.dat.
Le résultat réel du probe était **3.7.12.12** avec l'empreinte
`D969124783F12A3BDE6D1CA6C360CECA2C34E49C0E28CDB4425B2A73FB5C399C`.
Ce SHA représente un état installé à un instant T, pas une signature du
serveur. Les marqueurs invalides ou multiples bloquent ; aucun manifeste de
launcher obsolète n'est alors substitué.

## Schéma Protobuf — égalité réelle vérifiée

L'outil natif Protodec `il2cpp` installé sous DofusReverseIndexer a été
exécuté **hors jeu** sur les binaires locaux 3.7.12.12, sans lancer ni modifier
Dofus.exe, puis le résultat comparé à l'extraction 3.7.11.11 de l'indexeur.
Les deux `.proto` ont exactement **244 643 octets** et le même SHA-256 :

`BF3799E516EE52D968432DB5651FA662A23308D94AC8314862189E59DBD41651`

Cela prouve l'égalité des **schémas extraits par cet outil**, et rend plausible
la réutilisation du catalogue local. Cela ne prouve **pas** les alias `Any`
sur le réseau, les codes d'enveloppe ni un ACK serveur. Le fichier source
`data/bundle-build.json` conserve honnêtement sa version d'origine 3.7.11.11.

## Revue explicite opérateur pour un essai de connexion

L'utilisateur a déclaré explicitement que les mappings des deux patchs sont
les mêmes. Un reçu spécifique et local (`compatibility-review.json` dans le
dossier `data` du build Release) permet de traiter la paire 3.7.11.11 →
3.7.12.12 comme **candidate en revue**, non comme mapping certifié.

Génération :

```powershell
$root = '%USERPROFILE%\Videos\GIVRE\GivreWork-Integration-20261020'
$dri = '%USERPROFILE%\Videos\GIVRE\DofusReverseIndexer'
python "$root\tools\socket-mapping-review\prepare_review.py" `
  --game-root "$env:LOCALAPPDATA\Ankama\Dofus-dofus3" `
  --data-root "$root\GivreClone.App\bin\Release\net10.0\data" `
  --source-proto "$dri\local-output\historical-extractions-v3\3.7.11.11\dofus-3.7.11.11.proto" `
  --target-proto "$root\artifacts\protocol-target-review-20261020\dofus-3.7.12.12.proto" `
  --source-build 3.7.11.11 --target-build 3.7.12.12 `
  --operator-confirms-same-mapping --write
```

Le générateur compare les SHA des deux `.proto`, valide le marqueur cible,
les hash des mappings locaux et l'empreinte des binaires. Toute modification
de ces sources invalide le reçu. Le reçu ne contient ni token, ni compte, ni
code privé et n'est pas versionné sur GitHub.

**En mode revue**, `MZP12SocketVersionBinding.Evaluate` distingue
`ReviewedCrossPatchCandidate` de `Refusal.None`, affiche la provenance et
autorise uniquement un **essai de connexion**. `SocketProbe` désactive
`RegisterInjector`, `UnregisterInjector` et les modes de combat Turbo pour
ce candidat : les actions Lua, déplacements et transactions HDV ne sont pas
pilotables via Socket tant que les codes wire et ACK ne sont pas vérifiés.
Un nouveau build ou SHA différent fait échouer la revue.

## Résultats, limites et suite

- `InstalledDofusVersionBindingTests` : 11 tests sur marqueur, changement
  binaire, refus cross-patch non attesté, candidat explicite, empreintes et
  absence d'injecteur gameplay dans le mode de revue.
- `tools/socket-mapping-review/test_prepare_review.py` : 5 tests négatifs et
  positifs de génération du reçu.
- Source réévaluée depuis le clone autonome seulement : **ni GivreActive,
  ni MizanBot, ni Dofus.exe, ni secrets utilisateur ne sont modifiés**.
- Une authentification Socket effective et des messages de jeu correctement
  décodés doivent être observés sur un compte test avant admission runtime
  des mécanismes de déplacement/combat/HDV. Ne pas confondre égalité de
  `.proto` et identité complète du trafic réseau.
