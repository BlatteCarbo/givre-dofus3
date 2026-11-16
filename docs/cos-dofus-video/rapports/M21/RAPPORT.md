# M21 — Sadida : Arbres, Poupées, Infecté et propagation

**04/11/2026.** Étude documentaire complémentaire à la fiche [atlas](../classes/sadida.md). La source détaillée est [Dofus Grimoire, classe Sadida](https://dofus-grimoire.fr/classes/sadida/) version affichée **3.7.12** et [Back From AFK, évolution Sadida](https://backfromafk.com/classes/sadida/depuis-3-0/) (comparaisons jusqu'à 3.7). Ces sources sont communautaires et l'état exact du client 3.7.13.17 reste à recouper.

## Sujet et résultat

Le guide communautaire 3.7.12 décrit arbres à placer et faire évoluer, poupées associées, état Infecté, et effets touchant les autres cibles infectées. Un guide 2026 indépendant traite le leveling PvM 1–100 et la propagation par Ronce ; l'effet doit être lié à la bonne version, aux cibles qui conservent réellement le statut et aux obstacles.

## Vidéos / sources réellement disponibles

[Xawpiya Huppermage+Sadida 2026](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=195s), sous-titres 03:15–05:15 consultés (conseil de synergie seulement). [Guide Sadida PvM octobre 2026](https://krozone.fr/blog/sadida-dofus-2026-guide-pvm-solo-1-a-100-sans-lenteur/) consulté, source écrite et non transcription.

### Registre des règles atomiques

| ID | Énoncé mécanique | Statut | Limite |
| --- | --- | --- | --- |
| M21-R01 | Les Arbres et Poupées sont des entités/cases du terrain, avec propriétaire, capacité et état vivant. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Si l'arbre meurt entre pose et déclenchement, sa zone ne doit plus être attendue. |
| M21-R02 | Infecté est un état par cible avec source et expiration ; certains sorts peuvent en appliquer/propager les effets. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Ne pas considérer tous les monstres d'un groupe comme infectés par simple proximité. |
| M21-R03 | L'effet indirect sur plusieurs ennemis dépend des règles propres du sort et de l'état de chacun. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | L'infection n'autorise pas tout sort de dégâts à toucher automatiquement toutes les cibles. |
| M21-R04 | Les invocations occupent des cases, peuvent intercepter des attaques et gêner les alliés. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Le score tactique doit intégrer également leur exposition et leurs coûts. |

**Aucun `observe-video`, `valide-test` ou `valide-live` revendiqué ici.** Une description de vidéo ou une transcription thématique n'est pas une preuve des cellules et de l'ordre des trames. Sources exactes : [fiche 3.7.12](https://dofus-grimoire.fr/classes/sadida/), [diff 3.7](https://backfromafk.com/classes/sadida/depuis-3-0/) et, lorsqu'il existe, l'extrait d'auteur référencé ci-dessus.

## Plan pédagogique en plusieurs tours

T1 : poser Arbre sur case contrôlée ou infecter une cible ; T2 : propager l'état si les cibles éligibles et positions sont maintenues ; T3 : utiliser un sort dont l'effet sur Infecté est attesté, puis mettre à jour les cibles touchées individuellement.

## Contextes solo, duo, trio, quatre et plus

- **Solo** : mettre en concurrence le tour préparatoire et une action immédiate dont l'utilité est connue.
- **Duo** : Sadida + Huppermage : contrôle et soutien durable (VID-HUPPER-DUO-01). Décrire l'initiative inversée, l'allié réellement bénéficiaire et la perte d'une étape.
- **Trio** : Sadida + Enutrof + Pandawa : groupe contrôlé et propagation. Expliquer la contribution marginale du troisième personnage et la responsabilité de chaque coût PA/PM.
- **Quatuor** : Pandawa + Sadida + Enutrof + Féca : composition évoquée VID-TEAM4-01. Réserver une action à la sécurité du groupe, comparer au simple ajout de dégâts.
- **5–8** : même règle avec plusieurs acteurs mais conflits de cases et d'effets ; suivre séparément invocations, compagnons, buffs et challenges.

## Tests discriminants

**P01 positif proposé :** Deux cibles E1/E2 portent Infecté confirmé ; le sort à propagation prévu dans la version 3.7 est légal, la résolution sur chacune est consignée séparément.

**N01 négatif principal proposé :** E2 n'est plus Infecté ou l'Arbre propagateur est mort ; E2 ne reçoit pas de dégâts fantômes.

**N02 timing et contre-scénario :** L'ennemi élimine les poupées/arbres en zone, ou le combat se termine avant la mise en place.

**N03 initiative :** un soutien/contrôle annoncé joue après l'attaquant ; le tour de l'attaquant ne doit pas anticiper son effet. **N04 refus serveur :** toute action non confirmée laisse les effets en attente et impose une relecture de l'état.

## Ce qui manque pour terminer la preuve

Récupérer un tutoriel avec audio/transcription horodatée propre et captures lisibles montrant exactement les cellules et états ; vérifier les coûts et capacités sur le client 3.7.13.17 ; obtenir des événements de combat expurgés et tests reproductibles. Les cas ci-dessus sont un **protocole d'étude**, non un replay réellement exécuté.

