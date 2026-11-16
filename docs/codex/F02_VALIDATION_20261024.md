# F02 — fiche et répartition des caractéristiques (terminé, une limite)

## Contrat corrigé après essais réels du 24/10

La requête remplace **toute la répartition**, exprimée en budgets investis absolus pour chaque caractéristique. Ce ne sont ni des incréments ni les valeurs de base résultantes.

- 11:12 : Intelligence base 1 + équipement 25, pool 49. Le client officiel envoie intelligence=2, total devient 27, pool 48. Cette seule trace ne permettait pas de distinguer base et budget (coût 1).
- 11:32 : deux dépenses Lua d’un point, puis 11:36 dépense UI à deux clics : confirmations serveur, Intelligence totale 27→30, pool 48→45. Cela vérifie le chemin Lua/UI sur une seule stat, pas la conservation des autres.
- 11:36:52 : essai bot Sagesse=1 selon l’ancienne interprétation. La fiche suivante remet toutes les bases à zéro, pool 50. Le retour indique un échec. Aucun point perdu, mais redistribution incorrecte : le test invalide la première interprétation.
- 11:38:40 : client officiel Sagesse +1 → `wisdom=3`, Sagesse 1, pool 47.
- 11:40:13 : client officiel Intelligence +1 → `{ intelligence=1, wisdom=3 }`, Intelligence totale 26, Sagesse 1, pool 46. Preuve de conservation de la répartition et du budget investi.

Correctif : convertir chaque base en coût cumulé, conserver les six budgets, modifier seulement la cible. Une fiche sans l’une des six bases refuse l’envoi, pour éviter une remise à zéro involontaire. La forge n’accepte plus de requête mono-stat. La confirmation exige aussi la conservation des cinq autres bases et le pool attendu exact.

## Autres corrections source

- Bases séparées des totaux/bonus selon le oneof detailed/usable ; base omise et sous-message vide = 0.
- Budget demandé transformé en gain abordable selon les paliers (Intelligence 1/2/3/4, Sagesse 3, Vitalité 1). Force/Chance/Agilité : même politique, captures propres à corroborer.
- Instantané unique bases/pool/personnage/session ; pool absent = inconnu. Invalidation au changement de personnage et à la déconnexion.
- Auto-répartition armée par le niveau, exécutée après la fiche et la fin de combat ; annulation sur arrêt explicite. Répartition manuelle prioritaire jusqu’au niveau suivant. Aucun rattrapage automatique au simple login.
- Look de liste conservé pendant changement de personnage, effacé à l’arrêt du processus.
- Manifeste mapping régénéré. Messages fr/en/es et documentation Lua précisent le budget ; retrait de l’ancienne affirmation de potion obligatoire.
- Lecteur de caractéristique de combat : ne remplace pas une valeur par 0 sur sous-message inconnu.

## Validation

Les 2635 tests et le build précédant les essais réels passaient ; ils ne détectaient pas le contrat de répartition complète. Régressions supplémentaires ajoutées pour ce contrat : Sagesse base2 = budget6, Intelligence conservée, refus d’une allocation incomplète, coût cumulé aux seuils, contenu de la trame réellement forgée. Voir dernier bilan ci-dessous.

## Essais réels sur le binaire corrigé (24/10, instance Dakal, MITM)

- 11:47:46 : script `F02-Validation-repartition-complete.lua`. Pool 46 ; Sagesse +1 (budget 3) → ok, pool 43 ; Intelligence +1 → ok, pool 42 ; Sagesse budget 2 refusée (`ok=false`), pool inchangé 42. Le client officiel affiche Sagesse et Intelligence conservées.
- 11:48–11:49 : retrait des Bottes de l’Aventurier (−5 Force/Intelligence/Chance/Agilité, pods max 2855→2805, bonus de panoplie recalculés), puis remise : pods max 2855.
- 11:49:25 : dépense de tout le pool en Intelligence : `before=42 ok=true left=0 readback=0`.
- 12:3x (reprise Claude) : build Release OK, **2641 tests réussis, 0 échec, 2 ignorés** ; clés fr/en/es sans doublon. Arrêt de l’instance puis reconnexion MITM : Intelligence 69, Sagesse 2, pool 0, bottes en position 5, PV 117/117, pods max 2855. Aucune auto-répartition à la connexion.

## Limite restante

L’auto-répartition déclenchée par une montée de niveau n’a pas été observée en réel (Rangerbot niveau 11, 2351 XP restants). Couverte par tests ; à vérifier au prochain niveau.
