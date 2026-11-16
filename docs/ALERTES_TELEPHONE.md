# Alertes téléphone

Événements levés par Givre (`PhoneAlertHub`) : 💀 mort · 🎒 pods pleins (≥ 98 %) · 🔌 déconnexion · ⚔️ combat perdu · ⛔ bot figé (watchdog anti-blocage) · 👑 **archimonstre / monstre à âme capturable** apparu sur la carte d'un bot ·
📅 fin d'abonnement (7 / 3 / 1 jours et expiration).

## Où elles arrivent
- **Page du téléphone** (onglet « Alertes », long-poll permanent) : toast, vibration, pastille `(n)` dans le titre, notification du navigateur si autorisée (impossible sur iOS en http : pas de Web Push sans serveur de push).
- **Discord** : chaque alerte est aussi postée sur le **webhook du perso** (Paramètres du bot) → l'application Discord du téléphone envoie la notification **même téléphone verrouillé / app fermée**. C'est le canal fiable.
- **MCP** : `get_events(since, waitSec)`.

## Règles
- Anti-répétition : un même événement (type + bot) n'est pas répété avant 5 minutes ; archimonstre : une fois par groupe et par carte, puis pas avant 15 min pour le même monstre sur la même carte.
- Archimonstre : liste d'archimonstres du client + monstres à âme capturable des données du jeu ; relevé toutes les 4 s, hors combat.

## À vérifier en jeu
1. Que l'alerte archimonstre se déclenche à l'arrivée sur une carte qui en contient un (la détection dépend des groupes lus par le bot sur la carte).
2. Le seuil « pods pleins » (98 %) et la mort (dépend de `FighterKilled` pour mon perso).
3. L'envoi Discord (permission « relais de messages » + webhook renseigné).
