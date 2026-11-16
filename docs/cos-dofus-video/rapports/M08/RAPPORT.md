# M08 — Quatre comptes : le placeur avant ou après le frappeur

État : **un scénario de coordination documenté**, sans preuve vidéo visuelle et sans exécution. Date : 2026-11-04. Auteur : worker-5. Équipe étudiée : Pandawa / Éliotrope / Féca / Iop, niveau 200 pour rendre Colère de Iop disponible. La vidéo illustre un trio Panda–Élio–Iop avec quatrième rôle flexible ; elle ne prouve pas le scénario de plateau décrit ici.

## Corpus vérifié et versions

| Réf. | Vidéo / source | Vérification effective | Preuve et restriction |
| --- | --- | --- | --- |
| V08-A | Tenmalexis, [La MEILLEURE TEAM de 4 sur DOFUS en 2025 ?!](https://youtu.be/YFlyjdSo4Ek), publiée **2025-05-02**, 16 min, présentée comme **MAJ 3.1**, description mentionnant aussi « 2.70 » | `get_video_info`, `get_available_languages` : FR original et auto ; `get_timed_transcript(lang=fr)` : **225 fragments, curseur null**, parcourus pour les intervalles 01:14–02:45, 04:40–07:15, 08:07–09:35 et 10:25–11:55 | [01:19–02:28](https://youtu.be/YFlyjdSo4Ek?t=79) : trio placeur Panda, Élio soutien/portails, Iop dégâts ; [03:38–04:12](https://youtu.be/YFlyjdSo4Ek?t=218) : Féca proposé pour protéger le Panda ; [04:48–05:28](https://youtu.be/YFlyjdSo4Ek?t=288) : démo annoncée avec Panda, portail Élio et préparation Iop ; [06:19–07:18](https://youtu.be/YFlyjdSo4Ek?t=379) : exemple au tour 4, Brassage, Élio, puis Colère/Destin Iop. Ce sont des **déclarations du sous-titre**, pas des états de plateau lus. |
| V08-B | Tenmalexis, [LES MEILLEURS TEAM DE 4 SUR DOFUS UNITY !?](https://youtu.be/RHEcA6s0a1A), publiée **2024-11-23**, ~17 min, avant lancement Unity | `get_video_info` + langues FR disponibles ; **transcript non prélevé** car V08-A suffit au cas précis | Description chapitre [04:34 Panda Élio Féca Iop](https://youtu.be/RHEcA6s0a1A?t=274). Source de contexte, **pas** une confirmation de règle ou de rotation. |
| S08-1 | [Karcham Pandawa](https://openddb.dev/spells/12787-karcham), [Brassage Pandawa](https://openddb.dev/spells/12816-brassage) | Fiches consultées 2026-11-04, extraction version exacte non affichée | Karcham 1 PA pour porter ou jeter une cible adjacente au portage ; Brassage 2 PA, état **Sobre** applique dommages subis ×110 % sur la cible. |
| S08-2 | [Portail Éliotrope](https://openddb.dev/spells/14574-portail) | Fiche consultée | Portail 1 PA, rang 3 portée 1–6 PO, deux lancers/tour ; nécessite plusieurs portails actifs pour projeter les sorts. Les ennemis ne peuvent pas emprunter un portail au premier tour, risque ensuite. |
| S08-3 | [Bouclier Féca](https://openddb.dev/spells/12982-bouclier-feca) | Fiche consultée | Protection d'un allié, 2 PA, rang 2 portée 0–5 PO, relance 3 tours, dommages subis ×75 % selon conditions de sort. |
| S08-4 | [Colère de Iop](https://openddb.dev/spells/13124-colere-de-iop), [Puissance](https://openddb.dev/spells/13118-puissance) | Fiches consultées | Colère 7 PA, portée **1**, relance **3 tours**, bonus au retour de sort ; Puissance 3 PA et bonus sur le bénéficiaire. |
| S08-5 | [Dofus Grimoire Pandawa 3.7](https://dofus-grimoire.fr/classes/pandawa/) ; [Dofuspourlesnoobs, mise à jour 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) | Recoupement du premier pour porter/jeter (1 PA chaque), contexte de la mise à jour 3.7 du 2026-10-06 | Les fiches sont **tiers** ; version du client de référence local annoncée comme **3.7.13.17**, mais aucun export client contrôlé ici. La vidéo 3.1 n'établit pas les coûts et cooldowns 3.7. |

**Inspection visuelle :** impossible dans cette session ; `browser_tabs` renvoie `BROWSER_NOT_DISPATCHED: the extension did not claim this request before its deadline.` Aucune capture ni horodatage d'image n'a donc été validé. Les scores de dégâts cités oralement en vidéo (vers 07:05) **ne sont pas attribués au présent plan** : équipement, cible, états et montages inconnus.

## Règles atomiques priorisées

- **M08-R01 — Le placement avant l'attaque ouvre une action qui était géométriquement interdite.** Le Pandawa peut (si sobre et cible à 1 PO) `Karcham porter (1 PA) → Karcham jeter (1 PA)`, puis `Brassage (2 PA)` sur la cible. `corrobore-source` pour les coûts/rôles (S08-1, Grimoire) ; `hypothese` pour les cellules et le résultat exact de la fixture. Une cible jetée à portée du Iop doit être **confirmée** avant d'autoriser une Colère.
- **M08-R02 — Le cumul de soutien n'est effectif que pour les bénéficiaires et tours concernés.** Brassage en état Sobre vise les dommages **subis par l'ennemi** ; Bouclier Féca protège le **Pandawa** ; les Portails aident les sorts **effectivement projetés** ; Puissance augmente les dégâts du **Iop** s'il se cible et attaque ensuite. `corrobore-source` pour ces effets ; `hypothese` sur la fenêtre de validité et leur cumul, faute de client 3.7.13.17. Voir V08-A [02:04–02:28](https://youtu.be/YFlyjdSo4Ek?t=124), [04:48–05:28](https://youtu.be/YFlyjdSo4Ek?t=288).
- **M08-R03 — Ne jamais emprunter le budget d'un autre compte.** Un Iop à 12 PA et 6 PM en fixture peut dépenser 7 PA sur Colère si la cible est à 1 PO ; si Iop agit **avant** le Pandawa, le jet ne peut pas lui donner rétroactivement une cible. La défense Féca ne vaut qu'à partir de son propre lancement. `corrobore-source` pour les coûts (S08-2 à S08-4) et `hypothese` pour la rotation. V08-A [06:19–07:18](https://youtu.be/YFlyjdSo4Ek?t=379) énonce une préparation jusqu'au tour 4, sans prouver les initiatives exactes.

## Scénario de test relatif — exactement quatre personnages

**Fixture et non observation :** P=Pandawa `(5,0)` **Sobre**, ennemi boss E=`(6,0)`, I=Iop `(0,0)`, L=Éliotrope `(1,3)`, F=Féca `(4,3)`. Grille 4-voisins, cellules de déplacement `(1,0), (2,0), (3,0), (4,0)` vides initialement, lancé vers `(4,0)` disponible, chemins/LdV des soutiens valides. Chaque allié **individuellement 12 PA / 6 PM** au début de **son** tour (hypothèse de build d'essai), tous sorts appris, rang/variantes compatibles, relances prêtes, aucune invulnérabilité/mécanique de boss et aucune interruption ennemie dans la rotation simulée. Monstre mobile, états initiaux vides, aucun challenge restrictif. L'exemple décrit des étapes contrôlables ; les cas réels entre tours T1 et T4 dépendent des mouvements/attaques adverses.

### Plan principal — initiative P → L → F → I

| Tour | Acteur | Action et cible | Coût propre | Ressources propres après | Transition et preuve |
| --- | --- | --- | --- | --- | --- |
| T1 | P | Karcham sur E(6,0) — porter | 1 PA, 0 PM | 11 PA / 6 PM | E porté par P, [Karcham](https://openddb.dev/spells/12787-karcham) ; vérifier état porteur |
| T1 | P | Karcham — jeter E sur (4,0) | 1 PA, 0 PM | 10 PA / 6 PM | E attendu (4,0) **si lancer légal et confirmé** ; vérifier cellule libre |
| T1 | P | Brassage sur E, P Sobre | 2 PA, 0 PM | **8 PA / 6 PM** | E marqué comme vulnérable selon effet de sort, [Brassage](https://openddb.dev/spells/12816-brassage) |
| T1 | L | Portail A=(1,2), B=(4,2) | 1+1 PA, 0 PM | **10 PA / 6 PM** | Deux portails si cellules, portée et LdV acceptées ; routage exact **non prouvé** |
| T1 | F | Bouclier Féca sur P(5,0) | 2 PA, 0 PM | **10 PA / 6 PM** | Protège P après confirmation si 5 PO/rang et LdV ; [Bouclier](https://openddb.dev/spells/12982-bouclier-feca) |
| T1 | I | Marche (0,0)→(3,0), puis Colère sur E(4,0) | 3 PM puis 7 PA | **5 PA / 3 PM** | Attaque au contact, déclenche la relance Colère selon [fiche](https://openddb.dev/spells/13124-colere-de-iop) |
| T2–T3 | P/L/F/I | Tours intermédiaires **sans rotation fixée** : préserver la cible et les deux personnages exposés, vérifier états ennemis, cooldowns et portails | Coûts **inconnus** tant que les actions ne sont pas choisies | À recalculer **pour chaque acteur** | Le plan T4 est suspendu si E fuit, meurt, modifie ses résistances ou si l'un des acteurs est hors combat |
| T4 | P | Si E toujours (4,0), Sobre, 1 PO : Brassage sur E | 2 PA, 0 PM | **10 PA / 6 PM** | Vulnérabilité de ce tour ; si E a bougé : coût de re-placement et position à **recalculer**, ne pas supposer 2 PA |
| T4 | L | Maintenir/contrôler les portails actifs ; option recréer A et B si possible | 0 PA si conservés, sinon **2 PA** | **12 ou 10 PA / 6 PM** | Effet du routage à tester, aucune attaque à travers les portails présumée |
| T4 | F | Si nécessaire/relance valide, Bouclier sur P ; sinon défense conservatrice | 2 PA si jeté | **10 PA / 6 PM** si jeté | Protection selon placement, durée et états réels |
| T4 | I | Puissance sur I puis Colère sur E **si toujours au contact et sort récupéré** | 3+7 PA, 0 PM | **2 PA / 6 PM** si pas de mouvement | Boost bénéficie au Iop ; Colère revient en principe après sa relance 3 tours ; **bonus chiffré/dégâts finaux non calculés** |

**Mise au point essentielle :** les positions T4 sont un **préétat de fixture** à reconstruire après T1–T3 ; il est interdit de lire T1 comme preuve que les acteurs ont ces positions 3 tours plus tard. Les PA/PM affichés à T4 ne tiennent que si chaque acteur recommence le tour à 12/6, sans malus/bonus, et si aucun déplacement intermédiaire n'est requis. Une guérison ou une vulnérabilité ne doivent jamais être appliquées simplement parce que le sort est présent dans la liste de priorités.

### Contre-plan — I → P → L → F

Au T1, I commence (0,0), E est encore (6,0) et P bloque la case (5,0). Une Colère au contact demande une cellule adjacente libre. La cellule `(5,0)` est occupée ; atteindre `(6,1)` ou `(6,-1)` demande **7 PM** sur cette grille, au-delà des **6 PM** du Iop. Il n'a donc **aucun lancement légal de Colère sur E** dans cette fixture. L'action Pandawa ultérieure (E(6,0)→E(4,0)) ne change pas rétroactivement le tour du Iop.

Plan dégradé : I lance éventuellement **Puissance sur I (3 PA, 0 PM)**, garde 9 PA et se place en sécurité (PM à calculer selon cellule réelle) ; P, L, F jouent leur séquence de placement/protection ensuite ; I retente Colère **au prochain tour légal**, et le futur bonus de récupération sera également décalé. Un retour de Colère au T4 ne peut être annoncé si le premier lancer n'a jamais eu lieu.

## Comparaison avec quatre actions indépendantes

- **Ordre P avant I :** ouvre la légalité de Colère (7 PA) dès T1 avec les 2 PA de portage/jet et jusqu'à 2 PA de Brassage ; tous les budgets sont **séparés**. Le coût commun n'est pas un pool de 48 PA : les contraintes de portée restent propres à chaque personnage.
- **Ordre I avant P :** Colère T1 devient interdite dans le cas, donc retard de l'attaque et de son propre cooldown ; un boost personnel T1 peut être moins intéressant s'il expire avant l'attaque.
- Le **gain de dégâts brut** d'un Brassage sur une attaque est conditionné au statut de cible, à la durée et aux règles de cumul ; l'effet source indique ×110 %, **mais on ne calcule ni DPS ni dégâts garantis** sans jeu d'états et de statistiques. La protection Féca est un gain de survie potentiel de P, pas un multiplicateur de dégâts du Iop ; les portails Élio ouvrent d'autres options, mais la trajectoire n'est pas démontrée.
- Risques : vulnérabilité expire avant I, F trop loin pour protéger P, P passe Saoul et Brassage change d'effet, cible invulnérable/indéplaçable, cible s'échappe entre T1–T4, portail utilisé par ennemi après premier tour, challenge interdisant une action, F ou L joue trop tard pour prévenir le dommage ennemi.

## Actions immédiates pour développeur

Implémenter une **dépendance d'ordre** entre déplacement certifié et utilisation d'une attaque à PO 1. Ingestion de l'état porte/jette E et des budgets P séparés ; vérifier ressource et effet source avant toute attaque I. Le tour prochain n'hérite pas des PA actuels et un sort en relance ne doit jamais être présumé disponible. Tests détaillés dans la fiche et le passage. **Aucune exécution**. Prochaine preuve indispensable : inspection de la démo [V08-A 04:48](https://youtu.be/YFlyjdSo4Ek?t=288) et [06:19](https://youtu.be/YFlyjdSo4Ek?t=379), plus données du client 3.7.13.17.
