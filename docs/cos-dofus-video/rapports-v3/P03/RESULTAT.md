# P03 — Compagnons et attribution des challenges

Date : 2026-11-04. Audit documentaire et lecture de code en place, **sans nouveau combat**. Priorités : **De la même lignée**, **Duel**, **Prudent**. Les règles ci-dessous ont des niveaux de preuve différents ; le filtre logiciel n'est pas automatiquement la règle DOFUS 3.7.13.17.

## Faits nouveaux ou précisés depuis N02/N04

1. `GivreClone.App/Services/LaunchService.Combat.Challenges.cs:241-248` prend `me=SelfActorId(pid)` et construit `Allies()` à partir de **tout combattant non ennemi autre que `me`**, commentaire explicitant qu'il inclut les invocations. Sur ce chemin, **MemeLignee** est vrai lorsqu'**au moins un** tel allié est aligné sur la case finale (`:347`), sans filtre de nature compagnon/invocation. **C'est un comportement de code**, à comparer à la définition serveur du challenge.
2. **Duel/ChacunSonMonstre** évite l'ennemi dont la liste `Challenge.targets.Attackers` inclut un acteur différent de `me`, et évite également les attaquants mémorisés localement (`:263-308`). Le calcul dépend de l'identité effective de l'attaquant; `ownerId` seul ne suffit pas à décider si la contribution d'une invocation/compagnon doit être attribuée à son propriétaire.
3. **Prudent** filtre les cases finales par `distance(cellule, ennemi) != 1` (`:343-345`) ; il n'explique ni si la condition est évaluée pour **chaque compagnon**, ni la cause d'un plan vide tant que les `AllowedCells` et les événements de placement/cast ne sont pas reconstitués.
4. Le journal historique N02 signale `0` motif de cast et `3` mouvements sur la fenêtre compagnon `+7,734..+7,871s`, mais n'y prouve **ni 11 PA/6 PM ni un résultat serveur de cast** (`N02/PREUVES.md:9-22`). **Observation historique distincte, postérieure** : `docs/validation/official-client-371317-20261103.md:945-950` rapporte le fight 22, plan réellement vide à **11 PA / 6 PM** sous **Prudent**, et fallback `344→357`; les tableaux de cases de fin/autorisées ont été tronqués. Le rapport de la même campagne confirme ailleurs des casts Korbax **4242/4245** (`:868-869,:903-904`) : ce compagnon peut agir, sans expliquer ce tour précis. La validation historique aboutit à **25 résultats de combat** (`:960-966`), mais ne tranche pas ce défaut d'IA.

Les entrées d'identité de la campagne historique restent des notes du développeur : elles ne sont pas un export de messages serveur indépendant fourni à P03. Les identifiants humains/comptes, PID et journaux bruts ne sont pas copiés ici.

**Complément temporel reçu pendant la consolidation :** [jointure_n04_n10.json](jointure_n04_n10.json) rassemble les événements anonymisés N04 et N10 issus du **même hash de journal source** dans la fenêtre **+10,900 à +12,400 s**. On y retrouve `companion_turn_started` à **+10,967 s**, `request_tx` à **+11,268 s**, `position_change_seen` à **+11,398 s** (case 289), un autre changement à **+11,895 s** (case 330), `barrier_closed` à **+11,896 s**, puis `turn_end_reported` à **+12,214 s**. Cette jointure temporelle est une **preuve de co-occurrence issue de journaux historiques**, pas un lien formel commande→réponse : les exports ne contiennent pas le même `actionId` et l'identité de l'acteur est absente des `request_tx`. La sortie est lisible en UTF-8 avec BOM et n'est ni un nouveau combat ni un replay Protobuf.

**Confirmation historique spécifique, au-delà des règles générales Unknown** : la même source de validation `docs/validation/official-client-371317-20261103.md:390-407` rapporte **Duel échoué par annonce serveur** lorsque Korbax attaque la cible déjà attaquée par le maître. Les lignes `:479-497` rapportent **De la même lignée (964) échoué** après le sort compagnon 4242 confirmé, puis échec du déplacement final faute de PM. Ces deux épisodes ont une valeur de **témoignage développeur d'observation serveur**, mais P03 ne possède pas les trames originales. Ils n'établissent ni le traitement de toutes les invocations, ni un comportement universel de tous les compagnons. Le cas Prudent fight 22 `:945-950` reste causalement indéterminé.

**Jointure documentaire N04/N10 ajoutée** : N04 replay 1 et N10 partagent le SHA source `4768ae7738df2098ba3901e61f6cc0199fb1a970a68b7baf7c95f3069d3d6e9f`. Le fichier [jointure_n04_n10.json](jointure_n04_n10.json) contient **34 événements anonymisés** (5 N04 et 29 N10) de +10,9 à +12,4 secondes, dont un tour compagnon commun ligne 72, trois positions successives de `actor_1` (289/302/330) et six `request_tx`. Les requêtes n'ont aucun acteur, verbe, cible ou identifiant de transaction dans l'export, si bien qu'aucun envoi ne peut être certifié comme un cast ou relié à un mouvement précis. **N02 porte un SHA source différent** et ne peut être enrichi par cette jointure.

