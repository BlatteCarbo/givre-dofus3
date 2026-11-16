# MZP24 — Métier / XP de niveau : réintégration source-owned (16 octobre 2026)

## R — résultat

Le type `ViewModels.JobRow` apparaît dans la récupération des bindings de l'original (`docs/MZP23_VIEWMODEL_PARITY.md`) mais était absent du clone. Le clone dispose désormais de ce type, de l'état par PID `JobProgressSnapshot`, du décodage des bornes XP et d'une carte **Métiers** conditionnelle dans la vue Bots.

**Source des informations** : `JobExperiencesUpdateEvent` / `JobLevelUpEvent` déjà routés par `LaunchService.Packets.cs`. Les noms de métiers viennent, si disponible, de la table `JobData` du jeu (base locale `GameDb` en lecture seule) ; sinon l'interface affiche explicitement `Métier #id`.

La progression n'est affichée que si le serveur fournit un triplet cohérent `job_xp`, `job_xp_level_floor` et `job_xp_next_level_floor`. Une notification de changement de niveau supprime la progression de l'ancien niveau en attendant un snapshot frais. Cela empêche de déduire une XP inconnue d'un niveau seul.

## C — modifications

- `Services/JobProgressSnapshot.cs` : modèle immutable, validation des bornes, ratio relatif et formatage.
- `ViewModels/JobRow.cs` : les cinq bindings observés `Icon`, `Level`, `Name`, `Percent`, `XpText`, avec `HasXp` pour n'afficher une barre que si l'XP est connue.
- `Services/LaunchService.cs` / `LaunchService.Bank.cs` / `LaunchService.Packets.cs` : stockage par PID, snapshot en lecture, extraction par noms de champs du mapping existant, sans champ/opcode fictif.
- `ViewModels/BotRowViewModel.cs` / `BotsViewModel.cs` : rafraîchissement des niveaux toutes les ~1,5 seconde uniquement pour le bot sélectionné, sans flicker quand l'état est identique ; remise à zéro de la vue après changement de PID.
- `Views/BotsView.axaml` : carte visuelle métiers, progression conditionnelle.
- `GivreClone.Tests/JobProgressSnapshotTests.cs` et `GivreClone.Tests.csproj` : tests purs sous .NET, sans dépendance au vrai client.

## V — validations réelles

- `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release -m:1 --filter FullyQualifiedName~JobProgressSnapshotTests -v:q` : **9/9 PASS**, sortie `artifacts/MZP24JobProgressTests.log`.
- `dotnet build GivreClone.App/GivreClone.App.csproj -c Release -r win-x64 -m:1 -v:q -p:OutputPath=.../artifacts/MZP24JobBuild/` : **0 erreur, 164 avertissements**, sortie `artifacts/MZP24JobBuild.log`; exécutable `artifacts/MZP24JobBuild/GivreClone.exe`.

## B — bornes / non-prouvés

- Les numéros de champs `JobExperience` se fondent sur le catalogue candidat Dofus 3.7 fourni dans `data/fields.json`. Ils n'ont pas été vérifiés sur une trame **réelle** attribuée au clone dans ce lot.
- Pas d'XP récupérable avec une simple notification `JobLevelUpEvent`. L'affichage marque donc `XP non disponible` jusqu'à l'événement d'expérience suivant.
- Aucun personnage/test Dofus n'a été connecté pendant cette intégration ; les tests sont offline et les champs ne sont pas encore certifiés par ACK serveur.
- Le rapprochement avec les cinq propriétés de l'original **ne prouve pas** une équivalence des règles de présentation/calcul de l'application originale.
- Aucune modification de l'application ou du processus GivreClone d'origine.
