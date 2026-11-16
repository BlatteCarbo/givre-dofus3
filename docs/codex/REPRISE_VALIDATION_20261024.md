# Reprise du 24 octobre, 23 h

Intégration en avance rapide de `claude-port-ancien-givre-20261024`, jusqu'à `7c220d1`, sur la branche de correction. Changements locaux préexistants conservés et validés : arrêt Expédition sur défaite, passage de donjon compté seulement après une nouvelle victoire du boss, dépôt bancaire confirmé et barrière de départ des équipes sur les trajets et téléportations.

## Correctifs de reprise

- Lancer enregistré avant l'injection : une confirmation de modificateur peut arriver pendant l'envoi réseau. Si l'injection échoue, l'enregistrement est retiré.
- Rotation rafraîchie contre sa dernière version connue, même si l'événement de modification arrive avant l'entrée dans l'attente du lancer.
- Inventaire Lua : expansion des tuples mixtes chaîne/nombre/chaîne des coûts de caractéristiques. 977 liaisons, zéro signature documentée absente. Tests et documentation alignés sur les coûts réels et le statut actuel du contexte Lua des invocations.
- Test concurrent des modificateurs borné et asynchrone ; test PacketBus d'annulation exécuté sur un thread dédié pour éviter de tester la saturation du pool au lieu de la course d'enregistrement.

## Vérifications

Build application Release réussi : zéro erreur, 208 avertissements. Suite finale : 2 935 réussis, 2 ignorés, zéro échec. Un passage précédent a échoué sur l'attente du worker d'annulation PacketBus ; correction décrite ci-dessus puis suite complète réussie. Clés fr/en/es sans doublons. `git diff --check` propre.

## Essai serveur borné

Instance isolée `port-validation-20261024`, canal `GivreWork-Port-20261024`, un seul compte copié, aucun planning et autostart désactivé. L'autre compte actif dans l'instance d'origine n'a pas été interrompu. Foragerbot déconnecté seul pour connecter Rangerbot dans l'instance d'essai. Profils modifiés uniquement dans la copie.

Socket, Rangerbot niveau 14, Turbo, mode automatique, défis désactivés dans la copie. Trajet serveur confirmé jusqu'à la zone, puis Expédition Champs d'Astrub limitée à un monstre, un combat et trois minutes.

Combat du 24/10 à 23:10:57–23:11:03, contre une Rose Démoniaque :

- À 23:10:57.760, application des modificateurs du sort 32465 après confirmation serveur.
- À 23:11:01.045, nouveau buff 32465 confirmé au tour 4.
- À 23:11:01.136, Flèche de Barrage 32443 lancée à distance 4 après déplacement confirmé vers 340 ; à 23:11:01.161, poussée adverse 286→272 suivie. Le lancer a donc un effet serveur observé.
- Victoire `issue=2`, 10 747 XP, niveau 15 observé ensuite. Expédition arrêtée après un combat, `scriptRunning=false` confirmé. Aucun message de sort sans réponse observé dans ce combat.

Cela confirme le chemin Socket du buff de portée et l'arrêt sur limite. Cela ne valide ni toutes les classes/modifications de sorts, ni les dissipations, ni le classement optimal de l'IA (aucun réordonnancement observé ici), ni le MITM, ni les effets des modules natifs. Les autres lots restent suivis dans TODO_FEATURES_20261024.md.

Les journaux complets et les fichiers de comptes restent locaux. Aucun secret n'est ajouté au dépôt.
