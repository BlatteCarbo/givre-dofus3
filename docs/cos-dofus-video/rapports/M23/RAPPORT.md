# M23 — Pandawa : portage/jet, états et préparation de zones alliées

**Mission documentaire du 4 novembre 2026 — worker-5.** Périmètre spécifique : une cible ennemie portée/jettée pour rejoindre une zone de frappe du Forgelance ; états Sobre/Saoul, case de réception et ordre d'initiative. **Ni tests, ni images vidéo de plateau, ni jeu exécuté.** Le programme global vise 19 classes, du solo aux équipes 5+, mais ce rapport ne certifie que le sous-cas M23.

## Sources effectivement vérifiées

| Réf. | Titre, auteur, date, durée, version | Méthode, intervalle et statut |
| --- | --- | --- |
| **V23-1** | [*PANDAWA SUR DOFUS 3 !*](https://www.youtube.com/watch?v=I59ZNptd3P8), **SAPEUH**, 18/01/2025, ~15 min, vidéo PvP Pandawa Eau Dofus 3 (version précise non annoncée) | MCP `get_video_info` et `get_available_languages` réussis ; FR auto disponible. `get_timed_transcript(lang="fr")` échoue **`Could not retrieve a transcript ... YouTube is blocking requests from your IP`**. Aucun passage réellement transcrit ni visionné. La description prouve uniquement le sujet, pas les jets. |
| **V23-2** | [*LE MODE PARFAIT POUR PANDAWA SUR DOFUS (Choquant)*](https://www.youtube.com/watch?v=dnD4p3lqwvk), **SAPEUH**, 31/07/2025, ~18 min, PvP Pandawa Feu | Même résultat MCP : métadonnées et langues OK ; transcript FR auto bloqué par IP, pas de paroles horodatées ni lecture d'image. Utilisable comme piste de vérification visuelle ultérieure. |
| **V23-3** | [*DOFUS : Les MEILLEURES TEAMS de 4 en 2026 ! (Guide Complet)*](https://www.youtube.com/watch?v=VCVjlE8pVcI), **XAWPIYA**, 08/06/2026, ~9 min, Dofus Unity, version de patch exacte non dite | MCP `get_video_info`, langues, `get_timed_transcript(fr)` OK : **294 segments FR automatiques, `next_cursor=null`**. Paroles examinées [01:06–01:28](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=66s) : Pandawa comme placeur capable d'isoler/regrouper ; [03:36–03:48](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=216s) : Forgelance cité en soutien de zone ; [04:26–05:04](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=266s) : Pandawa place/tank, Énutrof entrave, Crâ frappe, Féca protège. **`declare-video` seulement**, aucune case de jeu observée. |
| **V23-4** | [*La MEILLEURE TEAM de 4 sur DOFUS en 2025 ?!*](https://www.youtube.com/watch?v=YFlyjdSo4Ek), **Tenmalexis**, 02/05/2025, ~16 min, **MAJ 3.1** déclarée | MCP transcript FR récupéré : **225 fragments, curseur nul**. [04:48–05:10](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=288s) : rôle de tank/placeur du Panda et référence à Brassage ; [08:07–09:10](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=487s) : équipe Panda/Énu/Crâ. Les paroles « souplic/brassage » sont parfois mal transcrites ; ne pas reconstruire sort et cellule. |

### Sources de mécanique et de version

- **S23-1** : [Dofus Grimoire, classe Pandawa](https://dofus-grimoire.fr/classes/pandawa/) ; site affichant **Dofus 3.7.12, correctif du 20/10/2026** via [page principale](https://dofus-grimoire.fr/). Règles documentées : **Karcham, 1 PA** pour porter puis **1 PA** pour jeter, portage nécessairement en état **Sobre** ; rang élevé = jet en ligne jusqu'à **6 PO** (valeur de guide, à recontrôler en client), **case de réception libre**. Sous Saoul, le Pandawa ne peut pas porter ; ce guide indique notamment -1 PM et une réduction de dommages subis tant qu'il est Saoul.
- **S23-2** : [OpenDofusDB, Karcham](https://openddb.dev/spells/12787-karcham) : rang 3 acquis au niveau 144, **1 PA**, cible adjacente à **1 PO**, 4 lancers/tour et une limite de lancer par cible affichées (interprétation du **cycle porter+jeter** à vérifier). [Chamrak](https://openddb.dev/spells/12810-chamrak), **variante exclusive**, porte/jette 1 PA et modifie LdV/portée quand l'entité est portée.
- **S23-3** : [OpenDofusDB, Brassage](https://openddb.dev/spells/12816-brassage), **2 PA / 0–6 PO**, en état **Sobre** augmente les dommages subis de la cible (**×110 %** dans la fiche) ; état **Saoul** entraîne **Pesanteur** et `Brassage` (3 tours), ce qui est un effet radicalement différent. Cette variante remplace **Souillure** ; ne pas supposer les deux disponibles.
- **S23-4** : [BackfromAFK, Pandawa 3.4 → 3.7](https://backfromafk.com/classes/pandawa/depuis-3-4/), données 3.7 (octobre 2026) : `Varappe` passe à +2 lancers Karcham/Chamrak au lieu de +4, `Brassage` **état 2 → 3 tours**, `Distillation` charge réduite. [Depuis 3.0](https://www.backfromafk.com/classes/pandawa/depuis-3-0/) liste des changements Karcham/Chamrak dans les conditions de lancer : la vidéo 2025 ne détermine donc pas la légalité de 2026.
- **S23-5** : [OpenDofusDB, Pandikulation](https://openddb.dev/spells/12793-pandikulation), **2 PA**, jette une entité portée, attire jusqu'à 2 cases et agit en zone ; solution alternative **conditionnelle**, avec zones/cibles à vérifier, pas une action qui peut être improvisée sans portage.

**Cible du projet** : client local **3.7.13.17** selon kit source. **Aucun export de tables de sorts client 3.7.13.17 n'a été lu ici** ; Grimoire 3.7.12 reste une documentation tierce. L'extension navigateur CoS avait renvoyé `BROWSER_NOT_DISPATCHED` lors de l'essai de capture vidéo : aucun statut `observe-video`. Il n'est pas légitime de convertir les tours de guides simulés en combat observé.

## Règles atomiques prioritaires

| ID | Affirmation opérationnelle | Statut | Preuve et limites |
| --- | --- | --- | --- |
| **M23-R01** | Porter+jeter exige un **état Sobre**, une entité adjacente pour le portage, une destination légale et **libre** pour le jet ; ce sont deux actions de **1 PA**. Un sort de jet ne doit pas présumer une entité portée à partir d'un ACK. | `corrobore-source` S23-1/S23-2 ; `hypothese` compatibilité exacte 3.7.13.17 | La règle de « 1 par cible » / compteur porter+jeter et l'application des états Porteur/Porté doivent être confirmées dans traces client. |
| **M23-R02** | Le jet change la cellule de la cible **avant** l'évaluation d'une zone alliée. Brassage Sobre (2 PA) cible le monstre et non le frappeur : l'effet augmente les dommages subis par **ce monstre**, seulement tant que l'effet est actif. | `corrobore-source` S23-3 + raisonnement d'initiative `hypothese` | Vérifier durée, cumul avec les vulnérabilités du Forgelance, ordre des événements de dommages. |
| **M23-R03** | Sous Saoul, le plan Karcham n'est pas admissible directement ; Brassage applique **Pesanteur** au lieu de la vulnérabilité de Sobre. | `corrobore-source` S23-1/S23-3 ; `hypothese` runtime cible | Un retour à Sobre (Bombance/Picole selon variante) coûte des PA et exige sa propre admission, pas un changement gratuit. |
| **M23-R04** | La mise à jour 3.7 a modifié limites Varappe et Brassage ; ne pas reprendre un compteur 2025 ni une rotation pré-3.7. | `corrobore-source` S23-4 ; `obsolete-pour-cible` pour paramètre historique | Source de données effective du bot à vérifier. |

## Tour principal illustratif — hypothèse de test commune à M24

Les coordonnées sont **relatives** (pas les IDs de cellules Dofus). Plateau orthogonal vide hors entités mentionnées ; aucun obstacle, tacle, challenge, invulnérabilité ou effet adverse. P = Pandawa **(0,0)**, E1 = ennemi **(1,0)**, E2 **(5,1)**, E3 **(4,2)**, F = Forgelance allié **(2,3)**. **Case libre R=(5,0)**, **case libre L=(4,1)** (centre de la future Lance) et trajet P : (0,0)→(1,0)→(2,0)→(3,0), tous disponibles après portage. P commence **Sobre**, Karcham rang 3 et Brassage équipés, PA=12, PM=6, cible E1 portable.

| Étape | Acteur | Action/cible | Admission | Budget de P si effet confirmé | Transition attendue |
| --- | --- | --- | --- | --- | --- |
| **P1** | P | Karcham **porter E1** à (1,0) | P Sobre, E1 adjacent, cible portable et vivante | **11 PA, 6 PM** | `carriedBy(E1)=P`, cellule E1 initiale vacante ; état portage à confirmer |
| **P2** | P | Marcher chargé vers (3,0), **3 PM** | trajet libre, capacité de déplacement avec E1 et absence de tacle | **11 PA, 3 PM** | P=(3,0), E1 toujours porté ; vérifier déplacement terminé |
| **P3** | P | Karcham **jeter E1 → R=(5,0)** | R libre, distance 2 en ligne, portée du jet valide | **10 PA, 3 PM** | E1=(5,0), `carriedBy(E1)=none`, seulement après confirmation |
| **P4** | P | Brassage **Sobre sur E1** | cible à 2 PO, sort équipé, état Sobre, aucune immunité | **8 PA, 3 PM** | E1 marqué pour dommages subis si l'effet est confirmé ; autres E non marqués |
| **F1** | F (tour suivant) | **Fer Rouge** sur L=(4,1) puis zones du Forgelance M24 | initiative **P → F**, L libre, F=12 PA/6 PM propres | P inchangé **8 PA/3 PM** | E1, E2 et E3 sont alors dans le carré 3×3 centré sur L ; coûts et positions F consignés en M24 |

**Comparateur** : P ne déplace pas E1, F place sa Lance au même L ; E1 reste (1,0), en dehors du carré L. Avec un jet confirmé à R, **une cible de plus est potentiellement touchée** ; gain de dégâts et effets dépendent du sort lancé et des résistances/zone effective, donc **inconnus**. Le placement coûte à P 2 PA + 3 PM, plus 2 PA de Brassage facultatif : il faut comparer ce coût et l'exposition du Panda à la valeur marginale des attaques de zone. Le résultat de P n'octroie aucun PA au F.

## Contres essentiels et repli

1. **Refus / cellule R occupée** : placer un obstacle ou autre entité à (5,0). P peut éventuellement porter E1, mais le **jet à R** ne doit pas être déclaré réussi. Ne pas téléporter E1 à travers un obstacle ni supposer le drop automatique. Conserver `carried/unknown` selon état serveur, chercher une autre cellule de jet admissible qui garde E1 dans la zone L, ou libérer une case sur un tour ultérieur. Tant que la destination n'est pas certifiée, F ne peut pas compter E1 dans sa zone.
2. **Initiative inverse F → P** : au moment du tour de F, E1 est encore (1,0) ; Fer Rouge au centre L=(4,1) ne touche que le sous-groupe E2/E3. Quand P jette ensuite E1 à R, cela ne crée **aucun dégât rétroactif**. L'agent peut retarder sa zone, avancer une protection ou choisir une attaque différente au lieu de précompter la synergie.
3. **État Saoul initial** : Karcham (portage) illégal tant que non Sobre ; Brassage Saoul est **Pesanteur**, pas ×110 %. Une annulation/refus ne devient pas un changement de posture. Repli sous réserve de variante : payer un changement d'état confirmé puis recalculer 12 PA, ou traiter le tour comme un tour défense/entrave.
4. **Collision de mobilité** : même avec une réception R libre, un état interdit et/ou les cellules d'obstruction peuvent interrompre portage, marche ou recul des entités de zone ; **aucune formule de dégâts de collision** n'est inférée.

## Solo / duo / trio / quatuor / 5+

| Taille / ordre | Bénéficiaire et dépendance | Risque et repli |
| --- | --- | --- |
| **Solo Pandawa** | P regroupe E1 près de E2/E3 ; éventuelle **Flasque Explosive** ou autre zone propre seulement si sort, portée et PA encore disponibles, **coût non alloué** ici. | P devient exposé en (3,0) ; si pas de sort de zone légal, garder PM/PA pour défense au lieu d'inventer un follow-up. |
| **Duo P → F** | P 4PA/3PM dépensés, F **12PA/6PM propres** dans fixture M24 ; le placement augmente le nombre de cibles d'une zone centrée L. | F avant P = absence d'effet de placement au tour de F. |
| **Trio P → Énu → F** | Énu peut tenter retrait PM sur cibles **déjà confirmées**, puis F zone ; son budget **inconnu** et indépendant. | Retrait esquivé = danger futur ; F ne doit pas attribuer à Énu des PM réellement perdus. |
| **Quatuor P → Féca → Énu → F** | P déplace, Féca protège le membre réellement ciblé, Énu contrôle, F exploite L ; buffs et positions s'appliquent **au tour de l'acteur**. | Féca tardif ne protège pas contre dommage antérieur ; détacler E peut le sortir des zones. |
| **5+** | Graph de dépendances `portage→position→zone` avec plusieurs placeurs, frappeurs, invocations/compagnons contrôlés. | Conflits de cellules d'atterrissage, chal, auras, buff d'équipe ; ordonner les confirmations et jamais mutualiser PA/PM. |

**Couverture honnête :** cette déclinaison concerne **une fenêtre de tour**, pas une validation de tous les duos ni toutes les classes. En présence de boss non portables ou d'un challenge interdisant la manipulation, le plan est interdit et un plan dégradé doit être calculé.

## Prochaines preuves et statut

- **`valide-test` / `valide-live` : aucun.** Test positif + deux négatifs et champs à implémenter dans les fichiers associés.
- Capturer le vrai portage/jet + états en client **3.7.13.17**, ou un replay expurgé avec événement de portage et jet ; confirmer compteur de Karcham, portée exacte de la phase jet, interdictions portage/état, cellules libres, ordre visuel/serveur.
- Réessayer les transcriptions V23-1/V23-2 lorsque blocage IP levé, inspecter les images aux passages pertinents **après les avoir localisés** ; aucun instant de vidéo individuelle n'a été examiné, et aucun timestamp y relatif n'est inventé.
