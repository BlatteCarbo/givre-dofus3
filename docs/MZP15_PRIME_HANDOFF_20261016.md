# MZP15 — combat avec état réel dans le moteur historique

## RESULT

Le moteur C# historique GivreClone.Engine peut maintenant plier les changements de combat d'un ingress protégé MZP1 : FightStartingEvent (fight_id), FightTurnEvent (character_id), FightEndEvent, sans inférer des codes obfusqués ou des numéros de champ. Aucun des tests OFFLINE n'est une preuve de combat réellement joué sur Dofus 3.7.11.11 et la parité exacte avec GivreClone v1.3.1 n'est toujours pas attestée.

## CHANGES

- GivreClone.Engine/MZP15VerifiedFightReader.cs : projection stricte inbound uniquement, nom/code résolu par ProtocolCatalog, champs wire confirmés uniquement par fields.json. Rejet sorties client, mauvais tags, ID nul/overflow, doublons et absence de schéma.
- GivreClone.Engine/GameState.cs : FightId optionnel, indicateurs InFight/IsMyTurn/FightKnown, ne dit « mon tour » que si CharacterId a été admis par le lecteur MZP14 et si un début de combat a été reçu. Fin et révocation réinitialisent les états de combat, un nouveau personnage invalide également les vieux états.
- tools/MZP15FightHarness/ : 20 tests offline des transitions et rejets, référence directe aux vraies bibliothèques Engine/Core/Protocol.
- Harness MZP1/MZP2 source-linked raccordés au lecteur de production MZP15.

## VALIDATION

MZP15FightHarness : **20 PASS** hors ligne. MZP14CharacterHarness : **17 PASS**. MZP13StateHarness : **19 PASS**. MZP1GuardedWiringHarness : **30 PASS** et MZP2HostAdmissionHarness : **41 PASS**. Les builds historiques Debug/Release sont consignés par le runner.

## BLOCKERS

Le code GivreClone v1.3.1 exact demeure indisponible sous forme de source ou binaire détaché vérifié. Les lecteurs sont de la reconstruction C#, non de la décompilation. Aucun flux de jeu authentifié, serveur ACK, personnage, carte, combat ou résultat de HDV/inventaire n'a été observé. La chaîne Launcher 2 / connexion / MITM / gameplay H24 et l'ensemble des écrans restent encore partiellement fonctionnels. Les trois bots Givre originaux n'ont pas été touchés.
