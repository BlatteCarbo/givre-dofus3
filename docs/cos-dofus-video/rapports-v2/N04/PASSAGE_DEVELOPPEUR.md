# N04 — Contrat de replay à brancher ensuite

## Utilisation déjà possible sans compte

L'export [replays_anonymises.json](replays_anonymises.json) décrit deux chronologies d'ancien journal avec `seq`, `t_sec`, `actor` aliasé, `kind`, cellules vues et positions connues. Il peut alimenter un outil de **comparaison structurelle hors ligne** : identité stable d'acteur pendant le combat, ordre non décroissant, non-projection d'une commande/refus comme occupation certifiée, changement de tour vers un compagnon.

Ce n'est **pas** encore un replay complet `GameActionFightEvent` : ne pas le charger aveuglément comme séquence de messages serveur. Les événements `movement_refusal_reported` sont des lignes de journal et non des paquets signés ; l'absence d'un `position_change_seen` après un refus n'est pas une preuve de position inchangée si l'événement est manquant.

## Pour passer au niveau de preuve supérieur

1. Depuis les seules **captures historiques** déjà disponibles, produire un export de messages serveur décodés anonymisé (schema/type de message, ordre, heure relative, identité de combattant remappée) ; conserver le hash de la capture et l'offset des paquets.
2. Ajouter les états initial/final autoritaires de chaque personnage et compagnon, ainsi que l'ID source d'une invocation ou des marks lorsqu'ils sont explicitement reçus.
3. Construire deux oracles : refus confirmé **n'entraîne pas un déplacement certifié** ; changement de tour au compagnon **change le caster autorisé** sans créditer sa ressource au propriétaire. Conserver `Unknown` si l'identité ou l'issue n'est pas décodée.
4. Rejouer avec un harness de protocole **dans une mission distincte** et comparer les réducteurs, plutôt que d'attribuer `valide-test` au simple succès du validateur JSON.

**Zéro code ou Git de production modifié, aucun compte ou session utilisé.** Le script d'export N04 n'a que des entrées fichiers historiques et une sortie JSON anonymisée dans son propre dossier.
