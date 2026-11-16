# Essai MITM du 24 octobre, 23 h 15

Autorisation utilisateur explicite du compte de test ; l'entrée correspondante de McpBlockedLogins a été retirée uniquement dans l'instance isolée `port-validation-20261024`. Les protections et les sessions de l'instance d'origine n'ont pas été modifiées. Aucun identifiant de connexion ni secret n'est reproduit ici.

Version exécutée : publication `GivreClone-Reprise-20261024-2312`, code applicatif validé lors de la reprise `d623215`. Client graphique MITM PID 17460, Sadida Minerbot-Vaincra niveau 155, entrée en jeu confirmée. Aucun script au lancement.

## Combat de référence sans options visuelles

Les six options visuelles et le tactique sont désactivés dans le profil de la copie. Turbo et sorts automatiques actifs, règles de défis désactivées. Script borné à un combat, groupe de deux monstres, sur la carte courante 88085774, avec régénération avant engagement et budget de recherche de 90 secondes.

Combat vers 23:16:26–23:16:30 : victoire serveur `issue=2`, deux tours, 11 564 XP. Ligne `MITM_BASELINE won=true` puis fin du script observées. Le classement automatique des sorts apparaît explicitement au deuxième tour, suivi du lancer 13528 et de la mort de la cible. Les PV serveur sont suivis. Après le test, `scriptRunning=false` et `inFight=false`.

`fight_metrics` : un écart inter-tours de 82 ms, quatre accusés externes comptabilisés. Cet écart comprend les tours adverses ; ce n'est ni une durée d'injection ni une comparaison statistique avec Socket. Ce Sadida ne valide pas le buff de portée du Crâ.

## Cycle des six options hors combat

Chaque option activée seule par `set_combat_profile` ; le fichier du module retourne successivement :

- Sans effets des sorts : `READY mask=1 in_combat=0`.
- Lancers courts : `READY mask=2 in_combat=0`.
- Sans animations d'attaque/sort : `READY mask=4 in_combat=0`.
- Sans animations de mort : `READY mask=8 in_combat=0`.
- Déplacements instantanés : `READY mask=16 in_combat=0`.
- Sans attentes visuelles : `READY mask=32 in_combat=0`.

Après désactivation de toutes les options : `DISABLED`. Le client reste connecté, hors combat et sans script. Cela prouve le raccordement des masques et le retour à l'état désactivé, pas l'effet visuel ni la restauration complète de chaque animation.

## Reprise avec observation Windows disponible

Après relance de l'hôte, computer-use a permis les captures du client. L'indisponibilité précédente n'est plus le blocage courant.

Les six options ont ensuite été activées séparément en combat, avec état natif `READY`, respectivement masques 1, 2, 4, 8, 16 et 32, et victoire serveur après chaque engagement réussi. Repères locaux : 23:55:37, 23:56:50, 23:58:37, 00:04:40, 00:05:16 et vers 00:07. Les premières recherches sans engagement pour morts et attentes ne sont pas comptées comme combats. Après désactivation des six options, état `DISABLED` et nouveau combat gagné. Cela confirme le raccordement en combat et le retour désactivé ; les durées exactes et chaque animation transitoire ne sont pas mesurées par ces captures. Un refus de sort est apparu pendant l'essai déplacement instantané, sans cause établie.

Le client a affiché le dialogue d'inactivité, y compris sans temporisation Lua artificielle. Des fenêtres de tour se sont terminées pendant l'exécution du moteur. Le lien causal avec une requête client AFK reste à confirmer par une trace explicite.

## Garde-fou de fin de tour AFK (validation en cours)

Le filtre des trames client reconnaît désormais `FightTurnFinishRequest.is_afk`. Il supprime uniquement une valeur explicite 1 en MITM, pendant un combat et un tour ouverts, pilotés par le moteur ou un hook Lua autorisé. Les fins manuelles, valeurs inconnues, sessions Socket et tours non possédés sont transmis. Chaque suppression produit une trace sans contenu de paquet. Le catalogue requis inclut ce champ. Huit cas testent la politique ; la capture réelle de la suppression reste nécessaire avant de déclarer la cause et le correctif confirmés.

