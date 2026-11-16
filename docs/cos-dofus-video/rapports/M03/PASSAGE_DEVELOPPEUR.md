# M03 — Passage développeur : contrat pour le simulateur Xélor

**Livraison : recherche / spécification.** Aucun code, test ou replay GivreClone créé ou exécuté. Référence : [fiche de preuve](./FICHE_XELOR.md) et [rapport de collecte](./RAPPORT.md). **Cible présumée d'après le kit : DOFUS 3.7.13.17, sous-patch exact non vérifié.**

## Trois règles prioritaires

| Ordre | Règle | Preuve | Condition / limite |
| --- | --- | --- | --- |
| **1** | **Création = permutation de deux cellules occupées**. La cible et l'entité occupant la destination prennent Téléfrag ; la case d'arrivée libre n'applique pas cet état. | **M03-R01**, [guide 3.7.12, passif](https://dofus-grimoire.fr/classes/xelor/), [INFI 00:05–00:15](https://www.youtube.com/watch?v=m28gccWeFGs&t=5s) (historique). | Insertion dans historique et ordre d'effets serveur pas attestés ; n'appliquer qu'après confirmation d'événements. |
| **2** | **PA : +2 au Xélor par Téléfrag créé, au plus une fois par sort générateur / tour** ; coûts 3.7 proposés : **Permutation 4 PA à exactement 2 PO**, **Gelure 2 PA**. | **M03-R02,R03,R04** ; [guide 3.7, Xélor eau](https://dofus-grimoire.fr/classes/xelor/) ; [Back from AFK 3.7, portée Permutation](https://backfromafk.com/classes/xelor/depuis-3-0/) ; [SAPEUH 01:50–02:03](https://www.youtube.com/watch?v=5sGLA1FiT7M&t=110s). | Restitution confirmée seulement par source communautaire, pas par reçu serveur. Les restrictions de lancer exactes doivent venir du client cible. |
| **3** | **Gelure utilise la position précédente**, le retour peut provoquer une seconde permutation. **Clepsydre 4 PA** et **Pétrification 4 PA initialement** consomment le Téléfrag **de leur cible** ; Clepsydre crédite le **tour suivant**, Pétrification réduit le **prochain lancer** après consommation. | **M03-R04,R05,R06** ; [SAPEUH 01:59–02:06](https://www.youtube.com/watch?v=5sGLA1FiT7M&t=119s), [Fashionista, sorts Xélor](https://dofusfashionista.gg/fr/spells_linked/XELOR/OTY5MzJFpcif/), [Back from AFK 3.7](https://backfromafk.com/classes/xelor/depuis-3-0/). | Contenu/expiration de l'historique et ordre d'application non prouvés en 3.7.13.17. |

**Statuts autorisés ici :** corrobore-source, declare-video, hypothese. Captures du lecteur SAPEUH : **plateau PvP réel visible**, mais détail des cases et transitions **non vérifié** ; aucun résultat observe-video sur les règles de placement. Aucune preuve valide-test / valide-live.

## Entrées et transitions requises

Contrat de données **proposé** (noms indicatifs) :

| Donnée d'entrée | Pourquoi | Transition / garde |
| --- | --- | --- |
| Entity { uid, team, alive, cell, states } | Éviter les permutations entre identités incorrectes ou entités disparues | Sur swap, déplacer atomiquement les **deux** UID ; Téléfrag par UID, consommation sur la **cible** seulement |
| Board { cellValid, occupiedBy, obstacle, los } | Décider si la destination est libre, occupée, inaccessible | Case occupée → candidat swap ; vide → simple TP ; interdite → action impossible |
| MoveHistory[uid] { from, to, actionId, actor, turn, phase, sequence } | Résoudre « position précédente » même après marche, poussée ou TP | Insérer/apurer selon politique **à mesurer** ; ne pas déduire historique de l'unique position courante |
| CastConstraints { spellId, rank, variants, poMin, poMax, line, los, perTurn, perTarget, cooldown } | Valider Permutation à 2 PO, Gelure, Clepsydre, Pétrification | Source : tables client exactes, non noms de sort seuls |
| TurnLedger { actorUid, turnEpoch, ap, refundedBySpell, delayedAp, petrificationDiscount } | Calculer restitution conditionnelle et récompenses différées | +2 PA **au plus une fois par générateur/tour**, appliquer Clepsydre **tour suivant**, remise Pétrification **cast futur** |
| ServerReceipt { commandId, session, source, status, changedEntities, ap, stateEvents } | Conserver le lien entre simulation et combat réel | Refus → invalider plan et recalculer ; Unknown → attendre/reconcilier ; confirmation serveur → avancer état |

Laisser explicitement **UNKNOWN** les durées de mémoire, la politique exact de retours, les cases interdites, les effets déclenchés par tiers, les valeurs de dommages et de retrait PA. Le tutoriel INFI de 2022 avance une mémoire de **deux tours** ([02:06](https://www.youtube.com/watch?v=m28gccWeFGs&t=126s)) ; **ne pas coder cette durée pour 3.7** sans preuve complémentaire.

## Cas discriminants à porter en replay

| Cas | Setup et actions | Résultat **prédit**, à confronter aux événements serveur | Statut |
| --- | --- | --- | --- |
| **Positif P1** | X à (0,0), E à (2,0), (1,0) libre, 12 PA ; **Permutation(E) → Clepsydre(E) → Gelure(E) → Pétrification(E)**, et choix de sorts légal | Après Permutation : X(2,0), E(0,0), TF sur deux, **10 PA** ; après Clepsydre : TF E consommé, **6 PA** ; après Gelure : X(0,0), E(2,0), TF recréé, **6 PA** ; après Pétrification : TF E consommé, **2 PA**, réduction du prochain cast, bonus Clepsydre pour prochain tour. | **proposé, non exécuté** |
| **Négatif N1** | X (0,0), E (1,0), tenter Permutation version 3.7. | Refus car **1 PO** alors que 3.7 exige **2 PO** ; aucun échange, aucun remboursement. | **proposé, non exécuté** |
| **Négatif N2** | Après Permutation, déplacer E de (0,0) vers (0,1) ; (0,0) libre ; ensuite Gelure(E). | Retour à la **dernière** position (0,0) libre : **aucune permutation et aucune restitution de +2 PA**, si l'historique se met à jour à ce mouvement. | **proposé, non exécuté**, dépend historique |
| **Négatif N3** | Deux Permutations différentes provoquant chacune un Téléfrag au cours du même tour avec PA suffisants. | **Deux swaps**, mais une seule restitution **+2 PA pour le sort Permutation** ce tour. | **proposé, non exécuté** |

Le modèle doit **réfuter** une hypothèse de plan si la cible ne se trouve plus à la bonne position, si la variante équipée ne convient pas, si un lancer est refusé ou si une consommation a déjà vidé l'état. Un ACK transport seul ne valide aucun effet de jeu.

## Ordre de travail suggéré

1. **Données exactes :** lire les sorts et variantes du client cible et les messages serveur correspondant à déplacement, Telefrag, PA et refus. Confronter PO 2 et coûts 4/2/4/4, puis historique et durée.
2. **Tests unitaires / replay déterministe :** P1, N1, N2, N3 + cas cible morte/disparue et résultat Unknown ; vérifier simultanément cellules, états individuels et ledger PA, jamais les seuls dégâts affichés.
3. **Validation humaine sur journal expurgé :** au moins un cas positif et un cas négatif avec UID, tour, commande, source d'état, ancienne/nouvelle case et PA. **Valide-test** uniquement si le test est exécuté avec artefact réel ; **valide-live** uniquement si combat documenté et autorisé séparément.

**Priorité : élevée** pour l'IA Xélor, car la possibilité de finir le tour dépend à la fois de l'occupation, de l'historique et du plafond de remboursement. Risque : la capture SAPEUH 2025 ne prouve pas de coordonnées exactes et les données 3.7.12 ne certifient pas le patch 3.7.13.17. Les corrections de version et événements manquants doivent précéder une exécution active.
