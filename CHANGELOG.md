# Journal des versions

## 1.3.3.11 — 23/10/2026
- Forgemagie : onglet complet (file d'objets du sac, objectifs lus sur l'objet réel, exos, jet parfait, runes du sac par palier, journal de chaque rune, arrêt) ; le bot passe les runes à l'atelier de la carte (6 ateliers) ; rune conseillée passable à la main ; 45 familles de runes. Séquence d'atelier encore à confirmer en jeu.
- Forgemagie : achat automatique des runes manquantes à l'HDV des runes le plus proche (lots plafonnés au % du prix moyen, budget), retour à l'atelier et reprise.
- Forgemagie : notification et webhook Discord pour un exo, un over et un objet terminé (runes, kamas, durée) ; option par perso.
- Lua : module fm: (item, setTarget, clearTargets, advise, apply, autoRun) et forgeItem.
- Combat : mode automatique « tous mes sorts appris » (sorts à dégâts ajoutés à la liste, par dégâts/PA ; décocher écarte) ; combat:setAutoSpells. En équipe, les suiveurs frappent d'abord l'ennemi que leur chef vient de toucher.
- Chasse au trésor : onglet dédié (étapes nommées, drapeaux, creuser, abandon confirmé), « Y aller » vers la carte de l'étape (direction ou indice DofusDB) et bouton Chasse automatique.
- Comptes : bannissement détecté en Socket mémorisé (pastille Banni, date de fin), plus de lancement automatique jusqu'à Débloquer, une seule alerte par ban.
- Paramètres : copie de tous les réglages d'un perso vers d'autres (tout cocher).
## 1.3.3.10 — 23/10/2026
- Combat : frise de l'ordre de jeu en haut de la carte (déduite des débuts de tour : camp, PV %, combattant qui joue encadré, morts estompés) ; Lua combat:turnOrder().
- Carte de combat : dégâts et soins flottants au-dessus des combattants.
- Réglages : « Sauvegarde des réglages » (zip sur le Bureau, comptes en option) et restauration appliquée au redémarrage, l'ancienne version étant gardée.
- Console : Ctrl+F place le curseur dans la recherche du Journal, Échap la vide.
- Tests : le projet PureTests compile de nouveau (322 tests).
## 1.3.3.9 — 23/10/2026
- Vue d'ensemble : filtre des sessions (toutes / en jeu / à vérifier) et recherche par nom, serveur ou zone.
- Notification quand un personnage passe sous 25 % de PV hors combat (les pods pleins restent gérés par les notifications existantes).
- Fiche du bot : perso précédent / suivant (boutons de l'en-tête, Ctrl+Page haut / Ctrl+Page bas).
- Infos : dépense manuelle des points de caractéristiques (stat + nombre, hors combat, confirmation en deux clics).
- Console : recherche dans le Journal (mot, joueur, catégorie).
- Simulateur de combat : « rafale max théorique » sur une cible avec tes PA (montre si l'ordre des priorités perd des dégâts).
- Outil MCP get_alerts : points d'attention par bot (mêmes règles que la Vue d'ensemble).
- Livraison : le dossier WebAuth (connexion Ankama par navigateur) est inclus dans le dossier publié.
## 1.3.3.8 — 23/10/2026
- Vue d'ensemble : alertes « pods presque pleins » et « PV bas hors combat » ; chaque carte affiche le rendement des dernières 24 h (xp/h, k/h, combats) ; bouton « Rapport d'incident » (zip sur le Bureau avec les journaux récents, secrets masqués, et l'état des sessions ; rien n'est envoyé).
- Forgemagie : carte « Conseil de rune » (objectif et jet max par ligne, runes du sac, over autorisé ou non) ; rien n'est posé en jeu.
- Réglages : pause et abandon des combats anormalement longs (nombre de tours) réglables.
- Fiche du bot : l'onglet consulté est conservé en revenant sur la page Bots.

## 1.3.3.7 — 22/10/2026
- Outil MCP advise_rune (conseiller de forgemagie hors jeu, lecture seule) ; module RuneAdvisor testé.

## 1.3.3.6 — 22/10/2026
- Menu : « Vue d'ensemble » ajoutée à l'interface à onglets ; chaque carte de perso affiche maintenant ses PV, ses pods, ses kamas et sa zone.

## 1.3.3.5 — 22/10/2026
- Combat très long : au-delà de 150 de tes tours, le moteur se met en pause (journal) pour que tu reprennes la main ; l'abandon automatique (AppConfig.AbandonFightAfterTurns) est désactivé par défaut.
- Panneau de combat : journal des variations de PV (« Moi : -120 PV », « Ennemi (case 245) : mort »).

## 1.3.3.4 — 22/10/2026
- Livraison autonome : le dossier embarque le runtime .NET 10 (plus besoin de l'installer ; sur un Windows avec seulement .NET 9, l'appli ne démarrait pas).

## 1.3.3.3 — 22/10/2026
- Couleurs : palette sombre neutre relevée sur l'interface web de MizanBot (fond #131313, cartes #171717, sélection #212224, textes plus nets).

## 1.3.3.2 — 22/10/2026
- Combat : le panneau « je joue » n'apparaît qu'en combat, plus petit ; le bouton « Je joue mes combats » est dans le bandeau (corrige son état qui se désynchronisait) ; bouton Abandonner (en combat seulement, 2 clics) ; flèches et encart de carte masqués en combat.
- Bouton Havre-sac dans le bandeau (hors combat).
- HDV : le carnet d'hôteliers est consulté sans attendre le délai complet, et un message précis dit si la case de l'hôtelier est introuvable.

## 1.3.3.1 — 21/10/2026
- Fiche du bot façon MizanBot : bandeau Niveau + XP + abonnement + kamas avec « Lancer / arrêter le script » et « Tout déposer » ; barre d'état en bas (vie, énergie, pods, kamas, position) ; l'ancienne carte « État » est repliée.

## 1.3.3.0 — 21/10/2026
- Sorts : bouton « ⇄ variante » dans la rotation (Stalagmite / Onde céleste…), hors combat ; outil MCP set_spell_variant.
- Carte : infobulle d'un groupe de monstres au survol (niveau total + chaque monstre et son niveau, comme en jeu) ; clic droit sur un joueur : proposer un échange, inviter dans le groupe, copier le pseudo.
- Expédition : après un combat, le trajet attend la vraie carte avant de planifier (évitait ~40 s de détour par le havre-sac).
- Correctif : minuteur du combat manuel démarré par la vue (et plus par le ViewModel).

## 1.3.2.0 — 21/10/2026
- Combat jouable depuis la carte du bot : « Je joue mes combats » coupe le placement/défi/tours automatiques ; cases de placement orange (clic = se placer), choix du défi, bouton Prêt, barre de sorts (clic sort puis case, portée en vert), déplacement en bleu, Passer, bandeau PA/PM/tour, journal des actions.
- HDV : l'hôtelier absent de la liste du serveur (Astrub) est retrouvé via un carnet (relevé intégré + apprentissage quand tu ouvres un HDV à la main en MITM).

## 1.3.1.2 — 21/10/2026
- Abonnement Dofus : la date de fin (ou « non abonné ») est lue dans la réponse d'identification du serveur de connexion, comme GivreActive (champ subscription_end_date + droits accès illimité / abonnement infini). Fonctionne en Socket dès la connexion ; ligne « abonnement : fin=… » dans le journal.

## 1.3.1.1 — 21/10/2026
- Carte du monde DofusDB (zaaps, envoyer le bot ici), flèches de direction autour de la carte du bot (2e clic = annuler).
- Inventaire : rendu du personnage, total des caractéristiques de l'équipement (bonus de panoplie inclus), Équiper/Utiliser masqués pour les ressources.
- Console : 3e vue « Discussion » (MP et canaux) ; outils MCP read_chat, send_private_message, plan_production_chain, recommend_zones, estimate_spell_damage.
- Combat : sorts de zone visés sur la case qui touche le plus d'ennemis, case adjacente, même sort plusieurs fois, Zombie/Nomade, estimation de dégâts (budget « achever »).
- Expédition : plusieurs sous-zones, une clé de donjon absente n'arrête plus le script (trousseau).
- Coordonnées en combat (plus de 0,0), voyage par coordonnées quand le graphe est incomplet (HDV).
- Page téléphone : état réel des comptes, choix Socket/MITM, scripts, chat.
- Performance : collectes mémoire de génération 2 en arrière-plan.
- Alerte (lecture seule) si le fichier hosts redirige Ankama vers ce PC.
