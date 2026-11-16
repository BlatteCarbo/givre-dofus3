# GivreClone — réception MZP1 ordonnée, 13 novembre 2026

## Périmètre de la continuation

- Dépôt de travail : `GivreWork/fix-f01-f16-final-20261024`, base observée `b4d28a8`.
- Modifiés exclusivement pour cette correction : `GivreClone.Engine/Recovery/MZP1GuardedEngineWiring.cs` ; nouveau `GivreClone.Tests/MZP1OrderedPublicationTests.cs`.
- Tous les changements locaux précédents, y compris les fichiers non suivis et la source permanente `GivreProxy`, sont conservés. Aucun reset, nettoyage, commit, push, lancement de jeu ou appel à un socket réel.

## Problème confirmé

`TryDispatchCaptured` sérialisait `GameState.ApplyGuardedInbound` sous `_gate` mais relâchait ce verrou avant `Bus.Publish`. Deux captures acceptées simultanément pouvaient donc replier l'état dans l'ordre A puis B, tout en publiant la notification B avant A. Cela rendait la lecture d'événements incohérente avec l'état que les comportements voyaient et permettait une publication imbriquée lorsqu'un abonné rappelait l'ingress.

## Correctif

- Enqueue une projection **sans payload ni RawFrame** exactement dans la section critique qui a replié l'état et confirmé l'admission.
- Active un **seul drainer FIFO** par `MZP1GuardedEngineWiring`. Un callback réentrant ne lance pas une deuxième diffusion ; sa capture est placée derrière le message courant.
- Exécute chaque `Bus.Publish` **hors du verrou d'admission**. Le drainer revérifie `_guard.IsAuthenticated` à chaque message.
- Lors d'un `Revoke`, purge les messages en attente et refuse les nouveaux. Un événement ayant **déjà franchi la porte de publication** avant une révocation concurrente peut encore entrer dans son callback ou le terminer ; cette exécution ne prétend pas l'interrompre rétroactivement.
- Fixe un plafond local à **1 024 messages en attente**. Le suivant révoque la session plutôt que perdre silencieusement une observation de serveur ou accumuler indéfiniment.
- Une exception inattendue de la mécanique de publication révoque la session et libère la file. Les exceptions isolées de chaque abonné sont déjà gérées dans `PacketBus`.

La garantie est l'ordre **d'admission/traitement dans ce moteur** ; elle ne certifie pas un ordre total amont si le producteur réseau appelle les callbacks dans le désordre. `CapturedMessage.Seq` provient d'un compteur potentiellement partagé entre connexions/directions ; l'utiliser pour attendre des valeurs contiguës ou reconstruire un ACK serait incorrect sans contrat d'ordonnancement du producteur.

## Régressions ciblées

`MZP1OrderedPublicationTests.cs` utilise uniquement une autorité et un journal de test synthétiques, avec un `IFrameSink` qui refuse tout envoi :

1. Deux captures concurrentes : l'abonné au premier paquet est volontairement bloqué ; le second ne le dépasse pas.
2. Callback réentrant : une nouvelle capture arrive dans la file sans `Publish` récursif.
3. Révocation pendant le premier callback : file purgée, nouveaux événements refusés, aucune attente du callback pour révoquer.
4. Saturation : les 1 024 événements supplémentaires sont bornés et le 1 025e provoque une révocation, sans diffusion ultérieure.
5. Provenance : les mauvais PID/connexion et PID nul sont refusés ; une notification acceptée ne contient plus d'octets ou champs privés.

**Résultat initial ciblé : 5/5 PASS**, `docs/codex/validation-20261113-mzp1-ordered-initial.log`, exit 0.

Première régression générale : **5 814 PASS / 2 ignorés / 0 échec**, total 5 816, `docs/codex/validation-20261113-mzp1-ordered-full-nonlive.log`, exit 0. Elle utilise le filtre historique `FullyQualifiedName!~Live`. Ce filtre éliminait aussi par sous-chaîne un nouveau test nommé `Accepted_delivery_...` (le texte `delivery` contient `live`), bien qu'il ait réussi dans les 5 tests ciblés. Le nom a donc été changé en `Accepted_notifications_...` ; la validation finale recouvre l'ensemble des nouveaux tests.

**Validation finale déjà effectuée avant cette reprise** (après renommage et compilation) :

- Régression ciblée MZP1 / sessions / PacketBus : **32/32 PASS**, exit 0, `docs/codex/validation-20261113-mzp1-ordered-targeted-final.log`.
- `GivreClone.Tests`, filtre `FullyQualifiedName!~Live` : **5 815 PASS / 2 ignorés / 0 échec**, total 5 817, exit 0, `docs/codex/validation-20261113-mzp1-ordered-full-final.log`.
- `GivreClone.PureTests` : **362/362 PASS**, exit 0, `docs/codex/validation-20261113-mzp1-ordered-puretests.log`.
- `CombatShadowComparison.Harness` : **65 assertions exécutables PASS**, hors jeu, exit 0, `docs/codex/validation-20261113-mzp1-ordered-shadow.log`.

Ces preuves précèdent le chantier **distinct** de prévisualisation HDV : ne pas les présenter comme une validation de ses nouveaux changements. Aucun test live effectué.

## Limites et prochaines priorités

1. Le flux alternatif historique `BotSession.Dispatch` non gardé conserve une publication directe ; il ne doit pas devenir un substitut à l'autorité MZP1 pour les connexions réelles.
2. Une capture déjà dans `Bus.Publish` au moment exact de la révocation peut achever ses callbacks ; aucun envoi de jeu n'est certifié ni autorisé par cette seule notification.
3. Un producteur de paquets doit prouver l'identité du socket, le sens, l'ordre global entre les deux pompes et l'époque de connexion pour garantir la causalité des événements ; aucun artifice basé sur timestamps ou UID d'un événement générique.
4. Les signatures Roublard/Éliotrope/Xélor/Féca/Écaflip/Sacrieur restent des critères historiques de preuve. Aucun `DetonationConfirmed`, Téléfrag, portail ou état n'est inféré d'une simple prédiction du `SpellResolver`.
5. Les prochaines optimisations de navigation, recettes HDV ou progression doivent conserver leurs propres témoins serveur et leur couverture de tests négatifs.
6. Audit additionnel hors combat : `HdvSellRow.EstValue` (`HdvViewModel.Bulk.cs`) multiplie `AvgUnit * Qty` sans contrôle de dépassement ; `RefreshSellRows` additionne les quantités de piles dans des `int`. Ces valeurs alimentent les affichages et tris, non le garde de taxes corrigé précédemment. Les rendre saturants et les tester est une tâche séparée ; aucun correctif de ce domaine n'est revendiqué dans cette vague MZP1.
