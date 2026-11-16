# Tâches pour Codex — 23/10/2026

Branche de référence : `integration-opus-20261020` (version 1.3.3.11+). **Pas `master`** : `master` est resté en 1.3.1 et n'a ni la fenêtre banque/échange, ni le combat manuel, ni la recherche Ctrl+K.

## Règles pour ne pas se marcher dessus

- Travaille dans **ton propre worktree** créé depuis `integration-opus-20261020` (ex. `git worktree add ../GivreWork-Codex -b codex-ui-20261023 integration-opus-20261020`). Ne commite jamais directement dans `GivreWork-Integration-20261020` : Claude y travaille en continu.
- **Fichiers réservés à Claude** (ne pas modifier) : `Services/LaunchService.Forge.cs`, `Services/Eco/*Rune*`, `Services/Eco/ForgeState.cs`, `ViewModels/SmithMagicViewModel*.cs`, `Views/SmithMagicView.axaml`, `Services/LaunchService.Combat*.cs`, `Services/Combat/*`, `Services/GivreLuaHost.Travel.cs`, `Services/LaunchService.Movement.cs`, `Services/LaunchService.Bank.cs`, `ViewModels/TreasureViewModel.cs`, `Services/Treasure/*`, `ViewModels/BotsViewModel.Combat.cs`, `ViewModels/SpellRowViewModel.cs`.
- Langues : toute nouvelle clé dans `Lang/fr.axaml`, `en.axaml`, `es.axaml` doit être **unique** (le test `FightSummaryTextTests` plante sur une clé en double). Vérifie avec `grep -c 'x:Key="TaCle"'`.
- Avant de rendre : `dotnet build GivreClone.App` (pas seulement les tests : le projet de tests ne compile qu'une partie des fichiers de l'appli), puis `dotnet test GivreClone.Tests`.
- La base du jeu est maintenant installée : `%LOCALAPPDATA%\GivreReplica\TestRuns\givre-app-offline\userdata\gamedata\dofus.sqlite` (ledouxm/dofus-sqlite v6.0_3.7.12.12, SHA-256 556567d8…). Ne la remplace pas.

## Tâches (par ordre de priorité)

### T1 — Console : chat lisible et joli
L'onglet Console affiche le chat en texte brut : `{chatitem,5389040,8270}`, `{chatmonster,1159}`, `{chatachievement,3074}`, `{lobby,…}`, `&gt;`, canaux « Canal 11 » / « Canal -1 ».
- Remplacer les balises par des pastilles (nom + icône de l'objet / du monstre / du succès, via `GameNames` et les icônes existantes).
- Décoder les entités HTML (`&gt;` → `>`).
- Nommer les canaux (Général, Commerce, Recrutement, Groupe, Guilde, Alliance, MP) avec une couleur par canal, comme dans le jeu.
- Mise en page : heure discrète, pseudo en gras coloré, message lisible, lignes aérées, thème sombre existant (`Surface1Brush`, `TextSecondaryBrush`…).

### T2 — Icônes de la barre latérale de la fiche du bot
« Expédition », « Trajets », « Chasse au trésor » et « Forgemagie » n'ont pas d'icône, contrairement aux autres onglets. Ajoute des icônes cohérentes avec les existantes (`Styles/Icons.axaml` ou icônes du jeu déjà chargées).

### T3 — Infobulles d'objets façon jeu
Au survol d'un objet (Inventaire, Banque, échange, HDV) : fiche comme dans Dofus — nom, niveau, type, panoplie, liste des effets avec icône par caractéristique et couleur (positif vert, négatif rouge, 0 grisé), poids, prix moyen. Les effets réels de l'objet viennent de `LaunchService.ItemEffects(pid, uid)` / `ItemEffectLines`.

### T4 — Thème sombre : polissage général
Uniformiser les cartes (coins, marges, bordures `StrokeSoftBrush`), les en-têtes de section, les boutons ghost/primary, les listes. Viser le rendu des pages Vue d'ensemble / Comptes déjà propres. Pas de changement fonctionnel.

### T5 — Idées de dofus-enhanced
Lire https://github.com/Altansar69/dofus-enhanced (lecture seule, ne rien exécuter de ce dépôt). Lister dans `docs/codex/DOFUS_ENHANCED_IDEES.md` les fonctionnalités utiles absentes de GivreClone, avec pour chacune : intérêt, faisabilité (MITM/Socket), fichiers concernés. Implémenter ensuite celles qui sont purement d'interface et hors fichiers réservés.

### T6 — Expédition et listes de zones
Avec la base du jeu désormais présente, vérifier que l'onglet Expédition liste bien zones, sous-zones et monstres (le message « Catalogue GameData local introuvable » doit disparaître au redémarrage). S'il reste un trou, le corriger.

## Rendu attendu
Un commit par tâche dans ta branche, message en français, puis un court compte rendu dans `docs/codex/RENDU_CODEX_20261023.md` (ce qui est fait, ce qui ne l'est pas, comment vérifier). Claude intégrera ta branche.
