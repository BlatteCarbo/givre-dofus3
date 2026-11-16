# GivreWork — critères de recette après comparatif concurrentiel

Statut : **revue hors jeu**. Le bot, les comptes et le jeu n'ont pas été
pilotés par ce lot. Aucune preuve d'ACK réel n'est déduite des tests de source,
des tests purs ou d'une compilation .NET réussie.

## Régressions à verrouiller

| Scénario | Attendu | Test disponible / reste à faire |
|---|---|---|
| PID Windows réutilisé après arrêt | Plus d'inventaire, coffre, kamas, effets d'objet ni dialogue de téléport de l'ancienne session | InventorySessionLifecycleTests + revue Forget(); **relecture UI live à faire** |
| Même PID, nouveau personnage | Les informations du précédent personnage deviennent inconnues jusqu'au prochain snapshot, pas réattribuées au nouveau | Source + tests; **cas client test à exécuter** |
| Objet supprimé / quantité zéro | Cache d'effets réévalué; ne conserve pas les jets d'un UID absent du sac ET du coffre | Code prune + test de source; **relecture jeu à faire** |
| Coffre à code de maison | Ses données Storage* ne contaminent pas le cache bag/banque | Garde source + test de source; **test live à faire** |
| Dialogue Anomalie type 4 | Destinations consultables mais aucun ordre de téléportation non certifié; tout chemin Lua ou direct refuse | TeleportActionPolicyTests; **capture wire et ACK requis avant activation** |
| Dialogue zaap périmé / destination absente | Pas de téléportation envoyée si carte, type de liste ou cible ne correspond plus | TeleportActionPolicyTests; **capture validante requise** |
| Repricing vendeur incertain | Ni faux OK ni nouvelle mise en vente sans rapprochement manuel; annonce et sac examinés | HdvRepriceCorrelationTests + affichage revue; **pas de test HDV réel** |
| Commande MCP avec alias ambigu/compte protégé | Refus, aucune action déclenchée; sorties bornées, secrets omis | McpControlSecurityTests et smoke; **tester un vrai compte test uniquement** |
| Discord en erreur réseau/HTTP 429 | Pas de notification marquée livrée, pas de secret dans les logs; prochaine tentative disciplinée | AnomalyAlertReliabilityHarness; **aucun webhook réel** |
| UI clavier, lecteur d'écran | Contrôles iconiques nommés, focus visible et sans dépendance à la souris | Compilation XAML et revue DOM/Avalonia hors jeu; **test NVDA/contraste manuel à faire** |
| Assistant sans code | Génère uniquement le DSL de trajet Lua après validation des champs; avant remplacement exige prévisualisation et confirmation; ne lance rien | NoCodeLuaGeneratorTests, build Avalonia; **cartes/ressources réelles non validées** |
| Catalogue Lua | Manifestes/versions/capacités contrôlés, vérification SHA-256 et chemins, scripts inconnus non exécutés | ScriptCatalogSecurityTests; **aucun branchement exécution/marketplace** |
| Jalons de quêtes | Les validations ne proviennent que d'événements serveur associés à une session/identité; un paquet sortant n'est pas un ACK | QuestProgressionTrackerTests + QuestCriteriaParserSecurityTests; **adaptateur runtime/UI non branché** |
| Détection MAJ Dofus au démarrage | Lire le vrai marqueur multi-ligne, les 4 SHA et le build bundle ; comparer uniquement les extractions natives vérifiées ; pas de mise à jour auto de codec | ProtocolUpdateProbeTests + ProtocolUpdateIndexerTests ; paire locale 3.7.10.10 → 3.7.11.11 : fichiers proto SHA identiques ; **ACK/routage non prouvés** |
| Changement binaire avec version inchangée | Avertir si les empreintes ne correspondent plus au dernier démarrage, conserver le repère antérieur et ne jamais l'utiliser comme preuve de compatibilité | ProtocolObservedDriftTests ; 1er lancement sans baseline de confiance, pas d'affirmation de protocole validé |
| Commande d'indexeur | Le plan hors ligne est séparé de l'extraction explicite, sortie bornée et cache isolé ; aucune promotion ni lancement du jeu | Test CLI réel du plan 3.7.11.11 (review_only, status planned) ; **extraction avec --execute et téléchargements non testés** |
| Portage GivreActive : fallback de sorts | Ne jamais cibler une cellule occupée par ennemi avec invocation/buff/soin, ni contourner le cooldown / conditions de profil dans LastChanceAttack | CombatParityFallbackTests + CombatParityLastChanceTests ; **efficacité gameplay non mesurée** |
| Import de profils combat | Des changements de cible/conditions deviennent visibles avant application, pas seulement priorité et cooldown | SpellParityDescriptionTests ; **import en UI réelle non validé** |

## Restrictions et données inconnues

- **Transaction** : envoi local ≠ réponse serveur ≠ effet confirmé.
- **Prix historiques HDV** : la réponse courante n'atteste pas le GID source ;
  la vue présente explicitement cette limite et ne déclenche aucune requête
  automatique.
- **Mode marchand joueur, monture, invocation contrôlée, anomalie** :
  pas de commande nouvelle sans identifiant de message et séquence ACK vérifiés.
- **Comparaison panoplies** : les presets possèdent GID/emplacement, pas les
  jets ; gains de caractéristiques et prix sont inconnus.
- **Planification d'expédition** : le coffre bancaire ne devient pas
  automatiquement une source de stock fiable si le joueur ne l'a pas consulté.

## Commandes de contrôle reproductibles

Ouvrir PowerShell dans la copie d'intégration :

    dotnet build GivreClone.slnx -c Release -m:1 --no-restore
    dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release -m:1 --no-restore
    dotnet test GivreClone.PureTests/GivreClone.PureTests.csproj -c Release --no-restore
    ./GivreMcp/Smoke.ps1
    dotnet run --project AnomalyAlertReliabilityHarness -c Release --no-restore
    dotnet run --project tools/R6IntegrationUiHarness/R6IntegrationUiHarness.csproj -c Release --no-restore

Les manques recensés, les sources éditeurs et la priorisation produit sont dans
docs/COMPETITOR_GAPS_20261020.md.

### Benchmark Lua

La transformation d'un script de 10 000 lignes reste testée dans la suite
principale (diagnostics, plis et idempotence du formatage). Le seuil de temps
mur est variable pendant les compilations concurrentes ; il est testé dans
tools/Benchmark-LuaEditor.ps1 sur une machine libre, en Release, sur trois
exécutions, avec budget strict de 300 ms. Ce critère n'est PAS certifié
par le seul succès des tests fonctionnels.
