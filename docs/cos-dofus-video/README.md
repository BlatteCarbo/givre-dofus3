# Kit Chat On Steroids : apprendre des videos Dofus

## Demarrage

1. Ouvre ce dossier comme espace de travail CoS, ou autorise son acces dans le projet existant.
2. Active les outils fichiers de CoS et un acces navigateur avec captures. Ajoute le MCP de transcription decrit dans `OUTILS.md`.
3. Colle le contenu de `PROMPT.md` dans une nouvelle conversation et ajoute tes liens YouTube, puis le sujet souhaite. Le prompt fonctionne aussi sans skill.
4. Optionnel : copie le dossier `skill/dofus-video-lab` dans `%APPDATA%\chat-on-steroids\skills\`, puis recharge les skills CoS. Ce format reprend celui du skill `omo` deja present localement ; son chargement reste a verifier dans CoS.
5. Recupere les rapports dans `rapports/` et donne-moi `SYNTHESE.md` avec les fiches retenues. Ces documents servent de specifications et de scenarios de test, pas de commandes executables par le bot.

Exemple de demande apres le prompt :

> Etudie ces trois liens pour les reseaux Sram terre. Cherche les conditions exactes de declenchement, l'ordre des pousses et teleportations, les obstacles et les differences selon les versions. Produis les fiches et une synthese pour le developpeur. Commence par une video pour verifier les outils, puis traite les autres.

Autres campagnes utiles : Roublard et murs de bombes ; Xelor et Telefrags ; Eliotrope et choix de sortie des portails ; Sacrieur placement puis attaque ; synergies de quatre comptes ; compagnons et challenges ; monstres, invulnerabilites et donjons. Traiter une mecanique precise a la fois, puis comparer les classes lorsqu'elle est comprise.

## Contenu

- `PROMPT.md` : mission prete a coller, autonome.
- `OUTILS.md` : outils necessaires, configuration et verification.
- `mcp.example.json` : exemple standard a adapter au client CoS.
- `MODELE_FICHE.md` : contrat de livraison des observations, combos et tests.
- `MISSIONS_COS.md` : missions independantes et prompt de coordination pour travailler en parallele du developpement.
- `VAGUE_2_HORS_YOUTUBE.md` : prompt pret a coller et dix missions sur les donnees client, replays, performances et audits, apres la livraison M01-M33.
- `VAGUE_3_PREUVES_MANQUANTES.md` : six missions ciblees sur les inconnues restantes apres la vague 2 ; prompt autonome a coller dans CoS.
- `skill/dofus-video-lab/SKILL.md` : raccourci optionnel vers cette mission.

## Etat de verification du kit

Les commandes et capacites MCP ont ete comparees a leur documentation le 4 novembre 2026. La configuration JSON est un exemple de format MCP standard, pas une modification de la configuration CoS. Aucun MCP n'est installe par ce dossier. Le chargement CoS, l'acces aux sous-titres et la lecture visuelle doivent passer le controle de `OUTILS.md` sur la machine utilisatrice.

Une transcription explique les paroles ; elle ne prouve pas les cellules, les PA depenses, les effets visuels ni la causalite d'un combo. Une video ancienne reste utile pour proposer une hypothese, pas pour certifier le client actuel.
