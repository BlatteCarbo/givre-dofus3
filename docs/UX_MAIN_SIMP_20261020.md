# Navigation principale simplifiée — 20/10/2026

## Modifications

Sur `MainWindow.axaml` et `MainViewModel.QuickNavigation.cs`, la barre de titre conserve une recherche unique « Ctrl+K », les notifications et les contrôles de fenêtre. Les anciens raccourcis répétés Dashboard / Protocole / Mods / Atlas / Métiers / Fabrication / Diagnostics / Discord ont été regroupés dans **Outils**, menu flottant avec deux groupes : « Planification » et « Connexions et diagnostic ».

Dans la sidebar, les pages d'usage régulier (Tableau de bord, Comptes, Bots, Équipes) restent en accès direct. Proxys, Adaptateurs, Diagnostics, Mods visuels, Atlas, Coach métiers et Atelier objets sont désormais sous le groupe repliable **Outils**. Paramètres et Mon compte restent au bas de la navigation. Aucune commande ni fonctionnalité n'est supprimée : les entrées existantes restent câblées aux mêmes commandes. Les boutons nommés utilisés par MainWindow.axaml.cs pour les diagnostics, les animations et l'indicateur de navigation sont conservés. La recherche Ctrl+K retrouve les pages indépendamment du groupe replié.

Le bouton de titre Outils fonctionne également avec le skin alternatif « Classic », sans avoir à modifier le menu de ce skin. Les actions consultatives dans son menu passent par les mêmes clés de navigation que la palette rapide, et ferment le menu après sélection. Les modules d'audit / Discord conservent leur commande d'origine.

Le contenu de la sidebar dispose de son propre défilement vertical : ouvrir les outils n'éloigne pas les boutons « Paramètres » et « Mon compte », conservés dans le pied.

## Contraintes vérifiées

Sections de pages, modales, bannière de version/protocole, contenu Classic, contrôles de fenêtre, événements souris, et logique gameplay de l'application non modifiés. Les noms `MZP0TitleDiagnosticsBtn`, `MZP0NavDiagnosticsBtn` et `NavProxysBtn`/ `NavAdaptersBtn`/autres boutons de navigation sont conservés pour les références depuis le code-behind existant. Aucun nouveau service ni référence de projet.

## Recette UI à confirmer par prime

Tester le menu Outils avec souris et clavier (ouvrir / fermer / naviguer), la palette Ctrl+K et Esc, le mode Classic, puis les vues Proxys/Adaptateurs lorsque la sidebar est dépliée, et le panneau Diagnostics. Un bouton repliable ne ferme pas une page : l'état de la page ne change qu'à l'activation de son lien.

Build ciblé Release App réussi avec **0 erreur**, 197 avertissements du dépôt. Un attribut ZIndex superflu a été retiré pendant la validation. La modification finale du ScrollViewer n'affecte aucun contrat de ViewModel ou code-behind ; le build solution du prime doit confirmer le montage définitif.
