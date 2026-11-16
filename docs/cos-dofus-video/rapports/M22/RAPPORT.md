# M22 — Ouginak : Proie, Rage et contrôle du corps-à-corps

**04/11/2026.** Étude documentaire complémentaire à la fiche [atlas](../classes/ouginak.md). La source détaillée est [Dofus Grimoire, classe Ouginak](https://dofus-grimoire.fr/classes/ouginak/) version affichée **3.7.12** et [Back From AFK, évolution Ouginak](https://backfromafk.com/classes/ouginak/depuis-3-0/) (comparaisons jusqu'à 3.7). Ces sources sont communautaires et l'état exact du client 3.7.13.17 reste à recouper.

## Sujet et résultat

Le guide communautaire 3.7.12 expose une relation Proie/Rage, des états de transformation et le rôle des alliés au corps-à-corps. Le guide HUZ fin 2025 contient un passage réellement transcrit à partir de 12:46 sur Osamodas, Féca, invocations autour de la cible et risques de mêlée ; ce commentaire d'auteur ne prouve pas la version 3.7 exacte.

## Vidéos / sources réellement disponibles

[HUZ, chapitre Ouginak 12:46](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=766s) : transcription française automatique consultée jusqu'à 14:35, évoque Osamodas et Féca, opinion d'auteur `declare-video`. Aucun tour de jeu Ouginak reconstruit visuellement.

### Registre des règles atomiques

| ID | Énoncé mécanique | Statut | Limite |
| --- | --- | --- | --- |
| M22-R01 | La cible Proie est une identité/état spécifique qui peut influencer les dégâts et les soins perçus par les alliés. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Un changement de Proie doit invalider les calculs établis sur l'ancienne cible. |
| M22-R02 | Rage et les états dérivés se modifient selon des événements de coups décrits par la source 3.7.12. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Le compteur ne doit pas croître sur une attaque refusée ou une cible hors portée. |
| M22-R03 | La transformation change les options de déplacement/portée et le profil de risque. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Une routine de corps-à-corps ne doit pas se déclencher sur une cible qui s'est téléportée. |
| M22-R04 | Un nombre d'entités adjacentes peut modifier certains sorts, selon la configuration exacte. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Décrire le comptage (allié/invocation/ennemi) et l'instant d'évaluation avant de coder. |

**Aucun `observe-video`, `valide-test` ou `valide-live` revendiqué ici.** Une description de vidéo ou une transcription thématique n'est pas une preuve des cellules et de l'ordre des trames. Sources exactes : [fiche 3.7.12](https://dofus-grimoire.fr/classes/ouginak/), [diff 3.7](https://backfromafk.com/classes/ouginak/depuis-3-0/) et, lorsqu'il existe, l'extrait d'auteur référencé ci-dessus.

## Plan pédagogique en plusieurs tours

T1 : désigner une Proie et vérifier la cible ; T1/T2 : approcher légalement et observer le compteur ; T2 : utiliser une action au contact ou à courte portée seulement si la proie est encore accessible ; évaluer la survie du corps-à-corps au tour ennemi.

## Contextes solo, duo, trio, quatre et plus

- **Solo** : mettre en concurrence le tour préparatoire et une action immédiate dont l'utilité est connue.
- **Duo** : Ouginak + Osamodas : idées de placement des créatures autour de la proie discutées dans VID-DUO19-01. Décrire l'initiative inversée, l'allié réellement bénéficiaire et la perte d'une étape.
- **Trio** : Ouginak + Osamodas + Féca : mêlée et couverture. Expliquer la contribution marginale du troisième personnage et la responsabilité de chaque coût PA/PM.
- **Quatuor** : Ouginak + Pandawa + Eniripsa + Féca : hypothèse de mêlée sécurisée. Réserver une action à la sécurité du groupe, comparer au simple ajout de dégâts.
- **5–8** : même règle avec plusieurs acteurs mais conflits de cases et d'effets ; suivre séparément invocations, compagnons, buffs et challenges.

## Tests discriminants

**P01 positif proposé :** Proie E reste identifiée et à portée ; le bonus conditionnel éventuel est enregistré uniquement si l'état d'attaque et de Proie est confirmé.

**N01 négatif principal proposé :** Proie E se déplace hors de portée avant le tour Ouginak ; le bot abandonne son plan de mêlée au lieu de lancer à travers la carte.

**N02 timing et contre-scénario :** Un monstre à longue portée fuit ; la création d'invocations autour de la cible gêne l'allié ou augmente les dégâts de zone ennemis.

**N03 initiative :** un soutien/contrôle annoncé joue après l'attaquant ; le tour de l'attaquant ne doit pas anticiper son effet. **N04 refus serveur :** toute action non confirmée laisse les effets en attente et impose une relecture de l'état.

## Ce qui manque pour terminer la preuve

Récupérer un tutoriel avec audio/transcription horodatée propre et captures lisibles montrant exactement les cellules et états ; vérifier les coûts et capacités sur le client 3.7.13.17 ; obtenir des événements de combat expurgés et tests reproductibles. Les cas ci-dessus sont un **protocole d'étude**, non un replay réellement exécuté.