## Tactique : échec confirmé

Le module négocie ABI 8 mais échoue sur `Core.Rendering.MapRenderer`, absent des types résolus du client 3.7.12.12. Les diagnostics natifs indiquent désormais la classe requise manquante. Une recherche structurelle n'a trouvé aucune cible compatible et n'est pas conservée. Aucun fonctionnement du tactique, de F8 ou de sa restauration n'est revendiqué. Voir le README du module pour l'empreinte reconstruite.

## Expédition

Essai réel de la limite pods sur le Sadida : à 00:01:37, pods 85 %, seuil 80 %, arrêt immédiat du script avant déplacement ou combat, carte 88085773 inchangée. Un trajet voisin a confirmé l'arrivée serveur sur 88085774 vers 00:02:12. Les recherches bornées sans groupe éligible se sont arrêtées ; elles ne prouvent pas un catalogue vide.

Régressions du Lua réellement généré : catalogue vide, seuil pods sur une ou plusieurs cartes, exclusion des cartes inaccessibles et durée maximale sans groupe éligible. Les scénarios de défaite et regroupement ont une couverture automatisée ; leur validation réelle complète reste ouverte.

## Identité des comptes

Foragerbot reste déconnecté suivant la consigne utilisateur. Une tentative de reprise par l'ancien alias MITM Archerbot a révélé un autre compte (liste de personnages incompatible) ; il a été immédiatement déconnecté sans script ni combat lancé. La copie de test a ensuite été corrigée avec l'identifiant exact de l'entrée Socket d'Foragerbot/Rangerbot. Ne pas déduire l'identité d'un compte de son alias d'affichage.

## Validation du code

Build application Release réussi (0 avertissement, 0 erreur dans la dernière exécution incrémentale). Suite reconstruite : 2 948 réussis, 2 ignorés, 0 échec. Une première exécution `--no-build` utilisait le manifeste protocolaire antérieur ; la reconstruction complète a résolu cette incohérence. 2 701 clés uniques dans chacune des langues fr/en/es. Les journaux complets et données de compte restent locaux.

## Correctif de sélection explicite MITM

L'essai avec le bon compte a révélé que `LaunchInGameForTeamAsync` imposait `AutoConnectType=2`, même avec un nom explicite. Le client a reconnecté Foragerbot avant l'injection différée de sélection du Crâ. Foragerbot a été immédiatement déconnecté, sans script ni combat. Le chemin utilise maintenant 1 (choix du personnage) lorsqu'un personnage est demandé, et conserve 2 pour un lancement sans personnage explicite. La sélection Socket reste inchangée. Sélection corrigée confirmée : Rangerbot, Crâ niveau 15, entré en jeu en MITM (PID 22040), sans reprise du script d’Foragerbot.

## Buff de portée du Crâ confirmé en MITM

25/10, 02:28:12–02:28:16, Rangerbot niveau 15 contre un Tournesol Sauvage, toutes options visuelles et tactique désactivés. Buff 32465 lancé à 02:28:13.030, 68 modificateurs appliqués après confirmation à .076. Deux lancers 32444 à distance 8 à .128 et .160. Le rang 1 de SpellLevelData donne une portée de base 6 (niveau requis 15) ; la cible passe de 99/99 à 59/95 au tour suivant. Le serveur a donc accepté un lancer au-delà de la portée de base. Victoire issue=2, 9 512 XP, trois tours ; niveau 16 et PV 142/142 après combat. Script borné terminé.

Aucune expiration Turbo relevée dans ce combat. Deux écarts entre tours de 253 et 25 ms, tours adverses compris ; dix accusés externes comptabilisés. Ces deux observations ne constituent pas une mesure statistique de performance. Aucune suppression AFK observée pendant ce combat court ; ce garde-fou reste à confirmer lors d'un état inactif.

Après le correctif de sélection : nouveau build application réussi et nouvelle suite complète, 2 948 réussis, 2 ignorés, 0 échec.

## Catalogue vide réel et échec d'engagement

