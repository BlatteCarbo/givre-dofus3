# Bastionbot — Bûcheron, 26 octobre 2026

Demande utilisateur : améliorer le script joint `pasted-text.txt` et le mettre sur Bastionbot. Le script pêcheur a été arrêté hors combat, son arrêt relu, puis le nouveau script sauvegardé et lancé : **Bastionbot - Bucheron 1-200 ameliore.lua**. L'ancien script pêcheur reste sauvegardé ; ne pas le rétablir automatiquement lors des contrôles de session.

Compte ciblé : `c4a95aedecaf4a82803e6c64eb33ca38`. Version active : publication CombatLife du 26 octobre. Bûcheron niveau 200 observé via get_character_info puis confirmé par le script. Aucune modification des autres comptes.

## Changements

- Données des 18 paliers originales conservées. Contrôle statique de fermeture de chaque circuit ; ceci ne prouve pas l'accessibilité de toutes les cartes.
- Filtre `ITEMS_TO_GATHER` construit depuis les ressources produites par `SkillData.parentJobId=2` dans la base installée ouverte en lecture seule. Récolte de bois uniquement ; filtre d'interactifs laissé vide pour ne pas mélanger les identifiants.
- Déduplication des circuits : au niveau 200, circuits réellement distincts des paliers 200 (40 étapes), 150 (36) et 130 (28).
- Positionnement par carte source et curseur, arrivée relue après voyage ; garde combat avant/après récolte et voyage.
- Circuits et transitions indisponibles mis en pause temporaire, six échecs entraînant pause et resynchronisation ; aucune blacklist permanente.
- Banque Astrub à 85 %, dépôt complet, fermeture du dialogue et contrôle de diminution du poids ; arrêt si dépôt non confirmé. Aucun retrait ni achat.
- Arrêt sur défaite ou résultat de combat inconnu. Aucun combat volontaire ajouté.

## Vérifications

Syntaxe Lua valide, lint vide via l'application. Niveau 200 et trois circuits distincts confirmés dans la console (2096–2100), voyage réel vers l'entrée 155977218 commencé avec téléportation confirmée. Validation des récoltes et de la banque à compléter ci-dessous. Pas de recompilation nécessaire pour ce changement de script.

SHA256 initial du script : `de0ab46f51d18bfe63bd2d2ebd9446ecf2d07c531f57e00493eec26915030145`.

Récolte réelle confirmée à 10:33 UTC : arrivée 155977218 (2121), Noyer ×16 (2123), If ×9 (2125), puis transitions confirmées vers 155977217 et 155976705 (2129/2133), nouvelle récolte d'If démarrée (2134). Script actif et progression sur plusieurs cartes. Dépôt bancaire et protecteur non rejoués pendant cet essai ; ces branches restent non validées en session pour ce nouveau script.
