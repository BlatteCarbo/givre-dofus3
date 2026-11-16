# MZP24 — Import local de proxys : aperçu masqué, validation et corrections

16 octobre 2026 — worker-3. Produit : clone source-owned `GivreClone.App`.

## R — fonctionnalité

L'onglet **Proxys** du clone dispose désormais d'un assistant d'import en masse visible dans `Views/ProxysView.axaml`. Cliquer **Ouvrir l'import**, coller des proxys (une ligne par entrée), choisir `SOCKS5` ou `HTTP` et lire l'aperçu. Il indique chaque **numéro de ligne**, les entrées reconnues, les doublons et les erreurs corrigeables. Les noms d'utilisateur et mots de passe n'apparaissent jamais dans la liste des résultats, bien que le texte initial reste visible dans le champ de saisie jusqu'à l'annulation ou à l'import.

Les formats restent ceux de `ProxyImportParser` (MZP23) : `hôte:port`, `hôte:port:user:pass`, `user:pass@hôte:port`, `socks5://...`, `http://...`. L'aperçu canonise hôte DNS, adresse IPv4 et port ; refuse les chemins dans l'hôte, les caractères de contrôle, les ports hors plage, `https://` ambigu pour le relais HTTP CONNECT en clair et les entrées excessives. Les doublons de la même paire protocole/hôte/port/utilisateur sont rapportés avec le numéro de la première ligne. Les lignes de commentaires commençant par `#` ou `--` et les lignes vides sont ignorées.

La commande **Importer les lignes validées** reste désactivée tant qu'une ligne est refusée ; chaque correction est réévaluée. Si aucun refus ne subsiste, elle emploie **le chemin historique source-owned** `ProxysViewModel.ImportProxies -> Stores.Proxies.AddExternal`, qui chiffre les mots de passe au repos avec DPAPI. Le nouvel aperçu et son utilitaire console n'effectuent aucun appel réseau, accès à un compte, client Dofus ou session Givre.

## C — fichiers et limites de propriété

- `GivreClone.App/Services/MZP24ProxyImportReview.cs` — nouveau moteur pur et borné, diagnostic sans secrets. API : `MZP24ProxyImportReview.Inspect(text, protocol)`, résultat `Summary`, `Rows`, `Accepted`, `Duplicates`, `Rejected`.
- `tools/MZP24ProxyImportReview/{MZP24ProxyImportReview.csproj,Program.cs}` — nouveau compagnon offline pour voir et tester une revue depuis le terminal. Ne lit que l'entrée standard redirigée, sans accéder aux fichiers du clone/original.
- `GivreClone.App/ViewModels/ProxysViewModel.Import.cs` — intégration de la revue et interdiction des imports partiels contenant des lignes refusées. Autorisation explicitement étendue par PRIME pendant MZP24.
- `GivreClone.App/Views/ProxysView.axaml` — groupe d'import et visualisation des erreurs/aperçus masqués ; autorisation explicitement étendue par PRIME pendant MZP24.
- Ce document nouveau.

Le champ de texte contient temporairement les données collées par l'opérateur ; n'y coller que des proxys destinés au clone. Le champ est vidé sur **Annuler** ou après import. L'aperçu ne montre jamais les identifiants. L'option historique `ImportTest` reste désactivée par défaut et n'est pas exposée dans ce nouvel assistant : l'import local ne déclenche donc aucune validation réseau de proxy.

## V — validations ciblées

Depuis `%USERPROFILE%\Videos\GIVRE\GivreShareWork` :

```powershell
dotnet build tools/MZP24ProxyImportReview/MZP24ProxyImportReview.csproj -c Release --no-restore -m:1 -v:q
dotnet tools/MZP24ProxyImportReview/bin/Release/net10.0/MZP24ProxyImportReview.dll --self-test
@('socks5://utilisateur:secret@host.example:1080','https://host.example:443') |
  dotnet tools/MZP24ProxyImportReview/bin/Release/net10.0/MZP24ProxyImportReview.dll
```

**Résultat vérifié sur le poste :** compilation de l'outil en Release avec `0 avertissement / 0 erreur`, puis `PASS MZP24ProxyImportReview 20 checks`. Le scénario d'entrée mélangée a affiché `2 accepté(s), 0 doublon(s), 2 rejeté(s)`, avec codes de ligne et raisons, et a volontairement retourné `1`. Aucun identifiant ni mot de passe de l'exemple n'a été affiché. `--self-test` exerce le parseur réel et la revue (lignes/numérotation, cas HTTP, canonisation, refus HTTPS, doublons, hôtes invalides, secrets masqués et limites de taille). Le programme ne persiste rien.

Pour compiler l'UI, utiliser la cible du projet :

```powershell
dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore -m:1 -v:q
```

Le projet source a `TargetFramework=net10.0`. Un `dotnet build -r win-x64 --no-restore` exige au préalable une restauration avec ce RID ; sans cette étape il peut échouer avec `NETSDK1047` sans que cela implique une erreur des sources MZP24.

**Compilation de l'application :** la commande cible `net10.0` sans RID a compilé le XAML, le nouveau service et les ViewModels avec `0 erreur` ; l'ensemble du projet signalait `165 avertissements` de nullabilité, d'API Windows ou de propriétés `Watermark` obsolètes, principalement hors MZP24. Une compilation initiale `-r win-x64 --no-restore` a échoué en amont avec `NETSDK1047` (restauration pour RID absente) ; il ne s'agit pas d'un diagnostic produit. Aucun client, fenêtre, processus de jeu ou réseau n'a été ouvert par ces validations.

**Recompilation finale après intégration XAML :** `dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore -m:1 -v:q -p:WarningLevel=0 -p:OutputPath=%USERPROFILE%\Videos\GIVRE\GivreShareWork\artifacts\MZP24ProxyPreviewBuild\` a terminé avec `0 erreur / 35 avertissements Avalonia` (utilisations existantes de `Watermark`, aucune sur le champ nouvellement ajouté). Le diff des deux fichiers partagés a passé `git diff --check`.

## B — limitations et séparation

Cette livraison concerne la préparation locale de la liste de proxys, pas la connexion à Ankama ni la certification du MITM. Elle n'ajoute pas d'API de transport, d'injection, de compte ni de ticket et ne modifie pas GivreClone original. L'import utilise encore `ProxyStore` : les proxys deviennent visibles dans le clone local après rechargement de la liste. Aucun test en jeu/connexion, aucun test de proxy public et aucune parité v1.3.1 complète ne sont revendiqués.
