# M05 — Sacrieur : déplacer une cible avant de frapper

État : recherche documentaire achevée pour **un cas précis**, test non exécuté. Date : 2026-11-04. Auteur : worker-5. Périmètre : Dofus PC Unity, comparaison des plans « s'approcher et frapper » / « attirer et frapper » ; variante de secours Transposition. Il ne s'agit pas d'une validation du moteur ni d'une lecture des images de combat.

## Sources réellement examinées

| Référence | Source / version | Passages réellement consultés | Apport |
| --- | --- | --- | --- |
| V05-A | SAPEUH, *LE SACRIEUR SUR DOFUS UNITY !*, 2024-10-19, ~15 min, vidéo de préversion Unity : https://youtu.be/_8JB1a1H1mU | Transcript FR **automatique** intégral récupéré (457 fragments, sans pagination) ; focus 00:25–01:30, 02:55–03:25, 07:45–08:45, 10:05–10:38 | 00:44–01:05 [l'auteur explique une permutation conditionnelle sur Pénitence](https://youtu.be/_8JB1a1H1mU?t=44) ; 03:14–03:24 [enchaîne oralement plusieurs attaques](https://youtu.be/_8JB1a1H1mU?t=194) ; 10:17–10:35 [signale la pesanteur et choisit Fluctuation pour se rapprocher et attaquer](https://youtu.be/_8JB1a1H1mU?t=617). **Mécaniques de 2024 non transposées automatiquement en 3.7.** |
| V05-B | Capitemflam, *MONTER son SACRIEUR RAPIDEMENT en LEVELING sur DOFUS du LVL 160 à 165*, 2025-02-28, ~1 h, tourné sur 3.0 : https://youtu.be/9OCwxSU83vk | Transcript FR **automatique** complet (1224 fragments, sans pagination) ; focus 43:00–45:55 et 46:20–47:20 | 45:06–45:15 [l'auteur apprécie la capacité de l'épée à attirer, repousser et déplacer](https://youtu.be/9OCwxSU83vk?t=2706) ; 46:48–47:16 [il annonce une transposition pour éloigner son personnage et laisser l'épée en mêlée](https://youtu.be/9OCwxSU83vk?t=2808). La transcription « transport » est ambiguë. |
| S05-1 | OpenDofusDB, [Attirance](https://openddb.dev/spells/12735-attirance), fiche consultée 2026-11-04, jeu PC, version exacte de l'extraction non publiée | Fiche complète d'Attirance | 2 PA, rang 3 à 2–10 PO, **en ligne**, attire de 9 cases hors Souffrance >=6, effet majoré en Souffrance >=6 ; vérification client requise. |
| S05-2 | OpenDofusDB, [Transposition](https://openddb.dev/spells/12736-transposition) | Fiche complète | 3 PA, rang 3 à 1–9 PO, échange les positions, relance 3 tours, l'absence de ligne de vue est recoupée par [Dofus Grimoire 3.7](https://dofus-grimoire.fr/classes/sacrieur/). |
| S05-3 | OpenDofusDB : [Mutilation](https://openddb.dev/spells/12737-mutilation), [Dissolution](https://openddb.dev/spells/12757-dissolution), [Stase](https://openddb.dev/spells/12728-stase) | Fiches complètes | Mutilation 2 PA et sacrifice de PV pour de la puissance ; Dissolution 4 PA avec vol de vie en zone ; Stase 3 PA, 1–5 PO, max. 3 lancers/tour. |
| S05-4 | [Dofuspourlesnoobs, Placement et déplacement](https://www.dofuspourlesnoobs.com/placement-et-deacuteplacement.html) | Article de mécanique, version non précisée | Attraction arrêtée avant obstacle ; les états Enraciné, Indéplaçable, Inébranlable peuvent empêcher l'attirance. Statuts à contrôler dans la cible. |
| S05-5 | [BackfromAFK, Sacrieur changements 3.0 → 3.7](https://backfromafk.com/classes/sacrieur/depuis-3-0/) | Tableau des 13 sorts modifiés, 3.7 (oct. 2026) | Confirme divergences de patch entre vidéos anciennes et contexte actuel ; ne valide pas toutes les règles d'Attirance. |

Le kit mentionne un client de référence **3.7.13.17** ; aucune donnée du client courant n'a été extraite pendant cette mission. Le référentiel 3.7 du Grimoire est plus récent que les vidéos et reste **tiers**. Les sous-titres ont été lus ; **aucune image/plateau vidéo n'a été inspecté** : l'extension navigateur CoS a répondu `BROWSER_NOT_DISPATCHED`. Ni coordonnées, ni PA observés, ni affichage d'états ne peuvent donc être attestés par les vidéos.

## Trois conclusions actionnables

- **M05-R01 — Attirance n'est admissible qu'avec sa géométrie et ses états vérifiés.** La description actuelle de la fiche indique 2 PA, cible en ligne à 2–10 PO au rang 3 et attraction jusqu'à butée. Statut : `corrobore-source` pour les paramètres documentés ; `hypothese` pour leur conformité exacte au client 3.7.13.17. Source : S05-1 et S05-4.
- **M05-R02 — Placement et attaque appartiennent au même budget de PA du Sacrieur.** Avec le scénario à 12 PA / 6 PM hypothétiques : Mutilation (2) → Attirance (2) → Dissolution (4) → Stase (3) = **11 PA** ; il reste 1 PA. Les déplacements consomment les PM de cet acteur et aucun boost allié n'est présumé. Statut : `corrobore-source` (coûts), `hypothese` (géométrie, rendement et plan). Sources S05-1 à S05-3.
- **M05-R03 — La rentabilité du boost est conditionnelle à l'attaque obtenue et aux PV risqués.** Le coût immédiat Mutilation 2 PA, PV sacrifiés, puissance personnelle ne garantit pas que le plan attirance soit meilleur que trois Stase à distance. Le bénéficiaire du bonus est le **Sacrieur**, tandis que l'allié bénéficie éventuellement du placement. Statut : `corrobore-source` (effet Mutilation), `hypothese` (optimisation). Source S05-3 ; exemple de prudence en vidéo V05-A [07:46](https://youtu.be/_8JB1a1H1mU?t=486).

## Plans comparés

Condition d'essai **créée pour le simulateur, non extraite de la vidéo** : Sacrieur niveau suffisant pour les rangs cités, 12 PA / 6 PM, PV > coût Mutilation, Souffrance <6, Attirance/Dissolution/Stase/Mutilation sélectionnés, relances disponibles, S=(0,0), cible E=(8,0), terrain libre horizontal, aucun tacle, aucune immunité de déplacement, cible vivante et pas de challenge restrictif. Les distances sont **relatives**, pas des ID de cellules serveur.

| Axe | Plan A — déplacement personnel puis frappe | Plan B — attraction puis frappe |
| --- | --- | --- |
| Mobilité | S parcourt 3 cases (0,0)→(3,0) : **3 PM** consommés, 3 restants | S reste (0,0) : **0 PM** consommé, 6 restants |
| Sorts ordonnés | Mutilation 2 PA → Stase 3 PA ×3 | Mutilation 2 PA → Attirance 2 PA → Dissolution 4 PA → Stase 3 PA |
| PA | 2+3+3+3=**11 PA**, 1 restant | 2+2+4+3=**11 PA**, 1 restant |
| Effet attendu | Trois attaques directes à 5 PO (si visibilité) ; E reste (8,0) | E (8,0) attiré au plus près de S, attendu (1,0) si trajet libre ; Dissolution et Stase deviennent légales si géométries de zone/visée sont confirmées |
| Valeur/risk | Sacrieur avance et s'expose depuis (3,0), conserve la position de E | E rapproché du groupe, opportunité d'attaque de zone pour le Sacrieur ou un allié après lui ; **danger** de rapprocher le monstre du groupe |
| Choix | Privilégier si l'ennemi est dangereux au corps à corps ou si les trois frappes offrent plus de valeur | Privilégier si la cible doit être fixée, si la zone frappe plusieurs ennemis, ou si un allié profitera de la nouvelle cellule |

Si Mutilation est refusée ou jugée non rentable : recalculer immédiatement les PV/PA ; le plan B sans boost peut être `Attirance 2 + Dissolution 4 + Stase 3 + Stase 3 = 12 PA`, si les deux frappes sont légales et respectent la limite par tour. La valeur numérique de ces deux variantes dépend des résistances, PV, niveau de sort et géométrie de zone — **inconnue**. Pour Transposition de secours, un allié échange sa cellule avec S pour 3 PA uniquement si la cible et les états le permettent : ne pas réutiliser le plan initial sans recalcul de positions.

## Échec et replanification

1. En cas de **refus** d'Attirance (obstacle/alignement/état/relance/portée), conserver E à sa cellule certifiée et les PA réellement confirmés ; rechercher une attaque possible sans attraction, ou un déplacement personnel légal. Ne pas simuler l'attraction fantôme.
2. Si une **confirmation d'action sans état final** arrive, conserver le déplacement comme `Unknown` ; attendre une source d'état fiable avant Dissolution.
3. Si E est déplacé mais survit au corps à corps, calculer exposition de S et des alliés, tacle/retour de dégâts/challenges ; option de repli : distance et défense. Le boost ne justifie pas une mort probable.
4. En cas de Transposition imposant un état interdit, conserver l'impossibilité comme garde spécifique à vérifier sur client actuel ; le commentaire 2024 sur la pesanteur n'en établit pas les règles 3.7.

Tests positifs et négatifs sont détaillés dans la fiche. Tous sont **proposés, non exécutés**.

## Reprise

Transcriptions V05-A et V05-B intégrales collectées (curseur `null`) ; passages ciblés ci-dessus lus. Image non disponible : prochaine preuve utile, capture vidéo de [V05-A à 10:17](https://youtu.be/_8JB1a1H1mU?t=617) ou [V05-B à 46:48](https://youtu.be/9OCwxSU83vk?t=2808), puis extraction de la table effective des sorts du client 3.7.13.17. Ne pas importer le Pénitence 2024 dans l'implémentation courante sur la seule parole de V05-A.
