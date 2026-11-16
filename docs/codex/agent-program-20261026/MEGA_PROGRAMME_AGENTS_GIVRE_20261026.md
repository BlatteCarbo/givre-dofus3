# Programme Givre — 161 lots pour agents

Version du 26 octobre 2026. Fichier autonome à distribuer : prompt maître, missions, sources et architecture proposée. Les missions décrivent du travail à faire, pas des fonctionnalités déjà livrées.

## Distribution immédiate

Donner à chaque agent le prompt maître et UNE fiche. Remplir identité, base source incluant les modifications non commitées, worktree, branche et fichiers réservés. Les agents web sans accès au poste reçoivent un paquet de sources minimal et rendent analyses, prototypes et patches ; ils ne prétendent pas avoir exécuté Givre.

S01–S03 préparent la photographie de source, les propriétaires et le build reproductible. Les autres lots peuvent commencer en recherche ou prototype isolé pendant ce temps ; l’intégration attend les dépendances. Les chemins historiques réservés restent coordonnés. Un seul agent intègre chaque point chaud.

## Proposition de première vague : 32 agents

- 01–04 : S01, S02, S03, S04.
- 05–10 : C01, C02, C03, C04, C06, T01 ; diagnostics indépendants avant modifications partagées.
- 11–16 : N01, N02, L01, H01, H02, H03.
- 17–19 : trois variantes isolées de U01 : encre/émeraude, nuit/cuivre, ivoire/forêt.
- 20–24 : M01, M03, U03, P01, E01.
- 25–30 : R04, R08, R05, B01, B02, B03.
- 31 : Q01 preuves ; 32 : Q07 revue indépendante des premières livraisons.

A11 prend une place opérationnelle dédiée dès que le compte est accessible ; il ne partage pas son compte avec un autre agent. Cette liste est une proposition d’affectation, aucun agent n’a été lancé par ce dossier.

## Règles de résultat

Recherche, prototype, code vérifié et validation en session sont des statuts différents. Une commande acceptée n’est pas une action confirmée. Un build ne prouve pas l’effet en jeu. Un dessin ne prouve pas le branchement UI. Toute limite doit rester dans le rapport. Chaque revue essaie de réfuter au moins un critère métier.

## Prompt maître à donner à chaque agent

# Prompt maître — programme Givre, agents Chat on Steroids et relecture T3

Tu es un agent de développement ou de recherche de l'équipe Givre. Ce message constitue un ordre de travail concret, pas une demande de proposer vaguement des idées. L'utilisateur dispose de nombreux agents : les enquêtes longues, reverse engineering, comparaisons de dépôts, prototypes visuels et validations approfondies doivent être répartis en lots. Le coordinateur T3 recevra les résultats, les vérifiera, puis assurera l'intégration. Aucun agent n'est autorisé à déclarer un autre lot terminé sans en avoir contrôlé les preuves.

## Affectation à remplir par le coordinateur

- IDENTITÉ_AGENT : `<nom stable>`
- LOT : `<S01, C04, U03, B02…>`
- BASE_SOURCE : `<commit de la photographie validée, PAS seulement l'ancien HEAD>`
- WORKTREE : `<répertoire absolu propre à cet agent>`
- BRANCHE : `agents/20261026/<LOT>-<identité>`
- FICHIERS_RÉSERVÉS : `<liste exacte, attribuée avant modification>`
- MODE : `recherche`, `prototype isolé`, `implémentation`, ou `revue indépendante`.

Si l'affectation n'est pas remplie, commence par S01–S03 en lecture et prépare une répartition. N'attribue pas silencieusement le même fichier à plusieurs agents. Si tu n'as ni terminal ni accès aux fichiers, dis-le une fois et fournis une analyse sourcée ou un patch fondé sur les fichiers reçus ; n'invente aucune exécution.

## Autorisation de moyens donnée par l'utilisateur

**Tu as le droit d'installer et d'utiliser tous les outils nécessaires à ton lot** : SDK, compilateurs, bibliothèques, outils de build, analyse statique, décompilateurs, outils de reverse engineering, outils d'image, profils, navigateurs et outils de test. Tu peux télécharger les sources publiques citées, créer des environnements Python/Node/.NET/Rust, compiler des outils, lancer des prototypes et réaliser des investigations longues. Ne demande pas de confirmation pour chaque installation courante, lecture de source, build ou essai local réversible. Choisis les outils adaptés et note leurs versions dans le rendu.

Installe de préférence dans ton environnement de travail afin de ne pas modifier les dépendances des autres agents. Les installations nécessaires sont autorisées ; cela ne vaut pas ordre d'arrêter les sessions actives, de remplacer les bases, de supprimer les modifications d'autres agents, de désactiver des protections de la machine, de dépenser des kamas ou de publier. Ces actions ne découlent pas d'un simple lot de développement. Le coordinateur possède les essais en session et la publication finale. Les secrets existants ne sont pas du matériel de recherche à copier.

## Projet et point de départ

Projet : GivreClone, application Avalonia/.NET avec moteurs combat, transport Socket/MITM, scripts Lua, HDV, métiers, craft, forge, chasses, équipes, carte et contrôle MCP.

