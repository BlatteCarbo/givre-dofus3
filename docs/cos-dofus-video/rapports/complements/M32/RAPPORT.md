# M32 — Comparaison SOLO / DUO / TRIO / QUATUOR, compositions et initiative

Recherche du **4 novembre 2026**. Objet : comparer, pour un même problème tactique de PvM, la chaîne Pandawa–Enutrof–Crâ avec un quatrième **Féca ou Eniripsa**, puis examiner les alternatives **Huppermage, Steamer, Sadida, Zobal**. Un guide de composition n'atteste pas la faisabilité d'un sort au tour N : les variantes, PA, positions, cibles et messages de combat doivent rester explicites. Les « meilleures équipes » sont des **avis de créateurs situés dans leur contexte**, jamais des règles du serveur.

## 1. Vidéos authentifiées, accès réellement vérifié

| Réf. | Auteur, titre, date, durée (MCP) | Méthode et intervalle vérifié | Limite de version |
| --- | --- | --- | --- |
| V32-H | **HUZ REPLAY**, [Les MEILLEURS DUOS de CLASSES sur DOFUS (fin 2025)](https://www.youtube.com/watch?v=X9gMQA5rhVw), **05/12/2025**, **37 min** | `get_video_info`, `get_available_languages` FR automatique, **`get_timed_transcript(lang=fr)` réussi** : 1 203 segments, `next_cursor=null`. Segments effectivement lus : 04:45–06:15 Crâ, 08:25–10:50 Eni, 18:10–20:28 Enu, 21:10–23:15 Steamer, 23:15–25:00 Féca, 25:00–26:40 Hupper, 26:40–28:20 Zobal, 28:20–30:20 Panda, 31:25–33:35 Sadida. Liens horodatés dans les règles. | **Avant 3.7**, avis de duo à recouper ; sous-titres automatiques déforment noms de sorts, éléments, pronoms et chiffres. Aucune case de plateau inspectée. |
| V32-T26 | **Tenmalexis**, [Les MEILLEURS TEAMS de 4 sur DOFUS en 2026 ?](https://www.youtube.com/watch?v=U-L4-3toUGE), **30/08/2026**, **12 min** | Métadonnées `get_video_info` et FR humaine + FR automatique détectées ; **`get_timed_transcript` échoue (« YouTube is blocking requests from your IP »)**. Description et chapitres écrits par l'auteur vérifiés : 00:00 cadrage, **01:10** progression, **06:42** donjons, **08:15** farm, **10:17** Songes ; **ces repères sont des chapitres, pas des passages oraux lus**. | Avant 3.7 ; seuls l'auteur, ses propositions écrites et leur classement par objectif sont attestés. |
| V32-T25 | **Tenmalexis**, [La MEILLEURE TEAM de 4 sur DOFUS en 2025 ?!](https://www.youtube.com/watch?v=YFlyjdSo4Ek), **02/05/2025**, **16 min** | `get_video_info`, langues FR détectées ; description explicite « Pandawa/Enutrof/Crâ + Féca, Zobal, Huppermage, Eniripsa ou Crâ » ; chapitres **01:00** Panda-Iop-Elio, **08:09** Panda-Crâ-Enu. Transcription non revendiquée comme lue. | **3.1** selon auteur : ancienne méta, utile comme historique de familles de rôles et non comme preuve d'équilibrage 3.7. |

Le service MCP de transcription fonctionne **sur V32-H**, mais pas sur V32-T26 au moment de l'essai. On ne généralise pas l'échec IP à toutes les vidéos. Les conclusions marquées `declare-video` proviennent exclusivement des paroles HUZ effectivement horodatées ; les avis des deux Tenmalexis sont `avis-description-video`.

## 2. Recoupement 3.7, sources documentaires

- [Dofus Grimoire 3.7.12 — Pandawa](https://dofus-grimoire.fr/classes/pandawa/) : Karcham porte/jette une cible adjacente pour **1 PA par usage** et les coordonnées changent. La fiche propose porter 1 PA + déplacement 3 PM + jeter 1 PA. Précondition **Sobre**, variante Karcham/Chamrak, ciblage et case libre à confirmer.
- [Dofus Grimoire 3.7.12 — Enutrof](https://dofus-grimoire.fr/classes/enutrof/) : Maladresse **1 PA, tentative jusqu'à -2 PM**, Obsolescence **3 PA, -20 Esquive PM** au rang documenté, Pelle Aurifère **4 PA, tentative jusqu'à -3 PM**. Résultat effectif esquivable et potentiellement partiel ; M16 documente les autres inconnues.
- [Dofus Grimoire 3.7.12 — Féca](https://dofus-grimoire.fr/classes/feca/) : Barricade **2 PA**, protection Invulnérable en Mêlée sur une cible selon état/rang ; glyphes à **début de tour** et auras distincts, important pour l'ordre d'initiative. Ne pas transformer Barricade en invulnérabilité universelle ou permanente.
- [Dofus Grimoire 3.7.12 — Eniripsa](https://dofus-grimoire.fr/classes/eniripsa/) : Mot Stimulant **2 PA, +2 PA à une cible alliée**, et soins/soutien à cibles identifiées ; remplacement possible d'Encouragements sur une même cible ; M15 documente les phases inconnues.
- [Dofus Grimoire 3.7.12 — Huppermage](https://dofus-grimoire.fr/classes/huppermage/) : les **combinaisons** dépendent des états élémentaires sur cible et ne sont pas des dégâts libres sans préparation.
- [Dofus Grimoire 3.7.12 — Steamer](https://dofus-grimoire.fr/classes/steamer/) : tourelles/effets de déplacement conditionnels, case d'occupation et orientation ; ne pas supposer une tourelle déjà posée.
- [Dofus Grimoire 3.7.12 — Sadida](https://dofus-grimoire.fr/classes/sadida/) : arbres, poupées contrôlables, état Infection et cases d'invocations ; la version 3.7 doit prévaloir sur la vidéo 2025.
- [Dofus Grimoire 3.7.12 — Zobal](https://dofus-grimoire.fr/classes/zobal/) : **masque actif** préalable pour Plastron et autres effets ; ciblage du bouclier conditionné par la zone réelle.
- [Dofus pour les Noobs — MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), publiée 04/10/2026 : refonte de visibilité des glyphes/portails et révisions du **Crâ**, ainsi que limitation de l'amplification via portails pour l'Infection du Sadida. **La vidéo d'août ne couvre pas ces ajustements.**
- [Huzounet — Les Teams de 8](https://huzounet.fr/guides/les-teams-de-8), actualisé **30/09/2026** mais contenant la mention « exemples en 2.73 » : avis de rôles/éléments utile pour équipes larges, **pas** validation d'un client 3.7.

## 3. Comparaison par effectif : pourquoi le plan change

| Taille | Composition testable | Atouts documentés / conseil | Coût et limite pratique |
| --- | --- | --- | --- |
| **Solo** | Crâ seul ; contre-témoin Eniripsa seul | HUZ V32-H [04:51–05:20](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=291s) privilégie Crâ pour kite ; [08:41–09:40](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=521s) décrit Eni comme profil de soin/flexibilité. | Aucune action de placeur ou buff d'allié disponible. Tester fuite/LoS/retraits propres et durée de combat, pas extrapoler un tour à quatre. |
| **Duo** | **Crâ + Enutrof** vs **Crâ + Eniripsa** | HUZ [05:14–06:15](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=314s), [18:15–20:08](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1095s) recommande surtout Crâ/Enu pour entrave + distance. Eni privilégie PV/bonus ciblé si dégâts reçus inévitables. | Si le monstre esquive -PM, Crâ doit pouvoir repousser ou se protéger ; si Crâ joue avant Eni, le buff ne peut pas lui permettre une attaque **sur son tour déjà achevé**. |
| **Trio** | **Pandawa + Enutrof + Crâ** | Pandawa propose placement, Enutrof tente ralentissement, Crâ exploite une ligne de tir ; la formule de base de Tenmalexis T25/T26 est explicitement récurrente dans leurs **descriptions**. | L'ordre **Panda→Enu→Crâ** doit être dans la timeline ; après placement il faut recalculer LoS et portée du Crâ. Le Panda peut finir exposé sans protecteur. |
| **Quatuor A** | **Panda + Enu + Crâ + Féca** | Féca offre une mitigation préventive (Barricade/glyphes), plus adaptée si un tour ennemi dangereux suit avant la prochaine chance de soigner. | Éventuel gaspillage si personne ne subit de dégâts ; Barricade protège **un type d'attaque et une cible**, non tout le groupe. |
| **Quatuor B** | **Panda + Enu + Crâ + Eni** | Eni offre soins et bonus de PA ciblé pour l'allié encore à venir, intéressant si l'adversaire passe des dégâts prévisibles. | Si soin nécessaire **avant** prochain tour Eni ou si cible devient Insoignable/morte, le soin peut arriver trop tard. Le Crâ doit agir après son buff si l'on veut effet immédiat. |
| **Quatuor C** | **Panda + Enu + Crâ + Hupper/Steamer/Sadida/Zobal** | Hupper : polyvalence/combinaisons ; Steamer : tourelle, pousse/attire ; Sadida : Infection et contrôle ; Zobal : boucliers/masques. Avis HUZ horodatés [21:25–28:20](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1285s) ; T25 liste Hupper/Zobal dans son texte. | Préparation, cibles, cases libres, masques/tourelles/poupées, coûts et temporisations différents. Aucun « substitut de soin universel » ni classement de DPS prouvé. |

**Contrainte de contenu :** ce comparatif est un modèle PvM générique. Songes, boss à états, succès duo, combats de quête et donjons modulaires peuvent avoir des règles différentes. Ne pas prendre une recommandation de quatuor comme preuve que quatre joueurs sont admis dans n'importe quel combat.

## 4. Paroles horodatées, reformulées sans les élever au rang de règle

| ID | Ce que déclare réellement HUZ | Interprétation limitée |
| --- | --- | --- |
| M32-V01 | [05:12–06:14](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=312s) : Crâ se combine avec une classe d'entrave/kite, Enutrof lui semble privilégié ; dans certains donjons Pandawa pourrait avoir un meilleur rapport efficacité. | **Préférence de joueur**, non un taux de réussite. |
| M32-V02 | [08:41–10:49](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=521s) : Eni comme soigneur et personnage flexible ; Sadida/Énu/Crâ/Féca peuvent accompagner un Eni selon distance et style. | Soins et buff utiles seulement si cible, timing et portée permettent le déclenchement. |
| M32-V03 | [18:16–20:10](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1096s) : Enutrof est présenté comme entraveur, un partenaire distant (Crâ) profite de l'entrave. | Retrait **tenté** ≠ PM retirés ; vidéo 2025. |
| M32-V04 | [21:25–23:12](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1285s) : Steamer peut être kiter/repousser ; duo Steamer–Sadida préféré de l'auteur. | Ne démontre ni placement exact de tourelle ni valeur des dégâts. |
| M32-V05 | [23:16–25:00](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1396s) : Féca distance/entrave ou protection selon voie, duo Panda/Féca possible. | Rôle dépend de sa variante et du temps du glyphe. |
| M32-V06 | [25:01–26:39](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1501s) : Hupper/Sadida favori pour combinaison de runes et infection. | Son goût personnel n'est pas une synergie automatique ni un proc « chaque joueur ». |
| M32-V07 | [26:40–28:20](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1600s) : Zobal boucliers/masques et rôle secondaire ; invocations/arbres pourraient bénéficier d'un shield de zone. | **Ne pas** certifier qu'une invocation précise reçoit Plastron sans cellule et filtre de cible 3.7. |
| M32-V08 | [28:20–30:15](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1700s) : Pandawa facilite placement de zones, au prix de préparation ; intérêt avec Roublard. | L'initiative et les cases libres gouvernent le gain réel. |
| M32-V09 | [31:25–33:35](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1885s) : Sadida feu et kiting ; certaines synergies qui infligent des dégâts quand les ennemis **dépensent** des PM perdent de leur intérêt si tout PM est retiré. | Excellent contre-exemple à « maximiser retrait PM toujours », effet spécifique du sort et version actuelle à vérifier. |

## 5. Un cycle concret (cas central M32-CYCLE-01)

**Fixture choisie**, non extraite d'une vidéo : entités P=Pandawa, E=Enutrof, C=Crâ, S=Féca **ou** Eniripsa, ennemis M1 et M2. Ordre d'initiative **P→E→S→C→M1→M2** ; tous vivants, équipements/variantes adéquats, zones libres et sorts portés hors relance. P commence **Sobre**, adjacent à M1 et peut jeter M1 dans une case libre `Z` sous ligne de tir de C ; E a portée sur M1 ; C n'a pas encore joué.

1. **Pandawa** : Karcham porter M1 (1 PA), déplacement facultatif seulement s'il reste une route, Karcham jeter M1 en `Z` (1 PA). Réception serveur vérifie `M1.cell=Z` ; ne pas utiliser `Z` si refus, décès ou interruption.
2. **Enutrof** : Obsolescence M1 (3 PA, malus d'Esquive PM si reçu), puis Maladresse M1 (1 PA, **jusqu'à** -2 PM). Si le serveur confirme 0 PM retiré, M1 reste mobile. Aucun PM de C n'est affecté.
3. **S = Féca** : Barricade (2 PA dans la fiche 3.7.12) sur **P** si la menace annoncée de M1 est **mêlée** et P exposé. Si menace à distance, ce n'est pas une protection adéquate : autre sort légal ou action directe à rechercher. **S = Eni** à la place : Mot Stimulant (2 PA) **sur C**, si C joue ensuite, ou soin sur P selon PV manquants et portée. Le gain de PA de C doit être confirmé à son propre tour.
4. **Crâ** : choisit un sort de frappe **réellement appris, légal** depuis sa cellule, ligne de vue recalculée après la projection. Coût et dégâts **inconnus ici**, puisqu'aucun sort/rang Crâ n'a été isolé en 3.7.12 ; ne pas inventer un « tir 3 PA ». Réutiliser sa propre liste de sorts/version.
5. **Monstres** : M1 agit avec ses PM **restants observés**, et avec ses sorts de téléportation/portée éventuels. Les PM retirés n'interdisent pas automatiquement une attaque. Recalculer après chaque mort/placement.

Le cas dérivé **Panda+Enu+Crâ seulement** saute l'étape S et mesure les dégâts évités versus le coût d'un quatrième acteur. **Avec Eni/Féca**, comparer protection/soin utiles et round entier, sans supposer les PV, résistances ou IA du monstre.

## 6. Deux négatifs discriminants (et repli)

- **N32-INIT — initiative inversée :** `C→M1→P→E→S`. Le Crâ a déjà tiré avant que le Panda place M1 et avant que Eni lui donne +2 PA ou que Féca protège P. Attendu : **aucun effet rétroactif**, plan du cycle courant dégradé ; préparer prochain cycle seulement si M1 survit. Une simulation qui crédite le Crâ des PA de Stimulant ou d'une ligne de tir rétrospective échoue.
- **N32-ESQUIVE-LOS — retrait esquivé et géométrie :** M1 esquive tous les PM, Pandawa le jette dans une cellule `Z` **qui masque la LoS de C** (obstacle entre C et Z). Attendu : C ne tire pas son sort à LoS, M1 peut encore approcher ; plan B = repositionnement/attaque différente/protection. Les deux échecs sont **indépendants** (fixer `Z` et changer esquive, puis fixer esquive et changer `Z` pour isoler la cause).
- **N32-SYNERGIE-SADI — effet dépendant du mouvement :** classe Sadida prépare un effet dépendant de PM dépensés par ennemi (selon avis HUZ) ; Enu retire tous les PM et l'ennemi ne bouge pas. Vérifier déclenchement réel du sort version 3.7, **ne pas lui créditer des dégâts d'activation inexistants**.

## 7. Restitution au développeur
Le moteur doit comparer des **plans légaux pour un même état** et reporter explicitement identité du bénéficiaire, PA de chaque acteur, délais d'initiative, valeur d'entrave réellement obtenue, LoS/cases après chaque effet et survie attendue. Voir [FICHE_COMPOSITIONS.md](FICHE_COMPOSITIONS.md) et [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md). Tous les tests sont **proposés et non exécutés** ; aucun statut `valide-test` ou `valide-live`. Les passages oraux de V32-H ne sont pas des captures de plateau. Pas de code, Git, jeu/live ou SYNTHESE.md.

