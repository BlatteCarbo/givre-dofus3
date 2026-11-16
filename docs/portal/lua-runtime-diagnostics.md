# Projection des diagnostics Lua (lecture seule)

Le fichier `GivreClone.App/Services/LuaEditor/RuntimeTraceProjection.cs` fournit `RuntimeTraceProjection.Project(logText, isRunning, source)` pour reconstituer le diagnostic **local** d'une session à partir du journal écrit par `GivreLuaHost.Logging.cs`.

Cette API pure analyse les lignes `[INFO]`, `[WARN]`, `[ERR ]`, `[OK  ]` et `[DBG ]`, sépare les sessions d'un même journal, expose la dernière activité et la dernière erreur, détecte une position Lua dans les messages MoonSharp et renvoie la ligne source lorsque le script correspondant est transmis. La quantité de texte est bornée et les longues lignes ignorées. Aucun fichier n'est ouvert par cette API : l'appelant fournit uniquement la chaîne qu'il a le droit de consulter.

**Statut trivalent** : `running-observed-by-caller`, `stopped-observed-by-caller`, `unknown`. Un ancien journal ne suffit jamais à déduire le statut actuel, la réussite d'un mouvement ou l'existence d'un ACK serveur. Le paramètre `isRunning` doit venir d'un hôte identifié et actuel ; passer `null` sinon. Le message d'erreur et le contexte restent des données de diagnostic, pas une instruction à exécuter.

Tests déterministes dans `LuaRuntimeTraceProjectionTests.cs`, intégrés aux projets de tests locaux et purs. Une interface ou commande future peut utiliser cette projection sans modifier son modèle de preuve.
