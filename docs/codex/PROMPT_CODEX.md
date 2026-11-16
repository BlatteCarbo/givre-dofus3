# Prompt à donner à Codex (recherche en lecture seule, 3 livrables)

Tu travailles pour moi sur mon projet de bot Dofus 3. Ton rôle est UNIQUEMENT de la recherche et de la documentation : lis, analyse, écris des rapports. Tu ne modifies aucun code existant.

## Règles (à respecter à la lettre)
1. **Écriture** : tu écris seulement dans `%USERPROFILE%\Videos\GIVRE\GivreShareWork\docs\codex\`. Aucun autre fichier du dépôt n'est modifié, créé ou supprimé. Pas de commit, pas de `git stash`, pas de build qui écrase `bin/` ou `obj/` de `GivreShareWork`.
2. **Je (Claude) travaille en parallèle sur `GivreShareWork`** : ne touche pas à `GivreClone.App`, `GivreClone.Protocol`, `GivreClone.Tests`, `native`, `tools`. Si tu as besoin de compiler quelque chose, fais-le dans une COPIE sous `%USERPROFILE%\Videos\GIVRE\_codex_work\`.
3. **Ne lis jamais et ne recopie jamais de secrets** : fichiers `*.secret`, `.env`, `accounts.json`, `device-ids.json`, jetons, mots de passe, clés API, certificats, sessions. Si un fichier en contient, cite son chemin sans son contenu.
4. **Pas de réseau vers Ankama ni vers Dofus**, pas de lancement de `Dofus.exe`, pas d'injection, pas de modification du fichier `hosts`, ne touche pas au GivreClone d'origine installé dans `%USERPROFILE%\AppData\Local\Givre\`.
5. **Honnêteté** : n'invente rien. Chaque affirmation d'un livrable cite sa preuve (fichier + ligne, ou extrait de code). Ce que tu n'as pas pu vérifier est marqué « NON VÉRIFIÉ », avec la raison. Une hypothèse est écrite comme hypothèse.
6. Langue des rapports : français. Format : Markdown.

## Contexte
- Build du client installé : Dofus 3.7.11.11. Le trafic de jeu est en clair (protobuf), les messages ont des noms obfusqués (`type.ankama.com/xxx`).
- Mapping utilisé par mon clone : `%USERPROFILE%\Videos\GIVRE\GivreShareWork\data\matching_current.json` (code → nom) et `fields.json` (message → champ → numéro). Un champ dont le nom commence par une lettre minuscule suivie de lettres sans sens (ex. `fwmg`, `fyvk`, `fpbo`) est un champ OBFUSQUÉ non résolu.
- Pipeline de décompilation déjà prêt (lis ses notes avant de refaire quoi que ce soit) : `%USERPROFILE%\Videos\GIVRE\` (cherche `full-il-decompile`, `DofusReverseIndexer`, `GivreActive\analysis`). Sorties déjà présentes : `DofusReverseIndexer\local-output\`, `GivreActive\analysis\`.

## Livrable 1 — Ankama Launcher : ce que le lanceur donne à `Dofus.exe`
Fichier : `docs/codex/01_ankama_launcher.md`
Source principale : `%USERPROFILE%\Documents\MVP\ankama-launcher-extracted\` (déjà extrait, lisible) et `%USERPROFILE%\Documents\MVP\ZaapEmu\` (code seulement, jamais les `.secret`).
Réponds, preuves à l'appui :
- a) Ligne de commande exacte (arguments et variables d'environnement) avec laquelle le vrai lanceur démarre `Dofus.exe` pour Dofus 3.
- b) Contenu et rôle de `divazaap.json` (ou équivalent) : tous les champs. Surtout **le format exact de `connectionHosts`** (nom:hôte:port ? autre ?) et d'où viennent ses valeurs réelles.
- c) Liste COMPLÈTE des méthodes du service Zaap (Thrift) que le client appelle, avec leurs arguments, leurs types de retour, et ce que le client fait si une méthode renvoie vide ou échoue.
- d) Mes notes disent que `connectionHosts` / `configUrl` ne pilote rien et que le fichier `hosts` est obligatoire pour rediriger le trafic. Confirme ou infirme avec le code, et dis ce qui pilote réellement l'hôte de connexion du client.
- e) Comment le lanceur obtient et renouvelle le jeton de jeu (`CreateToken`), et quand le certificat Shield (id + hash) est exigé ou non (compte avec ou sans Shield).

## Livrable 2 — REA Dofus : champs obfusqués des fonctions non câblées
Fichier : `docs/codex/02_dofus_champs_obfusques.md`
Source : le client 3.7.11.11 décompilé (pipeline existant). Pour chaque point, donne : nom clair du message, nom clair ou obfusqué du champ, **numéro de champ protobuf**, type, preuve (extrait de code décompilé ou chemin de fichier), et niveau de confiance.
- a) Requête d'**achat** et de **vente** à un PNJ marchand (boutique PNJ) : message, champs (objet, quantité, ...).
- b) `QuestStartRequest` : que représente le champ 2 (`fpbo`) ?
- c) `ClientChallengeInitRequest` : numéro et rôle du champ `fyvk` (chaîne d'engagement de la connexion par socket direct).
- d) **Changement de personnage** (retour à la sélection du personnage) : message(s) à envoyer.
- e) **Tutoriel guidé** : existe-t-il un message pour le quitter ? lequel ?
- f) Messages d'**échange** et d'**HDV** dont des champs sont encore obfusqués dans `fields.json` (liste, avec ce que tu as pu résoudre).
Termine par un tableau récapitulatif « message | champ | numéro | résolu ? | preuve ».

## Livrable 3 — Récap GivreActive (pour améliorer Givre)
Fichier : `docs/codex/03_recap_givreactive.md`
Source : `%USERPROFILE%\Videos\GIVRE\GivreActive\` (mon dernier projet, plan maître et `AGENTS.md` à lire en premier).
Fais un inventaire utile à quelqu'un qui reprend le clone de Givre (`GivreShareWork`) :
- Pour chaque module important (IA de combat, pathfinding et graphe de transitions entre cartes, récolte et garde-fous d'aggro, banque, HDV, serveur MCP de test, pont de données SQLite, outils de mesure) : chemin des fichiers, ce que ça fait, **état réel** (compile / testé en jeu / non), limites connues.
- Colonne « à reprendre dans le clone ? » avec la raison (ce que Givre fait mieux que `GivreShareWork`, ou l'inverse). Compare en lisant le code des deux côtés, sans rien modifier.
- Liste des bugs connus ou pièges déjà documentés qui s'appliqueraient au clone.
- Liste des fichiers de données réutilisables (mappings, graphes, bases) avec leur version de jeu et leur date.

## Fin de mission
Crée `docs/codex/00_SYNTHESE.md` : une page, avec (1) ce qui est établi, (2) ce qui reste NON VÉRIFIÉ, (3) les 5 actions les plus utiles à faire ensuite dans le clone, classées par gain. Ne dis jamais qu'une fonctionnalité « marche » sans preuve d'exécution : « compile » et « fonctionne » sont deux choses.
