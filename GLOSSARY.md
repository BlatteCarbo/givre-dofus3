# Givre : combat

Le bot joue les tours de combat Dofus de ses personnages et de leurs compagnons contrôlés.

## Language

**Combattant joué**:
Un combattant dont le bot décide les actions : le personnage du compte ou son compagnon contrôlé.
_Avoid_: bot, acteur, owner

**Lancer**:
Une demande de sort sur une case, comptée seulement une fois confirmée par le serveur.
_Avoid_: cast envoyé, tentative

**Refus**:
La réponse du serveur qui rejette un lancer, avec son code (174 ligne de vue, 175 sort indisponible, 229 piège sur la case).
_Avoid_: échec, erreur de cast

**Recharge**:
Le nombre de tours qu'un sort doit attendre après un lancer : la plus longue entre celle du jeu et celle du profil.
_Avoid_: cooldown du profil, intervalle

**Registre des lancers**:
La mémoire d'un combattant joué : ses lancers, ses refus et leurs durées de vie (combat, tour).
_Avoid_: historique, cache de sorts
