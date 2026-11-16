# MZP4 PRIME — 16 octobre 2026

## RESULT

Nouvelle verticale réellement reliée **dans la page Comptes existante** :
profil de TEST isolé, validation manuelle, CRUD et sauvegarde locale. Codec
de production neutre MZP4 compilable intégré aux sources `GivreClone.Protocol`.
Correctif de confidentialité du replay P0 Givre validé. Le GivreClone installé
et ses trois sessions n'ont fait l'objet d'aucune commande dans cette passe.

**L'objectif de reconstruction complète de GivreClone v1.3.1 n'est PAS atteint.**
Les sources `GivreCore` / `GivreLauncher.Core` sont absentes ; l'App complète
ne compile pas. Aucun login, ACK ou gameplay réel n'est certifié.

## CHANGES

- `GivreClone.App/Views/AccountsView.axaml` : montage direct du panneau offline
  dans la véritable vue Comptes. Rien n'a été supprimé des contrôles existants.
- `GivreClone.App/Views/MZP4TestAccountsPanel.cs` : ouverture opt-in, 7 actions
  locales, gestion du bail, feedback d'échec, candidatures non certifiées.
- `tools/MZP4AccountsUiHarness/` : Avalonia Headless sur code de production,
  vrais clics UI → service MZP3 → disque, dossier test unique supprimé.
- `GivreClone.App/Services/MZP4LauncherPreflight.cs` : module worker-1 repris,
  contrôle read-only des choix de ports et ACL/chemins de TEST, sans autorité
  de démarrage ni réseau; `tools/MZP4LauncherHarness/`. Intégré dans le panneau
  `MZP4TestAccountsPanel` avec port explicite et refus détaillé.
- `GivreClone.App/Views/BotsView.axaml` et nouveau
  `MZP4OfflinePacketPanel.cs` : inspection neutre par vrai MZP3 codec dans
  l'onglet Paquets de l'App existante, sans liaison au flux d'un bot et sans
  publication des valeurs de champs. `tools/MZP4PacketsUiHarness/`.
- `GivreClone.Protocol/MZP4ProtocolAdapter.cs` : module du worker-1, vrais MZP0
  + MZP3, catalogues version/SHA, builders de fields et enveloppes neutres,
  sans shim `GivreCore`; `tools/MZP4ProtoIntegrationHarness/`.
- `docs/MZP4_ACCOUNTS_PRODUCT.md`, `docs/MZP4_PROTO_PRODUCT.md`.
- `docs/MZP4_PACKETS_UI.md`.
- `docs/MZP4_LAUNCHER_BOUNDARY.md`.
- GivreRework : `P0SameBytesDifferential.cs`,
  `P0DualDecoderOrchestrator.cs`, `tests/MZP4ReplayPrivacyHarness/`,
  `docs/MZP4_PRIVATE_REPLAY_LIFETIME.md`, ajout au `HANDOFF.md`.
- Snapshot `tools/MZP2DependencyInventory.json` réécrit et vérifié après les
  ajouts (510 références lexicales/100 fichiers, fournisseur toujours absent).

## VALIDATION

Depuis GivreShareWork, **tous rerun par PRIME** :

| Contrôle | Résultat |
| --- | --- |
| `MZP4ProtoIntegrationHarness` | **30/30 PASS Release** |
| `MZP4AccountsUiHarness` | **19/19 PASS Release** (Headless + IO test + prévol) |
| `MZP4LauncherHarness` | **35/35 PASS Release** (source App, ACL Windows) |
| `MZP4PacketsUiHarness` | **8/8 PASS Release** (Headless + codec MZP3) |
| `MZP1GuardedWiringHarness` | **29/29 PASS Release** |
| `MZP2HostAdmissionHarness` | **41/41 PASS Release** |
| `MZP3TestAccountsHarness` | **27/27 PASS Release** |
| `MZP3AdmissionLinkHarness` | **7/7 PASS Release** |
| `MZP2DependencyInventory --verify` | **PASS** |
| `git diff --check` | **PASS**, ne couvre pas les untracked |

Depuis GivreRework :

| Contrôle | Résultat |
| --- | --- |
| `MZP4ReplayPrivacyHarness` | **11/11 PASS offline** |
| `MZP3ReplayHarness` | **38 PASS offline** |
| `DesktopHost -c Release -p:EnableAvaloniaPro=true` | **0 erreur, 0 avertissement** |

`dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore
-m:1 -v:q` : **FAIL**, `CS0246 GivreCore` dès `CharacterInfoReader`,
`CharacterSpellsReader`, `EnvelopeBuilder` et lecteurs métier. Aucun build
complet n'est annoncé. Panneau MZP4 compilé en propre, pas MainWindow.

Agents : worker-1 réellement lancé puis terminé (protocole). Les tentatives
worker-2 et worker-3 ont échoué au démarrage; PRIME a repris l'UI et la
confidentialité P0. Aucun résultat fictif attribué à ces workers.

## BLOCKERS / suite

1. Fournisseurs `GivreProxy/GivreCore` et `GivreLauncher/GivreLauncher.Core`
   authentiques toujours absents. Aucune classe de remplacement fantôme.
2. Aucun vrai décodeur legacy versionné Dofus 3.7.10.10, aucune capture
   entrant serveur authentifiée, aucun vérificateur indépendant d'ACK.
3. UI Comptes **liée en source** à l'App historique, mais la fenêtre complète
   ne peut être exécutée sans dépendances; vérification UI isolée seulement.
4. G0 Givre reste NOT_COMPARED. P0 et H24 gameplay non certifiés.

Priorité : récupérer des sources fournisseur autorisées ou établir des ports
de transport/protocole complets prouvés, puis build entier de la vraie App ;
obtenir une instance de TEST distincte prouvée avant toute connexion réelle.
