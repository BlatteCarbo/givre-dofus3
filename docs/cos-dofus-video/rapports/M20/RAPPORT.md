# M20 — Steamer : tourelles, états d'Évolution et attaques autonomes

**04/11/2026.** Étude documentaire complémentaire à la fiche [atlas](../classes/steamer.md). La source détaillée est [Dofus Grimoire, classe Steamer](https://dofus-grimoire.fr/classes/steamer/) version affichée **3.7.12** et [Back From AFK, évolution Steamer](https://backfromafk.com/classes/steamer/depuis-3-0/) (comparaisons jusqu'à 3.7). Ces sources sont communautaires et l'état exact du client 3.7.13.17 reste à recouper.

## Sujet et résultat

Le guide communautaire 3.7.12 décrit jusqu'à trois tourelles et les paliers d'Évolution, avec sort spécial aux paliers avancés. Le comparateur 3.0→3.7 signale des modifications d'Évolution, Turbine et Scaphandre : toutes les valeurs historiques de soin, dommages et bouclier doivent être revalidées.

## Vidéos / sources réellement disponibles

[Xawpiya duos Huppermage 2026](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=65s) 01:05–03:15 décrit le Steamer comme allié de contrôle, transcription MCP consultée. Vidéos Capitemflam 2026 sur le Steamer repérées par index secondaire mais URL/frames non établies : aucune preuve mécanique n'en est tirée.

### Registre des règles atomiques

| ID | Énoncé mécanique | Statut | Limite |
| --- | --- | --- | --- |
| M20-R01 | Chaque tourelle est une invocation persistante qui possède identité, position, état d'évolution et actions propres. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | La tourelle ne peut pas agir une fois morte ; sa nouvelle invocation recommence avec un nouvel état. |
| M20-R02 | Évolution change le jeu d'actions ou les bonus de la tourelle selon son palier. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Le palier ne se déduit pas du nombre de tours écoulés si la montée a été refusée. |
| M20-R03 | Des sorts du Steamer peuvent interagir avec la présence/position de tourelles pour générer PA, soin ou contrôle. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Le gain de PA dépend des conditions exactes du sort et doit être confirmé. |
| M20-R04 | Un mur ou une tourelle bloquant la ligne de vue/cheminement d'un allié peut réduire l'utilité de la stratégie. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Comparer bénéfices offensifs et coût d'occupation. |

**Aucun `observe-video`, `valide-test` ou `valide-live` revendiqué ici.** Une description de vidéo ou une transcription thématique n'est pas une preuve des cellules et de l'ordre des trames. Sources exactes : [fiche 3.7.12](https://dofus-grimoire.fr/classes/steamer/), [diff 3.7](https://backfromafk.com/classes/steamer/depuis-3-0/) et, lorsqu'il existe, l'extrait d'auteur référencé ci-dessus.

## Plan pédagogique en plusieurs tours

T1 : poser une tourelle à une case utile ; T2 : la faire évoluer si elle est vivante et si la survie est plausible ; T3 : déclencher/observer son sort propre puis replanifier selon la nouvelle portée et les obstacles.

## Contextes solo, duo, trio, quatre et plus

- **Solo** : mettre en concurrence le tour préparatoire et une action immédiate dont l'utilité est connue.
- **Duo** : Steamer + Huppermage : duo documenté avec contrôle progressif dans VID-HUPPER-DUO-01. Décrire l'initiative inversée, l'allié réellement bénéficiaire et la perte d'une étape.
- **Trio** : Steamer + Féca + Pandawa : protéger les tourelles et ouvrir les angles. Expliquer la contribution marginale du troisième personnage et la responsabilité de chaque coût PA/PM.
- **Quatuor** : Steamer + Huppermage + Féca + Enutrof : hypothèse de jeu prolongé. Réserver une action à la sécurité du groupe, comparer au simple ajout de dégâts.
- **5–8** : même règle avec plusieurs acteurs mais conflits de cases et d'effets ; suivre séparément invocations, compagnons, buffs et challenges.

## Tests discriminants

**P01 positif proposé :** Tourelle confirmée vivante, évolution confirmée, cible dans sa portée légale ; les effets sur cette cible sont attribués à l'acteur/tourelle correspondant.

**N01 négatif principal proposé :** Tourelle détruite entre pose et évolution ; un ancien niveau d'Évolution et les dégâts attendus n'existent plus.

**N02 timing et contre-scénario :** L'adversaire force un déplacement de la tourelle ou se met hors de ligne ; les PA d'investissement ne sont pas récupérés avant la fin du combat.

**N03 initiative :** un soutien/contrôle annoncé joue après l'attaquant ; le tour de l'attaquant ne doit pas anticiper son effet. **N04 refus serveur :** toute action non confirmée laisse les effets en attente et impose une relecture de l'état.

## Ce qui manque pour terminer la preuve

Récupérer un tutoriel avec audio/transcription horodatée propre et captures lisibles montrant exactement les cellules et états ; vérifier les coûts et capacités sur le client 3.7.13.17 ; obtenir des événements de combat expurgés et tests reproductibles. Les cas ci-dessus sont un **protocole d'étude**, non un replay réellement exécuté.

