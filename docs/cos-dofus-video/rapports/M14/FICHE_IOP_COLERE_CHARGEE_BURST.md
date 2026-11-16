# M14-F01 — Fenêtre de Colère, charge et coût du contact

## Identité / preuve

- ID stable `M14-F01`. Classe **Iop**, niveau 200, cas terrestre Colère de Iop avec variante Fureur **non choisie**. Cible : ennemi E, alliés potentiels L (Éliotrope), P (Pandawa), F (Féca), Z (Zobal), N (Eniripsa).
- Versions : tutoriel Tenmalexis [*team de quatre* du 02/05/2025](https://www.youtube.com/watch?v=YFlyjdSo4Ek), Dofus 3.1 annoncée ; guide XAWPIYA [*duos Iop 2026* du 15/07/2026](https://www.youtube.com/watch?v=d89eRr0ciHQ), description uniquement ; guide XAWPIYA [*team quatre 2026* du 08/06/2026](https://www.youtube.com/watch?v=VCVjlE8pVcI), sous-titres FR automatiques accessibles ; [Grimoire Iop 3.7.12](https://dofus-grimoire.fr/classes/iop/) et [diff 3.6–3.7](https://backfromafk.com/classes/iop/depuis-3-6/) ; client de référence local **3.7.13.17 non testé**.
- MCP : `get_video_info` et langues disponibles sur les trois ; transcriptions **225** fragments Tenmalexis et **294** XAWPIYA team, `next_cursor=null`. XAWPIYA duo retourne `Could not retrieve a transcript ... YouTube is blocking requests from your IP`, donc **aucun intervalle transcrit** de cette vidéo.
- Passages réellement examinés : [Tenmalexis 05:13–05:28](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=313s), [06:19–06:32](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=379s), [07:00–07:25](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=420s) ; [XAWPIYA 2026 01:05–01:45](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=65s), [02:37–02:55](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=157s). Les autres affirmations proviennent des fiches tierces, **pas des vidéos**. Aucun passage visuel inspecté.

## Règles atomiques

| Règle | Formulation précise | Statut | Source et réserve |
| --- | --- | --- | --- |
| **M14-R01** | Colère (au niveau 200) **7 PA**, **1 PO**, **3 tours relance**, dégâts de base indiqués 81–100 Terre premier lancer, 191–210 au retour si charge réelle. | `corrobore-source` guide 3.7.12 ; client local `hypothese` | [Dofus Grimoire Iop](https://dofus-grimoire.fr/classes/iop/), section « La grosse Colère, trois tours plus tard » |
| **M14-R02** | Puissance rang 3 **3 PA**, booste sa cible ; Massacre **2 PA**, augmente dommages reçus par E : **3+2+7 = 12 PA** au T4 si tous les sorts légaux. | `corrobore-source`, somme analytique | [Grimoire Iop](https://dofus-grimoire.fr/classes/iop/) ; ordre d'application/durée à certifier sur 3.7.13.17 |
| **M14-R03** | Bond **4 PA**, à **1–5 PO** au rang haut, cible case libre, sans LdV ; préparer contact en dépensant PA plutôt que PM rend certains boosts inaccessibles. | `corrobore-source` pour descriptif, comparaison `hypothese` | [Grimoire Bond](https://dofus-grimoire.fr/classes/iop/). |
| **M14-R04** | Tenmalexis fait référence à une charge préparée et à un gros tour vers T4 avec Pandawa et Éliotrope, mais ses cellules et PA réels n'ont pas été observés. | `declare-video` | [05:13](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=313s), [06:19](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=379s), [07:00](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=420s), version **3.1** |
| **M14-R05** | Le burst « chargé » n'est légal que si le **premier sort exécuté** et le cooldown futur ont été confirmés ; refus, interruption ou état inconnu invalident le suffixe automatique du plan. | `hypothese` de planificateur à tester | Il faut traces d'état du combat et du serveur ; pas de `valide-test`. |

## État initial reproductible

Fixture **hypothétique** (non tirée d'une vidéo) :

```text
I = Iop (0,0)           12 PA / 6 PM au début de chaque tour I
E = ennemi (1,0)        vivant, mobile, vulnérable, résistances inconnues
Plateau orthogonal, une case = une unité de déplacement.
Ni mur ni tacle, LdV valide, pas de challenge restrictif.
Niveau I >= 200 ; Colère choisie contre Fureur, rangs hauts ;
Puissance, Massacre, Pression, Concentration équipés, hors relance.
T2 et T3 : état inconnu, AUCUNE position E présumée durable.
```

## Chronologie positive T1 → T4

| Étape | Acteur | Sort / cible | Conditions | Consommation propre | État après la transition confirmée |
| --- | --- | --- | --- | --- | --- |
| T1.1 | I | **Colère** E(1,0) | PO 1, cooldown prêt | **7 PA**, 0 PM | I **5 PA/6PM** ; charge/cooldown seulement sur résolution |
| T1.2 | I | **Pression** E | E toujours vivant/à portée | **3 PA**, 0 PM | I **2 PA/6PM** ; érosion si effet réussi |
| T1.3 | I | **Concentration** E | E encore adjacent ; variante disponible | **2 PA**, 0 PM | I **0 PA/6PM** |
| T2 | I + autres | actions libres nécessaires à survie | nouvelle initiative/positions | **unknown** | relance/charge suivies, E libre de bouger |
| T3 | I + autres | idem | idem | **unknown** | relecture avant T4 |
| T4.1 | I | **Puissance** sur I | I récupère 12PA/6PM **si aucune altération**, sort prêt | **3 PA**, 0 PM | I **9PA/6PM**, boost **sur I** |
| T4.2 | I | **Massacre** E | cible atteignable, sort prêt | **2 PA**, 0 PM | I **7PA/6PM**, effet **sur E**, pas sur I |
| T4.3 | I | **Colère** E | E encore à 1 PO, charge T1 et cooldown confirmés | **7 PA**, 0 PM | I **0PA/6PM** ; dommages chargés à mesurer réellement |

Source des coûts [Grimoire Iop 3.7.12](https://dofus-grimoire.fr/classes/iop/). Le **nombre de tours** signifie trois tours de relance après T1 ; on ne réutilise pas le sort au T2/T3 et le retour T4 est **conditionnel au calendrier exact du jeu**. Aucun jet de dommages observé ou calculé sur un adversaire concret.

## Comparaison : marche / Bond / fenêtre ratée

| Scénario T4 | Géométrie hypothétique | Dépense | Restant après Colère | Ce que le simulateur peut conclure |
| --- | --- | --- | --- | --- |
| **I au contact de E** | I(0,0), E(1,0), aucune marche | Puissance3+Massacre2+Colère7 | **0 PA/6PM** | Burst complet admissible si les trois sorts sont légaux |
| **I éloigné de cinq PM du contact** | I(0,0), E(6,0), (1–5,0) libres, sans tacle | Marche jusqu'à (5,0) = **5 PM** + 3+2+7 PA | **0 PA/1PM** | Même combo PA mais I exposé ; danger à estimer avant marche |
| **Bond requis** | I(0,0), E(6,0), I peut bondir en (5,0) après mouvement préparatoire vers portée, destination libre | **Bond 4 + Colère 7 = 11 PA**, 0 PM dépensé par Bond | **1 PA/6PM** si Bond depuis une case d'où (5,0) est à ≤5 PO | Les 3+2 PA de buffs ne rentrent pas : 3+2+4+7=**16 > 12** ; perte de fenêtre optimisée |
| **Contact impossible** | I(0,0), E(6,0), 0 PM, (5,0) occupée, autres cases adjacentes à distance >5 | Bond sans destination au contact, puis Colère | **Colère interdite** | Reporter ; aucun 7 PA ni charge/dégât fictifs |

Pour le cas Bond « 0 PM », I doit être au moins à une position où la cellule (5,0) entre dans la portée de Bond ; dans les coordonnées affichées elle est à 5 PO et **est** directement atteignable si libre. La distance et la LdV sont vérifiées avant le lancement ; la fiche **ne présume pas** que les portails permettent de franchir des obstacles sans changement de sort.

## Solo / duo / trio / quatuor / davantage

| Taille | Acteurs et initiative, hypothétiques | Étapes et ressources | Bénéficiaire/risque |
| --- | --- | --- | --- |
| **Solo** | I→E | T1 7+3+2 ; T4 3+2+7 à **12 PA/6 PM propres** | I doit rester vivant et au contact ; si E part, Bond remplace deux boosts éventuels |
| **Duo** | **Élio → I**, ou **Zobal → I**, ou **Eni → I** (partenaires décrits dans V14-2) | Allié prépare portail, bouclier ou soin (ses **PA/PM unknown**) ; I garde ses 12PA/6PM de fixture au début de son tour | Portail ne prouve pas le ciblage direct Colère ; bouclier survie, soin après dégât ne le prévient pas |
| **Trio** | **Pandawa → Élio → I** | Panda place E, Élio ouvre accès **avant** I, puis I vérifie PO 1 et puissance/cooldown | Sans confirmation serveur de placement, ne jamais précompter la Colère |
| **Quatuor** | **Pandawa → Élio → Féca → I**, ou inverse **I → Panda → Élio → Féca** | Versions A/B à tester : I peut bénéficier des effets antérieurs, **jamais rétroactivement** ; chaque allié tient son budget propre `unknown` | Si Féca joue après l'attaque adverse, sa protection ne la réduit pas |
| **5+** | toute classe alliée supplémentaire | Graphe dynamique d'opportunités, pas de combo codé en dur | invocations, obstacles et conditions de challenge changent le tour |

## Tests discriminants proposés — non exécutés

| ID | Variation | Actions | Résultat observable attendu |
| --- | --- | --- | --- |
| **M14-T+** | I(0,0), E(1,0) aux T1 et T4, sorts prêts, 12PA/6PM au départ | T1 Colère+Pression+Concentration ; T4 Puissance+Massacre+Colère | 12 PA dépensés à chaque fenêtre, Colère T4 **chargée uniquement si T1 réussi** et relance résolue |
| **M14-T-refus** | Identique mais Colère T1 explicitement refusée | T1 tentative échoue puis T4 | Pas de charge créée, pas de bonus +110 inventé |
| **M14-T-unknown** | ACK commande T1 sans résultat serveur | T1 puis calcul T4 | État de charge `Unknown`, plan chargé non certifié |
| **M14-T-bond** | E=(6,0), I=(0,0), (5,0) libre, Bond disponible et 12PA | Bond4 puis Colère7 | **1PA restant**, impossible Puissance+Massacre ce même tour |
| **M14-T-block** | E=(6,0), I=(0,0), **0 PM**, (5,0) occupée, autres cases adjacentes E hors portée Bond | tenter Bond puis Colère | Aucune position valide à 1 PO, **Colère illégale** |
| **M14-T-cooldown** | Colère T1 résolue, tentative T3 | Relancer au T3 | Refus tant que relance non terminée, aucun dégât/crédit PA fictif |
| **M14-T-variant** | Fureur sélectionnée, Colère indisponible | Demander Colère | Rejet d'admission de sort avant consommation |
| **M14-T-ally-order** | Duo/4 : Élio ou Panda joue après I | I tente Colère selon position future | Refus si hors PO maintenant ; aucune anticipation d'effet allié non encore résolu |
| **M14-T-survie** | Contact E fatal à I selon horizon ennemi | Comparer burst immédiat et report avec protection | Décision report préférée si contraintes de survie le montrent ; calcul exact inconnu |

Pour **chaque test** : `proposé, non exécuté` ; pas de valeur `valide-test` ou `valide-live`.

## Passage développeur / reprise

Lire `fightEpoch`, `turnOwner`, `actorPA/PM/PV` par compte, `spellVariant`, `cooldownReady`, `chargeState` confirmé par le jeu, `lineOfSight`, `targetCell`, `walkability`, `targetResistances`, `buffRecipient`, `vulnerabilityTarget`, `allyOrder`, `challengeState`. Distinguer « premier lancement », « Colère prête T4 », « vrai dommage », « annulation » et « Unknown ». Examiner caméra/plateau de [Tenmalexis 05:13](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=313s) et [07:00](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=420s) quand l'outil visuel le permet ; revoir transcription V14-2 quand blocage YouTube disparaît ; comparer les valeurs avec le client **3.7.13.17**.
