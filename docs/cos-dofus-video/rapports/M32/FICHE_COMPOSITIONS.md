# M32 — Fiche pédagogique : du solo au quatuor, puis adaptation

## Pourquoi les duos/trios peuvent échouer malgré les bonnes classes

Un duo n'est pas deux profils moyens de dommages : la sortie d'une action de placeur détermine la légalité de l'attaque suivante. L'ordre d'initiative inverse cette dépendance. Une stratégie de préparation (bombes, tourelles, runes) profite d'une protection seulement si celle-ci s'applique pendant la fenêtre dangereuse.

### Arcs de synergies documentaires à examiner

| Arc | Exemple concret | Comment savoir que le combo a marché ? | Source |
| --- | --- | --- | --- |
| Placeur→attaque au contact | Pandawa→Iop Colère | cellule de l'ennemi et disponibilité de Colère confirmées au tour Iop | [M08](../M08/RAPPORT.md), [M14](../M14/RAPPORT.md) |
| Entrave→kiting | Enutrof→Crâ | retrait réel rend le chemin d'attaque adverse impossible | [M16](../M16/RAPPORT.md), [Xawpiya](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=265s) |
| Protection→mise en place | Féca→Roublard | bombe toujours vivante lorsque le mur devient exploitable | [M02](../M02/RAPPORT.md), [M09](../M09/RAPPORT.md) |
| Portail→frappe du finisseur | Éliotrope→Iop | entrée et sortie du portail connues, cible réellement atteignable | [M04](../M04/RAPPORT.md), [M14](../M14/RAPPORT.md) |
| Déplacement→poussée contre mur | Pandawa→Forgelance | position/mur confirmés ; résistances de poussée évaluées | [M10](../M10/RAPPORT.md) |
| Soins→survie du finisseur | Eniripsa→Iop | Iop survit pour jouer au tour chargé ; soin réellement reçu | [M15](../M15/RAPPORT.md), [M14](../M14/RAPPORT.md) |
| Invocations→contrôle | Osamodas→Ouginak | occupation par créatures améliore la menace à la proie | [M18](../M18/RAPPORT.md), [M22](../M22/RAPPORT.md) |
| Boucliers→stratégie lente | Zobal→Steamer/Huppermage | la structure mise en place survit jusqu'à son action | [M12](../M12/RAPPORT.md), [Xawpiya](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=65s) |

Le tableau donne des **hypothèses à falsifier**, sauf lorsque le créateur décrit le couple à l'instant cité. Aucun arc n'est déclaré `valide-live`.

## Tour comparatif à dessiner

```text
Tour global T:
P place E sur e' (source événement confirmé)
 ├─ OK → N essaie de retirer PM sur E
 │         ├─ retrait confirmé → C choisit case sûre et tire
 │         └─ retrait esquivé → C recalcule mobilité de E
 └─ refus → N contrôle depuis position initiale, C tire si angle connu
F joue ensuite : protection selon PV et menace actualisés
```

**Trois précautions** : ressources PA/PM indépendantes par acteur, un effet qui expire avant le tour du bénéficiaire n'a pas de valeur, et chaque challenge peut imposer une action différente du meilleur score de dégâts.

## Transposition à 5–8

Ajouter le cinquième combattant **seulement** s'il apporte une action nouvelle mesurable : second contrôle sur une autre cible, retrait d'état, résurrection, protection de l'invocation, gestion du succès. Deux placeurs sur la même cible peuvent entrer en conflit, de même que deux entités convoitant une case. Voir [M33](../M33/RAPPORT.md).

## Exercices à résoudre

1. Le Panda joue en dernier : quelle action Crâ peut-il réellement prendre aujourd'hui, et laquelle préparer pour le prochain tour ?
2. Un retrait de 3 PM échoue : calculer les cases atteignables de l'ennemi, sans modifier arbitrairement ses PA.
3. La Colère du Iop est prête mais l'ennemi vient de devenir invulnérable : prioriser survie et prochain tour utile.
4. Le Zobal protège une tourelle déjà morte : quel est le vrai bénéficiaire du bouclier ?
5. Le challenge Duel impose un ennemi différent à chaque attaquant ; le quatuor se répartit-il les cibles de façon compatible ?

Réponses : obtenir les positions/effets via événements et chercher une action alternative ; ne jamais convertir un nom de classe en transition garantie.

