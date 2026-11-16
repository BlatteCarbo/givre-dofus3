# M14 — Iop : fenêtre de burst, Colère chargée et placement

**Statut :** spécification documentaire datée **04/11/2026**. Elle traite précisément la **Colère lancée au tour 1 puis réutilisée à partir du tour 4**, et la différence entre « contact atteignable sans coût PA » et « contact nécessitant Bond ». Elle ne démontre pas une rotation complète universelle : le combat peut interrompre la charge et les versions varient. Aucune action de jeu, test, code, configuration ou opération Git.

## Corpus 2025–2026 et vérification réelle

| Ref | Vidéo authentifiée | Collecte exacte | Conclusions de vidéo limitées |
| --- | --- | --- | --- |
| **V14-1** | Tenmalexis, [*La MEILLEURE TEAM de 4 sur DOFUS en 2025 ?!*](https://www.youtube.com/watch?v=YFlyjdSo4Ek), 02/05/2025, **16 min**, mise à jour **3.1** mentionnée (description mentionne également « 2.70 ») | Métadonnées/langues et **225 fragments de transcript français**, `next_cursor=null`, acquis via MCP ; focus [04:48–05:35](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=288s), [06:19–07:25](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=379s). Sous-titres FR et FR auto listés ; `lang=fr` obtenu. | [05:13–05:26](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=313s) : auteur prépare la Colère du Iop ; [06:19–06:28](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=379s) : « tour 4 » et préparation du Panda ; [07:00–07:18](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=420s) : bonus des alliés puis Colère et Épée du Destin annoncées. **Paroles `declare-video` ; pas de cellule ni budget observés**, montants de dommages non réutilisables. |
| **V14-2** | XAWPIYA, [*DOFUS : Les MEILLEURS DUOS avec le Iop en 2026 ! (Guide Complet)*](https://www.youtube.com/watch?v=d89eRr0ciHQ), 15/07/2026, **8 min** | `get_video_info` et `get_available_languages` réussis ; FR automatique proposé ; **transcript indisponible**, message `Could not retrieve a transcript ... YouTube is blocking requests from your IP`. Aucun intervalle de paroles lu ni image inspectée. | Description : partenaires **Eniripsa** (soin), **Zobal** (boucliers), **Éliotrope** (portails). **Contexte de duo uniquement** ; pas de preuve d'un sort précis ni d'une exécution. |
| **V14-3** | XAWPIYA, [*DOFUS : Les MEILLEURES TEAMS de 4 en 2026 ! (Guide Complet)*](https://www.youtube.com/watch?v=VCVjlE8pVcI), 08/06/2026, **9 min** | Transcription FR automatique **294 fragments intégralement récupérés**, `next_cursor=null`; focus [00:50–02:05](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=50s), [02:25–03:05](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=145s). | [02:37–02:55](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=157s) recommande un Crâ, **Iop ou Sram** comme rôle dégâts selon composition ; [01:05–01:45](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=65s) insiste sur placement/tank Pandawa. Pas de Combo Colère démontré. |

Les **guides sont authentiques** (titre, auteur, date, durée remontés par le MCP), mais l'analyse des **paroles horodatées** ne porte que sur V14-1/V14-3 ; V14-2 est `metadata/description-only`. Aucun statut `observe-video` : accès aux captures navigateur CoS non disponible (réponse `BROWSER_NOT_DISPATCHED` lors de l'essai), donc aucun plateau, PA affiché, dégâts ni ordre serveur ne peuvent être affirmés d'après la vidéo.

## Recoupement de règles versionnées

- **S14-1** : [Dofus Grimoire, fiche Iop — bannière **MAJ 3.7.12**](https://dofus-grimoire.fr/classes/iop/), lecture le 04/11/2026. Au niveau 200, **Colère de Iop** (variante opposée à **Fureur**) coûte **7 PA**, portée **1 PO** (mêlée), relance **3 tours**. Les dégâts de base indiqués sont 81–100 Terre au premier lancer et 191–210 après retour chargé. Le bonus de +110 de base **dépend du premier lancer réellement effectué** ; ne pas simuler « Colère chargée » sur simple intention d'action.
- **S14-2** : même fiche, **Puissance** rang 3 coûte **3 PA**, portée **0–6 PO**, relance **4 tours**, booste la cible ; **Massacre 2 PA** applique un effet de dommages subis augmentés sur l'ennemi, retour **4 tours** (voir combo Terre). **Pression 3 PA** et **Concentration 2 PA** sont présentées dans la rotation au contact T1.
- **S14-3** : même fiche, **Bond 4 PA** à **1–5 PO** au rang haut, téléporte sur **case libre**, effet de vulnérabilité des ennemis en zone ; **sans ligne de vue** selon fiche, n'utilise pas de PM ; demande néanmoins la destination libre et la condition de sort.
- **S14-4** : [Back from AFK, Iop, évolutions 3.6 → 3.7](https://backfromafk.com/classes/iop/depuis-3-6/), données affichées **3.7, octobre 2026** : seules **Précipitation** et **Zénith** changent dans ce périmètre. Précipitation applique maintenant **état Précipité** (arme bloquée sur la cible), avec retrait de PA au tour suivant. Cet effet n'est **pas** inclus dans la rotation Colère étudiée.
- **Écart de version :** la vidéo de référence est 3.1 (2025), l'autre guide 2026 est non transcrit ; page guide 3.7.12, client local cible annoncé **3.7.13.17**. Les coûts/valeurs ne deviennent pas `valide-live` tant qu'ils ne sont pas extraits/recoupés dans le client cible.

## Règles atomiques actionnables

| ID | Règle précise | Statut | Source / limites |
| --- | --- | --- | --- |
| **M14-R01** | Colère **7 PA / 1 PO / 3 tours de relance** : premier lancer validé T1 puis retour possible T4 avec bonus, si le même combat et la rotation réelle le permettent. | `corrobore-source` pour fiche 3.7.12, `hypothese` pour disponibilité 3.7.13.17 | S14-1 ; V14-1 [05:13](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=313s), [07:00](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=420s) `declare-video`. |
| **M14-R02** | Tour de frappe T4 **Puissance 3 → Massacre 2 → Colère 7 = 12 PA** si cible/états légaux. Puissance bénéficie au Iop s'il se cible ; Massacre bénéficie à **tous frappeurs** sur ennemi marqué, tant que l'effet subsiste. | `corrobore-source` pour coûts/effets tiers ; ordre tactique `hypothese` | S14-2 ; montant final inconnu : caractéristiques, résistances, critique, états et cumul. |
| **M14-R03** | Le placement et la distance déterminent si on peut payer le boost : **marcher 5 PM** vers le corps à corps conserve 12 PA, tandis que **Bond 4 PA + Colère 7 PA = 11 PA** et ne laisse pas de place pour Puissance/Massacre ensemble. | Coût Bond/Colère `corrobore-source` ; comparaison `hypothese` de fixture | S14-1/S14-3 ; tacle, obstacles, portails, cases libres peuvent changer la décision. |
| **M14-R04** | Un burst dépend de la **vraie initiative** : le placement du Pandawa/portail Éliotrope effectué **après** le Iop ne peut pas rendre une Colère antérieure légale. | `hypothese` d'ordonnancement, `declare-video` pour rôles conseillés | V14-1/V14-2 ; reprendre M08 pour un cas à 4, sans inventer une exécution vidéo exacte. |
| **M14-R05** | Un ACK de cast ou un échec/timeout `Unknown` ne doit pas armer un cooldown/bonus de charge sans résultat serveur permettant de confirmer l'état. | `hypothese` de contrat sûr pour moteur | Validations offline/live à construire. |

## Séquence de référence T1 et T4 (fixture, pas vidéo)

Iop **I=(0,0)**, adversaire mobile **E=(1,0)**, ligne de vue et mêlée légales, aucun obstacle, pas de challenge restrictif, 12 PA/6 PM **au début de chacun des tours du Iop** (hypothèse de build, non donnée de vidéo), niveau 200 avec **Colère**, Pression, Concentration, Puissance, Massacre disponibles et variantes compatibles, cible vivante, effets/cooldowns vides. **T2–T3 non spécifiés** : sans actions et états réels il est interdit de promettre la position E à T4.

| Fenêtre | Acteur | Action | Précondition | PA / PM propres après | État attendu |
| --- | --- | --- | --- | --- | --- |
| **T1.1** | I | Colère sur E à 1 PO | Colère disponible, cible mobile vivante et au contact | **5 PA / 6 PM** | premier lancer résolu, relance/charge suivie si certifiée |
| **T1.2** | I | Pression sur E | E toujours éligible | **2 PA / 6 PM** | dégâts/érosion éventuels après effet |
| **T1.3** | I | Concentration sur E | E toujours à PO 1 et vivant | **0 PA / 6 PM** | dégâts après effet |
| **T2–T3** | I / ennemis / alliés | Maintien de la survie et positionnement | Aucun enchaînement imposé | **Unknown tant qu'actions absentes** | suivre relance + position E + PV |
| **T4.1** | I | Puissance sur I | PA de départ 12, sort hors relance, pas de malus de PA | **9 PA / 6 PM** | boost **sur I** |
| **T4.2** | I | Massacre sur E | cible à portée, sort prêt, effet admis | **7 PA / 6 PM** | cible marquée ; dégâts ultérieurs augmentés selon état |
| **T4.3** | I | Colère sur E | E toujours à **1 PO**, vraie charge T1 confirmée, cooldown résolu | **0 PA / 6 PM** | frappe chargée sous boosts si confirmée ; dégâts réels inconnus |

**Risque** : si E n'a plus 1 PO T4, I peut avoir besoin de PM ou Bond. L'absence de PM suffisants ne permet pas d'inventer le déplacement ; Bond **coûte 4 PA**, ce qui interdit **Puissance 3 + Massacre 2 + Bond 4 + Colère 7 = 16 PA** sur 12 PA. Une option dégradée si une destination de Bond est accessible et légale : **Bond 4 → Colère 7 = 11 PA**, **1 PA restant**, protection/boosts perdus ; n'attribuer au Bond aucun coefficient de dégâts sans état effectivement appliqué. L'option plus prudente est reporter la frappe si la cible ou le Iop risquent de mourir et replanifier.

## Plans solo, duo, trio, quatuor et davantage

| Taille | Ordre et bénéficiaire hypothétiques | Décision/risque |
| --- | --- | --- |
| **Solo Iop** | T1 Colère 7+Pression 3+Concentration 2 ; T4 Puissance 3+Massacre 2+Colère 7 | La fenêtre marche si E reste accessible ; privilégier survie si le contact va tuer I. |
| **Duo Iop + Élio** | **Élio → Iop**, positions/portails certifiés avant que I commence ; Élio peut créer accès, I frappe. Inversion **Iop → Élio** : ne pas créditer les portails ultérieurs. | Les PA/PM de Élio sont **unknown** ; routage exact et multiplicateur non établis dans V14-2. |
| **Duo Iop + Zobal ou Eniripsa** | **Zobal/Eni → Iop**, bouclier/soin appliqué avant l'exposition si légal ; I conserve ses **propres 12 PA**. | Valeur en survie, pas garantie de dommage ; vidéo V14-2 **description seulement**. |
| **Trio Panda → Élio → Iop** | Panda rapproche/fixe, Élio arrange accès, I attaque **après** états certifiés ; Fallback si position change : marcher, Bond+Colère, ou reporter. | Aucun coût des alliés inventé ; danger de cible fuyante et portail ouvert à l'ennemi. |
| **Quatuor Panda → Élio → Féca → Iop**, ou Panda → Iop → Élio → Féca | Avant I : placement/protection peuvent aider ; après I : tout arrive **trop tard pour la Colère de ce tour**. | Différence d'initiative discriminante, réutilise M08 ; pas de cumul de PA fictif. |
| **5+** | Ordre strict selon initiative réelle, plusieurs tanks et placeurs indépendants. | Rechercher conflits de placement, challenges et invocations contrôlées ; ne pas généraliser une rotation 4. |

Chaque allié possède **son** budget PA/PM et son état ; seuls les budgets du Iop dans cette fixture sont chiffrés.

## Refus, Unknown et exclusions

- **T1 Colère refusée** : ne pas enregistrer une charge, ni promettre « +110 » au T4, ni déclencher cooldown de 3 tours sur intention.
- **T4 cible hors contact** : refaire calcul PO/tacle/PM/chemin ; si Bond nécessaire, réserver 4 PA et abandonner le boost complet à 12 PA.
- **T4 Massacre refusé** : ne pas appliquer son multiplicateur à Colère ; garder plan possible **Puissance 3 + Colère 7 = 10 PA** si la cible au contact.
- **T4 statut charge/cooldown inconnu** : ne pas choisir Colère comme action certaine ; poursuivre reconnaissance d'état avant cast.
- **Fureur sélectionnée** : Colère n'est pas équipée dans ce slot de variante (S14-1) ; ne pas afficher un burst impossible.
- **Précipitation** : sort d'équipe versionné [état Précipité 3.7](https://backfromafk.com/classes/iop/depuis-3-6/) ; non utilisé ici, et ses PA donnés à un **allié** ne doivent pas se confondre avec les PA du Iop.

**Tests positifs/négatifs et reprise** dans les deux documents suivants. Aucun `valide-test`, `valide-live` ou observation d'image n'est attribué.
