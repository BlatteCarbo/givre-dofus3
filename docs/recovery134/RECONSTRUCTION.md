# Reconstruction dans GivreWork — comparaison avec la récupération 1.3.4

La comparaison a identifié quatre types de ViewModel absents des sources actuelles. Ils sont maintenant implémentés et reliés à l'application : recherche rapide, vue d'ensemble, chasse au trésor et préparation locale de forgemagie. Il s'agit de nouveau C# fondé sur les surfaces récupérées et sur les services existants, pas d'une conversion certaine des corps natifs en source original.

## Écarts traités

| Surface récupérée | Avant | Comportement ajouté |
|---|---|---|
| `QuickSearchViewModel`, `QuickSearchItem` — RVA `0x22ba49c` à `0x22c5ff5` | Aucun type correspondant ni recherche globale de navigation retrouvé. | Bouton de recherche dans la barre de titre, filtre local pages/bots insensible à la casse et aux accents, sélection clavier, ouverture du résultat et fermeture. Les identités conservent les classes de masquage du mode stream. Aucun contenu de requête n'est journalisé. |
| `OverviewViewModel` — RVA `0x22d51d1` à `0x22d52e7` | Liste des bots disponible, pas de page d'ensemble correspondante. | Nouvelle entrée « Vue d'ensemble », cartes issues de la collection existante, sélection via le chemin existant `PickBot`, état vide. Accessible aussi par la recherche, y compris avec le skin classique. |
| `TreasureViewModel`, `TreasureStepRow` — RVA `0x22d1f46` à `0x22d50a4` | `LaunchService.Treasure` et des commandes Lua existaient ; pas d'onglet dédié. | Onglet Trésor dans Bots : état et étapes observés, essais, checkpoint, drapeau/retrait/creuser/abandon manuels. Une injection acceptée reste une demande en attente ; seuls des événements plus récents du même contexte terminent cette attente. Les codes serveur restent bruts. |
| `SmithMagicViewModel` et lignes — RVA incluant `0x22c6f8a` à `0x22cb82d` | Aucun module de forgemagie correspondant retrouvé. | Onglet de préparation : objets réellement lus du sac, file sans doublon d'UID, objectifs manuels, jet parfait local. Le planificateur pur gère les seuils de rune fournis par l'appelant, stocks et budgets sans débordement. Aucun achat/application de rune : le transport manque. |

WebAuth a été comparé séparément : ses sept fichiers fonctionnels sont déjà présents, identiques après normalisation des espaces de noms, sauf libellés et noms de répertoires. Aucun deuxième composant d'authentification n'a été ajouté. Voir `../codex/WEBAUTH_RECOVERY_COMPARISON_20261023.md`.

## Éléments réutilisés et intégration

- Les états, forges et permissions existants de `LaunchService.Treasure` sont réutilisés. Les sélections et rafraîchissements passent par `BotsViewModel`, pas par un autre transport.
- La recherche ouvre les commandes de navigation existantes et identifie les bots par leur compte local ; elle ne lance pas de compte ou de script.
- La vue d'ensemble partage la même collection observable que la page Bots. Pas de copie de sessions ou de faux indicateurs de fonctionnement.
- La préparation de forgemagie est remise à zéro au changement de PID. Les objectifs et la file restent en mémoire ; les effets/runes d'un objet ne sont pas inventés depuis son nom. Les objectifs manuels ont un identifiant d'effet inconnu et ne permettent donc pas une recommandation de rune live.
- Les nouvelles vues sont compilées par le projet App existant. La recherche est à l'intérieur du shell, sous les overlays de connexion, de splash et de contrôle existants.
- Les modifications déjà présentes au début de la passe (combat, journaux, langues et tests associés) ne sont pas incluses dans cette reconstruction.

## Vérification reproductible

Depuis la racine de GivreWork :

```powershell
dotnet run --project tools/RecoveredNavigationHarness/RecoveredNavigationHarness.csproj
dotnet run --project tools/RecoveredSmithMagicHarness/RecoveredSmithMagicHarness.csproj
dotnet run --project tools/TreasureSmoke/TreasureSmoke.csproj
dotnet run --project tools/RecoveredUiHarness/RecoveredUiHarness.csproj -c Debug -m:1
```

La dernière commande compile l'application réelle et ses références, puis instancie les nouvelles vues Avalonia dans un hôte headless. Les scénarios utilisent des données fictives ; aucun client de jeu, compte ou login n'est lancé. Les tests de trésor emploient une fixture de service hors ligne pour les reçus et les changements de session.

Résultats de cette passe : recherche 13 contrôles ; planificateur de forgemagie 14 contrôles ; scénario trésor réussi ; contrat WebAuth 18 contrôles. Le résultat du banc UI et le nombre de contrôles sont consignés dans `verification.json`. Des avertissements préexistants restent présents, notamment `TextBox.Watermark` obsolète et `CA2022` dans `GivreControl`.

## Ce qui reste inconnu ou manquant

- Résolution/navigation automatique des indices de chasse, plans légendaires et achat de fragments : non reconstruits.
- Forge live, poids/probabilités des runes, effets observés par objet, achats et recettes d'atelier : non récupérés. Le bouton de démarrage de forgemagie reste indisponible.
- La correspondance native des signatures, structures de données et appels indirects reste incertaine. Les 550 échecs et les 30 093 fonctions avec avertissements du cœur ne deviennent pas des méthodes C# connues.
- La présence d'un nom de binding n'établit ni le type exact de sa propriété ni le comportement de sa commande. `binding-comparison.json` conserve les références et les candidats sans type source correspondant. Les noms de lignes de quêtes, échanges, équipement, craft et autres interfaces ne suffisent pas à reconstituer leurs algorithmes.
- Les textes des nouveaux panneaux sont en français ; ils ne complètent pas les dictionnaires de langues existants modifiés ailleurs.
- Aucun fonctionnement gameplay ni parité exhaustive avec MizanBot n'est certifié par ces tests hors ligne.

## Sources et traçabilité

`scripts/Compare-Recovered134.py` régénère la comparaison compacte depuis le fichier de chaînes de l'image mémoire. Cette passe compare 99 surfaces de binding aux types C# sous App/Engine/Protocol ; une absence de nom est un candidat, pas une preuve d'absence d'un comportement équivalent.

Les fichiers bruts et les exports natifs restent dans `%USERPROFILE%\Downloads\GivreClone`. Le cœur a terminé l'export de 201 388 fonctions découvertes : 200 838 sorties de pseudocode et 550 échecs. Ce décompte ne signifie pas autant de fonctions correctement reconstruites. L'empreinte de la capture est conservée dans `binding-comparison.json` ; `analysis/recovery-status.json` dans la récupération contient les détails de couverture.
