# World Atlas UI — 20 octobre 2026

## Livrable

Panneau réel Avalonia 12 / .NET 10 : WorldAtlasPanel.axaml + .cs, namespace GivreClone.App.Views. Constructeur sans paramètre new WorldAtlasPanel(), avec son propre DataContext WorldAtlasViewModel. À monter dans MainWindow avec la déclaration XML namespace views: using:GivreClone.App.Views.

Fonctions : filtres Surface, Mines, Grottes, Zaaps, Inconnue ; recherche cumulative mapId/X/Y/zone ; résultats bornés à 140 ; inspecteur de carte ; source/version/SHA-256/référence de preuve ; transitions dirigées et inspecteur de destination ; départ/arrivée manuels et vérification d'une route ; fiches manuelles temporaires marquées UNVERIFIED. Aucune carte raster, géométrie, entrée de grotte, sortie ou connexion n'est inventée.

## API minimale à brancher au moteur worker-1

- Construction : var panel = new WorldAtlasPanel();
- Injection directe : bool accepted = panel.LoadSnapshot(snapshot);
- Injection différée : panel.SnapshotProvider = () => ConvertVerifiedLocalAtlas(source); le bouton « Actualiser la source locale » appelle ce fournisseur uniquement.
- Le DTO WorldAtlasSnapshot reçoit (Title, Evidence, Maps, Transitions).
- WorldAtlasMapRecord reçoit (MapId, X?, Y?, Zone, Layer, Label, Evidence, IsManual=false).
- WorldAtlasTransitionRecord reçoit (FromMapId, ToMapId, Label, Evidence, Bidirectional=false).
- WorldAtlasEvidence reçoit (State, Source, GameVersion, Sha256, EvidenceReference).
- WorldAtlasLayer propose Unknown, Surface, Mine, Cave, Zaap ; WorldAtlasEvidenceState propose Unverified, Verified.

Le worker-1 crée des modèles AtlasMap/AtlasTransition/AtlasSnapshot propres à son lecteur. Prime doit fournir un petit adaptateur de conversion, sans changer les métadonnées de preuve. L'interface exige mapId positif et unique, coordonnées X/Y présentes ensemble ou absentes, limites ±10000, au plus 30000 cartes / 100000 transitions. Les preuves complètes comportent source, version du jeu, SHA-256 sur 64 caractères hexadécimaux et référence de preuve. Une étiquette Verified seule ne suffit pas : la validation des empreintes et de l'origine appartient au lecteur local de worker-1, l'interface vérifie uniquement la structure de la preuve.

La source locale est branchée par un fournisseur injectable, donc le panneau ne dispose pas d'un champ d'accès arbitraire à un dossier et ne lit pas lui-même des données non documentées. Aucun chargement automatique d'un fichier inconnu ou de DofusDB. Les fiches manuelles restent en mémoire jusqu'à la fermeture, sans sauvegarde ni transition.

## Conditions des parcours

Un parcours est calculé par BFS sur transitions déclarées, avec vérification de la provenance du snapshot, de chaque carte et de chaque arête et de l'identité de version. Direction par défaut uniquement FromMapId vers ToMapId ; sens retour autorisé seulement par liaison séparée ou preuve Bidirectional=true. Le statut UNVERIFIED reste affiché si une transition ou une version manque. Les coordonnées adjacentes ne deviennent jamais une transition ; aucune action dans le client, commande de navigation ou script n'est produite.

## Validation

Build App Release sans restore tenté le 20 octobre. Le build par défaut a d'abord été bloqué hors OWN : 3 CS2001 sur fichiers générés manquants sous GivreClone.App/Services/NoCodeLot2/obj/Release/net10.0. Le premier contournement modifiant DefaultItemExcludes a généré des attributs d'assembly dupliqués CS0579, car il supprimait les exclusions implicites de MSBuild. L'essai final, en conservant les exclusions implicites et en ne complétant que DefaultExcludesInProjectFolder, a **réussi avec 0 erreur** et 195 avertissements au niveau du dépôt :

    dotnet build GivreClone.App/GivreClone.App.csproj --no-restore -c Release -v:q -clp:ErrorsOnly -p:BuildProjectReferences=false -p:DefaultExcludesInProjectFolder=**/obj/**%3B**/bin/**%3B**/.*/**

La compilation a vérifié les nouveaux XAML/C# Avalonia du lot. Le prime reste responsable du build solution Release avec les projets référencés après la stabilisation des workers ; les tests backend Atlas attendent le livrable de worker-1.

Provenance : inspiration UX uniquement ; aucun code ou asset d'un dépôt tiers et aucune action/client Dofus.
