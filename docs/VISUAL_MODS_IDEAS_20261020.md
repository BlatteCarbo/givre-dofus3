# Modules visuels locaux — 20 octobre 2026

## Livrable exploitable

`GivreClone.App.Views.VisualModsPanel` est un vrai `UserControl` Avalonia 12.1.1 autonome (DataContext possédé par le panneau), conçu pour être monté dans un écran/overlay existant. `new VisualModsPanel()` fonctionne sans injection du `MainViewModel`. Surcharge `new VisualModsPanel(new VisualModsViewModel(temporaryFilePath))` pour tests et aperçus isolés. Propriétés publiques `ViewModel` et `StoragePath`. Aucun appel réseau ou au processus Dofus.

Fonctions : saisie de **positions X,Y**, libellés et notes ; favoris ; suppression ; choix du point de départ pour le planificateur ; mini-carte 2D **schématique** des seuls points saisis (échelle relative et axes approximatifs) ; transcription d'un indice exact et saisie manuelle de son point cible ; une case confirme que l'utilisateur a **personnellement vérifié la transcription et ses coordonnées** ; calcul de `ΔX`, `ΔY`, direction et distance de Manhattan `|ΔX|+|ΔY|`. Cette distance mesure les écarts d'une grille théorique, jamais un chemin, une accessibilité de cellule, ni une solution de chasse.

L'état de session est explicitement `UNKNOWN`. Ni position du personnage, ni état d'une chasse, ni ressources/métiers ne sont inférés. Le panneau ne communique avec aucun client du jeu et ne déclenche aucun déplacement. Les idées proviennent uniquement des rubriques décrites dans le README public de `Altansar69/dofus-miniatures` r80 (3.7.12.12) ; le code, les images, les assets, les coordonnées et les bases de données de ce dépôt n'ont **pas été importés** (licence non établie).

## Montage

```xml
<views:VisualModsPanel />
```

`xmlns:views="using:GivreClone.App.Views"` est le namespace de la vue. Le panneau prend en charge son `DataContext` dans son constructeur, ce qui évite une dépendance cachée au ViewModel du shell. Prévoir une largeur agréable d'environ 1 050 px dans un overlay scrollable ; les contenus sont scrollables verticalement. Il ne demande aucune modification de `.csproj` (items SDK inclus par convention).

## Stockage et garanties

- Chemin local par défaut : `%LOCALAPPDATA%/GivreClone/visual-mods/carnet-v1.json`, format JSON `Version=1`, sauvegarde précédente `.bak`, écriture atomique avec le `AtomicJson` existant.
- Limites : 250 points, entiers X/Y inclus dans `[-10000,10000]`, libellé 1–60 caractères, note 0–800 caractères, fichier 512 Kio. Tout texte est interprété comme du texte, jamais du code/markup.
- Données manquantes : carnet vide et éditable. JSON corrompu, incomplet, schéma différent, trop gros, doublons/valeurs incorrectes, accès refusé ou version future : **lecture seule et fichier conservé tel quel**, sans écrasement par une sauvegarde vide. Même un appel direct de `Save` vérifie le document actuellement sur disque avant remplacement. Les actions écrivent sur disque **avant** de modifier la collection affichée.
- Garde sur lien symbolique/jonction : vérification des dossiers parents, de la cible, du `.tmp` et du `.bak` avant lecture/écriture ; aucun suivi volontaire de redirection de chemin. La validation de chemins ne constitue pas une garantie transactionnelle face à un adversaire modifiant la structure du disque exactement entre vérification et écriture.
- Les coordonnées sont déclaratives : la mini-carte est uniquement un nuage de repères mis à l'échelle. Aucun identifiant de map, historique d'XP ou indice de jeu vérifié ne se trouve dans le stockage.

## Validation

Commande ciblée : `dotnet test GivreClone.Tests/GivreClone.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~VisualMods`.

Tests déterministes sur fichiers temporaires : sauvegarde/recharge/backup, création et validation d'entrées, rejet d'ancienne/future version mal formée, plafond de taille, préservation du fichier illisible, refus des liens lorsque la plateforme en permet la création, calcul conditionné à la case « indice vérifié », distance de Manhattan et positions projetées dans les limites de la mini-carte. Tous les tests fonctionnent sans jeu, réseau ni données Dofus.

## Autres idées sous réserve de données vérifiées

Le README r80 évoque carnet messages, points de ressources, progression des métiers et croisements de dragodindes. Ce panneau livre volontairement les notes/repères manuels ; les autres vues pourraient afficher plus tard des observations **explicitement sourcées et horodatées**, avec statut `UNKNOWN` quand la source manque. Aucun niveau de métier, emplacement de ressource ou recette d'élevage n'est présumé aujourd'hui.
