# MZP4 — vrai onglet Bots / Paquets : inspecteur local et borné

Le vrai `GivreClone.App/Views/BotsView.axaml` conserve le flux de paquets,
les filtres et l'inspecteur historiques. Sous ce dernier, il monte
`MZP4OfflinePacketPanel`, un contrôle additionnel **strictement hors ligne**.
Il n'interroge ni compte, ni session, ni processus, ni socket et ne prend
aucun octet du flux existant : seule la saisie volontaire est analysée.

La commande « Analyser localement » utilise réellement `MZP3RawProtobuf`
du produit, avec ses bornes (131072 octets par défaut), puis affiche uniquement
numéro du champ, type wire et taille de sa valeur. Aucun contenu, hex brut,
texte de chaîne, identifiant, nom de serveur ou ACK ne passe dans la sortie.
La saisie de données sensibles reste déconseillée : les chaînes de l'UI,
les copies internes du parseur et le GC ne garantissent pas l'effacement
cryptographique total. Le buffer hex temporaire détenu par le panneau est
effacé en `finally` ; « Effacer la saisie » réinitialise le champ visible.

Validation isolée, sans GivreCore de substitution :

```powershell
Set-Location %USERPROFILE%\Videos\GIVRE\GivreShareWork
dotnet restore tools/MZP4PacketsUiHarness/MZP4PacketsUiHarness.csproj --ignore-failed-sources
dotnet run --project tools/MZP4PacketsUiHarness/MZP4PacketsUiHarness.csproj -c Release --no-restore
```

**8/8 PASS** Avalonia Headless : structure initiale sans capture, liaison
présente dans vrai AXAML `BotsView`, clic d'inspection depuis un champ hex
neutre, lecture par vrai codec, valeurs masquées, rejet entrée Protobuf
tronquée, rejet hex invalide, effacement manuel de saisie.

Ce contrôle est intégré au code source de la vraie App, mais le WinExe
`GivreClone.App` n'est pas encore compilable faute de `GivreCore` et
`GivreLauncher.Core`. Ce test ne valide pas le runtime de `MainWindow`, ni
le trafic de jeu, ni le moteur MITM, ni un ACK ou un état de gameplay.
