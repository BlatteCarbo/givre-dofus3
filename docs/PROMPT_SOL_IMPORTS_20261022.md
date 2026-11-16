# Prompt pour « 6.1 Sol » : quoi importer de MizanBot dans GivreClone

Colle ce texte tel quel dans Sol, avec en pièces jointes : `docs/MIZANBOT_UI_MAP_20261022.md`, `CHANGELOG.md`, les notes de version MizanBot (1.1.4 à 1.3.3) et 2 à 3 captures (fiche bot, carte en combat, onglet Combat).

---

Tu es chef de produit et architecte pour un bot Dofus 3 (client Unity, protocole protobuf). Deux produits :

- **GivreClone** (le mien) : C# .NET 10, Avalonia 12, CommunityToolkit.Mvvm, scripts Lua (MoonSharp), serveur MCP. Modes MITM (client Dofus piloté) et Socket (sans client). Déjà présent : combat auto avec challenges, carte isométrique, HDV, chasse au trésor, banque, inventaire, planning, éditeur Lua, MCP, carte du monde DofusDB, combat jouable à la main depuis la carte, variantes de sorts.
- **MizanBot 1.3.3** (concurrent, commercial) : je l'ai exploré en détail à l'écran (voir `MIZANBOT_UI_MAP_20261022.md`) et j'ai ses notes de version.

**Contraintes**
- Je ne copie ni code, ni textes, ni images de MizanBot : seulement des idées de fonctionnalité, d'organisation et d'ergonomie, réécrites à ma façon.
- Un seul développeur, un PC modeste (14 pouces), pas d'effets lourds (pas de tuiles animées, pas de particules).
- Tout ce qui agit en jeu doit pouvoir être coupé, et rien ne doit dépenser de l'argent réel (ogrines, abonnement) sans confirmation explicite.
- Je ne veux pas de fonctions « de test » : seulement des choses qu'un joueur utilise vraiment.

**Ta mission**
1. Pour chaque fonctionnalité de MizanBot (UI, combat, carte, console, HDV, chasse, sécurité, équipe, comptes, MCP/IA, performances), dis si elle vaut la peine d'être reprise dans GivreClone.
2. Classe-les dans un tableau : *valeur pour un joueur* (1-5), *effort* (S/M/L), *risque* (ban, bug, régression), *dépendances*, et ta décision : **Faire maintenant / Plus tard / Ne pas faire**, avec une phrase de justification.
3. Pour les 8 meilleures, donne une spécification courte : comportement attendu, écrans à modifier, données ou paquets nécessaires, cas limites, et comment la tester en jeu.
4. Signale les pièges : ce qui ressemble à une bonne idée mais attire les modérateurs (ex. combats sans fin, comportements trop réguliers), ce qui dépend d'un serveur central (partage de données entre utilisateurs), et ce qui est juridiquement ou techniquement fragile.
5. Termine par un ordre de réalisation sur 2 semaines, et 3 idées que MizanBot n'a pas et qui feraient la différence.

**Format** : français, sans jargon inutile, tableau puis spécifications, 1 500 mots maximum pour la partie prose.

Points à examiner en priorité : combat en direct (ordre de jeu, dégâts flottants, abandon automatique après N tours, abandon d'un challenge bloquant), détection de modérateurs et de ban, rapports d'incident, mode Stream, console de chat par canal, remise en vente HDV très rapide, chasse au trésor de bout en bout, reconnexion de secours d'un bot bloqué, équipes (suiveurs qui prennent le zaap du chef), onglets gardés ouverts, assistant IA via MCP.
