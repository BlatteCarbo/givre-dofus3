# M19 — Huppermage : états élémentaires, combinaisons de deux éléments et runes

**04/11/2026.** Étude documentaire complémentaire à la fiche [atlas](../classes/huppermage.md). La source détaillée est [Dofus Grimoire, classe Huppermage](https://dofus-grimoire.fr/classes/huppermage/) version affichée **3.7.12** et [Back From AFK, évolution Huppermage](https://backfromafk.com/classes/huppermage/depuis-3-0/) (comparaisons jusqu'à 3.7). Ces sources sont communautaires et l'état exact du client 3.7.13.17 reste à recouper.

## Sujet et résultat

La base 3.7.12 décrit six combinaisons élémentaires et leurs effets spécifiques, l'application des états par sort, la durée de runes et les conséquences du changement d'élément. L'analyse duo Xawpiya d'août 2026 est disponible avec 309 segments horodatés dans l'index ; elle décrit trois compagnons tactiques, mais pas les nombres de la simulation.

## Vidéos / sources réellement disponibles

[Xawpiya Huppermage duos 2026](https://www.youtube.com/watch?v=TmHIdyxtGmY) : sous-titres 01:05–03:15 (Steamer), 03:15–05:15 (Sadida), 05:15–07:05 (Zobal), `declare-video`. [Tenmalexis, tutoriel Huppermage 2025](https://www.youtube.com/watch?v=ACIK1JFdttc) : chapitres détaillés dans la description, mais transcription bloquée par IP ; seules les métadonnées/chapitres sont utilisables.

### Registre des règles atomiques

| ID | Énoncé mécanique | Statut | Limite |
| --- | --- | --- | --- |
| M19-R01 | Une cible reçoit un état élémentaire lié à l'action réellement résolue ; la combinaison ne se déclenche qu'à la succession conforme de deux éléments. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Conserver par cible l'état précédent, pas un drapeau global Huppermage. |
| M19-R02 | Le couple d'éléments détermine la catégorie d'effet ; les six couples doivent être discriminés. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Ne pas attribuer un retrait PM universel à toute combinaison. |
| M19-R03 | Les runes sont des ressources de terrain distinctes des états élémentaires sur combattants. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Une rune expirée, déplacée ou consommée ne doit plus participer à l'effet. |
| M19-R04 | La version 3.7 a modifié beaucoup de sorts Huppermage ; les combos 2025 doivent être contrôlés. | `corrobore-source` 3.7.12 ; à vérifier 3.7.13.17 | Ne pas figer des dégâts/portées tirés des vidéos anciennes. |

**Aucun `observe-video`, `valide-test` ou `valide-live` revendiqué ici.** Une description de vidéo ou une transcription thématique n'est pas une preuve des cellules et de l'ordre des trames. Sources exactes : [fiche 3.7.12](https://dofus-grimoire.fr/classes/huppermage/), [diff 3.7](https://backfromafk.com/classes/huppermage/depuis-3-0/) et, lorsqu'il existe, l'extrait d'auteur référencé ci-dessus.

## Plan pédagogique en plusieurs tours

T1 : appliquer un premier état élémentaire à la cible identifiée ; T1/T2 : déclencher un second élément différent si la cible, la portée et les ressources le permettent ; ensuite relever l'effet supplémentaire serveur et conserver séparément l'éventuelle rune.

## Contextes solo, duo, trio, quatre et plus

- **Solo** : mettre en concurrence le tour préparatoire et une action immédiate dont l'utilité est connue.
- **Duo** : Huppermage + Steamer : contrôle et tourelles ; +Sadida : endurance et propagation ; +Zobal : protection. Décrire l'initiative inversée, l'allié réellement bénéficiaire et la perte d'une étape.
- **Trio** : Huppermage + Steamer + Féca : compenser le tour de préparation. Expliquer la contribution marginale du troisième personnage et la responsabilité de chaque coût PA/PM.
- **Quatuor** : Sram + Féca + Sadida + Huppermage : composition commentée dans VID-TEAM4-01. Réserver une action à la sécurité du groupe, comparer au simple ajout de dégâts.
- **5–8** : même règle avec plusieurs acteurs mais conflits de cases et d'effets ; suivre séparément invocations, compagnons, buffs et challenges.

## Tests discriminants

**P01 positif proposé :** Deux effets de deux éléments différents sont confirmés sur la même cible avant expiration ; une combinaison spécifique est attendue selon le tableau du patch.

**N01 négatif principal proposé :** Même séquence avec première application refusée ou cible changée : aucun effet de combinaison ne peut être inventé.

**N02 timing et contre-scénario :** La cible disparaît/est désenvoûtée avant le deuxième sort ou un ennemi tue la tourelle de l'allié durant l'installation du terrain.

**N03 initiative :** un soutien/contrôle annoncé joue après l'attaquant ; le tour de l'attaquant ne doit pas anticiper son effet. **N04 refus serveur :** toute action non confirmée laisse les effets en attente et impose une relecture de l'état.

## Ce qui manque pour terminer la preuve

Récupérer un tutoriel avec audio/transcription horodatée propre et captures lisibles montrant exactement les cellules et états ; vérifier les coûts et capacités sur le client 3.7.13.17 ; obtenir des événements de combat expurgés et tests reproductibles. Les cas ci-dessus sont un **protocole d'étude**, non un replay réellement exécuté.

