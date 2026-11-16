# Reclamation de succes - capture MITM 31 oct 21:48 (Bastionbot, Mikhal)

Interface: icone coffre "Recompenses" au-dessus de la barre d'objets, apparue apres la connexion. Fenetre "Recompenses": ligne "Peninsule des gelees - Exploration, 91 513 XP" + bouton RECUPERER, bouton TOUT RECUPERER.
Ce succes etait deja termine AVANT la connexion (backlog): exactement le cas que le bot ignore aujourd'hui.
- C->S AchievementRewardRequest [mdw] champ1 = 247 (id du succes).
- S->C AchievementRewardResultEvent [mdj] champ1 = 247, champ3 = 1 (succes). Chat: "Vous avez gagne 91 513 points d'experience."
- A la connexion 21:47:54: S->C AchievementsEvent [mdp] 1462 champs (dump tronque a 239 lignes; entrees #1 msg {#1 int, #2 int id perso}). Le marqueur "recompense en attente" doit se trouver dans ce message ou un voisin (RideAchievement* sont pour les montures). A determiner depuis le mapping (data/fields.json, server-mapping/fields.json) et la structure du message.
