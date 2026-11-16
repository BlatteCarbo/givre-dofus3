# M18 — Osamodas : invocations indépendantes, boost Piqûre Motivante et capacité

**04/11/2026.** Étude documentaire complémentaire à la fiche [atlas](../classes/osamodas.md). La source détaillée est [Dofus Grimoire, classe Osamodas](https://dofus-grimoire.fr/classes/osamodas/) version affichée **3.7.12** et [Back From AFK, évolution Osamodas](https://backfromafk.com/classes/osamodas/depuis-3-2/) (comparaisons jusqu'à 3.7). Ces sources sont communautaires et l'état exact du client 3.7.13.17 reste à recouper.

## Sujet et résultat

Le guide communautaire 3.7.12 attribue aux créatures leur tour et leurs caractéristiques propres, et décrit Piqûre Motivante sur invocation ou sur allié. La comparaison 3.2→3.7 recense des dizaines de sorts modifiés, dont Tofu, Bouftou, Crapipou, Dragonnet et Piqûre Motivante (selon les sections). Ne jamais transposer les coûts d'une vidéo de 2025 directement à 3.7.

## Vidéos / sources réellement disponibles

[Métadonnées d'un gameplay Osamodas Eau](https://www.youtube.com/watch?v=v8KbySowKsM) identifiées (chapitres 00:00, 06:32, 10:48, 15:40), mais sans transcription MCP ni captures du plateau pour cette mission. Éviter d'en déduire des règles de classe.

### Registre des règles atomiques

| ID | Énoncé mécanique | Statut | Limite |
| --- | --- | --- | --- |
| M18-R01 | Une invocation possède son identifiant, ses PV, sa case, son propriétaire et son ordre de tour propres. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Un acteur invoqué ne doit pas être confondu avec son invocateur pour l'identité d'une action. |
| M18-R02 | Piqûre Motivante a des effets différents selon cible invocation/allié, décrits en 3.7.12. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Les gains AP/MP doivent être attribués au bénéficiaire observé et à la bonne durée. |
| M18-R03 | Les familles de créatures et leurs variantes ont des caractéristiques/actions distinctes. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Une invocation générique sans template/rang ne suffit pas pour simuler ses actions. |
| M18-R04 | La saturation d'une carte par invocations peut empêcher les déplacements et lignes de vue alliés. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | La présence de plus d'entités n'est pas automatiquement un gain tactique. |

**Aucun `observe-video`, `valide-test` ou `valide-live` revendiqué ici.** Une description de vidéo ou une transcription thématique n'est pas une preuve des cellules et de l'ordre des trames. Sources exactes : [fiche 3.7.12](https://dofus-grimoire.fr/classes/osamodas/), [diff 3.7](https://backfromafk.com/classes/osamodas/depuis-3-2/) et, lorsqu'il existe, l'extrait d'auteur référencé ci-dessus.

## Plan pédagogique en plusieurs tours

T1 : choisir une case d'invocation où la créature pourra protéger ou agir, vérifier la capacité ; T2 : appliquer un bonus au bon bénéficiaire s'il vivra assez longtemps pour le rentabiliser ; T3 : calculer son action dans son propre tour à partir d'un état serveur actualisé.

## Contextes solo, duo, trio, quatre et plus

- **Solo** : mettre en concurrence le tour préparatoire et une action immédiate dont l'utilité est connue.
- **Duo** : Osamodas + Ouginak : les entités peuvent influencer l'entourage de la cible, piste HUZ à vérifier. Décrire l'initiative inversée, l'allié réellement bénéficiaire et la perte d'une étape.
- **Trio** : Osamodas + Pandawa + Iop : placement puis invocations/bonus utiles au finisseur. Expliquer la contribution marginale du troisième personnage et la responsabilité de chaque coût PA/PM.
- **Quatuor** : Osamodas + Féca + Enutrof + Crâ : hypothèse de contrôle avec espace à gérer. Réserver une action à la sécurité du groupe, comparer au simple ajout de dégâts.
- **5–8** : même règle avec plusieurs acteurs mais conflits de cases et d'effets ; suivre séparément invocations, compagnons, buffs et challenges.

## Tests discriminants

**P01 positif proposé :** Case libre et limite non atteinte ; l'invocation confirmée dispose de son propre ID et son attaque au tour suivant est attribuée à la créature.

**N01 négatif principal proposé :** Même carte, mais plafond atteint ou case occupée ; aucune créature fantôme ne peut agir.

**N02 timing et contre-scénario :** Un monstre de zone détruit les deux invocations avant le prochain tour ; le plan doit recalculer les coûts de remplacement.

**N03 initiative :** un soutien/contrôle annoncé joue après l'attaquant ; le tour de l'attaquant ne doit pas anticiper son effet. **N04 refus serveur :** toute action non confirmée laisse les effets en attente et impose une relecture de l'état.

## Ce qui manque pour terminer la preuve

Récupérer un tutoriel avec audio/transcription horodatée propre et captures lisibles montrant exactement les cellules et états ; vérifier les coûts et capacités sur le client 3.7.13.17 ; obtenir des événements de combat expurgés et tests reproductibles. Les cas ci-dessus sont un **protocole d'étude**, non un replay réellement exécuté.