Worktree de référence de cette préparation :
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`

Branche : `fix/f01-f16-final-20261024`. HEAD observé : `ce2a9bbd956b3022a53d7dabd759227cbfb7827a`.

**IMPORTANT : ce worktree contient beaucoup de modifications suivies ET de nouveaux fichiers non commités. Un worktree créé depuis ce seul HEAD n'inclut pas les correctifs actuels.** S01 prépare d'abord une photographie de source contrôlée et un manifeste ; le coordinateur donne ensuite le commit/paquet exact à chaque agent. Ne jamais `reset --hard`, `clean -fd`, écraser ou mettre de côté les changements d'un autre agent. Ne pas utiliser l'ancien master comme référence fonctionnelle.

Lis les consignes `AGENTS.md` applicables, puis les documents récents :
- `docs/codex/REPRISE_20261026.md`
- `docs/codex/TODO_FEATURES_20261024.md`
- `docs/codex/REPRISE_TOUS_LOTS_20261025.md`
- `docs/codex/MINERBOT_COMBAT_20261026.md`
- `docs/codex/MINEUR_RUN_20261026.md`
- `docs/codex/BASTIONBOT_PECHEUR_20261026.md`
- `docs/codex/FORAGERBOT_STUFF_20261026.md`
- `docs/codex/FORAGERBOT_FARM_20261026.md`
- la fiche du lot et ses dépendances dans `MEGA_PROGRAMME_AGENTS_GIVRE_20261026.md`.

Les historiques contiennent des états dépassés. La consigne utilisateur la plus récente et la section datée la plus récente prévalent. Vérifie toujours la version effectivement exécutée avant d'interpréter un journal.

## Ce que l'utilisateur veut vraiment

1. Corriger les bugs et les fonctionnalités seulement esquissées ; suivre toute la chaîne UI ou MCP → commande → transport → réponse serveur → état métier → affichage.
2. Des sessions durables : les indisponibilités temporaires déclenchent une attente et une reprise raisonnable ; elles ne doivent ni provoquer un arrêt définitif injustifié ni alimenter une boucle d'actions sans progrès. Une défaite ou un résultat transactionnel inconnu reste un cas à traiter explicitement.
3. Une UI nettement plus belle. L'utilisateur **n'aime pas le gris**. Proposer une vraie direction artistique : bleu encre/pétrole, émeraude, accents chauds, contrastes maîtrisés, typographie et espacement. Ne pas se contenter de changer trois couleurs ou d'ajouter des cartes grises.
4. Une carte lisible, avec peu d'informations par défaut ; couches et diagnostics sur demande. Le combat sur la carte doit devenir clair et utile : placement, cibles, PV/PA/PM, portées, zones et résultat des actions réellement synchronisés.
5. Un véritable HDV dans l'application : quand le bot Socket ouvre un HDV, l'utilisateur voit immédiatement le marché correspondant, les offres réelles et leurs jets ; acheter/vendre doit rester lié à une offre et à une confirmation précises.
6. Une économie mesurée : lots, taxes, prix récents, profondeur, ventes réalisées, coût de trajet et de fabrication ; pas de revenu fictif calculé depuis les retraits bancaires ou la valeur moyenne du sac.
7. Pour Foragerbot : farmer les groupes **de 1 à 8 monstres contenant AU MOINS un Boufton Noir**, y compris sept Blancs et un Noir ; ensuite préparer beaucoup de clés du donjon Bouftou et boucler le donjon. La recette, les stocks, les métiers, l'entrée et les consommations doivent être vérifiés avant de déclencher cette production future.
8. Préparer un futur pont T3 Code ↔ Chat on Steroids : des coordinateurs T3/ChatGPT/Claude distribuent des travaux aux agents CoS, suivent l'avancement, récupèrent des preuves et relancent des revues. **Le pont n'est pas encore livré : on veut d'abord une architecture et un prototype isolé, sans annoncer un accès distant aux workers qui n'a pas été démontré.**

## Sessions en cours : ne pas les perturber depuis les lots de développement

- Bastionbot : compte `c4a95aedecaf4a82803e6c64eb33ca38`, Pêcheur 200, script `Bastionbot - Pecheur 1-200 ameliore`.
- Minerbot : `4bc58e7f9db345aaa2e7dfdaf9ae1726`, Mineur 130, script `Mineur 1-200 - Minerbot`. Le retrait total d'un million de kamas est déjà effectué : ne pas le répéter.
- Foragerbot : `00fe352d06bf491fb5abcc2029ce84d7`, stuff complet acheté/équipé pour 1 453 373 kamas, dont Gelano PA/165 vitalité et monture Ébène 100 ; script `Foragerbot - Tainela Boufton Noir rentable` maintenant configuré 1–8 avec au moins un Noir. Ne pas racheter le stuff. Régénération automatique supprimée à la demande utilisateur à 10:15 UTC : MIN_LIFE_PERCENT=0 et aucun appel regenerate ; ne pas la réintroduire.
- Les trois profils sont en Turbo. La publication `GivreClone-CombatLife-20261026` est active au relevé ; le PID est à redécouvrir, jamais à réutiliser comme identité permanente.
- Nouvelle mission utilisateur : le compte `player.four666+1` sera accessible à un agent désigné. Il doit créer un NOUVEAU personnage sur **Kourial**, puis le prendre en charge par MCP ou Chat on Steroids Desktop. Ne jamais sélectionner Mikhal ou Dakhal comme repli. Les autres comptes peuvent être utilisés si nécessaire, avec un propriétaire opérationnel unique par compte et coordination des scripts en cours.
- Surveillance existante toutes les trois heures ; arrêt de session prévu le 26 octobre à 23 h 50 Europe/Paris, après combat, comptes laissés connectés. Les lots n'ajoutent pas de surveillants concurrents.

La base installée et les données de session restent en place. Les expériences de paquets, économie, forge et native se font avec fixtures et instances isolées tant que le coordinateur n'a pas donné une fenêtre de validation réelle.

## Méthode attendue pour TON lot

1. Reformule le défaut ou le comportement visé en deux phrases. Donne les fichiers que tu comptes modifier et les dépendances manquantes.
2. Reproduis ou caractérise le comportement actuel ; conserve une preuve minimale, sans journal complet ni secret. Distingue fait observé, lecture de code, hypothèse et proposition.
3. Lis les sources de référence pertinentes à un commit précis. Vérifie licences, version du jeu et protocole ; un émulateur d'une ancienne version est une piste, pas la vérité du serveur actuel.
4. Livre un changement utile et intégré. N'ajoute pas un bouton non branché, un service factice ou un `TODO` en prétendant avoir terminé. Si le blocage est réel, livre son diagnostic exploitable et les contrats nécessaires.
5. Contrôle succès, refus, annulation, redémarrage et changement de compte lorsque pertinents. Les tests doivent détecter un vrai défaut métier ; évite les tests qui recopient l'implémentation.
6. Pour une UI, joins des captures à plusieurs tailles et un scénario interactif précis. Pour un protocole, joins les champs par noms catalogués, la version et une fixture minimale. Pour l'économie, distingue prix proposé, transaction confirmée et estimation.
7. Termine ton lot sur ta branche, avec diff, commandes de vérification, risques et limites. Le coordinateur intègre ; ne déploie pas l'application sur les bots actifs.

## Contrat de concurrence

Un agent = un worktree = une branche. Les agents peuvent lire tous les modules, mais ne modifient que leur périmètre attribué. Les points chauds partagés ont un seul propriétaire à la fois : `LaunchService.Packets.cs`, `LaunchService.cs`, `GivreLuaHost.Bindings.cs`, `BotsView.axaml`, les ressources de thème globales, `RequiredMapping.cs`, les fichiers de projet et les dictionnaires FR/EN/ES. Si ton lot a besoin de l'un de ces fichiers sans le posséder, fournis une petite proposition d'intégration au coordinateur, et continue ta partie indépendante.

Les anciennes réservations combat/forge/déplacement/trésor sont à reconfirmer dans le registre de propriétaires ; elles ne doivent pas être ignorées sous prétexte d'une grande équipe. Deux designs UI peuvent être explorés en parallèle dans des prototypes, mais un seul propriétaire applique les ressources finales.

Builds et tests lourds peuvent tourner dans des worktrees distincts ; évite deux builds du même worktree et les sorties binaires partagées. Ne tue jamais un testhost ou un processus non attribué à ton exécution.

## Livrable obligatoire

Crée `docs/agents/20261026/<LOT>/<AGENT>/REPORT.md` avec :

```text
lot_id / agent_id / mode / base_commit / head_commit
statut : recherche | prototype | code_verifie | valide_en_session | bloque
probleme et comportement obtenu
preuves : faits / hypotheses / limites
fichiers modifies et eventuels points d'integration reserves
commandes exactes et resultats reels de validation
sources : URL + commit + chemins consultes
scenarios succes/refus/annulation/reprise verifies
limitations et travaux restant necessaires
instructions de revue en cinq minutes
```

Joins `RESULT.json`, un patch ou un commit accessible, les captures/fixtures nécessaires et leurs empreintes. Aucune preuve de build ou de session inventée. Une capture d'écran ne prouve pas l'achat ; une commande acceptée ne prouve pas l'exécution ; un module chargé ne prouve pas son effet ; un test vert ne prouve pas que le jeu est corrigé.

Le coordinateur répondra : accepté, accepté avec limites, à reprendre, ou rejeté avec reproduction. Il pourra te redonner le même lot pour une seconde passe. Tu conserves donc les décisions et les objections dans ton rapport plutôt que de compter sur la mémoire d'un chat.

## Démarrage

Lis ta fiche. Contrôle la photographie de source et le propriétaire des fichiers. Commence le travail concret, puis fournis rapidement une première preuve ou un diagnostic. Si tu coordonnes, distribue d'abord les enquêtes et prototypes indépendants ; n'attends pas qu'un seul agent ait tout terminé pour exploiter l'équipe. Priorités initiales : S01–S03, C01, N01, L01, H01–H03, U01–U03, M01–M03, P01 et B01–B02.

## Nouvelle mission compte — consigne utilisateur du 26 octobre

Le compte `player.four666+1` sera mis à disposition de l’agent affecté à A11. Créer un nouveau personnage sur **Kourial exclusivement**, puis s’en occuper via MCP, Chat on Steroids Desktop ou une autre surface disponible et vérifiée. L’utilisateur autorise aussi les interventions nécessaires sur les autres comptes. Cela remplace l’ancienne interdiction générale de toucher le quatrième compte ; cela ne signifie pas que tous les agents doivent agir sur les sessions en parallèle.

Réserver le compte dans le registre avant action. Vérifier l’identité du compte et la liste réelle des serveurs ; ne pas confondre nom affiché, identifiant serveur et sélection effective. Si Kourial est inaccessible, indisponible ou absent, expliquer le blocage sans créer ailleurs. Ne supprimer aucun personnage existant. Si la classe ou le nom n’a pas été précisé, les choisir raisonnablement selon le projet et consigner le choix. Ne réclamer une intervention que pour une authentification ou étape réellement bloquante. Les secrets passent par une saisie privée ou l’interface d’authentification, jamais dans les rapports. Relire serveur, personnage et état connecté après création, puis rendre une preuve minimale et le plan de progression. Aucun achat réel ne découle de cette mission.


## Pont futur T3 Code ↔ Chat on Steroids

Le pont est une proposition à construire après retour des agents. Aucun worker CoS n’a été réveillé ni aucun endpoint de pont installé pendant cette préparation.

Sources ciblées relues : CoS src/main/agents.ts, src/main/mcp/tools-core.ts, src/main/mcp/server.ts, docs/codex-desktop-bridge.md ; versions et contenus dans bridge-source-refresh.json. Les fonctions TypeScript internes ne sont pas automatiquement des outils publics.

Constat : les workers CoS appartiennent à une famille détenue par une conversation prime. Les noms worker-1 ne sélectionnent pas le propriétaire. Le réveil renouvelle l’incarnation ; conserver conversation, famille, incarnation et worker séparément. Le serveur MCP local décrit un port éphémère et des contrôles token/Host/Origin ; ne pas les désactiver. La RFC de pont Codex Desktop ne démontre pas un réveil de worker CoS depuis T3.

Architecture proposée : broker local durable indépendant du runtime Givre ; adaptateur T3 ; adaptateur CoS exécutant les actions sous le propriétaire légitime ; stockage des briefs, baux, résultats, objections et artefacts. Commencer par export/import de fichiers, puis worker pull, puis réveil poussé seulement si une capacité supportée existe et qu’on relit le bon destinataire.

Identité complète : installation, projet, job, coordinateur, ownerConversation, family, incarnation, worker, attempt et reviewRound. Un alias ou un en-tête fabriqué ne donne pas autorité. Tous les coordinateurs consultent les mêmes baux de fichiers/comptes.

États : draft, queued, leased, running, submitted, reviewing, accepted, needs_changes, blocked, cancelled, dispatch_unknown. Un timeout après envoi exige réconciliation avant réessai ; dédupliquer les livraisons par requestId et attempt. Ne pas promettre une exécution physique exactement une fois.

Contrats PROPOSÉS : capabilities, submit_job, claim_job, heartbeat, get_job, list_jobs, submit_artifact, submit_result, request_review, cancel_job. Ils ne sont pas annoncés disponibles. Inclure schemaVersion, expectedVersion, idempotencyKey, baseCommit, fichiers réservés, résultat attendu, limites et empreintes d’artefacts. Les tokens restent privés.

MVP1 : distribution manuelle et retour relisible. MVP2 : deux workers synthétiques et bail atomique avec crash/reprise. MVP3 : réveil d’un vrai worker autorisé puis lecture de son résultat, même famille. MVP4 : deux coordinateurs, ancien worker, callback dupliqué, annulation et crash après envoi. Aucun test initial ne lance d’action de jeu.

Côté T3, distinguer tâches déléguées et conversations ordinaires. Découvrir les capacités et options modèle disponibles à l’exécution. « GPT 6 High web » est une cible exprimée par l’utilisateur, pas une capacité vérifiée de cette installation. Une notification de fin déclenche une revue, jamais une intégration automatique.

## Sources et réutilisations à examiner

Les neuf têtes de dépôts utilisateur ont été vérifiées pendant cette reprise. Les nouvelles têtes T3/CoS ont fait l’objet de lectures ciblées rafraîchies. Conserver le commit des preuves et vérifier les licences avant intégration de code.

### Altansar69/dofus-enhanced

https://github.com/Altansar69/dofus-enhanced/tree/2b494e141eb82a47d9c10741b7ac09877f7ec8a9

Commit `2b494e141eb82a47d9c10741b7ac09877f7ec8a9` ; langage déclaré : C++ ; licence : Unlicense.

Animations, attentes et restauration : R08 ; vérifier ABI et version du client.

Fichiers dans les preuves : `src/animations/animations.cpp`, `docs/GUIDE.md`.

### Keka-Bron/JondoEmu

https://github.com/Keka-Bron/JondoEmu/tree/6b9b857ac9496bcc2553a7c2a5e8b8e4cad8546d

Commit `6b9b857ac9496bcc2553a7c2a5e8b8e4cad8546d` ; langage déclaré : C# ; licence : non établie dans les métadonnées consultées.

Schémas et banc local : R03/R10 ; une version différente ne prouve pas le protocole réel.

Fichiers dans les preuves : `Jondo.Unity.Server/Handlers/BankHandler.cs`, `Jondo.Unity.Protocol/Wire/ProtoSchema.cs`.

### vDAKK/stakk-open-source

https://github.com/vDAKK/stakk-open-source/tree/59631e9035cbd83e3416b6c8d500f9225153eae6

Commit `59631e9035cbd83e3416b6c8d500f9225153eae6` ; langage déclaré : JavaScript ; licence : GPL-3.0.

UI multi-compte et sessions : R07/A/U ; examiner les obligations GPL-3.0.

Fichiers dans les preuves : `package.json`, `lib/web-server.js`.

### ix-xs/metamob.api

https://github.com/ix-xs/metamob.api/tree/8be181dc62fbeae26af64f347fb29b4c5492d27d

Commit `8be181dc62fbeae26af64f347fb29b4c5492d27d` ; langage déclaré : TypeScript ; licence : MIT.

Collections et quêtes : R06 ; résolution de noms, cache, pagination et limites API.

Fichiers dans les preuves : `package.json`, `docs/src/content/docs/basics/rate-limit.md`.

### meakitfed/SuperDofus

https://github.com/meakitfed/SuperDofus/tree/7d5f145974a98048c61f44bd8512b6e4371c4461

Commit `7d5f145974a98048c61f44bd8512b6e4371c4461` ; langage déclaré : GDScript ; licence : non établie dans les métadonnées consultées.

Looks et rendu : R04/M ; cache, directions, montures, couleurs et assets absents.

Fichiers dans les preuves : `project.godot`.

### Skkay/Ogrine-Rates_Server

https://github.com/Skkay/Ogrine-Rates_Server/tree/dbef78f9a2f5c0207b7141d20d301ab19d7928c7

Commit `dbef78f9a2f5c0207b7141d20d301ab19d7928c7` ; langage déclaré : PHP ; licence : non établie dans les métadonnées consultées.

Historique de taux : E ; serveur, provenance, fraîcheur et distinction observation/transaction.

Fichiers dans les preuves : `composer.json`.

### Fronne/Dofus-Craft

https://github.com/Fronne/Dofus-Craft/tree/21f5b96b88b8521e5f48670b4c5e3b0c3f4dbeb8

Commit `21f5b96b88b8521e5f48670b4c5e3b0c3f4dbeb8` ; langage déclaré : TypeScript ; licence : non établie dans les métadonnées consultées.

Craft ou achat : P/E ; recettes, taxes, stocks et dépendances de fabrication.

Fichiers dans les preuves : `package.json`.

### Unclerein/dofus-prospection

https://github.com/Unclerein/dofus-prospection/tree/de845abfa4fcacc1ec588efbf5048ec3fa75fb36

Commit `de845abfa4fcacc1ec588efbf5048ec3fa75fb36` ; langage déclaré : Python ; licence : non établie dans les métadonnées consultées.

Acquisition de prix : E/R ; distinguer offres, moyennes et ventes réalisées.

Fichiers dans les preuves : `dofustool/analysis/crafts.py`, `dofustool/web/server.py`.

### Valentin-alix/BillBot

https://github.com/Valentin-alix/BillBot/tree/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14

Commit `5373d0dea2ffa6731cf81dd716f04d0c95d7ed14` ; langage déclaré : C# ; licence : non établie dans les métadonnées consultées.

Comparaison de parcours : R07/L/H ; vérifier le branchement réel des fonctions annoncées.

Fichiers dans les preuves : `pyproject.toml`.

Pont : https://github.com/totec448-spec/chat-on-steroids/tree/2fca8760bac79b7633267ac83012aa52f0a357f4 ; preuve initiale `863f22c811dcc2baacd5b5c0a010878e1774012c`, nouveaux fichiers ciblés dans bridge-source-refresh.json.

Pont : https://github.com/pingdotgg/t3code/tree/3433ba15fdfec1acaad273f293098e773fc97bee ; preuve initiale `6ffde5ba39ba6a718b658e2ef846e8f674c9ee6e`, nouveaux fichiers ciblés dans bridge-source-refresh.json.

## Index

- S01–S10 : Socle et intégration.
- C01–C10 : Combat et sorts.
- T01–T10 : Turbo et défis.
- N01–N10 : Navigation et récolte.
- L01–L10 : Lua et MCP.
- H01–H10 : HDV Socket.
- E01–E10 : Économie.
- P01–P10 : Production et donjons.
- U01–U10 : Interface.
- M01–M10 : Carte et combat visuel.
- F01–F10 : Stocks, banque et forge.
- A01–A11 : Comptes et équipes.
- R01–R10 : Recherche et reverse engineering.
- B01–B10 : Pont T3 / CoS.
- Q01–Q10 : Qualité et livraison.
- X01–X10 : Fonctionnalités avancées.

## S01 — Photographie complète de la source actuelle

Famille : Socle et intégration. Dépendances d’intégration : aucune.

### Mission concrète

Inventorier modifications suivies, nouveaux fichiers utiles et dépendances locales. Produire une base reproductible incluant les correctifs non commités sans emporter userdata, logs ou exécutables de session.

### Critères d’acceptation

Un agent neuf retrouve les mêmes sources et la même référence sans écraser le worktree actif. Manifeste de contenu et empreintes fournis.

### Scénarios à vérifier

Reconstruction dans un dossier vierge, comparaison des fichiers attendus, vérification qu'un simple checkout du vieux HEAD était incomplet.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S02 — Registre des propriétaires et graphe de dépendances

Famille : Socle et intégration. Dépendances d’intégration : S01.

### Mission concrète

Attribuer les points chauds à un seul intégrateur et transformer les réservations historiques en registre explicite. Préparer les leases de lots et les demandes de modification partagée.

### Critères d’acceptation

Deux agents ne peuvent posséder simultanément un même fichier exclusif. Les lots de recherche restent démarrables sans verrou d'écriture.

### Scénarios à vérifier

Simuler deux affectations concurrentes, départ d'un agent et transfert d'un lease avec modifications non livrées.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S03 — Reproduction du build sans chemins personnels

Famille : Socle et intégration. Dépendances d’intégration : S01.

### Mission concrète

Tracer Directory.Build.targets et les références locales des projets pour documenter les vrais prérequis. Supprimer les dépendances implicites au poste uniquement dans une copie de développement.

### Critères d’acceptation

Le build application et les tests adaptés fonctionnent dans un worktree préparé par une procédure écrite, avec les chemins et versions déclarés.

### Scénarios à vérifier

Nouvel environnement sans sorties binaires préexistantes, restauration froide puis build incrémental.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S04 — Inventaire des fonctionnalités réellement branchées

Famille : Socle et intégration. Dépendances d’intégration : S01.

### Mission concrète

Parcourir les écrans et commandes MCP, puis suivre chaque action jusqu'au traitement et au retour visible. Classer intégré, partiel, indisponible explicite ou squelette.

### Critères d’acceptation

Matrice traçable avec point d'entrée, service, forge/décodeur, état et preuve. Les modules visibles ne sont plus supposés opérationnels.

### Scénarios à vérifier

Échantillon d'au moins vingt actions couvrant Socket, MITM, économie et configuration, dont des refus.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S05 — Contrat unique d'état des commandes

Famille : Socle et intégration. Dépendances d’intégration : S04.

### Mission concrète

Définir accepté, en cours, envoyé, confirmé, refusé, expiré et indéterminé. Étudier les divergences entre MCP, UI et journaux, sans remplacer tous les services d'un coup.

### Critères d’acceptation

Une action ne devient jamais réussie à partir d'un simple accusé de prise en charge. Une même corrélation relie ses étapes.

### Scénarios à vérifier

Scénarios de retard, annulation, double accusé, mauvais PID et résultat serveur après timeout.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S06 — Génération de session et identité de compte

Famille : Socle et intégration. Dépendances d’intégration : S05.

### Mission concrète

Auditer les caches indexés par PID ou nom et proposer une identité compte-session-génération commune aux commandes différées.

### Critères d’acceptation

Un PID recyclé ou une reconnexion ne permet pas d'appliquer une action de l'ancienne session à la nouvelle.

### Scénarios à vérifier

Deux sessions successives partageant le même numéro de PID, changement de personnage et réponse ancienne retardée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S07 — Événements métiers et projection UI

Famille : Socle et intégration. Dépendances d’intégration : S05, S06.

### Mission concrète

Cartographier les sources de vérité et les projections concurrentes dans LaunchService, WorldState et ViewModels. Définir des événements versionnés sur un périmètre pilote.

### Critères d’acceptation

Une projection reconstruite depuis les événements donne le même état qu'une mise à jour directe. La version et la fraîcheur sont visibles en diagnostic.

### Scénarios à vérifier

Réordonnancement, doublons, snapshot partiel et reconstruction après reconnexion sur le pilote choisi.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S08 — Transactions mutuellement exclusives

Famille : Socle et intégration. Dépendances d’intégration : S05, S06.

### Mission concrète

Étudier les collisions HDV, banque, échange, craft et forge. Proposer un bail d'activité par compte avec transitions explicites, puis intégrer un pilote borné.

### Critères d’acceptation

Deux opérations incompatibles ne partagent jamais le même dialogue ou les mêmes accusés. Une opération annulée libère son bail.

### Scénarios à vérifier

Ouverture banque pendant HDV, changement d'échange pendant pose, arrêt de script et crash de tâche.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S09 — État de santé et progression réelle

Famille : Socle et intégration. Dépendances d’intégration : S05, S07.

### Mission concrète

Définir la progression utile par activité plutôt qu'un simple heartbeat. Afficher depuis combien de temps un bot n'a ni avancé, ni récolté, ni terminé d'action.

### Critères d’acceptation

Une boucle de messages identiques ne masque plus un blocage. Les attentes légitimes sont expliquées et bornées par type.

### Scénarios à vérifier

Groupe indisponible répété, ressource épuisée, attente banque et combat lent mais progressant.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## S10 — Intégration centrale et manifeste protocole

Famille : Socle et intégration. Dépendances d’intégration : S01, S02, S03.

### Mission concrète

Prendre en charge les petits raccordements dans les fichiers partagés, le manifeste RequiredMapping et les projets. Maintenir une branche d'intégration sérialisée sans absorber de changements étrangers.

### Critères d’acceptation

Chaque raccordement référence son lot, ses preuves et ses contrats. Build application, tests requis et clés FR/EN/ES contrôlés avant livraison.

### Scénarios à vérifier

Intégrer deux lots indépendants, détecter une collision et vérifier les manifests contre les sources réelles.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C01 — PV, soins, vol de vie et érosion

Famille : Combat et sorts. Dépendances d’intégration : S01, S05.

### Mission concrète

Reconstituer le défaut Minerbot avec une trace minimale. Vérifier la correction LifePointsGain, les resynchronisations de stats, les variations de vitalité et les réductions du plafond.

### Critères d’acceptation

Les PV restent cohérents après dégâts et soins, un plafond érodé ne remonte pas sans événement approprié. La correction chargée est identifiée.

### Scénarios à vérifier

Séquence dégâts-soin-érosion, soin après PV locaux nuls, snapshot blessé et monstres qui se soignent.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C02 — Vie et mort des combattants

Famille : Combat et sorts. Dépendances d’intégration : C01, S07.

### Mission concrète

Séparer valeur de PV dérivée, présence sur la carte, invisibilité, désinvocation et décès annoncé. Auditer les listes d'ennemis et les positions conservées dans plusieurs caches.

### Critères d’acceptation

Un ennemi vivant ne disparaît pas simplement parce qu'une estimation locale atteint zéro. Un décès serveur retire les cibles de toutes les projections.

### Scénarios à vérifier

PV locaux nuls sans mort, mort confirmée, retour visible, invocation et déconnexion au milieu d'un tour.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C03 — Dégâts conditionnels et masques de cible

Famille : Combat et sorts. Dépendances d’intégration : S01, C02.

### Mission concrète

Analyser les effets des sorts, leurs masques, conditions et zones, notamment les cas 12788 et 13528. Remplacer la somme aveugle par un modèle explicitant l'applicabilité.

### Critères d’acceptation

Une même cible ne reçoit pas simultanément des branches incompatibles. L'incertitude empêche une estimation excessivement précise.

### Scénarios à vérifier

Effets allié/ennemi, lanceur/invocation, branches conditionnelles et cas documentés dans la base installée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C04 — Portée, ligne de vue et refus de lancement

Famille : Combat et sorts. Dépendances d’intégration : C02, C03.

### Mission concrète

Comparer géométrie locale, obstacles et causes de refus serveur. Recaler les règles sur les sorts appris et les modifications de portée en cours de combat.

### Critères d’acceptation

Aucun enchaînement de lancers identiques sur une cible invalide. Le diagnostic distingue portée, obstacle, état et budget.

### Scénarios à vérifier

Cible déplacée, obstacle temporaire, bonus de PO, case occupée et échec confirmé suivi d'un autre sort.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C05 — Budget PA/PM autoritaire

Famille : Combat et sorts. Dépendances d’intégration : S05, C02.

### Mission concrète

Suivre les variations serveur, les dépenses en attente et les resets de tour. Empêcher un snapshot de base de regonfler un budget courant connu.

### Critères d’acceptation

Les actions planifiées ne dépassent pas les ressources réellement disponibles. Les budgets inconnus restent identifiés comme tels.

### Scénarios à vérifier

Retrait de PA au début du tour, remboursement, lancement refusé et retour de stats après une action.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C06 — Rôle des sorts sur soi et sur alliés

Famille : Combat et sorts. Dépendances d’intégration : C01, C02, C03.

### Mission concrète

Auditer OnMe, SelfBuff, TargetKind et les doublons de sorts dans les profils. Reconstituer pourquoi Minerbot lance certains sorts sur soi quand la liste d'ennemis se vide.

### Critères d’acceptation

Les soins, buffs et attaques de zone ne sont pas confondus. La simulation et le moteur appliquent le même rôle et les mêmes conditions.

### Scénarios à vérifier

Sort offensif sans cible, sort mixte soin/dégâts, invocation présente et deux configurations du même sort.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C07 — Planification d'un tour sous incertitude

Famille : Combat et sorts. Dépendances d’intégration : C03, C04, C05.

### Mission concrète

Concevoir un pilote de sélection qui pénalise l'incertitude, les actions déjà refusées et les cibles impossibles plutôt que maximiser seulement des dégâts estimés.

### Critères d’acceptation

Le plan retourne une explication vérifiable et sait passer à une alternative. Les décisions utilisent les informations réellement connues.

### Scénarios à vérifier

Ennemi hors vue, résistant air, faible PV incertains et état empêchant le sort principal.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C08 — Tacle, fuite et déplacement de combat

Famille : Combat et sorts. Dépendances d’intégration : C02, C05, N02.

### Mission concrète

Comparer chemin planifié, coût en PM, tacle et déplacement réellement confirmé. Gérer les déplacements tronqués ou refusés sans boucler sur la même cellule.

### Critères d’acceptation

La position de départ de l'action suivante est la cellule serveur. Le moteur différencie mouvement complet et partiel.

### Scénarios à vérifier

Tacle imprévu, poussée, téléportation, chemin bloqué après planification et déplacement annulé.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C09 — Matrice de classes et configurations réelles

Famille : Combat et sorts. Dépendances d’intégration : C01, C03, C06.

### Mission concrète

Construire des scénarios représentatifs pour Sadida, Sacrieur, Pandawa et Crâ avant d'étendre. Vérifier la rotation automatique et les réglages de profils par classe.

### Critères d’acceptation

Chaque classe a des cas de succès et de refus reproductibles. Les limites des classes non testées sont déclarées.

### Scénarios à vérifier

Soin, invocation, portée, attaque de zone, placement et sortie de combat pour chaque classe retenue.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## C10 — Replay déterministe du moteur combat

Famille : Combat et sorts. Dépendances d’intégration : C01, C02, C05, S07.

### Mission concrète

Créer un lecteur de traces expurgées qui rejoue les décisions sans transport réel. Séparer données reçues, état produit, décision et commande prévue.

### Critères d’acceptation

La même trace et la même configuration produisent le même plan. Les divergences entre versions sont lisibles par tour.

### Scénarios à vérifier

Comparer avant/après correction Minerbot, réponse doublée et événement reçu tardivement.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T01 — Accusés Turbo et identifiants réutilisés

Famille : Turbo et défis. Dépendances d’intégration : S05, S06.

### Mission concrète

Auditer les bornes de génération, les timestamps d'accusés et les fins de séquence. Conserver les régressions existantes et ajouter les collisions entre combats.

### Critères d’acceptation

Un ancien accusé ne supprime pas l'accusé d'une nouvelle séquence. Un accusé client courant évite un doublon.

### Scénarios à vérifier

Même action ID réutilisé, changement de tour, nouveau combat et fin de séquence avant ouverture de l'attente.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T02 — Barrières Turbo Socket et MITM

Famille : Turbo et défis. Dépendances d’intégration : T01, C05.

### Mission concrète

Comparer les chemins d'attente et de récupération après expiration. Définir une politique partagée là où les preuves le permettent, avec les différences de transport explicites.

### Critères d’acceptation

Les timeouts sont expliqués sans enchaînement aveugle d'actions. La fin de tour indique si la barrière était fermée.

### Scénarios à vérifier

Socket rapide, MITM retardé, barrière tardive, annulation et accusé absent.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T03 — Mesure des temps de combat

Famille : Turbo et défis. Dépendances d’intégration : T01, C10.

### Mission concrète

Instrumenter temps serveur, attente protocole, décision, rendu et déplacement. Corriger le double comptage des traces de lancement dans les statistiques de casts.

### Critères d’acceptation

Le rapport distingue durée combat, temps d'action et pauses. Une trace textuelle répétée n'est pas comptée comme une seconde action.

### Scénarios à vérifier

Comparer journal, messages serveur et compteurs sur trois combats et un replay hors ligne.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T04 — Validation des six options d'animation

Famille : Turbo et défis. Dépendances d’intégration : S06, R01.

### Mission concrète

Vérifier chaque bit du masque, sa disponibilité selon transport, son effet et sa restauration. Comparer les sources embarquées au commit externe étudié.

### Critères d’acceptation

Chaque option a une preuve distincte activée/désactivée. Le statut actif ne repose pas seulement sur la case cochée.

### Scénarios à vérifier

Activer une option à la fois, changer de profil et décharger sans affecter un autre PID.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T05 — Tactique compatible avec le client courant

Famille : Turbo et défis. Dépendances d’intégration : R01, R02.

### Mission concrète

Enquêter sur MapRenderer non résolu en partant du binaire/version et des recherches structurelles vérifiables. Ne pas masquer une résolution impossible par une case active.

### Critères d’acceptation

Une classe et des points d'accroche prouvés, ou un refus propre documenté. La restauration est testée avant validation.

### Scénarios à vérifier

Carte extérieure, entrée combat, sortie combat, changement carte et bascule F8 sur le bon client.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T06 — Tueur à vue et cible de début de tour

Famille : Turbo et défis. Dépendances d’intégration : C02, C07.

### Mission concrète

Tester la cible désignée serveur, le repli local, les égalités et le recalcul après une mort ou au tour suivant.

### Critères d’acceptation

La cible du tour précédent n'est pas conservée par erreur. La priorité serveur reste visible et reproductible.

### Scénarios à vérifier

A proche au premier tour, B proche au second, cible désignée contradictoire et mort en milieu de tour.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T07 — Nomade et coups de grâce de zone

Famille : Turbo et défis. Dépendances d’intégration : C03, C05, T06.

### Mission concrète

Vérifier l'anticipation du déplacement obligatoire quand plusieurs ennemis peuvent mourir ensemble. Revoir la décision après chaque action qui change la létalité.

### Critères d’acceptation

Le mouvement requis intervient avant le coup qui termine réellement le combat. Une estimation incertaine n'est pas présentée comme certaine.

### Scénarios à vérifier

Deux ennemis au-dessus de30%, sort de zone létal, budget PM réduit et adversaire hors zone.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T08 — Défis incompatibles et abandon explicable

Famille : Turbo et défis. Dépendances d’intégration : T06, T07, C07.

### Mission concrète

Modéliser les conflits entre défis, style de combat et survie. Donner une raison claire lorsque le bot renonce au bonus plutôt que poursuivre une impasse.

### Critères d’acceptation

Les décisions ne prétendent pas satisfaire un défi abandonné. L'utilisateur peut relire les contraintes et l'arbitrage.

### Scénarios à vérifier

Défi de mouvement bloqué, soin indispensable, allié invoqué et cible imposée inaccessible.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T09 — Tour bloqué, AFK et fin de tour

Famille : Turbo et défis. Dépendances d’intégration : S09, C04, T02.

### Mission concrète

Détecter absence de progrès et refus répétés sans considérer les messages comme activité utile. Recaler les budgets de tour et la politique d'arrêt.

### Critères d’acceptation

Un tour ne consomme pas des dizaines de tentatives identiques. La fin de tour conserve un motif exploitable.

### Scénarios à vérifier

Aucun sort castable, PA insuffisants, cible périmée, calcul trop long et annulation explicite.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## T10 — Benchmark Turbo comparable

Famille : Turbo et défis. Dépendances d’intégration : T02, T03, T04, Q04.

### Mission concrète

Préparer un protocole de comparaison Socket/MITM avec version, profil, monstres et fenêtres temporelles bornés. Rapporter médiane et dispersion sans confondre logs et combats.

### Critères d’acceptation

Les conclusions sont limitées aux scénarios réellement mesurés. Aucun pourcentage de gain fondé sur des sessions incompatibles.

### Scénarios à vérifier

Répétitions contrôlées, outlier réseau, refus et essais avec options visuelles séparées.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N01 — Sorties de mine et chemins réellement praticables

Famille : Navigation et récolte. Dépendances d’intégration : S01, N02.

### Mission concrète

Reproduire les refus autour de193331715/193331717/193200131 et les détours réussis. Comparer graphe de transitions, types de sortie et préconditions.

### Critères d’acceptation

Une route ne sélectionne plus systématiquement une transition reconnue invalide. Le contournement est distingué du correctif du graphe.

### Scénarios à vérifier

Sortie bord, soleil, porte et itinéraire alternatif observé, avec même carte mais autre cellule de départ.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N02 — Cellule autoritaire et accusés de déplacement

Famille : Navigation et récolte. Dépendances d’intégration : S05, S06.

### Mission concrète

Auditer les changements de carte, cellules confirmées et mouvements encore en cours. Vérifier qu'une arrivée acceptée ne devient pas une position fictive.

### Critères d’acceptation

La carte et la cellule confirmées viennent du serveur et de la bonne génération. Les mouvements en attente sont distingués.

### Scénarios à vérifier

Refus puis correction, réponse ancienne, cellule0 valide, changement de carte pendant déplacement.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N03 — Coût des trajets et raccourcis

Famille : Navigation et récolte. Dépendances d’intégration : N01, N02, E02.

### Mission concrète

Évaluer les décisions marche/zaap/Havre-Sac selon accès, coût, délai et historique de refus. Éviter un choix de marche qui enferme le bot dans une mine.

### Critères d’acceptation

L'itinéraire expose son coût estimé et ses préconditions. Les destinations impossibles n'entraînent pas de boucle de téléports.

### Scénarios à vérifier

PreferWalk vrai/faux, zaap inconnu, fonds insuffisants et sortie de bâtiment.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N04 — Obstacles temporaires et cooldown des arêtes

Famille : Navigation et récolte. Dépendances d’intégration : N01, N02.

### Mission concrète

Différencier obstacle de cellule, transition invalide durable et refus temporaire. Définir une mémoire bornée des échecs avec expiration utile.

### Critères d’acceptation

Un refus répété ne coûte pas la même séquence d'essais à chaque passage. Les chemins récupérés redeviennent utilisables.

### Scénarios à vérifier

Joueur occupant sortie, transition fausse, carte revisitée et disparition d'obstacle.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N05 — Annulation et remplacement de trajet

Famille : Navigation et récolte. Dépendances d’intégration : S05, S06, N02.

### Mission concrète

Suivre une annulation de script jusqu'aux commandes différées et aux ACKs. Empêcher le trajet précédent de reprendre après lancement d'un autre script.

### Critères d’acceptation

Aucun saut ni clic de l'ancien trajet n'est envoyé après son annulation confirmée. La nouvelle commande repart d'un état cohérent.

### Scénarios à vérifier

Remplacement pendant marche, pendant changement de carte, avant ACK et après reconnexion.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N06 — État de navigation expliqué dans l'UI

Famille : Navigation et récolte. Dépendances d’intégration : N02, N04, U03.

### Mission concrète

Afficher destination, prochaine étape, position confirmée, attente et raison de détour. Garder les détails bas niveau dans un panneau optionnel.

### Critères d’acceptation

L'utilisateur sait ce que le bot attend sans lire les journaux. Les erreurs mentionnent l'étape réellement fautive.

### Scénarios à vérifier

Refus de porte, replanification par zaap, attente serveur et destination annulée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N07 — Récoltes injoignables et carte intérieure

Famille : Navigation et récolte. Dépendances d’intégration : N02, F01.

### Mission concrète

Étudier les ressources marquées présentes mais hors portée ainsi que les recherches de ressources depuis une banque intérieure.

### Critères d’acceptation

Une ressource inaccessible ne bloque pas la ronde. Une recherche depuis l'intérieur peut proposer des destinations extérieures pertinentes.

### Scénarios à vérifier

Truites sans case atteignable, filon épuisé, atelier intérieur et frontière de monde.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N08 — Enregistreur et relecture de parcours

Famille : Navigation et récolte. Dépendances d’intégration : N02, L03.

### Mission concrète

Rendre les transitions enregistrées dépendantes des cartes observées, pas de positions déduites. Ajouter une validation hors ligne et une relecture arrêtant les étapes ambiguës.

### Critères d’acceptation

Le parcours enregistré reste lisible et signale portes, directions et conditions. Les doublons de carte sont conservés quand ils sont intentionnels.

### Scénarios à vérifier

Circuit à répétitions, téléport, transition refusée et fichier partiellement sauvegardé.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N09 — Navigation multi-compte sans mélange d'état

Famille : Navigation et récolte. Dépendances d’intégration : S06, N02.

### Mission concrète

Rejouer des déplacements simultanés de comptes sur les mêmes cartes et sur des cartes différentes. Auditer les singletons et caches non indexés par session.

### Critères d’acceptation

Aucun obstacle, cellule ou ACK d'un compte n'est attribué à un autre. Les reconnections restent isolées.

### Scénarios à vérifier

Deux comptes sur la même sortie, PID recyclé, monde intérieur commun et ordre inversé des réponses.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## N10 — Simulateur de graphe de voyage

Famille : Navigation et récolte. Dépendances d’intégration : N01, N03, N04, Q02.

### Mission concrète

Construire un laboratoire permettant de rejouer et visualiser les choix de route avec coût et refus injectés. Préparer des jeux d'essai de mines et banques.

### Critères d’acceptation

Le laboratoire identifie l'arête responsable d'un détour et rejoue une correction sans compte de jeu.

### Scénarios à vérifier

Mine bloquée, zaap manquant, composante déconnectée et cycles de transitions.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L01 — Contrat de survie des scripts après combat

Famille : Lua et MCP. Dépendances d’intégration : C01, S05.

### Mission concrète

Auditer gatherAll, travelTo, callbacks et retour de move. Transformer l'interruption par combat en attente d'issue sans masquer les vraies défaites.

### Critères d’acceptation

Une victoire reprise ne termine pas le script comme circuit inaccessible. Une défaite ou un résultat inconnu ne relance pas automatiquement une action.

### Scénarios à vérifier

Protecteur pendant récolte, callback tardif, banque déclenchée au retour et résultat inconnu.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L02 — Stratégie commune de reprise temporaire

Famille : Lua et MCP. Dépendances d’intégration : L01, S09, N04.

### Mission concrète

Extraire un contrat de pause, backoff, resynchronisation et progression utile pour les scripts pêche, mine et farm sans effacer leurs règles métier.

### Critères d’acceptation

Les pauses légitimes ne provoquent pas l'anti-blocage et les boucles sans progrès restent détectables. La durée restante est visible.

### Scénarios à vérifier

Toutes cartes en cooldown, réseau lent, ressource épuisée et opération non confirmée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L03 — Audit complet du catalogue Lua

Famille : Lua et MCP. Dépendances d’intégration : S04.

### Mission concrète

Comparer chaque API annoncée à sa liaison réelle et à son comportement. Produire une table automatique de capacités, arguments, modes et erreurs.

### Critères d’acceptation

Les stubs sont explicitement indisponibles, les signatures cohérentes et les exemples exécutables. Aucun faux succès pour API non branchée.

### Scénarios à vérifier

Méthodes avec self implicite, alias historiques, types incorrects et exemple minimal par module.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L04 — Hooks et événements à identité stable

Famille : Lua et MCP. Dépendances d’intégration : S06, L01.

### Mission concrète

Vérifier ordre, thread, durée et cardinalité de onFightEnd, onTurn, onPlacement et événements d'inventaire. Définir un contrat de livraison.

### Critères d’acceptation

Un callback ne s'exécute pas deux fois sur la même issue et ne touche pas la session suivante. Le dépassement de durée est visible.

### Scénarios à vérifier

Double fin de combat, annulation dans un hook, reconnexion et callback interrompu.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L05 — Éditeur Lua utile pour les scripts longs

Famille : Lua et MCP. Dépendances d’intégration : L03, U03.

### Mission concrète

Améliorer diagnostics, navigation, recherche, symboles et exemples adaptés au catalogue réel. Ajouter des liens du diagnostic vers l'appel concerné.

### Critères d’acceptation

Une erreur pointe la bonne ligne et explique une action possible. Le gros script pêche reste navigable sans gel.

### Scénarios à vérifier

Fichier100ko, alias inconnu, appel à méthode indisponible et modification du catalogue.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L06 — Tests de contrat sur le vrai host

Famille : Lua et MCP. Dépendances d’intégration : L03, L04, S03.

### Mission concrète

Créer des harness ciblés exécutant les bindings de production plutôt que des doubles qui réimplémentent la logique.

### Critères d’acceptation

Les tests échouent si un binding réel change de sémantique. Les données de catalogue viennent du même bundle que la production.

### Scénarios à vérifier

Colon vs point, valeurs nil, annulation et appel pendant combat sur plusieurs modules.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L07 — Journal structuré par run de script

Famille : Lua et MCP. Dépendances d’intégration : S05, L04.

### Mission concrète

Associer un run ID, étapes, actions et résultats pour rendre les reprises lisibles. Préparer un résumé de fin distinct du journal technique.

### Critères d’acceptation

Une session de script remplaçante n'hérite pas des compteurs de l'ancienne. Le curseur est clairement lié au run et à l'instance.

### Scénarios à vérifier

Arrêt/reprise, redémarrage application, résultat tardif et pagination de console.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L08 — Contrats de scripts partageables

Famille : Lua et MCP. Dépendances d’intégration : L03, L05.

### Mission concrète

Définir un paquet de script avec dépendances API, paramètres, version de catalogue, droits nécessaires et scénarios testés.

### Critères d’acceptation

L'import vérifie les prérequis et explique les incompatibilités sans exécuter le script. Les réglages sont séparés du code.

### Scénarios à vérifier

Script ancien, carte retirée, module absent et migration de paramètres.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L09 — Inspecteur d'étape et débogage sans action

Famille : Lua et MCP. Dépendances d’intégration : L04, L07, C10.

### Mission concrète

Créer une vue de l'étape courante avec variables utiles autorisées, destination et décision attendue. Étudier un mode simulation sur fixtures.

### Critères d’acceptation

L'inspecteur peut expliquer une attente sans modifier les globals ni envoyer une commande au serveur.

### Scénarios à vérifier

Pause de récolte, résultat combat en attente, banque et longue fonction de trajet.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## L10 — Qualification des scripts pêche/mine/farm

Famille : Lua et MCP. Dépendances d’intégration : L01, L02, N07, Q02.

### Mission concrète

Rejouer des cycles complets jusqu'au dépôt avec l'injection d'incidents ciblés. Corriger les incohérences restantes des trois scripts récents.

### Critères d’acceptation

Chaque script a une preuve de collecte, déplacement, banque et reprise, et une liste courte des limites réelles.

### Scénarios à vérifier

Banque à85%, circuit vide, protecteur gagné, destination inaccessible et arrêt utilisateur.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H01 — Ouverture HDV Socket visible immédiatement

Famille : HDV Socket. Dépendances d’intégration : S05, S06, U03.

### Mission concrète

Relier les événements d'ouverture du marché au bon bot et à une surface UI réelle. Afficher automatiquement son état, sans dépendre d'une fenêtre du client Dofus.

### Critères d’acceptation

Quand un bot Socket ouvre un HDV, l'application montre marché, mode et compte correspondants. Une fermeture ou reconnexion invalide cette vue.

### Scénarios à vérifier

Ouverture par script, par MCP et depuis UI, changement de bot sélectionné, fermeture serveur et mode vente/achat.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H02 — Toutes les offres et leurs jets

Famille : HDV Socket. Dépendances d’intégration : S05, H01.

### Mission concrète

Vérifier la correction du cache BuyerOffers par UID, GID, effets et lots. Traiter les listes vides transitoires sans écraser une réponse plus récente.

### Critères d’acceptation

Plusieurs Gelanos de jets différents restent distincts et consultables. Le prix moyen n'est jamais substitué à une offre achetable.

### Scénarios à vérifier

Gelano sans PA, PA/vitalité, PA/PM, plusieurs lots et unfollow/refollow avec liste vide intermédiaire.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H03 — Achat de l'offre réellement choisie

Famille : HDV Socket. Dépendances d’intégration : H02, S06, S08.

### Mission concrète

Raccorder la sélection UI/MCP au contrat UID-prix-quantité-effets-génération. Vérifier le rechargement avant achat et l'inventaire reçu.

### Critères d’acceptation

L'objet acheté correspond aux jets inspectés. Une offre modifiée est refusée sans recherche automatique d'une autre variante.

### Scénarios à vérifier

UID remplacé, prix changé, jets changés, budget dépassé et timeout après envoi.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H04 — Scanner acheteur fiable et annulable

Famille : HDV Socket. Dépendances d’intégration : H02, N05.

### Mission concrète

Corriger les changements de catégories et les réponses tardives. Définir un scan borné sans afficher rupture de stock quand l'API n'a pas répondu.

### Critères d’acceptation

Un scan fournit une couverture et une fraîcheur explicites. Arrêter le scan annule les recherches différées.

### Scénarios à vérifier

Catégorie répétée, changement rapide, jzn vide avant rempli et marché fermé pendant recherche.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H05 — Vente et corrélation d'annonce

Famille : HDV Socket. Dépendances d’intégration : S05, S08, H02.

### Mission concrète

Vérifier pose objet, UID d'annonce, quantité, prix, taxe et disparition du sac. Éviter la confirmation sur un accusé non lié.

### Critères d’acceptation

Une vente publiée est reliée à l'instance exacte et au débit correct. Une annonce incertaine est mise à rapprocher avant nouvel essai.

### Scénarios à vérifier

Même GID avec autres jets, quantité partielle, taxes et absence de réponse après injection.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H06 — Remise en vente sans duplication

Famille : HDV Socket. Dépendances d’intégration : H05, S08.

### Mission concrète

Auditer reprice, retrait puis republication et états indéterminés. Afficher un dossier de rapprochement avec les preuves disponibles.

### Critères d’acceptation

Aucune répétition automatique après résultat ambigu. L'opérateur peut rapprocher annonce courante et inventaire sans supposer un succès.

### Scénarios à vérifier

Retrait confirmé mais republication absente, nouvel UID, taxe prélevée et reconnexion.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H07 — UI de comparaison des jets

Famille : HDV Socket. Dépendances d’intégration : H02, U02, U05.

### Mission concrète

Créer une comparaison lisible mettant en avant PA/PM/PO, vitalité, élément, dommages et puissance. Proposer filtres et compromis prix/jet.

### Critères d’acceptation

Un Gelano sans PA est impossible à confondre avec un PA. La sélection reste stable pendant un rafraîchissement de prix.

### Scénarios à vérifier

Vingt variantes, attribut manquant, malus, overmage et tri pendant réponse réseau.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H08 — Vue vente synchronisée avec le dialogue

Famille : HDV Socket. Dépendances d’intégration : H01, H05, S08.

### Mission concrète

Afficher annonces, inventaire éligible, slots et taxes du marché réellement ouvert. Signaler les données de cache et les opérations en cours.

### Critères d’acceptation

L'utilisateur n'a pas une fausse vue temps réel après fermeture. Une action ne s'applique pas à un ancien marché.

### Scénarios à vérifier

Fermeture, changement de compte, prix reçu tardivement et vente en parallèle d'un script.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H09 — API MCP du marché pour agents

Famille : HDV Socket. Dépendances d’intégration : H02, H03, S05, B03.

### Mission concrète

Concevoir des outils bornés de liste, inspection, devis et achat exact. Les réponses donnent session, marché, offre et fraîcheur au lieu de demander du parsing de console.

### Critères d’acceptation

Un agent peut préparer un choix d'équipement sans acheter. L'achat nécessite une référence vérifiable et retourne sa vraie confirmation.

### Scénarios à vérifier

Lecture seule, devis périmé, UID inconnu, résultat indéterminé et mauvais bot.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## H10 — Validation complète HDV Socket et MITM

Famille : HDV Socket. Dépendances d’intégration : H03, H05, H06, H08, Q04.

### Mission concrète

Organiser une matrice d'essais représentatifs achat/vente/reprice/fermeture et comparer les deux transports sur les mêmes contrats.

### Critères d’acceptation

Chaque statut revendiqué est soutenu par inventaire et événement serveur appropriés. Les tests n'effectuent pas de dépenses importantes pour produire une preuve.

### Scénarios à vérifier

Achat borné, vente d'une ressource autorisée, retrait, timeout simulé et sessions séparées.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E01 — Prix de lot, prix unitaire et prix moyen

Famille : Économie. Dépendances d’intégration : H02.

### Mission concrète

Auditer les conversions et les libellés, y compris prix fractionnaires. Préserver la distinction lot achetable, moyenne serveur et saisie utilisateur.

### Critères d’acceptation

Aucune ligne n'annonce comme prix d'achat une moyenne ou un lot divisé avec un arrondi trompeur.

### Scénarios à vérifier

Lot10 moins cher que100, valeur absente, moyenne4,2M et petit prix fractionnaire.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E02 — Coût complet d'une activité

Famille : Économie. Dépendances d’intégration : E01, S07.

### Mission concrète

Modéliser trajet, banque, taxes, consommables, régénération, défaites et temps sans cible. Identifier les coûts réellement observables et ceux estimés.

### Critères d’acceptation

Le coût complet expose ses hypothèses. Un retrait bancaire ou un achat de stuff n'est pas compté comme revenu de farm.

### Scénarios à vérifier

Les sessions récentes des trois bots et un jeu de données entièrement synthétique contrôlé.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E03 — Liquidité et ventes réalisées

Famille : Économie. Dépendances d’intégration : H05, E01.

### Mission concrète

Séparer annonce, disparition d'annonce, vente confirmée et variation de prix. Préparer des indicateurs prudents de liquidité par serveur et fenêtre temporelle.

### Critères d’acceptation

Le score ne prétend pas connaître un volume de transactions non observé. Les prix proposés et réalisés restent séparés.

### Scénarios à vérifier

Annonce annulée, vente partielle, reprice et absence prolongée de données.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E04 — Classement des farms par preuves

Famille : Économie. Dépendances d’intégration : C03, E01, E02, E03.

### Mission concrète

Combiner drops catalogués, durée réelle de combat, déplacements et prix récents. Comparer Boufton Noir, Pious, larves et Prespics avec incertitude explicite.

### Critères d’acceptation

Le classement montre le nombre d'observations et peut refuser de trancher faute de données. Le plus gros prix unitaire ne suffit pas.

### Scénarios à vérifier

Rareté élevée, drop fréquent peu cher, combats lents et échantillon de trois victoires seulement.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E05 — Séries temporelles et anomalies de marché

Famille : Économie. Dépendances d’intégration : H02, E01.

### Mission concrète

Créer un stockage borné par serveur, objet, variante et taille de lot. Détecter ruptures et valeurs aberrantes sans effacer la preuve brute utile.

### Critères d’acceptation

Une anomalie reste inspectable et n'influence pas silencieusement une recommandation. La rétention est bornée.

### Scénarios à vérifier

Prix multiplié100, changement de serveur, trou de données et modification de lot disponible.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E06 — Optimiseur d'équipement sous budget

Famille : Économie. Dépendances d’intégration : H03, H07, C03.

### Mission concrète

Formaliser les exigences du stuff Foragerbot en contraintes et compromis. Énumérer des offres exactes, pas une liste de prix moyens.

### Critères d’acceptation

Le panier respecte budget et caractéristiques obligatoires. Le modèle explique les compromis et la fraîcheur du devis.

### Scénarios à vérifier

PA obligatoire, set bonus, deux anneaux, malus vitalité, prix périmé et monture.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E07 — Provenance et fraîcheur des données économiques

Famille : Économie. Dépendances d’intégration : E01, E05.

### Mission concrète

Associer à chaque prix sa source, son serveur, son instant, son unité et son niveau de preuve. Prévoir migration et invalidation du cache.

### Critères d’acceptation

Un conseil peut remonter jusqu'au relevé qui le justifie. Les agrégats ne mélangent pas silencieusement plusieurs mondes.

### Scénarios à vérifier

Moyenne serveur, offre achetable, import externe, saisie manuelle et donnée sans date.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E08 — Collecte passive de marché et dofus-prospection

Famille : Économie. Dépendances d’intégration : E07, R03.

### Mission concrète

Étudier la séparation collecte, stockage et analyse du dépôt Python. Prototyper un import d'observations vers un contrat Givre, sans double capture sur les sessions actives.

### Critères d’acceptation

Rapport sourcé et adaptateur de fixtures, avec limites sur la visibilité réelle des prix et des ventes.

### Scénarios à vérifier

Fichier d'observations expurgées, schéma ancien, objet inconnu et événement répété.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E09 — Taux d'ogrines et coût d'abonnement

Famille : Économie. Dépendances d’intégration : E07, R05.

### Mission concrète

Étudier Ogrine-Rates_Server pour modéliser une conversion datée et sourcée. Construire une vue de coût sans achat ni renouvellement automatique.

### Critères d’acceptation

Le taux affiche serveur/source/date et reste une indication. Aucun abonnement n'est acheté pour un test.

### Scénarios à vérifier

Taux manquant, source en panne, variations brusques et comparaison de périodes.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## E10 — Tableau économique compréhensible

Famille : Économie. Dépendances d’intégration : E02, E03, E07, U03.

### Mission concrète

Présenter revenu réalisé, valeur estimée, coûts et stock avec définitions courtes. Offrir une fenêtre par run au lieu des seuls cumuls journaliers.

### Critères d’acceptation

L'utilisateur peut distinguer rentabilité observée, potentiel et trésorerie sans lire des journaux. Les unités sont cohérentes.

### Scénarios à vérifier

Retrait1M Minerbot, achat1,453M Foragerbot, dépôt de ressources et jour contenant plusieurs scripts.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P01 — Recette actuelle de clé du donjon Bouftou

Famille : Production et donjons. Dépendances d’intégration : S01, F03.

### Mission concrète

Résoudre l'ID de la clé, sa recette, les niveaux métiers, l'atelier et les quantités depuis la base actuelle. Ne pas réutiliser une recette historique sans contrôle.

### Critères d’acceptation

Une fiche de production indique sources, recette, version et prérequis réellement connus. Aucune fabrication n'est lancée par ce lot.

### Scénarios à vérifier

Comparer catalogue installé, métier du personnage, recette intermédiaire et objet consommé à l'entrée du donjon.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P02 — Plan d'approvisionnement pour cent clés

Famille : Production et donjons. Dépendances d’intégration : P01, E07, F02.

### Mission concrète

Calculer besoins bruts, ingrédients disponibles, manquants et provenance de récolte/drop/achat. Éviter de double compter les stocks équipés ou déjà réservés.

### Critères d’acceptation

Le plan varie correctement avec quantité et stock, distingue banque en cache et stock confirmé et respecte les réservations.

### Scénarios à vérifier

Zéro, une, cent clés, recette avec intermédiaire et stock bancaire inconnu.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P03 — Chaîne récursive craft ou achat

Famille : Production et donjons. Dépendances d’intégration : P01, E01, E07.

### Mission concrète

Étudier Dofus-Craft et construire un optimiseur de recettes avec alternatives acheter/crafter, rendements et arrondis entiers.

### Critères d’acceptation

La décision est traçable par ingrédient, sans cycles infinis ni double utilisation du même stock. Les prix manquants sont explicites.

### Scénarios à vérifier

Recette partagée, quantité par lot, cycle de données malformé et ingrédient sans prix.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P04 — Réservations de stock pour la production

Famille : Production et donjons. Dépendances d’intégration : S08, P02, F02.

### Mission concrète

Créer un contrat de stock réservé pour un lot de clés pendant que les bots continuent à récolter ou déposer. Inclure les matériaux consommés et produits confirmés.

### Critères d’acceptation

Deux plans ne peuvent consommer la même ressource. Annulation et échec libèrent seulement les réservations encore valides.

### Scénarios à vérifier

Deux recettes simultanées, dépôt bancaire, quantité modifiée et résultat de craft partiel.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P05 — Exécution du craft par lots vérifiés

Famille : Production et donjons. Dépendances d’intégration : P01, P04, F03.

### Mission concrète

Raccorder un plan à l'atelier réel avec pose des UID, quantités et nombre de fabrications confirmés. Gérer plusieurs piles et résultat partiel.

### Critères d’acceptation

Le nombre de clés obtenu et les ressources consommées sont rapprochés. Aucun Ready envoyé après pose non confirmée.

### Scénarios à vérifier

UID incorrect, mauvais côté d'échange, multi-piles, artisan indisponible et fermeture en cours.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P06 — Entrée donjon, clés et trousseau

Famille : Production et donjons. Dépendances d’intégration : P01, N02, F07.

### Mission concrète

Auditer dialogue d'entrée, clé normale, trousseau et refus. Identifier les réponses serveur et la consommation exacte.

### Critères d’acceptation

Une entrée est prouvée par carte/salle et coût de clé, pas par sélection de réponse PNJ. Le refus garde le stock cohérent.

### Scénarios à vérifier

Clé absente, trousseau disponible, dialogue modifié et entrée confirmée après retard.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P07 — Parcours complet du donjon Bouftou

Famille : Production et donjons. Dépendances d’intégration : P06, C09, N02.

### Mission concrète

Construire un scénario de toutes les salles avec groupes, déplacements et conditions de fin. Préparer un test borné avant boucle illimitée.

### Critères d’acceptation

Un passage fini est défini par récompense/sortie observée et état final cohérent. Les salles inconnues ne sont pas sautées.

### Scénarios à vérifier

Combat long, refus de porte, défaite, salle sans groupe et sortie vers carte inattendue.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P08 — Boucle donjon avec réapprovisionnement

Famille : Production et donjons. Dépendances d’intégration : P02, P05, P07, S05.

### Mission concrète

Concevoir la machine d'état farm-matériaux, stockage, craft, entrée, salles, sortie, banque et reprise. Chaque transition doit être confirmable et annulable.

### Critères d’acceptation

Une boucle ne consomme pas des clés en aveugle. Elle s'arrête explicitement sur résultat inconnu et reprend les étapes seulement prouvées.

### Scénarios à vérifier

Plus de clés, pods pleins, inventaire désynchronisé, reconnect et annulation entre deux passages.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P09 — Économie du cycle farm-clés-donjons

Famille : Production et donjons. Dépendances d’intégration : P03, P07, E02, E03, E04.

### Mission concrète

Comparer vente de la laine, vente des clés et consommation des clés en donjon. Inclure temps de craft, coût d'atelier, butin et risques.

### Critères d’acceptation

Le conseil peut recommander de ne pas fabriquer. Les récompenses non observées sont des hypothèses affichées.

### Scénarios à vérifier

Cent clés théoriques, dix passages observés, prix de laine en baisse et coût de trajet dominant.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## P10 — Pilotage utilisateur et MCP de la production

Famille : Production et donjons. Dépendances d’intégration : P04, P08, P09, H09.

### Mission concrète

Créer un devis de production et un suivi de lot avec objectifs, budget et compte assigné. Séparer préparation, validation et exécution de la chaîne.

### Critères d’acceptation

L'utilisateur voit besoin en ingrédients, étapes terminées et prochaine action. Le MCP peut reprendre un lot par ID sans doublonner.

### Scénarios à vérifier

Devis périmé, budget insuffisant, autre compte, arrêt partiel et relance du même identifiant.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U01 — Trois directions visuelles sans gris dominant

Famille : Interface. Dépendances d’intégration : S04.

### Mission concrète

Explorer trois propositions cohérentes : encre/pétrole-émeraude, sombre nuit/cuivre et clair ivoire/forêt. Produire des prototypes d'écrans réels, pas seulement une palette.

### Critères d’acceptation

Chaque proposition couvre tableau de bord, carte et HDV avec états chargement/erreur. L'utilisateur peut comparer les directions.

### Scénarios à vérifier

Captures à1280x800 et1920x1080, contraste, densité et longue liste de bots.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U02 — Tokens de thème et hiérarchie visuelle

Famille : Interface. Dépendances d’intégration : U01.

### Mission concrète

Définir fonds, surfaces, bordures, accents, typographie, espacements, rayons et états. Remplacer progressivement les valeurs dispersées par des ressources cohérentes.

### Critères d’acceptation

Un changement de thème ne casse pas les états désactivés ou les couleurs sémantiques. Le gris n'est pas simplement remplacé partout par une couleur saturée.

### Scénarios à vérifier

Inventaire, marché, erreur, sélection, hover, focus clavier et traduction longue.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U03 — Navigation et densité par tâche

Famille : Interface. Dépendances d’intégration : S04, U01.

### Mission concrète

Repenser l'organisation selon superviser, jouer/voir, préparer, automatiser et diagnostiquer. Réduire les panneaux permanents et utiliser une divulgation progressive.

### Critères d’acceptation

Les actions fréquentes sont visibles sans une forêt de réglages. Les diagnostics restent accessibles sans dominer l'écran.

### Scénarios à vérifier

Superviser trois bots, ouvrir le marché d'un bot Socket, vérifier un combat et modifier une ronde.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U04 — Tableau de bord des bots centré sur l'action

Famille : Interface. Dépendances d’intégration : S09, U02, U03.

### Mission concrète

Afficher activité, état confirmé, problème utile et prochaine étape avec une densité raisonnable. Concevoir des tailles compactes et détaillées.

### Critères d’acceptation

La situation des trois bots est compréhensible en cinq secondes. Un arrêt volontaire ne ressemble pas à une panne.

### Scénarios à vérifier

Bots nombreux, reconnexion, combat, banque, attente ressource et une opération incertaine.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U05 — Composants de listes riches réutilisables

Famille : Interface. Dépendances d’intégration : U02, H07, Q05.

### Mission concrète

Créer des lignes d'objets, badges, panneaux de propriétés et comparateurs cohérents. Prévoir virtualisation, sélection stable et infobulles réelles.

### Critères d’acceptation

Les objets et offres HDV utilisent les mêmes conventions sans perdre les différences de contexte. Les listes volumineuses restent fluides.

### Scénarios à vérifier

Dix mille lignes, mises à jour rapides, jets négatifs, noms longs et icône absente.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U06 — Commandes, raccourcis et palette d'actions

Famille : Interface. Dépendances d’intégration : S06, U03.

### Mission concrète

Concevoir une palette contextuelle recherchant bot, vue et action disponible. Afficher les préconditions et éviter les raccourcis dangereux hors contexte.

### Critères d’acceptation

Une commande indique le compte ciblé et ne dépend pas d'une ancienne sélection. Le clavier couvre les parcours principaux.

### Scénarios à vérifier

Changer de bot pendant une recherche, dialogue ouvert, commande indisponible et raccourci en édition Lua.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U07 — États vides, chargement et erreurs utiles

Famille : Interface. Dépendances d’intégration : S05, U02.

### Mission concrète

Remplacer les placeholders ambigus et faux zéros par des messages courts fondés sur l'état réel. Fournir reprise ou diagnostic adapté au contexte.

### Critères d’acceptation

Une banque non chargée ne dit pas vide et un marché sans réponse ne dit pas rupture de stock. Une erreur ne disparaît pas avant sa résolution.

### Scénarios à vérifier

Connexion lente, catalogue absent, mode non supporté, cache périmé et timeout après envoi.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U08 — Accessibilité et traduction des nouveaux écrans

Famille : Interface. Dépendances d’intégration : U02, U03, U05.

### Mission concrète

Tester clavier, focus, lisibilité, taille de police, contraste et réduction d'animations. Réviser FR/EN/ES avec clés uniques et formats culturels.

### Critères d’acceptation

La hiérarchie ne repose pas uniquement sur couleur ou survol. Les nouveaux composants restent utilisables en texte agrandi.

### Scénarios à vérifier

Police150%, noms longs espagnols, navigation sans souris et thème clair/sombre.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U09 — Migration de thème et préférences sans perte

Famille : Interface. Dépendances d’intégration : U02, U03, S06.

### Mission concrète

Conserver thème, densité, panneaux, colonnes et raccourcis par utilisateur en versionnant les préférences. Éviter d'écrire un état transitoire lors de fermeture.

### Critères d’acceptation

Un ancien profil s'ouvre proprement et les valeurs inconnues ne cassent pas l'application. Un reset ciblé est possible.

### Scénarios à vérifier

Préférence partielle, ancienne version, écran débranché et interruption pendant sauvegarde.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## U10 — Revue UI indépendante sur vrais parcours

Famille : Interface. Dépendances d’intégration : U04, U05, U06, U07, M03, H01.

### Mission concrète

Évaluer les propositions retenues avec les tâches concrètes du projet. Consigner les irritants avec captures, nombre d'étapes et proposition de correction.

### Critères d’acceptation

La revue produit des constats vérifiables plutôt qu'un jugement esthétique vague. La direction visuelle reste cohérente entre écrans.

### Scénarios à vérifier

OuvrirHDVSocket, choisirGelanoPA, suivreprotecteur, comprendrepausemine et modifierbanque85%.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M01 — Carte épurée par défaut

Famille : Carte et combat visuel. Dépendances d’intégration : U01, U03.

### Mission concrète

Réduire la densité de l'onglet carte : montrer terrain, bot, entités utiles et activité actuelle. Déplacer données techniques et détails de ressources vers un inspecteur à la demande.

### Critères d’acceptation

Les informations indispensables restent lisibles sans masquer la carte. L'utilisateur active les couches qu'il veut.

### Scénarios à vérifier

Carte dense de ressources, zone urbaine avec joueurs, intérieur de mine et écran compact.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M02 — Pipeline de rendu carte et invalidation

Famille : Carte et combat visuel. Dépendances d’intégration : M01, Q05.

### Mission concrète

Auditer IsoMapControl, RouteMapControl et leurs données. Réduire les recalculs, allocations et invalidations globales pendant déplacements et combats.

### Critères d’acceptation

La carte ne bloque pas le thread UI lors des mises à jour. Les couches inchangées sont réutilisées et leur cache est borné.

### Scénarios à vérifier

Carte pleine, changement de bot rapide, événements fréquents et mesure de frame time.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M03 — Carte de combat vraiment lisible

Famille : Carte et combat visuel. Dépendances d’intégration : C02, C05, M01, U02.

### Mission concrète

Concevoir placement, unités, PV/PA/PM, cible, zones et état de tour avec une hiérarchie nette. Montrer les informations confirmées et réserver l'analyse avancée à l'inspecteur.

### Critères d’acceptation

Une personne comprend qui joue et quelle action se prépare sans lire les logs. La vue réagit correctement à poussées et morts.

### Scénarios à vérifier

Trois ennemis superposés, invocation, unité invisible, changement de tour et combatSocket.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M04 — Portées et zones interactives fiables

Famille : Carte et combat visuel. Dépendances d’intégration : C03, C04, M03.

### Mission concrète

Ajouter un survol de sort affichant portée utile, obstacles, cellules candidates et zone touchée en réutilisant les mêmes contrats que le moteur.

### Critères d’acceptation

La prévisualisation n'annonce pas une case légale que le moteur refuse pour une règle connue. Les inconnues sont signalées.

### Scénarios à vérifier

Zone orientée, portée modifiable, ligne de vue, minimum de portée et cases non ciblables.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M05 — Sélection et inspecteur d'entités

Famille : Carte et combat visuel. Dépendances d’intégration : M01, M03, S06.

### Mission concrète

Unifier hit-testing, choix d'entités empilées, infobulles et panneau de détails. Limiter les étiquettes permanentes.

### Critères d’acceptation

Les clics sélectionnent ce qui est affiché et les données restent liées à la bonne entité après mouvement.

### Scénarios à vérifier

Plusieurs unités même cellule, icônes petites, disparition d'entité et zoom.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M06 — Sprites et looks Socket depuis données locales

Famille : Carte et combat visuel. Dépendances d’intégration : R04, M02, U01.

### Mission concrète

Étudier SuperDofus et les renderers liés pour proposer des personnages plus lisibles que des points génériques. Comparer rendu natif, atlas pré-rendu et rendu hors-processus.

### Critères d’acceptation

Prototype isolé avec coût mesuré, licences et cache explicites. Aucun moteur Godot embarqué dans Avalonia sans justification de coût.

### Scénarios à vérifier

Trois looks, recoloration, monture/cavalier, animation et asset absent.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M07 — Carte du monde, recherche et itinéraires

Famille : Carte et combat visuel. Dépendances d’intégration : N03, M01, U03.

### Mission concrète

Proposer une recherche de lieu, ressource, atelier et accès utile, avec aperçu du trajet. Éviter l'accumulation d'icônes permanentes.

### Critères d’acceptation

La recherche explique la destination et ses prérequis. La carte du monde ne révèle pas une position confirmée imaginaire.

### Scénarios à vérifier

Banque intérieure, HDV ressources/équipement, étage différent et coordonnées ambiguës.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M08 — Timeline et replay visuel de combat

Famille : Carte et combat visuel. Dépendances d’intégration : C10, M03.

### Mission concrète

Construire une timeline de tours/actions reliée au replay déterministe. Permettre de sélectionner une action et voir avant/après, refus et données de décision.

### Critères d’acceptation

La timeline reste distincte du combat live et n'envoie jamais d'action serveur. Les trous de données sont visibles.

### Scénarios à vérifier

Saut temporel, action rejetée, soin ignoré dans ancienneversion et comparaison de deux moteurs.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M09 — Éditeur de trajets et couches de ressources

Famille : Carte et combat visuel. Dépendances d’intégration : N07, N08, M01.

### Mission concrète

Relier l'enregistreur à une carte où l'utilisateur peut revoir les étapes et conditions. Les ressources se filtrent par métier, niveau et utilité.

### Critères d’acceptation

Un parcours est révisable sans exposer tous les IDs et détails techniques dans le flux normal.

### Scénarios à vérifier

Circuit répété, banque, porte, filtre métier et ressource épuisée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## M10 — Rendu multi-échelle et budgets graphiques

Famille : Carte et combat visuel. Dépendances d’intégration : M02, M05, Q05.

### Mission concrète

Mesurer résolution, zoom, DPI, mémoire et batterie sur les différentes cartes. Fixer des budgets et une dégradation progressive de détail.

### Critères d’acceptation

Le zoom garde les éléments importants lisibles, les caches ne croissent pas sans borne et le rendu se dégrade proprement.

### Scénarios à vérifier

4K, DPI150%, fenêtre minimale, vingt minutes de navigation et rendu sans accélération.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F01 — Récolte confirmée et compteur exact

Famille : Stocks, banque et forge. Dépendances d’intégration : S05, N02.

### Mission concrète

Vérifier les messages de ressource utilisée, fin d'interaction, quantité reçue et poids. Éviter de traiter gatherAll=true comme preuve d'une collecte.

### Critères d’acceptation

La récolte terminée est associée à une ressource et une variation confirmées. Les tentatives injoignables restent séparées.

### Scénarios à vérifier

Ressource épuisée, protecteur, gain nul, inventaire plein et deux événements identiques.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F02 — Inventaire et banque à fraîcheur explicite

Famille : Stocks, banque et forge. Dépendances d’intégration : S05, S06.

### Mission concrète

Auditer le cache des objets, effets, quantités et snapshots bancaires. Différencier inconnu, partiel, ancien, chargé et vide confirmé.

### Critères d’acceptation

Zéro piles après reconnexion n'est jamais présenté comme une banque vide. Les objets équipés ne sont pas comptés disponibles au craft.

### Scénarios à vérifier

Reconnexion, banque non ouverte, modification partielle, split de pile et déplacement d'objet.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F03 — Craft multi-piles et nombre réellement exécuté

Famille : Stocks, banque et forge. Dépendances d’intégration : S08, F02.

### Mission concrète

Renforcer les corrections récentes de pose atelier, compte de crafts et consommation. Exécuter le vrai service avec transport simulé puis essai autorisé borné.

### Critères d’acceptation

Une recette ne part pas tant que UID/quantité/côté et nombre de fabrications ne concordent pas. Un résultat partiel reste visible.

### Scénarios à vérifier

Objet direct vs inventaire, mauvaisepile, mauvaiscôté, nombre différent et fermetureatelier.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F04 — Forgemagie et puits observés

Famille : Stocks, banque et forge. Dépendances d’intégration : S08, F02, F03.

### Mission concrète

Auditer la séparation conseil, choix de rune, application et résultat. Comparer changements d'effets et estimation de puits sans faire passer une estimation pour une vérité.

### Critères d’acceptation

L'outil expose son niveau de confiance et les limites de calcul. L'application d'une rune dépend de la bonne instance d'objet.

### Scénarios à vérifier

Échec neutre/critique, effet absent, overmage, objet remplacé et réponse tardive.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F05 — Budget et annulation de forge

Famille : Stocks, banque et forge. Dépendances d’intégration : F04, E02, S08.

### Mission concrète

Vérifier budget global, stock de runes et compteur d'actions sur annulation/reprise. Empêcher plusieurs séances d'utiliser le même budget.

### Critères d’acceptation

Le plafond est évalué avant l'action et les consommations incertaines restent à rapprocher. L'annulation empêche la rune suivante.

### Scénarios à vérifier

Timeout après pose, achat de rune concurrent, budget juste et reprise après crash.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F06 — Métiers et plans de progression réels

Famille : Stocks, banque et forge. Dépendances d’intégration : F01, F02, N07.

### Mission concrète

Rendre visibles niveaux métiers, XP connue et paliers sans déduire l'XP métier de l'XP personnage. Proposer un plan tenant compte des accès confirmés.

### Critères d’acceptation

Les gains et prérequis sont sourcés. Une carte du catalogue n'est pas déclarée accessible par sa seule présence.

### Scénarios à vérifier

Mineur130, pêcheur200, métier inconnu, île verrouillée et palier franchi pendant run.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F07 — Dialogue PNJ et choix de réponse

Famille : Stocks, banque et forge. Dépendances d’intégration : S05, N02.

### Mission concrète

Auditer les recherches de PNJ, actions numériques et réponses de dialogue. Gérer l'ouverture par interactif et le changement de contexte sans faux succès.

### Critères d’acceptation

Une réponse n'est considérée réussie qu'avec la transition attendue. Les hôtels de vente extérieurs ne sont pas confondus avec ateliers intérieurs.

### Scénarios à vérifier

HDV créaturesBonta, banquier, entréedonjon, PNJ introuvable et dialogue interrompu.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F08 — Chasse au trésor de bout en bout

Famille : Stocks, banque et forge. Dépendances d’intégration : F07, N02, C09.

### Mission concrète

Qualifier départ, indice, navigation, validation, combat et récompense sur une chasse bornée. Distinguer indice introuvable et faute de position.

### Critères d’acceptation

Le module peut reprendre une chasse connue et expliquer une étape bloquée. Une recherche d'indice ne vaut pas validation serveur.

### Scénarios à vérifier

Indice ambigu, direction, étage, échec de validation, coffre et inventaire plein.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F09 — Capture d'âmes et synchronisation Metamob

Famille : Stocks, banque et forge. Dépendances d’intégration : F02, R06, H09.

### Mission concrète

Auditer pierre, état de capture, résultat et inventaire, puis préparer une synchronisation explicite des collections. Étudier le client typé metamob.api.

### Critères d’acceptation

Une capture n'est annoncée qu'après preuve. La synchronisation peut être prévisualisée et n'écrase pas les données externes sans choix.

### Scénarios à vérifier

Monstre non capturable, pierre insuffisante, doublon, API en panne et conflit de collection.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## F10 — Ateliers et banques dans les scripts

Famille : Stocks, banque et forge. Dépendances d’intégration : F02, F03, F07, L03.

### Mission concrète

Unifier les contrats d'ouverture/fermeture et l'emploi de leaveDialog pour éviter le masquage du module bank par la fonction bank().

### Critères d’acceptation

Une fonction de trajet bank n'empêche pas la fermeture du vrai dialogue. La reprise de ronde utilise la carte réellement atteinte.

### Scénarios à vérifier

Dépôtglobal, fraisbancaires, banqueferméeparserveur et craft annulé pendant dépôt.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A01 — Connexion et choix de personnage précis

Famille : Comptes et équipes. Dépendances d’intégration : S05, S06.

### Mission concrète

Auditer les commandes de connexion, serveur demandé et personnage cible, en Socket et MITM. Vérifier que les erreurs finales arrivent dans l'UI et MCP.

### Critères d’acceptation

Le compte demandé rejoint le personnage demandé ou retourne une cause précise. Pas de sélection implicite basée sur la dernière ligne visible.

### Scénarios à vérifier

Deuxpersonnages, serveurindisponible, abonnementinconnu et demande remplacée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A02 — Reconnexion complète après cellule perdue

Famille : Comptes et équipes. Dépendances d’intégration : A01, S06, N02.

### Mission concrète

Reconstituer pourquoi reconnect simple diffère de disconnect puis connect. Préparer une reprise qui invalide tous les états nécessaires au lieu d'exiger une manipulation manuelle.

### Critères d’acceptation

La cellule confirmée, les interactifs et le script repartent dans une même génération. Les anciens accusés ne sont plus valides.

### Scénarios à vérifier

Carteintérieure, inactivitéserveur, microcoupure et reconnexionpendanttrajet.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A03 — Équipes, chef et suiveurs

Famille : Comptes et équipes. Dépendances d’intégration : S05, S06, C05.

### Mission concrète

Qualifier déplacement, regroupement, combat, placement et départ d'un membre. Les accusés et ordres doivent être liés à une équipe et sa version.

### Critères d’acceptation

Une absence de suiveur ne devient pas un succès de regroupement. Le changement de chef annule les anciens ordres différés.

### Scénarios à vérifier

Suiveur lent, membre déconnecté, arrivée tardive et combat lancé avant regroupement.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A04 — Planning et horaires fiables

Famille : Comptes et équipes. Dépendances d’intégration : S05, S06, L07.

### Mission concrète

Auditer fuseaux, changements d'heure, scripts en cours et fin de session après combat. Rendre les actions futures lisibles et annulables.

### Critères d’acceptation

Un planning ne lance pas deux runs du même bot et n'interrompt pas un combat pour respecter une minute exacte.

### Scénarios à vérifier

Changementheureoctobre, passagejour, redémarrageapplication et tâche déjà lancée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A05 — Sélection de bot et commandes contextuelles

Famille : Comptes et équipes. Dépendances d’intégration : S06, U03.

### Mission concrète

Tracer les commandes UI qui lisent un bot sélectionné mutable. Capturer l'identité cible au déclenchement et revalider les préconditions à l'envoi.

### Critères d’acceptation

Cliquer sur un autre bot pendant une attente ne redirige pas l'action. L'UI montre toujours la cible originale.

### Scénarios à vérifier

Ouverturemarché, équipement, stopscript et dialogue de configuration pendant changement de sélection.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A06 — Isolation des données entre comptes

Famille : Comptes et équipes. Dépendances d’intégration : S06, S07.

### Mission concrète

Auditer caches globaux, statistiques, journaux, profils et objets partagés. Documenter ce qui est volontairement commun et ce qui doit être séparé.

### Critères d’acceptation

Les informations d'un compte ne contaminent pas les décisions d'un autre. Les données communes gardent leur source et serveur.

### Scénarios à vérifier

Même nom sur deuxserveurs, mêmesGID, PIDréutilisé et deuxcomptes avec HDV ouverts.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A07 — Notifications utiles et déduplication

Famille : Comptes et équipes. Dépendances d’intégration : S09, U07.

### Mission concrète

Revoir les notifications de blocage, défaite, banque, succès et fin de tâche. Les rendre actionnables, dédupliquées et ciblées au bon compte.

### Critères d’acceptation

Un incident ne produit pas des dizaines d'alertes identiques. Une reprise résout l'incident visible sans effacer l'historique utile.

### Scénarios à vérifier

Refus répétés, combat perdu, bot volontairement arrêté et reconnexion réussie.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A08 — Supervision durable à trois bots

Famille : Comptes et équipes. Dépendances d’intégration : A04, L07, S09.

### Mission concrète

Formaliser le contrôle périodique actuel : curseurs, vérifications, actions autorisées et preuves. Séparer supervision, activité en jeu et fin de session.

### Critères d’acceptation

Le contrôle ne répète ni retrait, ni achat de stuff, ni déploiement. Les curseurs repartent correctement après redémarrage.

### Scénarios à vérifier

Consoleréinitialisée,400lignesavec truncatedfalse, donnéesbancairesen cache et tâchesconcurrentes.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A09 — Reprise d'instance et récupération de préférences

Famille : Comptes et équipes. Dépendances d’intégration : S05, S06, U09.

### Mission concrète

Auditer sauvegardes, schémas et restauration après crash. Séparer paramètres permanents, activité interrompue et opérations au résultat inconnu.

### Critères d’acceptation

Une instance restaurée ne répète pas une opération économique ambiguë. Les profils et scripts ne disparaissent pas silencieusement.

### Scénarios à vérifier

Fichierpartiel, versionancienne, crashpendantreprice et arrêtWindows.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A10 — Vue flotte et actions groupées contrôlées

Famille : Comptes et équipes. Dépendances d’intégration : A05, A06, U04.

### Mission concrète

Concevoir une vue compacte pour plusieurs comptes, avec préconditions et résultat individuel des actions groupées.

### Critères d’acceptation

Une action groupée affiche succès/refus par bot, sans faire croire qu'un lot entier a réussi quand un seul compte répond.

### Scénarios à vérifier

Relancer3scripts, Turbo3comptes, un compteencombat et quatrièmeinactif à exclure.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R01 — Inventaire des artefacts et versions natives

Famille : Recherche et reverse engineering. Dépendances d’intégration : S01.

### Mission concrète

Constituer un dossier minimal de versions, empreintes, exports et dépendances pour les modules natifs et clients de test. Utiliser REA pour les artefacts, outils source pour les dépôts.

### Critères d’acceptation

Chaque affirmation renvoie à une version exacte et une méthode. Aucun binaire téléchargé n'est exécuté par simple curiosité.

### Scénarios à vérifier

Comparer DLLembarquée et source, ABI, architecture et dépendance manquante dans une instance isolée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R02 — Résolution structurelle du tactique

Famille : Recherche et reverse engineering. Dépendances d’intégration : R01.

### Mission concrète

Étudier les types obfusqués et points d'accroche du rendu actuel à partir des artefacts attribués. Documenter les alternatives quand un nom historique ne résout plus.

### Critères d’acceptation

Les candidats ont des preuves structurelles et une stratégie de refus propre. Une correspondance approximative n'est pas injectée comme certaine.

### Scénarios à vérifier

Deuxversionsclient, fauxpositifstructurel, typeabsent et restaurationaprèsinitialisationpartielle.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R03 — Comparaison des décodeurs et schémas

Famille : Recherche et reverse engineering. Dépendances d’intégration : S01, R01.

### Mission concrète

Comparer Givre, JondoEmu, stakk et dofus-prospection sur quelques messages réellement utilisés. Produire un tableau sémantique et une fixture par divergence utile.

### Critères d’acceptation

Les numéros de champs d'anciennes versions ne sont pas copiés dans le client courant. Les noms et types sont corroborés par le bundle actuel.

### Scénarios à vérifier

Perte/gainPV, changementcarte, offreHDV, inventaire et résultatcraft.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R04 — Rendu de looks SuperDofus

Famille : Recherche et reverse engineering. Dépendances d’intégration : R01.

### Mission concrète

Analyser architecture de décodage/bake/cache des entités et les dépendances de ses assets. Prototyper une extraction de concepts compatible avec l'application.

### Critères d’acceptation

Rapport avec coûts, limites et preuve sur looks choisis. Les fichiers du jeu restent externes à la distribution du prototype.

### Scénarios à vérifier

Personnage, monstre, monture, recoloration et direction avec assetmanquant.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R05 — Atlas des neuf dépôts de référence

Famille : Recherche et reverse engineering. Dépendances d’intégration : S01.

### Mission concrète

Lire sources pertinentes aux commits figés, licences et tests. Classer idées réutilisables, adaptables, incompatibles et non vérifiées avec justification.

### Critères d’acceptation

Chaque dépôt a au moins un chemin inspecté et une idée reliée à un lot Givre. Aucun copier-coller aveugle d'un projet sans licence claire.

### Scénarios à vérifier

Vérifier langage réel, version de jeu, présence d'une API et statut du dépôt.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R06 — Client Metamob typé et collections

Famille : Recherche et reverse engineering. Dépendances d’intégration : R05.

### Mission concrète

Étudier résolution de noms, pagination, limites, erreurs et mutations du client. Préparer un contrat .NET ou une passerelle avec données de test.

### Critères d’acceptation

L'adaptateur sait distinguer réseau, autorisation, limite et entité inconnue. Les mutations ne sont pas exécutées pendant la recherche.

### Scénarios à vérifier

Nominconnue, pagevide, retry, conflit et limiteAPI.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R07 — Étude comparative UI stakk et BillBot

Famille : Recherche et reverse engineering. Dépendances d’intégration : R05, U01.

### Mission concrète

Analyser navigation, densité, cartes, panneaux économiques et retours d'action depuis les sources et captures publiques.

### Critères d’acceptation

Recommandations illustrées et liées aux problèmes Givre. Le rapport distingue ce qui a été vu de ce qui a seulement été déduit.

### Scénarios à vérifier

Au moins trois parcours comparés et une proposition originale adaptée à Avalonia.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R08 — Évolution des modules dofus-enhanced

Famille : Recherche et reverse engineering. Dépendances d’intégration : R01, R05.

### Mission concrète

Comparer les sources actuellement embarquées au nouveau commit consulté, par fonctionnalité et contrat ABI. Préparer des portages séparés des changements non nécessaires.

### Critères d’acceptation

Chaque portage a son périmètre, sa licence, ses dépendances et une méthode de restauration. Les effets ne sont pas annoncés validés sans essai.

### Scénarios à vérifier

Masquesanimation, changementprofil, déchargement, mappingtypes et clientnull.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R09 — Corpus de paquets expurgés et minimisés

Famille : Recherche et reverse engineering. Dépendances d’intégration : S06, R03, Q02.

### Mission concrète

Construire des fixtures utiles depuis les événements nécessaires, en retirant authentification, noms non utiles et données de comptes. Conserver provenance et hash.

### Critères d’acceptation

Le corpus peut être partagé aux agents sans logs complets. Une fixture conserve assez de contexte pour reproduire le défaut.

### Scénarios à vérifier

Casuredeparsing, doublemessage, mauvaisegénération et contrôle de redaction automatique.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## R10 — Laboratoire local compatible avec JondoEmu

Famille : Recherche et reverse engineering. Dépendances d’intégration : R03, R05, Q02.

### Mission concrète

Étudier un banc d'essai local émulé pour scénarios répétables. Délimiter clairement les différences de version et les comportements qui ne prouvent rien sur le serveur réel.

### Critères d’acceptation

Un scénario local peut être automatisé sans toucher les comptes actifs. L'émulateur reste une source d'essais, pas une vérité protocolaire.

### Scénarios à vérifier

Banque, déplacement, soin, craft et actionrefusée avec versionexplicitementdifférente.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B01 — Audit des capacités réelles T3 et CoS

Famille : Pont T3 / CoS. Dépendances d’intégration : S01.

### Mission concrète

Comparer la version installée, le catalogue d'outils et les sources figées de T3/CoS. Identifier ce qui est déjà exposé et ce qui exige un adaptateur.

### Critères d’acceptation

Table disponible/draft/absent prouvée pour liste, lecture, envoi, réveil, attente et annulation. Aucun endpoint imaginé présenté comme existant.

### Scénarios à vérifier

Un agentCoS endormi, un actif, un T3thread et une installation sans extension connectée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B02 — Adressage d'un worker et propriété de conversation

Famille : Pont T3 / CoS. Dépendances d’intégration : B01, S06.

### Mission concrète

Modéliser appInstance, ownerSession, ownerConversation, family, incarnation et worker. Respecter les contrôles existants au lieu d'usurper les en-têtes de corrélation.

### Critères d’acceptation

Une requête T3 n'adopte pas les workers d'un autre propriétaire. La reprise peut distinguer famille endormie et incarnation retirée.

### Scénarios à vérifier

Nomsworkeridentiques, propriétairedifférent, redémarrageCoS et ancienworkerretiré.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B03 — Contrat durable des jobs multi-agents

Famille : Pont T3 / CoS. Dépendances d’intégration : B01, S05.

### Mission concrète

Définir JobSpec, Lease, Progress, Artifact, Result et Review avec version, base commit et clés d'idempotence. Prévoir import/export manuel avant automatisation.

### Critères d’acceptation

Un lot peut être distribué et relu sans transcript complet. Les formats restent indépendants du fournisseur et du modèle.

### Scénarios à vérifier

Double soumission, résultat sans commit, artefact absent, changement de base et reprise de job.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B04 — Broker local de tâches et baux

Famille : Pont T3 / CoS. Dépendances d’intégration : B02, B03.

### Mission concrète

Prototyper une file durable locale avec revendication atomique, bail expirant, heartbeat et état de réconciliation. Les handlers ne font pas encore d'actions de jeu.

### Critères d’acceptation

Un seul lease actif autorise l'écriture d'un lot. La perte d'un worker ne duplique pas automatiquement une opération externe déjà envoyée.

### Scénarios à vérifier

Deuxworkers, crashbroker, expirationbail, notificationperdue et retourtardif.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B05 — Passerelle CoS à identité prouvée

Famille : Pont T3 / CoS. Dépendances d’intégration : B02, B04.

### Mission concrète

Concevoir un adaptateur qui fait exécuter les opérations de workers par leur propriétaire légitime. Étudier worker pull, plugin et API officiellement exposée avant toute automation DOM.

### Critères d’acceptation

Preuve d'un réveil d'un worker connu et lecture de son résultat dans une famille autorisée. Les contrôles Host/Origin/token ne sont pas supprimés.

### Scénarios à vérifier

Propriétaireabsent, ongletfermé, nouvelleincarnation et messageenvoyédeuxfois.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B06 — Adaptateur T3 et notifications de fin

Famille : Pont T3 / CoS. Dépendances d’intégration : B01, B03, B04.

### Mission concrète

Relier le broker aux capacités T3 disponibles sans créer des conversations ordinaires à la place de tâches déléguées. Prévoir webhook ou mécanisme applicatif pris en charge.

### Critères d’acceptation

Le résultat revient au thread/projet prévu, une fois logiquement, avec tâche et preuve identifiées. Le modèle/options retournés sont contrôlés.

### Scénarios à vérifier

Notificationdupliquée, threadoccupé, projetincorrect et repriseaprèsredémarrage.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B07 — Contexte utile et artefacts sans fuite

Famille : Pont T3 / CoS. Dépendances d’intégration : B03, R09.

### Mission concrète

Construire des paquets de tâche bornés : brief, extraits, docs, base, objections et références d'artefacts. Éviter de recopier userdata et historiques entiers.

### Critères d’acceptation

Le worker reçoit ce qui est nécessaire et le rapport garde la provenance. Un nouveau tour peut continuer avec les objections non résolues.

### Scénarios à vérifier

Fichiergros, documentexterne, sourceabsente et artefactcontenantunechaînesensible.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B08 — Tableau de pilotage des agents

Famille : Pont T3 / CoS. Dépendances d’intégration : B03, B04, U03.

### Mission concrète

Concevoir la vue jobs, leases, worktrees, preuves, blocages et décisions de revue. Distinguer transport accepté, worker actif et résultat vérifié.

### Critères d’acceptation

Un coordinateur gère des dizaines d'agents sans perdre propriété de fichiers et base de chaque patch. Les actions restent traçables.

### Scénarios à vérifier

Vingtagents, troiscoordinateurs, tâchesliées, agentendormi et revueàreprendre.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B09 — Revue en boucle et désaccords conservés

Famille : Pont T3 / CoS. Dépendances d’intégration : B03, B07, Q07.

### Mission concrète

Formaliser première livraison, objections, réponse et seconde revue comme objets distincts. Garder l'identité de chaque round et ses preuves.

### Critères d’acceptation

Un relecteur ne répète pas des constats déjà corrigés faute de contexte. Une objection ne disparaît pas en réécrivant le résumé.

### Scénarios à vérifier

Deuxrounds, correctifpartiel, nouvellehypothèse et changementdebaseentrelesrounds.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## B10 — Robustesse du pont multi-orchestrateurs

Famille : Pont T3 / CoS. Dépendances d’intégration : B04, B05, B06, B09.

### Mission concrète

Tester deux coordinateurs et plusieurs familles CoS avec pertes réseau et redémarrages. Prévoir quotas de concurrence locaux, arrêt et reprise sans changer d'identité.

### Critères d’acceptation

Pas d'action ni de fichier attribué au mauvais agent. Un timeout de réveil peut rester indéterminé et doit être rapproché avant répétition.

### Scénarios à vérifier

Crashapresenvoi, duplicatcallback, leaseretiré, sessioninconnue et restaurationsimultanée.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q01 — Critères de réception communs aux lots

Famille : Qualité et livraison. Dépendances d’intégration : S02, S04.

### Mission concrète

Définir les niveaux recherche, prototype, code vérifié et validé en session. Créer une checklist courte par famille avec preuves minimales et limites.

### Critères d’acceptation

Deux relecteurs peuvent classer le même livrable avec des raisons comparables. Les quatre niveaux ne sont pas confondus.

### Scénarios à vérifier

LotUI, protocole, économie, reverseengineering et broker sans essairéel.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q02 — Injection de fautes sur services réels

Famille : Qualité et livraison. Dépendances d’intégration : S03, S05.

### Mission concrète

Préparer transport et horloge contrôlables pour exécuter la source métier sans serveur réel. Éviter les faux services qui recopient l'implémentation.

### Critères d’acceptation

Les tests injectent refus, retard, réordonnancement, annulation et mauvaise identité sur les points de production.

### Scénarios à vérifier

CommandeHDV, changementcarte, combat et craft avec étatavant/après vérifiable.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q03 — Vérification des traductions et ressources

Famille : Qualité et livraison. Dépendances d’intégration : S03, U08.

### Mission concrète

Automatiser unicité des clés, présence FR/EN/ES, formats et ressources référencées. Conserver les essais nécessaires aux écrans réellement modifiés.

### Critères d’acceptation

Une clé dupliquée ou un format cassé est détecté avant packaging. Les valeurs manquantes ont un repli lisible.

### Scénarios à vérifier

Clédupliquée, placeholderincompatible, ressourceabsente et cultureespagnole.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q04 — Protocole d'essai en session isolée

Famille : Qualité et livraison. Dépendances d’intégration : S02, S03, Q01.

### Mission concrète

Définir autorisation, compte, instance, durée, budget et critères d'arrêt de chaque essai réel. Le coordinateur alloue la fenêtre sans interrompre le farm courant.

### Critères d’acceptation

Chaque preuve indique version et scénario exacts. Aucun agent ne partage un compte de test sans réservation.

### Scénarios à vérifier

Essai achatborné, forgerune, transportMITM, Socket et déchargementnative.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q05 — Budgets de performance reproductibles

Famille : Qualité et livraison. Dépendances d’intégration : S03, S09.

### Mission concrète

Fixer mesures de démarrage, mémoire, UI, rendu, latence de décision et tailles de caches. Comparer scénarios contrôlés plutôt que ressentis.

### Critères d’acceptation

Les régressions importantes ont un seuil et une méthode. Un gain local ne masque pas une régression sur un autre écran.

### Scénarios à vérifier

Cartedense, dixmilleoffres, traceslongues, troissessions et redémarrages.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q06 — Packaging et dépendances reproductibles

Famille : Qualité et livraison. Dépendances d’intégration : S03, R01, Q03.

### Mission concrète

Vérifier runtime, DLL natives, notices et fichiers de catalogue réellement publiés. Préparer un artefact séparé au lieu d'écraser la version en cours.

### Critères d’acceptation

Le package démarre sur un environnement de validation et déclare son identité de build. Les artefacts de recherche ne partent pas en production.

### Scénarios à vérifier

Machinepropre, DLLmanquante, architectureincorrecte et catalogueincompatible.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q07 — Revue contradictoire indépendante

Famille : Qualité et livraison. Dépendances d’intégration : Q01, Q02.

### Mission concrète

Assigner à un autre agent une reproduction et une revue des risques métier. Il ne se limite pas à lire le résumé du développeur.

### Critères d’acceptation

La revue contient au moins un essai susceptible de réfuter la correction et répond aux limites annoncées. Le reviewer ne modifie pas le même worktree.

### Scénarios à vérifier

Régressionancienne, casoùuneconfirmationmanque et scénarioadversarial de concurrence.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q08 — Compatibilité des schémas et migrations

Famille : Qualité et livraison. Dépendances d’intégration : S05, A09.

### Mission concrète

Inventorier fichiers JSON, bases caches et contrats d'artefacts. Définir migrations idempotentes et repli sur version inconnue.

### Critères d’acceptation

Une migration peut être relancée sans doubler les données et ne convertit pas des états inconnus en succès.

### Scénarios à vérifier

Sauvegardepartielle, deuxversionssuccessives, retourarrière et cacheavecchampinconnu.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q09 — Rapports de régression navigables

Famille : Qualité et livraison. Dépendances d’intégration : Q01, Q07, B03.

### Mission concrète

Construire un index par lot, scénario, version et preuve avec résumé court. Permettre au coordinateur de retrouver rapidement le test correspondant à un défaut.

### Critères d’acceptation

Une affirmation de correction peut être suivie jusqu'au diff et à la preuve. Les liens cassés et empreintes incorrectes sont signalés.

### Scénarios à vérifier

Rapportdéplacé, artefactmanquant, anciennebase et plusieursrounds de revue.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Q10 — Livraison finale et audit de non-régression

Famille : Qualité et livraison. Dépendances d’intégration : S10, Q03, Q04, Q06, Q07, Q08.

### Mission concrète

Assembler une version candidate après intégrations sérialisées et exécuter les contrôles requis. Préparer rollback, limites connues et points encore ouverts.

### Critères d’acceptation

La livraison n'annonce pas tous les bugs résolus sur la seule base des tests. La version exécutée est vérifiée après activation par le coordinateur.

### Scénarios à vérifier

Buildapplication, suiterequise, cléslangues, smokeUI et sessionsautoriséesbornées.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X01 — Centre d'intentions et prochaine action

Famille : Fonctionnalités avancées. Dépendances d’intégration : S09, U04, B08.

### Mission concrète

Proposer une vue qui dit ce que chaque bot cherche à obtenir, pourquoi il attend et ce qui permettra d'avancer. Réutiliser la provenance d'état métier.

### Critères d’acceptation

La vue donne une explication courte et utile, sans inventer de raisonnement caché ou exposer des dumps techniques.

### Scénarios à vérifier

Attenterepousse, manqueclé, groupemort, banqueinconnue et déconnexion.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X02 — Laboratoire de stratégie sans dépenses

Famille : Fonctionnalités avancées. Dépendances d’intégration : C10, E04, E06, M08.

### Mission concrète

Créer un outil what-if sur profils combat, taille de groupes, itinéraire et équipement. Utiliser simulation et données observées avec niveau de confiance.

### Critères d’acceptation

L'utilisateur peut comparer deux choix sans lancer le bot ni acheter. Les limites et hypothèses sont visibles au résultat.

### Scénarios à vérifier

Stuffactuel, variantebudget, groupe8 et cibleavecsoininconnu.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X03 — Planificateur d'objectifs métier et donjon

Famille : Fonctionnalités avancées. Dépendances d’intégration : P02, P08, P10, F06.

### Mission concrète

Décomposer un objectif utilisateur en tâches et prérequis vérifiables, comme obtenir100clés puis faire10donjons. Distinguer préparation et exécution.

### Critères d’acceptation

Chaque étape a coût, compte, dépendances et preuve de fin. Le plan peut être révisé sans refaire une étape confirmée.

### Scénarios à vérifier

Stockinconnu, métierinsuffisant, cléconsommée et arrêtde session.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X04 — Bibliothèque de recettes d'automatisation

Famille : Fonctionnalités avancées. Dépendances d’intégration : L03, L08, S05.

### Mission concrète

Proposer des assemblages réutilisables de blocs réels : trajet, récolte, banque, combat, marché et production. Chaque bloc indique ses capacités et limites.

### Critères d’acceptation

Un assemblage ne génère pas de squelette caché. Les prérequis manquants empêchent l'exécution avec explication.

### Scénarios à vérifier

Blocnonimplémenté, événementinconnu, paramètreinvalidé et changementd'API.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X05 — Optimisation coopérative des comptes

Famille : Fonctionnalités avancées. Dépendances d’intégration : A03, F02, F06, P04.

### Mission concrète

Étudier répartition de récolte, craft et transport entre comptes en fonction des métiers et stocks. Commencer par un plan sans transfert réel.

### Critères d’acceptation

Aucun plan n'attribue un métier absent ni une ressource non disponible. Les échanges futurs sont des étapes explicitement confirmables.

### Scénarios à vérifier

Pêcheur200, mineur130, artisanfaibleniveau et budgetcommun nonautorisé.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X06 — Catalogue de données avec preuves de compatibilité

Famille : Fonctionnalités avancées. Dépendances d’intégration : R03, Q08, U07.

### Mission concrète

Proposer une page version de jeu, catalogue, champs requis, sources et fonctions compatibles. Prévoir une mise à jour atomique et réversible du catalogue.

### Critères d’acceptation

Une base ancienne ne donne pas silencieusement une recette ou une portée comme actuelle. L'utilisateur comprend les fonctions indisponibles.

### Scénarios à vérifier

Baseinstallée, ancienbundle, migrationratée et comparaison de deuxversions.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X07 — Assistant d'analyse d'incident pour MCP

Famille : Fonctionnalités avancées. Dépendances d’intégration : S09, L07, B03, R09.

### Mission concrète

Définir un outil qui retourne un diagnostic compact : dernier progrès, action en attente, refus, fraîcheur et artefacts utiles. Aucun log complet par défaut.

### Critères d’acceptation

Un agent peut orienter une enquête sans scanner tout userdata ni répéter des transactions. Les causes non prouvées restent hypothèses.

### Scénarios à vérifier

Boucle83tours, routebloquée, achatindéterminé et consoletropancienne.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X08 — Architecture de plugins de confort

Famille : Fonctionnalités avancées. Dépendances d’intégration : R05, R07, S07.

### Mission concrète

Étudier des points d'extension pour rendus, conseils et sources externes, sans multiplier les singletons ni casser les versions. Comparer stakk et l'existant Givre.

### Critères d’acceptation

Prototype d'un plugin lecture seule avec version de contrat, désactivation et limites de ressources. Pas de chargeur universel prématuré.

### Scénarios à vérifier

Pluginabsent, versionincompatible, exception et déchargement pendant une vue.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X09 — Expériences visuelles orientées utilisateur

Famille : Fonctionnalités avancées. Dépendances d’intégration : U01, U03, M03, H01, A10.

### Mission concrète

Proposer trois interactions ambitieuses : combat lisible, HDV Socket immédiat et vue flotte avec carte sobre. Faire des prototypes navigables et recueillir des décisions comparables.

### Critères d’acceptation

Chaque prototype résout une tâche concrète et indique ce qu'il faudra raccorder. Aucune maquette déclarée fonctionnalité finie.

### Scénarios à vérifier

Parcoursd'un GelanoPA, suivi d'un groupe8 et compréhension d'un détour de mine.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## X10 — Roadmap vérifiée après les premiers retours

Famille : Fonctionnalités avancées. Dépendances d’intégration : Q01, Q09, B09.

### Mission concrète

Consolider les résultats des agents en priorités produit fondées sur défauts, dépendances, efforts et bénéfices. Retirer les idées déjà résolues et conserver les inconnues.

### Critères d’acceptation

La prochaine vague dépend des preuves reçues, pas du nombre de fichiers ou de tokens consommés. Chaque priorité a un propriétaire et une raison.

### Scénarios à vérifier

Trois lotsrejetés, correctiontransversale, ideerendueinutile et nouvellecontrainteutilisateur.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## A11 — Nouveau personnage Kourial — player.four666+1

Famille : Comptes et équipes. Dépendances d’intégration : S02, A01, L01.

### Mission concrète

Prendre en charge le compte mis à disposition, créer un nouveau personnage sur Kourial exclusivement, puis assurer son démarrage et sa progression via une surface MCP ou Chat on Steroids Desktop effectivement disponible. Coordonner les interventions nécessaires sur les autres comptes.

### Critères d’acceptation

Identité compte vérifiée, nouveau personnage créé et serveur Kourial relu depuis une source réelle. Aucun repli vers Mikhal ou Dakhal. Activité et suivi de progression réellement confirmés.

### Scénarios à vérifier

Serveur absent ou complet, authentification bloquante, nom indisponible, erreur de sélection, création acceptée sans confirmation, reconnexion et conflit de propriétaire du compte.

### Démarrage et limites du périmètre

Lire le prompt maître et les sources de la photographie attribuée. Rechercher les points d’entrée UI/MCP, le service, le transport, la réponse serveur et la projection d’état concernés. Déclarer les fichiers avant modification ; la fiche ne donne aucun droit de modifier un fichier possédé par un autre agent. Recherche et prototypes isolés peuvent commencer avant la livraison des dépendances ; l’intégration exige leurs contrats.

### Livrable exploitable

1. Reproduction minimale ou limites d’accès explicites, avec distinction fait/hypothèse.
2. Contrat d’entrée/sortie et source de vérité ; refus, annulation et reprise pertinents.
3. Patch intégré dans le périmètre attribué, ou prototype/recherche correctement nommé ; aucun squelette déclaré terminé.
4. Commandes réellement exécutées, résultats et preuves minimales. UI : captures et parcours ; protocole : fixture et version ; économie : origine, taille de lot, date et nature du prix.
5. REPORT.md, RESULT.json, commit/patch, artefacts et empreintes. Indiquer les petits raccordements attendus de l’intégrateur.

### Revue indépendante

Construire un scénario capable de réfuter les critères d’acceptation ci-dessus. Vérifier la version et la base du patch, puis confirmer ou contester avec preuve. Conserver les objections et leurs réponses pour le prochain tour ; aucun résumé ne fait disparaître une limite connue.

---

## Retour et intégration

Le coordinateur reçoit chaque résultat, vérifie sa base et ses fichiers, examine le diff, reproduit les points importants et répond accepté, accepté avec limites, à reprendre ou rejeté avec reproduction. Un autre agent relit les changements sensibles. L’intégrateur sérialise les modifications des points chauds.

Avant chaque commit d’intégration : build application, tests requis, clés FR/EN/ES et git diff --check. Pas de builds concurrents dans le même worktree. Adapter les tests au risque et aux comportements ; ne pas ajouter de tests qui recopient simplement le code.

Statuts possibles : recherche, prototype, code_verifie, valide_en_session, bloque. Les artefacts absents ou les commandes non exécutées restent explicitement absents/non exécutés. Les agents peuvent utiliser tous les outils nécessaires dans leur environnement attribué. Une preuve de session identifie la version exécutée, le scénario et le retour serveur utile, sans journal complet ni secret.
