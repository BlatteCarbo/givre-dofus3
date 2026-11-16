# N10 — Chronologie Turbo hors jeu : traces disponibles et trous de preuve

**4 novembre 2026 — extraction d'une séquence historique réellement effectuée, aucune modification du jeu ou de ses processus.** L'outil [extract_turbo.py](extract_turbo.py) lit les résumés déjà présents dans le dossier privé du clone et n'exporte que les **types, temps relatifs et mesures numériques** des lignes `[turbo-metric]`, sans texte original, PID, nom, compte ou chemin complet.

## Une chronologie de combat datée du 04/11/2026

Extraction réussie avec code de sortie **0** : `TURBO_TIMELINE_OK events=80 categories=ack_tx,barrier_closed,companion_turn,fight_ready,player_turn,request_tx,sequence_end_self`. La [chronologie machine-lisible](turbo_chronologie_anonymisee.json) conserve l'empreinte SHA-256 du log source privé et la ligne de chaque observation. Le passage visible couvre **t = 0,285 à 16,606 s** ; il ne faut pas lire cet intervalle comme la durée totale du combat.

| Événement journalisé | Nombre d'occurrences |
| --- | ---: |
| `fight_ready` | 1 |
| `sequence_end_self` | 23 |
| `ack_tx` | 23 |
| `request_tx` | 15 |
| `barrier_closed` | 15 |
| `player_turn` | 2 |
| `companion_turn` | 1 |

Les 23 ACK ne sont **pas** 23 demandes forcément distinctes : les ACK d'actions et les fins de séquence peuvent concerner des actions de différents acteurs. Pour établir une causalité par commande il faudrait un identifiant de séquence/époque exploitable et corréler explicitement chaque `request_tx`, `sequence_end_self`, `ack_tx` et `barrier_closed`.

**Chaîne demandée par la mission** : l'entrée/ready, les tours du joueur/compagnon et les barrières de Turbo sont repérés ; **le premier tour ennemi**, **le bail natif**, **le multiplicateur de timeScale réellement appliqué** et **le reçu de retour en mode hors combat** ne figurent **pas tous** dans la chronologie de journal retenue. Ces états sont marqués `unproven` dans le JSON, pas interpolés. Le code `NativeTurboRuntime` publie un bail, mais sa présence dans une méthode ne prouve pas son succès pour le combat observé.

## Diagnostics d'étapes, sans extrapolation

- `request_to_sequence_end_ms` mesure jusqu'à la fin de séquence ; `sequence_end_to_ack_ms` est post-séquence. Il faut conserver cette décomposition avant d'accuser le moteur de l'animation ou du réseau.
- `request_to_ack_ms` mesure un délai de barrière ; sa fermeture est différente du fait d'avoir envoyé la demande.
- Les intitulés `barrier_closed` et `ack_tx` sont des **logs d'instrumentation de l'application**, pas des certificats indépendants des événements serveur. Les occurrences peuvent être dédoublées.
- On ne déduit **aucun multiplicateur x2/x8 effectif** et aucune performance comparée MITM vs Socket sans trace du module natif, de l'horloge et d'un corpus comparable.

**Niveau de réalisation** : chronologie partielle mais reproductible et anonymisée, instrumentation manquante exacte identifiée. Une preuve de bail/timeScale/retour carte demeure ouverte ; aucune activation, désactivation ou accélération Turbo n'a été faite par cette mission.
