# P01 — Interdiction de soins et vol de vie : état 76 et oracles documentaires

**Mission documentaire, 4 novembre 2026.** Cible : client DOFUS **3.7.13.17**, build **2026-11-03T14:09:52+00:00**. La lecture supplémentaire des bundles installés **SpellStatesDataRoot / SpellsDataRoot / SpellLevelsDataRoot** apporte des identifiants concrets qui manquaient à N08. Aucun combat, compte, session, test, compilation ou modification du moteur n'a été effectué.

## Résultat probatoire

1. **Donnée client extraite : état 76 = Insoignable.** La ligne native SpellStateData(id=76) porte nameId=65291, descriptionId=1183851, icon=stateInsoignable, **incurable=1** et displayTurnRemaining=1. Le texte localisé indique que l'état empêche de récupérer de la vie. Cet indicateur est une preuve client sérieuse d'interdiction des gains de PV, mais **pas une capture serveur** de chacune des catégories ci-dessous.
2. **Contre-exemple : état 5920 ≠ Insoignable.** SpellStateData(id=5920), nommé CountsKills, possède aussi icon=stateInsoignable, alors que **incurable=0**. Un moteur qui décide l'interdiction sur l'icône, un nom d'effet ou un ID deviné serait incorrect dès la lecture des données. La clé pertinente est l'identité exacte de l'état réellement observé sur le combattant.
3. **Lien concret entre état et sorts :** des SpellLevelData du client associent actionId=**950**, value=**76**, à la pose et actionId=**951**, value=**76**, au retrait. Le code du résolveur local traite lui aussi 950 comme ajout, 951/952 comme retrait (SpellResolver.cs:403-412). Ce rapprochement code/donnée ne démontre pas l'application, le rejet, la dissipation ou l'expiration par le serveur.
4. **Portée de l'extraction :** sur les 34 318 SpellLevelData du bundle officiel, la recherche ciblée a relevé **231 lignes d'effets** (grades et branches compris) action 950/value 76, **20** action 951/value 76, et respectivement **1** action 950/value 5920 et **1** action 951/value 5920. Ce sont des *lignes sérialisées*, pas 231 sorts distincts ni des lancers observés. Une jointure sur les 17 006 SpellData a relevé une référence de niveau au spellId 30742 sans SpellData correspondant : elle reste non attribuée. L'extraction N01 ne portait que sur quatre classes et ne contenait **aucune** ligne action 950/951/value 76 pour ces classes ; il fallait donc lire le bundle complet.
5. **Soutien externe à portée limitée :** [Huzounet, Les états sur Dofus](https://huzounet.fr/guides/les-etats-sur-dofus) et [Dofus pour les Noobs, États](https://www.dofuspourlesnoobs.com/eacutetats.html), consultés le 04/11/2026, décrivent l'interdiction de récupérer des PV par **soins et vol de vie**. Leur texte ne fournit ni reçu serveur sur 3.7.13.17, ni calendrier de déclenchement, ni décision explicite sur les boucliers ou les changements de PV maximum.

## Familles à distinguer avant toute simulation

| Famille | Exemple traçable 3.7.13.17 | Preuve et conséquence prudente |
| --- | --- | --- |
| **Soin direct** | Mot de Reconstitution (spellId **13187**, grade 1, levelId **41440**) : effet normal **1109**, diceNum=100, targetMask=a, order=0, puis **950/value 76**, duration=4, order=1 ; pas de branche critique, criticalHitProbability=0. | Donnée client : séquence enregistrée *soin puis état*. L'ordre effectif et la condition de soin sur état préexistant requièrent le serveur. |
| **Soin direct % PV maximum** | Cicatrisation (**14587**, grade 1, levelId **44378**) : action **1109**, normal diceNum=6 / critique 8, targetMask=a. | Le code SpellResolver.cs:370-382 utilise MaxLife cible pour 1109 ; ce code est un modèle local, pas un oracle serveur. |
| **Vol de vie** | Arnaque (**12911**, grade 1, levelId **40910**) : action **93**, normal dice=16–18, critique 19–21, targetMask=a,A. | SpellResolver.cs:344-368 inflige des dégâts puis appelle Heal sur le **lanceur** pour des actions 91–95. L'interdiction éventuelle porte sur le combattant qui **reçoit** les PV, distinct de la cible des dommages. |
| **Régénération / soin déclenché** | Affliction (**14584**, grade 1, levelId **44369**) : soin action **2998** immédiat trigger=I et autre 2998 trigger=**TB**, côté normal et critique, avec vol de vie 91 dans d'autres lignes. | Le second soin est conditionné par un déclencheur, non assimilable à une guérison immédiate. Le moment exact de TB et l'état 76 à cet instant restent **Unknown**. |
| **Bouclier** | Roublardise (**13431**, grade 1, levelId **41883**) : action **1020**, diceNum=150, duration=1, targetMask complexe, criticalHitProbability=0. | Le modèle local crée ShieldResolved sans Heal (SpellResolver.cs:389-394). Le champ incurable ne constitue pas à lui seul une preuve serveur que le bouclier peut être appliqué sous 76. |
| **PV maximum / érosion** | SpellStateData **76** contient incurable=1 ; son enregistrement ne porte aucune règle de modification chiffrée de MaxLife. | **Ne pas confondre** blocage du gain de PV, réduction des PV max, érosion, PV manquants et protection. Une trace distinguant Life/MaxLife/Shield est obligatoire avant d'affirmer leur interaction. |

Les actions 950/951 opèrent sur des *états* ; l'action 1109 soigne, 93 appartient au vol de vie selon le code lu, et 1020 aux boucliers. La présence d'un actionId dans un niveau ne prouve ni qu'une cible est éligible (masques/ligne de vue/états), ni que l'effet a été effectivement exécuté.

## Table d'IDs et branches vérifiables

| Source SpellLevelData | Grade et branche | Effet/condition constatés | Statut |
| --- | --- | --- | --- |
| **13187 / 41440** Mot de Reconstitution | 1 normal, critique vide | uid 209838 action 1109 diceNum 100 order 0 ; uid **209839** action **950/value 76**, duration **4**, order 1 ; targetMask=a | Client |
| **1173 / 6331** Blopagation | 1 normal / critique | uid **35490** action 950/76 durée **1** ; uid **35491** action 950/76 durée **2** ; mask=A, criticalHitProbability=5 | Client, durée critique différente |
| **1084 / 5886** Dépouillage | 1 normal / critique | Normal : actions 179 et 154, **aucun 950/76** ; critique : uid **137566**, action 950/76, durée **2**, mask=A | Client, état ajouté sur critique uniquement |
| **5097 / 22981** Anti-soin | 1 normal | uid **126453**, action 950/76, durée **1**, mask=a,A ; critique vide | Client |
| **4736 / 22541** Poison incurable | 1 normal | uid **124553**, action 950/76, durée **1**, mask=A,O ; action 77 distincte | Client |
| **29779 / 79170** Transhumance | 1 normal | uid **385029**, action **951/value 76**, mask=C, durée 0 | Client, retrait candidat |
| **20860 / 54775** État Insoignable | 1 normal | uid **261356** 950/76 order 0 ; uid **261357** 951/76 mask=a,A,E76 order 1 ; uid 393030 action 120 ; aucune conclusion de séquence serveur | Client, masque à interpréter |
| **14587 / 44378** Cicatrisation | 1 normal / critique | uid **226187** 1109/6 et uid **226190** 1109/8, mask=a | Client |
| **14584 / 44369** Affliction | 1 normal / critique | uid **410153** 2998/I, uid **410155** 2998/TB ; critique **410156/410158** | Client, déclenchement inconnu |
| **12911 / 40910** Arnaque | 1 normal / critique | uid **208179** action 93 dés 16–18 ; uid **208180** action 93 dés 19–21 | Client |
| **13431 / 41883** Roublardise | 1 normal | uid **259838**, action 1020, duration 1, mask=a,P,F3112,F3113,F3114,F5161,F5163,F5162 | Client, masque multiple |
| **14825 / 44911** Esbroufe | 1 normal | **statesCriterion=HS!76** ; effets 752, 128, 1033, aucun état 76 posé dans cette fiche | Client ; sémantique du critère Unknown |
| **30461 / 80622** PerSpellKillCounter | 1 normal | uid **394977** 950/**5920** ; uid **394980** 951/**5920** ; icône identique au 76 mais incurable=0 | Contrôle négatif client |
| **3545 / 20720** Cohobation | 1 normal | uid **428026** 950/76, duration 2, **delay 8**, order 117 | Client ; activation serveur différée inconnue |

## Scénarios candidats et oracles

Les **douze entrées** sont aussi structurées dans [fixtures_candidates.json](fixtures_candidates.json). Les nombres de PV, positions et présences d'état des cas sont **synthétiques, non exécutés** ; les IDs, grades, branches et effets sont **extraits**. Aucune ligne de cette table ne constitue un test réussi.

| Cas | Contraste d'entrée | Oracle réellement justifié | Branche restant Unknown |
| --- | --- | --- | --- |
| P01-01 | Deux états identiques sauf **id 76** contre **5920** | **Client** : incurable=1 contre 0 ; même icône. Ne pas bloquer sur le second par homonymie d'icône. | Confirmation serveur des états actifs |
| P01-02 | Reconstitution sur allié 30/100 sans 76, puis même soin sur allié portant déjà 76 | **Client** : ordre 1109 puis 950/76 ; **documentaire** : ne pas promettre le second gain de PV. | Le premier soin appliqué, l'ordre serveur, le comportement si 76 préexiste |
| P01-03 | Cicatrisation 14587 sur 30/100, cible 76 absente/présente | **Client** : 1109 en N et C ; **externe** : soins bloqués sous Insoignable ; valeur 6/8 % sérialisée. | PV exacts, validation serveur, résistances/bonus aux soins |
| P01-04 | Arnaque 12911 touche adversaire, lanceur 30/100 avec/sans 76 | **Code + externe** : le gain du vol de vie vise le lanceur ; sous 76, ne pas certifier de guérison. | Dégâts effectifs, absorption bouclier, gain serveur exact |
| P01-05 | Affliction 14584 avec ligne I puis ligne TB, 76 présent lors du déclenchement différé ou retiré avant | **Client** : action 2998 dans deux lignes à triggers distincts ; **code** : TB classé différé. | Frontière temporelle TB, bénéficiaire final, état au déclenchement |
| P01-06 | Roublardise 13431 sur cible avec/sans 76 | **Client + code** : 1020 fait partie des boucliers, distinct d'une ligne Heal. | Bouclier réellement accordé/refusé sous 76 ; masques complexes |
| P01-07 | Même cible 30/100, 76 actif, puis PV max réduits par source externe *supposée* | **Client** : l'état 76 ne porte aucune valeur de réduction des PV max ; comparer Life, MaxLife et Shield séparément. | Existence/source de la réduction, règle d'érosion serveur, plafond de soin |
| P01-08 | Blopagation 1173 grade1 normal contre critique sur même cible | **Client** : 950/76 durée 1 contre durée 2. | Moment exact d'expiration de l'état et résultat du jet critique serveur |
| P01-09 | Dépouillage 1084 grade1 normal contre critique | **Client** : seul le critique possède 950/76 (uid 137566). | Réussite du critique, application au destinataire réel, réduction/dissipation |
| P01-10 | Transhumance 29779, 951/76, puis tentative de soin | **Client + code** : 951 retire un état 76 dans le résolveur ; le soin ne peut être crédité qu'après retrait confirmé. | Applicabilité du sort en combat, événement autoritaire de retrait |
| P01-11 | Esbroufe 14825 avec état 76 présent/absent sur lanceur | **Client** : statesCriterion littéral **HS!76** ; oracle de lancement **non établi** sans décodeur du critère. | Grammaire du critère et refus/acceptation serveur |
| P01-12 | Cohobation 3545 à l'instant T puis T+8 | **Client** : ligne 950/76 delay=8 ; **code** : la ligne est Deferred, pas exécutée par le résolveur immédiat. | Unité du délai, activation et expiration réelles |

## Conséquences pour le développeur

- Porter l'état par son **ID 76 reçu et authentifié** et distinguer explicitement trois valeurs : présence, absence certaine, **Unknown**. Le faux ami 5920 impose un contrôle négatif.
- Au calcul de valeur d'un soin, d'une régénération ou du vol de vie, distinguer **récupération de Life** et **dégâts de l'action** ; ne créditer aucun gain de PV impossible ou non confirmé. Pour le vol de vie, tester l'état du *bénéficiaire* du gain de vie.
- Ne pas déduire de incurable=1 une interdiction des boucliers ni une réduction des PV max. Maintenir Life, MaxLife, Shield et provenance de leur modification comme champs séparés.
- Conserver les branches normal/critique et les gardes statesCriterion, targetMask, triggers, delay, duration. Une règle « tous les coups posent Insoignable » est réfutée par Dépouillage ; une règle « durée unique » l'est par Blopagation.
- Réserver l'oracle serveur à des événements corrélés : spellId+grade+branch, actorId/targetId, état 76 avant/après, Life/MaxLife/Shield avant/après, dommage reçu, ordre exact des effets, timestamps de trigger et expiration. Sans eux, le résultat opérationnel est **Unknown**, même si la donnée client est claire.

**Statut : preuves nouvelles documentaires obtenues, 12 scénarios candidats justifiés, 0 validation serveur.** Voir [PREUVES.md](PREUVES.md) pour les empreintes, la méthode et les échecs/limites.
