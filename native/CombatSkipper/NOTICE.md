Fork de `mods/combat-animation-skipper` du depot AlpaGit/dofus3-native-host (licence MIT, copyright Nexytrus contributors / Bubble).
Modification Givre : `FightEntitiesService` n'existe plus dans le client 3.7.11.11 ; la detection du combat passe par `InputChecker` seul.
Version locale 1.0.7 : reprise de la temporalite hors combat si le service optionnel devient illisible, transitions journalisees,
et refus des multiples adresses candidates d'une meme signature (hooks non ambigus seulement).
Version locale 1.0.8 : liberation des pointeurs Animator2D en fin de combat et cache borne a 8192 entrees ;
les sessions longues ne conservent plus une liste croissante d'adresses Unity potentiellement recyclees.
