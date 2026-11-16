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
