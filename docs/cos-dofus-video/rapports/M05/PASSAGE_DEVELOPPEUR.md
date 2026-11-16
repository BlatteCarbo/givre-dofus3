# Passage développeur — M05 Sacrieur

Statut : **spécification documentaire** ; aucune modification du bot, aucune simulation exécutée, aucune confirmation serveur, aucune image de combat visionnée. Détails et provenance dans [la fiche](FICHE_SACRIEUR_ATTIRANCE_ATTAQUE.md) et [le rapport](RAPPORT.md).

## Trois règles prioritaires

1. **M05-R01 : légalité et résultat Attirance.** [Fiche de sort](https://openddb.dev/spells/12735-attirance) : 2 PA, portée en ligne (rang 3 : 2–10 PO), mouvement avec longueur dépendant de Souffrance. [Guide d'obstacles](https://www.dofuspourlesnoobs.com/placement-et-deacuteplacement.html) : l'attraction s'arrête sur obstruction et des états bloquent le déplacement. Exiger `casterId`, `targetId`, `sourceCell`, `targetCell`, `obstacles`, `states`, `suffering`, `spellRank`, `pa`; transition E(8,0)→E(1,0) **uniquement dans la fixture libre et après preuve de l'effet**. `corrobore-source`, compatibilité exacte cible inconnue.
2. **M05-R02 : comptabilité des ressources et des coûts d'opportunité.** [Mutilation 2 PA](https://openddb.dev/spells/12737-mutilation), [Dissolution 4 PA](https://openddb.dev/spells/12757-dissolution), [Stase 3 PA](https://openddb.dev/spells/12728-stase). Plan A « marche 3 PM + Mutilation + 3 Stase » : 11 PA / 3 PM ; plan B « Mutilation + Attirance + Dissolution + Stase » : 11 PA / 0 PM. Les deux ont un budget initial de **12 PA / 6 PM hypothétique**, aucun chiffre réel de dégâts. Le bénéficiaire de Mutilation est S ; valoriser le PV sacrifié et le risque du placement.
3. **M05-R03 : replanifier sur résultats serveur.** [SAPEUH 10:17–10:35, pré-Unity](https://youtu.be/_8JB1a1H1mU?t=617) évoque « Pesanteur » et choisit une autre mobilité ; [Capitemflam 46:48](https://youtu.be/9OCwxSU83vk?t=2808) annonce un échange pour retirer son personnage. Ce sont des paroles, **aucune preuve visuelle**, et les conditions actuelles sont à vérifier. Transposition documentée [3 PA / échange / relance 3](https://openddb.dev/spells/12736-transposition). Refus, incertitude et apparition de nouvel état invalident le suffixe du plan.

## Contrat simulateur

Entrées : `fightEpoch`, `turnActor`, `actorPA/PM/PV`, `spellIds+rank+variants`, `cooldowns`, `entitiesPositions`, `walkability`, `lineOfSight`, `moveImmunities`, `suffering`, `team+challenge`. Les coordonnées de la fiche sont relatives, **pas des cellIds**.

Transitions : pour chaque action, produire `legal` / `refused` / `unknown` et `costIfConfirmed`. Ne pas créditer le déplacement/soin ou débiter les PA sans la source d'état pertinente ; distinguer un ACK transport du résultat de jeu. En cas de `unknown`, suspendre les attaques dont la portée dépend de la cible supposément déplacée. Réévaluer le coût de rapprochement de l'ennemi (survie alliés, tacle, zone d'ennemi, futur de la cible).

## Tests proposés — non exécutés

- **Positif M05-T+** : S(0,0), E(8,0), ligne libre, 12PA/6PM, rang 3 ; `Mutilation(2) → Attirance(2) → Dissolution(4) → Stase(3)` ; attendre 1 PA / 6 PM, E proche de S **si confirmé**. Contraster avec S avançant 3 PM puis 3 Stase.
- **Négatif M05-T-obstacle** : bloquer le trajet d'E à (3,0), rejouer. La cible **ne doit pas** être déclarée (1,0) ; Dissolution ne doit pas profiter d'une destination fictive. Autres négatifs : E hors ligne, Indéplaçable, rang insuffisant, cible morte entre actions.
- **Inconnues bloquantes** : ID et détails client 3.7.13.17, états exacts interdits sur Transposition/Attirance, cases d'effet et LdV de Dissolution, Résistances/PV/Puissance pour comparer boost vs frappe, ordre serveur des événements. Pas de `valide-test` ou `valide-live`.

Priorité proposée : **élevée pour moteur de placement + validation après effet**, moyenne pour politique de boost ; tous les chiffres de performance sont indéterminés. Prochaine preuve : captures des passages [SAPEUH 10:17](https://youtu.be/_8JB1a1H1mU?t=617) et [Capitemflam 46:48](https://youtu.be/9OCwxSU83vk?t=2808) ou replay expurgé ; image inaccessible via navigateur CoS (`BROWSER_NOT_DISPATCHED`).
