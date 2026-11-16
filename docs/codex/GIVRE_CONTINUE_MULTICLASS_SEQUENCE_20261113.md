# GivreClone — séquences tactiques vérifiables multiclasse

Vague hors jeu du 13 novembre 2026, client catalogue 3.7.13.17.
Les plans décrits ici sont des propositions déterministes **sans
émission réseau** et non des combats live démontrés.

## Nouveau planificateur conservateur

`GivreClone.App/Services/Combat/VerifiedTacticalSequence.cs` expose
`Prepare` puis `Next` pour des sous-séquences d'au plus deux actions.
Le plan est lié à `ComboFrame`, `ComboEvidence`, classe/grade appris
et version de catalogue. Avant chaque proposition il vérifie l'état
de combat, génération, tour, carte, acteur, PA/PM observés,
absence de commande pendante, position et visibilité confirmées,
portée, alignements, cases, occupation et LoS.

Pour une action requérant un déplacement, `Next` propose
uniquement le mouvement d'abord ; le lancer doit attendre la
confirmation serveur de nouvelle position puis une nouvelle
évaluation des contraintes. Pour un deuxième sort,
un simple `ActionResult.Sent` ou un état local n'est pas
une confirmation : un cast serveur exact, corrélé à l'acteur,
au grade, à la cellule et à l'effet attendu doit précéder
l'étape suivante.

La classification des effets possède des gardes spécifiques :

- **Roublard :** pose vérifiée d'Explobombe seulement, aucune
  détonation automatique attribuée au sort d'explosion du monstre.
  Une combinaison mur/boost/poussée sera autorisée uniquement
  après preuve serveur de mur actif, propriété et dégâts.
- **Éliotrope :** création et activation de marques de portail,
  réseau de portails du bon propriétaire et projection d'impact
  vérifiés avant un tir redirigé.
- **Xélor :** échange des deux positions et état Téléfrag confirmé
  après cast et avant enchaînement.
- **Féca :** glyphe posé/activé et encore présent avant une
  deuxième action dépendante.
- **Écaflip :** état de carte/passif issu d'un effet de sort
  vérifié, jamais choisi par proximité.
- **Sacrieur :** état de Souffrance et déplacement de cible
  confirmés avant l'action qui en dépend.

L'algorithme refuse toute provenance, classe, état, sortie de
portail, ordre d'événements, cible, coût ou capacité inconnus.
`CombatTurnPlanner` et l'injection sort/PM autoritaires ne sont
pas contournés.

## Ingress diagnostic, frontière de preuve

`LaunchService.Combat.ComboIngress.cs` et
`LaunchService.Packets.cs` conservent un journal borné
de marques décodées, rattaché au PID, combat, carte et tour
diagnostiques. Les snapshots sont détachés et les marques
inactives n'autorisent aucune stratégie.

**Limite essentielle :** `DecodedPacket` porte un PID, un sens,
le code et le payload, mais pas une génération de
connexion authentifiée au moment de la trame. Même une trame
S→C de la bonne famille peut être un callback retardé. Les
observations de cet ingress sont donc systématiquement
`ServerConfirmed=false` et ne peuvent alimenter une
exécution `VerifyObservedCombo` positive. Il faudra
propager une provenance transport vérifiée jusqu'au décodeur,
sans inventer d'ACK, avant de rendre ce planificateur
autonome dans `LaunchService.Combat.Turn`.

## Validation et risques

Les nouvelles suites `VerifiedTacticalSequenceTests` et
`CombatComboIngressAuthorityTests` (si l'ingress est figé)
doivent être compilées et lancées avec les régressions de
combat existantes. Aucun test live, statistique de dégâts,
ou victoire n'est attesté par les fixtures.

Cette architecture est un socle extensible, pas encore
une IA multi-tours complète : espace de recherche borné
à deux actions et aucune hypothèse de coefficients
ou règles d'états non prouvées.
