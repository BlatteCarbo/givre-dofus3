# Intégration F06–F16 — 24 octobre 2026

Base récente 6ca99c9, branche fix/f01-f16-final-20261024. Aucun changement dans master. La livraison F13 d47f7e0 et les trois livraisons isolées F09/F10, F11/F12 et F14/F15/F16 ont été intégrées après relecture. Les livraisons enfants n’avaient pas de validation terminée ; seule la validation de l’intégration ci-dessous fait foi.

## Corrections de l’intégrateur

- F06/F07 : goUseInteractive garde GatherResource pour les vraies ressources et utilise UseElement pour les portes/mécanismes. Un changement de carte non nul termine l’usage générique sans événement de récolte inventé. La disparition temporaire de la carte ne constitue plus une arrivée.
- F06 : un refus sur un chemin court, une absence de cellule après recalage ou l’expiration déjà consommée dans la boucle de détour ne provoquent plus une nouvelle attente commune de 16 s. Le dernier détour reste sensible au refus. Deux détours au plus sont conservés ; une arrivée inattendue réelle est replanifiée.
- F06/F07 : les actions d’arrivée carte/récolte/attaque portent le jeton de leur run. Armement, annulation et dispatch sont sérialisés ; les ACK ignorent un propriétaire annulé. Arrêt répété, observateur de cancellation en erreur et nettoyage tardif de A ne suppriment pas les actions de B. Le scope est aussi utilisé par les trois voyages hors Lua (trésor/équipe). Une marche déjà envoyée peut finir ; ses actions suivantes annulées ne sont plus injectées.
- F03/F07 : propagation du jeton au moteur appelé par combat:playTurn, au repli après erreur, à la fin de tour et au prêt après placement. Les délais et points d’envoi contrôlent l’arrêt ; l’annulation ne passe pas par le catch qui envoie une fin de tour. Les timers de hook ignorent un run annulé. Le moteur automatique indépendant du profil conserve sa politique de reprise.
- F09 : les ingrédients sont répartis sur les piles du sac ; les entrées de recette dupliquées sont regroupées. Stock insuffisant ou somme hors capacité ne donnent aucun Ready. Le résultat inconnu après Ready ne déclenche aucun retry. Le harnais F08 exécute désormais la vraie source Craft, avec événements et transport simulés, à la place des gardes de texte initialement livrées.
- F11 : un trajet annulé sur changement de personnage ne garde pas Going bloqué et son retour tardif ne modifie pas le trajet remplaçant. Le Lua généré traite les étapes direction avec le MapCount décodé ; seuls les changements réussis comptent. Après reprise à mi-parcours, il revient à l’origine avant de recompter. Un retour au coffre échoué n’envoie plus startHunt depuis la mauvaise carte.
- F12 : le budget de session et les compteurs de runes survivent aux réouvertures après achat et couvrent la file entière ; limite par ligne liée à l’objet. Les prix observés remplacent le zéro systématique. Un prix inconnu n’est pas traité comme gratuit si une limite de valeur est configurée. Une réservation au résultat inconnu reste consommée par prudence.
- F13/F14 : la reconnexion remet le choix du script au planning avant tout Restart lorsque le planning est actif. Les autres corrections F13 (identité de session, liaison de compte, occurrences stables, snapshots atomiques, jointure revalidée) sont conservées.
- F16 : export sans fichiers dérivés d’identifiants et sans son propre ZIP ; contrôle d’unicité et de parité des dictionnaires. Inventaire Lua régénéré, nouvelle lecture getNextFlagMapCount documentée.

## Régressions et revue

Tests sur vrais services sans constructeur/transport pour annulation, pacing de fin de tour, navigation refusée et admission forge ; vrai service Craft lié au harnais F08 ; exécution MoonSharp du script de chasse généré (retour coffre refusé, direction avec refus, reprise intermédiaire) ; budget de session partagé entre objets et retries ; tests planning/équipe, abonnement, export et traductions. Les tests ne consomment ni rune, ni kamas, ni ressource de compte réel.

Deux revues statiques indépendantes ont examiné les modifications sensibles. La première a trouvé le dépassement directionnel après reprise ; le correctif et son test exécuté ont été ajoutés. La seconde ne conserve aucune objection concrète sur ce correctif et les scopes de voyage. Cela ne remplace pas des réponses serveur réelles.

## Validation de l’intégration

Dernière passe séquentielle réussie : build Release App, 206 avertissements préexistants et zéro erreur ; suite complète **2 787 réussites, zéro échec, deux ignorés, total 2 789**, durée des tests 24 s. Résultat local `GivreClone.Tests/TestResults/f01-f16-verified.trx` (ignoré Git). Dictionnaires : 2 701 clés chacun, uniques et identiques entre fr/en/es. `git diff --check` sans erreur.