## Matrice par famille de contrainte

| Famille | Source de décision dans le code | Joueur vs compagnon contrôlé vs invocation | Statut serveur |
| --- | --- | --- | --- |
| Acteur autorisé | `SelfActorId(pid)`, `ChallengePlan` `:241-248` | L'acteur du tour doit être distingué de l'owner; le même PID peut piloter un compagnon | **Unknown** pour la portée des règles aux summons/compagnons |
| Ordre des attaquants | `Targets.Attackers` et mémoire `st.Attackers`, `:304-308` | Un attaquant `companion` distinct ne doit pas être renommé `owner` sans preuve | Code clair, critère serveur **Unknown** |
| Cible imposée | `Targets.TargetId`, `Only/Avoid`, `:251-309` | Préserver cible serveur et auteur exact, y compris AoE | Champ et chemin de code identifiés, validité d'une attaque **Unknown** |
| Case finale | `EndCellOk`, `Prudent`, `MemeLignee`, `:209-217,:343-348` | Lignée : `Allies()` accepte alliés/invocations; Prudent teste distance aux ennemis | Test de code possible, condition réelle **Unknown** |
| Élément utilisé | `ChallengeSpellOk`, `ElementLock`, `:371-383` | Source `spellId` et attributaire du cast nécessaires | **Unknown** si sort non confirmé |
| Invocation/compagnon | `Allies()` exclut seulement ennemi et `me`; propriété non utilisée pour Lignée | Compagnon contrôlé et invocation sont distincts en combat; owner mapping absent de N02 | **Unknown**, aucun transfert automatique de contribution |

Le choix du challenge dans `ChallengeCatalog` et la politique d'abandon automatique ne sont pas une preuve de réussite du challenge. `ChallengeResultEvent` autoritaire est nécessaire, idéalement associé à `Challenge.targets/attackers` avant/après l'action.

## Contre-exemples minimaux candidats

1. **Lignée-L1**, fin de P alignée avec un **autre personnage**, aucun compagnon : sortie du filtre actuel `true` si l'entité est bien classée non ennemie; résultat challenge serveur requis.
2. **Lignée-L2**, fin de P alignée uniquement avec une **invocation alliée** : même sortie `true` du filtre actuel, mais **pas de réussite serveur présumée**. C'est le contre-exemple décisif au raccourci « tout allié compte ».
3. **Lignée-L3**, fin de P alignée uniquement avec un **compagnon contrôlé** : filtre logiciel `true` si `_fighters` comprend ce combattant; critère serveur à établir séparément.
4. **Duel-D1**, acteur A touche X puis acteur B touche Y : mémoire d'attaquants distincts, aucune collision.
5. **Duel-D2**, acteur A touche X puis acteur B envisage X : `Avoid` si `A != me`; résultat réel à capturer, même en AoE.
6. **Duel-D3**, invocation de A touche X puis B/X : tester deux attributaires possibles (invocation vs A) en prenant `actorId` du receipt, jamais en substituant le owner.
7. **Prudent-R1**, case finale de compagnon à distance 1 d'un ennemi vs distance 2 : le filtre actuel refuse la première et admet la seconde si toutes les autres contraintes le permettent; aucune garantie de chemin vers l'une ou l'autre.
8. **Prudent-R2**, le même plateau à 11 PA/6 PM, aucune case finale autorisée dans le snapshot complet : seul un diagnostic complet peut distinguer contrainte impossible, sort inadmissible, itinéraire introuvable et fallback; l'incident enregistré ne fournit pas cette preuve.

Ces cas sont documentés dans [cas_challenges.json](cas_challenges.json), **candidats de fixtures, non exécutés**, avec les catégories de source. Le cas Prudent utilise des cellules et des ressources rapportées dans un journal historique, mais la fixture ne prétend pas reconstruire les cases ni la cause du fight 22.

## Champs exacts encore nécessaires pour expliquer le tour vide

`fightId`, `session/epoch`, `round`, `turnActorId`, `ownerId`, `controlledBy`, `actorKind` (joueur/compagnon/invocation), `startTurnPA/PM` autoritaires, `endTurnPA/PM`, liste de sorts/grades et portes d'admission, obstacles/occupations/positions, `AllowedCells`, `EndCell`, contrainte Prudent active et cibles, plan choisi/alternatives, chaque `commandId` mouvement/cast, `requestSent`, `serverReceipt` et ses variations d'état, `ChallengeResultEvent`, toute action de fallback, horodatages et résultat après tour. La présence d'une ligne `request_tx` sans résultat n'est **pas** un lancement de sort confirmé.

**Livré :** matrice et huit contre-exemples, lecture du filtre logiciel, confrontation à trois épisodes historiques (Duel et Lignée échoués, tour Prudent vide à 11 PA/6 PM), et jointure N04/N10. **Non livré :** explication causale du tour Prudent, généralisation aux invocations et autres compagnons, replay protocolaire complet.
