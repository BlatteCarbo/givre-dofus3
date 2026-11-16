# M26 — Temple de Koutoulou : Folie, dégâts alliés et échanges de position (DOFUS 3.7)

**4 novembre 2026, étude documentaire**. Le guide détaillé [Temple de Koutoulou](https://www.dofuspourlesnoobs.com/temple-de-koutoulou.html) comprend une section explicitement actualisée « Depuis la 3.7 ». La [présentation de la MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), datée du 4 octobre 2026, confirme l'ajout d'un moyen de réduire Folie en infligeant des dégâts à un allié. **Aucune vidéo horodatée spécifique et aucun plateau 3.7 n'ont été vérifiés** pour ce cas, aucun test ou replay exécuté.

## Problème tactique principal

La mécanique Folie relie des événements qui, dans un combat ordinaire, paraissent indépendants : **finir son tour en ligne de vue du boss**, **attaquer directement un ennemi**, **permuter plusieurs fois**, **subir le tour d'un double invoqué**, et depuis la 3.7 **frapper volontairement un allié pour réduire ses états de Folie**. Une simple préférence « maximiser dégâts ennemis » peut conduire à une mort ou déplacer toute l'équipe dans une zone dangereuse.

Le guide décrit une échelle de **1 à 10 Folie** avec seuils défavorables à 2/4/6 et mort à 10 ; le détail d'un effet (zone, coût/arrondis) doit être confronté à l'état de combat du sous-patch local. Un état de Folie est perdu au **début du tour de son possesseur** selon ce guide. Il faut mesurer le gain net d'une attaque alliée : retirer de la Folie, mais infliger des dégâts à la personne que l'on veut sauver, et éventuellement déclencher un challenge.

## Sources et limites

| Réf. | Source réellement consultée | Qualité |
| --- | --- | --- |
| KOU-S1 | [Dofus pour les Noobs — MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), publié le 04/10/2026, paragraphe Koutoulou | **corrobore-source 3.7** : réduction Folie par dommages sur un allié ; pas de log serveur ni formule |
| KOU-S2 | [Dofus pour les Noobs — Temple de Koutoulou](https://www.dofuspourlesnoobs.com/temple-de-koutoulou.html), sections II/V/VI | Guide spécifique avec seuils, conditions de Folie, échanges, portée et mécanique ajoutée en 3.7 ; certains chiffres peuvent être historiques sans marque de sous-patch |
| KOU-S3 | [Grimoire — catalogue boss](https://dofus-grimoire.fr/boss/) | Répertoire de découverte, pas une preuve du déroulement du tour de Koutoulou |
| KOU-V | Aucune vidéo 2025–2026 dont la transcription et les images ont été inspectées dans cette mission | Aucun statut `declare-video`/ `observe-video` attribuable ; pas d'horodatage inventé |

## Règles atomiques à retenir provisoirement

| ID | Règle candidat versionnée | Statut | Conséquence pour le simulateur |
| --- | --- | --- | --- |
| M26-R01 | Le boss n'a pas un verrou d'invulnérabilité permanent ; frapper directement un ennemi peut forcer un échange de place avec l'entité admissible la plus proche | `corrobore-source` KOU-S2, ordre serveur exact `hypothese` | Prévoir positions **avant/après chaque ligne directe**, pas seulement après le sort entier |
| M26-R02 | Échanger deux fois avec la même entité pendant le tour peut ajouter Folie ; si c'est un ennemi, l'invulnérabilité temporaire peut en résulter | `corrobore-source` KOU-S2 | Compteur `exchangeCount[actor,target,turn]`, phase de réinitialisation et état temporaire par cible |
| M26-R03 | Folie augmente notamment en terminant son tour en ligne de vue du boss ; au début de son propre tour, le personnage en perd un niveau | `corrobore-source` KOU-S2 | Vérification fin de tour, puis décroissance à l'horloge **de l'acteur**, pas de la ronde globale |
| M26-R04 | Depuis 3.7, infliger des dommages à un **allié** peut réduire sa Folie d'un niveau | `corrobore-source` KOU-S1/KOU-S2 | Enregistrer `beneficiary=ally`, perte de PV/bouclier et baisse de Folie séparément ; hypothèse d'une baisse par ligne de dégâts à vérifier |
| M26-R05 | À partir du tour 2, le boss peut téléporter les alliés en ligne de vue à son contact au début de son tour, sauf certains états empêchant cette téléportation | `corrobore-source` KOU-S2 | Ne pas anticiper un alignement sans connaître la ligne de vue et l'état de chaque personnage |
| M26-R06 | Le boss dispose également d'échanges avec des monstres alliés sous conditions, et la Pesanteur peut empêcher cette **autre** compétence | `corrobore-source` KOU-S2 | Distinguer **effet de swap déclenché par dégâts** et **sort propre du boss**, dont les immunités diffèrent |
| M26-R07 | Les effets de zone et de contre-attaque sont particulièrement dangereux en Folie élevée, y compris pour des alliés proches | `corrobore-source` KOU-S2, chiffres à revérifier | Scorer dommages collatéraux et distance des alliés, pas seulement dégâts ennemis |
| M26-R08 | Des poisons peuvent produire des dégâts sans provoquer les mêmes permutations que les lignes de dégâts directs | `corrobore-source` KOU-S2 | Séparer famille de dommage et déclencheur ; ne jamais généraliser à tous les effets différés sans trace |

## Quatre plans de composition, chacun conditionnel

**Solo** : personnages à faible portée peuvent réduire la Folie en évitant les fins de tour en ligne de vue. Le choix entre une attaque directe et un poison reste conditionné à l'élément/effet versionné et à la position possible après échange.

**Duo Féca + Crâ** : le Féca peut sécuriser une fin de tour ou protéger l'allié qui devra frapper pour réduire Folie ; le Crâ peut éviter le contact. Mais une protection n'annule pas nécessairement une mort à Folie 10, et les deux ne doivent pas supposer leurs cases après des permutations.

**Trio Pandawa + Eniripsa + Iop** : Pandawa oriente les positions, Eniripsa gère les pertes de PV, Iop doit vérifier que l'approche et l'attaque ne provoquent pas plusieurs swaps et Folie. L'action de soin peut devenir inutile si la Folie reste proche du maximum.

**Quatuor Pandawa + Enutrof + Crâ + Féca** : l'entrave des monstres sert à limiter le risque de contact, tandis que placement et protection offrent des cellules hors LdV. Le modèle doit suivre la Folie et le tour de *chaque* acteur ; retirer des PM au boss ne supprime pas un swap déclenché par un dégât direct.

Ces plans sont `hypothese`, proposés pour expliquer les interactions de classe. Aucun n'est présenté comme un combat observé ou gagnant.

## Tests de régression à construire

- **P01 Folie réduite** : cible alliée A avec Folie observée et PV positifs ; une ligne de dommage admissible confirmée depuis B ; observer PV/bouclier et Folie séparément. Le texte 3.7 prévoit une diminution, mais le tick exact doit être confirmé.
- **N01 refus** : B tente un sort sur A mais le serveur le refuse ; A conserve la Folie précédente et ne subit pas de dommages supposés.
- **N02 mauvais bénéficiaire** : la ligne frappe le boss plutôt que l'allié ; aucun crédit de Folie réduite sur A ; vérifier les échanges induits.
- **N03 état déjà faible** : A à Folie inconnue ou nulle ; ne pas scorer un soin de Folie sans preuve de niveau positif.
- **N04 timing** : B attaque A **après** le tour de A ; la diminution ne peut pas corriger rétroactivement l'exposition de A plus tôt ce tour.
- **N05 challenge** : Duel, Intouchable, Sans-cœur ou autre challenge pouvant interdire/perdre des points si les dégâts de B sur A sont comptabilisés ; vérifier le texte in-game du challenge.
- **N06 échange** : deux lignes directes dans un seul sort, ordre et entité la plus proche potentiellement changés après la première ligne ; le simulateur ne conserve pas la même cible du swap par défaut.
- **N07 état boss** : même position mais l'allié possède Pesanteur ; le sort propre du boss peut échouer alors que le swap par effet de dommage a une règle différente. Séparer les mécanismes.

## Blocages

Aucune trace des événements Folie 3.7.13.17, de l'identité exacte des lignes de dégâts admissibles, du plafond de déclenchements ou de l'ordre des swaps ; vidéo non étudiée. Ces tests sont **proposés et non exécutés**. Le guide historique contient des chiffres de dégâts non transférables au build du personnage ou à l'équipement actuel.

