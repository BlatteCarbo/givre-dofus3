# Plan à confier à Astra (22/10/2026)

Contexte : MizanBot 1.3.4 (notes du 22/10) a ajouté la forgemagie, un mode de combat automatique planifié, une Vue d'ensemble, Ctrl+K, des équipes groupées dans la barre latérale et des pastilles riches dans le chat. Captures et vidéos reçues : onglet Forgemagie (atelier + réglages), Vue d'ensemble (cartes par perso), Forgemagie Tracker (site), combat auto avec frise de l'ordre de jeu, atelier Joaillomager dans le client.

## Ce que GivreClone a déjà (à ne pas refaire)
Dashboard (« Vue d'ensemble » ajouté au menu 1.3.3.6, avec PV / pods / kamas / zone), accès rapide (Ctrl+K), équipes dans l'arbre de gauche, console « Discussion », DamageEstimator branché sur « Achever », combat jouable à la main, variantes de sorts, carnet d'hôteliers HDV.

## Lots à spécifier / valider avec Astra
Pour chaque lot : demander une spécification courte (comportement, écrans, données, cas limites, test en jeu) et la liste des paquets à capturer.

1. **Forgemagie (gros lot, L)**
   - Écrans vus : atelier (objet, jets min-max, actuel, objectif, plafond, « Pa dès », « Ra dès », lissage, exo/over à ajouter, prochaine rune), file d'objets, runes du sac par palier, bilan (critiques / neutres / échecs / exos / objets finis / runes en kamas), journal ; réglages (remontage, rythme, exos et overs, achat de runes, atelier).
   - Paquets à capturer (à faire à la main, Capture exhaustive ON) : ouverture d'un atelier de forgemagie, pose de l'objet, pose d'une rune, résultat de la fusion (critique / neutre / échec, nouveaux jets, reliquat), retrait, fermeture.
   - Décision à demander : moteur de conseil de rune (règles du « reliquat » et des runes Pa/Ra) à implémenter en pur C# testable, avant toute automatisation.
2. **Combat : mode automatique planifié (L)** — planifier la meilleure suite de lancers avec les PA (combinaisons d'états, PV restants, vols de vie, téléportation, zones en ligne). Demander une architecture par étapes à partir de `DamageEstimator`, `SpellCastCheck` et `CombatMovePlanner`, avec un simulateur de tour pour valider sans jouer.
3. **Frise de l'ordre de jeu (M)** — capture requise : liste des tours au début du combat et mises à jour (mort, invocation). Sans capture, ne rien coder.
4. **Chat : pastilles d'objet / monstre / guilde (M)** — au clic sur un objet lié : caractéristiques, prix moyen (déjà en base HDV), bouton MP. Capture requise : message de chat contenant un objet lié, un groupe de monstres, une position, une guilde. Reprendre la structure protobuf avant toute interface.
5. **Interface** — équipes repliables avec chef marqué et membres hors ligne grisés (existe en partie), alertes de la Vue d'ensemble (déconnecté, banni, pause modérateur, bloqué, pods pleins), caractéristiques au total avec détail base / équipement / additionnel / temporaire.
6. **Sécurité et reprise** — pause avant abandon sur combat très long (fait en 1.3.3.5), détection de ban, rapport d'incident local, reconnexion bornée.
7. **Proxy en MITM** — vérifier que tout le trafic du client Dofus (chat inter-jeux, services Ankama, rapports d'erreur) sort bien par le proxy du compte, pas seulement la connexion au jeu.

## Prompt à coller dans Astra
« Tu es chef de produit / architecte pour GivreClone (C# .NET 10, Avalonia 12, Lua, MCP). Je te donne les notes de version de MizanBot 1.3.4 et la liste des lots ci-dessus. Pour chaque lot : (1) valeur, effort, risque ; (2) spécification courte ; (3) liste exacte des captures de paquets à faire pour lever les inconnues ; (4) plan de test en jeu ; (5) ordre de réalisation sur 5 jours. Contraintes : aucune copie de code ni de texte de MizanBot, un seul développeur, PC modeste, aucune dépense en ogrines sans confirmation, tout ce qui agit en jeu doit pouvoir être coupé. Rends un tableau puis les spécifications des 4 premiers lots. »

## Captures à faire ce soir (Capture de découverte + Capture exhaustive dans les Réglages)
- Une forgemagie à la main : ouvrir l'atelier, poser un objet, passer 3-4 runes (idéalement 1 critique, 1 neutre, 1 échec), reprendre l'objet, fermer.
- Un combat avec plusieurs monstres : du placement à la fin (pour la frise de l'ordre de jeu).
- Un message de chat avec un objet lié, un groupe de monstres partagé, une guilde.
Ensuite, dis-moi « captures faites » : je lis les lignes `[pkt-dbg]` et je décode.
