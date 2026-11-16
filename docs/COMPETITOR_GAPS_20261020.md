# GivreWork vs MoonBot / AnkaBot / DoFarm — 20 octobre 2026

Relevé des fonctions **annoncées** publiquement par les éditeurs, recoupé avec
les modules et tests présents dans cette branche. Ni les affirmations commerciales
ni nos tests hors ligne ne constituent une mesure comparative en jeu.

| Domaine | GivreWork (preuves source) | Référence externe | Écart / priorité |
|---|---|---|---|
| Contrôle web/mobile cloud | Application Avalonia locale, MCP, relais, dashboard | MoonBot annonce navigateur/mobile et sessions hébergées; DoFarm panel cloud | Panel distant sécurisé avec rôles/observabilité (P1); **ne pas** exposer les secrets |
| Scripting | MoonSharp Lua, éditeur, lint, scripts enregistrés ; **catalogue local de manifestes/SHA-256 en lecture seule ajouté à la branche** | MoonBot annonce 395 fonctions/20 namespaces, scripts partagés; DoFarm vend infrastructure de licences de scripts tiers | Revue des capacités ajoutée, mais pas encore de grants, exécution depuis catalogue ni marketplace (P1) |
| Automation sans code | Modes zone/donjon/HDV, trajets générés ; **assistant visuel trajet/récolte/combat ajouté dans l'onglet Lua**, aperçu et confirmation d'import | AnkaBot propose configuration d'IA simple depuis son interface, mais son guide général prévoit aussi des scripts pour les trajets et actions | Étendre les assistants aux métiers, équipements, HDV, donjons/quête avec données et transitions prouvées (P1) |
| Quêtes, chasse, Kolizéum | Quêtes observées, Road Creator, nouveau tracker de jalons validés **hors jeu** ; pas de circuit complet certifié | AnkaBot annonce chasses et Kolizéum solo/groupe ; dofus-miniatures r78 propose panneau manuel de chasse et ressources carte | Brancher tracker et UI sur un client TEST, chasses/évolution ensuite (P1/P2) ; **aucun paquet deviné** |
| Combat/récolte/craft | Moteur, récolte, profils de combat, CraftPlanner et UI ateliers déjà présents | MoonBot, AnkaBot, DoFarm annoncent ces familles | Priorité aux tests de transaction et de reprise plutôt qu'aux doublons |
| HDV/marges | Achat/vente, repricing avec garde corrélé, statistiques, prix historiques | MoonBot/DoFarm proposent HDV et contrôle prix | Historique prouvant source/fraîcheur, coût net/risque inconnu visibles (P1) |
| Montures/mode marchand/anomalies | États Lua explicitement indisponibles pour montures/marchand; liste anomalie visible, téléport non vérifiée | Certains concurrents annoncent large couverture de verbes | Rester fail-closed jusqu'à capture autorisée et ACK attribuable (P0 vérité produit) |
| Exploitation | Sessions multi-bots, reconnexion, supervision, MCP; tests locaux | MoonBot annonce cloud 24 h et commandes manuelles | Journal causal par session, plan de reprise, supervision à distance (P1) |

Sources éditeurs consultées :
- https://moon-bot.io/
- https://moon-bot.io/a-propos/
- https://www.ankamabot.com/
- https://dofarm.io/
- https://dofarm.io/offres.php
- https://doc.dofarm.io/doc-faq.html
- https://github.com/Altansar69/dofus-miniatures (révision r78, cible 3.7.12.12 ; compatibilité non garantie)

## Arbitrages

1. **P0 sûreté fonctionnelle** : identité (PID/personnage/serveur), instantanés
   inventaire/banque, ACK corrélés, état transaction UNKNOWN durable, aucune
   action s'appuyant sur une liste de zaap/anomalie périmée.
2. **P1 ergonomie** : dashboard et interactions accessibles, libellé clair sur
   valeurs non reçues, scripts validables hors jeu et journal d'erreur navigable.
3. **P1 différenciation** : paramétrage métier sans Lua obligatoire, rapports
   économiques avec dates/prix observés.
4. **P2 nouveaux domaines** : quêtes complètes, chasse au trésor et Kolizéum
   uniquement après preuve des contrats réseau et de leurs transitions serveur.

## Position actuelle

Le master de GitHub ne contient pas les changements de la PR brouillon #1.
La branche d'intégration améliore les garanties hors jeu, sans déclarer
certifiés les mouvements, téléportations, achats ou combats réels.
