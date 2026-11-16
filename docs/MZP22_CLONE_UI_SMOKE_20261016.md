# MZP22 — essai réel de l'interface du clone local

Date : 16 octobre 2026. Lancement du seul exécutable reconstruit
`artifacts/MZP19LocalBuild/GivreClone.exe`, processus Windows PID 21196,
démarré à 15:07:20 (heure locale). Sa fenêtre est distincte de l'application
GivreClone originale installée dans `%LOCALAPPDATA%/Givre/app/`.

## Résultats observés sur l'interface Avalonia

| Test | Observation | Résultat |
| --- | --- | --- |
| Démarrage de l'exécutable reconstruit | Fenêtre `GivreClone v1.3.1 · clone local`, processus Windows réactif (`Responding=True`) | PASS GUI |
| Navigation présente | Boutons `Comptes`, `Bots`, `Équipe`, `Proxys`, `Adaptateurs`, `Mon compte`, `Réglages` visibles dans l'arbre d'accessibilité | PASS présence, actions individuelles non testées |
| Vue de bots | État `Aucun bot en jeu` | PASS affichage hors session |
| Vue Comptes | Panneau de profils TEST indépendants présent | PASS présence |
| Ouverture des profils TEST | Clic réel sur `Ouvrir l'espace de test`, puis présence des commandes `Créer`, `Sélectionner`, `Renommer`, `Relire le disque` | PASS ouverture, CRUD non testé |
| Statut du stockage TEST | `Stockage de test ouvert · aucune session réelle admise.` | PASS affichage |
| Vérification mapping | Clic sur `Revérifier version et mapping (sans réseau)` ; Dofus 3.7.11.11 affiché, catalogue non publié et mapping non lié à une session | PASS réponse UI, FAIL compatibilité gameplay |
| Authentification TEST | Bouton affiché désactivé sans profil sélectionné | Comportement constaté, pas de login exécuté |
| Combat, carte, déplacement, banque, HDV, Lua connecté et ACK serveur | Aucune session réelle, aucun client Dofus lancé depuis le clone | NON TESTÉS |

L'inspection a utilisé uniquement l'identité de la fenêtre issue du processus
reconstruit, suivie de `get_window_state` et de clics sur les boutons TEST et
mapping sans réseau. L'original est resté ouvert. Les contrôles relatifs aux
comptes réels, aux secrets, à l'authentification et au lancement d'un Dofus ne
sont pas intervenus dans cet essai.

Cette observation prouve un **démarrage GUI et deux interactions** et ne certifie
aucune des autres fonctionnalités du programme. La parité du code source avec
le GivreClone natif original 1.3.1 reste à démontrer.

## Correction de l'avertissement licence en mode local

L'application précédente présentait aussi le bandeau « Votre licence n'inclut pas
la permission Multi », héritage trompeur du serveur de licence historique.
Le modèle `AccountsViewModel.ShowLegacyMultiLicenseNotice` et le binding de
`AccountsView.axaml` n'affichent désormais ce bandeau qu'en mode historique.
Le contrôle de lancement TEST indépendant reste en place et aucune capacité
multi-comptes n'a été autorisée par cette modification d'affichage.

`dotnet build GivreClone.App/GivreClone.App.csproj -c Release -r win-x64 -m:1 -v:q
-p:OutputPath=%USERPROFILE%\Videos\GIVRE\GivreShareWork\artifacts\MZP22PrimeLocalBuild\`
a fini avec **0 erreur, 163 avertissements** (existants, notamment nullabilité
et avertissements Avalonia). L'exécutable résultant
`artifacts/MZP22PrimeLocalBuild/GivreClone.exe` a été lancé en processus
**PID 24744**, fenêtre `GivreClone v1.3.1 · clone local`, Windows `Responding=True`.
L'arbre d'accessibilité contient la navigation, les profils TEST et le statut
du mapping, mais **ne contient plus l'avertissement de licence Multi**.
La précédente fenêtre TEST PID 21196 a été fermée via sa fenêtre après
vérification de son chemin, sans intervenir sur le GivreClone original.