Le script vide exporté par ExpeditionScript.Build a été exécuté sur le Crâ : message « zone vide », arrêt immédiat à 02:29:34, carte 189792256 inchangée, aucun engagement. Les accents du message ont été altérés par le helper d'export console temporaire ; le code applicatif n'a pas été modifié pour cela.

L'essai d'abandon n'a pas atteint le combat : demandes répétées d'attaque du groupe -20001, acquittées à l'injection mais sans début serveur, avec délai de 12 secondes. L'essai a été arrêté puis le compte déconnecté. Aucun abandon effectif ni validation de défaite réelle n'est revendiqué. Une boîte de montée de niveau était également affichée ; sa causalité avec les refus n'est pas établie.

Correctif : les échecs d'engagement ordinaires sont désormais exclus individuellement pendant 60 secondes. Le choix peut passer aux autres groupes et l'exclusion est effacée au changement de carte ; le mode camp garde son mécanisme existant. Trois tests couvrent le choix alternatif, l'expiration individuelle et les identifiants réutilisés sur une autre carte. Le raccordement réel du correctif reste à valider.

## Défaite réelle confirmée après reconnexion

25/10, 02:35:23, client MITM 15660, script exporté par ExpeditionScript.Build avec un hook onTurn limité à un abandon. Engagement confirmé à 02:35:25.330, hook appelé une fois à .744, ContextQuitRequest injecté à .771. Bilan serveur issue=0 à 02:35:26.258. À 02:35:27.088 : « défaite, arrêt », puis fin du script. État final hors combat et sans script, carte 192416776, PV 81. Aucun nouvel engagement. L'échec de l'essai précédent reste décrit plus haut ; cette nouvelle exécution valide l'arrêt après défaite, pas la cause des refus précédents ni l'exclusion temporaire en réel.

AutoRevive remis à sa valeur initiale true dans la copie ; les options visuelles restent désactivées. Compte de test déconnecté, Foragerbot non rétabli. Aucun script d'essai ne reste actif.

Validation finale après le délai d'exclusion : build application réussi, 2 951 tests réussis, 2 ignorés, 0 échec. L'inventaire Lua a été régénéré après l'échec de son contrôle d'empreinte, puis la suite complète a passé. Les clés de langue et les deux empreintes de la DLL tactique concordent.

Restent ouverts : adaptation réelle du tactique, observation d'une suppression AFK explicite, timings détaillés et restauration visuelle de chaque animation, regroupement avec plusieurs comptes compatibles en jeu, et validation réelle de l'exclusion après échec d'engagement.

## Reprise : sélection liée au client exact

Le mécanisme précédent conservait une demande globale « prochain client », en plus d'une demande par PID posée après lancement. Une liste d'un autre compte pouvait consommer la demande globale, et une sélection déjà consommée pouvait être réarmée après retour du lancement.

La cible est maintenant portée par LaunchRequest puis liée au PID dès sa création, avant injection du hook. La demande globale et le second enregistrement dans OrderConnectAsync sont supprimés. Une liste ne peut réclamer sa demande qu'une fois. L'envoi différé vérifie la génération de la demande ; Forget invalide cette demande et une ancienne terminaison ne retire pas celle d'un PID réutilisé. Le lancement planifié ou la reconnexion avec un personnage explicite ouvre également le choix du personnage en MITM.

Cinq cas de régression couvrent l'ordre inversé des listes de deux clients, une liste sans demande, les listes dupliquées, fermeture et réutilisation du PID, ainsi qu'un lancement sans cible. Build application réussi et suite complète : 2 956 réussis, 2 ignorés, 0 échec. 2 701 clés uniques par langue. Les passages précédemment illisibles de ce rapport ont été réencodés en UTF-8.

Essai réel de ce nouveau chemin, 25/10 à 06:53 : client MITM PID 13552, liste de personnages à 06:53:25.243, une seule sélection Rangerbot envoyée à 06:53:50.293, confirmation CharacterSelectionEvent à .397. État ingame sur 192416776, sans combat ni script. Compte déconnecté après vérification ; Foragerbot non rétabli. La concurrence de deux comptes reste vérifiée par les tests déterministes, sans prétendre à un essai simultané en jeu.
