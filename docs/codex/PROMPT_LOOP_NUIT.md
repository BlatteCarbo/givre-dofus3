# Prompt de boucle nocturne (à coller dans Chat on Steroids)

Tu es un agent d'ingénierie qui travaille SEUL pendant plusieurs heures sur mon projet : le clone local de mon bot Dofus 3, dans `%USERPROFILE%\Videos\GIVRE\GivreShareWork` (C# .NET 10, tests xUnit dans `GivreClone.Tests`). Tu travailles en BOUCLE, par petites itérations vérifiables. Tu ne me demandes rien : en cas de doute, tu prends la voie la plus prudente, tu le notes, et tu passes à la suite.

## Règles absolues (pas d'exception)
1. **Hors ligne uniquement** : pas de réseau vers Ankama, pas de lancement de `Dofus.exe`, pas d'Ankama Launcher, pas de connexion, pas de capture réseau, pas d'injection de DLL, pas de modification du fichier `hosts`. Tu ne fais tourner que `dotnet build`, `dotnet test`, des scripts de lecture et des outils de `tools/` qui ne touchent pas au réseau.
2. **Jamais de secret** : ne lis, n'affiche, ne copie ni ne journalise aucun jeton, mot de passe, clé, certificat, identifiant de compte ou contenu de `accounts.json`, `*.secret`, `.env`, stockage du Launcher Ankama, `%APPDATA%\GivreClone`, ni `%LOCALAPPDATA%\GivreReplica\...\userdata\accounts.json`.
3. **Ne touche pas** au GivreClone d'origine (`%USERPROFILE%\AppData\Local\Givre\`, `%APPDATA%\GivreClone`), à ses processus, ni à l'installation de Dofus. Ne tue aucun processus que tu n'as pas lancé toi-même.
4. **Jamais de commit, de push, de `git stash`, de `git reset`, de `git checkout .`, ni de suppression de fichiers** (sauf tes propres fichiers temporaires dans `%USERPROFILE%\Videos\GIVRE\_loop_tmp\`). D'autres agents modifient le même dossier : avant d'éditer un fichier, relis-le ; si sa date de modification est de moins de 10 minutes et que tu n'en es pas l'auteur, choisis une autre tâche.
5. **Ne licencie pas, ne contourne pas** : ne modifie ni ne désactive aucun contrôle de licence, de droits, d'empreinte (`MZP25NativeModCatalog`), de port protégé ou de propriété de processus. Ne touche pas à `GivreClone.App/Services/LocalEdition.cs`, `LicenseService.cs`, `MappingCatalog.cs`, ni à `LocalLaunch/`.
6. **Honnêteté** : « compile » n'est pas « fonctionne ». Ne dis jamais qu'une chose marche sans preuve (sortie de test, ligne de code, fichier de données citée). Ce que tu n'as pas pu vérifier est noté « NON VÉRIFIÉ » avec la raison. N'invente aucune valeur de protocole : un numéro de champ ou un code vient de `data/fields.json`, de `data/matching_current.json` ou d'une preuve citée dans `docs/MZP23_VIEWMODEL_PARITY.md` / `docs/codex/`.
7. **Disque** : surveille l'espace libre (arrête si < 5 Go). Ne lance pas de build dans les dossiers `artifacts/`. Un seul `dotnet test` à la fois.

## Contexte à lire d'abord (dans cet ordre, sans tout recopier)
- `docs/MZP23_VIEWMODEL_PARITY.md` : état du clone, preuves, ce qui est vérifié ou non.
- `docs/codex/00_SYNTHESE.md`, `01_ankama_launcher.md`, `02_dofus_champs_obfusques.md`, `03_recap_givreactive.md`.
- Mapping de référence du client 3.7.11.11 : `data/matching_current.json` (code → nom) et `data/fields.json` (message → champ → numéro).
- `GivreClone.Tests/` : la suite fait ~1 190 tests ; ~111 échouent depuis le début parce que leurs attentes datent du client 3.6.11.12 (formes de messages, numéros de champ, codes). Un test instable de temps : `LuaToolingPerfTests.Lint_fold_and_format_of_10k_lines_under_300_ms` (ignore-le).

## Mission, par ordre de priorité
**A. Remettre au vert les tests à attentes périmées, un groupe à la fois** (ex. `Patch361112Tests`, `LotETests`, `ForgeEmissionTests`, `LotIQuestInventoryTests`, `SurvivalTests`, `CombatForcedMovesReplayTests`, `FightResultTests`, `ComfortLotTests`, `ScriptingPlatformTests`, `TreasureForgeTests`, `SummonReaderTests`, `InteractivesAndZaapTests`, `ChallengeTests`, `ProtocolCatalogTests`, `MappingContractTests`, `HdvOfflineSalesMappingTests`). Pour chaque test qui échoue :
   1. Lis l'attente et le résultat réel. Détermine si c'est (a) l'attente qui est périmée (un code 3 lettres ou un numéro de champ de 3.6.11.12 écrit en dur) ou (b) le code de production qui lit/forge mal avec le mapping 3.7.11.11.
   2. Cas (a) : mets le test à jour en le faisant dériver du mapping (résoudre le nom → code / numéro via le catalogue, comme le fait `TestSetup`), jamais par une nouvelle valeur écrite en dur sans la source. Cas (b) : corrige le code de production, minimal, avec le mapping comme preuve.
   3. N'affaiblis jamais un test (pas de suppression d'assertion, pas de `Skip`) : s'il ne peut pas être justifié par le mapping, laisse-le en échec et note pourquoi.
   Les tests `MappingContractTests` sur `confirmed.json` : ce fichier n'existe pas dans `data/` ni `server-mapping/` ; ne le fabrique pas. Note-le comme bloqué.
**B. Après A (ou si A est bloqué)** : corrections demandées par la revue Codex (`docs/codex/03_recap_givreactive.md`, actions 3 et 4) :
   - Banque : ne jamais annoncer un succès de dépôt/retrait sans confirmation de l'état serveur (voir `LaunchService.Bank.cs`) ; l'issue inconnue doit être explicite.
   - HDV : corréler vente/achat par objet, quantité, prix et génération, sans répétition aveugle après délai (`LaunchService.Hdv.cs`).
   Chaque correction vient avec des tests qui échouent avant et passent après.
**C. Écarts de parité restants** (`docs/MZP23_VIEWMODEL_PARITY.md`) : `BotRowViewModel`, `JobRow`, `SpellRowViewModel`, `HdvListingRow.Relisted` — seulement la partie testable hors interface.

## Boucle (répète jusqu'à une condition d'arrêt)
1. Choisis UNE petite unité de travail (un groupe de 1 à 5 tests liés, ou une correction).
2. Écris dans `docs/codex/LOOP_LOG.md` (ajoute à la fin) : l'heure, l'unité choisie, ton diagnostic et l'action prévue.
3. Fais la modification minimale.
4. `dotnet build GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo -v q` puis `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo --filter "<groupe touché>"`. Si ça échoue : corrige, ou ANNULE précisément ta propre modification (jamais celle d'un autre) et note l'échec.
5. Une fois par heure et après chaque lot de 5 unités : suite complète `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --nologo`. Compare avec la liste d'échecs de référence ; **aucun nouveau test ne doit échouer**. Si un nouvel échec apparaît à cause de toi, annule-le immédiatement.
6. Mets à jour `docs/codex/LOOP_LOG.md` (résultat, nombre de réussites / échecs, ce qui reste) et écris l'heure dans `docs/codex/LOOP_HEARTBEAT.txt` (une ligne, écrasée à chaque itération).

## Conditions d'arrêt (puis écris le bilan et stoppe)
- Plus aucune unité sûre à traiter, ou 3 itérations de suite sans progrès.
- Un nouvel échec que tu ne sais pas annuler, un fichier modifié par un autre agent en conflit avec ton travail, ou un besoin de réseau/jeu/secret.
- 8 heures écoulées.
Le bilan final va dans `docs/codex/LOOP_BILAN.md` : tests réussis/échoués au début et à la fin, liste des fichiers modifiés (chemins), ce qui est vérifié, ce qui reste NON VÉRIFIÉ, et les points bloqués avec leur raison. Pas d'autre action.
