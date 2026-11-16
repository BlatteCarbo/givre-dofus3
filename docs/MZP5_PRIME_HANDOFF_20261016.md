# MZP5 PRIME — 16 octobre 2026

## RESULT

Nouvelle passe intégrant un parcours produit plus complet dans la véritable
page **Comptes** de GivreClone.App, un journal de reprise **réel mais isolé**,
un diagnostic H24 purement catégoriel, un décodeur inbound neutre de production
et une admission de corpus P0 Givre plus stricte. Les workers 1 et 5 ont
réellement livré du code/testé. Les tentatives de démarrage workers 4 et 6
ont échoué au niveau de la fenêtre/composer MCP; leurs missions ont été
reprises directement par PRIME, sans les compter comme actifs.

**Ce n'est pas la reconstruction terminée de GivreClone v1.3.1.** Ni moteur
MITM actif authentifié, ni login, ni sélection serveur réelle, ni jeu et ACK
ne sont revendiqués. GivreClone original + trois sessions : aucun accès process,
configuration ou arrêt demandé durant cette passe.

## CHANGES

Production C# dans GivreShareWork :

- `GivreClone.Protocol/MZP5InboundProtocol.cs` (worker-1) : GameMessage
  Request/Event/Response neutre avec MZP0/MZP3/MZP4, catalogues pinnés,
  séquences séparées, quarantaine, inconnus préservés, sans producteur live.
- `GivreClone.App/Services/MZP5TestRecoveryPreparation.cs` (PRIME) : création
  consentie de l'unique répertoire recovery dans le TEST run MZP3, ACL
  protégée, sélection sauvegardée, anti-reparse.
- `GivreClone.App/Services/MZP5OfflineJournalHealth.cs` (PRIME) : initialise
  uniquement après un deuxième clic explicite le vrai `MZP0RecoveryJournal`
  dans cet espace TEST et projette son état sans activer de session.
- `GivreClone.Engine/Recovery/MZP5RecoveryHealthProjection.cs` (PRIME) :
  agrège vraies révisions/états de journal, rejets/incertitudes, jamais d'envoi
  ni de replay automatique, aucune valeur privée dans la sortie.
- `GivreClone.App/Views/MZP4TestAccountsPanel.cs` et
  `tools/MZP4AccountsUiHarness/` : deux commandes d'UI concrètes et leurs
  validations intégrées sur le vrai service de TEST.
- `tools/MZP5RecoveryPreparationHarness/`,
  `tools/MZP5RecoveryHealthHarness/`,
  `tools/MZP5ProtocolHarness/`, `docs/MZP5_PROTOCOL.md`,
  `docs/MZP5_RECOVERY_PRODUCT.md`.
- `tools/MZP5Provenance/parity_audit.py` et `parity_snapshot.json` :
  **14 capacités**, témoins de symboles/ligne/SHA sur sources réelles,
  sans scans ZIP/dump ni prétendre décompiler GivreClone.exe.

Dans GivreRework :

- `src/GivreRework.Infrastructure/Protocol/Differential/MZP5ExactCorpusAdmission.cs`
  (worker-5), `tests/MZP5P0Harness/`, `docs/MZP5_P0_ADMISSION.md`.
  Les autorités des tests restent synthétiques, **G0 NON VALIDÉ**.

## VALIDATION

| Test / build | Résultat confirmé |
| --- | --- |
| `MZP5ProtocolHarness -c Release` | **133/133 PASS** hors réseau |
| `MZP5RecoveryPreparationHarness -c Release` | **16/16 PASS** IO/ACL Windows |
| `MZP5RecoveryHealthHarness -c Release` | **12/12 PASS** sur vrai journal |
| `MZP4AccountsUiHarness -c Release` | **25/25 PASS** Avalonia Headless + journal |
| `MZP5Provenance/parity_audit.py --verify` | **14/14 preuves lexicales** (pas runtime) |
| Givre `MZP5P0Harness -c Release` | **42/42 PASS** offline |
| Givre `MZP3ReplayHarness -c Release` | **38 PASS** offline |
| Givre `GivreRework.App -c Debug -p:EnableAvaloniaPro=true` | **0 erreur, 0 avertissement** |

L'ancien `MZP3ReplicaWorkbench.exe` publié est toujours présent sous
`tools/MZP3ReplicaWorkbench/bin/Release/net10.0-windows/win-x64/publish/` ;
**il n'intègre pas automatiquement les nouveaux écrans de l'App principale**.
Le nouveau code MZP5 compile via les vrais harness et est inclus par glob
dans les projets existants, mais cela **ne constitue pas** un exécutable
GivreClone complet.

## BLOCKERS ET PRIORITÉ

1. `GivreCore.csproj` et `GivreLauncher.Core.csproj` toujours absents,
   nécessaires à la vraie App et au moteur MITM. Ne jamais utiliser les
   stubs du harness comme substituts de production.
2. Binaire v1.3.1 installé hors périmètre, aucune copie détachée autorisée
   SHA-identique pour décompiler : **pas de C# réellement décompilé de 1.3.1**.
   L'option NativeAOT du csproj existant ne prouve pas la méthode de build
   du binaire original.
3. MZP5 inbound exige un vrai producteur, framing et provenance de version;
   labels/SHA déclarés ne sont ni authentification ni serveur ACK.
4. Givre G0 et G1-G8 : pas de corpus legacy exact authentifié, décodeur
   3.7.10.10 indépendant, login/cartes/combat attestés.
5. Avalonia Pro compile, mais aucun attachement du **MCP Avalonia DevTools**
   à une UI live n'a été certifié dans cette passe; l'interface de test a
   été validée avec Avalonia Headless et le MCP Chat On Steroids Core.

**Prochaine priorité substantielle :** identifier de véritables sources
autorisées/ports fournisseur Protobuf+MITM+Launcher, rétablir une compilation
de `GivreClone.App` sans supprimer ses fonctions, puis valider un producteur
unique et une réception/ACK sur instance de TEST indépendante. Pour Givre,
acquérir un corpus privé réellement authentifié et deuxième décodeur exact
afin de pouvoir commencer une vérification G0 honnête.
