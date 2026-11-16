# Fiche M30-PROTO-KROBES-001 — compteurs de restauration et Infection

## Identité et sources

- **Mécanique** : mort conditionnelle des Krobes, infection du Protozorreur, délais Malamibe ; SOLO, DUO, TRIO, QUATUOR et compagnons pris comme acteurs possibles.
- **Version** : référence explicite 3.7 du **04/10/2026**, cible annoncée **3.7.13.17** sans client ni trace réelle. Les guides anciens 5/10 tours sont **obsolete-pour-cible**.
- **Auteur/date** : worker-3, 04/11/2026.
- **Sources** : [patch 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; [guide détaillé Ventre de la Baleine](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) ; [recoupement Dofuserie](https://www.dofuserie.com/mise-a-jour-3-7-dofus/) ; [ancien guide 2018–2019 pour contradictions](https://dofus.jeuxonline.info/article/14644/ventre-baleine).
- **Vidéo** : aucun lien YouTube 2025/2026 précisément authentifié sur ce boss malgré recherche. **Pas de métadonnées ni de transcription MCP M30 ; aucun timestamp oral attribué, aucune image inspectée.**
- **Statuts** : corrobore-source pour délai et descriptions du guide, hypothese pour phases serveur et composition, contradictoire et obsolete-pour-cible pour 5/10 ; **ni valide-test ni valide-live**.

## Règles atomiques utiles

| ID | Énoncé précis | Statut/source | Limite |
| --- | --- | --- | --- |
| M30-R01 | Tous les Krobes sont invulnérables et Malamibe intercepte leurs dommages. | corrobore-source [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) | Le type et l'ordre des effets envoyés par le serveur ne sont pas mesurés. |
| M30-R02 | 3 validations consécutives de la condition de restauration, remise à zéro sur échec, puis mort conditionnelle du Krobe. | corrobore-source [patch](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) + [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) | Phase du troisième tick à vérifier. |
| M30-R03 | Bacterrib : aucun ennemi à ≤5 PO **au début de son propre tour**. | corrobore-source [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) | Ancien guide « plus de 6 PO » discordant, tester frontière 5/6/7. |
| M30-R04 | Pataugerme : quatre éléments **Terre, Feu, Eau, Air** frappent pendant le cycle. | corrobore-source [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) | Les quatre doivent être **réellement appliqués**, Neutre hors liste ; frontières temporelles à vérifier. |
| M30-R05 | Tabacille : aucun soin, vol de vie toléré dans guide. | corrobore-source [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) | Proc automatique ou soin d'une invocation peut casser le tour. |
| M30-R06 | Verminocule : personnage au contact au début du tour du monstre, **invocation exclue**. | corrobore-source [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) | Compagnon à tester distinctement. |
| M30-R07 | Virustine : pas de déplacement ni tentative de retrait PM, y compris déplacement allié ; dégâts de poussée sans déplacement peuvent causer perte PM spéciale sans reset. | corrobore-source guide, exception à valider | [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). Tentative esquivée et dommage de poussée sont deux événements distincts. |
| M30-R08 | Protozorreur : état Infection entretenu pendant **6 tours** ; glyphe violet sous entité blessée et attraction du boss par **première frappe sur boss de son tour**. | corrobore-source [patch](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). | Expiration précise et mort au sixième tick à confirmer. |
| M30-R09 | Malamibe intercepte les dégâts, respawn après décès et peut tuer l'équipe après **2 tours complets** en vie. | corrobore-source [guide](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). | Début horloge Malamibe, relances et effets sur la mort d'un compagnon à vérifier. |

## État initial reproductible et schéma symbolique

- **Combat Krobe simple P1** : Krobe k vivant avec état invulnérable et compteur c=0 ; personnage joueur A vivant ; aucun autre acteur à distance ≤5 PO si k=Bacterrib ; initiative **A avant k**, autres événements exclus ; positions relatives définies par vrai moteur de distance et non par coordonnées non sourcées.
- **Combat boss B1** : boss P vivant, non infecté, hors de tout glyphe ; allié A reçoit un dommage et engendre une case de glyphe violet **g** (selon guide) ; joueur B aligné avec P **à travers g** ; **aucune attaque du tour courant sur P**. Le premier sort réussi de B sur P doit utiliser l'attraction spéciale jusqu'à g. Ressources/PA/PO exacts **inconnus** jusqu'à lecture des sorts client.
- **Malamibe m** : présente comme interception active, prochain tour avant ou après frappeurs selon initiative réelle ; seuil de vie basé sur effectif vivant rapporté par le guide (2000 par personnage/compagnon, invocations exclues), à contrôler pour client exact. Ne pas supposer une valeur si le combat/compte d'acteurs n'est pas connu.
- **Alternative négative** : autre frappeur C joue **avant** la mise en place et touche P : déclencheur « première frappe de tour » déjà consommé ; alignement créé ensuite trop tard.

## Machine à états par objet, transitions attendues

| Objet et état initial | Événement / acteur | Garde | Transition attendue | Source / inconnues |
| --- | --- | --- | --- | --- |
| Krobe ALIVE, c=0 | VALIDATE_CONDITION(k,tour) | condition vraie | c→1 ; invuln toujours active | M30-R02 ; événement serveur réel inconnu |
| Krobe ALIVE, c=1 | VALIDATE_CONDITION(k,tour suivant) | condition vraie | c→2 | M30-R02 |
| Krobe ALIVE, c=2 | VALIDATE_CONDITION(k,tour suivant) | condition vraie | c→3 → mort **conditionnelle** ; non déduction à partir de dégâts | M30-R02 |
| Krobe ALIVE, c∈{1,2} | VALIDATE_CONDITION | condition fausse | c→0, l'état survit | M30-R02 ; à reconfirmer côté serveur |
| Bacterrib k | BEGIN_TURN(k) | tout adversaire >5 PO | compte éligible ; si A à ≤5 PO → échec | M30-R03 |
| Verminocule k | BEGIN_TURN(k) | personnage à contact | compte éligible ; invocation au contact seule → échec | M30-R06 |
| Pataugerme k | HIT_APPLIED, fin cycle | 4 éléments présents dans ce cycle | compte éligible ; un élément manquant → échec | M30-R04 ; frontières du cycle non confirmées |
| Tabacille k | HEAL_APPLIED (sur un joueur) | tout soin ordinaire | condition cesse d'être satisfaite ; vol de vie reste **exception proposée**, à vérifier | M30-R05 |
| Virustine k | MOVE_APPLIED ou PM_REMOVAL_ATTEMPT | mouvement / tentative de retrait | reset ; collision sans déplacement = chemin particulier à tester | M30-R07 |
| Protozorreur UNINFECTED | damage player → violet g → FIRST_HIT_ALIGNED(P) | g sur chemin d'attraction et première frappe | déplacement spécial dans g, état Infection, démarrage/entretien du compteur cP | M30-R08 |
| Protozorreur INFECTED cP=1..5 | tick de validation | Infection encore active | cP+1 ; à 6, mort conditionnelle | R08 ; phase réelle inconnue |
| Protozorreur INFECTED | expiration sans renouvellement | Infection perdue | cP=0, retour UNINFECTED hypothèse guidée par restauration | R08, replay requis |
| Malamibe ABSENT / DEAD | premier ennemi autorisé prend tour | conditions d'invocation | SUMMONED (nouvel UID), cible des interceptions | R09 |
| Malamibe SUMMONED | dommages sur Krobe | monster invuln, interception active | dommages reportés à Malamibe ; k garde PV / invuln | R01,R09 |
| Malamibe SUMMONED | délai atteint | survit pendant 2 tours complets | risque mort d'équipe ; statut win/loss à confirmer par événement | R09 |

## Plans par taille, acteurs et chronologie

| Format | Plan positif proposé | Changement d'ordre qui invalide / option de repli |
| --- | --- | --- |
| Solo | Pour Bacterrib, A termine hors rayon interdit avant BEGIN_TURN(k), c=1 puis 2 puis 3. Sur Tabacille, A évite tout soin (risque survie). | Monstre avance/attire A **avant son début de tour** : P faux ; privilégier survie, revoir compteur plutôt que simuler mort. |
| Duo | A pose un joueur au contact de Verminocule, B frappe Malamibe/couvre un élément de Pataugerme ; tous deux terminent avant BEGIN_TURN(k). | Contact placé **après BEGIN_TURN(k)** ne sauve pas la validation de ce tour. Retarder la progression, éviter double usage d'un acteur. |
| Trio | A provoque un dommage sur lui pour poser glyphe g ; B aligne et frappe en premier P ; C frappe Malamibe et assure survie. | C frappe P **avant** B : le déclencheur de première frappe est consommé, pas d'attraction ultérieure ; repli utiliser le prochain tour pour infecter. |
| Quatuor | A protecteur, B placeur, C et D couvrent quatre éléments / éliminent Malamibe avant délai ; priorité à ne pas casser la restauration en soignant Tabacille ou déplaçant Virustine. | Heal de A entre deux actions annule Tabacille ; déplacement allié du monstre Virustine annule ; replanifier séparément les Krobes. |

**Ressources** : chacun paie ses propres PA, PM, PO et relances ; aucune valeur de coût spécifique n'est supposée. **Challenge** : contrôler l'identité de l'acteur, y compris compagnon, avant d'ajouter une condition ; les challenges ne sont pas prouvés ici.

## Tests proposés, non exécutés

| ID | Entrées / actions | Résultat attendu | Discrimination |
| --- | --- | --- | --- |
| **P1** | Bacterrib, 3 BEGIN_TURN successifs avec aucun ennemi ≤5 PO. | Compteur 1→2→3 ; mort conditionnelle à la phase réelle de troisième validation. | R02/R03, nouveau délai 3 vs vieux 5. |
| **N1** | Même scénario mais un seul ennemi à 5 PO au deuxième BEGIN_TURN. | Pas de troisième mort au prochain début ; compteur reset 0 puis nouvelle séquence requise. | R02, distance et reset. |
| **N2** | Bacterrib, frontière 5/6/7 PO + invocation ennemie. | Résultat différentiel **à mesurer** ; préciser les distances et types reconnus. | Incohérence de guides pour seuil/profil. |
| **P2** | Pataugerme frappé terre/feu/eau/air dans chaque cycle x3, avec sources éligibles. | 3 validations puis mort conditionnelle. | R04, suivi ensemble des éléments. |
| **N3** | Pataugerme terre/feu/eau/neutre, air manquant. | Échec de validation, aucun incrément de compteur. | Neutre ≠ Air. |
| **N4** | Tabacille : pas de soin ordinaire 2 tours puis soin passif ou compagnon lors du troisième. | Reset si ce type de soin est admissible ; **résultat à mesurer**, distinguer vol de vie. | R05, acteur du soin. |
| **P3/N5** | Verminocule, personnage au contact à 3 débuts de tour ; variante seule invocation au contact. | P3 valide, N5 pas de validation si invocation seule. | R06, identité et timing. |
| **N6** | Virustine : comparer retrait PM tenté mais esquivé, poussée contre obstacle sans déplacement, déplacement réussi. | Résultat exact de l'exception et tentatives **à mesurer** ; ne pas présumer du décompte. | R07, événements distincts. |
| **P4** | Boss infecté sur six ticks de condition avec renouvellement avant expiration. | Mort après 6, non 10 ; cycle exact à certifier. | R08, durée patchée. |
| **N7** | Boss sans Infection pendant une phase, ou première frappe du tour avant alignement/glyphe. | Pas d'attraction spéciale tardive, compteur incomplet / reprise requise. | R08 et initiative. |
| **N8** | Malamibe réinvoquée (nouvel UID), les joueurs attendent les 2 tours complets. | Danger d'OS d'équipe, timer d'instance distinct ; replay pour horloge exacte. | R09. |

## Passage au développeur

Données : krobe UID/type, compteur/durée, predicate/evaluation phase, état infection, glyphID, acteur de dommage, première frappe sur boss par tour, origine du mouvement/chemin, composition et ordre initiative, Malamibe UID et timer, séparation dégâts/interception/mort conditionnelle. Machine à états **prévisionnelle** ; le serveur reste l'autorité pour position, effet et mort. Échecs/refus/Unknown de cast invalident les plans dépendants de l'action. Aucune validation/test réel.

**Reprise** : établir par replay 3.7.13.17 l'instant de troisième/sixième validation, le seuil PO Bacterrib, les événements Virustine, le rôle du compagnon au contact et l'horloge Malamibe.
