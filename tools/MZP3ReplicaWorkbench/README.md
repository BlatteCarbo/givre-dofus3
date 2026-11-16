# MZP3ReplicaWorkbench — application Windows indépendante

Application réellement exécutable, mais **pas encore un remplacement fonctionnel
de GivreClone v1.3.1**. Aucun client Dofus, processus original ou compte GivreClone
n'est lancé, capturé, injecté ou modifié.

## Parcours utilisables

- Comptes : création, liste, sélection, renommage et suppression des profils
  non secrets, sélection serveur/personnage CANDIDATE seulement, persistance
  atomique sous %LOCALAPPDATA%/GivreReplica/TestRuns/workbench.
- Paquets : génération d'un message Protobuf NEUTRE et inspection de champs
  d'octets hexadécimaux dans l'UI à travers le véritable codec de production
  MZP3RawProtobuf. Aucun mapping Dofus ni type métier certifié.
- Diagnostics : vrai panneau MZP0OperatorPanel partagé, session/ACK inconnus.
- Bots, Combat, Carte, Banque, HDV, Lua, Planning, etc. : navigation visible
  et message de blocage. Aucune commande de gameplay activée sans moteur réel.

Le WinExe compile les vrais sources MZP3TestAccountService.cs et
MZP3RawProtobuf.cs en lien direct ; l'adaptateur Admission qui exige
GivreCore est volontairement exclu de cet exécutable pour ne pas introduire
de stubs ou simuler des dépendances de production.

## Compilation

Depuis %USERPROFILE%\Videos\GIVRE\GivreShareWork :

    dotnet build tools/MZP3ReplicaWorkbench/MZP3ReplicaWorkbench.csproj -c Debug
    dotnet restore tools/MZP3ReplicaWorkbench/MZP3ReplicaWorkbench.csproj -r win-x64
    dotnet publish tools/MZP3ReplicaWorkbench/MZP3ReplicaWorkbench.csproj -c Release -r win-x64 --self-contained false --no-restore

EXE (nécessite .NET 10 Desktop Runtime) :

    tools/MZP3ReplicaWorkbench/bin/Release/net10.0-windows/win-x64/publish/MZP3ReplicaWorkbench.exe

Ne jamais installer dans le répertoire du véritable GivreClone.

## Validation HWND sous Windows

Deux exécutables test (Debug puis Release) ont réellement été ouverts.
Une fiche de test non secrète a été créée, persistée, retrouvée au
redémarrage Release et supprimée (liste vide à la fin).
Dans Paquets, les boutons Charger une fixture neutre puis Inspecter sans
transmettre ont affiché un vrai résultat 1 champ, numéro 1, Varint.
Aucun réseau, token, payload de jeu ou serveur ACK n'a été observé.
Seuls nos propres processus Workbench ont été refermés.
