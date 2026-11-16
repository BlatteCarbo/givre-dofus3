# Minerbot : combat protecteur du26oct08:52UTC

Journal20261026-095218-1073741833, carte4460544. Defaite serveur confirmee apres248,6s,83tours,306lancers,84deplacements. Script mineur arrete automatiquement par onFightEnd won=false ; NE PAS RELANCER sans correction/diagnostic. Etat apres combat : carte192416776,347/2419PV,6252/9927pods,998370kamas. Aucun abandon ni reconnexion forcee pendant combat.

Progression initiale observee : ennemi3400PV au tour1,2858T2,2356T3,1765T4,996T5,480T6. AuT7 ennemis[] alors que les deplacements et ciblages de -1 continuent. Personnage685T7,367T8,55T9 puis0T10. Pourtant les tours continuent jusqua83 avec PA/PM et maxPV qui diminuent. Ne pas conclure que personnage reellement mort auT10 : suivi PV/calcul local incoherent.

Sort13528 repete deux fois sur soi [buff] a partir duT7 alors quil etait lance sur ennemi aux premiers tours. Sort13525 parfois sur soi,13531 attaque de zone surestimee1569sans resistances. Profil Aggressive/Closest/Fast,AutoFight/AutoSpells actifs,defis desactives,TurboAckAll/EagerReadyfalse. AutoFight garde position ennemie via autre cache alors que Fighters alive/lifePoints vide : hypotheses vie derivee trop basse ou suppression prematuree, repli buff selectionnant sort offensif. Reconstituer evenements serveur pour confirmer avant modifier moteur.

Ce combat confirme boucle et defaite, pas cause exacte. Extrait local limite du journal dans artifacts/minerbot-combat-20261026-excerpt.txt ; ne pas publier de logs complets.

## Correctif de reprise

Défaut confirmé dans le code : les `LifePointsLost` étaient appliqués, mais aucun lecteur ni branchement de `GameActionFightEvent.life_points_gain` n'existait. Soins et vols de vie des joueurs comme des monstres étaient donc ignorés. Ajout du lecteur catalogué `LifePointsGain.target_id/delta`, application au même cache de combattants, plafond érodé conservé, notification UI du joueur. Aucun décès déduit d'un soin ni changement du cache de positions. Ceci corrige une dérive certaine, sans prouver que tous les événements du combat passé sont expliqués : les journaux disponibles ne contiennent pas les valeurs protobuf de chaque soin.

Build Release application réussi ; 8 tests survie puis 24 tests survie/contrat de mapping réussis. La première suite complète a donné 3012 réussis, 2 ignorés et un échec d'ordre du manifeste ; ordre corrigé et contrat repassé. Correctif encore absent de l'application active HdVOffers et non validé en combat réel. Pas de relance du script mineur d'Minerbot.

Vérification finale après correction du manifeste : build application Release réussi, zéro avertissement/erreur incrémental ; suite complète **3013 réussis, 2 ignorés, 0 échec**. FR/EN/ES sans doublon de clé, diff check propre.

Publication séparée prête : `%USERPROFILE%/Desktop/GivreClone-CombatLife-20261026/GivreClone.exe`. Application active inchangée ; ne pas confondre publication sur disque et correctif chargé. Aucun redémarrage des autres comptes pendant les achats d'Foragerbot.
