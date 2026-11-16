# MZP4 — profils de test dans la véritable page Comptes

## Intégration réelle, limite réelle

`GivreClone.App/Views/AccountsView.axaml` héberge directement
`MZP4TestAccountsPanel`, un `UserControl` Avalonia compilable à partir des sources
de production du service `MZP3TestAccountService`. Ce n'est **pas** une nouvelle
sidebar ni un remplacement des vues existantes. Le panneau apparaît dans le
parcours Comptes d'origine, avant les comptes/commandes historiques.

La première ouverture du magasin nécessite un clic explicite « Ouvrir l'espace
de test ». Aucune création de répertoire, lecture de compte de production,
connexion, injection, socket ou recherche de processus n'est effectuée au
constructeur. Le magasin utilisé par l'application est
`%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline` : ce dossier est
indépendant des comptes authentifiés, de l'installation originale et de la
clé distincte `workbench` du WinExe MZP3.

Actions câblées vers les **vraies méthodes** de persistance : créer,
sélectionner, renommer, enregistrer candidat serveur/personnage, effacer
candidat, supprimer et relire. `Save` est exécuté après chaque mutation ;
les erreurs `MZP3StoreException` restent visibles par code de refus. Un
serveur/personnage saisi n'est jamais promu en session authentifiée.

Le bouton « Vérifier l'isolation du TEST » emploie également le vrai
`MZP4LauncherPreflight` avec port saisi manuellement et chemins déduits du seul
run dédié. Il ne démarre aucun host, n'ouvre aucun socket et affiche des motifs
de refus catégoriels. Il refuse notamment le port 5658 et le dossier journal
`recovery` absent, sans le créer automatiquement. Un succès éventuel reste un
**candidat de configuration**, jamais une autorisation de connexion.

`DetachedFromVisualTree` ferme le bail du seul magasin possédé sans rendre
définitivement inutilisable la vue lorsqu'Avalonia la garde en cache :
la réouverture demandera un nouveau clic explicite. Une ouverture asynchrone
terminée après navigation est rejetée par génération. Aucun bouton
de ce panneau ne peut démarrer une session. Les comptes historiques du VM
restent inchangés, et le panneau ne référence ni `GivreLauncher.Core` ni
`GivreCore` pour sa logique hors ligne.

## Vérification reproductible

```powershell
Set-Location %USERPROFILE%\Videos\GIVRE\GivreShareWork
dotnet run --project tools/MZP4AccountsUiHarness/MZP4AccountsUiHarness.csproj -c Release --no-restore
```

Vérifié : **19 contrôles Avalonia Headless PASS** avec **source produit liée,
sans stubs**, dont absence d'accès avant consentement, présence du panneau dans
le vrai AXAML, clic explicite, réelle persistance locale temporaire, création
UI → service MZP3 → disque, affichage honnête de candidats non certifiés,
prévol App/Launcher depuis un port saisi avec deux refus vérifiés,
fermeture et nettoyage du seul répertoire de test généré. Aucun processus ou
compte de jeu n'est lu.

**Ne pas surestimer :** `GivreClone.App` complet reste `CS0246 GivreCore`, car
`GivreProxy/GivreCore` et `GivreLauncher/GivreLauncher.Core` sont absents.
La nouvelle vue est compilée/testée isolément ; le vrai `MainWindow` n'a pas
été démarré. Aucun parcours login, ACK, combat ni gameplay n'est attesté.
