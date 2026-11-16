# MZP5 — parcours réel de préparation/résilience TEST (hors réseau)

## Ce qui est effectivement raccordé

La **véritable** page `GivreClone.App/Views/AccountsView.axaml` conserve les
comptes historiques et monte `MZP4TestAccountsPanel`. Le parcours de TEST est :

1. Ouvrir volontairement le magasin MZP3 à bail exclusif.
2. Créer/sélectionner un alias et sauvegarder des candidats serveur/personnage
   (uniquement **hors ligne**, jamais authentifiés).
3. Cliquer « Préparer journal TEST privé ». La méthode de production
   `MZP3TestAccountService.PrepareIsolatedRecovery()` valide le bail, l'état
   sauvegardé, le dossier parent et sa DACL, puis crée exclusivement le sous-
   répertoire `recovery` avec DACL Windows protégée owner-only. Elle refuse
   fichier hostile/reparse, profil absent, état dirty ou approbation absente.
4. Cliquer « Initialiser et lire le journal TEST » :
   `InitializeAndInspectTestJournal()` ouvre le véritable
   `MZP0RecoveryJournal` dans **ce seul magasin de TEST** et affiche l'état
   catégoriel `MZP5RecoveryHealthProjection`. Aucune génération n'est activée.
5. Vérifier la configuration launcher MZP4 avec port explicitement choisi :
   lorsqu'elle est conforme, l'UI dit *candidat uniquement*, jamais connecté.

Code : `GivreClone.App/Services/MZP5TestRecoveryPreparation.cs`,
`GivreClone.App/Services/MZP5OfflineJournalHealth.cs`,
`GivreClone.Engine/Recovery/MZP5RecoveryHealthProjection.cs`,
`GivreClone.App/Views/MZP4TestAccountsPanel.cs`.

Le journal réel contient uniquement métadonnées (UID interne, révision,
génération, catégories) selon le contrat MZP0. La projection publie seulement
comptages, nécessité de revue, version du journal et capacité locale; aucun
payload, identifiant de compte, mot de passe, frame ou secret. Tout journal
indisponible ou incohérent retourne **Unavailable**, jamais un statut vert.
Une action incertaine ou une fenêtre de dispatch héritée impose une revue
explicite. Aucun code de ces composants ne fait d'envoi, de reconnexion ou de
rejeu d'opération, et leurs flags ACK/H24/Connecté restent `false`.

## Validation reproductible

Dans `/givre/GivreShareWork` (Windows, SDK .NET 10) :

```powershell
dotnet run --project tools/MZP5RecoveryPreparationHarness/MZP5RecoveryPreparationHarness.csproj -c Release --no-restore
dotnet run --project tools/MZP5RecoveryHealthHarness/MZP5RecoveryHealthHarness.csproj -c Release --no-restore
dotnet run --project tools/MZP4AccountsUiHarness/MZP4AccountsUiHarness.csproj -c Release --no-restore
```

Résultats vérifiés par PRIME : **16 tests Windows de fichiers/ACL**, **12 tests
du vrai MZP0RecoveryJournal** (état incertain après crash, reprise, preuve
positive exigée, aucune retransmission), **25 tests Avalonia Headless UI**.
Les tests créent des dossiers aléatoires dédiés, vérifient leur appartenance
avant nettoyage et ne consultent pas le compte ou le processus GivreClone actif.

Ces services sont compilables comme sources de production et raccordés en
AXAML; **l'application principale entière reste bloquée** par les projets
sources GivreCore et GivreLauncher.Core manquants. Aucun test du vrai MainWindow
ou gameplay ne peut être inféré de la validation Headless.
