# MizanBot 1.3.3 — cartographie de l'interface (relevé du 22/10/2026)

Relevé fait à l'écran sur une session réelle (compte player.three, perso Opencode niv. 4, mode Socket, interface « Web »). Sert de référence pour GivreClone.

## Palette (échantillonnée)
Fond de page #131313 · barre latérale / titre #161616 · cartes #171717 · élément sélectionné #212224 · barre vide #454545 · accent ambre #E6A63A / #ECAB35 · vert #3EBA4A · texte principal #ECECEC. (Appliquée à `Skins/classic-palette.axaml` en 1.3.3.3.)

## Navigation globale (barre latérale)
Comptes · Bots (compteur) · Équipe · Proxys · Adaptateurs · liste « Personnages » (pastille d'activité, niveau) · Superviseurs (+) · « N en jeu » · Mon compte · Réglages. Barre de titre : Discord, notifications (pastille), réduire / agrandir / fermer.

## Comptes
Boutons : Ajouter les comptes du Launcher Ankama (import sans mot de passe) · Importer une liste · Ajouter un compte · Lancer tout / Arrêter tout. Par compte : case à cocher, badge MITM/Socket, état + pseudo/serveur/IP, choix de connexion (écran de choix, serveur, perso), choix de proxy, Lancer, Socket, engrenage (paramètres), corbeille. Statut en clair au survol (ex. « identifiants refusés »).
Socket : fenêtre « Choisissez un serveur » (état En ligne / mono-compte, persos présents, dernière connexion) puis « Choisissez un personnage » (+ Nouveau personnage).

## Fiche du bot
En-tête : avatar, nom, état, serveur, badge Socket/MITM · Plus (Ouvrir la banque ici, Copier l'identifiant, Reconnecter, Passer en MITM/Socket) · Déconnexion · perso précédent / suivant. Bandeau : niveau + barre d'XP, abonnement (date + jours restants), Lancer le script, Tout déposer. Colonne « Actions rapides » à droite (grand écran). Barre d'état : Vie, Énergie, Pods, Kamas, Position + zone.
Onglets verticaux : Console · Combat · Carte · Inventaire · Banque · HDV · Chasse · Lua · Stats · Planning · Paramètres · Paquets · Infos.

- **Console** : Journal / Technique ; filtres par canal (Bot, Privé, Général, Commerce, Recrutement, Groupe, Guilde/Alliance) ; niveau de détail ; recherche mot ou joueur ; Pause, Copier, Vider ; envoi de chat (canal + destinataire + message).
- **Combat** : Comportement / Sorts / Importer d'un autre perso / Simuler le tour. Automatisation (jouer auto, pré-combat auto), verrouillage (spectateurs, combat, groupe), challenges (choisir un challenge, adapter l'IA, abandon sous % de PV), abandon après N tours, mods. Sorts : rotation par priorité.
- **Carte** : relief 3D avec icônes de monstres, étiquettes « Nom Lv N », infos (map id, position, cellule), acteur survolé, flèches de changement de carte, bascules transitions / numéros de cellule, archimonstres récents, légende. Clic droit = PNJ, zaap, joueur, atelier.
- **Inventaire** : kamas, nb d'objets, pods ; fiche du perso habillé avec emplacements ; grille d'objets (icône, quantité, gid), recherche, catégories, tri ; clic droit = équiper / utiliser / jeter / détruire / déposer.
- **Banque** : bandeau « banque fermée » + Ouvrir la banque ici ; type de dépôt, recherche, tri.
- **HDV** : sous-onglets HDV · Achat · Rentabilité · Statistiques · Réglages ; mes ventes (sous-coter, Tout sous-coter, Rafraîchir), contrôle (HDV cible, repricing auto), remise en vente automatique, journal d'activité, bilan (ventes, taxes, valeur, pertes), mise en vente sac + banque.
- **Chasse** : lancer la chasse, planning, réglages ; console de chasse ; préparation ; bilan (gagnées, perdues, abandonnées, réussite, durée, étapes) ; base d'indices (cartes connues, apprises en jeu, version serveur).
- **Lua** : éditeur (modèles, ouvrir, récents, enregistrer, format, recherche, vérifier, référence API), Enregistrer trajet, Lancer, console du script.
- **Stats** : 24 h / 7 j / 30 j / 1 an, tuiles (récoltes, combats, temps moyen, temps de botting, kamas, ressources, XP, passages banque), courbe, dernières actions, export CSV.
- **Planning** : planning activé / connexion auto, grille 7 jours, humanisation, bibliothèque de scripts, plages.
- **Paramètres** : vitesse de déplacement, anti-blocage, succès auto, messagerie privée (réponse auto, mots-clés/IA, délais, cooldown, copier vers d'autres persos).
- **Paquets** : liste filtrable (Tout / Entrant / Sortant / Reconnus), arbre / JSON du paquet, épingler, vider.
- **Infos** : personnage, classe, sexe, abonnement, kamas, XP, énergie, Vie / PA / PM / PO, caractéristiques (+1, +10, Tout, remise à zéro), métiers avec niveau et XP.

## Réglages globaux
Langue (FR / EN / ES) · thème d'accent (6) · interface (MizanBot / Façon Givre / Web) · sons et volume · musique d'ambiance · effets (particules, rendu économe) · …

## Idées retenues pour GivreClone (statut)
- Fait : palette, bandeau niveau/abonnement/kamas, barre d'état, panneau de combat jouable, bouton Havre-sac, Abandonner (2 clics).
- Fait le 23/10 (1.3.3.8-1.3.3.10) : pause / abandon après N tours (Réglages), frise d'ordre de jeu (déduite des débuts de tour) et dégâts flottants, onglet gardé ouvert, perso précédent / suivant, rapport d'incident local, recherche dans la console, filtre des sessions et rendement 24 h dans la Vue d'ensemble, dépense des points de caractéristiques, conseil de rune.
- À faire : « Actions rapides » en colonne (grand écran), abandon d'un challenge bloquant, console de chasse (données d'indices manquantes), résistances au survol en combat (id du monstre non exposé côté combattants).