Démarrage isolé du binaire corrigé : run vide `f01-f16-offline-smoke-20261024`, canal dédié, PID 16104. Fenêtre répondante ; get_app_status confirme journal initialisé, worker actif, aucune erreur et zéro bot/superviseur. list_bots renvoie zéro compte ; validate_lua_script confirme une syntaxe valide. Instance de smoke refermée après ce contrôle ; PID6740 toujours répondant et inchangé. Ce contrôle ne valide ni thème visuel ni fonctionnement en jeu. Première passe : 2 777 réussites, deux échecs de contrats statiques (signature Task.Delay modifiée pour le jeton et empreinte de l’inventaire Lua), deux tests ignorés. Contrats mis à jour et inventaire régénéré. Pas de nouvel échec comportemental dans cette passe.

Dépendances GivreCore/Launcher copiées dans obj/local-deps et références redirigées par Directory.Build.targets LOCAL NON LIVRÉ. Sorties standard pour conserver la recherche des sources par les tests. Après saturation mémoire des premières tentatives, un seul build/test à la fois, -m:1 /nodeReuse:false /p:UseSharedCompilation=false. Aucune suppression de données utilisateur, aucun arrêt du PID6740 ; les seuls processus arrêtés étaient des compilations/tests identifiés de cette tâche.

## Limites ouvertes, à ne pas masquer

- F03/F04/F05 : conserver les preuves réelles des rapports antérieurs ; nouvelle reconnexion, défis et parcours après ces corrections restent à rejouer sur la nouvelle version.
- F06/F07 : attente porte et refus mesurés dans les anciens logs ; correction actuelle non observée en jeu. Les API explicitement indisponibles de l’audit F07 restent indisponibles, elles ne sont pas devenues fonctionnelles par ce travail.
- F08/F09/F10 : scénario bancaire complet et chaîne HDV achat/vente/taxes/remise en vente non validés ici. Aucun achat de validation.
- F11/F12 : pas de chasse complète récompensée ni de rune appliquée en jeu. Corrélation et exclusion globale des opérations concurrentes d’échange restent à renforcer si des usages simultanés sont permis.
- F13 : regroupement strict après expiration corrigé dans la reprise ci-dessous ; validation multi-comptes réelle encore ouverte.
- F14 : nouvelle reconnexion Socket/MITM non exécutée dans la session utilisateur.
- F15 : tactique et six options d’animation, restauration et déchargement ne sont toujours pas validés visuellement en jeu.
- F16 : parité des clés et sauvegardes testées, audit visuel exhaustif de tous les écrans non réalisé.

Le suivi ne peut donc pas annoncer « F01 à F16 terminés » ni « tous les bugs corrigés ». Le PID6740 conserve l’ancienne version ; ses résultats éventuels ne valideraient pas la nouvelle publication.


## Reprise F13 — regroupement strict, 24 octobre 2026

Les attentes Lua avant attaque et sortie de carte retournent désormais un échec si le délai expire avec un membre encore absent, avec recontrôle au point d’action. Le prêt, la demande/sélection/validation de défi et la fermeture du combat sont refusés tant que les suiveurs attendus n’ont pas rejoint le même combat. Les automatismes moteur, scripts et compatibilité Givre attendent leur arrivée ou le retrait du membre attendu ; la fin du placement termine l’attente sans autoriser une action tardive. Les appels Lua de choix de défi restent bornés et retournent un échec après expiration.

L’attente appartient à la génération de combat. Un ancien choix de défi ne doit ni supprimer le choix du nouveau combat ni libérer sa gate. Les précombats et verrous différés recontrôlent également cette génération. Le journal du prêt forcé n’annonce un envoi que si le transport l’accepte.

Validation : build Release App réussi ; suite complète **2 823 réussites, zéro échec, deux ignorés** (2 825 cas). Douze cas StrictTeamWaitTests couvrent les gardes d’envoi, même/mauvais combat, délai expiré, arrêt, retrait/arrivée du membre, fin du placement et nettoyage tardif. Inventaire Lua régénéré ; tests de parité/unicité des traductions inclus dans la suite. Aucune validation réelle multi-comptes ajoutée. Directory.Build.targets et artifacts restent des fichiers locaux non livrés.

Publication séparée : `%USERPROFILE%/Desktop/GivreClone-Equipe-Strict-20261024/GivreClone.exe`, empreinte DLL identique à la sortie testée. Démarrage sur profil vide `f13-strict-smoke-20261024`, pipe dédié : fenêtre répondante, get_app_status confirme zéro bot/superviseur, journal initialisé, worker actif et aucune erreur. Fermeture demandée uniquement à cette instance de smoke (PID 12252) ; aucune session utilisateur interrompue. Ce contrôle ne constitue pas un essai en jeu.
