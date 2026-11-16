# M11 — transmission au développeur

**Trois règles candidates prioritaires :** M11-R01 résistance initiale du boss, M11-R02 déclencheur lié au tonneau et à l'adjacence, M11-R03 attribution du succès à **chaque cible effectivement affectée**. Sources et statuts dans [FICHE_GOURLO.md](FICHE_GOURLO.md).

**Entrées** : snapshot positions et visibilité, ordre d'initiative, état des tonneaux (owner, position, alive, âge, élément), résistances de la cible par élément, challenge actif, acteurs ayant déjà subi un tonneau.

**Transitions** : invocation → création d'une entité T ; déplacement E → nouvelle case si confirmée ; tour T → effet vers E si confirmé ; changement résistance/état → mise à jour ciblée ; mort T ou expiration → retirer uniquement ce qui est prouvé terminé.

**Test positif** : E adjacent à T au moment où T joue, effet observé, D choisit une attaque légale dans l'élément déverrouillé. **Test négatif** : E éloigné ou T mort ; aucun effet anticipé ni « cible déjà comptée » dans le challenge. Tous les tests sont proposés et non exécutés.

**Blocages** : version/effets exacts du Tonneau Pirate, chronologie de troisième tour, résolutions du serveur, conditions des succès. Ne pas synthétiser ces inconnues en valeurs arbitraires. L'intégration doit attendre un replay daté et une définition de sortie vérifiable.

