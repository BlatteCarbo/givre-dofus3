# M17 — Ecaflip : main de cartes, Pile ou Face et effets conditionnels

**04/11/2026.** Étude documentaire complémentaire à la fiche [atlas](../classes/ecaflip.md). La source détaillée est [Dofus Grimoire, classe Ecaflip](https://dofus-grimoire.fr/classes/ecaflip/) version affichée **3.7.12** et [Back From AFK, évolution Ecaflip](https://backfromafk.com/classes/ecaflip/depuis-3-0/) (comparaisons jusqu'à 3.7). Ces sources sont communautaires et l'état exact du client 3.7.13.17 reste à recouper.

## Sujet et résultat

Le guide communautaire 3.7.12 décrit le Poker d'Ecaflip : le coût en PA et l'élément du sort déterminent la carte piochée, quatre cartes au maximum dans la Main. Pile ou Face permet de rendre l'issue de certains effets moins aléatoire. Château de Cartes a changé en 3.7 : coût porté à 3 PA et texte de bouclier modifié.

## Vidéos / sources réellement disponibles

Aucun tutoriel récent à transcription horodatée exploitable n'a été collecté spécifiquement pour M17. Le chapitre Ecaflip du [guide HUZ fin 2025](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1213s) est repéré par le sommaire ; passage non relu dans cette mission.

### Registre des règles atomiques

| ID | Énoncé mécanique | Statut | Limite |
| --- | --- | --- | --- |
| M17-R01 | Chaque sort élémentaire concerné peut alimenter une Main avec une carte d'élément et de rang associés au coût ; limite annoncée de quatre cartes. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Ne jamais générer une carte sur une action refusée ou sur un sort hors règle du passif. |
| M17-R02 | Pile ou Face modifie les conditions des sorts Lapement, Mésaventure, Destin d'Ecaflip et Esprit Félin selon le guide 3.7.12. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Ce statut ne prouve pas le tirage exact, le détail des états ni le changement d'un nouveau patch. |
| M17-R03 | Rekop dépend de la combinaison actuelle de la Main et la défausse, selon la base communautaire 3.7.12. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | La valeur d'une main ne peut pas être ramenée à son nombre de cartes. |
| M17-R04 | Château de Cartes a été modifié entre 3.0 et 3.7, notamment une augmentation de son coût PA. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Les vidéos pré-modification ne sont pas des preuves de la nouvelle valeur. |

**Aucun `observe-video`, `valide-test` ou `valide-live` revendiqué ici.** Une description de vidéo ou une transcription thématique n'est pas une preuve des cellules et de l'ordre des trames. Sources exactes : [fiche 3.7.12](https://dofus-grimoire.fr/classes/ecaflip/), [diff 3.7](https://backfromafk.com/classes/ecaflip/depuis-3-0/) et, lorsqu'il existe, l'extrait d'auteur référencé ci-dessus.

## Plan pédagogique en plusieurs tours

T1 : tenter un sort élémentaire valide, enregistrer la carte seulement après le résultat confirmé ; T2 : fixer l'état Pile/Face avant un effet conditionnel lorsque cela apporte une valeur réelle ; T3 : comparer la Main actuelle à l'action qui la consomme, y compris le tour de défausse.

## Contextes solo, duo, trio, quatre et plus

- **Solo** : mettre en concurrence le tour préparatoire et une action immédiate dont l'utilité est connue.
- **Duo** : Écaflip + Crâ : allié distance profite d'une fenêtre de soutien, à évaluer selon durée des boosts. Décrire l'initiative inversée, l'allié réellement bénéficiaire et la perte d'une étape.
- **Trio** : Écaflip + Féca + Iop : protection / fenêtre offensive / effet de main. Expliquer la contribution marginale du troisième personnage et la responsabilité de chaque coût PA/PM.
- **Quatuor** : Écaflip + Pandawa + Eniripsa + Iop : hypothèse de mêlée et soutien, non filmée. Réserver une action à la sécurité du groupe, comparer au simple ajout de dégâts.
- **5–8** : même règle avec plusieurs acteurs mais conflits de cases et d'effets ; suivre séparément invocations, compagnons, buffs et challenges.

## Tests discriminants

**P01 positif proposé :** Cible éligible ; le sort élémentaire est confirmé puis la Main s'enrichit. L'action de consommation est légale et retire les cartes attendues d'après le serveur.

**N01 négatif principal proposé :** Même plateau, mais la première demande de sort échoue : le moteur n'ajoute pas de carte et ne prévoit pas une Main gagnante fictive.

**N02 timing et contre-scénario :** Un combat court se termine avant d'accumuler une Main utile ; une issue de hasard inconnue fait préférer une décision robuste plutôt qu'un plan unique.

**N03 initiative :** un soutien/contrôle annoncé joue après l'attaquant ; le tour de l'attaquant ne doit pas anticiper son effet. **N04 refus serveur :** toute action non confirmée laisse les effets en attente et impose une relecture de l'état.

## Ce qui manque pour terminer la preuve

Récupérer un tutoriel avec audio/transcription horodatée propre et captures lisibles montrant exactement les cellules et états ; vérifier les coûts et capacités sur le client 3.7.13.17 ; obtenir des événements de combat expurgés et tests reproductibles. Les cas ci-dessus sont un **protocole d'étude**, non un replay réellement exécuté.

